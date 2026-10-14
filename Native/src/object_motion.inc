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
        world.motion_pipe[kind]=world_graphics(vs,"__vertex_shader",ps,"__pixel_shader",world.motion_layout,bindings,attributes,&format,1,true,false,false,false,0,true,VK_NULL_HANDLE,false,false,false,false,VK_CULL_MODE_NONE,VK_FRONT_FACE_COUNTER_CLOCKWISE,VK_COMPARE_OP_EQUAL);
        spvReflectDestroyShaderModule(&vr);spvReflectDestroyShaderModule(&pr);vkDestroyShaderModule(device,vs,nullptr);vkDestroyShaderModule(device,ps,nullptr);
    }catch(...){if(vr._internal)spvReflectDestroyShaderModule(&vr);if(pr._internal)spvReflectDestroyShaderModule(&pr);if(vs)vkDestroyShaderModule(device,vs,nullptr);if(ps)vkDestroyShaderModule(device,ps,nullptr);throw;}
}
bool motion_rows(const Buffer& buffer,uint32_t offset,uint32_t row0,uint32_t row1,uint32_t row2,float* out){
    if(row0==0xFFFFFFFFu || row1==0xFFFFFFFFu || row2==0xFFFFFFFFu || !buffer.mapped)return false;
    uint32_t end=std::max(row0,std::max(row1,row2))+16;if(uint64_t(offset)+end>buffer.bytes)return false;
    auto* base=static_cast<const uint8_t*>(buffer.mapped)+offset;
    std::memcpy(out,base+row0,16);std::memcpy(out+4,base+row1,16);std::memcpy(out+8,base+row2,16);return true;
}
void render_object_motion(VkCommandBuffer cmd,WorldSlot& slot){
    bool ready=false;for(auto pipeline:world.motion_pipe)ready|=pipeline!=VK_NULL_HANDLE;
    if(!ready || !extent.width || !extent.height || !world.gbuffer[3].view)return;
    if(!world.motion.view || world.motion_w!=extent.width || world.motion_h!=extent.height){
        wait(next_completion);if(world.motion.view)destroy_image(world.motion);
        world.motion=make_image(extent.width,extent.height,VK_FORMAT_R16G16_SFLOAT,VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT,VK_IMAGE_ASPECT_COLOR_BIT);
        world.motion_w=extent.width;world.motion_h=extent.height;world.motion_sampled=false;
    }
    barrier(cmd,world.motion.handle,world.motion_sampled?VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,world.motion_sampled?VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT:VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT,world.motion_sampled?VK_ACCESS_2_SHADER_SAMPLED_READ_BIT:0,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    barrier(cmd,world.gbuffer[3].handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT);
    VkRenderingAttachmentInfo color{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};color.imageView=world.motion.view;color.imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;color.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;color.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
    VkRenderingAttachmentInfo depth{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};depth.imageView=world.gbuffer[3].view;depth.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;depth.loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;depth.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
    VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.renderArea={{0,0},extent};rendering.layerCount=1;rendering.colorAttachmentCount=1;rendering.pColorAttachments=&color;rendering.pDepthAttachment=&depth;
    vkCmdBeginRendering(cmd,&rendering);world_viewport(cmd,extent.width,extent.height);
    uint64_t signature=14695981039346656037ull;auto mix=[&](uint64_t value){signature^=value;signature*=1099511628211ull;};
    mix(world.current().draws.size());for(const auto& draw:world.current().draws){mix(draw.packet.mesh);mix(draw.packet.pipeline);mix(draw.gpu_object_at);mix(draw.instance_at);mix(draw.packet.instance_count);}
    auto& previous=world.slots[(slot_index+1)%world.slots.size()];
    bool history=world.motion_have_signature && signature==world.motion_signature && previous.objects.mapped && previous.projections.handle && slot.projections.handle;
    if(history){
        VkDescriptorBufferInfo current{slot.projections.handle,0,64},prior{previous.projections.handle,0,64};
        VkWriteDescriptorSet writes[2]{};for(auto& write:writes){write.sType=VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;write.dstSet=world.motion_sets[slot_index];write.descriptorCount=1;write.descriptorType=VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;}
        writes[0].dstBinding=0;writes[0].pBufferInfo=&current;writes[1].dstBinding=1;writes[1].pBufferInfo=&prior;vkUpdateDescriptorSets(device,2,writes,0,nullptr);
        vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,world.motion_layout,0,1,&world.motion_sets[slot_index],0,nullptr);
        VkPipeline bound{};VkBuffer bound_vertex{},bound_index{},bound_current{},bound_previous{};
        for(uint32_t i:world.current().order){
            const auto& draw=world.current().draws[i];const auto& packet=draw.packet;if((packet.flags&(4096|8192)) || packet.pipeline>=20 || !packet.instance_count || !draw.geometry)continue;
            uint32_t type=packet.pipeline%5;if(type==1)continue;uint32_t kind=type==0?0u:type-1;if(!world.motion_pipe[kind])continue;
            const auto& pipe=world.pipelines[packet.pipeline];
            float curr[12]{},prev[12]{};if(!motion_rows(slot.objects,draw.gpu_object_at,pipe.row0,pipe.row1,pipe.row2,curr) || !motion_rows(previous.objects,draw.gpu_object_at,pipe.row0,pipe.row1,pipe.row2,prev))continue;
            uint64_t instance_bytes=uint64_t(packet.instance_count)*64;
            if(kind && (uint64_t(draw.instance_at)+instance_bytes>slot.instances.bytes || uint64_t(draw.instance_at)+instance_bytes>previous.instances.bytes || !slot.instances.handle || !previous.instances.handle))continue;
            if(bound!=world.motion_pipe[kind]){vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,world.motion_pipe[kind]);bound=world.motion_pipe[kind];bound_vertex={};}
            ObjectMotionPush push{};std::memcpy(push.rows,curr,sizeof(curr));std::memcpy(push.rows[3],prev,sizeof(prev));push.screen[0]=float(extent.width);push.screen[1]=float(extent.height);
            vkCmdPushConstants(cmd,world.motion_layout,VK_SHADER_STAGE_VERTEX_BIT,0,sizeof(push),&push);
            VkBuffer buffers[]{draw.geometry->vertices.handle,slot.instances.handle,previous.instances.handle};VkDeviceSize at[]{draw.geometry->vertex_offset,draw.instance_at,draw.instance_at};
            uint32_t streams=kind?3u:1u;
            if(bound_vertex!=buffers[0] || bound_index!=draw.geometry->indices.handle || (kind && (bound_current!=buffers[1] || bound_previous!=buffers[2]))){
                vkCmdBindVertexBuffers(cmd,0,streams,buffers,at);vkCmdBindIndexBuffer(cmd,draw.geometry->indices.handle,draw.geometry->index_offset,VK_INDEX_TYPE_UINT32);
                bound_vertex=buffers[0];bound_index=draw.geometry->indices.handle;bound_current=buffers[1];bound_previous=buffers[2];
            }
            vkCmdDrawIndexed(cmd,draw.geometry->index_count,packet.instance_count,0,0,0);
        }
    }
    vkCmdEndRendering(cmd);
    barrier(cmd,world.motion.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    world.motion_sampled=true;world.motion_signature=signature;world.motion_have_signature=true;
}
