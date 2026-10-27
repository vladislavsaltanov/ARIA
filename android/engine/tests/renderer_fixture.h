#pragma once

#include "pcm_pool.h"
#include "plan.h"
#include "renderer.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <cstdint>
#include <utility>
#include <vector>

namespace aria::fixture {

constexpr double kRate = 48000.0;
constexpr double kUsPerFrame = 1e6 / kRate;
constexpr int64_t kHostUs = 13'000'000'000'000;
constexpr int64_t kRampHi = 0x0123456789ABCDEF;
constexpr int64_t kRampLo = 0x0FEDCBA987654321;
constexpr int64_t kOtherHi = 7;
constexpr int64_t kOtherLo = 9;
constexpr int64_t kAnchorFrame = 96000;
constexpr int32_t kFade = 480;
constexpr float kGarbage = 7.0F;

inline int16_t rampSample(int64_t frame) { return static_cast<int16_t>(1 + frame % 32767); }

inline int16_t halfScale(int64_t) { return 16384; }

inline float ramp(int64_t frame) { return static_cast<float>(rampSample(frame)) / 32768.0F; }

inline float faded(float value, int64_t framesIntoFade) {
    return framesIntoFade < kFade ? value * (static_cast<float>(framesIntoFade) / static_cast<float>(kFade)) : value;
}

inline VoicePlan voice(int32_t id, int64_t anchorUs = kHostUs, int64_t anchorFrame = kAnchorFrame) {
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

inline VoicePlan onOtherTrack(VoicePlan v) {
    v.trackHi = kOtherHi;
    v.trackLo = kOtherLo;
    return v;
}

inline RenderPlan plan(std::vector<VoicePlan> voices, int64_t hostUsAtFrame0 = kHostUs) {
    RenderPlan p;
    p.rate = kRate;
    p.voices = std::move(voices);
    p.timeMap = TimeMap{0, hostUsAtFrame0, kUsPerFrame};
    return p;
}

inline void expectFrame(const std::vector<float>& out, int32_t i, float left) {
    EXPECT_FLOAT_EQ(left, out[2 * i]) << "frame " << i;
    EXPECT_FLOAT_EQ(-left, out[2 * i + 1]) << "frame " << i;
}

inline bool allZero(const std::vector<float>& out) {
    return std::all_of(out.begin(), out.end(), [](float sample) { return sample == 0.0F; });
}

class RendererFixture : public ::testing::Test {
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

}
