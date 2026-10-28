#include "envelope.h"
#include "renderer_fixture.h"

#include <gtest/gtest.h>
#include <nlohmann/json.hpp>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <limits>
#include <string>
#include <vector>

namespace aria::fixture {
namespace {

using nlohmann::json;

constexpr double kParityTolerance = 1e-9;
constexpr int kEnvelopeVectors = 75;
constexpr float kHalf = 0.5F;

FadePlan fadeFrom(const json& value) {
    FadePlan fade;
    fade.from = value.at("from").get<double>();
    fade.to = value.at("to").get<double>();
    fade.progress = value.at("progress").get<double>();
    fade.frames = value.at("frames").get<int32_t>();
    fade.curve = value.at("curve").get<std::string>();
    fade.stopAtEnd = value.at("stopAtEnd").get<bool>();
    return fade;
}

double evaluate(const std::string& fn, const json& args) {
    if (fn == "curve") return curve(args[0].get<std::string>().c_str(), args[1].get<double>());
    if (fn == "dbToGain") return dbToGain(args[0].get<double>());
    if (args[0].is_null()) return fadeLevel(nullptr, args[1].get<double>());
    const FadePlan fade = fadeFrom(args[0]);
    return fadeLevel(&fade, args[1].get<double>());
}

FadePlan linearFade(double from, double to, double progress, int32_t frames) {
    FadePlan fade;
    fade.from = from;
    fade.to = to;
    fade.progress = progress;
    fade.frames = frames;
    fade.curve = "Linear";
    return fade;
}

VoicePlan withFade(VoicePlan v, const FadePlan& fade) {
    v.fade = fade;
    return v;
}

VoicePlan withGainDb(VoicePlan v, double gainDb) {
    v.gainDb = gainDb;
    return v;
}

VoicePlan paused(VoicePlan v) {
    v.playing = false;
    return v;
}

class RendererLevelTest : public RendererFixture {
protected:
    void publishHalf() { publish(kRampHi, kRampLo, 1, kChunkFrames, halfScale); }

    void expectRecovers(const RenderPlan& good, const RenderPlan& bad) {
        for (const bool goodFirst : {true, false}) {
            Renderer fresh{pool};
            int64_t frame = 0;
            if (goodFirst) {
                EXPECT_TRUE(allFinite(stream(fresh, &good, frame, 1024, 256)));
                frame += 1024;
            }
            EXPECT_TRUE(allFinite(stream(fresh, &bad, frame, 256, 256))) << "bad block, good first " << goodFirst;
            frame += 256;
            const std::vector<float> after = stream(fresh, &good, frame, 2048, 256);
            EXPECT_TRUE(allFinite(after)) << "recovery, good first " << goodFirst;
            for (int32_t i = 2048 - 256; i < 2048; ++i) expectFrame(after, i, kHalf);
        }
    }
};

constexpr std::array<double, 2> kNonFinite{std::numeric_limits<double>::quiet_NaN(), std::numeric_limits<double>::infinity()};

VoicePlan levelVoice() { return withFade(voice(1), linearFade(1.0, 1.0, 0.0, 4800)); }

TEST_F(RendererLevelTest, nonFiniteGainDbIsSilencedAndRecovers) {
    publishHalf();
    for (const double value : kNonFinite) expectRecovers(plan({levelVoice()}), plan({withGainDb(levelVoice(), value)}));
}

TEST_F(RendererLevelTest, nonFiniteFadeFromIsSilencedAndRecovers) {
    publishHalf();
    for (const double value : kNonFinite) {
        VoicePlan bad = levelVoice();
        bad.fade->from = value;
        expectRecovers(plan({levelVoice()}), plan({bad}));
    }
}

TEST_F(RendererLevelTest, nonFiniteFadeToIsSilencedAndRecovers) {
    publishHalf();
    for (const double value : kNonFinite) {
        VoicePlan bad = levelVoice();
        bad.fade->to = value;
        expectRecovers(plan({levelVoice()}), plan({bad}));
    }
}

TEST_F(RendererLevelTest, nonFiniteBackingGainPlanIsSkippedAndRecovers) {
    publishHalf();
    for (const double value : kNonFinite) {
        RenderPlan bad = plan({levelVoice()});
        bad.backingGain = static_cast<float>(value);
        expectRecovers(plan({levelVoice()}), bad);
    }
}

TEST_F(RendererLevelTest, nonFiniteMasterGainPlanIsSkippedAndRecovers) {
    publishHalf();
    for (const double value : kNonFinite) {
        RenderPlan bad = plan({levelVoice()});
        bad.masterGain = static_cast<float>(value);
        expectRecovers(plan({levelVoice()}), bad);
    }
}

TEST_F(RendererLevelTest, nonFiniteClickGainPlanIsSkippedAndRecovers) {
    publishHalf();
    for (const double value : kNonFinite) {
        RenderPlan bad = plan({levelVoice()});
        bad.clickGain = static_cast<float>(value);
        bad.test = TestPlan{kHostUs, 1024 * kUsPerFrame};
        expectRecovers(plan({levelVoice()}), bad);
    }
}

TEST_F(RendererLevelTest, stopDuringNonFiniteLevelFadesFromLastFiniteLevel) {
    publishHalf();
    for (const double value : kNonFinite) {
        Renderer fresh{pool};
        const VoicePlan bad = withGainDb(levelVoice(), value);
        const RenderPlan good = plan({levelVoice()});
        const RenderPlan poisoned = plan({bad});
        const RenderPlan stoppedWhilePoisoned = plan({paused(bad)});
        stream(fresh, &good, 0, 1024, 256);
        EXPECT_TRUE(allFinite(stream(fresh, &poisoned, 1024, 256, 256)));
        const std::vector<float> out = stream(fresh, &stoppedWhilePoisoned, 1280, 512, 256);
        EXPECT_TRUE(allFinite(out));
        EXPECT_FLOAT_EQ(kHalf, out[0]);
        EXPECT_FLOAT_EQ(kHalf * (1.0F - 100.0F / 384.0F), out[2 * 100]);
        EXPECT_EQ(0.0F, out[2 * 384]);
    }
}

TEST_F(RendererLevelTest, backingZeroTargetSnapsToExactSilence) {
    publishHalf();
    RenderPlan muted = plan({voice(1)});
    muted.backingGain = 0.0F;
    const RenderPlan open = plan({voice(1)});
    render(&open, 0, 1024);
    const std::vector<float> out = stream(renderer, &muted, 1024, 48000, 480);
    EXPECT_NE(0.0F, out[2 * 9600]);
    for (int32_t i = 24000; i < 48000; ++i) ASSERT_EQ(0.0F, out[2 * i]) << "frame " << i;
}

TEST_F(RendererLevelTest, masterRetargetMidRampContinuesFromCurrentValue) {
    publishHalf();
    RenderPlan silent = plan({voice(1)});
    silent.masterGain = 0.0F;
    RenderPlan up = plan({voice(1)});
    up.masterRampSeconds = 1.0F;
    RenderPlan down = silent;
    down.masterRampSeconds = 1.0F;
    render(&silent, 0, 1024);
    const std::vector<float> rising = stream(renderer, &up, 1024, 24000, 480);
    const std::vector<float> falling = stream(renderer, &down, 25024, 24000, 480);
    const float before = rising[2 * 23999];
    EXPECT_NEAR(0.25, before, 1e-3);
    EXPECT_NEAR(before, falling[0], 1e-4);
    EXPECT_LE(falling[0], before);
    EXPECT_NEAR(0.125, falling[2 * 23999], 1e-3);
}

TEST(EnvelopeTest, curvesAndFadeMatchParityVectors) {
    std::ifstream file(std::string(ARIA_PARITY_DIR) + "/timeline.json");
    ASSERT_TRUE(file.good());
    const json vectors = json::parse(file);
    int checked = 0;
    for (const json& vector : vectors) {
        const std::string fn = vector.at("fn").get<std::string>();
        if (fn != "curve" && fn != "fadeLevel" && fn != "dbToGain") continue;
        const double expected = vector.at("expected").get<double>();
        const double actual = evaluate(fn, vector.at("args"));
        EXPECT_NEAR(expected, actual, kParityTolerance * std::max({1.0, std::fabs(expected), std::fabs(actual)})) << vector.dump();
        ++checked;
    }
    EXPECT_EQ(kEnvelopeVectors, checked);
}

TEST_F(RendererLevelTest, fadeIsContinuousAcrossBlockBoundaries) {
    publishHalf();
    const RenderPlan p = plan({withFade(voice(1), linearFade(0.0, 1.0, 0.0, 48000))});
    for (const int32_t block : {64, 480}) {
        Renderer fresh{pool};
        const std::vector<float> out = stream(fresh, &p, 0, 52800, block);
        double jump = 0.0;
        for (size_t n = 2; n < out.size(); n += 2) jump = std::max(jump, static_cast<double>(std::fabs(out[n] - out[n - 2])));
        EXPECT_LT(jump, 1e-3) << "block " << block;
        EXPECT_NEAR(0.25, out[2 * 24000], 1e-4) << "block " << block;
        EXPECT_NEAR(0.5, out[2 * 48100], 1e-6) << "block " << block;
    }
}

TEST_F(RendererLevelTest, fadeFollowsTargetFrameNotDriftingReadPosition) {
    publishHalf();
    const VoicePlan v = withFade(voice(1), linearFade(0.0, 1.0, 0.0, 48000));
    const RenderPlan synced = plan({v});
    const RenderPlan late = plan({v}, kHostUs + 3000);
    render(&synced, 0, 1024);
    render(&late, 1024, 256);
    const std::vector<float> out = render(&late, 1280, 256);
    ASSERT_NE(1.0, renderer.probe(1)->ratio);
    for (int32_t i = 0; i < 256; ++i) EXPECT_NEAR(0.5 * (144.0 + 1280.0 + i) / 48000.0, out[2 * i], 1e-6) << "frame " << i;
}

TEST_F(RendererLevelTest, fadeHoldsProgressLevelBeforeFutureAnchor) {
    publishHalf();
    const RenderPlan before = plan({voice(1)});
    const RenderPlan ahead = plan({withFade(voice(1, kHostUs + 1'000'000, kAnchorFrame + 48000), linearFade(0.0, 1.0, 0.5, 4800))});
    render(&before, 0, 1024);
    render(&ahead, 1024, 256);
    const std::vector<float> out = render(&ahead, 1280, 256);
    for (int32_t i = 0; i < 256; ++i) expectFrame(out, i, 0.25F);
}

TEST_F(RendererLevelTest, stopFadesToZeroWithin384FramesMonotonically) {
    publishHalf();
    const VoicePlan v = withGainDb(voice(1), -6.0);
    const RenderPlan playing = plan({v});
    const RenderPlan stopped = plan({paused(v)});
    render(&playing, 0, 1024);
    const std::vector<float> out = stream(renderer, &stopped, 1024, 400, 100);
    const double level = 0.5 * std::pow(10.0, -6.0 / 20.0);
    EXPECT_NEAR(level, out[0], 1e-6);
    EXPECT_NEAR(level * (1.0 - 200.0 / 384.0), out[2 * 200], 1e-6);
    for (int32_t n = 1; n < 384; ++n) EXPECT_LT(out[2 * n], out[2 * (n - 1)]) << "frame " << n;
    EXPECT_GT(out[2 * 383], 0.0F);
    for (int32_t n = 0; n < 400; ++n) EXPECT_EQ(-out[2 * n], out[2 * n + 1]) << "frame " << n;
    for (int32_t n = 384; n < 400; ++n) EXPECT_EQ(0.0F, out[2 * n]) << "frame " << n;
}

TEST_F(RendererLevelTest, backingGainStepReaches63PercentAt20ms) {
    publishHalf();
    RenderPlan quiet = plan({voice(1)});
    quiet.backingGain = 0.0F;
    const RenderPlan loud = plan({voice(1)});
    EXPECT_TRUE(allZero(render(&quiet, 0, 1024)));
    const std::vector<float> out = render(&loud, 1024, 1024);
    EXPECT_GT(out[0], 0.0F);
    EXPECT_LT(out[0] / kHalf, 0.01F);
    const float at20ms = out[2 * 959] / kHalf;
    EXPECT_GE(at20ms, 0.62F);
    EXPECT_LE(at20ms, 0.64F);
}

TEST_F(RendererLevelTest, masterRampsLinearlyOver1s) {
    publishHalf();
    RenderPlan silent = plan({voice(1)});
    silent.masterGain = 0.0F;
    RenderPlan open = plan({voice(1)});
    open.masterRampSeconds = 1.0F;
    EXPECT_TRUE(allZero(render(&silent, 0, 1024)));
    const std::vector<float> out = stream(renderer, &open, 1024, 48480, 480);
    EXPECT_NEAR(0.25, out[2 * 12000] / kHalf, 0.01);
    EXPECT_NEAR(0.5, out[2 * 24000] / kHalf, 0.01);
    EXPECT_NEAR(0.75, out[2 * 36000] / kHalf, 0.01);
    EXPECT_FLOAT_EQ(kHalf, out[2 * 48100]);
}

TEST_F(RendererLevelTest, masterRampSecondsZeroNegativeOrNonFiniteJumps) {
    publishHalf();
    const RenderPlan full = plan({voice(1)});
    for (const float seconds : {0.0F, -1.0F, std::numeric_limits<float>::infinity(), std::numeric_limits<float>::quiet_NaN()}) {
        Renderer fresh{pool};
        RenderPlan quarter = plan({voice(1)});
        quarter.masterGain = 0.25F;
        quarter.masterRampSeconds = seconds;
        stream(fresh, &full, 0, 1024, 1024);
        const std::vector<float> out = stream(fresh, &quarter, 1024, 256, 256);
        for (int32_t i = 0; i < 256; ++i) expectFrame(out, i, 0.125F);
    }
}

TEST_F(RendererLevelTest, firstPlanStartsAtTargetGainsWithoutRamp) {
    publishHalf();
    RenderPlan p = plan({voice(1)});
    p.backingGain = 0.5F;
    p.masterGain = 0.8F;
    p.masterRampSeconds = 1.0F;
    const std::vector<float> out = render(&p, 0, 1024);
    expectFrame(out, 240, 0.1F);
    for (int32_t i = kFade; i < 1024; ++i) expectFrame(out, i, 0.2F);
}

TEST_F(RendererLevelTest, gainDbMinus6Gives0_501) {
    publishHalf();
    const RenderPlan p = plan({withGainDb(voice(1), -6.0)});
    const std::vector<float> out = render(&p, 0, 1024);
    EXPECT_NEAR(0.501, out[2 * 600] / kHalf, 5e-4);
}

TEST_F(RendererLevelTest, levelChangeIsInterpolatedAcrossOneBlock) {
    publishHalf();
    const RenderPlan before = plan({voice(1)});
    const RenderPlan after = plan({withGainDb(voice(1), -6.0)});
    render(&before, 0, 1024);
    const std::vector<float> out = stream(renderer, &after, 1024, 512, 256);
    const double gain = std::pow(10.0, -6.0 / 20.0);
    for (int32_t i = 0; i < 256; ++i) EXPECT_NEAR(0.5 * (1.0 + (gain - 1.0) * i / 256.0), out[2 * i], 1e-6) << "frame " << i;
    for (int32_t i = 256; i < 512; ++i) EXPECT_NEAR(0.5 * gain, out[2 * i], 1e-6) << "frame " << i;
}

TEST_F(RendererLevelTest, steadyOutputIsBitIdenticalForBlockSizes64_192_480_1024) {
    publishRamp(1);
    RenderPlan p = plan({withFade(withGainDb(voice(1), -3.0), linearFade(1.0, 0.5, 1.0, 4800))});
    p.backingGain = 0.7F;
    p.masterGain = 0.9F;
    constexpr int64_t kFrames = 15360;
    Renderer reference{pool};
    const std::vector<float> expected = stream(reference, &p, 0, kFrames, 64);
    EXPECT_NEAR(ramp(kAnchorFrame + 1000) * 0.5 * std::pow(10.0, -3.0 / 20.0) * 0.63, expected[2 * 1000], 1e-6);
    for (const int32_t block : {192, 480, 1024}) {
        Renderer fresh{pool};
        EXPECT_EQ(expected, stream(fresh, &p, 0, kFrames, block)) << "block " << block;
    }
}

}
}
