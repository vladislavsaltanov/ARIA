#include "renderer.h"

#include "chunk_window.h"
#include "envelope.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <limits>

namespace aria {
namespace {

constexpr double kMicrosPerSecond = 1e6;
constexpr double kLastAddressableFrame = static_cast<double>(kChunkFrames) * std::numeric_limits<int32_t>::max();
constexpr int64_t kNoEnd = std::numeric_limits<int64_t>::max();
const double kBackingCoefficient = 1.0 - std::exp(-1.0 / (Renderer::kBackingTauSeconds * Renderer::kOutputRate));

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

double voiceLevel(const RenderPlan& plan, const VoicePlan& voice, int64_t deviceFrame) {
    const double sinceAnchor = targetFrame(plan, voice, deviceFrame) - static_cast<double>(voice.anchorFrame);
    return dbToGain(voice.gainDb) * fadeLevel(voice.fade ? &*voice.fade : nullptr, std::max(0.0, sinceAnchor));
}

const VoicePlan* findVoice(const RenderPlan& plan, int32_t id) {
    for (const VoicePlan& voice : plan.voices) {
        if (voice.id == id) return &voice;
    }
    return nullptr;
}

bool continues(const VoiceState& state, const VoicePlan* voice) {
    return voice != nullptr && voice->playing && voice->epoch == state.epoch && voice->trackHi == state.trackHi &&
           voice->trackLo == state.trackLo;
}

void beginStop(VoiceState& state) {
    state.phase = SlotPhase::Stopping;
    state.stopFrom = static_cast<float>(state.level) * static_cast<float>(state.fadeInDone) / static_cast<float>(Renderer::kFadeInFrames);
    state.stopDone = 0;
}

template <typename GainAt>
bool mixVoice(const PcmPool& pool, VoiceState& state, int32_t frames, float* stereoOut, GainAt gainAt) {
    const bool copy = state.ratio == 1.0;
    ChunkWindow window(pool, state.trackHi, state.trackLo);
    bool starved = false;
    for (int32_t i = 0; i < frames; ++i) {
        const double position = state.position + static_cast<double>(i) * state.ratio;
        const double base = copy ? std::floor(position + 0.5) : std::floor(position);
        const int64_t frame = static_cast<int64_t>(base);
        if (frame < state.entryFrame || frame >= state.endFrame || frame >= state.endOfFile) continue;
        const Supply supply = window.supplyAt(frame, state.endOfFile);
        if (supply == Supply::Missing) {
            starved = true;
            state.fadeInDone = 0;
        }
        if (supply != Supply::Audible) continue;
        const Stereo value = copy ? window.frameAt(frame) : window.interpolated(frame, position - base);
        const float gain = gainAt(i);
        stereoOut[kChannels * i] += value.left * gain;
        stereoOut[kChannels * i + 1] += value.right * gain;
    }
    state.position += static_cast<double>(frames) * state.ratio;
    return starved;
}

}

Renderer::Renderer(const PcmPool& pool) : pool_(pool) {}

uint64_t Renderer::underruns() const { return underruns_.load(std::memory_order_relaxed); }

std::optional<VoiceProbe> Renderer::probe(int32_t voiceId) const {
    std::optional<VoiceProbe> stopping;
    for (const VoiceState& state : voices_) {
        if (state.phase == SlotPhase::Free || state.id != voiceId) continue;
        const VoiceProbe found{state.position, state.ratio, state.phase == SlotPhase::Stopping};
        if (!found.stopping) return found;
        if (!stopping) stopping = found;
    }
    return stopping;
}

void Renderer::render(const RenderPlan* plan, int64_t deviceFrame, int32_t frames, float* stereoOut) {
    if (frames <= 0 || stereoOut == nullptr) return;
    std::fill(stereoOut, stereoOut + static_cast<size_t>(frames) * kChannels, 0.0F);
    if (plan == nullptr || unusableScale(plan->rate) || unusableScale(plan->timeMap.usPerFrame)) return;
    if (!primed_) {
        primed_ = true;
        backing_ = plan->backingGain;
        master_ = plan->masterGain;
        masterTarget_ = plan->masterGain;
    }
    PlanOfSlot planOf{};
    assignVoices(*plan, deviceFrame, planOf);
    for (size_t slot = 0; slot < voices_.size(); ++slot) {
        VoiceState& state = voices_[slot];
        if (state.phase == SlotPhase::Active) renderActive(*plan, *planOf[slot], state, deviceFrame, frames, stereoOut);
        else if (state.phase == SlotPhase::Stopping) renderStopping(state, frames, stereoOut);
    }
    applyBusGains(*plan, frames, stereoOut);
}

void Renderer::assignVoices(const RenderPlan& plan, int64_t deviceFrame, PlanOfSlot& planOf) {
    for (size_t slot = 0; slot < voices_.size(); ++slot) {
        VoiceState& state = voices_[slot];
        if (state.phase != SlotPhase::Active) continue;
        const VoicePlan* voice = findVoice(plan, state.id);
        if (continues(state, voice)) {
            planOf[slot] = voice;
        } else {
            beginStop(state);
        }
    }
    for (const VoicePlan& voice : plan.voices) {
        if (!voice.playing || isActive(voice.id) || findVoice(plan, voice.id) != &voice) continue;
        VoiceState* state = claimSlot();
        if (state == nullptr) continue;
        *state = VoiceState{};
        state->phase = SlotPhase::Active;
        state->id = voice.id;
        state->epoch = voice.epoch;
        state->trackHi = voice.trackHi;
        state->trackLo = voice.trackLo;
        state->level = voiceLevel(plan, voice, deviceFrame);
        planOf[static_cast<size_t>(state - voices_.data())] = &voice;
    }
}

VoiceState* Renderer::claimSlot() {
    VoiceState* oldestStop = nullptr;
    for (VoiceState& state : voices_) {
        if (state.phase == SlotPhase::Free) return &state;
        if (state.phase == SlotPhase::Stopping && (oldestStop == nullptr || state.stopDone > oldestStop->stopDone)) oldestStop = &state;
    }
    return oldestStop;
}

bool Renderer::isActive(int32_t voiceId) const {
    return std::any_of(voices_.begin(), voices_.end(),
                       [voiceId](const VoiceState& state) { return state.phase == SlotPhase::Active && state.id == voiceId; });
}

void Renderer::renderActive(const RenderPlan& plan, const VoicePlan& voice, VoiceState& state, int64_t deviceFrame, int32_t frames,
                            float* stereoOut) {
    state.endFrame = voice.endFrame.value_or(kNoEnd);
    const double levelStart = state.level;
    state.level = voiceLevel(plan, voice, deviceFrame + frames);
    const double target = targetFrame(plan, voice, deviceFrame);
    if (!(std::fabs(target) < kLastAddressableFrame)) {
        state.entered = false;
        state.fadeInDone = 0;
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
    const double levelStep = (state.level - levelStart) / static_cast<double>(frames);
    const bool starved = mixVoice(pool_, state, frames, stereoOut, [&state, levelStart, levelStep](int32_t i) {
        return fadeInGain(state.fadeInDone) * static_cast<float>(levelStart + levelStep * static_cast<double>(i));
    });
    if (starved) underruns_.fetch_add(1, std::memory_order_relaxed);
}

void Renderer::renderStopping(VoiceState& state, int32_t frames, float* stereoOut) {
    const int32_t done = state.stopDone;
    const int32_t span = std::min(frames, kStopFrames - done);
    const float from = state.stopFrom;
    mixVoice(pool_, state, span, stereoOut, [from, done](int32_t i) {
        return from * (1.0F - static_cast<float>(done + i) / static_cast<float>(kStopFrames));
    });
    state.stopDone = done + span;
    if (state.stopDone >= kStopFrames) state.phase = SlotPhase::Free;
}

void Renderer::applyBusGains(const RenderPlan& plan, int32_t frames, float* stereoOut) {
    if (plan.masterGain != masterTarget_) {
        masterFrom_ = master_;
        masterTarget_ = plan.masterGain;
        masterRampDone_ = 0;
        masterRampFrames_ = unusableScale(plan.masterRampSeconds) ? 0 : std::llround(plan.masterRampSeconds * kOutputRate);
        if (masterRampFrames_ <= 0) master_ = masterTarget_;
    }
    const double backingTarget = plan.backingGain;
    for (int32_t i = 0; i < frames; ++i) {
        backing_ += (backingTarget - backing_) * kBackingCoefficient;
        if (masterRampDone_ < masterRampFrames_) {
            ++masterRampDone_;
            master_ = masterRampDone_ == masterRampFrames_
                          ? masterTarget_
                          : masterFrom_ + (masterTarget_ - masterFrom_) * static_cast<double>(masterRampDone_) /
                                              static_cast<double>(masterRampFrames_);
        }
        const float gain = static_cast<float>(master_ * backing_);
        stereoOut[kChannels * i] *= gain;
        stereoOut[kChannels * i + 1] *= gain;
    }
}

}
