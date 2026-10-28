#include "engine_core.h"

#include "plan_decoder.h"

#include <cstring>
#include <utility>

namespace aria {
namespace {

constexpr size_t kBytesPerFrame = sizeof(int16_t) * kChannels;

bool isConsistent(const uint8_t* bytes, size_t size, int32_t frames) {
    if (frames < 0 || frames > kChunkFrames) return false;
    if (size != static_cast<size_t>(frames) * kBytesPerFrame) return false;
    return size == 0 || bytes != nullptr;
}

}

EngineCore::EngineCore() : slot_(pool_), renderer_(pool_) {}

bool EngineCore::publishPlan(const uint8_t* data, size_t size) {
    std::unique_ptr<RenderPlan> plan = decodePlan(data, size);
    if (!plan) return false;
    slot_.publish(std::move(plan));
    slot_.retain(pool_, retained_);
    slot_.collectGarbage();
    return true;
}

bool EngineCore::writeChunk(const ChunkKey& key, const uint8_t* bytes, size_t size, int32_t frames) {
    if (!isConsistent(bytes, size, frames)) return false;
    if (pool_.isReady(key)) return true;
    const int slot = pool_.claim(key);
    if (slot < 0) return false;
    if (size > 0) std::memcpy(pool_.slotData(slot), bytes, size);
    pool_.publish(slot, frames);
    return true;
}

void EngineCore::markChunkEmpty(const ChunkKey& key) {
    if (pool_.isReady(key)) return;
    const int slot = pool_.claim(key);
    if (slot >= 0) pool_.publishEmpty(slot);
}

void EngineCore::retainChunks(std::vector<ChunkKey> keys) {
    retained_ = std::move(keys);
    slot_.retain(pool_, retained_);
    slot_.collectGarbage();
}

}
