#pragma once

#include "click.h"
#include "pcm_pool.h"
#include "plan.h"
#include "voice_state.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <optional>

namespace aria {

struct VoiceProbe {
    double position;
    double ratio;
    bool stopping;
};

class Renderer {
public:
    static constexpr int kMaxVoices = 8;
    static constexpr int32_t kFadeInFrames = 480;
    static constexpr int32_t kStopFrames = 384;
    static constexpr double kOutputRate = 48000.0;
    static constexpr double kBackingTauSeconds = 0.02;
    static constexpr double kReanchorSeconds = 0.005;
    static constexpr double kDriftWindowSeconds = 0.1;
    static constexpr double kMaxRatioDeviation = 5e-4;
    static constexpr double kDeadZoneFrames = 1e-3;

    explicit Renderer(const PcmPool& pool);
    Renderer(const Renderer&) = delete;
    Renderer& operator=(const Renderer&) = delete;

    void render(const RenderPlan* plan, int64_t deviceFrame, int32_t frames, float* stereoOut);
    uint64_t underruns() const;
    std::optional<VoiceProbe> probe(int32_t voiceId) const;

private:
    using PlanOfSlot = std::array<const VoicePlan*, kMaxVoices>;

    void assignVoices(const RenderPlan& plan, int64_t deviceFrame, PlanOfSlot& planOf);
    VoiceState* claimSlot();
    const VoiceState* activeSlot(int32_t voiceId) const;
    void renderActive(const RenderPlan& plan, const VoicePlan& voice, VoiceState& state, int64_t deviceFrame, int32_t frames,
                      float* stereoOut);
    void renderStopping(VoiceState& state, int32_t frames, float* stereoOut);
    void applyBusGains(const RenderPlan& plan, int64_t deviceFrame, int32_t frames, float* stereoOut);
    void mixClicks(const RenderPlan& plan, int64_t deviceFrame, int32_t frames, float* stereoOut);

    const PcmPool& pool_;
    std::array<VoiceState, kMaxVoices> voices_{};
    ClickSynth clicks_;
    std::atomic<uint64_t> underruns_{0};
    bool primed_ = false;
    double backing_ = 0.0;
    double master_ = 0.0;
    double masterFrom_ = 0.0;
    double masterTarget_ = 0.0;
    int64_t masterRampFrames_ = 0;
    int64_t masterRampDone_ = 0;
};

}
