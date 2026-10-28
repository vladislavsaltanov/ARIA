#include "click.h"

#include "pcm_pool.h"

#include <algorithm>
#include <cmath>

namespace aria {
namespace {

constexpr double kTwoPi = 6.283185307179586;
constexpr double kRate = 48000.0;
constexpr double kDecayFrames = ClickSynth::kBurstFrames / 4.0;

void synthesize(ClickSynth::Burst& burst, double hz, double amplitude) {
    for (int32_t i = 0; i < ClickSynth::kBurstFrames; ++i) {
        burst[static_cast<size_t>(i)] = static_cast<float>(amplitude * std::sin(kTwoPi * hz * i / kRate) * std::exp(-i / kDecayFrames));
    }
}

}

ClickSynth::ClickSynth() {
    synthesize(accent_, 2000.0, 1.0);
    synthesize(regular_, 1000.0, 0.5);
}

const ClickSynth::Burst& ClickSynth::burst(bool accent) const { return accent ? accent_ : regular_; }

void ClickSynth::trigger(int64_t frame, bool accent) {
    Click& oldest = *std::min_element(clicks_.begin(), clicks_.end(), [](const Click& a, const Click& b) { return a.frame < b.frame; });
    oldest = Click{frame, accent};
}

void ClickSynth::cancel() { clicks_.fill(Click{}); }

void ClickSynth::mix(int64_t blockFrame, int32_t from, int32_t to, float gain, float* stereoOut) const {
    for (const Click& click : clicks_) {
        if (click.frame == kSilent) continue;
        const Burst& samples = burst(click.accent);
        const int64_t lead = click.frame - blockFrame;
        const int64_t end = std::min<int64_t>(to, lead + kBurstFrames);
        for (int64_t i = std::max<int64_t>(from, lead); i < end; ++i) {
            const float value = gain * samples[static_cast<size_t>(i - lead)];
            stereoOut[kChannels * i] += value;
            stereoOut[kChannels * i + 1] += value;
        }
    }
}

}
