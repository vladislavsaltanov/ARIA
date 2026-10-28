#include "counting_allocator.h"
#include "plan_slot.h"
#include "renderer_fixture.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <memory>
#include <thread>
#include <vector>

namespace aria::fixture {
namespace {

using test::freesOf;
using test::watchFreesOnThisThread;

constexpr int kSlots = 30;
constexpr int64_t kLiveAck = 1000;
constexpr int64_t kPublications = 100000;
constexpr int64_t kCallsPerControlThread = 20000;
constexpr int32_t kBlock = 192;
constexpr int32_t kSmallBlock = 64;
constexpr int32_t kStop = 384;
constexpr double kMaxDeviation = 5e-4;
constexpr double kRampStep = 1.0 / 32768.0;
constexpr double kRoundingSlack = 1e-6;
constexpr int kPinnedKeys = kSlots - 2;
constexpr int kAbsentKeys = 1000;
constexpr int32_t kHeldFrames = 64;
constexpr int kHoldCycles = 200;
constexpr int64_t kRotations = 20000;
constexpr int32_t kRotatingChunkFrames = 2048;
constexpr std::chrono::seconds kStressDeadline{20};
constexpr std::chrono::seconds kHoldDeadline{5};

ChunkKey keyOf(int64_t n) { return {n, ~n, static_cast<int32_t>(n & 0xFFFF)}; }

ChunkKey rampKey(int32_t chunk) { return {kRampHi, kRampLo, chunk}; }

int16_t patternOf(int64_t n) { return static_cast<int16_t>(n & 0x7FFF); }

float stopGain(int32_t framesIntoStop) { return 1.0F - static_cast<float>(framesIntoStop) / static_cast<float>(kStop); }

int publishFilled(PcmPool& pool, const ChunkKey& key, int16_t value, int32_t frames) {
    const int slot = pool.claim(key);
    if (slot < 0) return slot;
    int16_t* data = pool.slotData(slot);
    std::fill(data, data + 2 * static_cast<size_t>(frames), value);
    pool.publish(slot, frames);
    return slot;
}

bool holds(const Chunk& chunk, int16_t value) {
    if (chunk.empty || chunk.frames != kHeldFrames) return false;
    return std::all_of(chunk.data, chunk.data + 2 * chunk.frames, [value](int16_t sample) { return sample == value; });
}

std::unique_ptr<RenderPlan> bare(int64_t generation) {
    auto p = std::make_unique<RenderPlan>();
    p->generation = generation;
    return p;
}

std::unique_ptr<RenderPlan> counted(int64_t n) {
    auto p = bare(n);
    p->rate = static_cast<double>(n) + 0.5;
    p->trimUs = -n;
    p->timeMap = TimeMap{2 * n, 3 * n, static_cast<double>(n) * 0.25};
    p->grid = GridPlan{static_cast<int32_t>(n), 0.0, 4, 5 * n};
    p->voices.resize(static_cast<size_t>(n % 4) + 1);
    for (VoicePlan& v : p->voices) {
        v.id = static_cast<int32_t>(n);
        v.anchorUs = 7 * n;
        v.anchorFrame = -n;
    }
    return p;
}

bool consistent(const RenderPlan& p) {
    const int64_t n = p.generation;
    if (p.rate != static_cast<double>(n) + 0.5 || p.trimUs != -n) return false;
    if (p.timeMap.baseFrame != 2 * n || p.timeMap.hostUsAtBaseFrame != 3 * n || p.timeMap.usPerFrame != static_cast<double>(n) * 0.25) return false;
    if (!p.grid || p.grid->voice != static_cast<int32_t>(n) || p.grid->firstBeatFrame != 5 * n) return false;
    if (p.voices.size() != static_cast<size_t>(n % 4) + 1) return false;
    return std::all_of(p.voices.begin(), p.voices.end(),
                       [n](const VoicePlan& v) { return v.id == static_cast<int32_t>(n) && v.anchorUs == 7 * n && v.anchorFrame == -n; });
}

TEST(PlanSlotTest, acquireBeforeAnyPublicationReturnsNull) {
    PcmPool pool;
    PlanSlot slot{pool};
    EXPECT_EQ(nullptr, slot.acquireForCallback(pool));
    EXPECT_EQ(nullptr, slot.acquireForCallback(pool));
}

TEST(PlanSlotTest, acquireReturnsTheLatestPublication) {
    PcmPool pool;
    PlanSlot slot{pool};
    slot.publish(bare(1));
    slot.publish(bare(2));
    const RenderPlan* seen = slot.acquireForCallback(pool);
    ASSERT_NE(nullptr, seen);
    EXPECT_EQ(2, seen->generation);
}

TEST(PlanSlotTest, oldPlanKeptUntilAcked) {
    PcmPool pool;
    PlanSlot slot{pool};
    auto first = bare(1);
    auto second = bare(2);
    const RenderPlan* firstPlan = first.get();
    const RenderPlan* secondPlan = second.get();
    watchFreesOnThisThread({firstPlan, secondPlan});
    slot.publish(std::move(first));
    ASSERT_EQ(firstPlan, slot.acquireForCallback(pool));
    slot.publish(std::move(second));
    slot.collectGarbage();
    EXPECT_EQ(0, freesOf(firstPlan));
    ASSERT_EQ(secondPlan, slot.acquireForCallback(pool));
    EXPECT_EQ(0, freesOf(firstPlan));
    slot.collectGarbage();
    EXPECT_EQ(1, freesOf(firstPlan));
    EXPECT_EQ(0, freesOf(secondPlan));
}

TEST(PlanSlotTest, collectGarbageFreesNothingWhileTheCallbackLagsThenEachReplacedPlanExactlyOnce) {
    PcmPool pool;
    PlanSlot slot{pool};
    auto first = bare(1);
    auto second = bare(2);
    auto third = bare(3);
    const RenderPlan* firstPlan = first.get();
    const RenderPlan* secondPlan = second.get();
    const RenderPlan* thirdPlan = third.get();
    watchFreesOnThisThread({firstPlan, secondPlan, thirdPlan});
    slot.publish(std::move(first));
    ASSERT_EQ(firstPlan, slot.acquireForCallback(pool));
    slot.publish(std::move(second));
    slot.publish(std::move(third));
    slot.retain(pool, {});
    for (int i = 0; i < 3; ++i) slot.collectGarbage();
    EXPECT_EQ(0, freesOf(firstPlan));
    EXPECT_EQ(0, freesOf(secondPlan));
    ASSERT_EQ(thirdPlan, slot.acquireForCallback(pool));
    for (int i = 0; i < 3; ++i) slot.collectGarbage();
    EXPECT_EQ(1, freesOf(firstPlan));
    EXPECT_EQ(1, freesOf(secondPlan));
    EXPECT_EQ(0, freesOf(thirdPlan));
}

TEST(PlanSlotTest, destructorFreesTheCurrentAndEveryUnackedPlanExactlyOnce) {
    auto first = bare(1);
    auto second = bare(2);
    auto third = bare(3);
    const RenderPlan* firstPlan = first.get();
    const RenderPlan* secondPlan = second.get();
    const RenderPlan* thirdPlan = third.get();
    watchFreesOnThisThread({firstPlan, secondPlan, thirdPlan});
    {
        PcmPool pool;
        PlanSlot slot{pool};
        slot.publish(std::move(first));
        slot.acquireForCallback(pool);
        slot.publish(std::move(second));
        slot.retain(pool, {});
        slot.publish(std::move(third));
        slot.collectGarbage();
        EXPECT_EQ(0, freesOf(firstPlan));
        EXPECT_EQ(0, freesOf(secondPlan));
        EXPECT_EQ(0, freesOf(thirdPlan));
    }
    EXPECT_EQ(1, freesOf(firstPlan));
    EXPECT_EQ(1, freesOf(secondPlan));
    EXPECT_EQ(1, freesOf(thirdPlan));
}

TEST(PlanSlotTest, retainWithoutPlanChangeIsReclaimableAfterTheNextBlock) {
    PcmPool pool;
    PlanSlot slot{pool};
    for (int n = 0; n < kSlots; ++n) ASSERT_GE(publishFilled(pool, keyOf(n), 1, 4), 0);
    slot.publish(bare(1));
    slot.acquireForCallback(pool);
    slot.retain(pool, {});
    EXPECT_EQ(-1, pool.claim(keyOf(100)));
    slot.acquireForCallback(pool);
    for (int n = 0; n < kSlots; ++n) EXPECT_GE(pool.claim(keyOf(100 + n)), 0) << "claim " << n;
}

TEST(PlanSlotTest, chunkRetiredAtAnEpochStaysFindableForTheBlockThatLoadedTheEpochBefore) {
    PcmPool pool;
    PlanSlot slot{pool};
    ASSERT_GE(publishFilled(pool, keyOf(1), 7, 4), 0);
    slot.publish(bare(1));
    slot.acquireForCallback(pool);
    slot.retain(pool, {});
    EXPECT_NE(nullptr, pool.find(keyOf(1)));
    slot.publish(bare(2));
    EXPECT_NE(nullptr, pool.find(keyOf(1)));
    slot.acquireForCallback(pool);
    EXPECT_EQ(nullptr, pool.find(keyOf(1)));
}

TEST(PlanSlotTest, slotBuiltOnAFreshPoolStartsAtEpochZero) {
    PcmPool pool;
    PlanSlot slot{pool};
    EXPECT_EQ(0, slot.epoch());
}

TEST(PlanSlotTest, slotBuiltOnALivePoolSeedsItsEpochFromThePoolAck) {
    PcmPool pool;
    pool.ackGeneration(kLiveAck);
    PlanSlot slot{pool};
    EXPECT_EQ(kLiveAck, slot.epoch());
    slot.retain(pool, {});
    EXPECT_EQ(kLiveAck + 1, slot.epoch());
}

TEST(PlanSlotTest, retiresOfASlotBuiltOnALivePoolStayProtectedUntilItsOwnBlockAcks) {
    PcmPool pool;
    pool.ackGeneration(kLiveAck);
    std::vector<ChunkKey> others;
    for (int n = 0; n < kSlots; ++n) {
        ASSERT_GE(publishFilled(pool, keyOf(n), 7, 4), 0);
        if (n > 0) others.push_back(keyOf(n));
    }
    PlanSlot slot{pool};
    slot.retain(pool, others);
    ASSERT_NE(nullptr, pool.find(keyOf(0)));
    EXPECT_EQ(-1, pool.claim(keyOf(100)));
    slot.acquireForCallback(pool);
    EXPECT_EQ(nullptr, pool.find(keyOf(0)));
    EXPECT_GE(pool.claim(keyOf(100)), 0);
}

class PlanSlotRenderTest : public RendererFixture {
protected:
    std::vector<float> blocks(int64_t from, int64_t frames, int32_t block) { return streamFromSlot(renderer, slot, pool, from, frames, block); }

    PlanSlot slot{pool};
};

TEST_F(PlanSlotRenderTest, nothingPublishedRendersSilence) {
    publishRamp(1);
    EXPECT_TRUE(allZero(blocks(0, 1920, kBlock)));
}

TEST_F(PlanSlotRenderTest, planSwapMidStreamIsContinuous) {
    publishRamp(1);
    constexpr int64_t kAnchorClearOfRampWrap = 98400;
    constexpr int64_t kTrimUs = 2000;
    constexpr int64_t kSwapFrame = 1920;
    constexpr int64_t kFrames = 9600;
    RenderPlan trimmed = plan({voice(1, kHostUs, kAnchorClearOfRampWrap)});
    trimmed.trimUs = kTrimUs;
    RenderPlan untrimmed = trimmed;
    untrimmed.trimUs = 0;
    slot.publish(std::make_unique<RenderPlan>(trimmed));
    std::vector<float> out = blocks(0, kSwapFrame, kBlock);
    EXPECT_EQ(1.0, renderer.probe(1)->ratio);
    slot.publish(std::make_unique<RenderPlan>(untrimmed));
    const std::vector<float> after = blocks(kSwapFrame, kFrames - kSwapFrame, kBlock);
    out.insert(out.end(), after.begin(), after.end());
    EXPECT_DOUBLE_EQ(1.0 - kMaxDeviation, renderer.probe(1)->ratio);
    const double largestStep = (1.0 + kMaxDeviation) * kRampStep + kRoundingSlack;
    for (int64_t i = kFade; i + 1 < kFrames; ++i) {
        const double step = static_cast<double>(out[2 * (i + 1)]) - static_cast<double>(out[2 * i]);
        ASSERT_GT(step, 0.0) << "frame " << i;
        ASSERT_LE(step, largestStep) << "frame " << i;
    }
}

TEST_F(PlanSlotRenderTest, stopFadeSurvivesTheSwapWhileTheStoppingVoicesChunksStayWanted) {
    publishRamp(1);
    slot.publish(std::make_unique<RenderPlan>(plan({voice(1)})));
    blocks(0, 1024, kSmallBlock);
    slot.publish(std::make_unique<RenderPlan>(plan({})));
    slot.retain(pool, {rampKey(1)});
    const std::vector<float> out = blocks(1024, 512, kSmallBlock);
    for (int32_t i = 0; i < kStop; ++i) EXPECT_NEAR(ramp(kAnchorFrame + 1024 + i) * stopGain(i), out[2 * i], 1e-6) << "frame " << i;
    for (int32_t i = kStop; i < 512; ++i) EXPECT_EQ(0.0F, out[2 * i]) << "frame " << i;
    EXPECT_EQ(0U, renderer.underruns());
}

TEST_F(PlanSlotRenderTest, stopFadeIsCutSilentlyWhenTheSwapRetiresTheStoppingVoicesChunks) {
    publishRamp(1);
    slot.publish(std::make_unique<RenderPlan>(plan({voice(1)})));
    ASSERT_FALSE(allZero(blocks(0, 1024, kSmallBlock)));
    slot.publish(std::make_unique<RenderPlan>(plan({})));
    slot.retain(pool, {});
    EXPECT_TRUE(allZero(blocks(1024, 512, kSmallBlock)));
    EXPECT_EQ(0U, renderer.underruns());
}

TEST(PlanSlotConcurrencyTest, callbackNeverSeesPartialPlan) {
    PcmPool pool;
    PlanSlot slot{pool};
    std::atomic<bool> controlDone{false};
    int64_t blocks = 0;
    int64_t partial = 0;
    int64_t regressions = 0;
    int64_t newest = 0;
    std::thread callback([&] {
        for (bool last = false; !last;) {
            last = controlDone.load(std::memory_order_acquire);
            const RenderPlan* p = slot.acquireForCallback(pool);
            ++blocks;
            if (p == nullptr) continue;
            const int64_t generation = p->generation;
            if (!consistent(*p)) ++partial;
            if (generation < newest) ++regressions;
            newest = std::max(newest, generation);
            std::this_thread::yield();
            if (!consistent(*p) || p->generation != generation) ++partial;
        }
    });
    for (int64_t n = 1; n <= kPublications; ++n) {
        slot.publish(counted(n));
        slot.collectGarbage();
    }
    controlDone.store(true, std::memory_order_release);
    callback.join();

    EXPECT_EQ(0, partial);
    EXPECT_EQ(0, regressions);
    EXPECT_EQ(kPublications, newest);
    EXPECT_GT(blocks, 1);
}

struct Held {
    PcmPool pool;
    PlanSlot slot{pool};
    std::atomic<int64_t> latest{0};
    std::atomic<int64_t> stalledAt{0};
    std::atomic<bool> retaining{false};
    std::atomic<bool> stop{false};
};

void rotateHeldChunks(Held& shared) {
    std::vector<ChunkKey> wanted;
    for (int i = 0; i < kAbsentKeys; ++i) wanted.push_back(ChunkKey{-2, -2, i});
    for (int i = 0; i < kPinnedKeys; ++i) {
        wanted.push_back(ChunkKey{-1, -1, i});
        if (publishFilled(shared.pool, wanted.back(), 0, kHeldFrames) < 0) return;
    }
    wanted.push_back(keyOf(0));
    for (int64_t n = 1; !shared.stop.load(std::memory_order_acquire); ++n) {
        int index = shared.pool.claim(keyOf(n));
        while (index < 0) {
            if (shared.stop.load(std::memory_order_acquire)) return;
            shared.stalledAt.store(n, std::memory_order_relaxed);
            std::this_thread::yield();
            index = shared.pool.claim(keyOf(n));
        }
        shared.stalledAt.store(0, std::memory_order_relaxed);
        int16_t* data = shared.pool.slotData(index);
        std::fill(data, data + 2 * static_cast<size_t>(kHeldFrames), patternOf(n));
        shared.pool.publish(index, kHeldFrames);
        wanted.back() = keyOf(n);
        shared.retaining.store(true, std::memory_order_relaxed);
        shared.slot.retain(shared.pool, wanted);
        shared.retaining.store(false, std::memory_order_relaxed);
        shared.latest.store(n, std::memory_order_relaxed);
    }
}

TEST(PlanSlotConcurrencyTest, blockHoldingAChunkAcrossItsRetireNeverSeesItRewritten) {
    Held shared;
    std::thread control([&] { rotateHeldChunks(shared); });

    int held = 0;
    int heldAcrossRetire = 0;
    int corrupted = 0;
    int timeouts = 0;
    const auto deadline = std::chrono::steady_clock::now() + kStressDeadline;
    while (held < kHoldCycles && std::chrono::steady_clock::now() < deadline) {
        if (!shared.retaining.load(std::memory_order_relaxed)) {
            shared.slot.acquireForCallback(shared.pool);
            continue;
        }
        shared.slot.acquireForCallback(shared.pool);
        const int64_t n = shared.latest.load(std::memory_order_relaxed);
        if (n == 0) continue;
        const Chunk* chunk = shared.pool.find(keyOf(n));
        if (chunk == nullptr) continue;
        const auto blockDeadline = std::chrono::steady_clock::now() + kHoldDeadline;
        bool stalledBehindTheHold = false;
        while (!stalledBehindTheHold && shared.latest.load(std::memory_order_relaxed) < n + 2) {
            if (std::chrono::steady_clock::now() > blockDeadline) {
                ++timeouts;
                break;
            }
            std::this_thread::yield();
            stalledBehindTheHold = shared.stalledAt.load(std::memory_order_relaxed) > n;
        }
        if (stalledBehindTheHold) ++heldAcrossRetire;
        if (!holds(*chunk, patternOf(n))) ++corrupted;
        ++held;
    }
    shared.stop.store(true, std::memory_order_release);
    control.join();

    EXPECT_EQ(kHoldCycles, held);
    EXPECT_GT(heldAcrossRetire, 0);
    EXPECT_EQ(0, corrupted);
    EXPECT_EQ(0, timeouts);
}

VoicePlan rotatingVoice(int64_t n, int64_t startFrame) {
    VoicePlan v = voice(1, kHostUs + static_cast<int64_t>(std::llround(static_cast<double>(startFrame) * kUsPerFrame)), 0);
    v.trackHi = n;
    v.trackLo = ~n;
    return v;
}

TEST(PlanSlotConcurrencyTest, controlPublishesRetainsAndCollectsWhileCallbackRendersRealBlocks) {
    PcmPool pool;
    PlanSlot slot{pool};
    Renderer renderer{pool};
    std::atomic<bool> done{false};
    std::atomic<int64_t> deviceFrame{0};
    int64_t blocks = 0;
    int64_t broken = 0;
    float loudest = 0.0F;
    std::thread callback([&] {
        std::vector<float> out(2 * kBlock);
        for (int64_t frame = 0; !done.load(std::memory_order_acquire); frame += kBlock) {
            renderer.render(slot.acquireForCallback(pool), frame, kBlock, out.data());
            deviceFrame.store(frame + kBlock, std::memory_order_relaxed);
            ++blocks;
            for (const float sample : out) {
                if (!(std::fabs(sample) <= static_cast<float>(Renderer::kMaxVoices))) ++broken;
                loudest = std::max(loudest, std::fabs(sample));
            }
        }
    });

    const auto deadline = std::chrono::steady_clock::now() + kStressDeadline;
    int64_t rotated = 0;
    for (int64_t n = 1; n <= kRotations && std::chrono::steady_clock::now() < deadline; ++n) {
        const ChunkKey key{n, ~n, 0};
        int index = pool.claim(key);
        while (index < 0 && std::chrono::steady_clock::now() < deadline) {
            std::this_thread::yield();
            index = pool.claim(key);
        }
        if (index < 0) break;
        int16_t* data = pool.slotData(index);
        std::fill(data, data + 2 * static_cast<size_t>(kRotatingChunkFrames), patternOf(n));
        pool.publish(index, kRotatingChunkFrames);
        slot.publish(std::make_unique<RenderPlan>(plan({rotatingVoice(n, deviceFrame.load(std::memory_order_relaxed))})));
        slot.retain(pool, {ChunkKey{n - 1, ~(n - 1), 0}, key});
        slot.collectGarbage();
        rotated = n;
    }
    done.store(true, std::memory_order_release);
    callback.join();

    EXPECT_EQ(kRotations, rotated);
    EXPECT_EQ(0, broken);
    EXPECT_GT(blocks, 0);
    EXPECT_GT(loudest, 0.0F);
}

TEST(PlanSlotConcurrencyTest, twoControlThreadsNeverDuplicateAnEpochWhileTheCallbackRenders) {
    PcmPool pool;
    PlanSlot slot{pool};
    Renderer renderer{pool};
    std::atomic<bool> done{false};
    int64_t partial = 0;
    int64_t regressions = 0;
    int64_t blocks = 0;
    std::thread callback([&] {
        std::vector<float> out(2 * kBlock);
        int64_t lastEpoch = 0;
        for (int64_t frame = 0; !done.load(std::memory_order_acquire); frame += kBlock) {
            const RenderPlan* p = slot.acquireForCallback(pool);
            renderer.render(p, frame, kBlock, out.data());
            ++blocks;
            if (p != nullptr && !consistent(*p)) ++partial;
            const int64_t epoch = slot.epoch();
            if (epoch < lastEpoch) ++regressions;
            lastEpoch = epoch;
        }
    });
    const auto control = [&](int64_t first) {
        for (int64_t i = 0; i < kCallsPerControlThread; ++i) {
            slot.publish(counted(first + 2 * i));
            slot.retain(pool, {});
            slot.collectGarbage();
        }
    };
    std::thread a(control, 1);
    std::thread b(control, 2);
    a.join();
    b.join();
    done.store(true, std::memory_order_release);
    callback.join();

    EXPECT_EQ(4 * kCallsPerControlThread, slot.epoch());
    EXPECT_EQ(0, partial);
    EXPECT_EQ(0, regressions);
    EXPECT_GT(blocks, 0);
}
}
}
