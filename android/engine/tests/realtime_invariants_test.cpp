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
#include <new>
#include <thread>
#include <vector>

namespace aria::fixture {
namespace {

using test::allocationsOnThisThread;

constexpr int32_t kBlock = 192;
constexpr int kBlocks = 1000;
constexpr int64_t kConcurrentPublications = 8;
constexpr std::chrono::seconds kOverlapDeadline{10};
constexpr int64_t kInvariantFrames = 30720;
constexpr int64_t kFirstClickFrame = 1024;

VoicePlan withGainDb(VoicePlan v, double gainDb) {
    v.gainDb = gainDb;
    return v;
}

RenderPlan threeVoicesAndAClick() {
    RenderPlan p = plan({withGainDb(voice(1), -3.0), onOtherTrack(voice(2)), withGainDb(voice(3, kHostUs + 50000), -6.0)});
    p.grid = GridPlan{1, 600.0, 4, kFirstClickFrame};
    p.backingGain = 0.7F;
    p.masterGain = 0.9F;
    p.clickGain = 0.8F;
    return p;
}

class RealtimeTest : public RendererFixture {
protected:
    void SetUp() override {
        publishRamp(1);
        publish(kOtherHi, kOtherLo, 1, kChunkFrames, halfScale);
    }

    bool silentBlocks(int64_t from) {
        bool silent = true;
        for (int b = 0; b < kBlocks; ++b) {
            std::fill(out.begin(), out.end(), kGarbage);
            renderer.render(slot.acquireForCallback(pool), from + static_cast<int64_t>(b) * kBlock, kBlock, out.data());
            silent = silent && allZero(out);
        }
        return silent;
    }

    std::vector<float> throughSlot(const RenderPlan& p, int32_t block) {
        slot.publish(std::make_unique<RenderPlan>(p));
        Renderer fresh{pool};
        return streamFromSlot(fresh, slot, pool, 0, kInvariantFrames, block);
    }

    PlanSlot slot{pool};
    std::vector<float> out = std::vector<float>(2 * kBlock);
};

void escape(void* pointer) { asm volatile("" : : "g"(pointer) : "memory"); }

TEST(AllocationCounterTest, countsPlainArrayAlignedAndVectorAllocationsOnTheCallingThreadOnly) {
    const int64_t before = allocationsOnThisThread();
    int* single = new int(1);
    escape(single);
    EXPECT_EQ(before + 1, allocationsOnThisThread());
    delete single;
    int* array = new int[16];
    escape(array);
    EXPECT_EQ(before + 2, allocationsOnThisThread());
    delete[] array;
    auto* aligned = new (std::align_val_t{64}) char[64];
    escape(aligned);
    EXPECT_EQ(before + 3, allocationsOnThisThread());
    ::operator delete[](aligned, std::align_val_t{64});
    std::vector<int> grown;
    grown.push_back(1);
    escape(grown.data());
    EXPECT_EQ(before + 4, allocationsOnThisThread());
    const int64_t onThisThread = allocationsOnThisThread();
    int64_t seenByOtherThread = -1;
    int64_t freshOnOtherThread = -1;
    std::thread([&seenByOtherThread, &freshOnOtherThread] {
        const int64_t fresh = allocationsOnThisThread();
        freshOnOtherThread = fresh;
        int* other = new int(2);
        escape(other);
        delete other;
        seenByOtherThread = allocationsOnThisThread() - fresh;
    }).join();
    EXPECT_EQ(1, seenByOtherThread);
    EXPECT_LT(freshOnOtherThread, onThisThread);
    EXPECT_GT(onThisThread, 4);
}

TEST_F(RealtimeTest, zeroAllocationsInRender) {
    const RenderPlan p = threeVoicesAndAClick();
    slot.publish(std::make_unique<RenderPlan>(p));
    std::atomic<bool> stop{false};
    std::atomic<int64_t> publications{0};
    std::thread control([&] {
        while (!stop.load(std::memory_order_acquire)) {
            slot.publish(std::make_unique<RenderPlan>(p));
            slot.collectGarbage();
            publications.fetch_add(1, std::memory_order_relaxed);
        }
    });
    while (publications.load(std::memory_order_relaxed) == 0) std::this_thread::yield();

    float loudest = 0.0F;
    const auto deadline = std::chrono::steady_clock::now() + kOverlapDeadline;
    const int64_t publishedBefore = publications.load(std::memory_order_relaxed);
    const int64_t before = allocationsOnThisThread();
    int64_t frame = 0;
    for (int b = 0; b < kBlocks || (publications.load(std::memory_order_relaxed) - publishedBefore < kConcurrentPublications &&
                                    std::chrono::steady_clock::now() < deadline);
         ++b) {
        renderer.render(slot.acquireForCallback(pool), frame, kBlock, out.data());
        frame += kBlock;
        for (const float sample : out) loudest = std::max(loudest, std::fabs(sample));
    }
    const int64_t after = allocationsOnThisThread();
    const int64_t concurrent = publications.load(std::memory_order_relaxed) - publishedBefore;
    stop.store(true, std::memory_order_release);
    control.join();

    EXPECT_EQ(before, after);
    EXPECT_GE(concurrent, kConcurrentPublications);
    EXPECT_GT(loudest, 0.0F);
}

TEST_F(RealtimeTest, emptyPlanSilentAndNoAllocation) {
    const int64_t beforeAnyPlan = allocationsOnThisThread();
    const bool silentBeforeAnyPlan = silentBlocks(0);
    const int64_t afterNoPlan = allocationsOnThisThread();
    slot.publish(std::make_unique<RenderPlan>(plan({})));
    const int64_t beforeEmptyPlan = allocationsOnThisThread();
    const bool silentWithEmptyPlan = silentBlocks(static_cast<int64_t>(kBlocks) * kBlock);
    const int64_t afterEmptyPlan = allocationsOnThisThread();

    EXPECT_TRUE(silentBeforeAnyPlan);
    EXPECT_EQ(beforeAnyPlan, afterNoPlan);
    EXPECT_TRUE(silentWithEmptyPlan);
    EXPECT_EQ(beforeEmptyPlan, afterEmptyPlan);
}

TEST_F(RealtimeTest, blockSizeInvariance) {
    const RenderPlan p = threeVoicesAndAClick();
    RenderPlan withoutClicks = p;
    withoutClicks.grid.reset();
    const std::vector<float> expected = throughSlot(p, 64);
    const std::vector<float> voicesOnly = throughSlot(withoutClicks, 64);
    for (int64_t i = 0; i < kFirstClickFrame; ++i) ASSERT_EQ(voicesOnly[2 * i], expected[2 * i]) << "frame " << i;
    EXPECT_NE(voicesOnly[2 * (kFirstClickFrame + 4)], expected[2 * (kFirstClickFrame + 4)]);
    EXPECT_NE(0.0F, voicesOnly[2 * 4000]);
    for (const int32_t block : {192, 480, 1024}) EXPECT_EQ(expected, throughSlot(p, block)) << "block " << block;
}

}
}
