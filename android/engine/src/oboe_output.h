#pragma once

#include "pcm_pool.h"
#include "plan_slot.h"
#include "renderer.h"

#include <oboe/Oboe.h>

#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <optional>

namespace aria {

struct OutputTimestamp {
    int64_t framePosition;
    int64_t monotonicNs;
};

class OboeOutput final : public oboe::AudioStreamDataCallback,
                         public oboe::AudioStreamErrorCallback,
                         public std::enable_shared_from_this<OboeOutput> {
public:
    static constexpr int32_t kSampleRate = 48000;
    static constexpr int32_t kStartBursts = 2;
    static constexpr int kReopenAttempts = 3;
    static constexpr int kReopenPauseMs = 250;

    OboeOutput(PlanSlot& slot, PcmPool& pool, Renderer& renderer);
    OboeOutput(const OboeOutput&) = delete;
    OboeOutput& operator=(const OboeOutput&) = delete;

    bool restart();
    void stop();
    std::optional<OutputTimestamp> timestamp();
    int32_t xrunCount();
    void growBuffer();
    int32_t consumeReopenCount();
    void simulateDisconnectForTests();

    oboe::DataCallbackResult onAudioReady(oboe::AudioStream* stream, void* audioData, int32_t numFrames) override;
    void onErrorAfterClose(oboe::AudioStream* stream, oboe::Result error) override;

private:
    bool openAndStartLocked();
    void closeLocked();
    bool reopenOnce(const oboe::AudioStream* failed);
    void refreshXrunsLocked();

    PlanSlot& slot_;
    PcmPool& pool_;
    Renderer& renderer_;

    std::mutex streamMutex_;
    std::shared_ptr<oboe::AudioStream> stream_;
    bool wantRunning_ = false;
    int32_t xrunsOfClosedStreams_ = 0;

    std::atomic<int32_t> lastXrunCount_{0};
    std::atomic<int32_t> reopens_{0};
    int64_t deviceFrame_ = 0;
};

}
