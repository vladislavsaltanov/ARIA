#include "interp.h"
#include "renderer_fixture.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <vector>

namespace aria::fixture {
namespace {

constexpr double kDeadZone = 1e-3;
constexpr double kMaxDeviation = 5e-4;
constexpr int kMaxVoices = 8;
constexpr double kTwoPi = 6.283185307179586;
constexpr double kSineHz = 440.0;

double sine(double frame) { return 0.5 * std::sin(kTwoPi * kSineHz * frame / kRate + 1.0); }

int16_t sineSample(int64_t frame) { return static_cast<int16_t>(std::lround(sine(static_cast<double>(frame)) * 32768.0)); }

std::vector<VoicePlan> tooManyVoices() {
    std::vector<VoicePlan> voices;
    for (int32_t id = 1; id <= kMaxVoices + 2; ++id) {
        voices.push_back(id > kMaxVoices ? onOtherTrack(voice(id)) : voice(id));
    }
    return voices;
}

class RendererVoiceTest : public RendererFixture {
protected:
    struct Read {
        double position;
        float left;
        float right;
    };

    std::vector<Read> resampleFrom(int64_t anchor, int64_t blocks) {
        const RenderPlan synced = plan({voice(1, kHostUs, anchor)});
        const RenderPlan late = plan({voice(1, kHostUs, anchor)}, kHostUs + 3000);
        render(&synced, 0, 256);
        std::vector<Read> reads;
        double start = renderer.probe(1)->position;
        for (int64_t block = 1; block < blocks; ++block) {
            const std::vector<float> out = render(&late, block * 256, 256);
            const double ratio = renderer.probe(1)->ratio;
            EXPECT_NE(1.0, ratio);
            for (int32_t i = 0; block >= 2 && i < 256; ++i) reads.push_back({start + i * ratio, out[2 * i], out[2 * i + 1]});
            start = renderer.probe(1)->position;
        }
        return reads;
    }
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
        const VoiceProbe probe = *renderer.probe(1);
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
    double worst = 0.0;
    bool crossed = false;
    for (const Read& read : resampleFrom(2 * kChunkFrames - 4800, 40)) {
        crossed = crossed || read.position >= 2.0 * kChunkFrames;
        worst = std::max({worst, std::fabs(read.left - sine(read.position)), std::fabs(read.right + sine(read.position))});
    }
    EXPECT_TRUE(crossed);
    EXPECT_LT(worst, 1e-4);
}

TEST_F(RendererVoiceTest, cubicTapsIntoAbsentChunkStayFiniteAndOnSource) {
    publish(kRampHi, kRampLo, 1, kChunkFrames, sineSample);
    constexpr double kBoundary = 2.0 * kChunkFrames;
    int onSource = 0;
    int edge = 0;
    int beyond = 0;
    for (const Read& read : resampleFrom(2 * kChunkFrames - 4800, 40)) {
        ASSERT_TRUE(std::isfinite(read.left) && std::isfinite(read.right)) << read.position;
        const double base = std::floor(read.position);
        if (base + 2.0 < kBoundary) {
            ++onSource;
            EXPECT_NEAR(sine(read.position), read.left, 1e-4) << read.position;
            EXPECT_NEAR(-sine(read.position), read.right, 1e-4) << read.position;
        } else if (base < kBoundary) {
            ++edge;
            EXPECT_LE(std::fabs(read.left), 1.0F) << read.position;
        } else {
            ++beyond;
            EXPECT_EQ(0.0F, read.left) << read.position;
            EXPECT_EQ(0.0F, read.right) << read.position;
        }
    }
    EXPECT_GT(onSource, 0);
    EXPECT_GE(edge, 1);
    EXPECT_GT(beyond, 0);
    EXPECT_GT(renderer.underruns(), 0U);
}

TEST_F(RendererVoiceTest, negativeStartIsGatedAndNeverReadsChunkBelowZero) {
    publish(kRampHi, kRampLo, -1, kChunkFrames, halfScale);
    publishRamp(0);
    constexpr int32_t kLead = 48;
    const RenderPlan p = plan({voice(1, kHostUs + 1000, 0)});
    const std::vector<float> out = render(&p, 0, 1024);
    for (int32_t i = 0; i <= kLead; ++i) expectFrame(out, i, 0.0F);
    for (int32_t j = 0; kLead + j < 1024; ++j) expectFrame(out, kLead + j, faded(ramp(j), j));
    EXPECT_NEAR(1024.0 - kLead, renderer.probe(1)->position, kDeadZone);
    EXPECT_EQ(0U, renderer.underruns());
}

TEST_F(RendererVoiceTest, driftReanchorOfPlayingVoiceIsNotGatedByFutureAnchor) {
    publishRamp(1);
    constexpr int32_t kBlock = 1200;
    constexpr int64_t kNowUs = kHostUs + 50000;
    constexpr int64_t kNowFrame = kAnchorFrame + 2 * kBlock;
    const RenderPlan synced = plan({voice(1)});
    render(&synced, 0, kBlock);
    render(&synced, kBlock, kBlock);
    const RenderPlan jumped = plan({voice(1, kNowUs + 50000, kNowFrame + 2400)}, kHostUs + 6000);
    const std::vector<float> out = render(&jumped, 2 * kBlock, 1024);
    for (int32_t i = 0; i < 1024; ++i) expectFrame(out, i, faded(ramp(kNowFrame + 288 + i), i));
    EXPECT_NE(0.0F, out[2]);
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
    constexpr double kNan = std::numeric_limits<double>::quiet_NaN();
    constexpr double kInf = std::numeric_limits<double>::infinity();
    std::vector<RenderPlan> badScale;
    for (const double usPerFrame : {0.0, -kUsPerFrame, kNan, kInf}) {
        badScale.push_back(plan({voice(1, kHostUs, 0)}));
        badScale.back().timeMap.usPerFrame = usPerFrame;
    }
    for (const double rate : {0.0, kNan, kInf}) {
        badScale.push_back(plan({voice(1, kHostUs, 0)}));
        badScale.back().rate = rate;
    }
    const RenderPlan unaddressable = plan({voice(1, kHostUs, std::numeric_limits<int64_t>::max() / 2)});
    const auto silentThroughout = [this](const RenderPlan* p, bool keepsNoVoice) {
        Renderer fresh{pool};
        for (int64_t block = 0; block < 4; ++block) {
            std::vector<float> out(2 * 192, kGarbage);
            fresh.render(p, block * 192, 192, out.data());
            EXPECT_TRUE(allZero(out)) << "block " << block;
        }
        EXPECT_EQ(0U, fresh.underruns());
        if (keepsNoVoice) EXPECT_FALSE(fresh.probe(1).has_value());
    };
    silentThroughout(nullptr, true);
    for (const RenderPlan& p : badScale) silentThroughout(&p, true);
    silentThroughout(&unaddressable, false);
}

}
}
