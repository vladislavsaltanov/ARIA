#pragma once

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <mutex>

namespace aria {

class ReopenRetry {
public:
    enum class Outcome { Reopened, Failed, Abandoned };

    static constexpr std::chrono::milliseconds kPause{250};

    explicit ReopenRetry(std::chrono::milliseconds pause = kPause);
    ReopenRetry(const ReopenRetry&) = delete;
    ReopenRetry& operator=(const ReopenRetry&) = delete;

    std::chrono::milliseconds pause() const { return pause_; }

    template <typename Attempt>
    bool runUntilReopenedOrCancelled(Attempt attempt) {
        const uint64_t ticket = currentTicket();
        for (;;) {
            const Outcome outcome = attempt();
            if (outcome == Outcome::Reopened) {
                reopens_.fetch_add(1, std::memory_order_acq_rel);
                return true;
            }
            if (outcome == Outcome::Abandoned) return false;
            if (!waitUnlessCancelled(pause_, ticket)) return false;
        }
    }

    void cancel();
    int32_t consumeReopens();

private:
    uint64_t currentTicket();
    bool waitUnlessCancelled(std::chrono::milliseconds delay, uint64_t ticket);

    const std::chrono::milliseconds pause_;
    std::mutex mutex_;
    std::condition_variable cancelled_;
    uint64_t ticket_ = 0;
    std::atomic<int32_t> reopens_{0};
};

}
