#include "../include/ray_policy.h"
#include "../include/ray_transform.h"
#include <limits>
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
    float rows[]{0,-1,0,10, 1,0,0,20, 0,0,1,30}, transform[12]{};
    if (!sevk_ray_instance_transform(rows, transform)) return 1;
    const float point[]{3,-1,2,1}, expected[]{11,23,32};
    for (int row=0;row<3;++row) {
        float actual=0;for(int col=0;col<4;++col)actual+=transform[row*4+col]*point[col];
        if(actual!=expected[row]){printf("rotated ray transform row %d: %f != %f\n",row,actual,expected[row]);return 1;}
    }
    rows[5]=std::numeric_limits<float>::quiet_NaN();
    if(sevk_ray_instance_transform(rows,transform))return 1;
    return 0;
}
