#pragma once
#include <stdint.h>
/* Optional GPU opaque admission. Availability and enablement are distinct.
 * enabled reports the admitted device features, not live scene ownership.
 * Features are OFF unless SE_VULKAN_GPU_OPAQUE=1 at device creation. No
 * update-after-bind features are required: descriptor tables are slot-owned. */
enum SevkGpuOpaqueFeature {
    SEVK_OPAQUE_MULTI_DRAW=1, SEVK_OPAQUE_INDIRECT_COUNT=2,
    SEVK_OPAQUE_FIRST_INSTANCE=4, SEVK_OPAQUE_DRAW_PARAMETERS=8,
    SEVK_OPAQUE_FLOAT64=16, SEVK_OPAQUE_RUNTIME_ARRAY=32,
    SEVK_OPAQUE_NONUNIFORM_TEXTURE=64, SEVK_OPAQUE_COMPUTE_QUEUE=128
};
enum SevkGpuOpaqueLimit {
    SEVK_OPAQUE_DRAW_LIMIT=1, SEVK_OPAQUE_STORAGE_RANGE=2,
    SEVK_OPAQUE_STORAGE_DESCRIPTORS=4, SEVK_OPAQUE_TEXTURE_DESCRIPTORS=8,
    SEVK_OPAQUE_COMPUTE_GROUP=16
};
typedef struct SevkGpuOpaqueCapabilities {
    uint32_t size, abi_version, supported, enabled;
    uint32_t available_features, missing_features, insufficient_limits, requested;
    uint32_t max_draw_count, max_storage_range, max_stage_storage, max_set_storage;
    uint32_t max_stage_textures, max_set_textures, max_group_invocations, max_group_x;
    uint32_t texture_capacity, storage_capacity, draw_capacity, max_stage_resources;
} SevkGpuOpaqueCapabilities;
SEVK_API int32_t SEVK_CALL sevk_get_gpu_opaque_capabilities(void* ctx,SevkGpuOpaqueCapabilities* caps);
