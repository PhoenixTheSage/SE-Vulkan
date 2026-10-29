void require_probe_format(){
    VkFormatProperties properties{};vkGetPhysicalDeviceFormatProperties(physical,stock_probe_format,&properties);
    constexpr VkFormatFeatureFlags required=VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT|VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT|VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BIT|VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BLEND_BIT|VK_FORMAT_FEATURE_BLIT_SRC_BIT|VK_FORMAT_FEATURE_BLIT_DST_BIT;
    if((properties.optimalTilingFeatures&required)!=required)throw std::runtime_error("stock R11G11B10 probes require sampled linear filtering, blended attachments and mip blits");
}
void init_probes(uint32_t id,const void* data,uint64_t bytes){
    if(recording || id>=4 || probes.stages[id].pipeline || !world.fullscreen)throw std::runtime_error("invalid probe stage/state");require_probe_format();wait(next_completion);SpvReflectShaderModule reflection{};auto shader=world_shader(data,bytes,VK_SHADER_STAGE_FRAGMENT_BIT,reflection);auto& stage=probes.stages[id];
    try{std::vector<VkDescriptorSetLayoutBinding> bindings;for(uint32_t i=0;i<reflection.descriptor_binding_count;++i){auto& b=reflection.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);bool valid=(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER && ((b.binding==0 && b.block.size==1280) || (b.binding==1 && b.block.size==(id==0?32u:16u)) || (id==1 && b.binding==2 && b.block.size==64))) || (type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE && b.binding>=128 && b.binding<=139 && !b.image.arrayed && !b.image.ms) || (type==VK_DESCRIPTOR_TYPE_SAMPLER && b.binding>=256 && b.binding<=259);if(!valid || b.set || b.count!=1)throw std::runtime_error("probe shader descriptor contract mismatch");bindings.push_back({b.binding,type,1,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr});stage.bindings.push_back({b.binding,type});}
        VkDescriptorSetLayoutCreateInfo l{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};l.bindingCount=uint32_t(bindings.size());l.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&l,nullptr,&stage.layout),"create probe postprocess layout");stage.pipeline_layout=world_pipeline_layout(stage.layout);VkFormat formats[6];std::fill(std::begin(formats),std::end(formats),stock_probe_format);stage.pipeline=world_graphics(world.fullscreen,"__vertex_shader",shader,"__pixel_shader",stage.pipeline_layout,{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,formats,id>=2?6:1,false,false,false,false,id==2?4:0);spvReflectDestroyShaderModule(&reflection);vkDestroyShaderModule(device,shader,nullptr);
    }catch(...){spvReflectDestroyShaderModule(&reflection);vkDestroyShaderModule(device,shader,nullptr);throw;}
}
void set_probes(const SevkProbeConfig& config,uint32_t skip_levels=0){
    if(recording || config.size!=sizeof(config) || config.abi_version!=SEVK_ABI_VERSION || config.flags&~31u || config.reserved || (config.flags&24u)==24u || config.resolution<16 || config.resolution>512 || (config.resolution&(config.resolution-1)) || skip_levels>=std::bit_width(config.resolution)-1 || !std::isfinite(config.blend) || config.blend<0 || config.blend>1 || (config.brdf && !textures.count(config.brdf)))throw std::runtime_error("invalid probe configuration");
    if(config.flags&1){if(!config.brdf)throw std::runtime_error("stock ambient BRDF LUT required for probes");for(uint32_t i=64;i<86;++i)if(!world.pipelines[i].handle)throw std::runtime_error("probe forward material unavailable");for(auto& s:probes.stages)if(!s.pipeline)throw std::runtime_error("probe processing stage unavailable");}
    for(float f:config.projections)if(!std::isfinite(f))throw std::runtime_error("nonfinite probe projection");probes.config=config;probes.skip_levels=skip_levels;if(config.flags&2){probes.ready=false;probes.face=probes.stage=0;probes.stats.cycles=0;}if(config.flags&16){probes.face=probes.stage=0;}
}
void probe_cube_destroy(ProbeCube& cube){for(auto v:cube.views)vkDestroyImageView(device,v,nullptr);destroy_image(cube.image);cube={};}
void probe_cube_create(ProbeCube& cube){cube.image=make_image(probes.resolution,probes.resolution,stock_probe_format,VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT,VK_IMAGE_ASPECT_COLOR_BIT,probes.mips,6);for(uint32_t f=0;f<6;++f)for(uint32_t m=0;m<probes.mips;++m){VkImageViewCreateInfo v{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};v.image=cube.image.handle;v.viewType=VK_IMAGE_VIEW_TYPE_2D;v.format=stock_probe_format;v.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,m,1,f,1};VkImageView view{};check(vkCreateImageView(device,&v,nullptr,&view),"create probe face/mip view");cube.views.push_back(view);}}
void probe_transition(VkCommandBuffer cmd,ProbeCube& cube,VkImageLayout old_layout,VkImageLayout layout){barrier(cmd,cube.image.handle,old_layout,layout,VK_IMAGE_ASPECT_COLOR_BIT,old_layout?VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT:VK_PIPELINE_STAGE_2_NONE,old_layout?VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT:0,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT,probes.mips,6);}
void probe_targets(VkCommandBuffer cmd){
    // Stock generated probes end at 2x2. AmbientSpecular uses the view's
    // level count to choose roughness LOD, so an extra 1x1 changes PBR sampling.
    // plan_probes already applied the reset a resize implies and advanced the schedule from it: keep that state.
    if(probes.resolution!=probes.config.resolution){wait(next_completion);bool ready=probes.ready;uint32_t face=probes.face,stage=probes.stage;uint64_t cycles=probes.stats.cycles;cleanup_probe_targets();probes.ready=ready;probes.face=face;probes.stage=stage;probes.stats.cycles=cycles;probes.resolution=probes.config.resolution;probes.mips=uint32_t(std::log2(probes.resolution));for(uint32_t i=0;i<2;++i){probe_cube_create(probes.original[i]);probe_cube_create(probes.filtered[i]);probe_cube_create(probes.final[i]);}for(auto& depth:probes.depth)depth=make_image(probes.resolution,probes.resolution,VK_FORMAT_D32_SFLOAT,VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT,VK_IMAGE_ASPECT_DEPTH_BIT);}
    if(!probes.initialized){VkClearColorValue clear{};VkImageSubresourceRange range{VK_IMAGE_ASPECT_COLOR_BIT,0,probes.mips,0,6};for(uint32_t i=0;i<2;++i)for(auto* cube:{&probes.original[i],&probes.filtered[i],&probes.final[i]}){probe_transition(cmd,*cube,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);vkCmdClearColorImage(cmd,cube->image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,&clear,1,&range);probe_transition(cmd,*cube,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);}for(auto& depth:probes.depth)barrier(cmd,depth.handle,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_NONE,0,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);probes.initialized=true;}
}
VkDescriptorSet probe_processing_set(WorldSlot& slot,uint32_t id,uint32_t offset,const std::array<VkImageView,12>& images){
    auto& s=probes.stages[id];auto set=world_set(slot,s.layout);uint32_t owner=1280+id;
    for(auto [binding,type]:s.bindings){
        auto catalog=std::find_if(s.catalog.begin(),s.catalog.end(),[&](auto r){return r.binding==binding;});auto row=stage_binding_row(owner);
        if(catalog!=s.catalog.end()&&(!catalog->owner_input||(row&&(row->requested_mask&(1u<<(binding-128)))))){write_stage_catalog(set,owner,*catalog);continue;}
        if(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER){
            if(binding==6)write_stage_extras(set,owner);
            else if(binding==0)world_write_buffer(set,0,probes.frame[slot_index][probes.work_face],1280,type);
            else if(id==1&&binding==2&&s.probe_full_view)world_write_buffer(set,2,probes.atmosphere_view[slot_index][probes.work_face],144,type);
            else {VkDescriptorBufferInfo b{probes.constants[slot_index][probes.work_face].handle,id==1?0u:offset,id==1?64u:id==0?32u:16u};VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstSet=set;w.dstBinding=binding;w.descriptorCount=1;w.descriptorType=type;w.pBufferInfo=&b;vkUpdateDescriptorSets(device,1,&w,0,nullptr);}
        }else if(type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE){if(!images[binding-128])throw std::runtime_error("missing probe processing input");world_write_image(set,binding,images[binding-128]);}
        else world_write_image(set,binding,{},environment_generation_sampler(binding),true);
    }return set;
}
void probe_fullscreen(VkCommandBuffer cmd,WorldSlot& slot,uint32_t id,uint32_t offset,uint32_t size,const VkImageView* views,uint32_t count,const std::array<VkImageView,12>& inputs,bool load=false){
    VkRenderingAttachmentInfo a[6]{};for(uint32_t i=0;i<count;++i){a[i].sType=VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;a[i].imageView=views[i];a[i].imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;a[i].loadOp=load?VK_ATTACHMENT_LOAD_OP_LOAD:VK_ATTACHMENT_LOAD_OP_DONT_CARE;a[i].storeOp=VK_ATTACHMENT_STORE_OP_STORE;}VkRenderingInfo r{VK_STRUCTURE_TYPE_RENDERING_INFO};r.renderArea={{0,0},{size,size}};r.layerCount=1;r.colorAttachmentCount=count;r.pColorAttachments=a;vkCmdBeginRendering(cmd,&r);auto& s=probes.stages[id];auto set=probe_processing_set(slot,id,offset,inputs);vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,s.pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,s.pipeline_layout,0,1,&set,0,nullptr);if(id==2){float strength=probes.ready?probes.config.blend:1;float blend[]{strength,strength,strength,strength};vkCmdSetBlendConstants(cmd,blend);}VkDeviceSize zero=0;vkCmdBindVertexBuffers(cmd,0,1,&world.quad.handle,&zero);world_viewport(cmd,size,size);post_note_execution(1280+id);vkCmdDraw(cmd,3,1,0,0);vkCmdEndRendering(cmd);
}
void probe_mips(VkCommandBuffer cmd,ProbeCube& cube){
    probe_transition(cmd,cube,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);for(uint32_t m=1;m<probes.mips;++m){VkImageMemoryBarrier2 b{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2};b.srcStageMask=b.dstStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;b.srcAccessMask=VK_ACCESS_2_TRANSFER_READ_BIT;b.dstAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;b.oldLayout=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;b.newLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;b.image=cube.image.handle;b.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,m,1,0,6};VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.imageMemoryBarrierCount=1;d.pImageMemoryBarriers=&b;vkCmdPipelineBarrier2(cmd,&d);VkImageBlit blit{};blit.srcSubresource={VK_IMAGE_ASPECT_COLOR_BIT,m-1,0,6};blit.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,m,0,6};blit.srcOffsets[1]={int32_t(probes.resolution>>(m-1)),int32_t(probes.resolution>>(m-1)),1};blit.dstOffsets[1]={int32_t(probes.resolution>>m),int32_t(probes.resolution>>m),1};vkCmdBlitImage(cmd,cube.image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,cube.image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&blit,VK_FILTER_LINEAR);std::swap(b.oldLayout,b.newLayout);b.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;b.dstAccessMask=VK_ACCESS_2_TRANSFER_READ_BIT;vkCmdPipelineBarrier2(cmd,&d);}probe_transition(cmd,cube,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
}
void prepare_probe_face(WorldSlot& slot,uint32_t face){
    probes.work_face=face;
auto& frame=probes.frame[slot_index][probes.work_face];auto& projection=probes.projection[slot_index][probes.work_face];auto& constants=probes.constants[slot_index][probes.work_face];world_buffer(frame,1280,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);world_buffer(projection,64,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);world_buffer(constants,world_align(64)+6*probes.mips*world_align(32),VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);std::memcpy(frame.mapped,probes.config.frames+face*1280,1280);std::memcpy(projection.mapped,probes.config.projections+face*16,64);auto& forward=probes.forward_projection[slot_index][probes.work_face];world_buffer(forward,64,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);std::memcpy(forward.mapped,probes.config.frames+face*1280+64,64);check(vmaFlushAllocation(allocator,forward.memory,0,64),"flush pure probe projection");
    // ForwardPostprocess transposes viewMatrix in HLSL to reconstruct world
    // rays. DXC uses row-major constants, so retain the actual view here.
    // The extra CPU transpose exchanged the two nonsymmetric X-face views.
    auto* view=reinterpret_cast<const float*>(probes.config.frames+face*1280);std::memcpy(constants.mapped,view,64);
    // Populate the extended DX11 b2 only when a replacement reads beyond view64.
    if(probes.stages[1].probe_full_view){
    auto& view_constants=probes.atmosphere_view[slot_index][face];world_buffer(view_constants,144,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);auto* values=static_cast<uint8_t*>(view_constants.mapped);std::memcpy(values,view,64);std::memcpy(values+64,probes.config.frames+face*1280+64,64);std::memcpy(values+128,probes.sun_direction,12);float size=float(probes.resolution);std::memcpy(values+140,&size,4);check(vmaFlushAllocation(allocator,view_constants.memory,0,144),"flush probe forward view constants");
    }
    for(uint32_t f=0;f<6;++f)for(uint32_t m=1;m<probes.mips;++m){uint32_t data[]{16,probes.resolution,probes.resolution>>m,f,std::bit_cast<uint32_t>(m>probes.skip_levels && probes.mips>probes.skip_levels+1?1.f-float(m-probes.skip_levels)/float(probes.mips-probes.skip_levels-1):1.f),0,0,0};std::memcpy(static_cast<uint8_t*>(constants.mapped)+world_align(64)+(f*probes.mips+m)*world_align(32),data,32);}for(auto* b:{&frame,&projection,&constants})check(vmaFlushAllocation(allocator,b->memory,0,VK_WHOLE_SIZE),"flush probe frame/constants");
}
void capture_probe_face(VkCommandBuffer cmd,WorldSlot& slot,uint32_t face,const std::vector<uint32_t>& offsets,const std::vector<uint32_t>& instance_offsets){
    prepare_probe_face(slot,face);slot.probe_materials.clear();
    auto& frame=probes.frame[slot_index][probes.work_face];auto& projection=probes.projection[slot_index][probes.work_face];
    frame_mark("probe geometry");for(auto& cube:probes.original)probe_transition(cmd,cube,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL);barrier(cmd,probes.depth[probes.work_face].handle,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);
    VkRenderingAttachmentInfo colors[2]{};for(uint32_t i=0;i<2;++i){colors[i].sType=VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;colors[i].imageView=probes.original[i].views[face*probes.mips];colors[i].imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;colors[i].loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;colors[i].storeOp=VK_ATTACHMENT_STORE_OP_STORE;}VkRenderingAttachmentInfo d{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};d.imageView=probes.depth[probes.work_face].view;d.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;d.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;d.storeOp=VK_ATTACHMENT_STORE_OP_STORE;VkRenderingInfo r{VK_STRUCTURE_TYPE_RENDERING_INFO};r.renderArea={{0,0},{probes.resolution,probes.resolution}};r.layerCount=1;r.colorAttachmentCount=2;r.pColorAttachments=colors;r.pDepthAttachment=&d;vkCmdBeginRendering(cmd,&r);world_viewport(cmd,probes.resolution,probes.resolution);
    struct ProbeBind {VkPipeline pipe{};VkDescriptorSet set{};uint32_t object=~0u,streams{},instance{};VkBuffer vertex{},index{};VkDeviceSize vertex_at{},index_at{};WorldRasterCache raster{};} bound{};
    for(uint32_t i:world.current().order){const auto& draw=world.current().draws[i];if(draw.packet.flags&(8192|16384))continue;const auto& p=world.pipelines[64+draw.packet.pipeline];auto& mesh=*draw.geometry;auto set=probe_geometry_set(slot,draw,frame,projection);uint32_t dynamic[]{0,offsets[i]};
        if(bound.pipe!=p.handle){vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,p.handle);bound.pipe=p.handle;if(world_voxel(draw.packet.pipeline))bound.raster.valid=false;}
        if(!world_voxel(draw.packet.pipeline))world_model_raster(cmd,draw.packet.pipeline,draw.packet.flags,false,VK_CULL_MODE_NONE,false,&bound.raster);
        if(bound.set!=set || bound.object!=offsets[i]){vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,probes.material_pipeline_layout,0,1,&set,2,dynamic);bound.set=set;bound.object=offsets[i];}
        uint32_t streams=p.type>=2?2u:1u,instance=streams>1?instance_offsets[i]:0;
        if(bound.vertex!=mesh.vertices.handle || bound.index!=mesh.indices.handle || bound.streams!=streams || bound.instance!=instance || bound.vertex_at!=mesh.vertex_offset || bound.index_at!=mesh.index_offset){VkBuffer buffers[]{mesh.vertices.handle,slot.instances.handle};VkDeviceSize positions[]{mesh.vertex_offset,instance};vkCmdBindVertexBuffers(cmd,0,streams,buffers,positions);vkCmdBindIndexBuffer(cmd,mesh.indices.handle,mesh.index_offset,VK_INDEX_TYPE_UINT32);bound.vertex=mesh.vertices.handle;bound.index=mesh.indices.handle;bound.streams=streams;bound.instance=instance;bound.vertex_at=mesh.vertex_offset;bound.index_at=mesh.index_offset;}
        vkCmdDrawIndexed(cmd,mesh.index_count,draw.packet.instance_count,0,0,0);geometry_note_execution(64+draw.packet.pipeline);++probes.stats.geometry_draws;++performance.probe_commands;
    }
    uint32_t native_commands=record_pass_view(cmd,slot,9+face,false,&frame,&projection);probes.stats.geometry_draws+=native_commands;performance.probe_commands+=native_commands;
    vkCmdEndRendering(cmd);barrier(cmd,probes.depth[probes.work_face].handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    for(auto& cube:probes.original)probe_transition(cmd,cube,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
    gather_shadow_usage(cmd,slot,1+face,frame,probes.depth[probes.work_face],probes.resolution,probes.resolution);
    ++probes.stats.faces; // plan_probes advanced probes.face
}
void postprocess_probe_face(VkCommandBuffer cmd,WorldSlot& slot,uint32_t face){
    prepare_probe_face(slot,face);
    for(auto& cube:probes.original)probe_transition(cmd,cube,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL);
    frame_mark("probe sky");std::array<VkImageView,12> sky{};sky[0]=probes.depth[probes.work_face].view;uint64_t source=world.probe_skybox?world.probe_skybox:world.skybox;sky[10]=source && world.cubes.count(source)?world.cubes.at(source).view:world.black.view;for(auto& cube:probes.original){auto view=cube.views[face*probes.mips];probe_fullscreen(cmd,slot,1,0,probes.resolution,&view,1,sky,true);guest_probe_celestial(cmd,face,view);probe_transition(cmd,cube,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);}frame_mark("probe atmosphere");render_probe_atmosphere(cmd,slot,face);
}
void copy_probe_levels(VkCommandBuffer cmd,ProbeCube& source,ProbeCube& destination,uint32_t mip_count,uint32_t layer,uint32_t layers){
    probe_transition(cmd,source,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);
    probe_transition(cmd,destination,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);
    std::vector<VkImageCopy> copies;for(uint32_t mip=0;mip<mip_count;++mip){VkImageCopy copy{};copy.srcSubresource=copy.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,mip,layer,layers};copy.extent={probes.resolution>>mip,probes.resolution>>mip,1};copies.push_back(copy);}
    vkCmdCopyImage(cmd,source.image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,destination.image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,uint32_t(copies.size()),copies.data());
    probe_transition(cmd,source,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
    probe_transition(cmd,destination,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
}
void build_probe_mips(VkCommandBuffer cmd,uint32_t kind){probe_mips(cmd,probes.original[kind]);}
void prefilter_probe_face(VkCommandBuffer cmd,WorldSlot& slot,uint32_t face){
    prepare_probe_face(slot,face);
    for(uint32_t kind=0;kind<2;++kind){auto& filtered=probes.filtered[kind];copy_probe_levels(cmd,probes.original[kind],filtered,probes.skip_levels+1,face,1);probe_transition(cmd,filtered,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL);
        std::array<VkImageView,12> input{};input[1]=probes.original[kind].image.view;
        for(uint32_t mip=probes.skip_levels+1;mip<probes.mips;++mip){auto view=filtered.views[face*probes.mips+mip];probe_fullscreen(cmd,slot,0,uint32_t(world_align(64)+(face*probes.mips+mip)*world_align(32)),probes.resolution>>mip,&view,1,input);++probes.stats.prefilter_passes;}
        probe_transition(cmd,filtered,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
    }
}
// The probe work of a frame is planned before it is recorded: plan_probes advances the probe state (face,
// stage, cycles, blend kind, ready) and lists the operations, and render_probes records them. Early release
// plans when recording starts, so the state the render thread reads during the overlap is already final;
// probes.ready still turns on after the blend is recorded, since this frame's descriptors read it.
struct ProbeOp {uint8_t kind;uint32_t arg;}; // 0 capture face, 1 postprocess face, 2 build mips, 3 prefilter face
std::vector<ProbeOp> probe_ops;bool probe_planned{},probe_blend_prepare{},probe_blend{},probe_set_ready{};uint32_t probe_blend_first{},probe_blend_last{};
SevkProbeStats probe_post{sizeof(SevkProbeStats),SEVK_ABI_VERSION};uint32_t probe_post_stage{};
void plan_probes(){
    probe_ops.clear();probe_planned=true;probe_blend_prepare=probe_blend=probe_set_ready=false;
    if(probes.config.flags&1){bool stock_timed=(probes.config.flags&8)!=0;
        // probe_targets recreates resized targets after this plan; schedule from the state that reset leaves.
        if(probes.resolution!=probes.config.resolution){probes.ready=false;probes.face=probes.stage=0;probes.stats.cycles=0;}
        if(!(probes.config.flags&4)){
            if(!(probes.config.flags&24)){
                uint32_t face=probes.face;probe_ops.push_back({0,face});probe_ops.push_back({1,face});probes.face=(face+1)%6;
                if(probes.face==0){probe_ops.push_back({2,1});probe_ops.push_back({2,0});for(uint32_t f=0;f<6;++f)probe_ops.push_back({3,f});++probes.stats.cycles;}
            }else{
                bool immediate=!probes.ready||(probes.config.flags&16);
                // Stock seeds work once when entering timed mode, not on every
                // refresh. Startup already populated filtered and final equally.
                // Preserve completed source faces while publication is blending;
                // reseeding from that lagging publication reintroduces old views.
                // Stock finalization postprocesses face zero in the same frame as
                // the sixth capture, making a timed refresh nineteen frames.
                uint32_t steps=immediate?20u:probes.stage==5?2u:1u;
                for(uint32_t n=0;n<steps;++n){uint32_t stage=probes.stage;
                    if(stage<6){probe_ops.push_back({0,stage});probes.face=(stage+1)%6;}
                    else if(stage<12)probe_ops.push_back({1,stage-6});
                    else if(stage==12)probe_ops.push_back({2,1});
                    else if(stage==13)probe_ops.push_back({2,0});
                    else probe_ops.push_back({3,stage-14});
                    if(++probes.stage==20){probes.stage=0;probes.face=0;++probes.stats.cycles;break;}
                }
            }
        }
        // Stock retains completed work, then blends one alternating
        // cube every timed frame as each fully processed face becomes available.
        if(probes.stage==0 || stock_timed){probe_blend_prepare=true;
            if(probes.stats.cycles){probe_blend=true;bool alternating=stock_timed && probes.ready;probe_blend_first=alternating?probes.blend_kind:0;probe_blend_last=alternating?probe_blend_first+1:2;if(alternating)probes.blend_kind^=1;probe_set_ready=true;}}
    }
    probe_post=probes.stats;probe_post.size=sizeof(SevkProbeStats);probe_post.abi_version=SEVK_ABI_VERSION;probe_post.ready=probes.ready||probe_set_ready;probe_post.next_face=probes.face;probe_post_stage=probes.stage;
}
void render_probes(VkCommandBuffer cmd,WorldSlot& slot,const std::vector<uint32_t>& offsets,const std::vector<uint32_t>& instance_offsets){
    critical_profile::Scope critical_probes(critical_profile::ProbeRecord,true);
    cpu_lap("probe plan");if(!probe_planned)plan_probes();probe_planned=false;
    if(!(probes.config.flags&1))return;cpu_lap("probe targets");probe_targets(cmd);
    for(const auto& op:probe_ops){record_count(op.kind==0?"probe captures":op.kind==1?"probe postprocess ops":op.kind==2?"probe mip ops":"probe prefilter ops",1.0);
        if(op.kind==0){cpu_lap("probe face prepare");capture_probe_face(cmd,slot,op.arg,offsets,instance_offsets);}
        else if(op.kind==1){cpu_lap("probe postprocess");postprocess_probe_face(cmd,slot,op.arg);}
        else if(op.kind==2){cpu_lap("probe mips");build_probe_mips(cmd,op.arg);}
        else{cpu_lap("probe prefilter");prefilter_probe_face(cmd,slot,op.arg);}
    }
    cpu_lap("probe blend prepare");if(probe_blend_prepare){prepare_probe_face(slot,0);
    if(probe_blend){frame_mark("probe blend");uint32_t first=probe_blend_first,last=probe_blend_last;for(uint32_t k=first;k<last;++k){probe_transition(cmd,probes.final[k],VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL);for(uint32_t m=0;m<probes.mips;++m){VkImageView targets[6];std::array<VkImageView,12> input{};for(uint32_t f=0;f<6;++f){targets[f]=probes.final[k].views[f*probes.mips+m];input[f]=probes.filtered[k].views[f*probes.mips+m];}probe_fullscreen(cmd,slot,2,0,probes.resolution>>m,targets,6,input,true);++probes.stats.blend_passes;}probe_transition(cmd,probes.final[k],VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);}probes.ready=true;}
    }
}
void render_probe_atmosphere(VkCommandBuffer cmd,WorldSlot& slot,uint32_t face){
    if(!probes.atmosphere_id || probes.atmosphere_intensity<=0)return;
    auto found=std::find_if(environment.draws.begin(),environment.draws.end(),[&](const auto& d){return !d.kind && d.id==probes.atmosphere_id;});if(found==environment.draws.end())return;
    auto& atmosphere=probes.atmosphere[slot_index][probes.work_face];auto& view_constants=probes.atmosphere_view[slot_index][probes.work_face];world_buffer(atmosphere,144,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);world_buffer(view_constants,144,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);std::memcpy(atmosphere.mapped,found->constants,144);auto* a=static_cast<float*>(atmosphere.mapped);a[16]=probes.atmosphere_intensity;a[18]=probes.atmosphere_desaturation;
    const float* view=reinterpret_cast<const float*>(probes.config.frames+face*1280);for(uint32_t i=0;i<3;++i)a[i]+=view[12]*view[i*4]+view[13]*view[i*4+1]+view[14]*view[i*4+2];
    auto* values=static_cast<uint8_t*>(view_constants.mapped);std::memcpy(values,view,64);std::memcpy(values+64,probes.config.frames+face*1280+64,64);std::memcpy(values+128,probes.sun_direction,12);float size=float(probes.resolution);std::memcpy(values+140,&size,4);for(auto* b:{&atmosphere,&view_constants})check(vmaFlushAllocation(allocator,b->memory,0,144),"flush probe atmosphere constants");
    auto* generation=post.atmosphere[2].generation?&post.atmosphere[2]:nullptr;
    auto set=world_set(slot,generation?generation->layout:environment.layouts[3]);
    if(!generation){world_write_buffer(set,0,probes.frame[slot_index][probes.work_face],1280,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,1,atmosphere,144,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,2,view_constants,144,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_image(set,128,probes.depth[probes.work_face].view);world_write_image(set,133,environment.luts.at(found->id).image.view);world_write_image(set,258,{},standard_sampler(258),true);}
    else for(auto b:generation->bindings){
        auto catalog=std::find_if(generation->catalog.begin(),generation->catalog.end(),[&](auto r){return r.binding==b.first;});
        if(catalog!=generation->catalog.end()&&(!catalog->owner_input||(stage_binding_row(514)&&(stage_binding_row(514)->requested_mask&(1u<<(b.first-128)))))){write_stage_catalog(set,514,*catalog);continue;}
        if(b.first==6&&b.second==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER)write_stage_extras(set,514);
        else if(b.second==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER){if(b.first==0)world_write_buffer(set,0,probes.frame[slot_index][probes.work_face],1280,b.second);else world_write_buffer(set,b.first,b.first==1?atmosphere:view_constants,144,b.second);}
        else if(b.second==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE)world_write_image(set,b.first,b.first==128?probes.depth[probes.work_face].view:environment.luts.at(found->id).image.view);
        else if(b.second==VK_DESCRIPTOR_TYPE_SAMPLER)world_write_image(set,b.first,{},environment_generation_sampler(b.first),true);
    }
    VkRenderingAttachmentInfo colors[2]{};for(uint32_t i=0;i<2;++i){probe_transition(cmd,probes.original[i],VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL);colors[i].sType=VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;colors[i].imageView=probes.original[i].views[face*probes.mips];colors[i].imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;colors[i].loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;colors[i].storeOp=VK_ATTACHMENT_STORE_OP_STORE;}VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.renderArea={{0,0},{probes.resolution,probes.resolution}};rendering.layerCount=1;rendering.colorAttachmentCount=2;rendering.pColorAttachments=colors;vkCmdBeginRendering(cmd,&rendering);world_viewport(cmd,probes.resolution,probes.resolution);vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,environment.pipelines[3]);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,(generation?generation->pipeline_layout:environment.pipeline_layouts[3]),0,1,&set,0,nullptr);VkDeviceSize zero=0;vkCmdBindVertexBuffers(cmd,0,1,&world.quad.handle,&zero);post_note_execution(514);vkCmdDraw(cmd,3,1,0,0);vkCmdEndRendering(cmd);for(auto& cube:probes.original)probe_transition(cmd,cube,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);++probes.stats.atmosphere_passes;
}
void cleanup_probe_targets(){for(uint32_t i=0;i<2;++i){probe_cube_destroy(probes.original[i]);probe_cube_destroy(probes.filtered[i]);probe_cube_destroy(probes.final[i]);}for(auto& depth:probes.depth)destroy_image(depth);probes.initialized=probes.ready=false;probes.face=probes.stage=probes.resolution=probes.mips=0;probes.stats.cycles=0;}
void readback_probe(uint32_t kind,uint32_t face,uint32_t mip,void* pixels,uint64_t capacity){
    if(recording || batching_uploads || kind>5 || face>=6 || mip>=probes.mips || !probes.initialized || !pixels)throw std::runtime_error("invalid explicit probe readback");uint32_t size=probes.resolution>>mip;uint64_t bytes=uint64_t(size)*size*4;if(capacity<bytes*2)throw std::runtime_error("probe capture buffer too small");auto& cube=kind<2?probes.original[kind]:kind<4?probes.filtered[kind-2]:probes.final[kind-4];Buffer capture{};
    try{wait(next_completion);capture=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true);immediate([&](VkCommandBuffer cmd){probe_transition(cmd,cube,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,mip,face,1};copy.imageExtent={size,size,1};vkCmdCopyImageToBuffer(cmd,cube.image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,capture.handle,1,&copy);probe_transition(cmd,cube,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);});check(vmaInvalidateAllocation(allocator,capture.memory,0,bytes),"invalidate explicit probe capture");// Keep the diagnostic ABI's RGBA16F result. Packed unsigned R11/G11/B10
        // shares float16's exponent bias; expanding mantissa bits is exact,
        // including subnormals/Inf/NaN. Missing alpha samples as one.
        for(uint64_t i=0;i<uint64_t(size)*size;++i){uint32_t packed;std::memcpy(&packed,static_cast<const uint8_t*>(capture.mapped)+i*4,4);uint16_t rgba[]{uint16_t((packed&0x7ffu)<<4),uint16_t(((packed>>11)&0x7ffu)<<4),uint16_t(((packed>>22)&0x3ffu)<<5),0x3c00u};std::memcpy(static_cast<uint8_t*>(pixels)+i*8,rgba,8);}destroy_buffer(capture);}catch(...){destroy_buffer(capture);throw;}
}
void cleanup_probes(){cleanup_probe_targets();for(auto& s:probes.stages){if(s.pipeline)vkDestroyPipeline(device,s.pipeline,nullptr);if(s.pipeline_layout)vkDestroyPipelineLayout(device,s.pipeline_layout,nullptr);if(s.layout)vkDestroyDescriptorSetLayout(device,s.layout,nullptr);}for(uint32_t i=0;i<2;++i){for(auto& buffer:probes.atmosphere[i])destroy_buffer(buffer);for(auto& buffer:probes.atmosphere_view[i])destroy_buffer(buffer);for(auto& buffer:probes.frame[i])destroy_buffer(buffer);for(auto& buffer:probes.projection[i])destroy_buffer(buffer);for(auto& buffer:probes.forward_projection[i])destroy_buffer(buffer);for(auto& buffer:probes.constants[i])destroy_buffer(buffer);}if(probes.material_pipeline_layout)vkDestroyPipelineLayout(device,probes.material_pipeline_layout,nullptr);if(probes.material_layout)vkDestroyDescriptorSetLayout(device,probes.material_layout,nullptr);probes={};}
