#include "pcm_pool.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <limits>
#include <set>
#include <thread>
#include <vector>

namespace {

using aria::Chunk;
using aria::ChunkKey;
using aria::PcmPool;

constexpr int kSlots = 30;
constexpr int32_t kStressFrames = 64;
constexpr int64_t kStressIterations = 100000;
constexpr int kHoldCycles = 200;
constexpr int64_t kProbedKeys = 2;
constexpr int64_t kPublishesPastHeldChunkWithoutProtection = 40;

ChunkKey keyOf(int64_t n) { return {n, ~n, static_cast<int32_t>(n & 0xFFFF)}; }

int16_t patternOf(int64_t n) { return static_cast<int16_t>(n & 0x7FFF); }

int publishFilled(PcmPool& pool, const ChunkKey& key, int16_t value, int32_t frames) {
    const int slot = pool.claim(key);
    if (slot < 0) return slot;
    int16_t* data = pool.slotData(slot);
    std::fill(data, data + 2 * static_cast<size_t>(frames), value);
    pool.publish(slot, frames);
    return slot;
}

bool chunkHolds(const Chunk& chunk, int16_t value) {
    if (chunk.empty || chunk.frames != kStressFrames) return false;
    return std::all_of(chunk.data, chunk.data + 2 * chunk.frames, [value](int16_t sample) { return sample == value; });
}

TEST(PcmPoolTest, findIsNullUntilPublished) {
    PcmPool pool;
    const ChunkKey key = keyOf(1);
    EXPECT_EQ(nullptr, pool.find(key));
    const int slot = pool.claim(key);
    ASSERT_GE(slot, 0);
    EXPECT_EQ(nullptr, pool.find(key));
    pool.publish(slot, 10);
    EXPECT_NE(nullptr, pool.find(key));
}

TEST(PcmPoolTest, publishThenFindReturnsFrames) {
    PcmPool pool;
    const ChunkKey key = keyOf(2);
    const int slot = pool.claim(key);
    ASSERT_GE(slot, 0);
    int16_t* data = pool.slotData(slot);
    for (int frame = 0; frame < 100; ++frame) {
        data[2 * frame] = static_cast<int16_t>(frame);
        data[2 * frame + 1] = static_cast<int16_t>(-frame);
    }
    pool.publish(slot, 100);

    const Chunk* chunk = pool.find(key);
    ASSERT_NE(nullptr, chunk);
    EXPECT_EQ(100, chunk->frames);
    EXPECT_FALSE(chunk->empty);
    EXPECT_EQ(data, chunk->data);
    EXPECT_EQ(99, chunk->data[2 * 99]);
    EXPECT_EQ(-99, chunk->data[2 * 99 + 1]);
}

TEST(PcmPoolTest, findDistinguishesEveryKeyField) {
    PcmPool pool;
    const ChunkKey key{10, 20, 30};
    ASSERT_GE(publishFilled(pool, key, 1, 4), 0);
    EXPECT_NE(nullptr, pool.find({10, 20, 30}));
    EXPECT_EQ(nullptr, pool.find({11, 20, 30}));
    EXPECT_EQ(nullptr, pool.find({10, 21, 30}));
    EXPECT_EQ(nullptr, pool.find({10, 20, 31}));
}

TEST(PcmPoolTest, publishEmptyYieldsEmptyChunk) {
    PcmPool pool;
    const ChunkKey key = keyOf(3);
    const int slot = pool.claim(key);
    ASSERT_GE(slot, 0);
    pool.publishEmpty(slot);

    const Chunk* chunk = pool.find(key);
    ASSERT_NE(nullptr, chunk);
    EXPECT_TRUE(chunk->empty);
    EXPECT_EQ(0, chunk->frames);
}

TEST(PcmPoolTest, publishAcceptsChunkShorterThanFullAndZeroFrames) {
    PcmPool pool;
    ASSERT_GE(publishFilled(pool, keyOf(4), 7, 1), 0);
    const int zero = pool.claim(keyOf(5));
    ASSERT_GE(zero, 0);
    pool.publish(zero, 0);

    const Chunk* one = pool.find(keyOf(4));
    ASSERT_NE(nullptr, one);
    EXPECT_EQ(1, one->frames);
    EXPECT_FALSE(one->empty);
    const Chunk* none = pool.find(keyOf(5));
    ASSERT_NE(nullptr, none);
    EXPECT_EQ(0, none->frames);
    EXPECT_FALSE(none->empty);
}

TEST(PcmPoolTest, everySlotHoldsAFullChunkWithoutOverlap) {
    PcmPool pool;
    for (int n = 0; n < kSlots; ++n) {
        const int slot = pool.claim(keyOf(n));
        ASSERT_GE(slot, 0);
        int16_t* data = pool.slotData(slot);
        data[0] = static_cast<int16_t>(n);
        data[2 * aria::kChunkFrames - 1] = static_cast<int16_t>(-n);
        pool.publish(slot, aria::kChunkFrames);
    }
    for (int n = 0; n < kSlots; ++n) {
        const Chunk* chunk = pool.find(keyOf(n));
        ASSERT_NE(nullptr, chunk);
        EXPECT_EQ(aria::kChunkFrames, chunk->frames);
        EXPECT_EQ(n, chunk->data[0]);
        EXPECT_EQ(-n, chunk->data[2 * aria::kChunkFrames - 1]);
    }
}

TEST(PcmPoolTest, publishClampsFramesIntoChunkCapacity) {
    PcmPool pool;
    const int over = pool.claim(keyOf(6));
    const int under = pool.claim(keyOf(7));
    ASSERT_GE(over, 0);
    ASSERT_GE(under, 0);
    pool.publish(over, aria::kChunkFrames + 1);
    pool.publish(under, -5);
    EXPECT_EQ(aria::kChunkFrames, pool.find(keyOf(6))->frames);
    EXPECT_EQ(0, pool.find(keyOf(7))->frames);
}

TEST(PcmPoolTest, publishOnOutOfRangeOrUnclaimedSlotIsIgnored) {
    PcmPool pool;
    EXPECT_EQ(nullptr, pool.slotData(-1));
    EXPECT_EQ(nullptr, pool.slotData(kSlots));
    pool.publish(-1, 1);
    pool.publish(kSlots, 1);
    pool.publishEmpty(-1);
    pool.publishEmpty(kSlots);

    ASSERT_EQ(0, pool.claim(keyOf(1)));
    pool.publish(5, 1);
    pool.publishEmpty(6);
    for (int n = 2; n <= kSlots; ++n) ASSERT_GE(pool.claim(keyOf(n)), 0) << "claim " << n;
}

TEST(PcmPoolTest, secondPublishOfTheSameClaimIsIgnored) {
    PcmPool pool;
    const int slot = pool.claim(keyOf(8));
    ASSERT_GE(slot, 0);
    pool.publish(slot, 3);
    pool.publish(slot, 7);
    pool.publishEmpty(slot);
    const Chunk* chunk = pool.find(keyOf(8));
    ASSERT_NE(nullptr, chunk);
    EXPECT_EQ(3, chunk->frames);
    EXPECT_FALSE(chunk->empty);
}

TEST(PcmPoolTest, claimFailsWhenAll30SlotsBusy) {
    PcmPool pool;
    std::set<int> slots;
    for (int n = 0; n < kSlots; ++n) {
        const int slot = pool.claim(keyOf(n));
        ASSERT_GE(slot, 0) << "claim " << n;
        ASSERT_LT(slot, kSlots);
        slots.insert(slot);
    }
    EXPECT_EQ(static_cast<size_t>(kSlots), slots.size());
    EXPECT_EQ(-1, pool.claim(keyOf(kSlots)));
}

TEST(PcmPoolTest, retiredSlotNotReusedUntilAckAdvances) {
    PcmPool pool;
    for (int n = 0; n < kSlots; ++n) ASSERT_GE(publishFilled(pool, keyOf(n), 1, 4), 0);
    pool.retain({}, 1);

    EXPECT_EQ(-1, pool.claim(keyOf(100)));
    pool.ackGeneration(0);
    EXPECT_EQ(-1, pool.claim(keyOf(100)));
    pool.ackGeneration(1);
    EXPECT_GE(pool.claim(keyOf(100)), 0);
}

TEST(PcmPoolTest, retainWithoutLaterPublicationBecomesReclaimableAfterAck) {
    PcmPool pool;
    for (int n = 0; n < kSlots; ++n) ASSERT_GE(publishFilled(pool, keyOf(n), 1, 4), 0);
    pool.retain({}, 5);
    pool.ackGeneration(5);

    for (int n = 0; n < kSlots; ++n) ASSERT_GE(pool.claim(keyOf(100 + n)), 0) << "claim " << n;
    EXPECT_EQ(-1, pool.claim(keyOf(200)));
}

TEST(PcmPoolTest, retainStampedWithAlreadyAckedGenerationIsReclaimableImmediately) {
    PcmPool pool;
    for (int n = 0; n < kSlots; ++n) ASSERT_GE(publishFilled(pool, keyOf(n), 1, 4), 0);
    pool.ackGeneration(7);
    pool.retain({}, 7);

    EXPECT_GE(pool.claim(keyOf(100)), 0);
    EXPECT_EQ(nullptr, pool.find(keyOf(0)));
}

TEST(PcmPoolTest, extremeGenerationsStayRetiredUntilAcked) {
    constexpr int64_t kLowest = std::numeric_limits<int64_t>::min();
    constexpr int64_t kHighest = std::numeric_limits<int64_t>::max();
    for (const int64_t extreme : {kLowest, kHighest}) {
        PcmPool pool;
        for (int n = 0; n < kSlots; ++n) ASSERT_GE(publishFilled(pool, keyOf(n), 1, 4), 0);
        pool.retain({}, extreme);

        EXPECT_EQ(-1, pool.claim(keyOf(100))) << "generation " << extreme;
        EXPECT_NE(nullptr, pool.find(keyOf(0))) << "generation " << extreme;
        pool.ackGeneration(kHighest);
        EXPECT_EQ(nullptr, pool.find(keyOf(0))) << "generation " << extreme;
        EXPECT_GE(pool.claim(keyOf(100)), 0) << "generation " << extreme;
    }
}

TEST(PcmPoolTest, blockThatBeganBeforeRetireKeepsItsChunkUntilItAcks) {
    PcmPool pool;
    const int heldSlot = publishFilled(pool, keyOf(0), 42, kStressFrames);
    ASSERT_GE(heldSlot, 0);
    pool.ackGeneration(0);
    const Chunk* held = pool.find(keyOf(0));
    ASSERT_NE(nullptr, held);

    pool.retain({keyOf(1)}, 1);
    for (int n = 1; n < kSlots; ++n) ASSERT_GE(publishFilled(pool, keyOf(n), 9, kStressFrames), 0);
    EXPECT_EQ(-1, pool.claim(keyOf(100)));
    EXPECT_TRUE(chunkHolds(*held, 42));
    EXPECT_NE(nullptr, pool.find(keyOf(0)));

    pool.ackGeneration(1);
    EXPECT_EQ(nullptr, pool.find(keyOf(0)));
    EXPECT_EQ(heldSlot, pool.claim(keyOf(100)));
}

TEST(PcmPoolTest, retainKeepsListedKeysAndRetiresTheRest) {
    PcmPool pool;
    ASSERT_GE(publishFilled(pool, keyOf(1), 1, 4), 0);
    ASSERT_GE(publishFilled(pool, keyOf(2), 2, 4), 0);
    pool.retain({keyOf(1)}, 1);

    EXPECT_NE(nullptr, pool.find(keyOf(1)));
    EXPECT_NE(nullptr, pool.find(keyOf(2)));
    pool.ackGeneration(1);
    EXPECT_NE(nullptr, pool.find(keyOf(1)));
    EXPECT_EQ(nullptr, pool.find(keyOf(2)));
}

TEST(PcmPoolTest, claimUsesEveryFreeSlotBeforeReclaimingARetiredOne) {
    PcmPool pool;
    const int retiredSlot = publishFilled(pool, keyOf(0), 1, 4);
    ASSERT_GE(retiredSlot, 0);
    pool.retain({}, 1);
    pool.ackGeneration(1);

    for (int n = 1; n < kSlots; ++n) {
        const int claimed = pool.claim(keyOf(n));
        ASSERT_GE(claimed, 0) << "claim " << n;
        EXPECT_NE(retiredSlot, claimed) << "claim " << n;
    }
    EXPECT_EQ(retiredSlot, pool.claim(keyOf(kSlots)));
    EXPECT_EQ(-1, pool.claim(keyOf(kSlots + 1)));
}

TEST(PcmPoolTest, retainNeverTurnsARetiredSlotBackIntoReady) {
    PcmPool pool;
    ASSERT_GE(publishFilled(pool, keyOf(1), 1, 4), 0);
    pool.retain({}, 1);
    pool.retain({keyOf(1)}, 2);

    EXPECT_NE(nullptr, pool.find(keyOf(1)));
    pool.ackGeneration(1);
    EXPECT_EQ(nullptr, pool.find(keyOf(1)));
    pool.ackGeneration(2);
    EXPECT_EQ(nullptr, pool.find(keyOf(1)));
}

TEST(PcmPoolTest, reWantedKeyIsReloadedIntoANewSlotWhileTheOldCopyIsUnacked) {
    PcmPool pool;
    pool.ackGeneration(0);
    const int oldSlot = publishFilled(pool, keyOf(1), 11, 4);
    ASSERT_GE(oldSlot, 0);
    pool.retain({}, 1);
    pool.retain({keyOf(1)}, 2);

    const int newSlot = publishFilled(pool, keyOf(1), 22, 4);
    ASSERT_GE(newSlot, 0);
    EXPECT_NE(oldSlot, newSlot);
    EXPECT_EQ(11, pool.slotData(oldSlot)[0]);
    const Chunk* found = pool.find(keyOf(1));
    ASSERT_NE(nullptr, found);
    EXPECT_EQ(pool.slotData(newSlot), found->data);
    EXPECT_EQ(22, found->data[0]);
}

TEST(PcmPoolTest, findPrefersTheReadyCopyOverAProtectedRetiredCopyInALowerSlot) {
    PcmPool pool;
    pool.ackGeneration(0);
    const int firstSlot = publishFilled(pool, keyOf(1), 11, 4);
    ASSERT_GE(firstSlot, 0);
    for (int n = 2; n < kSlots; ++n) ASSERT_GE(publishFilled(pool, keyOf(n), 5, 4), 0);
    pool.retain({}, 1);
    pool.retain({keyOf(1)}, 2);
    const int secondSlot = publishFilled(pool, keyOf(1), 22, 4);
    ASSERT_GE(secondSlot, 0);
    ASSERT_LT(firstSlot, secondSlot);
    EXPECT_EQ(22, pool.find(keyOf(1))->data[0]);
    EXPECT_EQ(pool.slotData(secondSlot), pool.find(keyOf(1))->data);
}

TEST(PcmPoolTest, oldCopyBecomesReclaimableCapacityOnceTheAckPassesItsStamp) {
    PcmPool pool;
    pool.ackGeneration(0);
    const int oldSlot = publishFilled(pool, keyOf(0), 1, 4);
    ASSERT_GE(oldSlot, 0);
    pool.retain({}, 1);
    pool.retain({keyOf(0)}, 2);
    const int newSlot = publishFilled(pool, keyOf(0), 2, 4);
    ASSERT_GE(newSlot, 0);
    for (int n = 1; n < kSlots - 1; ++n) ASSERT_GE(publishFilled(pool, keyOf(n), 3, 4), 0);
    EXPECT_EQ(-1, pool.claim(keyOf(100)));

    pool.ackGeneration(1);
    EXPECT_EQ(oldSlot, pool.claim(keyOf(100)));
    EXPECT_EQ(-1, pool.claim(keyOf(101)));
}

TEST(PcmPoolTest, repeatedDropAndReWantCyclesNeverExhaustAPoolWithOneSpareSlot) {
    constexpr int kWanted = kSlots - 1;
    constexpr int kCycles = 2000;
    PcmPool pool;
    std::vector<ChunkKey> wanted;
    std::vector<int16_t> values(kWanted);
    for (int n = 0; n < kWanted; ++n) {
        wanted.push_back(keyOf(n));
        values[n] = static_cast<int16_t>(n);
        ASSERT_GE(publishFilled(pool, wanted[n], values[n], 4), 0);
    }

    int64_t generation = 0;
    for (int cycle = 0; cycle < kCycles; ++cycle) {
        const int index = cycle % kWanted;
        std::vector<ChunkKey> without = wanted;
        without.erase(without.begin() + index);
        pool.retain(without, ++generation);
        pool.retain(wanted, ++generation);
        values[index] = static_cast<int16_t>(1000 + cycle);
        ASSERT_GE(publishFilled(pool, wanted[index], values[index], 4), 0) << "cycle " << cycle;
        pool.ackGeneration(generation);
        for (int n = 0; n < kWanted; ++n) {
            const Chunk* chunk = pool.find(wanted[n]);
            ASSERT_NE(nullptr, chunk) << "cycle " << cycle << " key " << n;
            ASSERT_EQ(values[n], chunk->data[0]) << "cycle " << cycle << " key " << n;
        }
    }
}

TEST(PcmPoolTest, isReadyIsTrueOnlyForAPublishedKeyThatIsNotRetired) {
    PcmPool pool;
    const ChunkKey key = keyOf(1);
    EXPECT_FALSE(pool.isReady(key));
    const int slot = pool.claim(key);
    ASSERT_GE(slot, 0);
    EXPECT_FALSE(pool.isReady(key));
    pool.publish(slot, kStressFrames);
    EXPECT_TRUE(pool.isReady(key));
    EXPECT_FALSE(pool.isReady(keyOf(2)));
    pool.retain({}, 5);
    EXPECT_FALSE(pool.isReady(key));
    EXPECT_NE(nullptr, pool.find(key));
}

TEST(PcmPoolTest, lowerAckDoesNotLowerTheReclaimHorizon) {
    PcmPool pool;
    for (int n = 0; n < kSlots; ++n) ASSERT_GE(publishFilled(pool, keyOf(n), 1, 4), 0);
    pool.retain({}, 5);
    pool.ackGeneration(5);
    ASSERT_EQ(nullptr, pool.find(keyOf(0)));

    pool.ackGeneration(2);
    pool.ackGeneration(std::numeric_limits<int64_t>::min());
    EXPECT_EQ(nullptr, pool.find(keyOf(0)));
    for (int n = 0; n < kSlots; ++n) EXPECT_GE(pool.claim(keyOf(100 + n)), 0) << "claim " << n;
}

class EngineEpoch {
public:
    int64_t load() const { return value_.load(std::memory_order_acquire); }

    void retainOnly(PcmPool& pool, const std::vector<ChunkKey>& keys) {
        const int64_t next = value_.load(std::memory_order_relaxed) + 1;
        pool.retain(keys, next);
        value_.store(next, std::memory_order_release);
    }

private:
    std::atomic<int64_t> value_{0};
};

struct Shared {
    PcmPool pool;
    EngineEpoch epoch;
    std::atomic<int64_t> latest{0};
    std::atomic<int64_t> stalledAfter{0};
    std::atomic<bool> stop{false};

    void beginBlock() { pool.ackGeneration(epoch.load()); }
};

void produce(Shared& shared, int64_t limit) {
    for (int64_t n = 1; n <= limit && !shared.stop.load(std::memory_order_acquire); ++n) {
        int slot = shared.pool.claim(keyOf(n));
        while (slot < 0) {
            if (shared.stop.load(std::memory_order_acquire)) return;
            shared.stalledAfter.store(n - 1, std::memory_order_relaxed);
            std::this_thread::yield();
            slot = shared.pool.claim(keyOf(n));
        }
        int16_t* data = shared.pool.slotData(slot);
        std::fill(data, data + 2 * static_cast<size_t>(kStressFrames), patternOf(n));
        shared.pool.publish(slot, kStressFrames);
        shared.latest.store(n, std::memory_order_relaxed);
        shared.epoch.retainOnly(shared.pool, {keyOf(n)});
    }
}

TEST(PcmPoolConcurrencyTest, findIsSafeFromReaderThreadWhileWriterPublishes) {
    Shared shared;
    std::atomic<bool> writerDone{false};
    std::atomic<int64_t> found{0};
    std::atomic<int64_t> corrupted{0};
    std::thread reader([&] {
        while (!writerDone.load(std::memory_order_acquire)) {
            shared.beginBlock();
            const int64_t newest = shared.latest.load(std::memory_order_relaxed) + 1;
            for (int64_t n = newest; n >= std::max<int64_t>(1, newest - kProbedKeys); --n) {
                const Chunk* chunk = shared.pool.find(keyOf(n));
                if (chunk == nullptr) continue;
                found.fetch_add(1, std::memory_order_relaxed);
                if (!chunkHolds(*chunk, patternOf(n))) corrupted.fetch_add(1, std::memory_order_relaxed);
                std::this_thread::yield();
                if (!chunkHolds(*chunk, patternOf(n))) corrupted.fetch_add(1, std::memory_order_relaxed);
            }
        }
    });

    produce(shared, kStressIterations);
    writerDone.store(true, std::memory_order_release);
    reader.join();

    EXPECT_EQ(0, corrupted.load());
    EXPECT_GT(found.load(), 0);
}

TEST(PcmPoolConcurrencyTest, readerBlockSpanningRetireNeverSeesItsChunkRewritten) {
    Shared shared;
    std::thread writer([&] { produce(shared, std::numeric_limits<int64_t>::max()); });

    int held = 0;
    int heldAcrossRetire = 0;
    int corrupted = 0;
    int timeouts = 0;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(60);
    while (held < kHoldCycles && std::chrono::steady_clock::now() < deadline) {
        shared.beginBlock();
        const int64_t n = shared.latest.load(std::memory_order_relaxed);
        if (n == 0) continue;
        const Chunk* chunk = shared.pool.find(keyOf(n));
        if (chunk == nullptr) continue;

        const auto blockDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (shared.stalledAfter.load(std::memory_order_relaxed) < n &&
               shared.latest.load(std::memory_order_relaxed) < n + kPublishesPastHeldChunkWithoutProtection) {
            if (std::chrono::steady_clock::now() > blockDeadline) {
                ++timeouts;
                break;
            }
            std::this_thread::yield();
        }
        if (shared.stalledAfter.load(std::memory_order_relaxed) > n) ++heldAcrossRetire;
        if (!chunkHolds(*chunk, patternOf(n))) ++corrupted;
        ++held;
    }
    shared.stop.store(true, std::memory_order_release);
    writer.join();

    EXPECT_EQ(kHoldCycles, held);
    EXPECT_GT(heldAcrossRetire, 0);
    EXPECT_EQ(0, corrupted);
    EXPECT_EQ(0, timeouts);
}

}
