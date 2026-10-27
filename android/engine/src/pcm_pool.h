#pragma once

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <memory>
#include <vector>

namespace aria {

constexpr int32_t kChunkFrames = 96000;
constexpr int kPoolSlots = 30;
constexpr int kChannels = 2;

struct Chunk {
    const int16_t* data;
    int32_t frames;
    bool empty;
};

struct ChunkKey {
    int64_t trackHi;
    int64_t trackLo;
    int32_t chunk;
};

inline bool operator==(const ChunkKey& a, const ChunkKey& b) {
    return a.trackHi == b.trackHi && a.trackLo == b.trackLo && a.chunk == b.chunk;
}

inline bool operator!=(const ChunkKey& a, const ChunkKey& b) { return !(a == b); }

class PcmPool {
public:
    PcmPool();
    PcmPool(const PcmPool&) = delete;
    PcmPool& operator=(const PcmPool&) = delete;

    int claim(const ChunkKey& key);
    int16_t* slotData(int slot);
    void publish(int slot, int32_t frames);
    void publishEmpty(int slot);
    void retain(const std::vector<ChunkKey>& keys, int64_t generation);

    const Chunk* find(const ChunkKey& key) const;
    void ackGeneration(int64_t seen);

private:
    static constexpr int64_t kNothingAcked = std::numeric_limits<int64_t>::min();
    static constexpr int64_t kFreeMark = std::numeric_limits<int64_t>::min();
    static constexpr int64_t kLoadingMark = kFreeMark + 1;
    static constexpr int64_t kReadyMark = std::numeric_limits<int64_t>::max();
    static constexpr int64_t kOldestRetiredStamp = kLoadingMark + 1;
    static constexpr int64_t kNewestRetiredStamp = kReadyMark - 1;

    struct Slot {
        std::atomic<int64_t> mark{kFreeMark};
        ChunkKey key{};
        Chunk chunk{};
    };

    static_assert(std::atomic<int64_t>::is_always_lock_free, "slot marks and acks must be lock-free");

    static bool isRetired(int64_t mark);
    static bool owns(int slot);
    int16_t* arenaOf(int slot) const;
    void settle(int slot, int32_t frames, bool empty);
    int pickSlotToClaim() const;

    std::unique_ptr<int16_t[]> arena_;
    std::array<Slot, kPoolSlots> slots_;
    std::atomic<int64_t> acked_{kNothingAcked};
};

}
