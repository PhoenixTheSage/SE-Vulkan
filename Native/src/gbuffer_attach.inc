VkFormat attach_vk_format(uint32_t format){
    switch(format){
    case 0:return VK_FORMAT_R8G8B8A8_UNORM;
    case 1:return VK_FORMAT_R16G16B16A16_SFLOAT;
    case 2:return VK_FORMAT_R32_SFLOAT;
    case 3:return VK_FORMAT_R16G16_SFLOAT;
    case 4:return VK_FORMAT_R32_UINT;
    case 5:return VK_FORMAT_R32G32_SFLOAT;
    default:throw std::runtime_error("unsupported GBuffer attachment format");
    }
}
void cleanup_gbuffer_attachments() noexcept {
    for(auto& target:world.attach){
        for(auto& pipeline:target.pipe)if(pipeline){vkDestroyPipeline(device,pipeline,nullptr);pipeline=VK_NULL_HANDLE;}
        for(auto& pipeline:target.indexed_pipe)if(pipeline){vkDestroyPipeline(device,pipeline,nullptr);pipeline=VK_NULL_HANDLE;}
        destroy_image(target.image);target={};
    }
    cleanup_indexed_motion();cleanup_indexed_attachments();
    if(world.attach_pool){vkDestroyDescriptorPool(device,world.attach_pool,nullptr);world.attach_pool=VK_NULL_HANDLE;}
    world.attach_sets[0]=world.attach_sets[1]=VK_NULL_HANDLE;
    if(world.attach_layout){vkDestroyPipelineLayout(device,world.attach_layout,nullptr);world.attach_layout=VK_NULL_HANDLE;}
    if(world.attach_set_layout){vkDestroyDescriptorSetLayout(device,world.attach_set_layout,nullptr);world.attach_set_layout=VK_NULL_HANDLE;}
}
void ensure_attach_layout(){
    if(world.attach_set_layout)return;
    VkDescriptorSetLayoutBinding bindings[]{{0,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,1,VK_SHADER_STAGE_VERTEX_BIT,nullptr},{1,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,1,VK_SHADER_STAGE_VERTEX_BIT,nullptr}};
    VkDescriptorSetLayoutCreateInfo layout{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};layout.bindingCount=2;layout.pBindings=bindings;
    check(vkCreateDescriptorSetLayout(device,&layout,nullptr,&world.attach_set_layout),"create GBuffer attachment layout");
    VkPushConstantRange range{VK_SHADER_STAGE_VERTEX_BIT,0,112};
    VkPipelineLayoutCreateInfo pipeline{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};pipeline.setLayoutCount=1;pipeline.pSetLayouts=&world.attach_set_layout;pipeline.pushConstantRangeCount=1;pipeline.pPushConstantRanges=&range;
    check(vkCreatePipelineLayout(device,&pipeline,nullptr,&world.attach_layout),"create GBuffer attachment pipeline layout");
    VkDescriptorPoolSize sizes[]{{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,4}};
    VkDescriptorPoolCreateInfo pool{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};pool.maxSets=2;pool.poolSizeCount=1;pool.pPoolSizes=sizes;
    check(vkCreateDescriptorPool(device,&pool,nullptr,&world.attach_pool),"create GBuffer attachment descriptors");
    VkDescriptorSetLayout layouts[]{world.attach_set_layout,world.attach_set_layout};
    VkDescriptorSetAllocateInfo sets{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};sets.descriptorPool=world.attach_pool;sets.descriptorSetCount=2;sets.pSetLayouts=layouts;
    check(vkAllocateDescriptorSets(device,&sets,world.attach_sets),"allocate GBuffer attachment descriptors");
}
void init_gbuffer_attachment(uint32_t slot,uint32_t format,uint32_t kind,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes){
    if(recording || slot>2 || kind>3)throw std::runtime_error("invalid GBuffer attachment");
    VkFormat vk=attach_vk_format(format);
    wait(next_completion);ensure_attach_layout();
    auto& target=world.attach[slot];
    if(target.live && target.format!=format)throw std::runtime_error("GBuffer attachment format conflict");
    target.format=format;
    SpvReflectShaderModule vr{},pr{};VkShaderModule vs{},ps{};
    try{
        vs=world_shader(vertex,vertex_bytes,VK_SHADER_STAGE_VERTEX_BIT,vr);ps=world_shader(pixel,pixel_bytes,VK_SHADER_STAGE_FRAGMENT_BIT,pr);
        if(vr.push_constant_block_count!=1 || vr.push_constant_blocks[0].size!=112)throw std::runtime_error("GBuffer attachment vertex push constant mismatch");
        if(pr.descriptor_binding_count || pr.push_constant_block_count)throw std::runtime_error("GBuffer attachment pixel shader must declare no resources");
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
        if(target.pipe[kind])vkDestroyPipeline(device,target.pipe[kind],nullptr);
        target.pipe[kind]=world_graphics(vs,"__vertex_shader",ps,"__pixel_shader",world.attach_layout,bindings,attributes,&vk,1,true,false,false,false,0,true,VK_NULL_HANDLE,false,false,false,false,VK_CULL_MODE_NONE,VK_FRONT_FACE_COUNTER_CLOCKWISE,VK_COMPARE_OP_EQUAL,false,true);
        target.live=true;
        spvReflectDestroyShaderModule(&vr);spvReflectDestroyShaderModule(&pr);vkDestroyShaderModule(device,vs,nullptr);vkDestroyShaderModule(device,ps,nullptr);
    }catch(...){if(vr._internal)spvReflectDestroyShaderModule(&vr);if(pr._internal)spvReflectDestroyShaderModule(&pr);if(vs)vkDestroyShaderModule(device,vs,nullptr);if(ps)vkDestroyShaderModule(device,ps,nullptr);throw;}
}
void render_gbuffer_attachments(VkCommandBuffer cmd,WorldSlot& slot){
    bool ready=false;for(const auto& target:world.attach)ready|=target.live;
    if(!ready || !extent.width || !extent.height || !world.gbuffer[3].view)return;
    barrier(cmd,world.gbuffer[3].handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT|VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT);
    if(slot.projections.handle){
        VkBuffer prior=slot.history_projection.handle?slot.history_projection.handle:slot.projections.handle;
        VkDescriptorBufferInfo current{slot.projections.handle,0,64},before{prior,0,64};
        VkWriteDescriptorSet writes[2]{};for(auto& write:writes){write.sType=VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;write.dstSet=world.attach_sets[slot_index];write.descriptorCount=1;write.descriptorType=VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;}
        writes[0].dstBinding=0;writes[0].pBufferInfo=&current;writes[1].dstBinding=1;writes[1].pBufferInfo=&before;vkUpdateDescriptorSets(device,2,writes,0,nullptr);
    }
    bool indexed_ready=prepare_indexed_attachments();
    for(auto& target:world.attach){
        if(!target.live)continue;
        VkFormat vk=attach_vk_format(target.format);
        if(!target.image.view || target.w!=extent.width || target.h!=extent.height){
            wait(next_completion);if(target.image.view)destroy_image(target.image);
            target.image=make_image(extent.width,extent.height,vk,VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT,VK_IMAGE_ASPECT_COLOR_BIT);
            target.w=extent.width;target.h=extent.height;target.sampled=false;
        }
        barrier(cmd,target.image.handle,target.sampled?VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,target.sampled?VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT:VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT,target.sampled?VK_ACCESS_2_SHADER_SAMPLED_READ_BIT:0,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
        VkRenderingAttachmentInfo color{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};color.imageView=target.image.view;color.imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;color.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;color.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
        VkRenderingAttachmentInfo depth{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};depth.imageView=world.gbuffer[3].view;depth.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;depth.loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;depth.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
        VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.renderArea={{0,0},extent};rendering.layerCount=1;rendering.colorAttachmentCount=1;rendering.pColorAttachments=&color;rendering.pDepthAttachment=&depth;
        vkCmdBeginRendering(cmd,&rendering);world_viewport(cmd,extent.width,extent.height);
        if(slot.projections.handle){
            vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,world.attach_layout,0,1,&world.attach_sets[slot_index],0,nullptr);
            VkPipeline bound{};WorldRasterCache raster{};
            for(uint32_t i:world.current().order){
                const auto& draw=world.current().draws[i];const auto& packet=draw.packet;if((packet.flags&(4096|8192)) || world_unfade(packet.pipeline)>=20 || !packet.instance_count || !draw.geometry)continue;
                uint32_t type=world_unfade(packet.pipeline)%5;if(type==1)continue;uint32_t kind=type==0?0u:type-1;if(!target.pipe[kind])continue;
                const auto& pipe=world.pipelines[packet.pipeline];
                float curr[12]{};if(!motion_rows(slot.host_objects,draw.gpu_object_at,pipe.row0,pipe.row1,pipe.row2,curr))continue;
                uint64_t instance_bytes=uint64_t(packet.instance_count)*64;
                bool previous_ok=kind && slot.history_instances.handle && uint64_t(draw.instance_at)+instance_bytes<=slot.history_instance_bytes;
                if(kind && (uint64_t(draw.instance_at)+instance_bytes>slot.instances.bytes || !slot.instances.handle))continue;
                if(bound!=target.pipe[kind]){vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,target.pipe[kind]);bound=target.pipe[kind];}
                world_model_raster(cmd,packet.pipeline,packet.flags,false,VK_CULL_MODE_NONE,false,&raster);
                float push[28]{};std::memcpy(push,curr,sizeof(curr));std::memcpy(push+12,curr,sizeof(curr));push[24]=float(extent.width);push[25]=float(extent.height);push[26]=push[27]=motion_distance_fade(slot,packet.pipeline);
                vkCmdPushConstants(cmd,world.attach_layout,VK_SHADER_STAGE_VERTEX_BIT,0,112,push);
                VkBuffer instance=previous_ok?slot.history_instances.handle:slot.instances.handle;
                VkBuffer buffers[]{draw.geometry->vertices.handle,slot.instances.handle,instance};VkDeviceSize at[]{draw.geometry->vertex_offset,draw.instance_at,draw.instance_at};
                vkCmdBindVertexBuffers(cmd,0,kind?3u:1u,buffers,at);vkCmdBindIndexBuffer(cmd,draw.geometry->indices.handle,draw.geometry->index_offset,VK_INDEX_TYPE_UINT32);
                vkCmdDrawIndexed(cmd,draw.geometry->index_count,packet.instance_count,0,0,0);
            }
        }
        if(pass_scene.ready && slot.projections.handle && target.pipe[1]){
            vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,target.pipe[1]);
            float push[28]{};push[0]=push[5]=push[10]=push[12]=push[17]=push[22]=1;push[24]=-float(extent.width);push[25]=float(extent.height);
            vkCmdPushConstants(cmd,world.attach_layout,VK_SHADER_STAGE_VERTEX_BIT,0,112,push);
            const auto& groups=pass_scene.scene.render_groups();
            for(uint32_t i=0;i<groups.size();++i){const auto& group=groups[i];if(group.key.view!=0 || group.kind!=native_pass::Kind::Colour || !group.count())continue;
                const auto& mesh=world.meshes.at(group.key.mesh);
                world_model_raster(cmd,0,group.key.double_sided?0x10000000u:0x08000000u);
                VkBuffer buffers[]{mesh.vertices.handle,pass_scene.instances[slot_index].handle,pass_scene.instances[slot_index].handle};VkDeviceSize offsets[]{mesh.vertex_offset,pass_scene.offsets.at(i),pass_scene.offsets.at(i)};
                vkCmdBindVertexBuffers(cmd,0,3,buffers,offsets);vkCmdBindIndexBuffer(cmd,mesh.indices.handle,mesh.index_offset,VK_INDEX_TYPE_UINT32);
                vkCmdDrawIndexed(cmd,group.key.count,group.count(),group.key.first,0,0);
            }
        }
        if(indexed_ready)record_indexed_attachments(cmd,slot,target);
        vkCmdEndRendering(cmd);
        barrier(cmd,target.image.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        target.sampled=true;
    }
}
