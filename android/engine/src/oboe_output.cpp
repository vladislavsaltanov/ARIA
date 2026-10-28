#include "oboe_output.h"

#include <algorithm>
#include <ctime>
#include <thread>

namespace aria {

OboeOutput::OboeOutput(PlanSlot& slot, PcmPool& pool, Renderer& renderer) : slot_(slot), pool_(pool), renderer_(renderer) {}

bool OboeOutput::restart() {
    std::lock_guard<std::mutex> lock(streamMutex_);
    closeLocked();
    wantRunning_ = true;
    if (openAndStartLocked()) return true;
    wantRunning_ = false;
    return false;
}

void OboeOutput::stop() {
    std::lock_guard<std::mutex> lock(streamMutex_);
    wantRunning_ = false;
    closeLocked();
}

bool OboeOutput::openAndStartLocked() {
    const std::shared_ptr<OboeOutput> self = weak_from_this().lock();
    if (!self) return false;
    oboe::AudioStreamBuilder builder;
    builder.setDirection(oboe::Direction::Output)
        ->setPerformanceMode(oboe::PerformanceMode::LowLatency)
        ->setSharingMode(oboe::SharingMode::Shared)
        ->setUsage(oboe::Usage::Media)
        ->setContentType(oboe::ContentType::Music)
        ->setFormat(oboe::AudioFormat::Float)
        ->setChannelCount(oboe::ChannelCount::Stereo)
        ->setSampleRate(kSampleRate)
        ->setSampleRateConversionQuality(oboe::SampleRateConversionQuality::Medium)
        ->setDataCallback(self)
        ->setErrorCallback(self);
    deviceFrame_ = 0;
    std::shared_ptr<oboe::AudioStream> opened;
    if (builder.openStream(opened) != oboe::Result::OK) return false;
    opened->setBufferSizeInFrames(kStartBursts * opened->getFramesPerBurst());
    if (opened->requestStart() != oboe::Result::OK) {
        opened->close();
        return false;
    }
    stream_ = std::move(opened);
    return true;
}

void OboeOutput::closeLocked() {
    reopen_.cancel();
    if (!stream_) return;
    refreshXrunsLocked();
    xrunsOfClosedStreams_ = lastXrunCount_.load(std::memory_order_relaxed);
    stream_->stop();
    stream_->close();
    stream_.reset();
}

std::optional<OutputTimestamp> OboeOutput::timestamp() {
    std::unique_lock<std::mutex> lock(streamMutex_, std::try_to_lock);
    if (!lock.owns_lock() || !stream_) return std::nullopt;
    const auto result = stream_->getTimestamp(CLOCK_MONOTONIC);
    if (result.error() != oboe::Result::OK) return std::nullopt;
    const oboe::FrameTimestamp value = result.value();
    return OutputTimestamp{value.position, value.timestamp};
}

void OboeOutput::refreshXrunsLocked() {
    const auto result = stream_->getXRunCount();
    if (result.error() == oboe::Result::OK) {
        lastXrunCount_.store(xrunsOfClosedStreams_ + result.value(), std::memory_order_relaxed);
    }
}

int32_t OboeOutput::xrunCount() {
    std::unique_lock<std::mutex> lock(streamMutex_, std::try_to_lock);
    if (lock.owns_lock() && stream_) refreshXrunsLocked();
    return lastXrunCount_.load(std::memory_order_relaxed);
}

void OboeOutput::growBuffer() {
    std::unique_lock<std::mutex> lock(streamMutex_, std::try_to_lock);
    if (!lock.owns_lock() || !stream_) return;
    const int32_t grown = std::min(stream_->getBufferSizeInFrames() + stream_->getFramesPerBurst(),
                                   stream_->getBufferCapacityInFrames());
    stream_->setBufferSizeInFrames(grown);
}

#ifdef ARIA_TEST_HOOKS
void OboeOutput::simulateDisconnectForTests(int failedReopenAttempts) {
    std::shared_ptr<oboe::AudioStream> failed;
    {
        std::lock_guard<std::mutex> lock(streamMutex_);
        if (!stream_) return;
        failed = stream_;
        failReopenAttempts_.store(failedReopenAttempts, std::memory_order_relaxed);
        failed->close();
    }
    std::thread([self = shared_from_this(), failed] { self->onErrorAfterClose(failed.get(), oboe::Result::ErrorDisconnected); })
        .detach();
}
#endif

int32_t OboeOutput::consumeReopenCount() { return reopen_.consumeReopens(); }

oboe::DataCallbackResult OboeOutput::onAudioReady(oboe::AudioStream*, void* audioData, int32_t numFrames) {
    const RenderPlan* plan = slot_.acquireForCallback(pool_);
    renderer_.render(plan, deviceFrame_, numFrames, static_cast<float*>(audioData));
    deviceFrame_ += numFrames;
    return oboe::DataCallbackResult::Continue;
}

void OboeOutput::onErrorAfterClose(oboe::AudioStream* failed, oboe::Result) {
    {
        std::lock_guard<std::mutex> lock(streamMutex_);
        if (!wantRunning_ || stream_.get() != failed) return;
        xrunsOfClosedStreams_ = lastXrunCount_.load(std::memory_order_relaxed);
        stream_.reset();
    }
    reopen_.runUntilReopenedOrCancelled([this] { return reopenAttempt(); });
}

ReopenRetry::Outcome OboeOutput::reopenAttempt() {
    std::lock_guard<std::mutex> lock(streamMutex_);
    if (!wantRunning_ || stream_) return ReopenRetry::Outcome::Abandoned;
#ifdef ARIA_TEST_HOOKS
    if (failReopenAttempts_.fetch_sub(1, std::memory_order_relaxed) > 0) return ReopenRetry::Outcome::Failed;
    failReopenAttempts_.store(0, std::memory_order_relaxed);
#endif
    return openAndStartLocked() ? ReopenRetry::Outcome::Reopened : ReopenRetry::Outcome::Failed;
}

}
