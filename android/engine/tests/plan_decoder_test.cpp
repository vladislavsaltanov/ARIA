#include "plan_decoder.h"

#include <gtest/gtest.h>
#include <nlohmann/json.hpp>

#include <atomic>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iterator>
#include <new>
#include <optional>
#include <string>
#include <vector>

namespace {

std::atomic<size_t> largestAllocation{0};

void recordAllocation(size_t size) {
    size_t seen = largestAllocation.load(std::memory_order_relaxed);
    while (size > seen && !largestAllocation.compare_exchange_weak(seen, size, std::memory_order_relaxed)) {
    }
}
}

void* operator new(size_t size) {
    recordAllocation(size);
    void* memory = std::malloc(size == 0 ? 1 : size);
    if (memory == nullptr) throw std::bad_alloc();
    return memory;
}

void operator delete(void* memory) noexcept { std::free(memory); }

void operator delete(void* memory, size_t) noexcept { std::free(memory); }

namespace {

using aria::RenderPlan;
using nlohmann::json;

constexpr size_t kFirstVoicePlayingFlag = 76;
constexpr size_t kFirstVoiceEndFramePresence = 77;
constexpr size_t kFirstVoiceFadePresence = 86;
constexpr size_t kFirstVoiceCurveLength = 115;
constexpr size_t kFirstVoiceStopAtEndFlag = 130;
constexpr size_t kEmptyPlanGridPresence = 28;
constexpr size_t kEmptyPlanTestPresence = 29;
constexpr size_t kVersionOffset = 4;
constexpr size_t kVoiceCountOffset = 24;
constexpr size_t kAllocationCeilingBytes = 4096;

std::string fixturePath(const std::string& name) {
    return std::string(ARIA_FIXTURES_DIR) + "/" + name;
}

std::vector<uint8_t> readBytes(const std::string& name) {
    std::ifstream file(fixturePath(name), std::ios::binary);
    return std::vector<uint8_t>(std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>());
}

json readJson(const std::string& name) {
    std::ifstream file(fixturePath(name));
    return json::parse(file);
}

template <typename Value, typename Read>
std::optional<Value> optionalFromJson(const json& node, Read read) {
    if (node.is_null()) return std::nullopt;
    return read(node);
}

aria::FadePlan fadeFromJson(const json& node) {
    return {node.at("from").get<double>(),
            node.at("to").get<double>(),
            node.at("progress").get<double>(),
            node.at("frames").get<int32_t>(),
            node.at("curve").get<std::string>(),
            node.at("stopAtEnd").get<bool>()};
}

aria::VoicePlan voiceFromJson(const json& node) {
    return {node.at("id").get<int32_t>(),
            node.at("epoch").get<int32_t>(),
            node.at("trackHi").get<int64_t>(),
            node.at("trackLo").get<int64_t>(),
            node.at("gainDb").get<double>(),
            node.at("anchorUs").get<int64_t>(),
            node.at("anchorFrame").get<int64_t>(),
            node.at("playing").get<bool>(),
            optionalFromJson<int64_t>(node.at("endFrame"), [](const json& n) { return n.get<int64_t>(); }),
            optionalFromJson<aria::FadePlan>(node.at("fade"), fadeFromJson)};
}

aria::GridPlan gridFromJson(const json& node) {
    return {node.at("voice").get<int32_t>(),
            node.at("bpm").get<double>(),
            node.at("beatsPerBar").get<int32_t>(),
            node.at("firstBeatFrame").get<int64_t>()};
}

aria::TestPlan testFromJson(const json& node) {
    return {node.at("anchorUs").get<int64_t>(), node.at("intervalUs").get<double>()};
}

RenderPlan planFromJson(const json& node) {
    RenderPlan plan;
    plan.generation = node.at("generation").get<int64_t>();
    plan.rate = node.at("rate").get<double>();
    for (const json& voice : node.at("voices")) plan.voices.push_back(voiceFromJson(voice));
    plan.grid = optionalFromJson<aria::GridPlan>(node.at("grid"), gridFromJson);
    plan.test = optionalFromJson<aria::TestPlan>(node.at("test"), testFromJson);
    plan.backingGain = node.at("backingGain").get<float>();
    plan.clickGain = node.at("clickGain").get<float>();
    plan.masterGain = node.at("masterGain").get<float>();
    plan.masterRampSeconds = node.at("masterRampSeconds").get<float>();
    plan.trimUs = node.at("trimUs").get<int64_t>();
    const json& timeMap = node.at("timeMap");
    plan.timeMap = {timeMap.at("baseFrame").get<int64_t>(),
                    timeMap.at("hostUsAtBaseFrame").get<int64_t>(),
                    timeMap.at("usPerFrame").get<double>()};
    return plan;
}

void expectSameFade(const std::optional<aria::FadePlan>& actual, const std::optional<aria::FadePlan>& expected) {
    ASSERT_EQ(expected.has_value(), actual.has_value());
    if (!expected) return;
    EXPECT_EQ(expected->from, actual->from);
    EXPECT_EQ(expected->to, actual->to);
    EXPECT_EQ(expected->progress, actual->progress);
    EXPECT_EQ(expected->frames, actual->frames);
    EXPECT_EQ(expected->curve, actual->curve);
    EXPECT_EQ(expected->stopAtEnd, actual->stopAtEnd);
}

void expectSameVoice(const aria::VoicePlan& actual, const aria::VoicePlan& expected) {
    EXPECT_EQ(expected.id, actual.id);
    EXPECT_EQ(expected.epoch, actual.epoch);
    EXPECT_EQ(expected.trackHi, actual.trackHi);
    EXPECT_EQ(expected.trackLo, actual.trackLo);
    EXPECT_EQ(expected.gainDb, actual.gainDb);
    EXPECT_EQ(expected.anchorUs, actual.anchorUs);
    EXPECT_EQ(expected.anchorFrame, actual.anchorFrame);
    EXPECT_EQ(expected.playing, actual.playing);
    EXPECT_EQ(expected.endFrame, actual.endFrame);
    expectSameFade(actual.fade, expected.fade);
}

void expectSamePlan(const RenderPlan& actual, const RenderPlan& expected) {
    EXPECT_EQ(expected.generation, actual.generation);
    EXPECT_EQ(expected.rate, actual.rate);
    ASSERT_EQ(expected.voices.size(), actual.voices.size());
    for (size_t i = 0; i < expected.voices.size(); ++i) expectSameVoice(actual.voices[i], expected.voices[i]);
    ASSERT_EQ(expected.grid.has_value(), actual.grid.has_value());
    if (expected.grid) {
        EXPECT_EQ(expected.grid->voice, actual.grid->voice);
        EXPECT_EQ(expected.grid->bpm, actual.grid->bpm);
        EXPECT_EQ(expected.grid->beatsPerBar, actual.grid->beatsPerBar);
        EXPECT_EQ(expected.grid->firstBeatFrame, actual.grid->firstBeatFrame);
    }
    ASSERT_EQ(expected.test.has_value(), actual.test.has_value());
    if (expected.test) {
        EXPECT_EQ(expected.test->anchorUs, actual.test->anchorUs);
        EXPECT_EQ(expected.test->intervalUs, actual.test->intervalUs);
    }
    EXPECT_EQ(expected.backingGain, actual.backingGain);
    EXPECT_EQ(expected.clickGain, actual.clickGain);
    EXPECT_EQ(expected.masterGain, actual.masterGain);
    EXPECT_EQ(expected.masterRampSeconds, actual.masterRampSeconds);
    EXPECT_EQ(expected.trimUs, actual.trimUs);
    EXPECT_EQ(expected.timeMap.baseFrame, actual.timeMap.baseFrame);
    EXPECT_EQ(expected.timeMap.hostUsAtBaseFrame, actual.timeMap.hostUsAtBaseFrame);
    EXPECT_EQ(expected.timeMap.usPerFrame, actual.timeMap.usPerFrame);
}

class ByteWriter {
public:
    ByteWriter& u8(uint8_t value) {
        bytes_.push_back(value);
        return *this;
    }

    ByteWriter& u32(uint32_t value) { return little(value, 4); }

    ByteWriter& i64(int64_t value) { return little(static_cast<uint64_t>(value), 8); }

    ByteWriter& f32(float value) {
        uint32_t bits;
        std::memcpy(&bits, &value, sizeof bits);
        return little(bits, 4);
    }

    ByteWriter& f64(double value) {
        uint64_t bits;
        std::memcpy(&bits, &value, sizeof bits);
        return little(bits, 8);
    }

    ByteWriter& header(uint32_t version) { return u32(0x4C505241).u32(version); }

    ByteWriter& planStart(uint32_t voiceCount) { return header(1).i64(7).f64(1.0).u32(voiceCount); }

    ByteWriter& minimalVoice(int32_t id) {
        return u32(static_cast<uint32_t>(id)).u32(1).i64(id).i64(-id).f64(-3.0).i64(1000).i64(2000).u8(1).u8(0).u8(0);
    }

    ByteWriter& planEnd() {
        return u8(0).u8(0).f32(0.5F).f32(0.25F).f32(1.0F).f32(0.125F).i64(-12500).i64(48000).i64(999).f64(20.5);
    }

    const std::vector<uint8_t>& bytes() const { return bytes_; }

private:
    ByteWriter& little(uint64_t value, int width) {
        for (int i = 0; i < width; ++i) bytes_.push_back(static_cast<uint8_t>(value >> (8 * i)));
        return *this;
    }

    std::vector<uint8_t> bytes_;
};

std::vector<uint8_t> emptyPlanBytes() { return ByteWriter().planStart(0).planEnd().bytes(); }

std::vector<uint8_t> planBytesWithVoices(int32_t count) {
    ByteWriter writer;
    writer.planStart(static_cast<uint32_t>(count));
    for (int32_t id = 1; id <= count; ++id) writer.minimalVoice(id);
    return writer.planEnd().bytes();
}

void putU32(std::vector<uint8_t>& bytes, size_t offset, uint32_t value) {
    for (size_t i = 0; i < 4; ++i) bytes[offset + i] = static_cast<uint8_t>(value >> (8 * i));
}

class PlanDecoderTest : public ::testing::Test {
protected:
    void SetUp() override {
        golden = readBytes("plan-v1.bin");
        ASSERT_FALSE(golden.empty()) << "missing fixture " << fixturePath("plan-v1.bin");
    }

    std::vector<uint8_t> golden;
};

TEST_F(PlanDecoderTest, decodesGoldenFixture) {
    const auto decoded = aria::decodePlan(golden.data(), golden.size());
    ASSERT_NE(nullptr, decoded);
    expectSamePlan(*decoded, planFromJson(readJson("plan-v1.json")));
}

TEST_F(PlanDecoderTest, decodesEmptyPlan) {
    const std::vector<uint8_t> bytes = emptyPlanBytes();
    const auto decoded = aria::decodePlan(bytes.data(), bytes.size());
    ASSERT_NE(nullptr, decoded);
    EXPECT_EQ(7, decoded->generation);
    EXPECT_EQ(1.0, decoded->rate);
    EXPECT_TRUE(decoded->voices.empty());
    EXPECT_FALSE(decoded->grid.has_value());
    EXPECT_FALSE(decoded->test.has_value());
    EXPECT_EQ(0.5F, decoded->backingGain);
    EXPECT_EQ(0.25F, decoded->clickGain);
    EXPECT_EQ(1.0F, decoded->masterGain);
    EXPECT_EQ(0.125F, decoded->masterRampSeconds);
    EXPECT_EQ(-12500, decoded->trimUs);
    EXPECT_EQ(48000, decoded->timeMap.baseFrame);
    EXPECT_EQ(999, decoded->timeMap.hostUsAtBaseFrame);
    EXPECT_EQ(20.5, decoded->timeMap.usPerFrame);
}

TEST_F(PlanDecoderTest, decodesMoreVoicesThanTheRendererCanPlay) {
    const std::vector<uint8_t> bytes = planBytesWithVoices(9);
    const auto decoded = aria::decodePlan(bytes.data(), bytes.size());
    ASSERT_NE(nullptr, decoded);
    ASSERT_EQ(9u, decoded->voices.size());
    EXPECT_EQ(9, decoded->voices.back().id);
    EXPECT_EQ(-9, decoded->voices.back().trackLo);
}

TEST_F(PlanDecoderTest, rejectsTruncatedBuffer) {
    ASSERT_NE(nullptr, aria::decodePlan(golden.data(), golden.size()));
    for (size_t length = 0; length < golden.size(); ++length) {
        EXPECT_EQ(nullptr, aria::decodePlan(golden.data(), length)) << "prefix of " << length << " bytes";
    }
}

TEST_F(PlanDecoderTest, rejectsEmptyInput) {
    EXPECT_EQ(nullptr, aria::decodePlan(nullptr, 0));
}

TEST_F(PlanDecoderTest, rejectsBadMagic) {
    for (size_t index = 0; index < 4; ++index) {
        std::vector<uint8_t> bytes = golden;
        bytes[index] ^= 0xFF;
        EXPECT_EQ(nullptr, aria::decodePlan(bytes.data(), bytes.size())) << "magic byte " << index;
    }
}

TEST_F(PlanDecoderTest, rejectsBadVersion) {
    for (const uint32_t version : {0u, 2u, 0x01000000u, 0xFFFFFFFFu}) {
        std::vector<uint8_t> bytes = golden;
        putU32(bytes, kVersionOffset, version);
        EXPECT_EQ(nullptr, aria::decodePlan(bytes.data(), bytes.size())) << "version " << version;
    }
}

TEST_F(PlanDecoderTest, rejectsTrailingBytes) {
    std::vector<uint8_t> bytes = golden;
    bytes.push_back(0);
    EXPECT_EQ(nullptr, aria::decodePlan(bytes.data(), bytes.size()));
}

TEST_F(PlanDecoderTest, rejectsFlagBytesOtherThanZeroOrOne) {
    for (const size_t offset : {kFirstVoicePlayingFlag, kFirstVoiceEndFramePresence, kFirstVoiceFadePresence, kFirstVoiceStopAtEndFlag}) {
        std::vector<uint8_t> bytes = golden;
        bytes[offset] = 2;
        EXPECT_EQ(nullptr, aria::decodePlan(bytes.data(), bytes.size())) << "flag at " << offset;
    }
    for (const size_t offset : {kEmptyPlanGridPresence, kEmptyPlanTestPresence}) {
        std::vector<uint8_t> bytes = emptyPlanBytes();
        bytes[offset] = 0x80;
        EXPECT_EQ(nullptr, aria::decodePlan(bytes.data(), bytes.size())) << "flag at " << offset;
    }
}

TEST_F(PlanDecoderTest, rejectsVoiceCountLargerThanRemainingBytes) {
    for (const uint32_t count : {1u, 0x7FFFFFFFu, 0x80000000u, 0xFFFFFFFFu}) {
        std::vector<uint8_t> bytes = emptyPlanBytes();
        putU32(bytes, kVoiceCountOffset, count);
        EXPECT_EQ(nullptr, aria::decodePlan(bytes.data(), bytes.size())) << "count " << count;
    }
}

TEST_F(PlanDecoderTest, rejectsVoiceCountOneAboveWhatTheBytesHold) {
    std::vector<uint8_t> bytes = planBytesWithVoices(3);
    putU32(bytes, kVoiceCountOffset, 4);
    EXPECT_EQ(nullptr, aria::decodePlan(bytes.data(), bytes.size()));
}

TEST_F(PlanDecoderTest, rejectsStringLengthLargerThanRemainingBytes) {
    for (const uint32_t length : {0x7FFFFFFFu, 0x80000000u, 0xFFFFFFFFu}) {
        std::vector<uint8_t> bytes = golden;
        putU32(bytes, kFirstVoiceCurveLength, length);
        EXPECT_EQ(nullptr, aria::decodePlan(bytes.data(), bytes.size())) << "length " << length;
    }
}

TEST_F(PlanDecoderTest, hostileCountsDoNotAllocateInProportionToThem) {
    for (const uint32_t count : {0x7FFFFFFFu, 0xFFFFFFFFu}) {
        std::vector<uint8_t> voices = emptyPlanBytes();
        putU32(voices, kVoiceCountOffset, count);
        std::vector<uint8_t> text = golden;
        putU32(text, kFirstVoiceCurveLength, count);
        for (const std::vector<uint8_t>* bytes : {&voices, &text}) {
            largestAllocation.store(0);
            EXPECT_EQ(nullptr, aria::decodePlan(bytes->data(), bytes->size()));
            EXPECT_LT(largestAllocation.load(), kAllocationCeilingBytes) << "count " << count;
        }
    }
}

}
