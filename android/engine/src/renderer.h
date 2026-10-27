#pragma once

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
};

class Renderer {
public:
    static constexpr int kMaxVoices = 8;
    static constexpr int32_t kFadeInFrames = 480;
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
    VoiceState* stateFor(const VoicePlan& voice);
    void renderVoice(const RenderPlan& plan, const VoicePlan& voice, VoiceState& state, int64_t deviceFrame, int32_t frames,
                     float* stereoOut);

    const PcmPool& pool_;
    std::array<VoiceState, kMaxVoices> voices_{};
    std::atomic<uint64_t> underruns_{0};
};

}
