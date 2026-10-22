// Main-view atmosphere owners share the native frame-boundary transaction.
// Probe MRT and main-view owners keep distinct layouts and execution records.
// Vertex/geometry owners are not aliased to these fragment/compute programs.
bool native_atmosphere_id(uint32_t id){return id>=512&&id<515;}
PostStage build_atmosphere_generation(uint32_t id,const void* data,uint64_t bytes){
    uint32_t kind=id-512,index=kind==2?3:kind;bool probe=kind==2;
    if(!native_atmosphere_id(id)||!environment.pipelines[index])throw std::runtime_error("atmosphere owner unavailable");
    PostStage result{};SpvReflectShaderModule reflected{};VkShaderModule module{};
    try {
        module=world_shader(data,bytes,kind==0?VK_SHADER_STAGE_COMPUTE_BIT:VK_SHADER_STAGE_FRAGMENT_BIT,reflected);
        if(reflected.push_constant_block_count)throw std::runtime_error("atmosphere generation push constants unsupported");
        if(!kind){auto work=reflected.entry_points[0].local_size;if(work.x!=8||work.y!=8||work.z!=1)throw std::runtime_error("atmosphere LUT requires 8x8x1 workgroups");}
        else {
            bool outputs[2]{};uint32_t count=probe?2u:1u;
            for(uint32_t i=0;i<reflected.output_variable_count;++i){auto& v=*reflected.output_variables[i];
                bool floating=v.format==SPV_REFLECT_FORMAT_R32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT;
                if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN||v.location>=count||outputs[v.location]||!floating)throw std::runtime_error("atmosphere fragment output contract mismatch");outputs[v.location]=true;}
            for(uint32_t i=0;i<count;++i)if(!outputs[i])throw std::runtime_error("atmosphere output missing");
            for(uint32_t i=0;i<reflected.input_variable_count;++i){auto& v=*reflected.input_variables[i];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;
                if(v.location||v.format!=SPV_REFLECT_FORMAT_R32G32_SFLOAT)throw std::runtime_error("atmosphere fullscreen input mismatch");}
        }
        std::vector<VkDescriptorSetLayoutBinding> bindings;
        for(uint32_t i=0;i<reflected.descriptor_binding_count;++i){auto& b=reflected.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
            bool extras=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.binding==6&&b.block.size>0&&b.block.size<=320;
            bool catalog=reflect_catalog_resource(result,b,kind!=0&&b.binding==133);
            bool uniform=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&((b.binding==1&&b.block.size==144)||(kind!=0&&b.binding==0&&b.block.size==1280)||(probe&&b.binding==2&&b.block.size==144));
            bool image=kind!=0&&type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&(b.binding==128||b.binding==133)&&b.image.dim==SpvDim2D&&!b.image.ms&&!b.image.arrayed;
            bool sampler=type==VK_DESCRIPTOR_TYPE_SAMPLER&&b.binding>=256&&b.binding<=259;
            bool storage=kind==0&&type==VK_DESCRIPTOR_TYPE_STORAGE_IMAGE&&b.binding==384&&b.image.dim==SpvDim2D&&!b.image.arrayed&&b.image.image_format==SpvImageFormatRg32f;
            if(b.set||b.count!=1||!(extras||catalog||uniform||image||sampler||storage))throw std::runtime_error("atmosphere generation descriptor contract mismatch");
            result.bindings.push_back({b.binding,type});bindings.push_back({b.binding,type,1,VkShaderStageFlags(kind==0?VK_SHADER_STAGE_COMPUTE_BIT:VK_SHADER_STAGE_FRAGMENT_BIT),nullptr});
        }
        if(!kind&&!std::any_of(result.bindings.begin(),result.bindings.end(),[](auto b){return b.first==384&&b.second==VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;}))throw std::runtime_error("atmosphere LUT output missing");
        VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=uint32_t(bindings.size());set.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&set,nullptr,&result.layout),"atmosphere generation descriptors");
        result.pipeline_layout=world_pipeline_layout(result.layout);result.compute=kind==0;
        if(!kind){VkComputePipelineCreateInfo c{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};c.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,module,"__compute_shader",nullptr};c.layout=result.pipeline_layout;check(vkCreateComputePipelines(device,pipeline_cache,1,&c,nullptr,&result.pipeline),"atmosphere generation LUT");}
        else if(probe){require_probe_format();const VkFormat formats[]{stock_probe_format,stock_probe_format};result.pipeline=world_graphics(world.fullscreen,"__vertex_shader",module,"__pixel_shader",result.pipeline_layout,{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,formats,2,false,false,false,false,2);}
        else hdr_pipeline_pair(result.pipeline,result.other_hdr,[&](VkFormat format){return world_graphics(world.fullscreen,"__vertex_shader",module,"__pixel_shader",result.pipeline_layout,{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,&format,1,false,false,false,false,2);});
        spvReflectDestroyShaderModule(&reflected);vkDestroyShaderModule(device,module,nullptr);return result;
    }catch(...){if(reflected._internal)spvReflectDestroyShaderModule(&reflected);if(module)vkDestroyShaderModule(device,module,nullptr);post_destroy_stage(result);throw;}
}

VkSampler environment_generation_sampler(uint32_t binding){
    // Environment/foliage recording precedes postprocess point-sampler creation.
    if(binding==257&&!post.point){guest_ensure_samplers();return guest.samplers[0];}
    return standard_sampler(binding);
}
