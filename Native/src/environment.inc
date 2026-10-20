// Stock atmosphere optical-depth LUT and cloud shell passes, in main HDR.
// The caller's packets contain copied constants, never retained managed pointers.
VkDescriptorSetLayout environment_layout(uint32_t kind){
    std::vector<VkDescriptorSetLayoutBinding> b;
    auto add=[&](uint32_t id,VkDescriptorType type,VkShaderStageFlags stages){b.push_back({id,type,1,stages,nullptr});};
    if(kind==0){add(1,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,VK_SHADER_STAGE_COMPUTE_BIT);add(384,VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,VK_SHADER_STAGE_COMPUTE_BIT);}
    else {if(kind==3)add(2,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,VK_SHADER_STAGE_FRAGMENT_BIT);add(0,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,VK_SHADER_STAGE_FRAGMENT_BIT);add(1,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,VK_SHADER_STAGE_VERTEX_BIT|VK_SHADER_STAGE_FRAGMENT_BIT);
        add(128,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,VK_SHADER_STAGE_FRAGMENT_BIT);add(kind==1 || kind==3?133:129,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,VK_SHADER_STAGE_FRAGMENT_BIT);add(kind==1 || kind==3?258:256,VK_DESCRIPTOR_TYPE_SAMPLER,VK_SHADER_STAGE_FRAGMENT_BIT);}
    VkDescriptorSetLayoutCreateInfo info{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};info.bindingCount=uint32_t(b.size());info.pBindings=b.data();VkDescriptorSetLayout result{};check(vkCreateDescriptorSetLayout(device,&info,nullptr,&result),"create environment descriptor layout");return result;
}
void init_environment(uint32_t kind,const void* data,uint64_t bytes){
    if(recording || kind>=10)throw std::runtime_error("invalid environment stage/state");wait(next_completion);init_world_layouts();if(kind>=4 && kind<9){init_foliage(kind,data,bytes);return;}
    SpvReflectShaderModule reflection{};VkShaderModule shader=world_shader(data,bytes,kind==0?VK_SHADER_STAGE_COMPUTE_BIT:(kind==2?VK_SHADER_STAGE_VERTEX_BIT:VK_SHADER_STAGE_FRAGMENT_BIT),reflection);
    try {
        for(uint32_t i=0;i<reflection.descriptor_binding_count;++i){auto& b=reflection.descriptor_bindings[i];
            bool uniform=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER && ((b.binding==0 && b.block.size==1280 && (kind==1 || kind==3 || kind==9)) || (b.binding==1 && b.block.size==144) || (kind==9 && b.binding==2 && b.block.size==144));
            bool image=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE && b.image.dim==SpvDim2D && !b.image.ms && !b.image.arrayed && (((kind==1 || kind==9) && (b.binding==128 || b.binding==133)) || (kind==3 && (b.binding==128 || b.binding==129)));
            bool storage=kind==0 && b.binding==384 && b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_STORAGE_IMAGE && b.image.dim==SpvDim2D && !b.image.arrayed;
            bool sample=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER && (((kind==1 || kind==9) && b.binding==258) || (kind==3 && b.binding==256));
            if(b.set || b.count!=1 || !(uniform || image || storage || sample))throw std::runtime_error("environment descriptor contract mismatch");
        }
        if(reflection.push_constant_block_count)throw std::runtime_error("unexpected environment push constants");
        if(environment.stages[kind])throw std::runtime_error("environment stage already configured");
        for(uint32_t i=0;i<4;++i)if(!environment.layouts[i]){environment.layouts[i]=environment_layout(i);environment.pipeline_layouts[i]=world_pipeline_layout(environment.layouts[i]);}
        if(kind==0){VkComputePipelineCreateInfo p{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};p.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,shader,"__compute_shader",nullptr};p.layout=environment.pipeline_layouts[0];check(vkCreateComputePipelines(device,pipeline_cache,1,&p,nullptr,&environment.pipelines[0]),"create atmosphere precompute");}
        if(kind==9){require_probe_format();const VkFormat formats[]{stock_probe_format,stock_probe_format};environment.pipelines[3]=world_graphics(world.fullscreen,"__vertex_shader",shader,"__pixel_shader",environment.pipeline_layouts[3],{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,formats,2,false,false,false,false,2);}
        if(kind==1){if(!world.fullscreen)throw std::runtime_error("fullscreen stage is required before atmosphere");hdr_pipeline_pair(environment.pipelines[1],environment.other_hdr[1],[&](VkFormat hdr){return world_graphics(world.fullscreen,"__vertex_shader",shader,"__pixel_shader",environment.pipeline_layouts[1],{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,&hdr,1,false,false,false,false,2);});}
        if(kind==3){if(!environment.stages[2])throw std::runtime_error("cloud vertex stage is required");SpvReflectShaderModule vs{}; // Reflect original VS retained at initialization below.
            std::vector<VkVertexInputAttributeDescription> attrs=cloud_attributes;
            hdr_pipeline_pair(environment.pipelines[2],environment.other_hdr[2],[&](VkFormat hdr){return world_graphics(environment.stages[2],"__vertex_shader",shader,"__pixel_shader",environment.pipeline_layouts[2],{{0,sizeof(SevkWorldVertex),VK_VERTEX_INPUT_RATE_VERTEX}},attrs,&hdr,1,true,false,false,false,1,true);});}
        if(kind==2){for(uint32_t i=0;i<reflection.input_variable_count;++i){auto& v=*reflection.input_variables[i];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;std::string name=v.name?v.name:"";name=name.substr(name.find_last_of('.')+1);VkFormat format{};uint32_t offset{};
            if(name=="POSITION0" || name=="position"){format=VK_FORMAT_R32G32B32A32_SFLOAT;offset=0;}else if(name=="NORMAL0" || name=="normal"){format=VK_FORMAT_R32G32_UINT;offset=16;}else if(name=="TEXCOORD0" || name=="texcoord"){format=VK_FORMAT_R32G32_SFLOAT;offset=40;}else throw std::runtime_error("unsupported cloud input "+name);cloud_attributes.push_back({v.location,0,format,offset});}}
        environment.stages[kind]=shader;spvReflectDestroyShaderModule(&reflection);
    } catch(...){spvReflectDestroyShaderModule(&reflection);vkDestroyShaderModule(device,shader,nullptr);throw;}
}
std::vector<VkVertexInputAttributeDescription> cloud_attributes;
std::vector<VkVertexInputAttributeDescription> foliage_attributes;
void init_foliage(uint32_t kind,const void* data,uint64_t bytes){
    auto stage=kind==4?VK_SHADER_STAGE_VERTEX_BIT:((kind==5 || kind==7)?VK_SHADER_STAGE_GEOMETRY_BIT:VK_SHADER_STAGE_FRAGMENT_BIT);
    SpvReflectShaderModule reflection{};auto shader=world_shader(data,bytes,stage,reflection);
    try {
        if(environment.stages[kind])throw std::runtime_error("foliage stage already configured");
        for(uint32_t i=0;i<reflection.descriptor_binding_count;++i){auto& b=reflection.descriptor_bindings[i];
            bool uniform=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER && ((b.binding==0 && b.block.size==1280) || (b.binding==1 && b.block.size==64) || (b.binding==2 && b.block.size==128) || (b.binding==4 && b.block.size==4096));
            bool image=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE && b.image.dim==SpvDim2D && !b.image.ms && ((b.binding==128 || b.binding==129)?b.image.arrayed:b.binding==156 && !b.image.arrayed);
            bool sample=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER && b.binding==259;
            if(b.set || b.count!=1 || !(uniform || image || sample))throw std::runtime_error("foliage descriptor contract mismatch");
        }if(reflection.push_constant_block_count)throw std::runtime_error("unexpected foliage push constants");
        if(!environment.foliage_layout){std::vector<VkDescriptorSetLayoutBinding> bindings;auto add=[&](uint32_t id,VkDescriptorType type){bindings.push_back({id,type,1,VK_SHADER_STAGE_VERTEX_BIT|VK_SHADER_STAGE_GEOMETRY_BIT|VK_SHADER_STAGE_FRAGMENT_BIT,nullptr});};
            for(uint32_t id:{0u,1u,2u,4u})add(id,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);for(uint32_t id:{128u,129u,156u})add(id,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE);add(259,VK_DESCRIPTOR_TYPE_SAMPLER);
            VkDescriptorSetLayoutCreateInfo l{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};l.bindingCount=uint32_t(bindings.size());l.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&l,nullptr,&environment.foliage_layout),"create foliage layout");environment.foliage_pipeline_layout=world_pipeline_layout(environment.foliage_layout);}
        if(kind==4){for(uint32_t i=0;i<reflection.input_variable_count;++i){auto& v=*reflection.input_variables[i];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;std::string name=v.name?v.name:"";name=name.substr(name.find_last_of('.')+1);
            if(name=="POSITION" || name=="POSITION0")foliage_attributes.push_back({v.location,0,VK_FORMAT_R32G32B32_SFLOAT,0});
            else if(name=="TEXCOORD0")foliage_attributes.push_back({v.location,0,VK_FORMAT_R8G8B8A8_UNORM,12});else throw std::runtime_error("unsupported foliage vertex input "+name);}}
        if(kind==6 || kind==8){if(!environment.stages[4] || !environment.stages[kind-1])throw std::runtime_error("foliage stages missing");environment.foliage_pipelines[kind==6?0:1]=world_graphics(environment.stages[4],"__vertex_shader",shader,"__pixel_shader",environment.foliage_pipeline_layout,{{0,16,VK_VERTEX_INPUT_RATE_VERTEX}},foliage_attributes,stock_formats.data(),3,true,false,false,false,0,false,environment.stages[kind-1],true);}
        environment.stages[kind]=shader;spvReflectDestroyShaderModule(&reflection);
    }catch(...){spvReflectDestroyShaderModule(&reflection);vkDestroyShaderModule(device,shader,nullptr);throw;}
}
void queue_foliage(const SevkFoliageDraw* draws,uint32_t count,const void* materials,uint32_t bytes){
    if(recording || count>8192 || (count && (!draws || !materials || bytes!=4096)))throw std::runtime_error("invalid foliage packet batch");
    std::vector<SevkFoliageDraw> copy;
    for(uint32_t i=0;i<count;++i){const auto& d=draws[i];if(d.size!=sizeof(d) || d.kind>1 || d.reserved[0] || d.reserved[1] || !environment.foliage_pipelines[d.kind] || !world.meshes.count(d.mesh) || world.meshes.at(d.mesh).vertex_stride!=16 || !world.arrays.count(d.color) || !world.arrays.count(d.normal))throw std::runtime_error("foliage packet/resources mismatch");copy.push_back(d);}
    if(count)std::memcpy(environment.foliage_table.data(),materials,4096);environment.foliage_draws=std::move(copy);
}
void prepare_foliage(WorldSlot& slot){
    if(environment.foliage_draws.empty())return;auto& objects=environment.foliage_objects[slot_index];auto& materials=environment.foliage_materials[slot_index];world_buffer(objects,world_align(128)*environment.foliage_draws.size(),VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);world_buffer(materials,4096,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);
    for(uint32_t i=0;i<environment.foliage_draws.size();++i)std::memcpy(static_cast<uint8_t*>(objects.mapped)+world_align(128)*i,environment.foliage_draws[i].object_constants,128);std::memcpy(materials.mapped,environment.foliage_table.data(),4096);
    for(auto* b:{&objects,&materials})check(vmaFlushAllocation(allocator,b->memory,0,VK_WHOLE_SIZE),"flush foliage constants");
}
void render_foliage(VkCommandBuffer cmd,WorldSlot& slot){
    for(uint32_t i=0;i<environment.foliage_draws.size();++i){auto& d=environment.foliage_draws[i];auto& mesh=world.meshes.at(d.mesh);auto set=world_set(slot,environment.foliage_layout);world_write_buffer(set,0,slot.frame,1280,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,1,slot.projections,64,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,4,environment.foliage_materials[slot_index],4096,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
        VkDescriptorBufferInfo buffer{environment.foliage_objects[slot_index].handle,world_align(128)*i,128};VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstSet=set;w.dstBinding=2;w.descriptorCount=1;w.descriptorType=VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;w.pBufferInfo=&buffer;vkUpdateDescriptorSets(device,1,&w,0,nullptr);
        world_write_image(set,128,world.arrays.at(d.color).view);world_write_image(set,129,world.arrays.at(d.normal).view);world_write_image(set,156,stock.dither.view);world_write_image(set,259,{},sampler,true);
        vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,environment.foliage_pipelines[d.kind]);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,environment.foliage_pipeline_layout,0,1,&set,0,nullptr);VkDeviceSize zero=0;vkCmdBindVertexBuffers(cmd,0,1,&mesh.vertices.handle,&mesh.vertex_offset);vkCmdDraw(cmd,mesh.index_count,1,0,0);++environment.stats.foliage_draws;environment.stats.foliage_points+=mesh.index_count;
    }
}
void queue_environment(const SevkEnvironmentDraw* draws,uint32_t count){
    if(recording || count>128 || (count && !draws))throw std::runtime_error("invalid environment packet batch");
    std::vector<SevkEnvironmentDraw> copy;
    for(uint32_t i=0;i<count;++i){const auto& d=draws[i];if(d.size!=sizeof(d) || d.kind>1 || !d.id || d.flags || d.reserved || !environment.pipelines[d.kind+1])throw std::runtime_error("invalid environment packet");
        if(d.kind==1 && (!world.meshes.count(d.mesh) || world.meshes.at(d.mesh).vertex_stride!=sizeof(SevkWorldVertex) || !textures.count(d.color) || !textures.count(d.alpha)))throw std::runtime_error("cloud resources missing");
        for(uint32_t f=0;f<144/4;++f){float value;std::memcpy(&value,d.constants+f*4,4);if(!std::isfinite(value))throw std::runtime_error("non-finite environment constant");}
        if(d.kind==0){const float* f=reinterpret_cast<const float*>(d.constants);if(f[3]<=f[7] || f[7]<=0 || f[12]<=0 || f[13]<=0 || f[14]<=0)throw std::runtime_error("invalid atmosphere radii/heights/scale");}
        copy.push_back(d);
    }environment.draws=std::move(copy);
}
VkDescriptorSet environment_set(WorldSlot& slot,uint32_t kind,uint32_t index){
    auto set=world_set(slot,environment.layouts[kind]);VkDescriptorBufferInfo buffer{environment.constants[slot_index].handle,world_align(144)*index,144};VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstSet=set;w.dstBinding=1;w.descriptorCount=1;w.descriptorType=VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;w.pBufferInfo=&buffer;vkUpdateDescriptorSets(device,1,&w,0,nullptr);
    if(kind)world_write_buffer(set,0,slot.frame,1280,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);return set;
}
void prepare_environment(VkCommandBuffer cmd,WorldSlot& slot){
    if(environment.draws.empty())return;
    auto& buffer=environment.constants[slot_index];world_buffer(buffer,world_align(144)*environment.draws.size(),VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);
    for(uint32_t i=0;i<environment.draws.size();++i)std::memcpy(static_cast<uint8_t*>(buffer.mapped)+world_align(144)*i,environment.draws[i].constants,144);
    check(vmaFlushAllocation(allocator,buffer.memory,0,VK_WHOLE_SIZE),"flush environment constants");
    for(uint32_t i=0;i<environment.draws.size();++i){auto& d=environment.draws[i];if(d.kind)continue;auto& lut=environment.luts[d.id];if(lut.initialized && !std::memcmp(lut.parameters.data(),d.constants+16,48))continue;
        if(!lut.image.handle)lut.image=make_image(512,128,VK_FORMAT_R32G32_SFLOAT,VK_IMAGE_USAGE_STORAGE_BIT|VK_IMAGE_USAGE_SAMPLED_BIT,VK_IMAGE_ASPECT_COLOR_BIT);
        barrier(cmd,lut.image.handle,lut.initialized?VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_ASPECT_COLOR_BIT,lut.initialized?VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT:VK_PIPELINE_STAGE_2_NONE,lut.initialized?VK_ACCESS_2_SHADER_SAMPLED_READ_BIT:0,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
        auto set=environment_set(slot,0,i);VkDescriptorImageInfo image{};image.imageView=lut.image.view;image.imageLayout=VK_IMAGE_LAYOUT_GENERAL;VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstSet=set;w.dstBinding=384;w.descriptorCount=1;w.descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;w.pImageInfo=&image;vkUpdateDescriptorSets(device,1,&w,0,nullptr);
        vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,environment.pipelines[0]);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,environment.pipeline_layouts[0],0,1,&set,0,nullptr);vkCmdDispatch(cmd,64,16,1);
        barrier(cmd,lut.image.handle,VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);std::memcpy(lut.parameters.data(),d.constants+16,48);lut.initialized=true;++environment.stats.lut_dispatches;
    }
}
void render_environment(VkCommandBuffer cmd,WorldSlot& slot){
    if(environment.draws.empty())return;
    for(uint32_t i=0;i<environment.draws.size();++i){auto& d=environment.draws[i];uint32_t kind=d.kind+1;auto set=environment_set(slot,kind,i);
        if(!d.kind){world_write_image(set,128,world.gbuffer[3].view);world_write_image(set,133,environment.luts.at(d.id).image.view);world_write_image(set,258,{},standard_sampler(258),true);}
        else {world_write_image(set,128,textures.at(d.color).image.view);world_write_image(set,129,textures.at(d.alpha).image.view);world_write_image(set,256,{},sampler,true);
            barrier(cmd,world.gbuffer[3].handle,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT);}
        barrier(cmd,world.hdr.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT|VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
        VkRenderingAttachmentInfo color{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};color.imageView=world.hdr.view;color.imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;color.loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;color.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
        VkRenderingAttachmentInfo depth_info{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};depth_info.imageView=world.gbuffer[3].view;depth_info.imageLayout=VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL;depth_info.loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;depth_info.storeOp=VK_ATTACHMENT_STORE_OP_NONE;
        VkRenderingInfo r{VK_STRUCTURE_TYPE_RENDERING_INFO};r.renderArea={{0,0},extent};r.layerCount=1;r.colorAttachmentCount=1;r.pColorAttachments=&color;if(d.kind)r.pDepthAttachment=&depth_info;vkCmdBeginRendering(cmd,&r);world_viewport(cmd,extent.width,extent.height);
        vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,environment.pipelines[kind]);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,environment.pipeline_layouts[kind],0,1,&set,0,nullptr);VkDeviceSize zero=0;
        if(!d.kind){vkCmdBindVertexBuffers(cmd,0,1,&world.quad.handle,&zero);vkCmdDraw(cmd,3,1,0,0);++environment.stats.atmosphere_draws;}
        else {auto& mesh=world.meshes.at(d.mesh);vkCmdBindVertexBuffers(cmd,0,1,&mesh.vertices.handle,&mesh.vertex_offset);vkCmdBindIndexBuffer(cmd,mesh.indices.handle,mesh.index_offset,VK_INDEX_TYPE_UINT32);vkCmdDrawIndexed(cmd,mesh.index_count,1,0,0,0);++environment.stats.cloud_draws;}
        vkCmdEndRendering(cmd);
        if(d.kind)barrier(cmd,world.gbuffer[3].handle,VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    }++environment.stats.frames;
}
void cleanup_environment() noexcept {
    for(auto p:environment.foliage_pipelines)if(p)vkDestroyPipeline(device,p,nullptr);if(environment.foliage_pipeline_layout)vkDestroyPipelineLayout(device,environment.foliage_pipeline_layout,nullptr);if(environment.foliage_layout)vkDestroyDescriptorSetLayout(device,environment.foliage_layout,nullptr);for(auto& b:environment.foliage_objects)destroy_buffer(b);for(auto& b:environment.foliage_materials)destroy_buffer(b);
    for(auto p:environment.other_hdr)if(p)vkDestroyPipeline(device,p,nullptr);for(auto p:environment.pipelines)if(p)vkDestroyPipeline(device,p,nullptr);for(auto p:environment.pipeline_layouts)if(p)vkDestroyPipelineLayout(device,p,nullptr);for(auto p:environment.layouts)if(p)vkDestroyDescriptorSetLayout(device,p,nullptr);for(auto p:environment.stages)if(p)vkDestroyShaderModule(device,p,nullptr);for(auto& b:environment.constants)destroy_buffer(b);for(auto& p:environment.luts)destroy_image(p.second.image);environment={};cloud_attributes.clear();foliage_attributes.clear();
}
