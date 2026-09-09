#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"
#include "aria_shim.h"
#include <stdatomic.h>
#include <string.h>
#include <stdlib.h>

struct aria_engine {
    ma_device device;
    ma_context context;
    int has_context;
    float* ring;
    int capacity_frames;
    int channels;
    _Atomic long head;
    _Atomic long tail;
    _Atomic float master_gain;
};

static void data_callback(ma_device* device, void* output, const void* input, ma_uint32 frame_count)
{
    (void)input;
    aria_engine* engine = (aria_engine*)device->pUserData;
    if (engine == NULL || engine->ring == NULL)
    {
        memset(output, 0, (size_t)frame_count * (size_t)device->playback.channels * sizeof(float));
        return;
    }
    float* out = (float*)output;
    long head = atomic_load_explicit(&engine->head, memory_order_acquire);
    long tail = atomic_load_explicit(&engine->tail, memory_order_acquire);
    long available = head - tail;
    if (available < 0)
    {
        available = 0;
    }
    long total = (long)frame_count;
    long playable = available < total ? available : total;
    long ring_index = tail & (engine->capacity_frames - 1);
    long offset = ring_index * engine->channels;
    long first = engine->capacity_frames - ring_index;
    if (first > playable)
    {
        first = playable;
    }
    if (first > 0)
    {
        memcpy(out, engine->ring + offset, (size_t)first * (size_t)engine->channels * sizeof(float));
    }
    long second = playable - first;
    if (second > 0)
    {
        memcpy(out + first * engine->channels, engine->ring, (size_t)second * (size_t)engine->channels * sizeof(float));
    }
    long written_samples = playable * engine->channels;
    long full_samples = total * engine->channels;
    for (long i = written_samples; i < full_samples; i++)
    {
        out[i] = 0.0f;
    }
    float gain = atomic_load_explicit(&engine->master_gain, memory_order_relaxed);
    for (long i = 0; i < written_samples; i++)
    {
        out[i] *= gain;
    }
    atomic_fetch_add_explicit(&engine->tail, playable, memory_order_release);
}

ARIA_EXPORT int aria_engine_create(int sample_rate, int channels, int block_size_frames, int backend, aria_engine** out_engine)
{
    if (out_engine == NULL || sample_rate <= 0 || channels <= 0 || block_size_frames <= 0)
    {
        return -1;
    }
    *out_engine = NULL;
    aria_engine* engine = (aria_engine*)calloc(1, sizeof(aria_engine));
    if (engine == NULL)
    {
        return MA_OUT_OF_MEMORY;
    }
    long target = (long)block_size_frames * 8;
    if (target < block_size_frames)
    {
        target = block_size_frames;
    }
    int capacity = 1;
    while (capacity < target)
    {
        capacity <<= 1;
    }
    engine->ring = (float*)malloc((size_t)capacity * (size_t)channels * sizeof(float));
    if (engine->ring == NULL)
    {
        free(engine);
        return MA_OUT_OF_MEMORY;
    }
    memset(engine->ring, 0, (size_t)capacity * (size_t)channels * sizeof(float));
    engine->capacity_frames = capacity;
    engine->channels = channels;
    atomic_init(&engine->head, 0);
    atomic_init(&engine->tail, 0);
    atomic_init(&engine->master_gain, 1.0f);

    ma_device_config config = ma_device_config_init(ma_device_type_playback);
    config.playback.format = ma_format_f32;
    config.playback.channels = (ma_uint32)channels;
    config.sampleRate = (ma_uint32)sample_rate;
    config.periodSizeInFrames = (ma_uint32)block_size_frames;
    config.dataCallback = data_callback;
    config.pUserData = engine;

    ma_result result;
    if (backend == 1)
    {
        ma_backend backends[1];
        backends[0] = ma_backend_null;
        ma_context_config context_config = ma_context_config_init();
        result = ma_context_init(backends, 1, &context_config, &engine->context);
        if (result != MA_SUCCESS)
        {
            free(engine->ring);
            free(engine);
            return (int)result;
        }
        engine->has_context = 1;
        result = ma_device_init(&engine->context, &config, &engine->device);
    }
    else
    {
        result = ma_device_init(NULL, &config, &engine->device);
    }
    if (result != MA_SUCCESS)
    {
        if (engine->has_context)
        {
            ma_context_uninit(&engine->context);
        }
        free(engine->ring);
        free(engine);
        return (int)result;
    }
    *out_engine = engine;
    return 0;
}

ARIA_EXPORT int aria_engine_start(aria_engine* engine)
{
    if (engine == NULL)
    {
        return -1;
    }
    return (int)ma_device_start(&engine->device);
}

ARIA_EXPORT int aria_engine_stop(aria_engine* engine)
{
    if (engine == NULL)
    {
        return -1;
    }
    return (int)ma_device_stop(&engine->device);
}

ARIA_EXPORT void aria_engine_destroy(aria_engine* engine)
{
    if (engine == NULL)
    {
        return;
    }
    ma_device_uninit(&engine->device);
    if (engine->has_context)
    {
        ma_context_uninit(&engine->context);
    }
    free(engine->ring);
    free(engine);
}

ARIA_EXPORT int aria_engine_write(aria_engine* engine, const float* data, int frame_count)
{
    if (engine == NULL || engine->ring == NULL || data == NULL || frame_count <= 0)
    {
        return 0;
    }
    long head = atomic_load_explicit(&engine->head, memory_order_relaxed);
    long tail = atomic_load_explicit(&engine->tail, memory_order_acquire);
    long free_frames = engine->capacity_frames - (head - tail);
    long frames = frame_count;
    if (frames > free_frames)
    {
        frames = free_frames;
    }
    if (frames <= 0)
    {
        return 0;
    }
    long ring_index = head & (engine->capacity_frames - 1);
    long offset = ring_index * engine->channels;
    long first = engine->capacity_frames - ring_index;
    if (first > frames)
    {
        first = frames;
    }
    memcpy(engine->ring + offset, data, (size_t)first * (size_t)engine->channels * sizeof(float));
    long second = frames - first;
    if (second > 0)
    {
        memcpy(engine->ring, data + first * engine->channels, (size_t)second * (size_t)engine->channels * sizeof(float));
    }
    atomic_fetch_add_explicit(&engine->head, frames, memory_order_release);
    return (int)frames;
}

ARIA_EXPORT int aria_engine_space(aria_engine* engine)
{
    if (engine == NULL)
    {
        return 0;
    }
    long head = atomic_load_explicit(&engine->head, memory_order_relaxed);
    long tail = atomic_load_explicit(&engine->tail, memory_order_acquire);
    long free_frames = engine->capacity_frames - (head - tail);
    if (free_frames < 0)
    {
        free_frames = 0;
    }
    return (int)free_frames;
}

ARIA_EXPORT void aria_engine_set_master_gain(aria_engine* engine, float gain)
{
    if (engine == NULL)
    {
        return;
    }
    atomic_store_explicit(&engine->master_gain, gain, memory_order_relaxed);
}

ARIA_EXPORT void aria_engine_flush(aria_engine* engine)
{
    if (engine == NULL)
    {
        return;
    }
    atomic_store_explicit(&engine->tail, atomic_load_explicit(&engine->head, memory_order_acquire), memory_order_release);
}

ARIA_EXPORT long aria_engine_played_frames(aria_engine* engine)
{
    if (engine == NULL)
    {
        return 0;
    }
    long tail = atomic_load_explicit(&engine->tail, memory_order_acquire);
    long head = atomic_load_explicit(&engine->head, memory_order_relaxed);
    if (tail > head)
    {
        return head;
    }
    return tail;
}

struct aria_decoder
{
    ma_decoder decoder;
};

ARIA_EXPORT int aria_decoder_open(const char* path, int sample_rate, int channels, aria_decoder** out_decoder)
{
    if (out_decoder == NULL || path == NULL)
    {
        return -1;
    }
    *out_decoder = NULL;
    aria_decoder* decoder = (aria_decoder*)calloc(1, sizeof(aria_decoder));
    if (decoder == NULL)
    {
        return MA_OUT_OF_MEMORY;
    }
    ma_decoder_config config = ma_decoder_config_init(ma_format_f32, (ma_uint32)channels, (ma_uint32)sample_rate);
    ma_result result = ma_decoder_init_file(path, &config, &decoder->decoder);
    if (result != MA_SUCCESS)
    {
        free(decoder);
        return (int)result;
    }
    *out_decoder = decoder;
    return 0;
}

ARIA_EXPORT int aria_decoder_read(aria_decoder* decoder, float* out, int frame_count)
{
    if (decoder == NULL || out == NULL || frame_count <= 0)
    {
        return 0;
    }
    ma_uint64 frames_read = 0;
    ma_result result = ma_decoder_read_pcm_frames(&decoder->decoder, out, (ma_uint64)frame_count, &frames_read);
    if (result != MA_SUCCESS && result != MA_AT_END)
    {
        return 0;
    }
    if (frames_read > (ma_uint64)frame_count)
    {
        frames_read = (ma_uint64)frame_count;
    }
    return (int)frames_read;
}

ARIA_EXPORT void aria_decoder_close(aria_decoder* decoder)
{
    if (decoder == NULL)
    {
        return;
    }
    ma_decoder_uninit(&decoder->decoder);
    free(decoder);
}
