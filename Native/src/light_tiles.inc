void init_light_tiles(const void* spirv,uint64_t bytes){
    if(recording||light_tiles.pipeline)throw std::runtime_error("invalid light tile initialization");
    SpvReflectShaderModule reflection{};VkShaderModule module{};
    try{
        module=world_shader(spirv,bytes,VK_SHADER_STAGE_COMPUTE_BIT,reflection);
        if(reflection.push_constant_block_count||reflection.entry_points[0].local_size.x!=16||reflection.entry_points[0].local_size.y!=16||reflection.entry_points[0].local_size.z!=1)throw std::runtime_error("stock light tile workgroup mismatch");
        std::vector<VkDescriptorSetLayoutBinding> bindings;
        for(uint32_t i=0;i<reflection.descriptor_binding_count;++i){const auto& b=reflection.descriptor_bindings[i];
            bool ok=b.set==0&&b.count==1&&((b.binding==0&&b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.block.size==1280)||
                (b.binding==1&&b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.block.size==16)||
                (b.binding==128&&b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE)||
                ((b.binding==141||b.binding==384)&&b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_STORAGE_BUFFER));
            if(!ok)throw std::runtime_error("stock light tile descriptor mismatch: binding="+std::to_string(b.binding)+" type="+std::to_string(b.descriptor_type)+" bytes="+std::to_string(b.block.size));
            bindings.push_back({b.binding,VkDescriptorType(b.descriptor_type),1,VK_SHADER_STAGE_COMPUTE_BIT,nullptr});
        }
        if(bindings.size()!=5)throw std::runtime_error("incomplete stock light tile bindings");
        VkDescriptorSetLayoutCreateInfo layout{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};layout.bindingCount=uint32_t(bindings.size());layout.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&layout,nullptr,&light_tiles.layout),"create stock light tile layout");
        VkPipelineLayoutCreateInfo pipeline_layout_info{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};pipeline_layout_info.setLayoutCount=1;pipeline_layout_info.pSetLayouts=&light_tiles.layout;check(vkCreatePipelineLayout(device,&pipeline_layout_info,nullptr,&light_tiles.pipeline_layout),"create stock light tile pipeline layout");
        VkComputePipelineCreateInfo pipeline{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};pipeline.layout=light_tiles.pipeline_layout;pipeline.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO};pipeline.stage.stage=VK_SHADER_STAGE_COMPUTE_BIT;pipeline.stage.module=module;pipeline.stage.pName="__compute_shader";check(vkCreateComputePipelines(device,pipeline_cache,1,&pipeline,nullptr,&light_tiles.pipeline),"create stock light tile pipeline");
        spvReflectDestroyShaderModule(&reflection);vkDestroyShaderModule(device,module,nullptr);
    }catch(...){if(reflection._internal)spvReflectDestroyShaderModule(&reflection);if(module)vkDestroyShaderModule(device,module,nullptr);throw;}
}
void prepare_light_tile_storage(WorldSlot& slot,uint32_t tiles,uint32_t points){
    if(!light_tiles.pipeline)throw std::runtime_error("light tile pipeline unavailable");
    uint64_t bytes=uint64_t(tiles)*65*4;
    if(!slot.light_tile_gpu.handle||slot.light_tile_gpu.bytes<bytes){destroy_buffer(slot.light_tile_gpu);slot.light_tile_gpu=make_buffer(bytes,VK_BUFFER_USAGE_STORAGE_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_SRC_BIT,false);}
    world_buffer(slot.light_tile_constants,16,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);std::memset(slot.light_tile_constants.mapped,0,16);std::memcpy(slot.light_tile_constants.mapped,&points,4);check(vmaFlushAllocation(allocator,slot.light_tile_constants.memory,0,16),"flush light tile constants");
    uint64_t read_bytes=uint64_t(tiles)*4;
    if(!slot.light_tile_readback.handle||slot.light_tile_readback.bytes<read_bytes){destroy_buffer(slot.light_tile_readback);slot.light_tile_readback=make_buffer(std::max(read_bytes,uint64_t(4096)),VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);}
}
void dispatch_light_tiles(VkCommandBuffer cmd,WorldSlot& slot){
    if(!light_tiles.requested)return;
    auto set=world_set(slot,light_tiles.layout);
    world_write_buffer(set,0,slot.frame,1280,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,1,slot.light_tile_constants,16,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
    world_write_buffer(set,141,slot.point_lights,slot.point_lights.bytes,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);world_write_buffer(set,384,slot.light_tile_gpu,slot.light_tile_gpu.bytes,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);world_write_image(set,128,world.gbuffer[3].view);
    auto buffer_barrier=[&](Buffer& buffer,VkPipelineStageFlags2 src,VkAccessFlags2 sa,VkPipelineStageFlags2 dst,VkAccessFlags2 da){VkBufferMemoryBarrier2 b{VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2};b.buffer=buffer.handle;b.offset=0;b.size=VK_WHOLE_SIZE;b.srcStageMask=src;b.srcAccessMask=sa;b.dstStageMask=dst;b.dstAccessMask=da;b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.bufferMemoryBarrierCount=1;d.pBufferMemoryBarriers=&b;vkCmdPipelineBarrier2(cmd,&d);};
    buffer_barrier(slot.light_tile_gpu,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
    vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,light_tiles.pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,light_tiles.pipeline_layout,0,1,&set,0,nullptr);vkCmdDispatch(cmd,(extent.width+15)/16,(extent.height+15)/16,1);
    buffer_barrier(slot.light_tile_gpu,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_TRANSFER_READ_BIT);
    buffer_barrier(slot.light_tile_readback,VK_PIPELINE_STAGE_2_TRANSFER_BIT|VK_PIPELINE_STAGE_2_HOST_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT|VK_ACCESS_2_HOST_READ_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT);
    VkBufferCopy copy{0,0,uint64_t(light_tiles.stats.tiles)*4};vkCmdCopyBuffer(cmd,slot.light_tile_gpu.handle,slot.light_tile_readback.handle,1,&copy);
    buffer_barrier(slot.light_tile_readback,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_HOST_BIT,VK_ACCESS_2_HOST_READ_BIT);
    slot.tile_pending=true;slot.tile_frame=work.world_frames+1;slot.tile_epoch=light_tiles.epoch;slot.tile_count=light_tiles.stats.tiles;slot.tile_points=light_tiles.points;light_tiles.stats.dispatches=1;
}
void sample_light_tiles(uint32_t index){
    light_tiles.stats.readback_us=0;auto& slot=world.slots[index];if(!slot.tile_pending)return;slot.tile_pending=false;
    if(slot.tile_epoch!=light_tiles.epoch||!light_tiles.requested)return;
    auto started=std::chrono::steady_clock::now();
    check(vmaInvalidateAllocation(allocator,slot.light_tile_readback.memory,0,uint64_t(slot.tile_count)*4),"invalidate completed light tile counts");
    uint64_t sum=0;const auto* counts=static_cast<const uint32_t*>(slot.light_tile_readback.mapped);
    for(uint32_t i=0;i<slot.tile_count;++i){if(counts[i]>slot.tile_points)throw std::runtime_error("GPU tile list exceeds point budget");sum+=counts[i];}
    light_tiles.stats.have_result=1;light_tiles.stats.completed_frame=slot.tile_frame;light_tiles.stats.assignments=sum;light_tiles.stats.full_assignments=uint64_t(slot.tile_count)*slot.tile_points;light_tiles.stats.readback_us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-started).count());
}
