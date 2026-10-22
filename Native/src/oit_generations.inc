// Actual native weighted-OIT publication shader; geometry remains a separate owner.
bool native_oit_id(uint32_t id){return id==2048;}
PostStage build_oit_generation(const void* data,uint64_t bytes){
    PostStage result{};SpvReflectShaderModule reflected{};VkShaderModule module{};
    try{module=world_shader(data,bytes,VK_SHADER_STAGE_FRAGMENT_BIT,reflected);if(reflected.push_constant_block_count)throw std::runtime_error("OIT generation push constants unsupported");
        if(reflected.output_variable_count!=1)throw std::runtime_error("OIT generation requires one HDR output");auto& output=*reflected.output_variables[0];
        if(output.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN||output.location||output.format!=SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT)throw std::runtime_error("OIT generation HDR output must preserve alpha");
        for(uint32_t i=0;i<reflected.input_variable_count;++i){auto& v=*reflected.input_variables[i];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;if(v.location||v.format!=SPV_REFLECT_FORMAT_R32G32_SFLOAT)throw std::runtime_error("OIT generation fullscreen input mismatch");}
        std::vector<VkDescriptorSetLayoutBinding> bindings;
        for(uint32_t i=0;i<reflected.descriptor_binding_count;++i){auto& b=reflected.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);bool catalog=reflect_catalog_resource(result,b);
            bool extras=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.binding==6&&b.block.size>0&&b.block.size<=320;
            bool frame=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.binding==0&&b.block.size>0&&b.block.size<=1280;
            bool image=type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&(b.binding==128||b.binding==129)&&b.image.dim==SpvDim2D&&!b.image.arrayed&&!b.image.ms;
            bool sampler=type==VK_DESCRIPTOR_TYPE_SAMPLER&&b.binding>=256&&b.binding<=259;
            if(b.set||b.count!=1||!(catalog||extras||frame||image||sampler))throw std::runtime_error("OIT generation descriptor contract mismatch");result.bindings.push_back({b.binding,type});bindings.push_back({b.binding,type,1,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr});
        }
        VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=uint32_t(bindings.size());set.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&set,nullptr,&result.layout),"OIT generation descriptors");result.pipeline_layout=world_pipeline_layout(result.layout);
        hdr_pipeline_pair(result.pipeline,result.other_hdr,[&](VkFormat format){return world_graphics(world.fullscreen,"__vertex_shader",module,"__pixel_shader",result.pipeline_layout,{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,&format,1,false,false,false,false,2);});
        spvReflectDestroyShaderModule(&reflected);vkDestroyShaderModule(device,module,nullptr);return result;
    }catch(...){if(reflected._internal)spvReflectDestroyShaderModule(&reflected);if(module)vkDestroyShaderModule(device,module,nullptr);post_destroy_stage(result);throw;}
}
