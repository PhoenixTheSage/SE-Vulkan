// Members of Renderer: first native execution path for the installed material
// shaders. Explicit offscreen work is synchronous; no per-draw GPU waits.
static constexpr std::array<VkFormat,3> stock_formats{VK_FORMAT_R8G8B8A8_SRGB,VK_FORMAT_A2B10G10R10_UNORM_PACK32,VK_FORMAT_R8G8B8A8_UNORM};
void cleanup_stock() noexcept {
    for(auto& item:stock.meshes) destroy_mesh(item.second);
    for(auto& image:stock.images) destroy_image(image);
    destroy_image(stock.dither);
    destroy_buffer(stock.frame); destroy_buffer(stock.projection); destroy_buffer(stock.objects);
    if(stock.pipeline) vkDestroyPipeline(device,stock.pipeline,nullptr);
    if(stock.pool) vkDestroyDescriptorPool(device,stock.pool,nullptr);
    if(stock.pipeline_layout) vkDestroyPipelineLayout(device,stock.pipeline_layout,nullptr);
    if(stock.layout) vkDestroyDescriptorSetLayout(device,stock.layout,nullptr);
    stock={};
}
VkShaderModule stock_shader(const void* data,uint64_t bytes,VkShaderStageFlagBits stage) {
    if(!data || bytes<20 || bytes>16*1024*1024 || bytes%4) throw std::runtime_error("invalid stock SPIR-V size");
    std::vector<uint32_t> words(bytes/4); std::memcpy(words.data(),data,bytes);
    // This first material pass enables Shader and shader demote only. Other
    // catalog capabilities belong to their own feature-gated native passes.
    for(size_t offset=5;offset<words.size();) {
        uint32_t length=words[offset]>>16,opcode=words[offset]&65535;
        if(!length || length>words.size()-offset) throw std::runtime_error("malformed stock SPIR-V instruction");
        if(opcode==10) throw std::runtime_error("stock GBuffer expects driver-ready SPIR-V without extension declarations; prepare reflection annotations before submission");
        if(opcode==17 && (length!=2 || (words[offset+1]!=1 && words[offset+1]!=5379))) throw std::runtime_error("stock GBuffer shader capability is not enabled by this pass");
        offset+=length;
    }
    SpvReflectShaderModule reflected{};
    if(spvReflectCreateShaderModule(bytes,words.data(),&reflected)!=SPV_REFLECT_RESULT_SUCCESS) throw std::runtime_error("stock shader reflection failed");
    bool valid=uint32_t(reflected.shader_stage)==uint32_t(stage) && reflected.entry_point_name &&
        std::strcmp(reflected.entry_point_name,stage==VK_SHADER_STAGE_VERTEX_BIT?"__vertex_shader":"__pixel_shader")==0 && !reflected.push_constant_block_count;
    for(uint32_t i=0;i<reflected.descriptor_binding_count;++i) {
        auto& b=reflected.descriptor_bindings[i];
        bool uniform=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER &&
            ((b.binding==0 && b.block.size==1280) || (b.binding==1 && b.block.size==64) || (b.binding==2 && b.block.size==128));
        bool image=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE && (b.binding>=128 && b.binding<=131 || b.binding==156) && b.image.dim==SpvDim2D && !b.image.arrayed && !b.image.ms;
        bool sample=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER && (b.binding==258 || b.binding==259);
        if(b.set!=0 || b.count!=1 || !(uniform||image||sample)) valid=false;
    }
    if(stage==VK_SHADER_STAGE_VERTEX_BIT) {
        std::array<bool,4> seen{};
        const std::array<SpvReflectFormat,4> expected{SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT,SPV_REFLECT_FORMAT_R32G32_UINT,SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT,SPV_REFLECT_FORMAT_R32G32_SFLOAT};
        for(uint32_t i=0;i<reflected.input_variable_count;++i) {
            auto& v=*reflected.input_variables[i]; if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN) continue;
            if(v.location>=4 || v.format!=expected[v.location]) valid=false; else seen[v.location]=true;
        }
        for(bool value:seen) valid&=value;
    } else {
        std::array<bool,3> seen{};
        for(uint32_t i=0;i<reflected.output_variable_count;++i) {
            auto& v=*reflected.output_variables[i]; if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN) continue;
            if(v.location>=3 || v.format!=SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT) valid=false; else seen[v.location]=true;
        }
        for(bool value:seen) valid&=value;
    }
    spvReflectDestroyShaderModule(&reflected);
    if(!valid) throw std::runtime_error("stock GBuffer shader does not match the supported reflected material contract");
    VkShaderModuleCreateInfo info{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};info.codeSize=bytes;info.pCode=words.data();
    VkShaderModule module{};check(vkCreateShaderModule(device,&info,nullptr,&module),"create stock shader");return module;
}
void init_stock(const void* vs,uint64_t vs_bytes,const void* ps,uint64_t ps_bytes) {
    if(recording) throw std::runtime_error("stock pipeline initialization inside a main frame");
    wait(next_completion);cleanup_stock();
    VkShaderModule vertex{},pixel{};
    try {
        vertex=stock_shader(vs,vs_bytes,VK_SHADER_STAGE_VERTEX_BIT);pixel=stock_shader(ps,ps_bytes,VK_SHADER_STAGE_FRAGMENT_BIT);
        std::array<VkDescriptorSetLayoutBinding,10> bindings{};
        const uint32_t indices[]{0,1,2,128,129,130,131,156,258,259};
        for(uint32_t i=0;i<10;++i) bindings[i]={indices[i],i==2?VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC:(i<3?VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER:(i<8?VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE:VK_DESCRIPTOR_TYPE_SAMPLER)),1,VK_SHADER_STAGE_VERTEX_BIT|VK_SHADER_STAGE_FRAGMENT_BIT,nullptr};
        VkDescriptorSetLayoutCreateInfo layout{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};layout.bindingCount=10;layout.pBindings=bindings.data();
        check(vkCreateDescriptorSetLayout(device,&layout,nullptr,&stock.layout),"create stock descriptor layout");
        VkPipelineLayoutCreateInfo pipeline_layout_info{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};pipeline_layout_info.setLayoutCount=1;pipeline_layout_info.pSetLayouts=&stock.layout;
        check(vkCreatePipelineLayout(device,&pipeline_layout_info,nullptr,&stock.pipeline_layout),"create stock pipeline layout");
        std::array<VkDescriptorPoolSize,4> sizes{{{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,16384},{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC,8192},{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,40960},{VK_DESCRIPTOR_TYPE_SAMPLER,16384}}};
        VkDescriptorPoolCreateInfo pool{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};pool.maxSets=8192;pool.poolSizeCount=4;pool.pPoolSizes=sizes.data();
        check(vkCreateDescriptorPool(device,&pool,nullptr,&stock.pool),"create stock descriptor pool");
        std::array<VkPipelineShaderStageCreateInfo,2> stages{{{VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_VERTEX_BIT,vertex,"__vertex_shader",nullptr},{VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_FRAGMENT_BIT,pixel,"__pixel_shader",nullptr}}};
        VkVertexInputBindingDescription binding{0,sizeof(SevkGBufferVertex),VK_VERTEX_INPUT_RATE_VERTEX};
        std::array<VkVertexInputAttributeDescription,4> attributes{{{0,0,VK_FORMAT_R32G32B32A32_SFLOAT,0},{1,0,VK_FORMAT_R32G32_UINT,16},{2,0,VK_FORMAT_R32G32B32A32_SFLOAT,24},{3,0,VK_FORMAT_R32G32_SFLOAT,40}}};
        static_assert(offsetof(SevkGBufferVertex,uv)==40);
        VkPipelineVertexInputStateCreateInfo input{VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO};input.vertexBindingDescriptionCount=1;input.pVertexBindingDescriptions=&binding;input.vertexAttributeDescriptionCount=4;input.pVertexAttributeDescriptions=attributes.data();
        VkPipelineInputAssemblyStateCreateInfo assembly{VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO};assembly.topology=VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
        VkPipelineViewportStateCreateInfo viewport{VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO};viewport.viewportCount=viewport.scissorCount=1;
        VkPipelineRasterizationStateCreateInfo raster{VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO};raster.polygonMode=VK_POLYGON_MODE_FILL;raster.cullMode=VK_CULL_MODE_NONE;raster.frontFace=VK_FRONT_FACE_COUNTER_CLOCKWISE;raster.lineWidth=1;
        VkPipelineMultisampleStateCreateInfo samples{VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO};samples.rasterizationSamples=VK_SAMPLE_COUNT_1_BIT;
        VkPipelineDepthStencilStateCreateInfo depth_state{VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO};depth_state.depthTestEnable=depth_state.depthWriteEnable=VK_TRUE;depth_state.depthCompareOp=VK_COMPARE_OP_GREATER_OR_EQUAL;
        std::array<VkPipelineColorBlendAttachmentState,3> blend_attachments{};for(auto& b:blend_attachments)b.colorWriteMask=15;
        VkPipelineColorBlendStateCreateInfo blend{VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO};blend.attachmentCount=3;blend.pAttachments=blend_attachments.data();
        std::array<VkDynamicState,2> states{VK_DYNAMIC_STATE_VIEWPORT,VK_DYNAMIC_STATE_SCISSOR};VkPipelineDynamicStateCreateInfo dynamic{VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO};dynamic.dynamicStateCount=2;dynamic.pDynamicStates=states.data();
        VkPipelineRenderingCreateInfo rendering{VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO};rendering.colorAttachmentCount=3;rendering.pColorAttachmentFormats=stock_formats.data();rendering.depthAttachmentFormat=VK_FORMAT_D32_SFLOAT;
        VkGraphicsPipelineCreateInfo pipeline{VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO,&rendering};pipeline.stageCount=2;pipeline.pStages=stages.data();pipeline.pVertexInputState=&input;pipeline.pInputAssemblyState=&assembly;pipeline.pViewportState=&viewport;pipeline.pRasterizationState=&raster;pipeline.pMultisampleState=&samples;pipeline.pDepthStencilState=&depth_state;pipeline.pColorBlendState=&blend;pipeline.pDynamicState=&dynamic;pipeline.layout=stock.pipeline_layout;
        check(vkCreateGraphicsPipelines(device,pipeline_cache,1,&pipeline,nullptr,&stock.pipeline),"create stock GBuffer pipeline");
        stock.frame=make_buffer(1280,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,true);stock.projection=make_buffer(64,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,true);
        // Exact MyGeneratedTextureManager DITHER_8x8 values, multiplied by four.
        const uint8_t pattern[64]{0,32,8,40,2,34,10,42,48,16,56,24,50,18,58,26,12,44,4,36,14,46,6,38,60,28,52,20,62,30,54,22,3,35,11,43,1,33,9,41,51,19,59,27,49,17,57,25,15,47,7,39,13,45,5,37,63,31,55,23,61,29,53,21};
        stock.dither=make_image(8,8,VK_FORMAT_R8_UNORM,VK_IMAGE_USAGE_TRANSFER_DST_BIT|VK_IMAGE_USAGE_SAMPLED_BIT,VK_IMAGE_ASPECT_COLOR_BIT);
        Buffer dither_upload=make_buffer(64,VK_BUFFER_USAGE_TRANSFER_SRC_BIT,true);
        try {for(uint32_t i=0;i<64;++i)static_cast<uint8_t*>(dither_upload.mapped)[i]=pattern[i]*4;check(vmaFlushAllocation(allocator,dither_upload.memory,0,64),"flush stock dither");
            immediate([&](VkCommandBuffer cmd){barrier(cmd,stock.dither.handle,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_NONE,0,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT);VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};copy.imageExtent={8,8,1};vkCmdCopyBufferToImage(cmd,dither_upload.handle,stock.dither.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&copy);barrier(cmd,stock.dither.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);});destroy_buffer(dither_upload);
        }catch(...){destroy_buffer(dither_upload);throw;}
        VkPhysicalDeviceProperties props{};vkGetPhysicalDeviceProperties(physical,&props);stock.object_stride=(128+props.limits.minUniformBufferOffsetAlignment-1)/props.limits.minUniformBufferOffsetAlignment*props.limits.minUniformBufferOffsetAlignment;
        stock.stats.size=sizeof(SevkGBufferStats);stock.stats.abi_version=SEVK_ABI_VERSION;stock.stats.ready=1;
        vkDestroyShaderModule(device,pixel,nullptr);vkDestroyShaderModule(device,vertex,nullptr);
    } catch(...) {if(pixel)vkDestroyShaderModule(device,pixel,nullptr);if(vertex)vkDestroyShaderModule(device,vertex,nullptr);cleanup_stock();throw;}
}
void upload_stock(uint64_t id,const SevkGBufferVertex* vertices,uint32_t vertex_count,const uint32_t* indices,uint32_t index_count) {
    if(recording || !stock.pipeline || !vertices || !indices || !vertex_count || !index_count || vertex_count>10000000 || index_count>30000000) throw std::runtime_error("invalid stock mesh upload");
    for(uint32_t i=0;i<index_count;++i)if(indices[i]>=vertex_count)throw std::runtime_error("stock mesh index outside vertices");
    uint64_t vb=uint64_t(vertex_count)*sizeof(SevkGBufferVertex),ib=uint64_t(index_count)*4;
    Mesh mesh{};Buffer staging{};
    try {
        staging=make_buffer(vb+ib,VK_BUFFER_USAGE_TRANSFER_SRC_BIT,true);std::memcpy(staging.mapped,vertices,vb);std::memcpy(static_cast<uint8_t*>(staging.mapped)+vb,indices,ib);check(vmaFlushAllocation(allocator,staging.memory,0,vb+ib),"flush stock mesh");
        mesh.vertices=make_buffer(vb,VK_BUFFER_USAGE_TRANSFER_DST_BIT|VK_BUFFER_USAGE_VERTEX_BUFFER_BIT,false);mesh.indices=make_buffer(ib,VK_BUFFER_USAGE_TRANSFER_DST_BIT|VK_BUFFER_USAGE_INDEX_BUFFER_BIT,false);mesh.index_count=index_count;
        immediate([&](VkCommandBuffer cmd){VkBufferCopy v{0,0,vb},i{vb,0,ib};vkCmdCopyBuffer(cmd,staging.handle,mesh.vertices.handle,1,&v);vkCmdCopyBuffer(cmd,staging.handle,mesh.indices.handle,1,&i);
            std::array<VkBufferMemoryBarrier2,2> barriers{};for(auto& b:barriers){b.sType=VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;b.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;b.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;b.dstStageMask=VK_PIPELINE_STAGE_2_VERTEX_INPUT_BIT;b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;b.size=VK_WHOLE_SIZE;}
            barriers[0].buffer=mesh.vertices.handle;barriers[0].dstAccessMask=VK_ACCESS_2_VERTEX_ATTRIBUTE_READ_BIT;barriers[1].buffer=mesh.indices.handle;barriers[1].dstAccessMask=VK_ACCESS_2_INDEX_READ_BIT;VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.bufferMemoryBarrierCount=2;d.pBufferMemoryBarriers=barriers.data();vkCmdPipelineBarrier2(cmd,&d);});
        destroy_buffer(staging);auto old=stock.meshes.find(id);if(old!=stock.meshes.end()){destroy_mesh(old->second);old->second=mesh;}else stock.meshes.emplace(id,mesh);stats.uploaded_bytes+=vb+ib;
    } catch(...){destroy_buffer(staging);destroy_mesh(mesh);throw;}
}
void render_stock(uint32_t width,uint32_t height,const void* frame,uint32_t frame_bytes,const float* projection,const SevkGBufferDraw* draws,uint32_t count) {
    if(recording || !stock.pipeline || !frame || frame_bytes!=1280 || !projection || (!draws && count) || count>8192 || !width || !height || width>8192 || height>8192)throw std::runtime_error("invalid stock GBuffer frame/size/batch");
    for(uint32_t i=0;i<count;++i){if(!stock.meshes.count(draws[i].mesh_id))throw std::runtime_error("stock draw references missing mesh");
        for(uint64_t id:{draws[i].color_id,draws[i].normal_id,draws[i].extensions_id,draws[i].alpha_id})if(!textures.count(id))throw std::runtime_error("stock draw references missing texture");}
    wait(next_completion);
    if(width!=stock.stats.width || height!=stock.stats.height) {
        stock.initialized=false;for(auto& image:stock.images)destroy_image(image);
        for(uint32_t i=0;i<3;++i)stock.images[i]=make_image(width,height,stock_formats[i],VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT,VK_IMAGE_ASPECT_COLOR_BIT);
        stock.images[3]=make_image(width,height,VK_FORMAT_D32_SFLOAT,VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT,VK_IMAGE_ASPECT_DEPTH_BIT);
        stock.stats.width=width;stock.stats.height=height;
    }
    if(count>stock.object_capacity){destroy_buffer(stock.objects);stock.object_capacity=std::max(count,64u);stock.objects=make_buffer(stock.object_stride*stock.object_capacity,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,true);}
    std::memcpy(stock.frame.mapped,frame,1280);std::memcpy(stock.projection.mapped,projection,64);
    check(vmaFlushAllocation(allocator,stock.frame.memory,0,1280),"flush stock frame");check(vmaFlushAllocation(allocator,stock.projection.memory,0,64),"flush stock projection");
    for(uint32_t i=0;i<count;++i)std::memcpy(static_cast<uint8_t*>(stock.objects.mapped)+i*stock.object_stride,draws[i].object_constants,128);
    if(count)check(vmaFlushAllocation(allocator,stock.objects.memory,0,count*stock.object_stride),"flush stock objects");
    check(vkResetDescriptorPool(device,stock.pool,0),"reset stock descriptors");std::vector<VkDescriptorSet> sets(count);
    for(uint32_t i=0;i<count;++i){VkDescriptorSetAllocateInfo alloc{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};alloc.descriptorPool=stock.pool;alloc.descriptorSetCount=1;alloc.pSetLayouts=&stock.layout;check(vkAllocateDescriptorSets(device,&alloc,&sets[i]),"allocate stock material");
        std::array<VkDescriptorBufferInfo,3> buffers{{{stock.frame.handle,0,1280},{stock.projection.handle,0,64},{stock.objects.handle,0,128}}};
        std::array<uint64_t,5> ids{draws[i].color_id,draws[i].normal_id,draws[i].extensions_id,draws[i].alpha_id,0};std::array<VkDescriptorImageInfo,7> images{};
        for(uint32_t j=0;j<4;++j)images[j]={VK_NULL_HANDLE,texture(ids[j]).image.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};images[4]={VK_NULL_HANDLE,stock.dither.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};images[5]={standard_sampler(258),VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};images[6]={standard_sampler(259),VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};
        const uint32_t indices[]{0,1,2,128,129,130,131,156,258,259};std::array<VkWriteDescriptorSet,10> writes{};
        for(uint32_t j=0;j<10;++j){auto& w=writes[j];w.sType=VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;w.dstSet=sets[i];w.dstBinding=indices[j];w.descriptorCount=1;w.descriptorType=j==2?VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC:(j<3?VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER:(j<8?VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE:VK_DESCRIPTOR_TYPE_SAMPLER));if(j<3)w.pBufferInfo=&buffers[j];else w.pImageInfo=&images[j-3];}vkUpdateDescriptorSets(device,10,writes.data(),0,nullptr);
    }
    immediate([&](VkCommandBuffer cmd){
        for(uint32_t i=0;i<4;++i){bool depth_image=i==3;barrier(cmd,stock.images[i].handle,stock.initialized?VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL:VK_IMAGE_LAYOUT_UNDEFINED,depth_image?VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL:VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,depth_image?VK_IMAGE_ASPECT_DEPTH_BIT:VK_IMAGE_ASPECT_COLOR_BIT,stock.initialized?VK_PIPELINE_STAGE_2_TRANSFER_BIT:VK_PIPELINE_STAGE_2_NONE,stock.initialized?VK_ACCESS_2_TRANSFER_READ_BIT:0,depth_image?VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT:VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,depth_image?VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT|VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT:VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);}
        std::array<VkRenderingAttachmentInfo,3> colors{};for(uint32_t i=0;i<3;++i){auto& c=colors[i];c.sType=VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;c.imageView=stock.images[i].view;c.imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;c.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;c.storeOp=VK_ATTACHMENT_STORE_OP_STORE;}
        colors[0].clearValue.color.float32[3]=1.f;
        VkRenderingAttachmentInfo depth_info{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};depth_info.imageView=stock.images[3].view;depth_info.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;depth_info.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;depth_info.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
        VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.renderArea={{0,0},{width,height}};rendering.layerCount=1;rendering.colorAttachmentCount=3;rendering.pColorAttachments=colors.data();rendering.pDepthAttachment=&depth_info;vkCmdBeginRendering(cmd,&rendering);
        VkViewport viewport{0,float(height),float(width),-float(height),0,1};VkRect2D scissor{{0,0},{width,height}};vkCmdSetViewport(cmd,0,1,&viewport);vkCmdSetScissor(cmd,0,1,&scissor);vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,stock.pipeline);
        for(uint32_t i=0;i<count;++i){auto& mesh=stock.meshes.at(draws[i].mesh_id);uint32_t offset=static_cast<uint32_t>(i*stock.object_stride);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,stock.pipeline_layout,0,1,&sets[i],1,&offset);VkDeviceSize zero=0;vkCmdBindVertexBuffers(cmd,0,1,&mesh.vertices.handle,&zero);vkCmdBindIndexBuffer(cmd,mesh.indices.handle,0,VK_INDEX_TYPE_UINT32);vkCmdDrawIndexed(cmd,mesh.index_count,1,0,0,0);}
        vkCmdEndRendering(cmd);
        for(uint32_t i=0;i<4;++i){bool depth_image=i==3;barrier(cmd,stock.images[i].handle,depth_image?VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL:VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,depth_image?VK_IMAGE_ASPECT_DEPTH_BIT:VK_IMAGE_ASPECT_COLOR_BIT,depth_image?VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT:VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,depth_image?VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT:VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);}
    });stock.initialized=true;++stock.stats.frames;stock.stats.draws+=count;
}
void read_stock(uint32_t attachment,void* pixels,uint64_t bytes) {
    if(recording || !stock.initialized || attachment>3 || !pixels || bytes!=uint64_t(stock.stats.width)*stock.stats.height*4)throw std::runtime_error("invalid stock GBuffer readback");
    Buffer read=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true);
    try {immediate([&](VkCommandBuffer cmd){VkBufferImageCopy copy{};copy.imageSubresource={VkImageAspectFlags(attachment==3?VK_IMAGE_ASPECT_DEPTH_BIT:VK_IMAGE_ASPECT_COLOR_BIT),0,0,1};copy.imageExtent={stock.stats.width,stock.stats.height,1};vkCmdCopyImageToBuffer(cmd,stock.images[attachment].handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,read.handle,1,&copy);
        VkBufferMemoryBarrier2 b{VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2};b.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;b.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;b.dstStageMask=VK_PIPELINE_STAGE_2_HOST_BIT;b.dstAccessMask=VK_ACCESS_2_HOST_READ_BIT;b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;b.buffer=read.handle;b.size=VK_WHOLE_SIZE;VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.bufferMemoryBarrierCount=1;d.pBufferMemoryBarriers=&b;vkCmdPipelineBarrier2(cmd,&d);});check(vmaInvalidateAllocation(allocator,read.memory,0,bytes),"invalidate stock readback");std::memcpy(pixels,read.mapped,bytes);destroy_buffer(read);}catch(...){destroy_buffer(read);throw;}
}
