struct ObjectMotionPush { float rows[6][4]; float screen[4]; };
static_assert(sizeof(ObjectMotionPush)==112, "object motion push constant");

void cleanup_object_motion() noexcept {
    for(auto& pipeline:world.motion_pipe)if(pipeline){vkDestroyPipeline(device,pipeline,nullptr);pipeline=VK_NULL_HANDLE;}
    if(world.motion_pool){vkDestroyDescriptorPool(device,world.motion_pool,nullptr);world.motion_pool=VK_NULL_HANDLE;}
    world.motion_sets[0]=world.motion_sets[1]=VK_NULL_HANDLE;
    if(world.motion_layout){vkDestroyPipelineLayout(device,world.motion_layout,nullptr);world.motion_layout=VK_NULL_HANDLE;}
    if(world.motion_set_layout){vkDestroyDescriptorSetLayout(device,world.motion_set_layout,nullptr);world.motion_set_layout=VK_NULL_HANDLE;}
    destroy_image(world.motion);world.motion_w=world.motion_h=0;world.motion_sampled=false;
}
void init_object_motion(uint32_t kind,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes){
    if(recording || kind>3)throw std::runtime_error("invalid object motion pipeline");
    wait(next_completion);init_world_layouts();
    VkPhysicalDeviceProperties props{};vkGetPhysicalDeviceProperties(physical,&props);
    if(props.limits.maxPushConstantsSize<sizeof(ObjectMotionPush))throw std::runtime_error("object motion push constant exceeds the device limit");
    if(!world.motion_set_layout){
        VkDescriptorSetLayoutBinding bindings[]{{0,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,1,VK_SHADER_STAGE_VERTEX_BIT,nullptr},{1,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,1,VK_SHADER_STAGE_VERTEX_BIT,nullptr}};
        VkDescriptorSetLayoutCreateInfo layout{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};layout.bindingCount=2;layout.pBindings=bindings;
        check(vkCreateDescriptorSetLayout(device,&layout,nullptr,&world.motion_set_layout),"create object motion layout");
        VkPushConstantRange range{VK_SHADER_STAGE_VERTEX_BIT,0,sizeof(ObjectMotionPush)};
        VkPipelineLayoutCreateInfo pipeline{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};pipeline.setLayoutCount=1;pipeline.pSetLayouts=&world.motion_set_layout;pipeline.pushConstantRangeCount=1;pipeline.pPushConstantRanges=&range;
        check(vkCreatePipelineLayout(device,&pipeline,nullptr,&world.motion_layout),"create object motion pipeline layout");
        VkDescriptorPoolSize sizes[]{{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,4}};
        VkDescriptorPoolCreateInfo pool{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};pool.maxSets=2;pool.poolSizeCount=1;pool.pPoolSizes=sizes;
        check(vkCreateDescriptorPool(device,&pool,nullptr,&world.motion_pool),"create object motion descriptors");
        VkDescriptorSetLayout layouts[]{world.motion_set_layout,world.motion_set_layout};
        VkDescriptorSetAllocateInfo sets{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};sets.descriptorPool=world.motion_pool;sets.descriptorSetCount=2;sets.pSetLayouts=layouts;
        check(vkAllocateDescriptorSets(device,&sets,world.motion_sets),"allocate object motion descriptors");
    }
    SpvReflectShaderModule vr{},pr{};VkShaderModule vs{},ps{};
    try{
        vs=world_shader(vertex,vertex_bytes,VK_SHADER_STAGE_VERTEX_BIT,vr);ps=world_shader(pixel,pixel_bytes,VK_SHADER_STAGE_FRAGMENT_BIT,pr);
        if(vr.push_constant_block_count!=1 || vr.push_constant_blocks[0].size!=sizeof(ObjectMotionPush))throw std::runtime_error("object motion vertex push constant mismatch");
        std::vector<VkVertexInputBindingDescription> bindings{{0,uint32_t(sizeof(SevkWorldVertex)),VK_VERTEX_INPUT_RATE_VERTEX}};
        std::vector<VkVertexInputAttributeDescription> attributes{{0,0,VK_FORMAT_R32G32B32A32_SFLOAT,0}};
        if(kind){bindings.push_back({1,64,VK_VERTEX_INPUT_RATE_INSTANCE});bindings.push_back({2,64,VK_VERTEX_INPUT_RATE_INSTANCE});}
        if(kind==1)for(uint32_t i=0;i<3;++i){attributes.push_back({1+i,1,VK_FORMAT_R32G32B32A32_SFLOAT,i*16});attributes.push_back({4+i,2,VK_FORMAT_R32G32B32A32_SFLOAT,i*16});}
        else if(kind==2){attributes.push_back({1,1,VK_FORMAT_R32G32B32A32_SFLOAT,32});attributes.push_back({2,2,VK_FORMAT_R32G32B32A32_SFLOAT,32});}
        else if(kind==3){
            attributes.push_back({1,0,VK_FORMAT_R32G32B32A32_SFLOAT,48});attributes.push_back({2,0,VK_FORMAT_R32G32B32A32_UINT,64});
            for(uint32_t i=0;i<8;++i){attributes.push_back({3+i,1,VK_FORMAT_R8G8B8A8_UNORM,i*4});attributes.push_back({12+i,2,VK_FORMAT_R8G8B8A8_UNORM,i*4});}
            attributes.push_back({11,1,VK_FORMAT_R32G32B32A32_SFLOAT,32});attributes.push_back({20,2,VK_FORMAT_R32G32B32A32_SFLOAT,32});
        }
        const VkFormat format=VK_FORMAT_R16G16_SFLOAT;
        if(world.motion_pipe[kind])vkDestroyPipeline(device,world.motion_pipe[kind],nullptr);
        world.motion_pipe[kind]=world_graphics(vs,"__vertex_shader",ps,"__pixel_shader",world.motion_layout,bindings,attributes,&format,1,true,false,false,false,0,true,VK_NULL_HANDLE,false,false,false,false,VK_CULL_MODE_NONE,VK_FRONT_FACE_COUNTER_CLOCKWISE,VK_COMPARE_OP_EQUAL,false,true);
        spvReflectDestroyShaderModule(&vr);spvReflectDestroyShaderModule(&pr);vkDestroyShaderModule(device,vs,nullptr);vkDestroyShaderModule(device,ps,nullptr);
    }catch(...){if(vr._internal)spvReflectDestroyShaderModule(&vr);if(pr._internal)spvReflectDestroyShaderModule(&pr);if(vs)vkDestroyShaderModule(device,vs,nullptr);if(ps)vkDestroyShaderModule(device,ps,nullptr);throw;}
}
// Reads the slot's host copy of its uploaded object constants, never the
// mapped upload memory (uncached BAR reads cost a PCIe round trip each).
bool motion_rows(const std::vector<uint8_t>& objects,uint32_t offset,uint32_t row0,uint32_t row1,uint32_t row2,float* out){
    if(row0==0xFFFFFFFFu || row1==0xFFFFFFFFu || row2==0xFFFFFFFFu || objects.empty())return false;
    uint32_t end=std::max(row0,std::max(row1,row2))+16;if(uint64_t(offset)+end>objects.size())return false;
    auto* base=objects.data()+offset;
    std::memcpy(out,base+row0,16);std::memcpy(out+4,base+row1,16);std::memcpy(out+8,base+row2,16);return true;
}
float motion_distance_fade(const WorldSlot& slot,uint32_t pipeline){
    if(pipeline<2560 || slot.host_frame.size()<4 || world.distance_fade_at>slot.host_frame.size()-4)return 0.f;
    float threshold;std::memcpy(&threshold,slot.host_frame.data()+world.distance_fade_at,4);return -threshold;
}
// Previous data is copied into the fenced current slot before either history
// consumer records commands. No submitted frame borrows another slot's buffers.
void prepare_world_history(WorldSlot& slot){
    auto& previous=world.slots[(slot_index+1)%world.slots.size()];slot.history_instance_bytes=0;
    if(!slot.projections.mapped)return;
    world_buffer(slot.history_projection,64,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);
    std::memcpy(slot.history_projection.mapped,previous.host_projection_valid?previous.host_projection.data():slot.host_projection.data(),64);
    check(vmaFlushAllocation(allocator,slot.history_projection.memory,0,64),"flush slot-owned previous projection");
    uint64_t required=0;
    for(const auto& draw:world.current().draws){const auto& packet=draw.packet;uint32_t base=world_unfade(packet.pipeline);
        if(!(packet.flags&(4096|8192))&&base<20&&base%5>=2&&draw.geometry)
            required=std::max(required,uint64_t(draw.instance_at)+uint64_t(packet.instance_count)*64);}
    if(!required||!previous.instances.mapped)return;
    uint64_t bytes=std::min({required,previous.instance_bytes_used,uint64_t(previous.host_instances.size())});if(!bytes)return;
    world_buffer(slot.history_instances,bytes,VK_BUFFER_USAGE_VERTEX_BUFFER_BIT);
    std::memcpy(slot.history_instances.mapped,previous.host_instances.data(),size_t(bytes));
    check(vmaFlushAllocation(allocator,slot.history_instances.memory,0,bytes),"flush slot-owned previous instances");slot.history_instance_bytes=bytes;
}
void render_object_motion(VkCommandBuffer cmd,WorldSlot& slot){
    bool ready=false;for(auto pipeline:world.motion_pipe)ready|=pipeline!=VK_NULL_HANDLE;
    bool written=gbuffer_velocity.frame;
    if((!ready&&!written) || !extent.width || !extent.height || !world.gbuffer[3].view)return;
    ensure_motion_image();
    bool indexed_ready=ready&&prepare_indexed_motion(slot);
    if(written)barrier(cmd,world.motion.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT|VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    else barrier(cmd,world.motion.handle,world.motion_sampled?VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,world.motion_sampled?VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT:VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT,world.motion_sampled?VK_ACCESS_2_SHADER_SAMPLED_READ_BIT:0,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    barrier(cmd,world.gbuffer[3].handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT);
    VkRenderingAttachmentInfo color{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};color.imageView=world.motion.view;color.imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;color.loadOp=written?VK_ATTACHMENT_LOAD_OP_LOAD:VK_ATTACHMENT_LOAD_OP_CLEAR;color.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
    VkRenderingAttachmentInfo depth{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};depth.imageView=world.gbuffer[3].view;depth.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;depth.loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;depth.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
    VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.renderArea={{0,0},extent};rendering.layerCount=1;rendering.colorAttachmentCount=1;rendering.pColorAttachments=&color;rendering.pDepthAttachment=&depth;
    vkCmdBeginRendering(cmd,&rendering);world_viewport(cmd,extent.width,extent.height);
    uint64_t signature=motion_signature();
    auto& previous=world.slots[(slot_index+1)%world.slots.size()];
    bool history=world.motion_have_signature && signature==world.motion_signature && previous.objects.mapped && previous.projections.mapped && slot.projections.handle;
    if(ready && slot.projections.handle && slot.history_projection.handle){
        VkDescriptorBufferInfo current{slot.projections.handle,0,64},prior{slot.history_projection.handle,0,64};
        VkWriteDescriptorSet writes[2]{};for(auto& write:writes){write.sType=VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;write.dstSet=world.motion_sets[slot_index];write.descriptorCount=1;write.descriptorType=VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;}
        writes[0].dstBinding=0;writes[0].pBufferInfo=&current;writes[1].dstBinding=1;writes[1].pBufferInfo=&prior;vkUpdateDescriptorSets(device,2,writes,0,nullptr);
        vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,world.motion_layout,0,1,&world.motion_sets[slot_index],0,nullptr);
    }
    if(ready&&history){
        VkPipeline bound{};VkBuffer bound_vertex{},bound_index{},bound_current{},bound_previous{};VkDeviceSize bound_vertex_at{},bound_index_at{},bound_instance_at{};WorldRasterCache raster{};
        for(uint32_t i:world.current().order){
            const auto& draw=world.current().draws[i];const auto& packet=draw.packet;if((packet.flags&(4096|8192)) || world_unfade(packet.pipeline)>=20 || !packet.instance_count || !draw.geometry)continue;
            if(written&&world.pipelines[packet.pipeline].velocity)continue;uint32_t type=world_unfade(packet.pipeline)%5;if(type==1)continue;uint32_t kind=type==0?0u:type-1;if(!world.motion_pipe[kind])continue;
            const auto& pipe=world.pipelines[packet.pipeline];
            float curr[12]{},prev[12]{};if(!motion_rows(slot.host_objects,draw.gpu_object_at,pipe.row0,pipe.row1,pipe.row2,curr) || !motion_rows(previous.host_objects,draw.gpu_object_at,pipe.row0,pipe.row1,pipe.row2,prev))continue;
            uint64_t instance_bytes=uint64_t(packet.instance_count)*64;
            if(kind && (uint64_t(draw.instance_at)+instance_bytes>slot.instances.bytes || uint64_t(draw.instance_at)+instance_bytes>slot.history_instance_bytes || !slot.instances.handle || !slot.history_instances.handle))continue;
            if(bound!=world.motion_pipe[kind]){vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,world.motion_pipe[kind]);bound=world.motion_pipe[kind];bound_vertex={};}
            world_model_raster(cmd,packet.pipeline,packet.flags,false,VK_CULL_MODE_NONE,false,&raster);
            ObjectMotionPush push{};std::memcpy(push.rows,curr,sizeof(curr));std::memcpy(push.rows[3],prev,sizeof(prev));push.screen[0]=float(extent.width);push.screen[1]=float(extent.height);push.screen[2]=motion_distance_fade(slot,packet.pipeline);push.screen[3]=motion_distance_fade(previous,packet.pipeline);
            vkCmdPushConstants(cmd,world.motion_layout,VK_SHADER_STAGE_VERTEX_BIT,0,sizeof(push),&push);
            VkBuffer buffers[]{draw.geometry->vertices.handle,slot.instances.handle,slot.history_instances.handle};VkDeviceSize at[]{draw.geometry->vertex_offset,draw.instance_at,draw.instance_at};
            uint32_t streams=kind?3u:1u;
            if(bound_vertex!=buffers[0] || bound_index!=draw.geometry->indices.handle || bound_vertex_at!=at[0] || bound_index_at!=draw.geometry->index_offset || (kind && (bound_current!=buffers[1] || bound_previous!=buffers[2] || bound_instance_at!=at[1]))){
                vkCmdBindVertexBuffers(cmd,0,streams,buffers,at);vkCmdBindIndexBuffer(cmd,draw.geometry->indices.handle,draw.geometry->index_offset,VK_INDEX_TYPE_UINT32);
                bound_vertex=buffers[0];bound_index=draw.geometry->indices.handle;bound_current=buffers[1];bound_previous=buffers[2];bound_vertex_at=at[0];bound_index_at=draw.geometry->index_offset;bound_instance_at=at[1];
            }
            vkCmdDrawIndexed(cmd,draw.geometry->index_count,packet.instance_count,0,0,0);
        }
    }
    if(ready && pass_scene.ready && slot.projections.handle && slot.history_projection.handle && world.motion_pipe[1] && pass_scene.history_instances[slot_index].handle){
        vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,world.motion_pipe[1]);
        ObjectMotionPush push{};push.rows[0][0]=push.rows[1][1]=push.rows[2][2]=push.rows[3][0]=push.rows[4][1]=push.rows[5][2]=1;push.screen[0]=float(extent.width);push.screen[1]=float(extent.height);
        vkCmdPushConstants(cmd,world.motion_layout,VK_SHADER_STAGE_VERTEX_BIT,0,sizeof(push),&push);
        const auto& groups=pass_scene.scene.render_groups();
        for(uint32_t i=0;i<groups.size();++i){const auto& group=groups[i];if(group.key.view!=0 || group.kind!=native_pass::Kind::Colour || !group.count() || pass_scene.history_offsets.at(i)==UINT32_MAX)continue;
            const auto& mesh=world.meshes.at(group.key.mesh);world_model_raster(cmd,0,group.key.double_sided?0x10000000u:0x08000000u);
            VkBuffer buffers[]{mesh.vertices.handle,pass_scene.instances[slot_index].handle,pass_scene.history_instances[slot_index].handle};VkDeviceSize offsets[]{mesh.vertex_offset,pass_scene.offsets.at(i),pass_scene.history_offsets[i]};
            vkCmdBindVertexBuffers(cmd,0,3,buffers,offsets);vkCmdBindIndexBuffer(cmd,mesh.indices.handle,mesh.index_offset,VK_INDEX_TYPE_UINT32);
            vkCmdDrawIndexed(cmd,group.key.count,group.count(),group.key.first,0,0);
        }
    }
    if(indexed_ready)record_indexed_motion(cmd,slot);
    vkCmdEndRendering(cmd);
    barrier(cmd,world.motion.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    world.motion_sampled=true;world.motion_signature=signature;world.motion_have_signature=true;
}
