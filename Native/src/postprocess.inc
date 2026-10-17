// Stock postprocess modules use reflected layouts, owned images and copied CPU
// constants. Resources stay on the graphics queue; status never waits for them.
VkFormat post_format(uint32_t id){if(id>=126)return VK_FORMAT_R8_UNORM;if(id>=62)return VK_FORMAT_R16G16B16A16_SFLOAT;if(id==33)return VK_FORMAT_R32_SFLOAT;if(id==34)return VK_FORMAT_R16_SFLOAT;if(id==35)return VK_FORMAT_R8G8B8A8_UNORM;if(id==53 || (id>=54 && id%2==0))return VK_FORMAT_R16G16_SFLOAT;if(id>=36)return VK_FORMAT_R8_UNORM;return id==0 || id==2?VK_FORMAT_R32G32_SFLOAT:(id==14 || id>=31?VK_FORMAT_R8_UNORM:(id==13 || (id>=9 && id<=12) || (id>=15 && id<=30)?VK_FORMAT_R8G8B8A8_UNORM:VK_FORMAT_R16G16B16A16_SFLOAT));}
void init_postprocess(uint32_t id,const void* data,uint64_t bytes){
    if(recording || id>=128 || post.stages[id].pipeline || !world.fullscreen)throw std::runtime_error("invalid stock postprocess stage/state");wait(next_completion);
    bool compute=id==3 || id==4 || (id>=9 && id<=12) || (id>=15 && id<=30);
    SpvReflectShaderModule reflection{};auto shader=world_shader(data,bytes,compute?VK_SHADER_STAGE_COMPUTE_BIT:VK_SHADER_STAGE_FRAGMENT_BIT,reflection);auto& stage=post.stages[id];
    try{std::vector<VkDescriptorSetLayoutBinding> bindings;
        for(uint32_t i=0;i<reflection.descriptor_binding_count;++i){auto& b=reflection.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
            bool valid=(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER && ((b.binding==0 && b.block.size==(id>=33 && id<62?208u:1280u)) || (b.binding==1 && b.block.size==(id==14?288u:id>=33?32u:64u)) || (b.binding==5 && b.block.size==16))) ||
                (type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE && b.binding>=128 && b.binding<=133 && b.image.dim==SpvDim2D && b.image.arrayed==(id>=36 && id<=53 && b.binding==128) && !b.image.ms) ||
                (type==VK_DESCRIPTOR_TYPE_SAMPLER && b.binding>=256 && b.binding<=259) ||
                (type==VK_DESCRIPTOR_TYPE_STORAGE_IMAGE && b.binding==384 && compute && id!=3 && id!=4) ||
                (type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER && ((b.binding==384 && (id==3 || id==4)) || (b.binding==128 && id==2)));
            if(!valid || b.set || b.count!=1)throw std::runtime_error("postprocess descriptor mismatch stage "+std::to_string(id)+" binding "+std::to_string(b.binding)+" size "+std::to_string(b.block.size));
            bindings.push_back({b.binding,type,1,VkShaderStageFlags(compute?VK_SHADER_STAGE_COMPUTE_BIT:VK_SHADER_STAGE_FRAGMENT_BIT),nullptr});stage.bindings.push_back({b.binding,type});}
        if(reflection.push_constant_block_count)throw std::runtime_error("unexpected postprocess push constants");
        VkDescriptorSetLayoutCreateInfo l{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};l.bindingCount=uint32_t(bindings.size());l.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&l,nullptr,&stage.layout),"create stock postprocess layout");stage.pipeline_layout=world_pipeline_layout(stage.layout);stage.compute=compute;
        if(compute){VkComputePipelineCreateInfo p{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};p.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,shader,"__compute_shader",nullptr};p.layout=stage.pipeline_layout;check(vkCreateComputePipelines(device,pipeline_cache,1,&p,nullptr,&stage.pipeline),"create stock postprocess compute");}
        else{std::array<VkFormat,8> formats{};formats.fill(post_format(id));stage.pipeline=world_graphics(world.fullscreen,"__vertex_shader",shader,"__pixel_shader",stage.pipeline_layout,{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,formats.data(),id==34?8:1,false);}
        spvReflectDestroyShaderModule(&reflection);vkDestroyShaderModule(device,shader,nullptr);
    }catch(...){spvReflectDestroyShaderModule(&reflection);vkDestroyShaderModule(device,shader,nullptr);throw;}
}
void set_postprocess(const SevkPostprocessConfig& config){
    static_assert(sizeof(SevkPostprocessConfig)==384);if(recording || config.size!=sizeof(config) || config.abi_version!=SEVK_ABI_VERSION || config.flags&~4095u || config.reserved || config.bloom_size<1 || config.bloom_size>8 || config.chromatic_iterations<1 || config.chromatic_iterations>16 || (config.dirt && !textures.count(config.dirt)))throw std::runtime_error("invalid postprocess configuration");
    for(uint32_t i=0;i<352/4;++i){float f;std::memcpy(&f,reinterpret_cast<const uint8_t*>(&config)+32+i*4,4);if(!std::isfinite(f))throw std::runtime_error("nonfinite postprocess constant");}
    if(config.flags&1)for(auto& s:post.stages)if(!s.pipeline)throw std::runtime_error("stock postprocess stages missing");post.config=config;
}
void post_transition(VkCommandBuffer cmd,PostImage& image,VkImageLayout layout){
    barrier(cmd,image.image.handle,image.layout,layout,VK_IMAGE_ASPECT_COLOR_BIT,image.layout?VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT:VK_PIPELINE_STAGE_2_NONE,image.layout?VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT:0,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT);image.layout=layout;
}
void post_image(PostImage& image,uint32_t width,uint32_t height,VkFormat format){
    if(image.image.handle && image.width==width && image.height==height)return;
    retire_image(image.image);image={};image.width=width;image.height=height;
    VkImageUsageFlags usage=VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    if(format==VK_FORMAT_R8G8B8A8_UNORM)usage|=VK_IMAGE_USAGE_STORAGE_BIT;
    image.image=make_image(width,height,format,usage,VK_IMAGE_ASPECT_COLOR_BIT);
}
void post_clear(VkCommandBuffer cmd,PostImage& image,float value){post_transition(cmd,image,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);VkClearColorValue color{{value,value,value,value}};VkImageSubresourceRange range{VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};vkCmdClearColorImage(cmd,image.image.handle,image.layout,&color,1,&range);post_transition(cmd,image,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);}
VkDescriptorSet post_set(WorldSlot& slot,uint32_t id,PostImage& dst,const std::array<VkImageView,6>& images){
    auto& stage=post.stages[id];auto set=world_set(slot,stage.layout);
    for(auto [binding,type]:stage.bindings){
        if(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER){if(binding==0)world_write_buffer(set,binding,slot.frame,1280,type);
            else{VkDescriptorBufferInfo b{(id>=62 && id<126)?highlights.constants[slot_index].handle:post.constants[slot_index].handle,binding==5?((id>=62 && id<126)?0:world_align(64)+world_align(288)):(id==14?world_align(64):0),binding==5?16u:(id==14?288u:64u)};VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstSet=set;w.dstBinding=binding;w.descriptorCount=1;w.descriptorType=type;w.pBufferInfo=&b;vkUpdateDescriptorSets(device,1,&w,0,nullptr);}}
        else if(type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE){auto view=images[binding-128];if(!view)throw std::runtime_error("missing stock postprocess input");world_write_image(set,binding,view);}
        else if(type==VK_DESCRIPTOR_TYPE_SAMPLER)world_write_image(set,binding,{},binding==257?post.point:sampler,true);
        else if(type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER)world_write_buffer(set,binding,post.histogram,256,type);
        else if(type==VK_DESCRIPTOR_TYPE_STORAGE_IMAGE){VkDescriptorImageInfo image{{},dst.image.view,VK_IMAGE_LAYOUT_GENERAL};VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstSet=set;w.dstBinding=binding;w.descriptorCount=1;w.descriptorType=type;w.pImageInfo=&image;vkUpdateDescriptorSets(device,1,&w,0,nullptr);}}
    return set;
}
void post_pass(VkCommandBuffer cmd,WorldSlot& slot,uint32_t id,PostImage& dst,std::array<VkImageView,6> images={}){
    auto& stage=post.stages[id];post_transition(cmd,dst,stage.compute?VK_IMAGE_LAYOUT_GENERAL:VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL);auto set=post_set(slot,id,dst,images);
    if(stage.compute){vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,stage.pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,stage.pipeline_layout,0,1,&set,0,nullptr);vkCmdDispatch(cmd,(dst.width+7)/8,(dst.height+7)/8,1);}
    else{VkRenderingAttachmentInfo a{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};a.imageView=dst.image.view;a.imageLayout=dst.layout;a.loadOp=VK_ATTACHMENT_LOAD_OP_DONT_CARE;a.storeOp=VK_ATTACHMENT_STORE_OP_STORE;VkRenderingInfo r{VK_STRUCTURE_TYPE_RENDERING_INFO};r.renderArea={{0,0},{dst.width,dst.height}};r.layerCount=1;r.colorAttachmentCount=1;r.pColorAttachments=&a;vkCmdBeginRendering(cmd,&r);vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,stage.pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,stage.pipeline_layout,0,1,&set,0,nullptr);VkDeviceSize zero=0;vkCmdBindVertexBuffers(cmd,0,1,&world.quad.handle,&zero);world_viewport(cmd,dst.width,dst.height);vkCmdDraw(cmd,3,1,0,0);vkCmdEndRendering(cmd);}
    post_transition(cmd,dst,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
}
void post_buffer_barrier(VkCommandBuffer cmd,VkPipelineStageFlags2 src,VkAccessFlags2 access,VkPipelineStageFlags2 dst,VkAccessFlags2 target){VkBufferMemoryBarrier2 b{VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2};b.srcStageMask=src;b.srcAccessMask=access;b.dstStageMask=dst;b.dstAccessMask=target;b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;b.buffer=post.histogram.handle;b.size=VK_WHOLE_SIZE;VkDependencyInfo info{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};info.bufferMemoryBarrierCount=1;info.pBufferMemoryBarriers=&b;vkCmdPipelineBarrier2(cmd,&info);}
void prepare_postprocess(VkCommandBuffer cmd,WorldSlot& slot){
    post.output=nullptr;if(!(post.config.flags&1))return;
    if(!post.point){VkSamplerCreateInfo s{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO};s.magFilter=s.minFilter=VK_FILTER_NEAREST;s.addressModeU=s.addressModeV=s.addressModeW=VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;check(vkCreateSampler(device,&s,nullptr,&post.point),"create postprocess point sampler");post.histogram=make_buffer(256,VK_BUFFER_USAGE_STORAGE_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT,false);}
    if(!post.compose || post.compose_format!=color_format){wait(next_completion);if(post.compose)vkDestroyPipeline(device,post.compose,nullptr);auto ps=world_file_shader("world_post_compose_ps");try{post.compose=world_graphics(world.fullscreen,"__vertex_shader",ps,"main",world.light_pipeline_layout,{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,&color_format,1,true,false,false,true);post.compose_format=color_format;vkDestroyShaderModule(device,ps,nullptr);}catch(...){vkDestroyShaderModule(device,ps,nullptr);throw;}}
    for(auto& e:post.exposure){post_image(e,1,1,VK_FORMAT_R32G32_SFLOAT);if(!e.layout || (post.config.flags&512))post_clear(cmd,e,0);}post.config.flags&=~512u;
    post_image(post.half,std::max(1u,extent.width/2),std::max(1u,extent.height/2),VK_FORMAT_R16G16B16A16_SFLOAT);
    for(auto* i:{&post.tone,&post.fxaa,&post.chromatic})post_image(*i,extent.width,extent.height,VK_FORMAT_R8G8B8A8_UNORM);
    for(auto* i:{&post.ao,&post.ao_blur})post_image(*i,extent.width,extent.height,VK_FORMAT_R8_UNORM);
    auto& constants=post.constants[slot_index];world_buffer(constants,world_align(64)+world_align(288)+16,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);std::memcpy(constants.mapped,post.config.histogram,64);std::memcpy(static_cast<uint8_t*>(constants.mapped)+world_align(64),post.config.ssao,288);float blur[4]{1.5f,0,0,0};std::memcpy(static_cast<uint8_t*>(constants.mapped)+world_align(64)+world_align(288),blur,16);check(vmaFlushAllocation(allocator,constants.memory,0,VK_WHOLE_SIZE),"flush copied postprocess constants");
    if(hbao.config.flags&1){frame_mark("HBAO");render_hbao(cmd,slot);}
    else if(post.config.flags&64){post_pass(cmd,slot,14,post.ao,{world.gbuffer[3].view,{},world.gbuffer[1].view,{},{},world.gbuffer[3].view});++post.stats.ssao_passes;
        if(post.config.flags&128){post_pass(cmd,slot,31,post.ao_blur,{world.gbuffer[3].view,{},{},{},{},post.ao.image.view});post_pass(cmd,slot,32,post.ao,{world.gbuffer[3].view,{},{},{},{},post.ao_blur.image.view});post.stats.ssao_passes+=2;}}
    else post_clear(cmd,post.ao,1);
}
void render_postprocess(VkCommandBuffer cmd,WorldSlot& slot){
    guest_replay(cmd,4);if(!(post.config.flags&1)){guest_replay(cmd,5);return;}auto flags=post.config.flags;auto& exposure=post.exposure[post.exposure_index];
    if(flags&2){post_pass(cmd,slot,1,post.half,{world.hdr.view});post_buffer_barrier(cmd,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT);vkCmdFillBuffer(cmd,post.histogram.handle,0,256,0);post_buffer_barrier(cmd,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
        uint32_t id=flags&1024?4:3;auto& s=post.stages[id];auto set=post_set(slot,id,post.half,{post.half.image.view,world.gbuffer[3].view});vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,s.pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,s.pipeline_layout,0,1,&set,0,nullptr);vkCmdDispatch(cmd,(post.half.width+15)/16,(post.half.height+15)/16,1);++post.stats.histogram_dispatches;post_buffer_barrier(cmd,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_READ_BIT);
        auto& next=post.exposure[post.exposure_index^1];post_pass(cmd,slot,2,next,{VK_NULL_HANDLE,exposure.image.view});post.exposure_index^=1;
    }else post_pass(cmd,slot,0,exposure);++post.stats.exposure_passes;
    VkImageView bloom{};
    if(flags&4){uint32_t width=(flags&8)?extent.width:std::max(1u,extent.width/2),height=(flags&8)?extent.height:std::max(1u,extent.height/2);uint32_t levels=uint32_t(std::clamp(int(std::log2(height))-8+int(post.config.bloom_size),1,10));
        post_image(post.bloom_down[0],width,height,VK_FORMAT_R16G16B16A16_SFLOAT);post_pass(cmd,slot,flags&16?6:5,post.bloom_down[0],{world.hdr.view,{},{},post.exposure[post.exposure_index].image.view});++post.stats.bloom_passes;
        for(uint32_t i=1;i<=levels;++i){width=std::max(1u,width/2);height=std::max(1u,height/2);post_image(post.bloom_down[i],width,height,VK_FORMAT_R16G16B16A16_SFLOAT);post_pass(cmd,slot,7,post.bloom_down[i],{post.bloom_down[i-1].image.view});++post.stats.bloom_passes;}
        auto* low=&post.bloom_down[levels];for(int i=int(levels)-1;i>=0;--i){post_image(post.bloom_up[i],post.bloom_down[i].width,post.bloom_down[i].height,VK_FORMAT_R16G16B16A16_SFLOAT);post_pass(cmd,slot,8,post.bloom_up[i],{low->image.view,post.bloom_down[i].image.view});low=&post.bloom_up[i];++post.stats.bloom_passes;}bloom=low->image.view;
    }else{post_image(post.bloom_down[0],1,1,VK_FORMAT_R16G16B16A16_SFLOAT);post_clear(cmd,post.bloom_down[0],0);bloom=post.bloom_down[0].image.view;}
    uint32_t tone=9+((flags&32)?1:0)+((flags&2048)?2:0);
    post_pass(cmd,slot,tone,post.tone,{world.hdr.view,post.exposure[post.exposure_index].image.view,bloom,textures.at(post.config.dirt).image.view});++post.stats.tone_dispatches;post.output=&post.tone;post.after_tonemap=post.output;guest_replay(cmd,5);ngx_from_tonemap(cmd);
    render_billboards(cmd,slot,3);
    if(flags&32){post_pass(cmd,slot,13,post.fxaa,{post.output->image.view});post.output=&post.fxaa;++post.stats.fxaa_passes;}
    frame_mark("highlights");render_highlights(cmd,slot);frame_mark("postprocess tail");
    if(flags&256){post_pass(cmd,slot,14+post.config.chromatic_iterations,post.chromatic,{post.output->image.view});post.output=&post.chromatic;++post.stats.chromatic_dispatches;}++post.stats.frames;
}
void cleanup_postprocess(){
    for(auto& s:post.stages){if(s.pipeline)vkDestroyPipeline(device,s.pipeline,nullptr);if(s.pipeline_layout)vkDestroyPipelineLayout(device,s.pipeline_layout,nullptr);if(s.layout)vkDestroyDescriptorSetLayout(device,s.layout,nullptr);}
    for(auto* i:{&post.shadow_blur,&post.half,&post.tone,&post.fxaa,&post.chromatic,&post.ao,&post.ao_blur})destroy_image(i->image);for(auto& i:post.exposure)destroy_image(i.image);for(auto& i:post.bloom_down)destroy_image(i.image);for(auto& i:post.bloom_up)destroy_image(i.image);for(auto& b:post.constants)destroy_buffer(b);destroy_buffer(post.histogram);if(post.point)vkDestroySampler(device,post.point,nullptr);if(post.compose)vkDestroyPipeline(device,post.compose,nullptr);post={};
}
