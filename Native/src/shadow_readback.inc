void readback_lighting_input(uint32_t input,void* data,uint64_t bytes){
    PostImage* target=input==0?&post.ao:input==2?&post.tone:input==3?((post.config.flags&32)?&post.fxaa:&post.tone):nullptr;
    // Input 8 is the completed R16G16 object-motion target; 9..11 are the pack attachment slots.
    WorldState::AttachTarget* attach=input>=9&&input<12?&world.attach[input-9]:nullptr;
    if(attach&&(!attach->image.handle||!attach->sampled||attach->w!=world.width||attach->h!=world.height))throw std::runtime_error("GBuffer attachment unavailable for readback");
    static constexpr uint32_t attach_bytes[]{4,8,4,4,4,8};
    if(input==8&&(!world.motion.handle||!world.motion_sampled||world.motion_w!=world.width||world.motion_h!=world.height))throw std::runtime_error("object motion target unavailable for readback");
    if(recording || !last_image_valid || !world.initialized || input>11 || !data || bytes!=uint64_t(world.width)*world.height*(attach?attach_bytes[attach->format]:input>=2?4:1) ||
       (target && (!target->image.handle || target->layout!=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL || target->width!=world.width || target->height!=world.height ||
        (input>=2 && target->format!=VK_FORMAT_R8G8B8A8_UNORM))))throw std::runtime_error("invalid completed lighting-input readback");
    wait(next_completion);auto& image=target?target->image:attach?attach->image:input==8?world.motion:input>=4?world.gbuffer[input-4]:world.shadows;Buffer staging{};VkImageAspectFlags aspect=input==7?VK_IMAGE_ASPECT_DEPTH_BIT:VK_IMAGE_ASPECT_COLOR_BIT;
    try{staging=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);immediate([&](VkCommandBuffer cmd){
        barrier(cmd,image.handle,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,aspect,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
        VkBufferImageCopy copy{};copy.imageSubresource={aspect,0,0,1};copy.imageExtent={world.width,world.height,1};vkCmdCopyImageToBuffer(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,staging.handle,1,&copy);
        barrier(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,aspect,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT);
    });check(vmaInvalidateAllocation(allocator,staging.memory,0,bytes),"completed lighting-input readback");std::memcpy(data,staging.mapped,bytes);destroy_buffer(staging);}catch(...){destroy_buffer(staging);throw;}
}

void readback_shadow(uint32_t cascade,void* data,uint64_t bytes){
    if(recording || !last_image_valid || cascade>=shadow_count || !data || bytes!=uint64_t(shadow_resolution)*shadow_resolution*4)throw std::runtime_error("invalid explicit shadow readback");
    wait(next_completion);auto& image=shadow_images[cascade];Buffer staging{};
    try{staging=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);immediate([&](VkCommandBuffer cmd){
        barrier(cmd,image.handle,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
        VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_DEPTH_BIT,0,0,1};copy.imageExtent={shadow_resolution,shadow_resolution,1};vkCmdCopyImageToBuffer(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,staging.handle,1,&copy);
        barrier(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    });check(vmaInvalidateAllocation(allocator,staging.memory,0,bytes),"explicit completed shadow readback");std::memcpy(data,staging.mapped,bytes);destroy_buffer(staging);}catch(...){destroy_buffer(staging);throw;}
}

void readback_spot(uint32_t light,void* data,uint64_t bytes){
    if(recording || !last_image_valid || light>=64 || !spots.images[light].handle || !data || bytes!=uint64_t(spots.resolution)*spots.resolution*4)throw std::runtime_error("invalid explicit shadow readback");
    wait(next_completion);auto& image=spots.images[light];Buffer staging{};
    try{staging=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);immediate([&](VkCommandBuffer cmd){
        barrier(cmd,image.handle,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
        VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_DEPTH_BIT,0,0,1};copy.imageExtent={spots.resolution,spots.resolution,1};vkCmdCopyImageToBuffer(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,staging.handle,1,&copy);
        barrier(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    });check(vmaInvalidateAllocation(allocator,staging.memory,0,bytes),"explicit completed shadow readback");std::memcpy(data,staging.mapped,bytes);destroy_buffer(staging);}catch(...){destroy_buffer(staging);throw;}
}
void set_lighting_diagnostics(uint32_t enabled){
    if(recording || enabled>1)throw std::runtime_error("invalid lighting diagnostic state");
    world.lighting_diagnostics=enabled!=0;
    if(!enabled){wait(next_completion);for(auto& image:world.lighting_stages)destroy_image(image);for(auto& image:world.lighting_terms)destroy_image(image);if(world.lighting_terms_sampler)vkDestroySampler(device,world.lighting_terms_sampler,nullptr);world.lighting_terms_sampler={};world.lighting_stage_valid=0;world.lighting_stage_width=world.lighting_stage_height=0;world.lighting_stage_format={};}
}
void set_lighting_diagnostic_sampler(uint32_t mode){
    if(recording || mode>1)throw std::runtime_error("invalid lighting diagnostic sampler mode");wait(next_completion);
    VkSampler sampler{};if(mode){
        if(anisotropy_limit<2.f)throw std::runtime_error("diagnostic anisotropic sampler is unsupported");
        VkSamplerCreateInfo s{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO};s.magFilter=s.minFilter=VK_FILTER_LINEAR;s.mipmapMode=VK_SAMPLER_MIPMAP_MODE_LINEAR;s.addressModeU=s.addressModeV=s.addressModeW=VK_SAMPLER_ADDRESS_MODE_REPEAT;s.maxLod=VK_LOD_CLAMP_NONE;s.anisotropyEnable=VK_TRUE;s.maxAnisotropy=1;
        check(vkCreateSampler(device,&s,nullptr,&sampler),"create isolated diagnostic anisotropic sampler");
    }
    if(world.lighting_terms_sampler)vkDestroySampler(device,world.lighting_terms_sampler,nullptr);world.lighting_terms_sampler=sampler;world.lighting_stage_valid&=~0x7f0u;
}
void readback_lighting_stage(uint32_t stage,void* data,uint64_t bytes){
    if(recording || !last_image_valid || !world.initialized || stage>10 || !data || bytes!=uint64_t(world.width)*world.height*16 ||
       (stage!=3 && (!world.lighting_diagnostics || !(world.lighting_stage_valid&(1u<<stage)) || world.lighting_stage_width!=world.width || world.lighting_stage_height!=world.height)))throw std::runtime_error("invalid completed lighting-stage readback");
    wait(next_completion);auto& image=stage==3?world.hdr:stage>=4?world.lighting_terms[stage-4]:world.lighting_stages[stage];VkFormat format=stage==3?world.hdr_format:stage>=4?VK_FORMAT_R32G32B32A32_SFLOAT:world.lighting_stage_format;
    if(stage>=4){Buffer staging{};try{staging=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);immediate([&](VkCommandBuffer cmd){
        barrier(cmd,image.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
        VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};copy.imageExtent={world.width,world.height,1};vkCmdCopyImageToBuffer(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,staging.handle,1,&copy);
        barrier(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    });check(vmaInvalidateAllocation(allocator,staging.memory,0,bytes),"completed lighting-term readback");std::memcpy(data,staging.mapped,bytes);destroy_buffer(staging);}catch(...){destroy_buffer(staging);throw;}return;}
    bool half=format==VK_FORMAT_R16G16B16A16_SFLOAT;if(!half && format!=VK_FORMAT_B10G11R11_UFLOAT_PACK32)throw std::runtime_error("unsupported lighting-stage format");
    uint64_t raw_bytes=uint64_t(world.width)*world.height*(half?8:4);Buffer staging{};VkImageLayout prior=stage==3?VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    try{staging=make_buffer(raw_bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);immediate([&](VkCommandBuffer cmd){
        barrier(cmd,image.handle,prior,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
        VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};copy.imageExtent={world.width,world.height,1};vkCmdCopyImageToBuffer(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,staging.handle,1,&copy);
        barrier(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,prior,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT);
    });check(vmaInvalidateAllocation(allocator,staging.memory,0,raw_bytes),"completed lighting-stage readback");
    auto decode=[](uint32_t bits,uint32_t mantissa){uint32_t exponent=(bits>>mantissa)&31u,fraction=bits&((1u<<mantissa)-1);return exponent==31?fraction?std::numeric_limits<float>::quiet_NaN():std::numeric_limits<float>::infinity():exponent?std::ldexp(1.f+float(fraction)/float(1u<<mantissa),int(exponent)-15):std::ldexp(float(fraction),-14-int(mantissa));};
    for(uint64_t pixel=0;pixel<uint64_t(world.width)*world.height;++pixel){float value[4];if(half){uint16_t raw[4];std::memcpy(raw,static_cast<const uint8_t*>(staging.mapped)+pixel*8,8);for(uint32_t c=0;c<4;++c)value[c]=(raw[c]&0x8000u?-1.f:1.f)*decode(raw[c]&0x7fffu,10);}else{uint32_t raw;std::memcpy(&raw,static_cast<const uint8_t*>(staging.mapped)+pixel*4,4);value[0]=decode(raw&2047u,6);value[1]=decode((raw>>11)&2047u,6);value[2]=decode(raw>>22,5);value[3]=1;}std::memcpy(static_cast<uint8_t*>(data)+pixel*16,value,16);}
    destroy_buffer(staging);}catch(...){destroy_buffer(staging);throw;}
}
