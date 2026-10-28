#include "engine_core.h"
#include "oboe_output.h"

#include <jni.h>

#include <cstdint>
#include <memory>
#include <mutex>
#include <optional>
#include <utility>
#include <vector>

namespace {

constexpr jsize kStampLongs = 2;
constexpr jsize kKeyLongs = 3;

struct Engine {
    aria::EngineCore core;
    std::shared_ptr<aria::OboeOutput> output;
    std::mutex control;

    Engine() : output(std::make_shared<aria::OboeOutput>(core.slot(), core.pool(), core.renderer())) {}
};

Engine* engineOf(jlong handle) { return reinterpret_cast<Engine*>(handle); }

template <typename Result, typename Body>
Result guarded(Result onFailure, Body body) {
    try {
        return body();
    } catch (...) {
        return onFailure;
    }
}

aria::ChunkKey keyOf(jlong hi, jlong lo, jint chunk) { return aria::ChunkKey{hi, lo, chunk}; }

}

extern "C" {

JNIEXPORT jlong JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeCreate(JNIEnv*, jobject) {
    return guarded<jlong>(0, [] { return reinterpret_cast<jlong>(new Engine()); });
}

JNIEXPORT void JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeDestroy(JNIEnv*, jobject, jlong handle) {
    Engine* engine = engineOf(handle);
    if (engine == nullptr) return;
    guarded<bool>(false, [engine] {
        engine->output->stop();
        delete engine;
        return true;
    });
}

JNIEXPORT jboolean JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeStart(JNIEnv*, jobject, jlong handle) {
    Engine* engine = engineOf(handle);
    return guarded<jboolean>(JNI_FALSE, [engine] {
        std::lock_guard<std::mutex> lock(engine->control);
        return engine->output->restart() ? JNI_TRUE : JNI_FALSE;
    });
}

JNIEXPORT void JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeStop(JNIEnv*, jobject, jlong handle) {
    Engine* engine = engineOf(handle);
    guarded<bool>(false, [engine] {
        std::lock_guard<std::mutex> lock(engine->control);
        engine->output->stop();
        return true;
    });
}

JNIEXPORT jboolean JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativePublish(JNIEnv* env, jobject, jlong handle,
                                                                                  jbyteArray plan) {
    Engine* engine = engineOf(handle);
    if (plan == nullptr) return JNI_FALSE;
    return guarded<jboolean>(JNI_FALSE, [env, engine, plan] {
        std::lock_guard<std::mutex> lock(engine->control);
        const jsize size = env->GetArrayLength(plan);
        jbyte* bytes = env->GetByteArrayElements(plan, nullptr);
        if (bytes == nullptr) return JNI_FALSE;
        const bool published = engine->core.publishPlan(reinterpret_cast<const uint8_t*>(bytes), static_cast<size_t>(size));
        env->ReleaseByteArrayElements(plan, bytes, JNI_ABORT);
        return published ? JNI_TRUE : JNI_FALSE;
    });
}

JNIEXPORT jboolean JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeWrite(JNIEnv* env, jobject, jlong handle, jlong hi,
                                                                                jlong lo, jint chunk, jbyteArray pcm,
                                                                                jint frames) {
    Engine* engine = engineOf(handle);
    if (pcm == nullptr) return JNI_FALSE;
    return guarded<jboolean>(JNI_FALSE, [env, engine, hi, lo, chunk, pcm, frames] {
        std::lock_guard<std::mutex> lock(engine->control);
        const jsize size = env->GetArrayLength(pcm);
        void* bytes = env->GetPrimitiveArrayCritical(pcm, nullptr);
        if (bytes == nullptr) return JNI_FALSE;
        const bool stored =
            engine->core.writeChunk(keyOf(hi, lo, chunk), static_cast<const uint8_t*>(bytes), static_cast<size_t>(size), frames);
        env->ReleasePrimitiveArrayCritical(pcm, bytes, JNI_ABORT);
        return stored ? JNI_TRUE : JNI_FALSE;
    });
}

JNIEXPORT void JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeMarkEmpty(JNIEnv*, jobject, jlong handle, jlong hi,
                                                                                jlong lo, jint chunk) {
    Engine* engine = engineOf(handle);
    guarded<bool>(false, [engine, hi, lo, chunk] {
        std::lock_guard<std::mutex> lock(engine->control);
        engine->core.markChunkEmpty(keyOf(hi, lo, chunk));
        return true;
    });
}

JNIEXPORT void JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeRetain(JNIEnv* env, jobject, jlong handle,
                                                                             jlongArray keys) {
    Engine* engine = engineOf(handle);
    if (keys == nullptr) return;
    guarded<bool>(false, [env, engine, keys] {
        const jsize count = env->GetArrayLength(keys) / kKeyLongs;
        std::vector<jlong> flat(static_cast<size_t>(count) * kKeyLongs);
        env->GetLongArrayRegion(keys, 0, count * kKeyLongs, flat.data());
        std::vector<aria::ChunkKey> parsed;
        parsed.reserve(static_cast<size_t>(count));
        for (size_t i = 0; i < static_cast<size_t>(count); ++i) {
            parsed.push_back(keyOf(flat[i * kKeyLongs], flat[i * kKeyLongs + 1], static_cast<jint>(flat[i * kKeyLongs + 2])));
        }
        std::lock_guard<std::mutex> lock(engine->control);
        engine->core.retainChunks(std::move(parsed));
        return true;
    });
}

JNIEXPORT jboolean JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeSampleStamp(JNIEnv* env, jobject, jlong handle,
                                                                                      jlongArray out) {
    Engine* engine = engineOf(handle);
    if (out == nullptr || env->GetArrayLength(out) < kStampLongs) return JNI_FALSE;
    return guarded<jboolean>(JNI_FALSE, [env, engine, out] {
        std::lock_guard<std::mutex> lock(engine->control);
        const std::optional<aria::OutputTimestamp> stamp = engine->output->timestamp();
        if (!stamp) return JNI_FALSE;
        const jlong values[kStampLongs] = {stamp->framePosition, stamp->monotonicNs};
        env->SetLongArrayRegion(out, 0, kStampLongs, values);
        return JNI_TRUE;
    });
}

JNIEXPORT jint JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeXrunCount(JNIEnv*, jobject, jlong handle) {
    Engine* engine = engineOf(handle);
    return guarded<jint>(0, [engine] {
        std::lock_guard<std::mutex> lock(engine->control);
        return static_cast<jint>(engine->output->xrunCount());
    });
}

JNIEXPORT void JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeGrowBuffer(JNIEnv*, jobject, jlong handle) {
    Engine* engine = engineOf(handle);
    guarded<bool>(false, [engine] {
        std::lock_guard<std::mutex> lock(engine->control);
        engine->output->growBuffer();
        return true;
    });
}

JNIEXPORT void JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeSimulateDisconnect(JNIEnv*, jobject, jlong handle) {
    Engine* engine = engineOf(handle);
    guarded<bool>(false, [engine] {
        std::lock_guard<std::mutex> lock(engine->control);
        engine->output->simulateDisconnectForTests();
        return true;
    });
}

JNIEXPORT jint JNICALL Java_aria_monitor_engine_OboeAudioEngine_nativeConsumeReopenCount(JNIEnv*, jobject, jlong handle) {
    Engine* engine = engineOf(handle);
    return guarded<jint>(0, [engine] {
        std::lock_guard<std::mutex> lock(engine->control);
        return static_cast<jint>(engine->output->consumeReopenCount());
    });
}

}
