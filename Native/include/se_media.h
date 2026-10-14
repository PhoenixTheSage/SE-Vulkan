#pragma once
#include <stdint.h>
/* Included from se_vulkan.h. Owner-thread calls; all strings are UTF-8.
 * Workers only publish CPU pixels. No media API calls Vulkan.
 * update's pixel pointer remains valid until the next update/close of this ID.
 * state: 0 opening, 1 playing, 2 stopped, 3 failed. Unknown IDs return 1. */
typedef struct SevkMediaFrame {
    uint32_t size, abi_version, state, width, height, reserved; /* flags: 1 audio stream, 2 valid A/V offset, 4 real audio backend, 8 BGRA pixels */
    uint64_t sequence, frames, dropped, conversion_us;
    int64_t position_us, av_offset_us;
    const void* pixels;
    char error[512];
} SevkMediaFrame;
SEVK_API int32_t SEVK_CALL sevk_media_create(const char* runtime_directory, void** media);
SEVK_API int32_t SEVK_CALL sevk_media_play(void* media, uint32_t id, const char* path, float volume);
SEVK_API int32_t SEVK_CALL sevk_media_update(void* media, uint32_t id, SevkMediaFrame* frame);
SEVK_API int32_t SEVK_CALL sevk_media_volume(void* media, uint32_t id, float volume);
SEVK_API int32_t SEVK_CALL sevk_media_close(void* media, uint32_t id);
SEVK_API void SEVK_CALL sevk_media_destroy(void* media);
SEVK_API int32_t SEVK_CALL sevk_update_stream_texture(void* context, uint64_t id, uint32_t width, uint32_t height, uint32_t format, const void* pixels, uint64_t bytes); /* format: 0 RGBA, 1 BGRA; sRGB view */
