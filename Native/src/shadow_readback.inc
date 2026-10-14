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
