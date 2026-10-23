void init_indexed_motion(uint32_t kind,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes){
    init_indexed_attachment(0,3,kind,vertex,vertex_bytes,pixel,pixel_bytes,true);
}
void invalidate_indexed_motion_history(){
    // The explicit scene-clear caller has waited for all submitted work.
    indexed_motion.actors.clear();indexed_motion.sources.reset();indexed_motion.frame=UINT64_MAX;indexed_motion.source_epoch=UINT64_MAX;
    for(uint32_t slot=0;slot<2;++slot){indexed_motion.uploaded_sources[slot].reset();destroy_buffer(indexed_motion.previous_rows[slot]);destroy_buffer(indexed_motion.previous_sources[slot]);}
}
bool prepare_indexed_motion(WorldSlot& world_slot){
    auto& state=indexed_motion;auto previous=std::move(state.actors);state.actors.clear();auto previous_sources=state.sources;
    if(state.frame==UINT64_MAX || state.frame+1!=stats.submitted_frames)previous.clear();state.frame=stats.submitted_frames;
    auto& r=opaque_reference;
    if(!state.pipelines[0] || !opaque_stock.ready || !r.frame_ready || r.actors.empty())return false;
    if(!state.sources || state.source_epoch!=opaque_source_epoch){state.sources=std::make_shared<const std::vector<uint8_t>>(r.source_inputs);state.source_epoch=opaque_source_epoch;}
    if(previous.empty())previous_sources.reset();
    struct Header {float rows[12];uint32_t source_first,source_count,valid;float fade;};static_assert(sizeof(Header)==64);
    std::vector<Header> headers(r.actors.size());std::vector<uint32_t> family_type(r.family_inputs.size(),UINT32_MAX);std::vector<bool> family_fade(r.family_inputs.size());
    for(uint32_t family=0;family<r.family_inputs.size();++family){const auto& f=r.family_inputs[family];for(uint32_t p=f.first_part;p<f.first_part+f.part_count;++p){const auto& part=r.part_inputs[p];if(!(part.flags&2))continue;const auto& batch=opaque_stock.batches.at(part.reserved).input;
        if(family_type[family]==UINT32_MAX)family_type[family]=batch.old?world.pipelines.at(batch.main_pipeline).type:5u;family_fade[family]=family_fade[family]||(batch.old&&batch.main_pipeline>=2560);
    }}
    uint32_t main_view=gpu_resident.published?gpu_resident.view_map[0]:0;
    for(uint32_t i=0;i<r.actors.size();++i){const auto& a=r.actors[i];uint64_t key=(uint64_t(a.owner)<<32)|a.generation;IndexedMotionState::Actor current{};
        for(uint32_t row=0;row<3;++row){for(uint32_t column=0;column<3;++column)current.rows[row*4+column]=a.basis[row*3+column];current.rows[row*4+3]=float(a.translation[row]-r.frame.camera[row]);}
        current.source_first=a.source_first;current.source_count=a.source_count;current.type=family_type.at(a.family);current.fade=family_fade.at(a.family)?motion_distance_fade(world_slot,2560):0.f;
        current.active=(a.flags&1)&&main_view<r.view_count&&(r.views[main_view].flags&1)&&!(a.hidden_views[main_view/32]&(1u<<(main_view%32)));
        auto found=previous.find(key);auto& output=headers[i];
        if(found!=previous.end()&&found->second.active&&found->second.type==current.type&&found->second.source_count==current.source_count){const auto& prior=found->second;std::memcpy(output.rows,prior.rows.data(),48);output.source_first=prior.source_first;output.source_count=prior.source_count;output.fade=prior.fade;output.valid=1;
            if(previous_sources && (uint64_t(prior.source_first)+prior.source_count)*64<=previous_sources->size())output.valid|=2;
        }
        state.actors.emplace(key,current);
    }
    world_buffer(state.previous_rows[slot_index],headers.size()*sizeof(Header),VK_BUFFER_USAGE_STORAGE_BUFFER_BIT);std::memcpy(state.previous_rows[slot_index].mapped,headers.data(),headers.size()*sizeof(Header));
    uint64_t bytes=previous_sources?previous_sources->size():0,needed=std::max(uint64_t(64),bytes);bool source_dirty=state.uploaded_sources[slot_index]!=previous_sources || !state.previous_sources[slot_index].handle || state.previous_sources[slot_index].bytes<needed;
    if(source_dirty){world_buffer(state.previous_sources[slot_index],needed,VK_BUFFER_USAGE_STORAGE_BUFFER_BIT);
        if(bytes)std::memcpy(state.previous_sources[slot_index].mapped,previous_sources->data(),size_t(bytes));else std::memset(state.previous_sources[slot_index].mapped,0,64);
        check(vmaFlushAllocation(allocator,state.previous_sources[slot_index].memory,0,VK_WHOLE_SIZE),"flush changed indexed motion source snapshot");state.uploaded_sources[slot_index]=previous_sources;
    }
    check(vmaFlushAllocation(allocator,state.previous_rows[slot_index].memory,0,VK_WHOLE_SIZE),"flush current-slot indexed actor history");
    auto& slot=r.slots[slot_index];Buffer* buffers[]{&slot.stock_colour,&slot.stock_objects,&r.sources,&slot.stock_draws,&slot.references,&state.previous_rows[slot_index],&state.previous_sources[slot_index]};VkDescriptorBufferInfo infos[7]{};VkWriteDescriptorSet writes[7]{};
    for(uint32_t i=0;i<7;++i){infos[i]={buffers[i]->handle,0,buffers[i]->bytes};writes[i]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[i].dstSet=world.motion_indexed_sets[slot_index];writes[i].dstBinding=128+i;writes[i].descriptorCount=1;writes[i].descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;writes[i].pBufferInfo=&infos[i];}vkUpdateDescriptorSets(device,7,writes,0,nullptr);return true;
}
void record_indexed_motion(VkCommandBuffer cmd,WorldSlot& slot){
    auto& r=opaque_reference;uint32_t view=gpu_resident.published?gpu_resident.view_map[0]:0;if(view>=r.view_count || !(r.views[view].flags&1) || (r.views[view].flags&34))return;
    VkDescriptorSet sets[]{world.motion_sets[slot_index],world.motion_indexed_sets[slot_index]};vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,world.attach_indexed_layout,0,2,sets,0,nullptr);
    uint32_t count=r.view_batches?uint32_t(r.recording_batches.at(view).size()):uint32_t(opaque_stock.batches.size());
    for(uint32_t n=0;n<count;++n){uint32_t b=r.view_batches?r.recording_batches[view][n]:n;const auto& batch=opaque_stock.batches[b];const auto& input=batch.input;const auto range=r.ranges[b];if(!range.capacity)continue;
        if(gbuffer_velocity.frame&&(input.old?opaque_stock.older[input.main_pipeline].velocity:opaque_stock.newer[input.main_pipeline].velocity))continue;
        uint32_t type=input.old?world.pipelines.at(input.main_pipeline).type:0,kind=input.old?(type==0?0u:type>=2&&type<=4?type-1:UINT32_MAX):4u;if(kind>4||!indexed_motion.pipelines[kind])throw std::runtime_error("indexed motion geometry class unavailable");
        vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,indexed_motion.pipelines[kind]);world_model_raster(cmd,input.main_pipeline,(input.flags&1)?0x10000000u:0x08000000u);
        VkDeviceSize zero=0;vkCmdBindVertexBuffers(cmd,0,1,&batch.vertices,&zero);vkCmdBindIndexBuffer(cmd,batch.indices,0,VK_INDEX_TYPE_UINT32);
        struct Push {uint32_t base;float fade,width,height;} push{view*r.draw_capacity+range.first,input.old?motion_distance_fade(slot,input.main_pipeline):0.f,float(extent.width),float(extent.height)};
        vkCmdPushConstants(cmd,world.attach_indexed_layout,VK_SHADER_STAGE_VERTEX_BIT,0,sizeof(push),&push);vkCmdDrawIndexedIndirectCount(cmd,r.slots[slot_index].args.handle,uint64_t(push.base)*sizeof(SevkOpaqueIndirect),r.slots[slot_index].counts.handle,(b*r.view_count+view)*4,range.capacity,sizeof(SevkOpaqueIndirect));
    }
}
void cleanup_indexed_motion() noexcept {
    for(auto& pipeline:indexed_motion.pipelines)if(pipeline)vkDestroyPipeline(device,pipeline,nullptr);
    for(auto& buffer:indexed_motion.previous_rows)destroy_buffer(buffer);for(auto& buffer:indexed_motion.previous_sources)destroy_buffer(buffer);indexed_motion={};
}
