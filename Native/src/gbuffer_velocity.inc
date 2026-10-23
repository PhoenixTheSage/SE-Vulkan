// DX11's GBuffer velocity contract for admitted classic owners. Anomaly relocates
// VS b6 (240 B), PS b7 (16 B) and VS t15/t16 into set 2 under ANOMALY_SPIRV; the
// owning GBuffer invocation writes Target3 into world.motion, which the replay
// pass then loads instead of clearing and skips for those draws.
// Every classic stream takes local_matrix from the object block, so DX11 gives it the per-draw
// currToPrev rows (skinned motion is rigid-body); t15/t16 belong to USE_SIMPLE_INSTANCING only.
// Fade-bank variants share their base stream and blended decals write Target3 as RG replace.
// Voxel GBuffer shaders are not native owners (they come from the voxel library).
bool gbuffer_velocity_admitted(uint32_t id,bool decal,bool voxel){return gbuffer_velocity_supported&&!voxel&&(decal||world_unfade(id)<20);}
void gbuffer_velocity_layouts(){
    if(gbuffer_velocity.pipeline_layout)return;init_world_layouts();
    VkDescriptorSetLayoutCreateInfo empty{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};check(vkCreateDescriptorSetLayout(device,&empty,nullptr,&gbuffer_velocity.empty),"create empty velocity set1 layout");
    const VkShaderStageFlags stages=VK_SHADER_STAGE_VERTEX_BIT|VK_SHADER_STAGE_FRAGMENT_BIT;
    VkDescriptorSetLayoutBinding bindings[]{{0,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC,1,stages,nullptr},{1,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,1,stages,nullptr},{128,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,1,stages,nullptr},{129,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,1,stages,nullptr}};
    VkDescriptorSetLayoutCreateInfo info{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};info.bindingCount=4;info.pBindings=bindings;check(vkCreateDescriptorSetLayout(device,&info,nullptr,&gbuffer_velocity.layout),"create GBuffer velocity layout");
    VkDescriptorSetLayout sets[]{world.material_layout,gbuffer_velocity.empty,gbuffer_velocity.layout};
    VkPipelineLayoutCreateInfo pipeline{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};pipeline.setLayoutCount=3;pipeline.pSetLayouts=sets;check(vkCreatePipelineLayout(device,&pipeline,nullptr,&gbuffer_velocity.pipeline_layout),"create GBuffer velocity pipeline layout");
}
// Exact descriptor contract of the relocated Anomaly.hlsli velocity resources.
bool gbuffer_velocity_binding(const SpvReflectDescriptorBinding& b){
    if(b.set!=2||b.count!=1)return false;
    if(b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER)return (b.binding==0&&b.block.size==sizeof(GBufferVelocityConstants))||(b.binding==1&&b.block.size==16);
    return b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_STORAGE_BUFFER&&(b.binding==128||b.binding==129);
}
void gbuffer_velocity_cleanup() noexcept {
    for(auto& buffer:gbuffer_velocity.constants)destroy_buffer(buffer);destroy_buffer(gbuffer_velocity.probe);destroy_buffer(gbuffer_velocity.prev_world);destroy_buffer(gbuffer_velocity.prev_bones);
    if(gbuffer_velocity.pipeline_layout)vkDestroyPipelineLayout(device,gbuffer_velocity.pipeline_layout,nullptr);
    for(auto layout:{gbuffer_velocity.layout,gbuffer_velocity.empty})if(layout)vkDestroyDescriptorSetLayout(device,layout,nullptr);
    gbuffer_velocity={};
}
void ensure_motion_image(){
    if(world.motion.view && world.motion_w==extent.width && world.motion_h==extent.height)return;
    wait(next_completion);if(world.motion.view)destroy_image(world.motion);
    world.motion=make_image(extent.width,extent.height,VK_FORMAT_R16G16_SFLOAT,VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT,VK_IMAGE_ASPECT_COLOR_BIT);
    world.motion_w=extent.width;world.motion_h=extent.height;world.motion_sampled=false;
}
uint64_t motion_signature(){
    uint64_t signature=14695981039346656037ull;auto mix=[&](uint64_t value){signature^=value;signature*=1099511628211ull;};
    mix(world.current().draws.size());for(const auto& draw:world.current().draws){mix(draw.packet.mesh);mix(draw.packet.pipeline);mix(draw.gpu_object_at);mix(draw.instance_at);mix(draw.packet.instance_count);}
    return signature;
}
// Same history rule as the replay pass: matching draw signature and a mapped previous slot.
bool motion_history(WorldSlot& slot,uint64_t signature){
    auto& previous=world.slots[(slot_index+1)%world.slots.size()];
    return world.motion_have_signature && signature==world.motion_signature && previous.objects.mapped && previous.projections.mapped && slot.projections.handle;
}
// Prepares this frame's set-2 resources and the fourth main GBuffer attachment.
bool gbuffer_velocity_begin(VkCommandBuffer cmd,WorldSlot& slot){
    gbuffer_velocity.frame=false;gbuffer_velocity.cursor=0;gbuffer_velocity.indexed_prepared=gbuffer_velocity.indexed_ready=false;gbuffer_velocity.gpu_set={};
    // Positions are camera-relative; a history-less GPU instance keeps DX11's camera-only motion.
    bool gpu_frame=opaque_stock.ready&&opaque_reference.frame_ready;
    if(gpu_frame){for(uint32_t i=0;i<3;++i){gbuffer_velocity.camera_delta[i]=gbuffer_velocity.camera_valid?float(opaque_reference.frame.camera[i]-gbuffer_velocity.camera[i]):0.f;gbuffer_velocity.camera[i]=opaque_reference.frame.camera[i];}gbuffer_velocity.camera_valid=true;}
    bool live=false,gpu=false;for(const auto& pipeline:world.pipelines)live|=pipeline.velocity;
    if(gpu_frame){for(const auto& pipeline:opaque_stock.newer)gpu|=pipeline.velocity;for(const auto& pipeline:opaque_stock.older)gpu|=pipeline.velocity;}
    uint32_t cells=0;if(pass_scene.ready){const auto& groups=pass_scene.scene.render_groups();for(const auto& group:groups)if(group.key.view==0&&group.kind==native_pass::Kind::Colour&&group.count()&&pass_scene.pipelines.at(group.key.shader<4?group.key.shader:12+group.key.shader-4).velocity)++cells;}
    if((!live&&!gpu&&!cells)||!extent.width||!extent.height)return false;
    uint32_t draws=cells;if(live)for(const auto& draw:world.current().draws)if(!(draw.packet.flags&(4096|8192))&&world.pipelines[draw.packet.pipeline].velocity)++draws;
    if(!draws&&!gpu)return false;
    gbuffer_velocity.stride=uint32_t((sizeof(GBufferVelocityConstants)+world.alignment-1)/world.alignment*world.alignment);gbuffer_velocity.capacity=draws+(gpu?1u:0u);
    auto& constants=gbuffer_velocity.constants[slot_index];world_buffer(constants,uint64_t(gbuffer_velocity.capacity)*gbuffer_velocity.stride,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);
    for(auto* zero:{&gbuffer_velocity.probe,&gbuffer_velocity.prev_world,&gbuffer_velocity.prev_bones})if(!zero->handle){
        // Rigid owners never index t15/t16; PS probe mode zero selects the computed velocity.
        *zero=make_buffer(64,zero==&gbuffer_velocity.probe?VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT:VK_BUFFER_USAGE_STORAGE_BUFFER_BIT,true);std::memset(zero->mapped,0,64);check(vmaFlushAllocation(allocator,zero->memory,0,64),"flush GBuffer velocity defaults");}
    auto set=world_set(slot,gbuffer_velocity.layout);
    // Native-cell owners index t15 in the current slot's per-instance history stream; classic owners ignore it.
    auto& cell_history=pass_scene.history_instances[slot_index];bool cell_rows=cells&&cell_history.handle&&!pass_scene.history_offsets.empty();
    VkDescriptorBufferInfo infos[]{{constants.handle,0,sizeof(GBufferVelocityConstants)},{gbuffer_velocity.probe.handle,0,16},cell_rows?VkDescriptorBufferInfo{cell_history.handle,0,cell_history.bytes}:VkDescriptorBufferInfo{gbuffer_velocity.prev_world.handle,0,64},{gbuffer_velocity.prev_bones.handle,0,64}};
    const uint32_t bindings[]{0,1,128,129};const VkDescriptorType types[]{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER};
    VkWriteDescriptorSet writes[4]{};for(uint32_t i=0;i<4;++i){writes[i]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[i].dstSet=set;writes[i].dstBinding=bindings[i];writes[i].descriptorCount=1;writes[i].descriptorType=types[i];writes[i].pBufferInfo=&infos[i];}
    vkUpdateDescriptorSets(device,4,writes,0,nullptr);gbuffer_velocity.set=set;
    gbuffer_velocity.history=motion_history(slot,motion_signature());gbuffer_velocity.frame=true;
    if(gpu){
        // USE_SIMPLE_INSTANCING reads t15 per instance: the adapter resolves instance -> actor through the
        // culled references and reads the replay's previous actor rows; b0 is per frame.
        gbuffer_velocity.indexed_prepared=true;gbuffer_velocity.indexed_ready=prepare_indexed_motion(slot);
        auto& references=opaque_reference.slots[slot_index].references;auto& previous=indexed_motion.previous_rows[slot_index];bool history=gbuffer_velocity.indexed_ready&&references.handle&&previous.handle;
        auto c=gbuffer_velocity_frame_constants(slot);c.has_history=history?1u:0u;c.prev_count=history?uint32_t(references.bytes/80):0u;std::memcpy(c.camera_delta,gbuffer_velocity.camera_delta,12);
        gbuffer_velocity.gpu_offset=gbuffer_velocity_store(c);auto gpu_set=world_set(slot,gbuffer_velocity.layout);
        VkDescriptorBufferInfo gpu_infos[]{infos[0],infos[1],history?VkDescriptorBufferInfo{previous.handle,0,previous.bytes}:infos[2],history?VkDescriptorBufferInfo{references.handle,0,references.bytes}:infos[3]};
        for(uint32_t i=0;i<4;++i){writes[i].dstSet=gpu_set;writes[i].pBufferInfo=&gpu_infos[i];}vkUpdateDescriptorSets(device,4,writes,0,nullptr);gbuffer_velocity.gpu_set=gpu_set;
    }
    ensure_motion_image();
    barrier(cmd,world.motion.handle,world.motion_sampled?VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,world.motion_sampled?VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT:VK_PIPELINE_STAGE_2_NONE,world.motion_sampled?VK_ACCESS_2_SHADER_SAMPLED_READ_BIT:0,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    return true;
}
// DX11 indexes t15[SV_InstanceID + InstanceBase]; each cell group starts at its history offset.
void gbuffer_velocity_bind_cell(VkCommandBuffer cmd,WorldSlot& slot,uint32_t group){
    if(!gbuffer_velocity.frame)throw std::runtime_error("native-cell velocity owner drawn outside its prepared GBuffer frame");
    auto& history=pass_scene.history_instances[slot_index];uint32_t at=group<pass_scene.history_offsets.size()?pass_scene.history_offsets[group]:UINT32_MAX;bool known=at!=UINT32_MAX&&history.handle;
    auto c=gbuffer_velocity_frame_constants(slot);c.has_history=known?1u:0u;c.prev_count=known?uint32_t(history.bytes/64):0u;c.instance_base=known?at/64:0u;
    uint32_t offset=gbuffer_velocity_store(c);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,gbuffer_velocity.pipeline_layout,2,1,&gbuffer_velocity.set,1,&offset);
}
void gbuffer_velocity_bind_gpu(VkCommandBuffer cmd,VkPipelineLayout layout){
    if(!gbuffer_velocity.gpu_set)throw std::runtime_error("GPU velocity owner drawn outside its prepared GBuffer frame");
    vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,layout,2,1,&gbuffer_velocity.gpu_set,1,&gbuffer_velocity.gpu_offset);
}
GBufferVelocityConstants gbuffer_velocity_frame_constants(WorldSlot& slot){
    auto& previous=world.slots[(slot_index+1)%world.slots.size()];GBufferVelocityConstants c{};
    const float* current_vp=reinterpret_cast<const float*>(slot.host_projection.data());
    const float* prior_vp=previous.host_projection_valid?reinterpret_cast<const float*>(previous.host_projection.data()):current_vp;
    // Vulkan compiles Keen geometry with -Zpr; Anomaly.hlsli transposes on load.
    for(uint32_t r=0;r<4;++r)for(uint32_t k=0;k<4;++k){c.view_proj[k*4+r]=current_vp[r*4+k];c.prev_view_proj[k*4+r]=prior_vp[r*4+k];}
    c.size[0]=float(extent.width);c.size[1]=float(extent.height);c.inv_size[0]=1.f/c.size[0];c.inv_size[1]=1.f/c.size[1];return c;
}
uint32_t gbuffer_velocity_store(const GBufferVelocityConstants& c){
    if(gbuffer_velocity.cursor>=gbuffer_velocity.capacity)throw std::runtime_error("GBuffer velocity draw outside its prepared frame budget");
    uint32_t offset=gbuffer_velocity.cursor++*gbuffer_velocity.stride;std::memcpy(static_cast<uint8_t*>(gbuffer_velocity.constants[slot_index].mapped)+offset,&c,sizeof(c));return offset;
}
// Writes one draw's AnomalyVelocity block and returns its dynamic offset.
uint32_t gbuffer_velocity_draw(WorldSlot& slot,const WorldDraw& draw){
    if(!gbuffer_velocity.frame)throw std::runtime_error("GBuffer velocity draw outside its prepared frame budget");
    auto& previous=world.slots[(slot_index+1)%world.slots.size()];const auto& pipe=world.pipelines[draw.packet.pipeline];
    auto c=gbuffer_velocity_frame_constants(slot);
    float curr[12]{},prev[12]{};
    bool rows=gbuffer_velocity.history&&motion_rows(slot.host_objects,draw.gpu_object_at,pipe.row0,pipe.row1,pipe.row2,curr)&&motion_rows(previous.host_objects,draw.gpu_object_at,pipe.row0,pipe.row1,pipe.row2,prev);
    if(rows){
        // Old-pipeline PrevRow is currToPrev = Prev * Invert(Curr), applied to camera-relative positions.
        double m[3][3],t[3],inv[3][3];for(uint32_t r=0;r<3;++r){for(uint32_t k=0;k<3;++k)m[r][k]=curr[r*4+k];t[r]=curr[r*4+3];}
        double det=m[0][0]*(m[1][1]*m[2][2]-m[1][2]*m[2][1])-m[0][1]*(m[1][0]*m[2][2]-m[1][2]*m[2][0])+m[0][2]*(m[1][0]*m[2][1]-m[1][1]*m[2][0]);
        if(std::abs(det)>1e-12){
            inv[0][0]=(m[1][1]*m[2][2]-m[1][2]*m[2][1])/det;inv[0][1]=(m[0][2]*m[2][1]-m[0][1]*m[2][2])/det;inv[0][2]=(m[0][1]*m[1][2]-m[0][2]*m[1][1])/det;
            inv[1][0]=(m[1][2]*m[2][0]-m[1][0]*m[2][2])/det;inv[1][1]=(m[0][0]*m[2][2]-m[0][2]*m[2][0])/det;inv[1][2]=(m[0][2]*m[1][0]-m[0][0]*m[1][2])/det;
            inv[2][0]=(m[1][0]*m[2][1]-m[1][1]*m[2][0])/det;inv[2][1]=(m[0][1]*m[2][0]-m[0][0]*m[2][1])/det;inv[2][2]=(m[0][0]*m[1][1]-m[0][1]*m[1][0])/det;
            for(uint32_t r=0;r<3;++r){double w=prev[r*4+3];for(uint32_t k=0;k<3;++k){double v=0;for(uint32_t j=0;j<3;++j)v+=prev[r*4+j]*inv[j][k];c.rows[r][k]=float(v);w-=v*t[k];}c.rows[r][3]=float(w);}
        }else rows=false;
    }
    c.has_history=rows?1u:0u;c.has_prev_world=rows?1u:0u;
    return gbuffer_velocity_store(c);
}
void gbuffer_velocity_end(){
    if(!gbuffer_velocity.frame||!gbuffer_velocity.cursor)return;
    check(vmaFlushAllocation(allocator,gbuffer_velocity.constants[slot_index].memory,0,uint64_t(gbuffer_velocity.cursor)*gbuffer_velocity.stride),"flush GBuffer velocity constants");
}
