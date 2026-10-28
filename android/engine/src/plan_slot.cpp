#include "plan_slot.h"

#include <algorithm>
#include <utility>

namespace aria {

template <typename BeforeEpochIsPublished>
int64_t PlanSlot::advanceEpoch(BeforeEpochIsPublished beforeEpochIsPublished) {
    const int64_t next = epoch_.load(std::memory_order_relaxed) + 1;
    beforeEpochIsPublished(next);
    epoch_.store(next, std::memory_order_release);
    return next;
}

void PlanSlot::publish(std::unique_ptr<RenderPlan> plan) {
    const RenderPlan* next = plan.get();
    const int64_t replacedAt = advanceEpoch([this, next](int64_t) { published_.store(next, std::memory_order_release); });
    if (current_) retired_.push_back(Retired{std::move(current_), replacedAt});
    current_ = std::move(plan);
}

void PlanSlot::retain(PcmPool& pool, const std::vector<ChunkKey>& keys) {
    advanceEpoch([&pool, &keys](int64_t next) { pool.retain(keys, next); });
}

void PlanSlot::collectGarbage() {
    const int64_t acked = acked_.load(std::memory_order_acquire);
    const auto stillVisible = std::find_if(retired_.begin(), retired_.end(), [acked](const Retired& old) { return old.replacedAt > acked; });
    retired_.erase(retired_.begin(), stillVisible);
}

const RenderPlan* PlanSlot::acquireForCallback(PcmPool& pool) {
    const int64_t epoch = epoch_.load(std::memory_order_acquire);
    pool.ackGeneration(epoch);
    acked_.store(epoch, std::memory_order_release);
    return published_.load(std::memory_order_acquire);
}

}
