#pragma once
#include <stdint.h>
#if defined(_WIN32)
  #define ARIA_EXPORT __declspec(dllexport)
#else
  #define ARIA_EXPORT __attribute__((visibility("default")))
#endif
typedef struct aria_engine aria_engine;
ARIA_EXPORT int aria_engine_create(int sample_rate, int channels, int block_size_frames, int backend, aria_engine** out_engine);
ARIA_EXPORT int aria_engine_start(aria_engine* engine);
ARIA_EXPORT int aria_engine_stop(aria_engine* engine);
ARIA_EXPORT void aria_engine_destroy(aria_engine* engine);
ARIA_EXPORT int aria_engine_write(aria_engine* engine, const float* data, int frame_count);
ARIA_EXPORT int aria_engine_space(aria_engine* engine);
ARIA_EXPORT void aria_engine_set_master_gain(aria_engine* engine, float gain);
ARIA_EXPORT void aria_engine_flush(aria_engine* engine);
ARIA_EXPORT long aria_engine_played_frames(aria_engine* engine);
typedef struct aria_decoder aria_decoder;
ARIA_EXPORT int aria_decoder_open(const char* path, int sample_rate, int channels, aria_decoder** out_decoder);
ARIA_EXPORT int aria_decoder_read(aria_decoder* decoder, float* out, int frame_count);
ARIA_EXPORT void aria_decoder_close(aria_decoder* decoder);
