#include "plan_decoder.h"

#include <cstring>
#include <limits>
#include <optional>
#include <string>

namespace aria {
namespace {

static_assert(std::numeric_limits<float>::is_iec559 && sizeof(float) == 4, "wire floats are IEEE 754 binary32");
static_assert(std::numeric_limits<double>::is_iec559 && sizeof(double) == 8, "wire doubles are IEEE 754 binary64");

constexpr uint32_t kMagic = 0x4C505241;
constexpr uint32_t kVersion = 1;
constexpr size_t kMinVoiceBytes = 51;

class Reader {
public:
    Reader(const uint8_t* data, size_t size) : cursor_(data), remaining_(size) {}

    bool ok() const { return ok_; }

    size_t remaining() const { return remaining_; }

    uint32_t u32() { return static_cast<uint32_t>(littleEndian(4)); }

    int32_t i32() { return static_cast<int32_t>(u32()); }

    int64_t i64() { return static_cast<int64_t>(littleEndian(8)); }

    float f32() {
        const uint32_t bits = u32();
        float value;
        std::memcpy(&value, &bits, sizeof value);
        return value;
    }

    double f64() {
        const uint64_t bits = littleEndian(8);
        double value;
        std::memcpy(&value, &bits, sizeof value);
        return value;
    }

    bool flag() {
        const uint64_t byte = littleEndian(1);
        if (byte > 1) ok_ = false;
        return byte == 1;
    }

    size_t count(size_t minElementBytes) {
        const size_t value = u32();
        if (value > remaining_ / minElementBytes) {
            ok_ = false;
            return 0;
        }
        return value;
    }

    std::string string() {
        const size_t length = count(1);
        if (!ok_) return {};
        std::string value(reinterpret_cast<const char*>(cursor_), length);
        cursor_ += length;
        remaining_ -= length;
        return value;
    }

private:
    uint64_t littleEndian(size_t width) {
        if (!ok_ || remaining_ < width) {
            ok_ = false;
            return 0;
        }
        uint64_t value = 0;
        for (size_t i = 0; i < width; ++i) value |= static_cast<uint64_t>(cursor_[i]) << (8 * i);
        cursor_ += width;
        remaining_ -= width;
        return value;
    }

    const uint8_t* cursor_;
    size_t remaining_;
    bool ok_ = true;
};

template <typename Decode>
auto optionalOf(Reader& reader, Decode decode) -> std::optional<decltype(decode(reader))> {
    if (!reader.flag()) return std::nullopt;
    return decode(reader);
}

FadePlan decodeFade(Reader& reader) {
    FadePlan fade;
    fade.from = reader.f64();
    fade.to = reader.f64();
    fade.progress = reader.f64();
    fade.frames = reader.i32();
    fade.curve = reader.string();
    fade.stopAtEnd = reader.flag();
    return fade;
}

VoicePlan decodeVoice(Reader& reader) {
    VoicePlan voice;
    voice.id = reader.i32();
    voice.epoch = reader.i32();
    voice.trackHi = reader.i64();
    voice.trackLo = reader.i64();
    voice.gainDb = reader.f64();
    voice.anchorUs = reader.i64();
    voice.anchorFrame = reader.i64();
    voice.playing = reader.flag();
    voice.endFrame = optionalOf(reader, [](Reader& r) { return r.i64(); });
    voice.fade = optionalOf(reader, decodeFade);
    return voice;
}

GridPlan decodeGrid(Reader& reader) {
    GridPlan grid;
    grid.voice = reader.i32();
    grid.bpm = reader.f64();
    grid.beatsPerBar = reader.i32();
    grid.firstBeatFrame = reader.i64();
    return grid;
}

TestPlan decodeTest(Reader& reader) {
    TestPlan test;
    test.anchorUs = reader.i64();
    test.intervalUs = reader.f64();
    return test;
}

TimeMap decodeTimeMap(Reader& reader) {
    TimeMap timeMap;
    timeMap.baseFrame = reader.i64();
    timeMap.hostUsAtBaseFrame = reader.i64();
    timeMap.usPerFrame = reader.f64();
    return timeMap;
}

}

std::unique_ptr<RenderPlan> decodePlan(const uint8_t* data, size_t size) {
    Reader reader(data, size);
    if (reader.u32() != kMagic || reader.u32() != kVersion) return nullptr;

    auto plan = std::make_unique<RenderPlan>();
    plan->generation = reader.i64();
    plan->rate = reader.f64();
    const size_t voiceCount = reader.count(kMinVoiceBytes);
    plan->voices.reserve(voiceCount);
    for (size_t i = 0; i < voiceCount && reader.ok(); ++i) plan->voices.push_back(decodeVoice(reader));
    plan->grid = optionalOf(reader, decodeGrid);
    plan->test = optionalOf(reader, decodeTest);
    plan->backingGain = reader.f32();
    plan->clickGain = reader.f32();
    plan->masterGain = reader.f32();
    plan->masterRampSeconds = reader.f32();
    plan->trimUs = reader.i64();
    plan->timeMap = decodeTimeMap(reader);

    if (!reader.ok() || reader.remaining() != 0) return nullptr;
    return plan;
}

}
