void init_shadow_usage(const void* data,uint64_t bytes){
    if(recording || shadow_usage.stage.pipeline)throw std::runtime_error("invalid shadow usage initialization");
    wait(next_completion);SpvReflectShaderModule reflection{};auto shader=world_shader(data,bytes,VK_SHADER_STAGE_COMPUTE_BIT,reflection);
    try{
        std::vector<VkDescriptorSetLayoutBinding> bindings;
        if(reflection.descriptor_binding_count!=5 || reflection.push_constant_block_count)throw std::runtime_error("stock shadow usage interface changed");
        for(uint32_t i=0;i<reflection.descriptor_binding_count;++i){auto& b=reflection.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
            bool valid=(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER && ((b.binding==0&&b.block.size==1280)||(b.binding==2&&b.block.size==32)||(b.binding==4&&b.block.size==1200))) || (type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER&&b.binding==385) || (type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&b.binding==128&&b.image.dim==SpvDim2D&&!b.image.arrayed&&!b.image.ms);
            if(!valid || b.set || b.count!=1)throw std::runtime_error("stock shadow usage resource contract mismatch");bindings.push_back({b.binding,type,1,VK_SHADER_STAGE_COMPUTE_BIT,nullptr});
        }
        auto& stage=shadow_usage.stage;VkDescriptorSetLayoutCreateInfo layout{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};layout.bindingCount=uint32_t(bindings.size());layout.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&layout,nullptr,&stage.layout),"shadow usage descriptor layout");stage.pipeline_layout=world_pipeline_layout(stage.layout);
        VkComputePipelineCreateInfo info{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};info.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,shader,"__compute_shader",nullptr};info.layout=stage.pipeline_layout;check(vkCreateComputePipelines(device,pipeline_cache,1,&info,nullptr,&stage.pipeline),"stock shadow usage pipeline");
        spvReflectDestroyShaderModule(&reflection);vkDestroyShaderModule(device,shader,nullptr);
    }catch(...){spvReflectDestroyShaderModule(&reflection);vkDestroyShaderModule(device,shader,nullptr);throw;}
}
void shadow_usage_buffer_barrier(VkCommandBuffer cmd,Buffer& buffer,VkPipelineStageFlags2 source,VkAccessFlags2 write,VkPipelineStageFlags2 target,VkAccessFlags2 read){
    VkBufferMemoryBarrier2 barrier{VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2};barrier.srcStageMask=source;barrier.srcAccessMask=write;barrier.dstStageMask=target;barrier.dstAccessMask=read;barrier.srcQueueFamilyIndex=barrier.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;barrier.buffer=buffer.handle;barrier.size=VK_WHOLE_SIZE;
    VkDependencyInfo dependency{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};dependency.bufferMemoryBarrierCount=1;dependency.pBufferMemoryBarriers=&barrier;vkCmdPipelineBarrier2(cmd,&dependency);
}
void gather_shadow_usage(VkCommandBuffer cmd,WorldSlot& slot,uint32_t view,Buffer& frame,Image& depth,uint32_t width,uint32_t height){
    if(!shadow_usage.enabled || !shadow_count)return;
    if(view>=19 || !shadow_usage.stage.pipeline)throw std::runtime_error("shadow usage view/pipeline missing");
    auto& values=shadow_usage.views[slot_index][view];
    if(!values.gpu.handle){values.gpu=make_buffer(32,VK_BUFFER_USAGE_STORAGE_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_SRC_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT,false);values.readback=make_buffer(32,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);values.screen=make_buffer(32,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,true);}
    if(values.pending)throw std::runtime_error("shadow usage slot reused before completion");
    uint32_t screen[]{0,0,std::bit_cast<uint32_t>(float(width)),std::bit_cast<uint32_t>(float(height)),std::bit_cast<uint32_t>(1.f),0,0,0};std::memcpy(values.screen.mapped,screen,32);check(vmaFlushAllocation(allocator,values.screen.memory,0,32),"flush shadow usage screen");
    shadow_usage_buffer_barrier(cmd,values.gpu,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT);
    vkCmdFillBuffer(cmd,values.gpu.handle,0,32,0);
    shadow_usage_buffer_barrier(cmd,values.gpu,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
    barrier(cmd,depth.handle,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    auto set=world_set(slot,shadow_usage.stage.layout);if(!shadow_usage.stage.generation){world_write_buffer(set,0,frame,1280,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,2,values.screen,32,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,4,transparency.shadow_constants[slot_index],1200,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,385,values.gpu,32,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);world_write_image(set,128,depth.view);}
    else for(auto [b,type]:shadow_usage.stage.bindings){auto catalog=std::find_if(shadow_usage.stage.catalog.begin(),shadow_usage.stage.catalog.end(),[&](auto r){return r.binding==b;});
        if(catalog!=shadow_usage.stage.catalog.end())write_stage_catalog(set,1793,*catalog);
        else if(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER){if(b==6)write_stage_extras(set,1793);else world_write_buffer(set,b,b==0?frame:b==2?values.screen:transparency.shadow_constants[slot_index],b==0?1280:b==2?32:1200,type);}
        else if(type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER)world_write_buffer(set,b,values.gpu,32,type);
        else if(type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE)world_write_image(set,b,depth.view);
        else world_write_image(set,b,{},b==271?shadow_sampler:environment_generation_sampler(b),true);
    }
    if(width>=2&&height>=2)post_note_execution(1793);
    vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,shadow_usage.stage.pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,shadow_usage.stage.pipeline_layout,0,1,&set,0,nullptr);vkCmdDispatch(cmd,((width/2)+15)/16,((height/2)+15)/16,1);
    shadow_usage_buffer_barrier(cmd,values.gpu,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
    VkBufferCopy copy{0,0,32};vkCmdCopyBuffer(cmd,values.gpu.handle,values.readback.handle,1,&copy);
    shadow_usage_buffer_barrier(cmd,values.readback,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_HOST_BIT,VK_ACCESS_2_HOST_READ_BIT);values.pending=true;
}
void sample_shadow_usage(uint32_t slot){
    for(uint32_t v=0;v<19;++v){auto& values=shadow_usage.views[slot][v];if(!values.pending)continue;
        if(slots[slot].completion>=shadow_usage.completion[v]){check(vmaInvalidateAllocation(allocator,values.readback.memory,0,32),"completed shadow usage readback");std::memcpy(shadow_usage.pixels[v],values.readback.mapped,32);shadow_usage.completion[v]=slots[slot].completion;shadow_usage.latest=std::max(shadow_usage.latest,slots[slot].completion);}values.pending=false;
    }
}
void cleanup_shadow_usage(){auto& stage=shadow_usage.stage;if(stage.pipeline)vkDestroyPipeline(device,stage.pipeline,nullptr);if(stage.pipeline_layout)vkDestroyPipelineLayout(device,stage.pipeline_layout,nullptr);if(stage.layout)vkDestroyDescriptorSetLayout(device,stage.layout,nullptr);for(auto& slot:shadow_usage.views)for(auto& view:slot){destroy_buffer(view.gpu);destroy_buffer(view.readback);destroy_buffer(view.screen);}shadow_usage={};}
