#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>

namespace aria {

class ClickSynth {
public:
    static constexpr int32_t kBurstFrames = 1920;
    static constexpr size_t kMaxClicks = 4;
    using Burst = std::array<float, kBurstFrames>;

    ClickSynth();

    const Burst& burst(bool accent) const;
    void trigger(int64_t frame, bool accent);
    void mix(int64_t blockFrame, int32_t from, int32_t to, float gain, float* stereoOut) const;

private:
    static constexpr int64_t kSilent = std::numeric_limits<int64_t>::min();

    struct Click {
        int64_t frame = kSilent;
        bool accent = false;
    };

    Burst accent_{};
    Burst regular_{};
    std::array<Click, kMaxClicks> clicks_{};
};

}
