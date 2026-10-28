#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace aria {

struct TimeMap {
    int64_t baseFrame = 0;
    int64_t hostUsAtBaseFrame = 0;
    double usPerFrame = 0.0;
};

struct FadePlan {
    double from = 0.0;
    double to = 0.0;
    double progress = 0.0;
    int32_t frames = 0;
    std::string curve;
    bool stopAtEnd = false;
};

struct GridPlan {
    int32_t voice = 0;
    double bpm = 0.0;
    int32_t beatsPerBar = 0;
    int64_t firstBeatFrame = 0;
};

struct TestPlan {
    int64_t anchorUs = 0;
    double intervalUs = 0.0;
};

struct VoicePlan {
    int32_t id = 0;
    int32_t epoch = 0;
    int64_t trackHi = 0;
    int64_t trackLo = 0;
    double gainDb = 0.0;
    int64_t anchorUs = 0;
    int64_t anchorFrame = 0;
    bool playing = false;
    std::optional<int64_t> endFrame;
    std::optional<FadePlan> fade;
};

struct RenderPlan {
    int64_t generation = 0;
    double rate = 0.0;
    std::vector<VoicePlan> voices;
    std::optional<GridPlan> grid;
    std::optional<TestPlan> test;
    float backingGain = 0.0F;
    float clickGain = 0.0F;
    float masterGain = 0.0F;
    float masterRampSeconds = 0.0F;
    int64_t trimUs = 0;
    TimeMap timeMap;
};

}
