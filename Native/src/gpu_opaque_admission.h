#pragma once
#include "se_gpu_opaque.h"
namespace gpu_opaque {
constexpr uint32_t required_features=255, texture_capacity=4096,
    storage_capacity=20, draw_capacity=65536, storage_range=128u*1024*1024,
    workgroup_size=64, max_parts=65535, max_batches=max_parts;
// NEW batches retain part identity. At 64 views their count table is <17 MiB,
// within the existing 128 MiB storage-range admission; no new GPU feature.
static_assert((uint64_t(max_batches)*64+1+64)*4<=storage_range);
// Linear work uses a two-dimensional dispatch above 65535 workgroups.
// At 64 views, group storage still fits the admitted 128 MiB range.
inline void admit(SevkGpuOpaqueCapabilities& c) {
    c.texture_capacity=texture_capacity; c.storage_capacity=storage_capacity;
    c.draw_capacity=draw_capacity; c.missing_features=required_features&~c.available_features;
    c.insufficient_limits=(c.max_draw_count<draw_capacity?SEVK_OPAQUE_DRAW_LIMIT:0)
        |(c.max_storage_range<storage_range?SEVK_OPAQUE_STORAGE_RANGE:0)
        |(c.max_stage_storage<storage_capacity||c.max_set_storage<storage_capacity?SEVK_OPAQUE_STORAGE_DESCRIPTORS:0)
        |(c.max_stage_textures<texture_capacity*2||c.max_set_textures<texture_capacity*2||c.max_stage_resources<texture_capacity*2+storage_capacity+1?SEVK_OPAQUE_TEXTURE_DESCRIPTORS:0)
        |(c.max_group_invocations<workgroup_size||c.max_group_x<workgroup_size?SEVK_OPAQUE_COMPUTE_GROUP:0);
    c.supported=!c.missing_features&&!c.insufficient_limits;
    c.enabled=c.supported&&c.requested;
}
}
