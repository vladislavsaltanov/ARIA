#include "interp.h"
#include "pcm_pool.h"
#include "plan.h"
#include "renderer.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <vector>

namespace {

using aria::ChunkKey;
using aria::cubic4;
using aria::kChunkFrames;
using aria::PcmPool;
using aria::Renderer;
using aria::RenderPlan;
using aria::VoicePlan;

constexpr double kRate = 48000.0;
constexpr double kUsPerFrame = 1e6 / kRate;
constexpr int64_t kHostUs = 13'000'000'000'000;
constexpr int64_t kRampHi = 0x0123456789ABCDEF;
constexpr int64_t kRampLo = 0x0FEDCBA987654321;
constexpr int64_t kOtherHi = 7;
constexpr int64_t kOtherLo = 9;
constexpr int64_t kAnchorFrame = 96000;
constexpr int32_t kFade = 480;
constexpr double kDeadZone = 1e-3;
constexpr double kMaxDeviation = 5e-4;
constexpr int kMaxVoices = 8;
constexpr float kGarbage = 7.0F;
constexpr double kTwoPi = 6.283185307179586;
constexpr double kSineHz = 440.0;

int16_t rampSample(int64_t frame) { return static_cast<int16_t>(1 + frame % 32767); }

int16_t halfScale(int64_t) { return 16384; }

double sine(double frame) { return 0.5 * std::sin(kTwoPi * kSineHz * frame / kRate + 1.0); }

int16_t sineSample(int64_t frame) { return static_cast<int16_t>(std::lround(sine(static_cast<double>(frame)) * 32768.0)); }

float ramp(int64_t frame) { return static_cast<float>(rampSample(frame)) / 32768.0F; }

float faded(float value, int64_t framesIntoFade) {
    return framesIntoFade < kFade ? value * (static_cast<float>(framesIntoFade) / static_cast<float>(kFade)) : value;
}

VoicePlan voice(int32_t id, int64_t anchorUs = kHostUs, int64_t anchorFrame = kAnchorFrame) {
    VoicePlan v;
    v.id = id;
    v.epoch = 1;
    v.trackHi = kRampHi;
    v.trackLo = kRampLo;
    v.anchorUs = anchorUs;
    v.anchorFrame = anchorFrame;
    v.playing = true;
    return v;
}

VoicePlan onOtherTrack(VoicePlan v) {
    v.trackHi = kOtherHi;
    v.trackLo = kOtherLo;
    return v;
}

RenderPlan plan(std::vector<VoicePlan> voices, int64_t hostUsAtFrame0 = kHostUs) {
    RenderPlan p;
    p.rate = kRate;
    p.voices = std::move(voices);
    p.timeMap = aria::TimeMap{0, hostUsAtFrame0, kUsPerFrame};
    return p;
}

std::vector<VoicePlan> tooManyVoices() {
    std::vector<VoicePlan> voices;
    for (int32_t id = 1; id <= kMaxVoices + 2; ++id) {
        voices.push_back(id > kMaxVoices ? onOtherTrack(voice(id)) : voice(id));
    }
    return voices;
}

void expectFrame(const std::vector<float>& out, int32_t i, float left) {
    EXPECT_FLOAT_EQ(left, out[2 * i]) << "frame " << i;
    EXPECT_FLOAT_EQ(-left, out[2 * i + 1]) << "frame " << i;
}

bool allZero(const std::vector<float>& out) {
    return std::all_of(out.begin(), out.end(), [](float sample) { return sample == 0.0F; });
}

class RendererVoiceTest : public ::testing::Test {
protected:
    void publish(int64_t hi, int64_t lo, int32_t chunk, int32_t frames, int16_t (*sampleOf)(int64_t)) {
        const int slot = pool.claim(ChunkKey{hi, lo, chunk});
        ASSERT_GE(slot, 0);
        int16_t* data = pool.slotData(slot);
        const int64_t first = static_cast<int64_t>(chunk) * kChunkFrames;
        for (int32_t i = 0; i < frames; ++i) {
            const int16_t left = sampleOf(first + i);
            data[2 * i] = left;
            data[2 * i + 1] = static_cast<int16_t>(-left);
        }
        pool.publish(slot, frames);
    }

    void publishRamp(int32_t chunk, int32_t frames = kChunkFrames) { publish(kRampHi, kRampLo, chunk, frames, rampSample); }

    std::vector<float> render(const RenderPlan* p, int64_t deviceFrame, int32_t frames) {
        std::vector<float> out(2 * static_cast<size_t>(frames), kGarbage);
        renderer.render(p, deviceFrame, frames, out.data());
        return out;
    }

    PcmPool pool;
    Renderer renderer{pool};
};

TEST_F(RendererVoiceTest, rendersSourceFramesAtTimeMap) {
    publishRamp(1);
    const RenderPlan p = plan({voice(1)});
    for (int64_t block = 0; block < 4; ++block) {
        const std::vector<float> out = render(&p, block * 256, 256);
        for (int32_t i = 0; i < 256; ++i) expectFrame(out, i, faded(ramp(kAnchorFrame + block * 256 + i), block * 256 + i));
    }
    EXPECT_EQ(1.0, renderer.probe(1)->ratio);
}

TEST_F(RendererVoiceTest, silentUntilAnchorThenFadesIn10ms) {
    publishRamp(0);
    publishRamp(1);
    constexpr int32_t kAnchorOffset = 48;
    const RenderPlan p = plan({voice(1, kHostUs + 1000)});
    const std::vector<float> out = render(&p, 0, 1024);
    for (int32_t i = 0; i <= kAnchorOffset; ++i) expectFrame(out, i, 0.0F);
    for (int32_t j = 0; kAnchorOffset + j < 1024; ++j) expectFrame(out, kAnchorOffset + j, faded(ramp(kAnchorFrame + j), j));
    EXPECT_EQ(ramp(kAnchorFrame + kFade), out[2 * (kAnchorOffset + kFade)]);
    EXPECT_EQ(0U, renderer.underruns());
}

TEST_F(RendererVoiceTest, trimMovesContentEarlier) {
    publishRamp(1);
    RenderPlan p = plan({voice(1)});
    p.trimUs = 2000;
    const std::vector<float> out = render(&p, 0, 1024);
    for (int32_t i = 0; i < 1024; ++i) expectFrame(out, i, faded(ramp(kAnchorFrame + 96 + i), i));
}

TEST_F(RendererVoiceTest, driftOf3msConvergesBelow0_1msWithRatioWithin5e_4) {
    for (int32_t chunk = 1; chunk <= 6; ++chunk) publishRamp(chunk);
    constexpr int32_t kBlock = 192;
    constexpr int64_t kBlocks = 10 * 48000 / kBlock;
    const RenderPlan synced = plan({voice(1)});
    const RenderPlan late = plan({voice(1)}, kHostUs + 3000);
    render(&synced, 0, kBlock);
    double position = renderer.probe(1)->position;
    for (int64_t block = 1; block <= kBlocks; ++block) {
        render(&late, block * kBlock, kBlock);
        const aria::VoiceProbe probe = *renderer.probe(1);
        ASSERT_LE(std::fabs(probe.ratio - 1.0), kMaxDeviation) << "block " << block;
        ASSERT_NEAR(position + kBlock * probe.ratio, probe.position, 1e-6) << "block " << block;
        if (block == 1) EXPECT_DOUBLE_EQ(1.0 + kMaxDeviation, probe.ratio);
        position = probe.position;
    }
    const double target = kAnchorFrame + (3000.0 + static_cast<double>((kBlocks + 1) * kBlock) * kUsPerFrame) * kRate / 1e6;
    EXPECT_LT(std::fabs(target - position), 0.0001 * kRate);
}

TEST_F(RendererVoiceTest, errorOver5msReanchorsWithFadeIn) {
    publishRamp(1);
    const RenderPlan synced = plan({voice(1)});
    const RenderPlan late = plan({voice(1)}, kHostUs + 5500);
    render(&synced, 0, 1024);
    render(&synced, 1024, 1024);
    const std::vector<float> out = render(&late, 2048, 1024);
    const int64_t target = kAnchorFrame + 2048 + 264;
    for (int32_t i = 0; i < 1024; ++i) expectFrame(out, i, faded(ramp(target + i), i));
    EXPECT_EQ(1.0, renderer.probe(1)->ratio);
    EXPECT_NEAR(static_cast<double>(target + 1024), renderer.probe(1)->position, kDeadZone);
}

TEST_F(RendererVoiceTest, errorUnder5msResamplesWithoutReanchor) {
    publishRamp(1);
    const RenderPlan synced = plan({voice(1)});
    const RenderPlan late = plan({voice(1)}, kHostUs + 4500);
    render(&synced, 0, 1024);
    render(&synced, 1024, 1024);
    const std::vector<float> out = render(&late, 2048, 1024);
    EXPECT_FLOAT_EQ(ramp(kAnchorFrame + 2048), out[0]);
    EXPECT_DOUBLE_EQ(1.0 + kMaxDeviation, renderer.probe(1)->ratio);
    EXPECT_NEAR(kAnchorFrame + 2048 + 1024 * (1.0 + kMaxDeviation), renderer.probe(1)->position, 1e-6);
}

TEST_F(RendererVoiceTest, errorInsideDeadZoneKeepsExactRatio) {
    publishRamp(1);
    const auto withError = [](int64_t deviceFrame, double position, double error) {
        RenderPlan p = plan({voice(1)});
        p.timeMap.usPerFrame = (position + error - kAnchorFrame) * 1e6 / (static_cast<double>(deviceFrame) * kRate);
        return p;
    };
    const RenderPlan synced = plan({voice(1)});
    render(&synced, 0, 256);
    const RenderPlan inside = withError(256, kAnchorFrame + 256, 0.5 * kDeadZone);
    render(&inside, 256, 256);
    EXPECT_EQ(1.0, renderer.probe(1)->ratio);
    const RenderPlan outside = withError(512, kAnchorFrame + 512, 3.0 * kDeadZone);
    render(&outside, 512, 256);
    EXPECT_NEAR(1.0 + 3.0 * kDeadZone / kRate / (2.0 * 0.1), renderer.probe(1)->ratio, 2e-8);
}

TEST_F(RendererVoiceTest, exactRatioCopiesWithoutInterpolation) {
    publishRamp(1);
    publishRamp(2);
    constexpr int64_t kAnchor = 2 * kChunkFrames - 1500;
    const RenderPlan p = plan({voice(1, kHostUs - 15, kAnchor)});
    render(&p, 0, 1024);
    const std::vector<float> out = render(&p, 1024, 1024);
    for (int32_t i = 0; i < 1024; ++i) {
        EXPECT_EQ(ramp(kAnchor + 1 + 1024 + i), out[2 * i]) << "frame " << i;
        EXPECT_EQ(-ramp(kAnchor + 1 + 1024 + i), out[2 * i + 1]) << "frame " << i;
    }
    EXPECT_EQ(1.0, renderer.probe(1)->ratio);
}

TEST(RendererInterpolationTest, cubicErrorBelow1e_4OnSine440At1_0004) {
    constexpr int kSamples = 48000;
    std::vector<float> wave(kSamples);
    for (int n = 0; n < kSamples; ++n) wave[n] = static_cast<float>(std::sin(kTwoPi * kSineHz * n / kRate));
    double worstCubic = 0.0;
    double worstLinear = 0.0;
    for (int k = 0; 1.0 + k * 1.0004 < kSamples - 3; ++k) {
        const double x = 1.0 + k * 1.0004;
        const int base = static_cast<int>(std::floor(x));
        const double frac = x - base;
        const double exact = std::sin(kTwoPi * kSineHz * x / kRate);
        worstCubic = std::max(worstCubic, std::fabs(cubic4(&wave[base - 1], frac) - exact));
        worstLinear = std::max(worstLinear, std::fabs(wave[base] + frac * (wave[base + 1] - wave[base]) - exact));
    }
    EXPECT_LT(worstCubic, 1e-4);
    EXPECT_GT(worstLinear, 1e-4);
}

TEST_F(RendererVoiceTest, resampledReadAcrossChunkBoundaryStaysOnSine) {
    publish(kRampHi, kRampLo, 1, kChunkFrames, sineSample);
    publish(kRampHi, kRampLo, 2, kChunkFrames, sineSample);
    constexpr int64_t kAnchor = 2 * kChunkFrames - 4800;
    const RenderPlan synced = plan({voice(1, kHostUs, kAnchor)});
    const RenderPlan late = plan({voice(1, kHostUs, kAnchor)}, kHostUs + 3000);
    render(&synced, 0, 256);
    double start = renderer.probe(1)->position;
    double worst = 0.0;
    bool crossed = false;
    for (int64_t block = 1; block < 40; ++block) {
        const std::vector<float> out = render(&late, block * 256, 256);
        const double ratio = renderer.probe(1)->ratio;
        ASSERT_NE(1.0, ratio);
        for (int32_t i = 0; block >= 2 && i < 256; ++i) {
            const double frame = start + i * ratio;
            crossed = crossed || frame >= 2.0 * kChunkFrames;
            worst = std::max({worst, std::fabs(out[2 * i] - sine(frame)), std::fabs(out[2 * i + 1] + sine(frame))});
        }
        start = renderer.probe(1)->position;
    }
    EXPECT_TRUE(crossed);
    EXPECT_LT(worst, 1e-4);
}

TEST_F(RendererVoiceTest, missingChunkSilentCountsUnderrunThenFadesInOnArrival) {
    publishRamp(1);
    constexpr int64_t kAnchor = 2 * kChunkFrames - 1024;
    const RenderPlan p = plan({voice(1, kHostUs, kAnchor)});
    render(&p, 0, 1024);
    EXPECT_EQ(0U, renderer.underruns());
    for (int64_t block = 1; block < 3; ++block) {
        EXPECT_TRUE(allZero(render(&p, block * 1024, 1024)));
        EXPECT_EQ(static_cast<uint64_t>(block), renderer.underruns());
    }
    publishRamp(2);
    const std::vector<float> out = render(&p, 3072, 1024);
    for (int32_t i = 0; i < 1024; ++i) expectFrame(out, i, faded(ramp(kAnchor + 3072 + i), i));
    EXPECT_EQ(2U, renderer.underruns());
}

TEST_F(RendererVoiceTest, duplicateVoiceIdRendersOnce) {
    publishRamp(1);
    const RenderPlan p = plan({voice(1), voice(1)});
    render(&p, 0, 1024);
    const std::vector<float> out = render(&p, 1024, 1024);
    for (int32_t i = 0; i < 1024; ++i) expectFrame(out, i, ramp(kAnchorFrame + 1024 + i));
    EXPECT_DOUBLE_EQ(static_cast<double>(kAnchorFrame + 2048), renderer.probe(1)->position);
}

TEST_F(RendererVoiceTest, emptyChunkSilentNoUnderrun) {
    const int slot = pool.claim(ChunkKey{kRampHi, kRampLo, 1});
    ASSERT_GE(slot, 0);
    pool.publishEmpty(slot);
    const RenderPlan p = plan({voice(1)});
    for (int64_t block = 0; block < 2; ++block) EXPECT_TRUE(allZero(render(&p, block * 256, 256)));
    EXPECT_EQ(0U, renderer.underruns());
}

TEST_F(RendererVoiceTest, shortChunkEndsCleanly) {
    publishRamp(1, 1000);
    publish(kOtherHi, kOtherLo, 1, 0, rampSample);
    const RenderPlan p = plan({voice(1), onOtherTrack(voice(2))});
    for (int64_t block = 0; block < 100; ++block) {
        const std::vector<float> out = render(&p, block * 1024, 1024);
        for (int32_t i = 0; i < 1024; ++i) {
            const int64_t frame = block * 1024 + i;
            ASSERT_TRUE(std::isfinite(out[2 * i]) && std::isfinite(out[2 * i + 1]));
            expectFrame(out, i, frame < 1000 ? faded(ramp(kAnchorFrame + frame), frame) : 0.0F);
        }
    }
    EXPECT_EQ(0U, renderer.underruns());
}

TEST_F(RendererVoiceTest, endFrameIsHardStop) {
    publishRamp(1);
    VoicePlan v = voice(1);
    v.endFrame = kAnchorFrame + 600;
    const RenderPlan p = plan({v});
    const std::vector<float> out = render(&p, 0, 1024);
    for (int32_t i = 0; i < 1024; ++i) expectFrame(out, i, i < 600 ? faded(ramp(kAnchorFrame + i), i) : 0.0F);
    EXPECT_NE(0.0F, out[2 * 599]);
}

TEST_F(RendererVoiceTest, anchorFrameBeyondInt32Plays) {
    constexpr int64_t kFar = 3'000'000'000;
    publish(kRampHi, kRampLo, static_cast<int32_t>(kFar / kChunkFrames), kChunkFrames, rampSample);
    const RenderPlan p = plan({voice(1, kHostUs, kFar)});
    for (int64_t block = 0; block < 2; ++block) {
        const std::vector<float> out = render(&p, block * 512, 512);
        for (int32_t i = 0; i < 512; ++i) expectFrame(out, i, faded(ramp(kFar + block * 512 + i), block * 512 + i));
    }
    EXPECT_DOUBLE_EQ(static_cast<double>(kFar + 1024), renderer.probe(1)->position);
}

TEST_F(RendererVoiceTest, voiceWithEpochChangeReanchors) {
    publishRamp(1);
    VoicePlan next = voice(1);
    next.epoch = 2;
    const RenderPlan first = plan({voice(1)});
    const RenderPlan second = plan({next});
    render(&first, 0, 1024);
    render(&first, 1024, 1024);
    const std::vector<float> out = render(&second, 2048, 1024);
    for (int32_t i = 0; i < 1024; ++i) expectFrame(out, i, faded(ramp(kAnchorFrame + 2048 + i), i));
}

TEST_F(RendererVoiceTest, stoppedVoiceIsSilentAndReentersWithFadeIn) {
    publishRamp(1);
    VoicePlan stopped = voice(1);
    stopped.playing = false;
    const RenderPlan playing = plan({voice(1)});
    const RenderPlan paused = plan({stopped});
    render(&playing, 0, 1024);
    EXPECT_TRUE(allZero(render(&paused, 1024, 1024)));
    const std::vector<float> out = render(&playing, 2048, 1024);
    for (int32_t i = 0; i < 1024; ++i) expectFrame(out, i, faded(ramp(kAnchorFrame + 2048 + i), i));
}

TEST_F(RendererVoiceTest, moreThanMaxVoicesIgnoredWithoutCrash) {
    publishRamp(1);
    publish(kOtherHi, kOtherLo, 1, kChunkFrames, halfScale);
    const RenderPlan p = plan(tooManyVoices());
    render(&p, 0, 1024);
    const std::vector<float> out = render(&p, 1024, 1024);
    for (int32_t i = 0; i < 1024; ++i) EXPECT_NEAR(kMaxVoices * ramp(kAnchorFrame + 1024 + i), out[2 * i], 1e-5);
    EXPECT_TRUE(renderer.probe(kMaxVoices).has_value());
    EXPECT_FALSE(renderer.probe(kMaxVoices + 1).has_value());
    EXPECT_FALSE(renderer.probe(kMaxVoices + 2).has_value());
}

TEST_F(RendererVoiceTest, vanishedVoiceFreesItsSlot) {
    publishRamp(1);
    publish(kOtherHi, kOtherLo, 1, kChunkFrames, halfScale);
    const RenderPlan full = plan(tooManyVoices());
    std::vector<VoicePlan> rest = tooManyVoices();
    rest.erase(rest.begin());
    const RenderPlan withoutFirst = plan(rest);
    render(&full, 0, 1024);
    render(&withoutFirst, 1024, 1024);
    const std::vector<float> out = render(&withoutFirst, 2048, 1024);
    EXPECT_FALSE(renderer.probe(1).has_value());
    EXPECT_TRUE(renderer.probe(kMaxVoices + 1).has_value());
    for (int32_t i = 0; i < 1024; ++i) {
        const float joined = faded(0.5F, i);
        EXPECT_NEAR((kMaxVoices - 1) * ramp(kAnchorFrame + 2048 + i) + joined, out[2 * i], 1e-5);
    }
}

TEST_F(RendererVoiceTest, nullOrInvalidPlanRendersSilence) {
    publishRamp(0);
    publishRamp(1);
    RenderPlan nanMap = plan({voice(1, kHostUs, 0)});
    nanMap.timeMap.usPerFrame = std::numeric_limits<double>::quiet_NaN();
    RenderPlan zeroRate = plan({voice(1)});
    zeroRate.rate = 0.0;
    const RenderPlan unaddressable = plan({voice(1, kHostUs, std::numeric_limits<int64_t>::max() / 2)});
    EXPECT_TRUE(allZero(render(nullptr, 0, 256)));
    EXPECT_TRUE(allZero(render(&nanMap, 0, 256)));
    EXPECT_TRUE(allZero(render(&zeroRate, 256, 256)));
    EXPECT_TRUE(allZero(render(&unaddressable, 512, 256)));
    EXPECT_EQ(0U, renderer.underruns());
}

}
