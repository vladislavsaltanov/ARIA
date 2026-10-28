#include "engine_core.h"

#include <gtest/gtest.h>

#include <cstdint>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

namespace {

using aria::ChunkKey;
using aria::EngineCore;
using aria::kChunkFrames;
using aria::kPoolSlots;

constexpr int32_t kFrames = 64;
constexpr size_t kBytesPerFrame = 4;

ChunkKey keyOf(int64_t n) { return {n, ~n, static_cast<int32_t>(n & 0xFFFF)}; }

std::vector<uint8_t> pcm(int32_t frames, uint8_t value) {
    return std::vector<uint8_t>(static_cast<size_t>(frames) * kBytesPerFrame, value);
}

bool write(EngineCore& core, const ChunkKey& key, const std::vector<uint8_t>& bytes, int32_t frames) {
    return core.writeChunk(key, bytes.data(), bytes.size(), frames);
}

std::vector<uint8_t> validPlanBytes() {
    std::ifstream file(std::string(ARIA_FIXTURES_DIR) + "/plan-v1.bin", std::ios::binary);
    return std::vector<uint8_t>(std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>());
}

void fillPool(EngineCore& core, int64_t first, int count) {
    for (int i = 0; i < count; ++i) ASSERT_TRUE(write(core, keyOf(first + i), pcm(kFrames, 1), kFrames));
}

TEST(EngineCoreTest, writeStoresTheBytesAsInterleavedLittleEndianSamples) {
    EngineCore core;
    std::vector<uint8_t> bytes = {0x01, 0x80, 0xFF, 0x7F, 0x34, 0x12, 0x00, 0x00};
    ASSERT_TRUE(core.writeChunk(keyOf(1), bytes.data(), bytes.size(), 2));
    const aria::Chunk* chunk = core.pool().find(keyOf(1));
    ASSERT_NE(nullptr, chunk);
    EXPECT_EQ(2, chunk->frames);
    EXPECT_FALSE(chunk->empty);
    EXPECT_EQ(static_cast<int16_t>(0x8001), chunk->data[0]);
    EXPECT_EQ(0x7FFF, chunk->data[1]);
    EXPECT_EQ(0x1234, chunk->data[2]);
    EXPECT_EQ(0, chunk->data[3]);
}

TEST(EngineCoreTest, writeAcceptsAFullChunkAndAZeroFrameChunk) {
    EngineCore core;
    EXPECT_TRUE(write(core, keyOf(1), pcm(kChunkFrames, 7), kChunkFrames));
    EXPECT_EQ(kChunkFrames, core.pool().find(keyOf(1))->frames);
    EXPECT_TRUE(core.writeChunk(keyOf(2), nullptr, 0, 0));
    ASSERT_NE(nullptr, core.pool().find(keyOf(2)));
    EXPECT_EQ(0, core.pool().find(keyOf(2))->frames);
}

TEST(EngineCoreTest, writeIsIdempotentForAResidentKeyAndKeepsTheFirstPayload) {
    EngineCore core;
    ASSERT_TRUE(write(core, keyOf(1), pcm(kFrames, 1), kFrames));
    for (int i = 0; i < 3 * kPoolSlots; ++i) ASSERT_TRUE(write(core, keyOf(1), pcm(kFrames, 9), kFrames));
    EXPECT_EQ(0x0101, core.pool().find(keyOf(1))->data[0]);
    fillPool(core, 100, kPoolSlots - 1);
}

TEST(EngineCoreTest, writeOfAKeyThatIsRetiredButStillVisibleLoadsAFreshCopy) {
    EngineCore core;
    ASSERT_TRUE(write(core, keyOf(1), pcm(kFrames, 1), kFrames));
    core.retainChunks({});
    ASSERT_NE(nullptr, core.pool().find(keyOf(1)));
    ASSERT_FALSE(core.pool().isReady(keyOf(1)));
    ASSERT_TRUE(write(core, keyOf(1), pcm(kFrames, 2), kFrames));
    EXPECT_TRUE(core.pool().isReady(keyOf(1)));
    EXPECT_EQ(0x0202, core.pool().find(keyOf(1))->data[0]);
}

TEST(EngineCoreTest, writeRejectsFramesBeyondTheChunkCapacityWithoutClaimingASlot) {
    EngineCore core;
    EXPECT_FALSE(write(core, keyOf(1), pcm(kChunkFrames + 1, 1), kChunkFrames + 1));
    EXPECT_EQ(nullptr, core.pool().find(keyOf(1)));
    fillPool(core, 100, kPoolSlots);
}

TEST(EngineCoreTest, writeRejectsNegativeFramesAndMissingBytesWithoutClaimingASlot) {
    EngineCore core;
    EXPECT_FALSE(core.writeChunk(keyOf(1), nullptr, 0, -1));
    EXPECT_FALSE(core.writeChunk(keyOf(1), nullptr, 8, 2));
    EXPECT_EQ(nullptr, core.pool().find(keyOf(1)));
    fillPool(core, 100, kPoolSlots);
}

TEST(EngineCoreTest, writeRejectsASizeThatDoesNotMatchTheFramesWithoutClaimingASlot) {
    EngineCore core;
    const std::vector<uint8_t> bytes = pcm(kFrames, 1);
    EXPECT_FALSE(core.writeChunk(keyOf(1), bytes.data(), bytes.size() - 1, kFrames));
    EXPECT_FALSE(core.writeChunk(keyOf(1), bytes.data(), bytes.size() + 1, kFrames));
    EXPECT_FALSE(core.writeChunk(keyOf(1), bytes.data(), bytes.size(), kFrames - 1));
    EXPECT_FALSE(core.writeChunk(keyOf(1), bytes.data(), 0, kFrames));
    EXPECT_EQ(nullptr, core.pool().find(keyOf(1)));
    fillPool(core, 100, kPoolSlots);
}

TEST(EngineCoreTest, writeReturnsFalseWhenNoSlotIsFreeAndSucceedsOnceOneIsReclaimable) {
    EngineCore core;
    fillPool(core, 100, kPoolSlots);
    EXPECT_FALSE(write(core, keyOf(1), pcm(kFrames, 1), kFrames));
    EXPECT_EQ(nullptr, core.pool().find(keyOf(1)));
    core.retainChunks({keyOf(101)});
    core.slot().acquireForCallback(core.pool());
    EXPECT_TRUE(write(core, keyOf(1), pcm(kFrames, 1), kFrames));
    EXPECT_TRUE(core.pool().isReady(keyOf(1)));
}

TEST(EngineCoreTest, markEmptyYieldsAnEmptyChunkAndIsIdempotent) {
    EngineCore core;
    core.markChunkEmpty(keyOf(1));
    ASSERT_NE(nullptr, core.pool().find(keyOf(1)));
    EXPECT_TRUE(core.pool().find(keyOf(1))->empty);
    for (int i = 0; i < 3 * kPoolSlots; ++i) core.markChunkEmpty(keyOf(1));
    fillPool(core, 100, kPoolSlots - 1);
}

TEST(EngineCoreTest, markEmptyDoesNotReplaceAResidentChunk) {
    EngineCore core;
    ASSERT_TRUE(write(core, keyOf(1), pcm(kFrames, 1), kFrames));
    core.markChunkEmpty(keyOf(1));
    EXPECT_FALSE(core.pool().find(keyOf(1))->empty);
}

TEST(EngineCoreTest, markEmptyWithAFullPoolIsHarmless) {
    EngineCore core;
    fillPool(core, 100, kPoolSlots);
    core.markChunkEmpty(keyOf(1));
    EXPECT_EQ(nullptr, core.pool().find(keyOf(1)));
}

TEST(EngineCoreTest, publishPlanReturnsTrueForAValidPlanAndFalseForGarbage) {
    EngineCore core;
    const std::vector<uint8_t> plan = validPlanBytes();
    ASSERT_FALSE(plan.empty());
    EXPECT_TRUE(core.publishPlan(plan.data(), plan.size()));
    EXPECT_NE(nullptr, core.slot().acquireForCallback(core.pool()));
    const std::vector<uint8_t> garbage(plan.size(), 0xAB);
    EXPECT_FALSE(core.publishPlan(garbage.data(), garbage.size()));
    EXPECT_FALSE(core.publishPlan(plan.data(), plan.size() / 2));
    EXPECT_FALSE(core.publishPlan(nullptr, 0));
}

TEST(EngineCoreTest, aRejectedPlanLeavesThePreviousPlanPublished) {
    EngineCore core;
    const std::vector<uint8_t> plan = validPlanBytes();
    ASSERT_TRUE(core.publishPlan(plan.data(), plan.size()));
    const aria::RenderPlan* before = core.slot().acquireForCallback(core.pool());
    const std::vector<uint8_t> garbage(8, 0xAB);
    ASSERT_FALSE(core.publishPlan(garbage.data(), garbage.size()));
    EXPECT_EQ(before, core.slot().acquireForCallback(core.pool()));
}

TEST(EngineCoreTest, aRetainedChunkStaysReadableAcrossPlanPublications) {
    EngineCore core;
    const std::vector<uint8_t> plan = validPlanBytes();
    ASSERT_TRUE(write(core, keyOf(1), pcm(kFrames, 1), kFrames));
    core.retainChunks({keyOf(1)});
    for (int i = 0; i < 5; ++i) {
        ASSERT_TRUE(core.publishPlan(plan.data(), plan.size()));
        core.slot().acquireForCallback(core.pool());
        EXPECT_TRUE(core.pool().isReady(keyOf(1)));
    }
}

TEST(EngineCoreTest, publishPlanRetainsTheKeysOfTheLastRetainCall) {
    EngineCore core;
    const std::vector<uint8_t> plan = validPlanBytes();
    ASSERT_TRUE(write(core, keyOf(1), pcm(kFrames, 1), kFrames));
    core.retainChunks({keyOf(1)});
    ASSERT_TRUE(write(core, keyOf(2), pcm(kFrames, 1), kFrames));
    ASSERT_TRUE(core.pool().isReady(keyOf(2)));
    ASSERT_TRUE(core.publishPlan(plan.data(), plan.size()));
    EXPECT_TRUE(core.pool().isReady(keyOf(1)));
    EXPECT_FALSE(core.pool().isReady(keyOf(2)));
}

TEST(EngineCoreTest, aChunkDroppedFromTheRetainSetBecomesInvisibleOnceTheCallbackAcks) {
    EngineCore core;
    ASSERT_TRUE(write(core, keyOf(1), pcm(kFrames, 1), kFrames));
    core.retainChunks({});
    EXPECT_NE(nullptr, core.pool().find(keyOf(1)));
    core.slot().acquireForCallback(core.pool());
    EXPECT_EQ(nullptr, core.pool().find(keyOf(1)));
}

}
