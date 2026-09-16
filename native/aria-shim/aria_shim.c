#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"
#define STB_VORBIS_HEADER_ONLY
#include "stb_vorbis.c"
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

#define ARIA_VORBIS_MAX_BLOCK_FRAMES 8192

typedef struct
{
    ma_data_source_base base;
    stb_vorbis* vorbis;
    ma_uint32 channels;
    ma_uint32 sample_rate;
    float* leftover;
    ma_uint32 leftover_frames;
    ma_uint32 leftover_cursor;
} aria_vorbis_source;

static ma_result aria_vorbis_read(ma_data_source* pDataSource, void* pFramesOut, ma_uint64 frameCount, ma_uint64* pFramesRead)
{
    aria_vorbis_source* source = (aria_vorbis_source*)pDataSource;
    float* out = (float*)pFramesOut;
    ma_uint64 total = 0;
    ma_uint32 channels = source->channels;
    if (pFramesOut == NULL)
    {
        if (source->leftover_cursor < source->leftover_frames)
        {
            ma_uint64 skip = source->leftover_frames - source->leftover_cursor;
            if (skip > frameCount)
            {
                skip = frameCount;
            }
            source->leftover_cursor += (ma_uint32)skip;
            *pFramesRead = skip;
            return MA_SUCCESS;
        }
        *pFramesRead = 0;
        return MA_SUCCESS;
    }
    while (total < frameCount)
    {
        if (source->leftover_cursor < source->leftover_frames)
        {
            ma_uint64 take = source->leftover_frames - source->leftover_cursor;
            if (take > frameCount - total)
            {
                take = frameCount - total;
            }
            memcpy(out + total * channels, source->leftover + (size_t)source->leftover_cursor * channels, (size_t)take * channels * sizeof(float));
            source->leftover_cursor += (ma_uint32)take;
            total += take;
            continue;
        }
        float** frames = NULL;
        int decoded_channels = 0;
        int decoded = stb_vorbis_get_frame_float(source->vorbis, &decoded_channels, &frames);
        if (decoded <= 0)
        {
            break;
        }
        ma_uint32 buffered = (ma_uint32)decoded;
        if (buffered > ARIA_VORBIS_MAX_BLOCK_FRAMES)
        {
            buffered = ARIA_VORBIS_MAX_BLOCK_FRAMES;
        }
        for (ma_uint32 i = 0; i < buffered; i++)
        {
            for (ma_uint32 c = 0; c < channels; c++)
            {
                source->leftover[(size_t)i * channels + c] = (c < (ma_uint32)decoded_channels) ? frames[c][i] : 0.0f;
            }
        }
        source->leftover_frames = buffered;
        source->leftover_cursor = 0;
    }
    *pFramesRead = total;
    return total > 0 ? MA_SUCCESS : MA_AT_END;
}

static ma_result aria_vorbis_seek(ma_data_source* pDataSource, ma_uint64 frameIndex)
{
    aria_vorbis_source* source = (aria_vorbis_source*)pDataSource;
    source->leftover_frames = 0;
    source->leftover_cursor = 0;
    if (frameIndex > 0xFFFFFFFFull)
    {
        frameIndex = 0xFFFFFFFFull;
    }
    if (stb_vorbis_seek(source->vorbis, (unsigned int)frameIndex) == 0)
    {
        return MA_INVALID_ARGS;
    }
    return MA_SUCCESS;
}

static ma_result aria_vorbis_get_data_format(ma_data_source* pDataSource, ma_format* pFormat, ma_uint32* pChannels, ma_uint32* pSampleRate, ma_channel* pChannelMap, size_t channelMapCap)
{
    aria_vorbis_source* source = (aria_vorbis_source*)pDataSource;
    if (pFormat != NULL)
    {
        *pFormat = ma_format_f32;
    }
    if (pChannels != NULL)
    {
        *pChannels = source->channels;
    }
    if (pSampleRate != NULL)
    {
        *pSampleRate = source->sample_rate;
    }
    if (pChannelMap != NULL)
    {
        ma_channel_map_init_standard(ma_standard_channel_map_default, pChannelMap, channelMapCap, source->channels);
    }
    return MA_SUCCESS;
}

static ma_result aria_vorbis_get_cursor(ma_data_source* pDataSource, ma_uint64* pCursor)
{
    aria_vorbis_source* source = (aria_vorbis_source*)pDataSource;
    ma_int64 cursor = (ma_int64)stb_vorbis_get_sample_offset(source->vorbis) - (ma_int64)(source->leftover_frames - source->leftover_cursor);
    if (cursor < 0)
    {
        cursor = 0;
    }
    *pCursor = (ma_uint64)cursor;
    return MA_SUCCESS;
}

static ma_result aria_vorbis_get_length(ma_data_source* pDataSource, ma_uint64* pLength)
{
    aria_vorbis_source* source = (aria_vorbis_source*)pDataSource;
    unsigned int length = stb_vorbis_stream_length_in_samples(source->vorbis);
    if (length == 0)
    {
        return MA_NOT_IMPLEMENTED;
    }
    *pLength = (ma_uint64)length;
    return MA_SUCCESS;
}

static ma_result aria_vorbis_set_looping(ma_data_source* pDataSource, ma_bool32 isLooping)
{
    (void)pDataSource;
    (void)isLooping;
    return MA_NOT_IMPLEMENTED;
}

static ma_data_source_vtable g_aria_vorbis_ds_vtable =
{
    aria_vorbis_read,
    aria_vorbis_seek,
    aria_vorbis_get_data_format,
    aria_vorbis_get_cursor,
    aria_vorbis_get_length,
    aria_vorbis_set_looping,
    0
};

static ma_result aria_vorbis_source_create(stb_vorbis* vorbis, aria_vorbis_source** ppSource)
{
    stb_vorbis_info info = stb_vorbis_get_info(vorbis);
    if (info.channels <= 0 || info.channels > 254)
    {
        stb_vorbis_close(vorbis);
        return MA_INVALID_FILE;
    }
    aria_vorbis_source* source = (aria_vorbis_source*)calloc(1, sizeof(aria_vorbis_source));
    if (source == NULL)
    {
        stb_vorbis_close(vorbis);
        return MA_OUT_OF_MEMORY;
    }
    source->leftover = (float*)malloc((size_t)ARIA_VORBIS_MAX_BLOCK_FRAMES * (size_t)info.channels * sizeof(float));
    if (source->leftover == NULL)
    {
        free(source);
        stb_vorbis_close(vorbis);
        return MA_OUT_OF_MEMORY;
    }
    ma_data_source_config ds_config = ma_data_source_config_init();
    ds_config.vtable = &g_aria_vorbis_ds_vtable;
    ma_result result = ma_data_source_init(&ds_config, &source->base);
    if (result != MA_SUCCESS)
    {
        free(source->leftover);
        free(source);
        stb_vorbis_close(vorbis);
        return result;
    }
    source->vorbis = vorbis;
    source->channels = (ma_uint32)info.channels;
    source->sample_rate = info.sample_rate;
    *ppSource = source;
    return MA_SUCCESS;
}

static ma_result aria_vorbis_on_init(void* pUserData, ma_read_proc onRead, ma_seek_proc onSeek, ma_tell_proc onTell, void* pReadSeekTellUserData, const ma_decoding_backend_config* pConfig, const ma_allocation_callbacks* pAllocationCallbacks, ma_data_source** ppBackend)
{
    (void)pUserData;
    (void)pConfig;
    (void)pAllocationCallbacks;
    *ppBackend = NULL;
    if (onRead == NULL || onSeek == NULL || onTell == NULL)
    {
        return MA_INVALID_ARGS;
    }
    if (onSeek(pReadSeekTellUserData, 0, ma_seek_origin_end) != MA_SUCCESS)
    {
        return MA_INVALID_FILE;
    }
    ma_int64 size = 0;
    if (onTell(pReadSeekTellUserData, &size) != MA_SUCCESS || size <= 0)
    {
        return MA_INVALID_FILE;
    }
    if (onSeek(pReadSeekTellUserData, 0, ma_seek_origin_start) != MA_SUCCESS)
    {
        return MA_INVALID_FILE;
    }
    if (size > 1024 * 1024 * 1024)
    {
        return MA_INVALID_FILE;
    }
    unsigned char* data = (unsigned char*)malloc((size_t)size);
    if (data == NULL)
    {
        return MA_OUT_OF_MEMORY;
    }
    size_t total = 0;
    while (total < (size_t)size)
    {
        size_t got = 0;
        if (onRead(pReadSeekTellUserData, data + total, (size_t)size - total, &got) != MA_SUCCESS || got == 0)
        {
            free(data);
            return MA_INVALID_FILE;
        }
        total += got;
    }
    int error = 0;
    stb_vorbis* vorbis = stb_vorbis_open_memory(data, (int)size, &error, NULL);
    free(data);
    if (vorbis == NULL)
    {
        return MA_INVALID_FILE;
    }
    aria_vorbis_source* source = NULL;
    ma_result result = aria_vorbis_source_create(vorbis, &source);
    if (result != MA_SUCCESS)
    {
        return result;
    }
    *ppBackend = &source->base;
    return MA_SUCCESS;
}

static ma_result aria_vorbis_on_init_file(void* pUserData, const char* pFilePath, const ma_decoding_backend_config* pConfig, const ma_allocation_callbacks* pAllocationCallbacks, ma_data_source** ppBackend)
{
    (void)pUserData;
    (void)pConfig;
    (void)pAllocationCallbacks;
    *ppBackend = NULL;
    if (pFilePath == NULL)
    {
        return MA_INVALID_ARGS;
    }
    int error = 0;
    stb_vorbis* vorbis = stb_vorbis_open_filename(pFilePath, &error, NULL);
    if (vorbis == NULL)
    {
        return MA_INVALID_FILE;
    }
    aria_vorbis_source* source = NULL;
    ma_result result = aria_vorbis_source_create(vorbis, &source);
    if (result != MA_SUCCESS)
    {
        return result;
    }
    *ppBackend = &source->base;
    return MA_SUCCESS;
}

static ma_result aria_vorbis_on_init_memory(void* pUserData, const void* pData, size_t dataSize, const ma_decoding_backend_config* pConfig, const ma_allocation_callbacks* pAllocationCallbacks, ma_data_source** ppBackend)
{
    (void)pUserData;
    (void)pConfig;
    (void)pAllocationCallbacks;
    *ppBackend = NULL;
    if (pData == NULL || dataSize == 0 || dataSize > 1024 * 1024 * 1024)
    {
        return MA_INVALID_ARGS;
    }
    int error = 0;
    stb_vorbis* vorbis = stb_vorbis_open_memory((const unsigned char*)pData, (int)dataSize, &error, NULL);
    if (vorbis == NULL)
    {
        return MA_INVALID_FILE;
    }
    aria_vorbis_source* source = NULL;
    ma_result result = aria_vorbis_source_create(vorbis, &source);
    if (result != MA_SUCCESS)
    {
        return result;
    }
    *ppBackend = &source->base;
    return MA_SUCCESS;
}

static void aria_vorbis_on_uninit(void* pUserData, ma_data_source* pBackend, const ma_allocation_callbacks* pAllocationCallbacks)
{
    (void)pUserData;
    (void)pAllocationCallbacks;
    if (pBackend == NULL)
    {
        return;
    }
    aria_vorbis_source* source = (aria_vorbis_source*)pBackend;
    if (source->vorbis != NULL)
    {
        stb_vorbis_close(source->vorbis);
    }
    ma_data_source_uninit(&source->base);
    free(source->leftover);
    free(source);
}

static ma_decoding_backend_vtable g_aria_vorbis_backend_vtable =
{
    aria_vorbis_on_init,
    aria_vorbis_on_init_file,
    NULL,
    aria_vorbis_on_init_memory,
    aria_vorbis_on_uninit
};

static const ma_decoding_backend_vtable* g_aria_vorbis_backends[] = { &g_aria_vorbis_backend_vtable };

struct aria_decoder
{
    ma_decoder decoder;
};

static int aria_decoder_open_impl(const void* path, int wide, int sample_rate, int channels, aria_decoder** out_decoder)
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
    config.ppCustomBackendVTables = (ma_decoding_backend_vtable**)g_aria_vorbis_backends;
    config.customBackendCount = 1;
    config.pCustomBackendUserData = NULL;
    ma_result result = wide
        ? ma_decoder_init_file_w((const wchar_t*)path, &config, &decoder->decoder)
        : ma_decoder_init_file((const char*)path, &config, &decoder->decoder);
    if (result != MA_SUCCESS)
    {
        free(decoder);
        return (int)result;
    }
    *out_decoder = decoder;
    return 0;
}

ARIA_EXPORT int aria_decoder_open(const char* path, int sample_rate, int channels, aria_decoder** out_decoder)
{
    return aria_decoder_open_impl(path, 0, sample_rate, channels, out_decoder);
}

ARIA_EXPORT int aria_decoder_open_w(const wchar_t* path, int sample_rate, int channels, aria_decoder** out_decoder)
{
    return aria_decoder_open_impl(path, 1, sample_rate, channels, out_decoder);
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

ARIA_EXPORT int aria_decoder_seek(aria_decoder* decoder, long frame_index)
{
    if (decoder == NULL || frame_index < 0)
    {
        return -1;
    }
    return (int)ma_decoder_seek_to_pcm_frame(&decoder->decoder, (ma_uint64)frame_index);
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
