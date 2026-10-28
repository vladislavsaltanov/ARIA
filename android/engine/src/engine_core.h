#pragma once

#include "pcm_pool.h"
#include "plan_slot.h"
#include "renderer.h"

#include <cstddef>
#include <cstdint>
#include <vector>

namespace aria {

class EngineCore {
public:
    EngineCore();
    EngineCore(const EngineCore&) = delete;
    EngineCore& operator=(const EngineCore&) = delete;

    bool publishPlan(const uint8_t* data, size_t size);
    bool writeChunk(const ChunkKey& key, const uint8_t* bytes, size_t size, int32_t frames);
    void markChunkEmpty(const ChunkKey& key);
    void retainChunks(std::vector<ChunkKey> keys);

    PcmPool& pool() { return pool_; }
    PlanSlot& slot() { return slot_; }
    Renderer& renderer() { return renderer_; }

private:
    PcmPool pool_;
    PlanSlot slot_;
    Renderer renderer_;
    std::vector<ChunkKey> retained_;
};

}
