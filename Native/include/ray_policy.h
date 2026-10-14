#pragma once
#include <stdint.h>

/* Shared with the CPU test. A disabled, unsupported, empty, or over-cap frame does not dispatch. */
inline int sevk_ray_dispatch_allowed(uint32_t enable, uint32_t supported, uint32_t instances, uint32_t triangles, uint32_t max_instances, uint32_t max_primitives)
{
    if (!enable || !supported || !instances) return 0;
    if (instances > max_instances || triangles > max_primitives) return 0;
    return 1;
}

inline void sevk_ray_capabilities_closed(uint32_t* supported, uint32_t* ray_query)
{
    if (supported) *supported = 0;
    if (ray_query) *ray_query = 0;
}
