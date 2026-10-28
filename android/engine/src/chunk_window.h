#pragma once

#include "interp.h"
#include "pcm_pool.h"

#include <algorithm>
#include <array>
#include <cstdint>

namespace aria {

struct Stereo {
    float left = 0.0F;
    float right = 0.0F;
};

enum class Supply { Audible, Silent, Missing };

class ChunkWindow {
public:
    ChunkWindow(const PcmPool& pool, int64_t trackHi, int64_t trackLo) : pool_(pool), trackHi_(trackHi), trackLo_(trackLo) {}

    const Chunk* chunk(int64_t index) {
        if (index == index_[recent_]) return chunk_[recent_];
        recent_ = 1 - recent_;
        if (index != index_[recent_]) {
            index_[recent_] = index;
            chunk_[recent_] = pool_.find(ChunkKey{trackHi_, trackLo_, static_cast<int32_t>(index)});
        }
        return chunk_[recent_];
    }

    Supply supplyAt(int64_t frame, int64_t& endOfFile) {
        const int64_t index = frame / kChunkFrames;
        const Chunk* found = chunk(index);
        if (found == nullptr) return Supply::Missing;
        if (found->empty) return Supply::Silent;
        if (frame - index * kChunkFrames < found->frames) return Supply::Audible;
        endOfFile = std::min(endOfFile, index * kChunkFrames + found->frames);
        return Supply::Silent;
    }

    Stereo frameAt(int64_t frame) {
        if (frame < 0) return {};
        const Chunk* found = chunk(frame / kChunkFrames);
        const int64_t offset = frame % kChunkFrames;
        if (found == nullptr || found->empty || offset >= found->frames) return {};
        const int16_t* sample = found->data + offset * kChannels;
        return {sample[0] * kSampleScale, sample[1] * kSampleScale};
    }

    Stereo interpolated(int64_t base, double frac) {
        std::array<float, kTaps> left{};
        std::array<float, kTaps> right{};
        for (int tap = 0; tap < kTaps; ++tap) {
            const Stereo value = frameAt(base - 1 + tap);
            left[tap] = value.left;
            right[tap] = value.right;
        }
        return {cubic4(left.data(), frac), cubic4(right.data(), frac)};
    }

private:
    static constexpr float kSampleScale = 1.0F / 32768.0F;
    static constexpr int kTaps = 4;

    const PcmPool& pool_;
    int64_t trackHi_;
    int64_t trackLo_;
    std::array<int64_t, 2> index_{-1, -1};
    std::array<const Chunk*, 2> chunk_{nullptr, nullptr};
    int recent_ = 0;
};

}
