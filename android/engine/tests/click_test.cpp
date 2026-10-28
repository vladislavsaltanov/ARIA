#include "click.h"
#include "renderer_fixture.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <limits>
#include <optional>
#include <utility>
#include <vector>

namespace aria::fixture {
namespace {

constexpr int32_t kBurst = ClickSynth::kBurstFrames;
constexpr double kTwoPi = 6.283185307179586;
constexpr double kBpm = 120.0;
constexpr int64_t kBeat = 24000;
constexpr int64_t kAt = 1000;
constexpr int32_t kBar = 4;
constexpr double kClickTolerance = 1e-6;
constexpr double kNan = std::numeric_limits<double>::quiet_NaN();
constexpr double kInf = std::numeric_limits<double>::infinity();
constexpr double kMaxIndex = 9007199254740992.0;
constexpr int64_t kMaxBeat = 9007199254740992;
constexpr std::chrono::seconds kBoundedRender{2};

struct Hit {
    int64_t frame;
    bool accent;
};

std::vector<float> clicks(int64_t frames, const std::vector<Hit>& hits, float gain = 1.0F) {
    const ClickSynth synth;
    std::vector<float> mono(static_cast<size_t>(frames), 0.0F);
    for (const Hit& hit : hits) {
        for (int64_t i = 0; i < kBurst && hit.frame + i < frames; ++i) {
            if (hit.frame + i >= 0) mono[static_cast<size_t>(hit.frame + i)] += gain * synth.burst(hit.accent)[static_cast<size_t>(i)];
        }
    }
    return mono;
}

void expectClicks(const std::vector<float>& out, const std::vector<float>& mono) {
    ASSERT_EQ(2 * mono.size(), out.size());
    for (size_t frame = 0; frame < mono.size(); ++frame) {
        ASSERT_NEAR(mono[frame], out[2 * frame], kClickTolerance) << "frame " << frame;
        ASSERT_EQ(out[2 * frame], out[2 * frame + 1]) << "frame " << frame;
    }
}

VoicePlan reading(int64_t fileFrame, int64_t atDeviceFrame) { return voice(1, kHostUs, fileFrame - atDeviceFrame); }

GridPlan grid(double bpm = kBpm, int64_t firstBeatFrame = 0, int32_t beatsPerBar = kBar) {
    return GridPlan{1, bpm, beatsPerBar, firstBeatFrame};
}

RenderPlan clickPlan(std::vector<VoicePlan> voices, std::optional<GridPlan> beats) {
    RenderPlan p = plan(std::move(voices));
    p.grid = beats;
    p.backingGain = 0.0F;
    p.clickGain = 1.0F;
    return p;
}

RenderPlan testPlan(int64_t anchorUs, double intervalUs) {
    RenderPlan p = clickPlan({}, std::nullopt);
    p.test = TestPlan{anchorUs, intervalUs};
    return p;
}

class ClickTest : public RendererFixture {
protected:
    std::vector<float> run(const RenderPlan& p, int64_t frames, int32_t block = 1024) { return stream(renderer, &p, 0, frames, block); }
};

TEST(ClickSynthTest, burstTablesMatchFormula) {
    const ClickSynth synth;
    ASSERT_EQ(1920, kBurst);
    for (int32_t i = 0; i < kBurst; ++i) {
        const double decay = std::exp(-i / 480.0);
        EXPECT_NEAR(std::sin(kTwoPi * 2000.0 * i / 48000.0) * decay, synth.burst(true)[i], 1e-6) << "accent " << i;
        EXPECT_NEAR(0.5 * std::sin(kTwoPi * 1000.0 * i / 48000.0) * decay, synth.burst(false)[i], 1e-6) << "regular " << i;
    }
}

TEST_F(ClickTest, clickLandsOnExactSampleOfBeat) {
    const RenderPlan p = clickPlan({reading(kBeat, kAt)}, grid());
    const std::vector<float> out = render(&p, 0, 4096);
    expectClicks(out, clicks(4096, {{kAt, false}}));
    EXPECT_EQ(0.0F, out[2 * kAt]);
    EXPECT_NE(0.0F, out[2 * (kAt + 1)]);
}

TEST_F(ClickTest, beatOnWholeFrameIgnoresReadPositionRoundingNoise) {
    constexpr int64_t kStart = 2;
    const RenderPlan p = clickPlan({reading(kBeat, kAt)}, grid());
    const std::vector<float> out = render(&p, kStart, 4096);
    const double noise = renderer.probe(1)->position - static_cast<double>(kBeat - kAt + kStart + 4096);
    ASSERT_LT(noise, 0.0);
    ASSERT_GT(noise, -1e-3);
    expectClicks(out, clicks(4096, {{kAt - kStart, false}}));
}

TEST_F(ClickTest, sameOutputForBlockSizes64_192_480_1024) {
    publishRamp(1);
    RenderPlan p = plan({voice(1)});
    p.grid = grid(600.0, 1024);
    p.trimUs = 1;
    p.backingGain = 0.7F;
    p.masterGain = 0.9F;
    p.clickGain = 0.8F;
    RenderPlan bare = p;
    bare.grid.reset();
    constexpr int64_t kFrames = 30720;
    Renderer reference{pool};
    const std::vector<float> expected = stream(reference, &p, 0, kFrames, 64);
    Renderer voiceOnly{pool};
    const std::vector<float> backing = stream(voiceOnly, &bare, 0, kFrames, 64);
    const ClickSynth synth;
    for (int32_t i = 0; i < 1024; ++i) ASSERT_EQ(backing[2 * i], expected[2 * i]) << "frame " << i;
    for (int32_t i = 0; i < kBurst; ++i) {
        EXPECT_NEAR(0.9 * 0.8 * synth.burst(true)[i], expected[2 * (1024 + i)] - backing[2 * (1024 + i)], 1e-5) << "burst " << i;
    }
    for (const int32_t block : {192, 480, 1024}) {
        Renderer fresh{pool};
        EXPECT_EQ(expected, stream(fresh, &p, 0, kFrames, block)) << "block " << block;
    }
}

TEST_F(ClickTest, tailContinuesAcrossBlockBoundary) {
    const RenderPlan p = clickPlan({reading(kBeat, kAt)}, grid());
    expectClicks(run(p, 4096, 256), clicks(4096, {{kAt, false}}));
}

TEST_F(ClickTest, accentOnIndexModBarZeroIncludingNegativeIndex) {
    constexpr int64_t kSpacing = 4800;
    const RenderPlan p = clickPlan({voice(1)}, grid(600.0, kAnchorFrame + 6 * kSpacing + 100, 3));
    expectClicks(run(p, 30720),
                 clicks(30720, {{100, true}, {4900, false}, {9700, false}, {14500, true}, {19300, false}, {24100, false}, {28900, true}}));
}

TEST_F(ClickTest, silentWhenBpmZeroOrBarsZero) {
    const std::vector<GridPlan> broken{grid(0.0),  grid(-kBpm), grid(kNan),         grid(kInf),
                                       grid(-kInf), grid(1e12), grid(kBpm, 0, 0), grid(kBpm, 0, -4)};
    for (const GridPlan& beats : broken) {
        Renderer fresh{pool};
        const RenderPlan p = clickPlan({reading(kBeat, kAt)}, beats);
        EXPECT_TRUE(allZero(stream(fresh, &p, 0, 48000, 480))) << "bpm " << beats.bpm << " bars " << beats.beatsPerBar;
    }
    const RenderPlan valid = clickPlan({reading(kBeat, kAt)}, grid());
    EXPECT_FALSE(allZero(run(valid, 4096)));
}

TEST_F(ClickTest, silentWhenGridVoiceMissing) {
    GridPlan other = grid();
    other.voice = 2;
    const RenderPlan missing = clickPlan({reading(kBeat, kAt)}, other);
    EXPECT_TRUE(allZero(run(missing, 4096)));
    Renderer fresh{pool};
    const RenderPlan empty = clickPlan({}, grid());
    EXPECT_TRUE(allZero(stream(fresh, &empty, 0, 4096, 1024)));
}

TEST_F(ClickTest, silentWhileGridVoiceUnaddressable) {
    const RenderPlan playing = clickPlan({reading(kBeat, kAt)}, grid());
    const RenderPlan unaddressable = clickPlan({voice(1, kHostUs, std::numeric_limits<int64_t>::max() / 2)}, grid());
    const std::vector<float> before = render(&playing, 0, 2048);
    const std::vector<float> after = render(&unaddressable, 2048, 2048);
    ASSERT_FALSE(renderer.probe(1)->stopping);
    std::vector<float> out = before;
    out.insert(out.end(), after.begin(), after.end());
    expectClicks(out, clicks(4096, {{kAt, false}}));
}

TEST_F(ClickTest, silentWhenVoicePaused) {
    VoicePlan stopped = reading(kBeat, kAt);
    stopped.playing = false;
    const RenderPlan playing = clickPlan({reading(kBeat, kAt)}, grid());
    const RenderPlan paused = clickPlan({stopped}, grid());
    Renderer fresh{pool};
    EXPECT_TRUE(allZero(stream(fresh, &paused, 0, 4096, 1024)));
    EXPECT_TRUE(allZero(render(&playing, 0, 900)));
    EXPECT_TRUE(allZero(render(&paused, 900, 200)));
    ASSERT_TRUE(renderer.probe(1)->stopping);
    EXPECT_TRUE(allZero(render(&paused, 1100, 2900)));
}

TEST_F(ClickTest, noBeatsAfterEndFrame) {
    for (const int64_t end : {2 * kBeat, 2 * kBeat + 1}) {
        Renderer fresh{pool};
        VoicePlan v = reading(kBeat, kAt);
        v.endFrame = end;
        const RenderPlan p = clickPlan({v}, grid());
        std::vector<Hit> hits{{kAt, false}};
        if (end > 2 * kBeat) hits.push_back({kAt + kBeat, false});
        expectClicks(stream(fresh, &p, 0, 28800, 960), clicks(28800, hits));
    }
}

TEST_F(ClickTest, beatBeforeEntryGateIsSilent) {
    const RenderPlan p = clickPlan({voice(1, kHostUs + 50000, 30000)}, grid(kBpm, 4000));
    EXPECT_TRUE(allZero(run(p, 4096)));
}

TEST_F(ClickTest, gridClickDuringEntryFadeInIsFullLevel) {
    publish(kRampHi, kRampLo, 0, kChunkFrames, halfScale);
    RenderPlan p = clickPlan({voice(1, kHostUs + 10000, kBeat)}, grid());
    p.backingGain = 1.0F;
    const std::vector<float> out = run(p, 4096);
    const ClickSynth synth;
    for (int64_t frame = 0; frame < 4096; ++frame) {
        const int64_t into = frame - 480;
        const float backing = into < 0 ? 0.0F : faded(0.5F, into);
        const float click = into >= 0 && into < kBurst ? synth.burst(false)[static_cast<size_t>(into)] : 0.0F;
        ASSERT_NEAR(backing + click, out[2 * frame], kClickTolerance) << "frame " << frame;
        ASSERT_NEAR(click - backing, out[2 * frame + 1], kClickTolerance) << "frame " << frame;
    }
}

TEST_F(ClickTest, testClickFollowsAnchorAndInterval) {
    const RenderPlan p = testPlan(kHostUs + 1'010'000, 500000.0);
    expectClicks(run(p, 51200), clicks(51200, {{480, true}, {24480, true}, {48480, true}}));
}

TEST_F(ClickTest, testClickHonoursTrimUs) {
    for (const auto& [trimUs, at] : {std::pair<int64_t, int64_t>{10000, 480}, {-10000, 1440}}) {
        Renderer fresh{pool};
        RenderPlan p = testPlan(kHostUs + 20000, 1e9);
        p.trimUs = trimUs;
        expectClicks(stream(fresh, &p, 0, 4096, 1024), clicks(4096, {{at, true}}));
    }
}

TEST_F(ClickTest, testClicksSilentForBrokenInterval) {
    for (const double interval : {0.0, -500000.0, kNan, kInf, 1.0}) {
        Renderer fresh{pool};
        const RenderPlan p = testPlan(kHostUs, interval);
        EXPECT_TRUE(allZero(stream(fresh, &p, 0, 4096, 1024))) << "interval " << interval;
    }
}

TEST_F(ClickTest, testPlanSuppressesGridClicks) {
    RenderPlan p = clickPlan({reading(kBeat, kAt)}, grid());
    p.test = TestPlan{kHostUs + 50000, 1e9};
    expectClicks(run(p, 4096), clicks(4096, {{2400, true}}));
    RenderPlan idle = p;
    idle.test->intervalUs = 0.0;
    Renderer fresh{pool};
    EXPECT_TRUE(allZero(stream(fresh, &idle, 0, 4096, 1024)));
}

TEST_F(ClickTest, clickGainZeroSilentAndIndependentOfBackingGain) {
    RenderPlan muted = clickPlan({reading(kBeat, kAt)}, grid());
    muted.clickGain = 0.0F;
    EXPECT_TRUE(allZero(run(muted, 4096)));
    for (const float backing : {0.0F, 0.25F, 1.0F}) {
        Renderer fresh{pool};
        RenderPlan p = clickPlan({reading(kBeat, kAt)}, grid());
        p.clickGain = 0.5F;
        p.backingGain = backing;
        expectClicks(stream(fresh, &p, 0, 4096, 1024), clicks(4096, {{kAt, false}}, 0.5F));
    }
    publish(kRampHi, kRampLo, 0, kChunkFrames, halfScale);
    RenderPlan audible = muted;
    audible.backingGain = 1.0F;
    RenderPlan bare = audible;
    bare.grid.reset();
    Renderer withGrid{pool};
    Renderer withoutGrid{pool};
    const std::vector<float> out = stream(withGrid, &audible, 0, 4096, 1024);
    EXPECT_FALSE(allZero(out));
    EXPECT_EQ(stream(withoutGrid, &bare, 0, 4096, 1024), out);
}

TEST_F(ClickTest, masterScalesClickButBackingDoesNot) {
    RenderPlan p = clickPlan({reading(kBeat, kAt)}, grid());
    p.masterGain = 0.5F;
    p.backingGain = 0.25F;
    p.clickGain = 0.8F;
    expectClicks(run(p, 4096), clicks(4096, {{kAt, false}}, 0.4F));
}

TEST_F(ClickTest, ratioShiftsClickOffsetInsideBlock) {
    constexpr int32_t kBlock = 4096;
    constexpr int64_t kInto = 3800;
    const RenderPlan synced = clickPlan({voice(1)}, grid(kBpm, kAnchorFrame + kBlock + kInto - 4 * kBeat));
    RenderPlan late = synced;
    late.timeMap.hostUsAtBaseFrame = kHostUs + 80;
    EXPECT_TRUE(allZero(render(&synced, 0, kBlock)));
    const std::vector<float> out = render(&late, kBlock, kBlock);
    ASSERT_NEAR(1.0004, renderer.probe(1)->ratio, 1e-7);
    expectClicks(out, clicks(kBlock, {{3799, true}}));
}

TEST_F(ClickTest, fifthConcurrentClickReplacesOldest) {
    constexpr int64_t kSpacing = 240;
    constexpr int64_t kFrames = 4800;
    const RenderPlan p = testPlan(kHostUs, 5000.0);
    const std::vector<float> out = run(p, kFrames, 480);
    const ClickSynth synth;
    for (int64_t frame = 0; frame < kFrames; ++frame) {
        const int64_t latest = frame / kSpacing;
        float sum = 0.0F;
        for (int64_t k = std::max<int64_t>(0, latest - 3); k <= latest; ++k) sum += synth.burst(true)[static_cast<size_t>(frame - k * kSpacing)];
        ASSERT_NEAR(sum, out[2 * frame], kClickTolerance) << "frame " << frame;
    }
}

TEST_F(ClickTest, nullOrUnusablePlanNeitherAddsNorRemovesClicks) {
    const RenderPlan p = testPlan(kHostUs + 5000, 1e9);
    RenderPlan poisoned = testPlan(kHostUs + 22500, 1e9);
    poisoned.clickGain = static_cast<float>(kNan);
    for (const RenderPlan* gap : {static_cast<const RenderPlan*>(nullptr), static_cast<const RenderPlan*>(&poisoned)}) {
        Renderer fresh{pool};
        expectClicks(stream(fresh, &p, 0, 1024, 1024), clicks(1024, {{240, true}}));
        EXPECT_TRUE(allZero(stream(fresh, gap, 1024, 256, 256)));
        expectClicks(stream(fresh, &p, 1280, 1024, 1024), clicks(1024, {{240 - 1280, true}}));
    }
}

TEST_F(ClickTest, silentForBeatIndicesBeyondDoublePrecision) {
    constexpr int64_t kMin = std::numeric_limits<int64_t>::min();
    const RenderPlan beats = clickPlan({reading(kBeat, kAt)}, grid(2'880'000.0, kMin));
    const RenderPlan tests = testPlan(kMin, kUsPerFrame);
    for (const RenderPlan* p : {&beats, &tests}) {
        Renderer fresh{pool};
        EXPECT_TRUE(allZero(stream(fresh, p, 0, 4096, 1024)));
    }
}

TEST_F(ClickTest, testClickLoopIsBoundedNearDoublePrecisionLimit) {
    const RenderPlan p = testPlan(static_cast<int64_t>(static_cast<double>(kHostUs) - (kMaxIndex - 3.5) * kUsPerFrame), kUsPerFrame);
    const auto started = std::chrono::steady_clock::now();
    const std::vector<float> out = render(&p, 0, 1024);
    EXPECT_LT(std::chrono::steady_clock::now() - started, kBoundedRender);
    EXPECT_TRUE(allFinite(out));
}

TEST_F(ClickTest, gridClickLoopIsBoundedNearDoublePrecisionLimit) {
    const RenderPlan p = clickPlan({reading(kBeat, kAt)}, grid(2'880'000.0, -(kMaxBeat - 25000)));
    const auto started = std::chrono::steady_clock::now();
    const std::vector<float> out = run(p, 4096);
    EXPECT_LT(std::chrono::steady_clock::now() - started, kBoundedRender);
    EXPECT_TRUE(allFinite(out));
    EXPECT_NE(0.0F, out[2 * 10]);
}

TEST_F(ClickTest, deviceFrameRewindCancelsActiveClicks) {
    const RenderPlan clicking = testPlan(kHostUs + 100000, 5000.0);
    const RenderPlan quiet = clickPlan({}, std::nullopt);
    for (const int64_t rewindTo : {int64_t{0}, int64_t{4608}}) {
        Renderer fresh{pool};
        ASSERT_FALSE(allZero(stream(fresh, &clicking, 4096, 1024, 1024)));
        EXPECT_TRUE(allZero(stream(fresh, &quiet, rewindTo, 8192 - rewindTo, 512))) << "rewind to " << rewindTo;
    }
}

TEST_F(ClickTest, forwardDeviceFrameGapKeepsActiveClicks) {
    const RenderPlan clicking = testPlan(kHostUs + 100000, 1e9);
    const RenderPlan quiet = clickPlan({}, std::nullopt);
    expectClicks(stream(renderer, &clicking, 4096, 1024, 1024), clicks(1024, {{4800 - 4096, true}}));
    expectClicks(stream(renderer, &quiet, 5600, 1024, 1024), clicks(1024, {{4800 - 5600, true}}));
}

TEST_F(ClickTest, firstBlockAtAnyDeviceFrameRendersItsClicks) {
    const RenderPlan clicking = testPlan(kHostUs + 100000, 1e9);
    expectClicks(stream(renderer, &clicking, 4096, 2048, 1024), clicks(2048, {{4800 - 4096, true}}));
}

TEST_F(ClickTest, nullOrUnusablePlanBlockStillTracksDeviceFrameRewind) {
    const RenderPlan clicking = testPlan(kHostUs + 100000, 1e9);
    const RenderPlan quiet = clickPlan({}, std::nullopt);
    RenderPlan poisoned = quiet;
    poisoned.clickGain = static_cast<float>(kNan);
    for (const RenderPlan* gap : {static_cast<const RenderPlan*>(nullptr), static_cast<const RenderPlan*>(&poisoned)}) {
        Renderer fresh{pool};
        ASSERT_FALSE(allZero(stream(fresh, &clicking, 4096, 1024, 1024)));
        EXPECT_TRUE(allZero(stream(fresh, gap, 0, 6144, 6144)));
        EXPECT_TRUE(allZero(stream(fresh, &quiet, 6144, 2048, 1024)));
    }
}

TEST_F(ClickTest, largeFramesAndBeatIndicesLandExactly) {
    constexpr int64_t kFar = 3'000'000'000;
    const RenderPlan byIndex = clickPlan({reading(kFar, kAt)}, grid());
    expectClicks(run(byIndex, 4096), clicks(4096, {{kAt, true}}));
    Renderer fresh{pool};
    const RenderPlan byOrigin = clickPlan({reading(kFar, kAt)}, grid(kBpm, kFar - 3 * kBeat));
    expectClicks(stream(fresh, &byOrigin, 0, 4096, 1024), clicks(4096, {{kAt, false}}));
}

}
}
