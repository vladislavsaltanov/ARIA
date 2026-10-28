#include "oboe_output.h"

#include <algorithm>
#include <chrono>
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

void OboeOutput::simulateDisconnectForTests() {
    oboe::AudioStream* failed = nullptr;
    {
        std::lock_guard<std::mutex> lock(streamMutex_);
        if (!stream_) return;
        failed = stream_.get();
        stream_->close();
    }
    std::thread([self = shared_from_this(), failed] { self->onErrorAfterClose(failed, oboe::Result::ErrorDisconnected); }).detach();
}

int32_t OboeOutput::consumeReopenCount() { return reopens_.exchange(0, std::memory_order_acq_rel); }

oboe::DataCallbackResult OboeOutput::onAudioReady(oboe::AudioStream*, void* audioData, int32_t numFrames) {
    const RenderPlan* plan = slot_.acquireForCallback(pool_);
    renderer_.render(plan, deviceFrame_, numFrames, static_cast<float*>(audioData));
    deviceFrame_ += numFrames;
    return oboe::DataCallbackResult::Continue;
}

void OboeOutput::onErrorAfterClose(oboe::AudioStream* failed, oboe::Result) {
    for (int attempt = 0; attempt < kReopenAttempts; ++attempt) {
        if (attempt > 0) std::this_thread::sleep_for(std::chrono::milliseconds(kReopenPauseMs));
        if (reopenOnce(failed)) return;
    }
}

bool OboeOutput::reopenOnce(const oboe::AudioStream* failed) {
    std::lock_guard<std::mutex> lock(streamMutex_);
    if (!wantRunning_ || (stream_ && stream_.get() != failed)) return true;
    xrunsOfClosedStreams_ = lastXrunCount_.load(std::memory_order_relaxed);
    stream_.reset();
    if (!openAndStartLocked()) return false;
    reopens_.fetch_add(1, std::memory_order_acq_rel);
    return true;
}

}
