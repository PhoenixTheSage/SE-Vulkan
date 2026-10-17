#pragma once
#include <cmath>
#include <cstring>

// Raster object constants and generic instances use three row-major float4
// rows. Vulkan's instance transform consumes exactly that same 3x4 layout.
inline bool sevk_ray_instance_transform(const float* rows, float* transform) {
    for (int i = 0; i < 12; ++i) if (!std::isfinite(rows[i])) return false;
    std::memcpy(transform, rows, 12 * sizeof(float));
    return true;
}
