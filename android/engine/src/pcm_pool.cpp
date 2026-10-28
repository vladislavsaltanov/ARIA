#include "pcm_pool.h"

#include <algorithm>

namespace aria {
namespace {

constexpr size_t kSlotSamples = static_cast<size_t>(kChunkFrames) * kChannels;

bool contains(const std::vector<ChunkKey>& keys, const ChunkKey& key) {
    return std::find(keys.begin(), keys.end(), key) != keys.end();
}

}

PcmPool::PcmPool() : arena_(new int16_t[kSlotSamples * kPoolSlots]) {}

bool PcmPool::isRetired(int64_t mark) { return mark >= kOldestRetiredStamp && mark <= kNewestRetiredStamp; }

bool PcmPool::owns(int slot) { return slot >= 0 && slot < kPoolSlots; }

int16_t* PcmPool::arenaOf(int slot) const { return arena_.get() + kSlotSamples * static_cast<size_t>(slot); }

int16_t* PcmPool::slotData(int slot) { return owns(slot) ? arenaOf(slot) : nullptr; }

int PcmPool::pickSlotToClaim() const {
    const int64_t acked = acked_.load(std::memory_order_acquire);
    int oldestRetired = -1;
    int64_t oldestStamp = 0;
    for (int i = 0; i < kPoolSlots; ++i) {
        const int64_t mark = slots_[i].mark.load(std::memory_order_relaxed);
        if (mark == kFreeMark) return i;
        if (!isRetired(mark) || mark > acked) continue;
        if (oldestRetired < 0 || mark < oldestStamp) {
            oldestRetired = i;
            oldestStamp = mark;
        }
    }
    return oldestRetired;
}

int PcmPool::claim(const ChunkKey& key) {
    const int index = pickSlotToClaim();
    if (index < 0) return -1;
    Slot& slot = slots_[index];
    slot.key = key;
    slot.chunk = Chunk{arenaOf(index), 0, false};
    slot.mark.store(kLoadingMark, std::memory_order_release);
    return index;
}

void PcmPool::settle(int slot, int32_t frames, bool empty) {
    if (!owns(slot) || slots_[slot].mark.load(std::memory_order_relaxed) != kLoadingMark) return;
    Slot& target = slots_[slot];
    target.chunk = Chunk{arenaOf(slot), std::clamp(frames, int32_t{0}, kChunkFrames), empty};
    target.mark.store(kReadyMark, std::memory_order_release);
}

void PcmPool::publish(int slot, int32_t frames) { settle(slot, frames, false); }

void PcmPool::publishEmpty(int slot) { settle(slot, 0, true); }

void PcmPool::retain(const std::vector<ChunkKey>& keys, int64_t generation) {
    const int64_t stamp = std::clamp(generation, kOldestRetiredStamp, kNewestRetiredStamp);
    for (Slot& slot : slots_) {
        if (slot.mark.load(std::memory_order_relaxed) != kReadyMark || contains(keys, slot.key)) continue;
        slot.mark.store(stamp, std::memory_order_release);
    }
}

const Chunk* PcmPool::find(const ChunkKey& key) const {
    const int64_t acked = acked_.load(std::memory_order_relaxed);
    const Chunk* retiredMatch = nullptr;
    for (const Slot& slot : slots_) {
        const int64_t mark = slot.mark.load(std::memory_order_acquire);
        if (mark == kReadyMark) {
            if (slot.key == key) return &slot.chunk;
        } else if (retiredMatch == nullptr && isRetired(mark) && mark > acked && slot.key == key) {
            retiredMatch = &slot.chunk;
        }
    }
    return retiredMatch;
}

bool PcmPool::isReady(const ChunkKey& key) const {
    return std::any_of(slots_.begin(), slots_.end(), [&key](const Slot& slot) {
        return slot.mark.load(std::memory_order_acquire) == kReadyMark && slot.key == key;
    });
}

int64_t PcmPool::ackedGeneration() const { return acked_.load(std::memory_order_acquire); }

void PcmPool::ackGeneration(int64_t seen) {
    if (seen > acked_.load(std::memory_order_relaxed)) acked_.store(seen, std::memory_order_release);
}

}
