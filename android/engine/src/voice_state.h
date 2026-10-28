#pragma once

#include <cstdint>
#include <limits>

namespace aria {

enum class SlotPhase { Free, Active, Stopping };

struct VoiceState {
    SlotPhase phase = SlotPhase::Free;
    bool entered = false;
    int32_t id = 0;
    int32_t epoch = 0;
    int64_t trackHi = 0;
    int64_t trackLo = 0;
    double position = 0.0;
    double blockStart = 0.0;
    double ratio = 1.0;
    int64_t entryFrame = 0;
    int64_t endFrame = std::numeric_limits<int64_t>::max();
    int64_t endOfFile = std::numeric_limits<int64_t>::max();
    int32_t fadeInDone = 0;
    double level = 0.0;
    float stopFrom = 0.0F;
    int32_t stopDone = 0;
};

}
