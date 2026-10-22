// Instrument only active GPU pack generations. Empty indirect commands are not production.
void gpu_owner_destroy_slot(GpuOwnerSlot& slot){destroy_buffer(slot.ranges);destroy_buffer(slot.work);destroy_buffer(slot.readback);slot={};}
void gpu_owner_resolve_slot(GpuOwnerSlot& slot){
    if(!slot.pending)return;slot.pending=false;check(vmaInvalidateAllocation(allocator,slot.readback.memory,0,uint64_t(slot.rows.size())*4),"completed GPU owner production");auto* work=static_cast<const uint32_t*>(slot.readback.mapped);
    for(uint32_t row=0;row<slot.rows.size();++row){auto& record=slot.rows[row];if(!record.module||!work[row])continue;
        for(uint32_t id:{record.module-1,record.module}){auto* state=native_geometry_id(id)?(post.geometry.empty()?nullptr:&post.geometry[id-4096]):extended_geometry_state(id);if(!state||state->generation!=record.generation||!state->track_geometry)continue;++state->executions;if(slot.completion>=state->last_completion){state->last_completion=slot.completion;state->last_frame=record.frame;}}
    }
}
void gpu_owner_resolve_generations(uint64_t completed){
    for(auto& slot:gpu_owners.slots)if(slot.pending&&slot.completion<=completed)gpu_owner_resolve_slot(slot);
    for(auto it=gpu_owners.retired.begin();it!=gpu_owners.retired.end();)if(it->first<=completed){gpu_owner_resolve_slot(it->second);gpu_owner_destroy_slot(it->second);it=gpu_owners.retired.erase(it);}else ++it;
}
void gpu_owner_update_tracking(){
    bool enabled=false;for(auto* states:{&post.geometry,&post.cells,&post.gpu_new,&post.gpu_old})for(auto& stage:*states)enabled|=stage.generation&&stage.track_geometry;
    if(gpu_owners.enabled&&!enabled){gpu_owners.retired.reserve(gpu_owners.retired.size()+2);for(auto& slot:gpu_owners.slots)if(slot.ranges.handle||slot.work.handle||slot.readback.handle){gpu_owners.retired.emplace_back(std::max(next_completion,slot.completion),std::move(slot));slot={};}if(gpu_owners.reduction.pipeline){post.retired.emplace_back(next_completion,std::move(gpu_owners.reduction));gpu_owners.reduction={};}}
    gpu_owners.enabled=enabled;
}
void gpu_owner_begin_frame(){
    if(!gpu_owners.enabled)return;auto& slot=gpu_owners.slots[slot_index];gpu_owner_resolve_slot(slot);slot.rows.clear();
    if(!opaque_stock.ready||!opaque_reference.frame_ready)return;uint64_t rows=uint64_t(opaque_reference.batch_count)*opaque_reference.view_count;
    if(!rows||rows>1048576)throw std::runtime_error("GPU owner proof row bound exceeded");slot.rows.assign(size_t(rows),{});
    
}
void gpu_owner_record(uint32_t module,uint32_t row,uint32_t first,uint32_t capacity){
    if(!gpu_owners.enabled)return;auto* state=extended_geometry_state(module);if(!state||!state->generation||!state->track_geometry)return;
    std::lock_guard lock(gpu_owner_mutex);auto& slot=gpu_owners.slots[slot_index];if(row>=slot.rows.size()||uint64_t(first)+capacity>opaque_reference.slots[slot_index].args.bytes/20)throw std::runtime_error("GPU owner proof range invalid");
    auto& record=slot.rows[row];if(record.module&&(record.module!=module||record.first!=first||record.capacity!=capacity||record.generation!=state->generation))throw std::runtime_error("conflicting GPU owner proof scope");record={module,first,capacity,guest.frame.frame_index,state->generation,record.module&&record.condition==UINT32_MAX?UINT32_MAX:gpu_owner_condition};
}
void gpu_owner_record_direct(uint32_t module,uint32_t condition){
    if(!gpu_owners.enabled)return;auto* state=native_geometry_id(module)?(post.geometry.empty()?nullptr:&post.geometry[module-4096]):extended_geometry_state(module);if(!state||!state->generation||!state->track_geometry)return;
    std::lock_guard lock(gpu_owner_mutex);auto& slot=gpu_owners.slots[slot_index];if(slot.rows.size()>=1048576||condition>=opaque_reference.slots[slot_index].counts.bytes/4)throw std::runtime_error("conditional geometry proof bound exceeded");slot.rows.push_back({module,UINT32_MAX,1,guest.frame.frame_index,state->generation,condition});
}
void gpu_owner_finish_frame(VkCommandBuffer cmd,WorldSlot& world_slot){
    if(!gpu_owners.enabled)return;auto& slot=gpu_owners.slots[slot_index];if(slot.rows.empty()||std::none_of(slot.rows.begin(),slot.rows.end(),[](const auto& r){return r.module!=0;}))return;
    auto& stage=gpu_owners.reduction;if(!stage.pipeline){
        std::array<VkDescriptorSetLayoutBinding,4> bindings{};for(uint32_t i=0;i<4;++i)bindings[i]={i,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,1,VK_SHADER_STAGE_COMPUTE_BIT,nullptr};VkDescriptorSetLayoutCreateInfo info{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};info.bindingCount=4;info.pBindings=bindings.data();
        PostStage candidate{};VkShaderModule shader{};try{check(vkCreateDescriptorSetLayout(device,&info,nullptr,&candidate.layout),"GPU owner reduction descriptors");candidate.pipeline_layout=world_pipeline_layout(candidate.layout);shader=world_file_shader("gpu_owner_production_cs",VK_SHADER_STAGE_COMPUTE_BIT);VkComputePipelineCreateInfo create{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};create.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,shader,"main",nullptr};create.layout=candidate.pipeline_layout;check(vkCreateComputePipelines(device,pipeline_cache,1,&create,nullptr,&candidate.pipeline),"GPU owner reduction pipeline");vkDestroyShaderModule(device,shader,nullptr);stage=std::move(candidate);}catch(...){if(shader)vkDestroyShaderModule(device,shader,nullptr);post_destroy_stage(candidate);throw;}
    }
    uint64_t bytes=slot.rows.size()*4;world_buffer(slot.ranges,bytes*4,VK_BUFFER_USAGE_STORAGE_BUFFER_BIT);world_buffer(slot.work,bytes,VK_BUFFER_USAGE_STORAGE_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_SRC_BIT);world_buffer(slot.readback,bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT);
    auto* ranges=static_cast<uint32_t*>(slot.ranges.mapped);for(uint32_t i=0;i<slot.rows.size();++i){auto& row=slot.rows[i];ranges[4*i]=row.first;ranges[4*i+1]=row.module?row.capacity:0;ranges[4*i+2]=i;ranges[4*i+3]=row.condition;}check(vmaFlushAllocation(allocator,slot.ranges.memory,0,bytes*4),"GPU owner proof ranges");
    auto set=world_set(world_slot,stage.layout);world_write_buffer(set,0,slot.ranges,bytes*4,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);world_write_buffer(set,1,opaque_reference.slots[slot_index].counts,opaque_reference.slots[slot_index].counts.bytes,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);world_write_buffer(set,2,opaque_reference.slots[slot_index].args,opaque_reference.slots[slot_index].args.bytes,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);world_write_buffer(set,3,slot.work,bytes,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);
    opaque_reference_dependency(cmd,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT|VK_PIPELINE_STAGE_2_HOST_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT|VK_ACCESS_2_HOST_WRITE_BIT,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
    vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,stage.pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,stage.pipeline_layout,0,1,&set,0,nullptr);vkCmdDispatch(cmd,(uint32_t(slot.rows.size())+63)/64,1,1);
    opaque_reference_dependency(cmd,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);VkBufferCopy copy{0,0,bytes};vkCmdCopyBuffer(cmd,slot.work.handle,slot.readback.handle,1,&copy);
    opaque_reference_dependency(cmd,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_HOST_BIT,VK_ACCESS_2_HOST_READ_BIT);slot.pending=true;slot.completion=next_completion+1;
}
void gpu_owner_cleanup(){for(auto& slot:gpu_owners.slots)gpu_owner_destroy_slot(slot);for(auto& retired:gpu_owners.retired)gpu_owner_destroy_slot(retired.second);post_destroy_stage(gpu_owners.reduction);gpu_owners={};}
