#include "../include/ray_policy.h"
#include "../include/se_vulkan.h"
#include <stdio.h>
int main()
{
    if (sizeof(SevkRayCapabilities) != 48) { printf("capabilities %zu\n", sizeof(SevkRayCapabilities)); return 1; }
    if (sizeof(SevkRayOptions) != 32) { printf("options %zu\n", sizeof(SevkRayOptions)); return 1; }
    uint32_t supported = 1, query = 1;
    sevk_ray_capabilities_closed(&supported, &query);
    if (supported || query) return 1;
    if (sevk_ray_dispatch_allowed(0, 1, 1, 1, 8, 8)) return 1;
    if (sevk_ray_dispatch_allowed(1, 0, 1, 1, 8, 8)) return 1;
    if (sevk_ray_dispatch_allowed(1, 1, 0, 0, 8, 8)) return 1;
    if (sevk_ray_dispatch_allowed(1, 1, 9, 1, 8, 8)) return 1;
    if (sevk_ray_dispatch_allowed(1, 1, 1, 9, 8, 8)) return 1;
    if (!sevk_ray_dispatch_allowed(1, 1, 1, 1, 8, 8)) return 1;
    return 0;
}
