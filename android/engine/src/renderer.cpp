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
constexpr double kBackingSnap = 1e-9;
constexpr double kBeatTolerance = 1e-3;
constexpr double kMaxBeatIndex = 9007199254740992.0;
constexpr uint64_t kStaleMapFrames = 48000;

double targetFrame(const RenderPlan& plan, const VoicePlan& voice, int64_t deviceFrame) {
    const TimeMap& map = plan.timeMap;
    const double hostUs = static_cast<double>(map.hostUsAtBaseFrame) +
                          (static_cast<double>(deviceFrame) - static_cast<double>(map.baseFrame)) * map.usPerFrame +
                          static_cast<double>(plan.trimUs);
    const double sinceAnchorUs = hostUs - static_cast<double>(voice.anchorUs);
    return static_cast<double>(voice.anchorFrame) + (sinceAnchorUs * plan.rate) / kMicrosPerSecond;
}

bool unusableScale(double value) { return !(value > 0.0) || !std::isfinite(value); }

bool mapOlderThanStream(const TimeMap& map, int64_t deviceFrame) {
    return deviceFrame < map.baseFrame && static_cast<uint64_t>(map.baseFrame) - static_cast<uint64_t>(deviceFrame) > kStaleMapFrames;
}

bool unusablePlan(const RenderPlan* plan, int64_t deviceFrame) {
    return plan == nullptr || unusableScale(plan->rate) || unusableScale(plan->timeMap.usPerFrame) || !std::isfinite(plan->backingGain) ||
           !std::isfinite(plan->masterGain) || !std::isfinite(plan->clickGain) || mapOlderThanStream(plan->timeMap, deviceFrame);
}

bool usableLevel(double level) { return std::fabs(level) <= std::numeric_limits<float>::max(); }

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

size_t consideredVoices(const RenderPlan& plan) { return std::min(plan.voices.size(), Renderer::kMaxPlanVoices); }

const VoicePlan* findVoice(const RenderPlan& plan, int32_t id) {
    const auto considered = plan.voices.begin() + static_cast<std::ptrdiff_t>(consideredVoices(plan));
    const auto found = std::find_if(plan.voices.begin(), considered, [id](const VoicePlan& voice) { return voice.id == id; });
    return found == considered ? nullptr : &*found;
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

template <typename Emit>
void gridClicks(const GridPlan& grid, const VoiceState& voice, int32_t frames, Emit emit) {
    const double spacing = 60.0 / grid.bpm * Renderer::kOutputRate;
    if (!(spacing >= 1.0) || !std::isfinite(spacing) || grid.beatsPerBar <= 0) return;
    const double origin = static_cast<double>(grid.firstBeatFrame);
    const auto indexAt = [origin, spacing](double frame) { return std::ceil((frame - origin) / spacing); };
    const double first = std::max(indexAt(voice.blockStart), indexAt(static_cast<double>(voice.entryFrame)));
    const double end = std::min(indexAt(voice.position), indexAt(static_cast<double>(voice.endFrame)));
    if (!(std::fabs(first) < kMaxBeatIndex) || !(std::fabs(end) < kMaxBeatIndex)) return;
    const double lead = voice.blockStart - origin;
    for (auto k = static_cast<int64_t>(first); k < static_cast<int64_t>(end); ++k) {
        const double offset = std::ceil((static_cast<double>(k) * spacing - lead) / voice.ratio - kBeatTolerance);
        emit(static_cast<int32_t>(std::clamp(offset, 0.0, static_cast<double>(frames))), k % grid.beatsPerBar == 0);
    }
}

template <typename Emit>
void testClicks(const RenderPlan& plan, int64_t deviceFrame, int32_t frames, Emit emit) {
    const TestPlan& test = *plan.test;
    const TimeMap& map = plan.timeMap;
    if (!(test.intervalUs >= map.usPerFrame) || !std::isfinite(test.intervalUs)) return;
    const double since = static_cast<double>(deviceFrame) - static_cast<double>(map.baseFrame);
    const double lead = static_cast<double>(test.anchorUs) - static_cast<double>(map.hostUsAtBaseFrame) - static_cast<double>(plan.trimUs);
    const auto offsetOf = [&](int64_t k) {
        return std::ceil((lead + static_cast<double>(k) * test.intervalUs) / map.usPerFrame - kBeatTolerance) - since;
    };
    const double estimate = std::floor((since * map.usPerFrame - lead) / test.intervalUs) - 1.0;
    if (!(std::fabs(estimate) < kMaxBeatIndex)) return;
    auto k = static_cast<int64_t>(estimate);
    while (offsetOf(k) < 0.0) ++k;
    for (double offset = offsetOf(k); offset < frames; offset = offsetOf(++k)) emit(static_cast<int32_t>(offset), true);
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
    if (deviceFrame < blockEnd_) clicks_.cancel();
    blockEnd_ = deviceFrame + frames;
    if (unusablePlan(plan, deviceFrame)) return;
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
    applyBusGains(*plan, deviceFrame, frames, stereoOut);
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
    for (size_t index = 0; index < consideredVoices(plan); ++index) {
        const VoicePlan& voice = plan.voices[index];
        if (!voice.playing || activeSlot(voice.id) != nullptr || findVoice(plan, voice.id) != &voice) continue;
        VoiceState* state = claimSlot();
        if (state == nullptr) continue;
        *state = VoiceState{};
        state->phase = SlotPhase::Active;
        state->id = voice.id;
        state->epoch = voice.epoch;
        state->trackHi = voice.trackHi;
        state->trackLo = voice.trackLo;
        const double level = voiceLevel(plan, voice, deviceFrame);
        state->level = usableLevel(level) ? level : 0.0;
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

const VoiceState* Renderer::activeSlot(int32_t voiceId) const {
    for (const VoiceState& state : voices_) {
        if (state.phase == SlotPhase::Active && state.id == voiceId) return &state;
    }
    return nullptr;
}

void Renderer::renderActive(const RenderPlan& plan, const VoicePlan& voice, VoiceState& state, int64_t deviceFrame, int32_t frames,
                            float* stereoOut) {
    state.endFrame = voice.endFrame.value_or(kNoEnd);
    const double levelEnd = voiceLevel(plan, voice, deviceFrame + frames);
    const bool audible = usableLevel(levelEnd);
    const double levelStart = audible ? state.level : 0.0;
    const double levelStep = audible ? (levelEnd - state.level) / static_cast<double>(frames) : 0.0;
    if (audible) state.level = levelEnd;
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
    state.blockStart = state.position;
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

void Renderer::applyBusGains(const RenderPlan& plan, int64_t deviceFrame, int32_t frames, float* stereoOut) {
    if (plan.masterGain != masterTarget_) {
        masterFrom_ = master_;
        masterTarget_ = plan.masterGain;
        masterRampDone_ = 0;
        masterRampFrames_ = unusableScale(plan.masterRampSeconds) ? 0 : std::llround(plan.masterRampSeconds * kOutputRate);
        if (masterRampFrames_ <= 0) master_ = masterTarget_;
    }
    const double backingTarget = plan.backingGain;
    const double busPeak = std::max(std::fabs(backing_), std::fabs(backingTarget)) * std::max(std::fabs(master_), std::fabs(masterTarget_));
    for (int32_t i = 0; i < frames; ++i) {
        backing_ += (backingTarget - backing_) * kBackingCoefficient;
        if (std::fabs(backingTarget - backing_) < kBackingSnap) backing_ = backingTarget;
        const auto gain = static_cast<float>(backing_);
        stereoOut[kChannels * i] *= gain;
        stereoOut[kChannels * i + 1] *= gain;
    }
    mixClicks(plan, deviceFrame, frames, stereoOut);
    for (int32_t i = 0; i < frames; ++i) {
        if (masterRampDone_ < masterRampFrames_) {
            ++masterRampDone_;
            master_ = masterRampDone_ == masterRampFrames_
                          ? masterTarget_
                          : masterFrom_ + (masterTarget_ - masterFrom_) * static_cast<double>(masterRampDone_) /
                                              static_cast<double>(masterRampFrames_);
        }
        const auto gain = static_cast<float>(master_);
        stereoOut[kChannels * i] *= gain;
        stereoOut[kChannels * i + 1] *= gain;
    }
    const bool muted = !usableLevel(busPeak);
    for (int32_t i = 0; i < kChannels * frames; ++i) {
        if (muted || !std::isfinite(stereoOut[i])) stereoOut[i] = 0.0F;
    }
}

void Renderer::mixClicks(const RenderPlan& plan, int64_t deviceFrame, int32_t frames, float* stereoOut) {
    const float gain = plan.clickGain;
    int32_t done = 0;
    const auto emit = [this, &done, deviceFrame, gain, stereoOut](int32_t offset, bool accent) {
        clicks_.mix(deviceFrame, done, offset, gain, stereoOut);
        clicks_.trigger(deviceFrame + offset, accent);
        done = offset;
    };
    if (plan.test) {
        testClicks(plan, deviceFrame, frames, emit);
    } else if (plan.grid) {
        const VoiceState* source = activeSlot(plan.grid->voice);
        if (source != nullptr && source->entered) gridClicks(*plan.grid, *source, frames, emit);
    }
    clicks_.mix(deviceFrame, done, frames, gain, stereoOut);
}

}
