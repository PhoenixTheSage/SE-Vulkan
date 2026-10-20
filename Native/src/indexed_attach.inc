void ensure_indexed_attach_layout(){
    if(world.attach_indexed_layout)return;ensure_attach_layout();
    VkDescriptorSetLayoutBinding bindings[7]{};for(uint32_t i=0;i<7;++i)bindings[i]={128+i,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,1,VK_SHADER_STAGE_VERTEX_BIT,nullptr};
    VkDescriptorSetLayoutCreateInfo create{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};create.bindingCount=7;create.pBindings=bindings;check(vkCreateDescriptorSetLayout(device,&create,nullptr,&world.attach_indexed_inputs),"create indexed auxiliary inputs");
    VkDescriptorSetLayout sets[]{world.attach_set_layout,world.attach_indexed_inputs};VkPushConstantRange push{VK_SHADER_STAGE_VERTEX_BIT,0,16};
    VkPipelineLayoutCreateInfo layout{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};layout.setLayoutCount=2;layout.pSetLayouts=sets;layout.pushConstantRangeCount=1;layout.pPushConstantRanges=&push;check(vkCreatePipelineLayout(device,&layout,nullptr,&world.attach_indexed_layout),"create indexed attachment pipeline layout");
    VkDescriptorPoolSize size{VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,28};VkDescriptorPoolCreateInfo pool{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};pool.maxSets=4;pool.poolSizeCount=1;pool.pPoolSizes=&size;check(vkCreateDescriptorPool(device,&pool,nullptr,&world.attach_indexed_pool),"create indexed auxiliary pool");
    VkDescriptorSetLayout inputs[]{world.attach_indexed_inputs,world.attach_indexed_inputs};VkDescriptorSetAllocateInfo allocation{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};allocation.descriptorPool=world.attach_indexed_pool;allocation.descriptorSetCount=2;allocation.pSetLayouts=inputs;check(vkAllocateDescriptorSets(device,&allocation,world.attach_indexed_sets),"allocate indexed attachment slots");check(vkAllocateDescriptorSets(device,&allocation,world.motion_indexed_sets),"allocate indexed motion slots");
}
void init_indexed_attachment(uint32_t slot,uint32_t format,uint32_t kind,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes,bool motion=false){
    if(recording || slot>2 || kind>4 || !opaque_caps.enabled)throw std::runtime_error("invalid indexed attachment initialization");wait(next_completion);ensure_indexed_attach_layout();
    auto& target=world.attach[slot];if(!motion && (!target.live || target.format!=format))throw std::runtime_error("indexed attachment requires matching CPU attachment");
    SpvReflectShaderModule vr{},pr{};VkShaderModule vs{},ps{};
    try{vs=opaque_stock_shader(vertex,vertex_bytes,VK_SHADER_STAGE_VERTEX_BIT,vr);ps=world_shader(pixel,pixel_bytes,VK_SHADER_STAGE_FRAGMENT_BIT,pr);
        if(pr.descriptor_binding_count || pr.push_constant_block_count || vr.push_constant_block_count!=1 || vr.push_constant_blocks[0].size!=(motion?16u:8u))throw std::runtime_error("indexed auxiliary shader contract mismatch");
        for(uint32_t i=0;i<vr.descriptor_binding_count;++i){const auto& b=vr.descriptor_bindings[i];if(b.count!=1 || !((b.set==0&&b.binding<(motion?2u:1u)&&b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.block.size==64)||(b.set==1&&b.binding>=128&&b.binding<(motion?135u:132u)&&b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_STORAGE_BUFFER)))throw std::runtime_error("indexed auxiliary descriptor outside contract");}
        std::vector<VkVertexInputAttributeDescription> attributes{{0,0,VK_FORMAT_R32G32B32A32_SFLOAT,0}};
        if(kind==3){attributes.push_back({1,0,VK_FORMAT_R32G32B32A32_SFLOAT,48});attributes.push_back({2,0,VK_FORMAT_R32G32B32A32_UINT,64});}
        auto format_vk=attach_vk_format(format);
        auto replacement=world_graphics(vs,"__vertex_shader",ps,"__pixel_shader",world.attach_indexed_layout,{{0,uint32_t(sizeof(SevkWorldVertex)),VK_VERTEX_INPUT_RATE_VERTEX}},attributes,&format_vk,1,true,false,false,false,0,true,VK_NULL_HANDLE,false,false,false,false,VK_CULL_MODE_NONE,VK_FRONT_FACE_CLOCKWISE,VK_COMPARE_OP_EQUAL,false,true);
        auto& destination=motion?indexed_motion.pipelines[kind]:target.indexed_pipe[kind];if(destination)vkDestroyPipeline(device,destination,nullptr);destination=replacement;
        spvReflectDestroyShaderModule(&vr);spvReflectDestroyShaderModule(&pr);vkDestroyShaderModule(device,vs,nullptr);vkDestroyShaderModule(device,ps,nullptr);
    }catch(...){if(vr._internal)spvReflectDestroyShaderModule(&vr);if(pr._internal)spvReflectDestroyShaderModule(&pr);if(vs)vkDestroyShaderModule(device,vs,nullptr);if(ps)vkDestroyShaderModule(device,ps,nullptr);throw;}
}
bool prepare_indexed_attachments(){
    auto& r=opaque_reference;if(!world.attach_indexed_layout || !opaque_stock.ready || !r.frame_ready)return false;
    auto& slot=r.slots[slot_index];Buffer* buffers[]{&slot.stock_colour,&slot.stock_objects,&r.sources,&slot.stock_draws};VkDescriptorBufferInfo infos[4]{};VkWriteDescriptorSet writes[4]{};
    for(uint32_t i=0;i<4;++i){if(!buffers[i]->handle)throw std::runtime_error("indexed attachment input missing");infos[i]={buffers[i]->handle,0,buffers[i]->bytes};writes[i]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[i].dstSet=world.attach_indexed_sets[slot_index];writes[i].dstBinding=128+i;writes[i].descriptorCount=1;writes[i].descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;writes[i].pBufferInfo=&infos[i];}vkUpdateDescriptorSets(device,4,writes,0,nullptr);return true;
}
void record_indexed_attachments(VkCommandBuffer cmd,WorldSlot& slot,WorldState::AttachTarget& target){
    auto& r=opaque_reference;uint32_t view=gpu_resident.published?gpu_resident.view_map[0]:0;if(view>=r.view_count || !(r.views[view].flags&1) || (r.views[view].flags&34))return;
    VkDescriptorSet sets[]{world.attach_sets[slot_index],world.attach_indexed_sets[slot_index]};vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,world.attach_indexed_layout,0,2,sets,0,nullptr);
    uint32_t count=r.view_batches?uint32_t(r.recording_batches.at(view).size()):uint32_t(opaque_stock.batches.size());
    for(uint32_t n=0;n<count;++n){uint32_t b=r.view_batches?r.recording_batches[view][n]:n;const auto& batch=opaque_stock.batches[b];const auto& input=batch.input;const auto range=r.ranges[b];if(!range.capacity)continue;
        uint32_t type=input.old?world.pipelines.at(input.main_pipeline).type:0,kind=input.old?(type==0?0u:type>=2&&type<=4?type-1:UINT32_MAX):4u;
        if(kind>4 || !target.indexed_pipe[kind])throw std::runtime_error("indexed attachment geometry class unavailable");
        vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,target.indexed_pipe[kind]);world_model_raster(cmd,input.main_pipeline,(input.flags&1)?0x10000000u:0x08000000u);
        VkDeviceSize zero=0;vkCmdBindVertexBuffers(cmd,0,1,&batch.vertices,&zero);vkCmdBindIndexBuffer(cmd,batch.indices,0,VK_INDEX_TYPE_UINT32);
        uint32_t base=view*r.draw_capacity+range.first,push[]{base,std::bit_cast<uint32_t>(input.old?motion_distance_fade(slot,input.main_pipeline):0.f)};
        vkCmdPushConstants(cmd,world.attach_indexed_layout,VK_SHADER_STAGE_VERTEX_BIT,0,8,push);
        vkCmdDrawIndexedIndirectCount(cmd,r.slots[slot_index].args.handle,uint64_t(base)*sizeof(SevkOpaqueIndirect),r.slots[slot_index].counts.handle,(b*r.view_count+view)*4,range.capacity,sizeof(SevkOpaqueIndirect));
    }
}
void cleanup_indexed_attachments() noexcept {
    if(world.attach_indexed_pool)vkDestroyDescriptorPool(device,world.attach_indexed_pool,nullptr);
    if(world.attach_indexed_layout)vkDestroyPipelineLayout(device,world.attach_indexed_layout,nullptr);
    if(world.attach_indexed_inputs)vkDestroyDescriptorSetLayout(device,world.attach_indexed_inputs,nullptr);
    world.attach_indexed_pool={};world.attach_indexed_layout={};world.attach_indexed_inputs={};world.attach_indexed_sets[0]=world.attach_indexed_sets[1]={};world.motion_indexed_sets[0]=world.motion_indexed_sets[1]={};
}
