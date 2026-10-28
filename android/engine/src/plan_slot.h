#pragma once

#include "pcm_pool.h"
#include "plan.h"

#include <atomic>
#include <cstdint>
#include <memory>
#include <vector>

namespace aria {

class PlanSlot {
public:
    PlanSlot() = default;
    PlanSlot(const PlanSlot&) = delete;
    PlanSlot& operator=(const PlanSlot&) = delete;

    void publish(std::unique_ptr<RenderPlan> plan);
    void retain(PcmPool& pool, const std::vector<ChunkKey>& keys);
    void collectGarbage();

    const RenderPlan* acquireForCallback(PcmPool& pool);

private:
    struct Retired {
        std::unique_ptr<RenderPlan> plan;
        int64_t replacedAt;
    };

    static_assert(std::atomic<int64_t>::is_always_lock_free, "epoch and ack must be lock-free");
    static_assert(std::atomic<const RenderPlan*>::is_always_lock_free, "plan pointer must be lock-free");

    template <typename BeforeEpochIsPublished>
    int64_t advanceEpoch(BeforeEpochIsPublished beforeEpochIsPublished);

    std::atomic<int64_t> epoch_{0};
    std::atomic<int64_t> acked_{0};
    std::atomic<const RenderPlan*> published_{nullptr};
    std::unique_ptr<RenderPlan> current_;
    std::vector<Retired> retired_;
};

}
