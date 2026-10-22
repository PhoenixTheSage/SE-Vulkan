// Captured stock probe processing owners. Factor blending remains stock-dormant.
bool native_probe_id(uint32_t id){return id>=1280&&id<1284;}
PostStage build_probe_generation(uint32_t id,const void* data,uint64_t bytes){
    uint32_t kind=id-1280;PostStage result{};SpvReflectShaderModule reflected{};VkShaderModule module{};
    try {
        module=world_shader(data,bytes,VK_SHADER_STAGE_FRAGMENT_BIT,reflected);if(reflected.push_constant_block_count)throw std::runtime_error("probe generation push constants unsupported");
        uint32_t targets=kind>=2?6u:1u;bool outputs[6]{};
        for(uint32_t i=0;i<reflected.output_variable_count;++i){auto& v=*reflected.output_variables[i];bool floating=v.format==SPV_REFLECT_FORMAT_R32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT;
            if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN||v.location>=targets||outputs[v.location]||!floating)throw std::runtime_error("probe generation output mismatch");outputs[v.location]=true;}
        for(uint32_t i=0;i<targets;++i)if(!outputs[i])throw std::runtime_error("probe generation output missing");
        for(uint32_t i=0;i<reflected.input_variable_count;++i){auto& v=*reflected.input_variables[i];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;if(v.location||v.format!=SPV_REFLECT_FORMAT_R32G32_SFLOAT)throw std::runtime_error("probe generation fullscreen input mismatch");}
        std::vector<VkDescriptorSetLayoutBinding> bindings;
        for(uint32_t i=0;i<reflected.descriptor_binding_count;++i){auto& b=reflected.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
            bool extras=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.binding==6&&b.block.size>0&&b.block.size<=320;
            bool uniform=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.block.size>0&&((b.binding==0&&b.block.size<=1280)||(b.binding==1&&b.block.size<=(kind==0?32u:16u))||(kind==1&&b.binding==2&&b.block.size<=144));
            bool stock_image=type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&!b.image.arrayed&&!b.image.ms&&
                (kind==0?(b.binding==129&&b.image.dim==SpvDimCube):kind==1?((b.binding==128&&b.image.dim==SpvDim2D)||(b.binding==138&&b.image.dim==SpvDimCube)):b.binding>=128&&b.binding<(kind==2?134u:140u)&&b.image.dim==SpvDim2D);
            bool catalog=reflect_catalog_resource(result,b,stock_image);
            bool sampler=type==VK_DESCRIPTOR_TYPE_SAMPLER&&b.binding>=256&&b.binding<=259;
            if(b.set||b.count!=1||!(extras||uniform||stock_image||catalog||sampler))throw std::runtime_error("probe generation descriptor contract mismatch");
            if(kind==1&&b.binding==2&&type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.block.size>64)result.probe_full_view=true;
            result.bindings.push_back({b.binding,type});bindings.push_back({b.binding,type,1,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr});
        }
        VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=uint32_t(bindings.size());set.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&set,nullptr,&result.layout),"probe generation descriptors");result.pipeline_layout=world_pipeline_layout(result.layout);
        require_probe_format();VkFormat formats[6];std::fill(std::begin(formats),std::end(formats),stock_probe_format);result.pipeline=world_graphics(world.fullscreen,"__vertex_shader",module,"__pixel_shader",result.pipeline_layout,{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,formats,targets,false,false,false,false,kind==2?4:0);
        spvReflectDestroyShaderModule(&reflected);vkDestroyShaderModule(device,module,nullptr);return result;
    }catch(...){if(reflected._internal)spvReflectDestroyShaderModule(&reflected);if(module)vkDestroyShaderModule(device,module,nullptr);post_destroy_stage(result);throw;}
}
