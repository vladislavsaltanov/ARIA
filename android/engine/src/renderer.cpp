#include "renderer.h"

#include "chunk_window.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <limits>

namespace aria {
namespace {

constexpr double kMicrosPerSecond = 1e6;
constexpr double kLastAddressableFrame = static_cast<double>(kChunkFrames) * std::numeric_limits<int32_t>::max();
constexpr int64_t kNoEnd = std::numeric_limits<int64_t>::max();

double targetFrame(const RenderPlan& plan, const VoicePlan& voice, int64_t deviceFrame) {
    const TimeMap& map = plan.timeMap;
    const double hostUs = static_cast<double>(map.hostUsAtBaseFrame) +
                          (static_cast<double>(deviceFrame) - static_cast<double>(map.baseFrame)) * map.usPerFrame +
                          static_cast<double>(plan.trimUs);
    const double sinceAnchorUs = hostUs - static_cast<double>(voice.anchorUs);
    return static_cast<double>(voice.anchorFrame) + (sinceAnchorUs * plan.rate) / kMicrosPerSecond;
}

bool unusableScale(double value) { return !(value > 0.0) || !std::isfinite(value); }

float fadeInGain(int32_t& framesDone) {
    if (framesDone >= Renderer::kFadeInFrames) return 1.0F;
    return static_cast<float>(framesDone++) / static_cast<float>(Renderer::kFadeInFrames);
}

double ratioFor(double errorFrames, double rate) {
    if (std::fabs(errorFrames) < Renderer::kDeadZoneFrames) return 1.0;
    const double correction = errorFrames / rate / (2.0 * Renderer::kDriftWindowSeconds);
    return 1.0 + std::clamp(correction, -Renderer::kMaxRatioDeviation, Renderer::kMaxRatioDeviation);
}

}

Renderer::Renderer(const PcmPool& pool) : pool_(pool) {}

uint64_t Renderer::underruns() const { return underruns_.load(std::memory_order_relaxed); }

std::optional<VoiceProbe> Renderer::probe(int32_t voiceId) const {
    for (const VoiceState& state : voices_) {
        if (state.occupied && state.id == voiceId) return VoiceProbe{state.position, state.ratio};
    }
    return std::nullopt;
}

void Renderer::render(const RenderPlan* plan, int64_t deviceFrame, int32_t frames, float* stereoOut) {
    if (frames <= 0 || stereoOut == nullptr) return;
    std::fill(stereoOut, stereoOut + static_cast<size_t>(frames) * kChannels, 0.0F);
    if (plan == nullptr || unusableScale(plan->rate) || unusableScale(plan->timeMap.usPerFrame)) return;
    for (VoiceState& state : voices_) state.renderedThisBlock = false;
    for (const VoicePlan& voice : plan->voices) {
        VoiceState* state = stateFor(voice);
        if (state == nullptr) continue;
        state->renderedThisBlock = true;
        renderVoice(*plan, voice, *state, deviceFrame, frames, stereoOut);
    }
    for (VoiceState& state : voices_) state.occupied = state.renderedThisBlock;
}

VoiceState* Renderer::stateFor(const VoicePlan& voice) {
    VoiceState* vacant = nullptr;
    for (VoiceState& state : voices_) {
        if (state.occupied && state.id == voice.id) return state.renderedThisBlock ? nullptr : &state;
        if (!state.occupied && vacant == nullptr) vacant = &state;
    }
    if (vacant != nullptr) {
        *vacant = VoiceState{};
        vacant->occupied = true;
        vacant->id = voice.id;
    }
    return vacant;
}

void Renderer::renderVoice(const RenderPlan& plan, const VoicePlan& voice, VoiceState& state, int64_t deviceFrame, int32_t frames,
                           float* stereoOut) {
    if (state.epoch != voice.epoch || state.trackHi != voice.trackHi || state.trackLo != voice.trackLo) {
        state.epoch = voice.epoch;
        state.trackHi = voice.trackHi;
        state.trackLo = voice.trackLo;
        state.entered = false;
        state.endOfFile = kNoEnd;
    }
    const double target = targetFrame(plan, voice, deviceFrame);
    if (!voice.playing || !(std::fabs(target) < kLastAddressableFrame)) {
        state.entered = false;
        return;
    }
    const double error = target - state.position;
    if (!state.entered || std::fabs(error) > kReanchorSeconds * plan.rate) {
        if (!state.entered) state.entryFrame = std::max<int64_t>(voice.anchorFrame, 0);
        state.entered = true;
        state.position = target;
        state.ratio = 1.0;
        state.fadeInDone = 0;
    } else {
        state.ratio = ratioFor(error, plan.rate);
    }
    const bool copy = state.ratio == 1.0;
    const int64_t endFrame = voice.endFrame.value_or(kNoEnd);
    ChunkWindow window(pool_, voice.trackHi, voice.trackLo);
    bool starved = false;
    for (int32_t i = 0; i < frames; ++i) {
        const double position = state.position + static_cast<double>(i) * state.ratio;
        const double base = copy ? std::floor(position + 0.5) : std::floor(position);
        const int64_t frame = static_cast<int64_t>(base);
        if (frame < state.entryFrame || frame >= endFrame || frame >= state.endOfFile) continue;
        const Supply supply = window.supplyAt(frame, state.endOfFile);
        if (supply == Supply::Missing) {
            starved = true;
            state.fadeInDone = 0;
        }
        if (supply != Supply::Audible) continue;
        const Stereo value = copy ? window.frameAt(frame) : window.interpolated(frame, position - base);
        const float gain = fadeInGain(state.fadeInDone);
        stereoOut[kChannels * i] += value.left * gain;
        stereoOut[kChannels * i + 1] += value.right * gain;
    }
    state.position += static_cast<double>(frames) * state.ratio;
    if (starved) underruns_.fetch_add(1, std::memory_order_relaxed);
}

}
