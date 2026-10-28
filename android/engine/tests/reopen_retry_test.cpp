#include "reopen_retry.h"

#include <gtest/gtest.h>

#include <atomic>
#include <chrono>
#include <thread>
#include <vector>

namespace {

using aria::ReopenRetry;
using Outcome = ReopenRetry::Outcome;
using std::chrono::milliseconds;

constexpr milliseconds kTick{1};

TEST(ReopenRetryTest, theDefaultPauseIsAConstant250ms) {
    const ReopenRetry retry;
    EXPECT_EQ(milliseconds(250), retry.pause());
    EXPECT_EQ(milliseconds(250), ReopenRetry::kPause);
}

TEST(ReopenRetryTest, thePauseBetweenAttemptsDoesNotGrowAfterManyFailures) {
    constexpr milliseconds kPause{20};
    constexpr int kFailures = 8;
    ReopenRetry retry(kPause);
    std::vector<std::chrono::steady_clock::time_point> starts;
    EXPECT_TRUE(retry.runUntilReopenedOrCancelled([&starts] {
        starts.push_back(std::chrono::steady_clock::now());
        return starts.size() <= kFailures ? Outcome::Failed : Outcome::Reopened;
    }));
    ASSERT_EQ(static_cast<size_t>(kFailures + 1), starts.size());
    for (size_t i = 1; i < starts.size(); ++i) EXPECT_GE(starts[i] - starts[i - 1], kPause);
    EXPECT_LT(starts.back() - starts.front(), kPause * kFailures * 3);
}

TEST(ReopenRetryTest, aSuccessfulFirstAttemptCountsOneReopenAndWaitsNever) {
    ReopenRetry retry(kTick);
    int attempts = 0;
    EXPECT_TRUE(retry.runUntilReopenedOrCancelled([&attempts] {
        ++attempts;
        return Outcome::Reopened;
    }));
    EXPECT_EQ(1, attempts);
    EXPECT_EQ(1, retry.consumeReopens());
    EXPECT_EQ(0, retry.consumeReopens());
}

TEST(ReopenRetryTest, failuresAreRetriedWithoutALimitAndOnlyTheSuccessCounts) {
    ReopenRetry retry(kTick);
    constexpr int kFailures = 50;
    int attempts = 0;
    EXPECT_TRUE(retry.runUntilReopenedOrCancelled([&attempts] {
        return ++attempts <= kFailures ? Outcome::Failed : Outcome::Reopened;
    }));
    EXPECT_EQ(kFailures + 1, attempts);
    EXPECT_EQ(1, retry.consumeReopens());
}

TEST(ReopenRetryTest, anAbandonedAttemptEndsTheLoopWithoutCountingAReopen) {
    ReopenRetry retry(kTick);
    int attempts = 0;
    EXPECT_FALSE(retry.runUntilReopenedOrCancelled([&attempts] {
        return ++attempts < 3 ? Outcome::Failed : Outcome::Abandoned;
    }));
    EXPECT_EQ(3, attempts);
    EXPECT_EQ(0, retry.consumeReopens());
}

TEST(ReopenRetryTest, cancelInterruptsALongWaitPromptly) {
    ReopenRetry retry(milliseconds(60000));
    std::atomic<int> attempts{0};
    std::atomic<bool> result{true};
    std::thread worker([&] {
        result = retry.runUntilReopenedOrCancelled([&attempts] {
            ++attempts;
            return Outcome::Failed;
        });
    });
    while (attempts.load() == 0) std::this_thread::yield();
    std::this_thread::sleep_for(milliseconds(50));
    const auto start = std::chrono::steady_clock::now();
    retry.cancel();
    worker.join();
    EXPECT_LT(std::chrono::steady_clock::now() - start, milliseconds(2000));
    EXPECT_FALSE(result.load());
    EXPECT_EQ(1, attempts.load());
    EXPECT_EQ(0, retry.consumeReopens());
}

TEST(ReopenRetryTest, aCancelBeforeTheFirstWaitStillEndsTheLoopAfterOneFailedAttempt) {
    ReopenRetry retry(milliseconds(60000));
    int attempts = 0;
    const auto start = std::chrono::steady_clock::now();
    EXPECT_FALSE(retry.runUntilReopenedOrCancelled([&] {
        ++attempts;
        retry.cancel();
        return Outcome::Failed;
    }));
    EXPECT_LT(std::chrono::steady_clock::now() - start, milliseconds(2000));
    EXPECT_EQ(1, attempts);
}

TEST(ReopenRetryTest, aCancelDoesNotPoisonALaterRun) {
    ReopenRetry retry(kTick);
    retry.cancel();
    int attempts = 0;
    EXPECT_TRUE(retry.runUntilReopenedOrCancelled([&attempts] {
        return ++attempts < 3 ? Outcome::Failed : Outcome::Reopened;
    }));
    EXPECT_EQ(3, attempts);
}

TEST(ReopenRetryTest, everyReopenIsCountedOnceAcrossRuns) {
    ReopenRetry retry(kTick);
    for (int i = 0; i < 3; ++i) retry.runUntilReopenedOrCancelled([] { return Outcome::Reopened; });
    EXPECT_EQ(3, retry.consumeReopens());
}

}
