#include "reopen_retry.h"

namespace aria {

ReopenRetry::ReopenRetry(std::chrono::milliseconds pause) : pause_(pause) {}

void ReopenRetry::cancel() {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        ++ticket_;
    }
    cancelled_.notify_all();
}

int32_t ReopenRetry::consumeReopens() { return reopens_.exchange(0, std::memory_order_acq_rel); }

uint64_t ReopenRetry::currentTicket() {
    std::lock_guard<std::mutex> lock(mutex_);
    return ticket_;
}

bool ReopenRetry::waitUnlessCancelled(std::chrono::milliseconds delay, uint64_t ticket) {
    std::unique_lock<std::mutex> lock(mutex_);
    return !cancelled_.wait_for(lock, delay, [this, ticket] { return ticket_ != ticket; });
}

}
