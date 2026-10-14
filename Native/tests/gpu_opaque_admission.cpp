#include "se_vulkan.h"
#include "../src/gpu_opaque_admission.h"
#include <stdexcept>
#include <iostream>
void require(bool value){if(!value)throw std::runtime_error("opaque admission contract");}
int main(){
    SevkGpuOpaqueCapabilities c{sizeof(c),SEVK_ABI_VERSION};c.available_features=255;
    c.max_draw_count=65536;c.max_storage_range=128u*1024*1024;
    c.max_stage_storage=c.max_set_storage=gpu_opaque::storage_capacity;c.max_stage_textures=c.max_set_textures=8192;
    c.max_group_invocations=c.max_group_x=64;c.max_stage_resources=8192+gpu_opaque::storage_capacity+1;gpu_opaque::admit(c);
    require(c.supported&&!c.enabled);c.requested=1;gpu_opaque::admit(c);require(c.enabled);
    for(uint32_t bit=1;bit<=128;bit*=2){auto missing=c;missing.available_features&=~bit;gpu_opaque::admit(missing);require(!missing.supported&&!missing.enabled&&missing.missing_features==bit);}
    for(uint32_t field=0;field<8;field++){auto missing=c;uint32_t* limits=&missing.max_draw_count;limits[field]=0;gpu_opaque::admit(missing);require(!missing.enabled&&missing.insufficient_limits);}
    auto stage=c;stage.max_stage_resources--;gpu_opaque::admit(stage);require(!stage.enabled&&(stage.insufficient_limits&SEVK_OPAQUE_TEXTURE_DESCRIPTORS));
    std::cout<<"GPU opaque optional admission and bounded limit contracts passed\n";
}
