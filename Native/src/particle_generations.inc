// Particle programs preserve stage-local DX register namespaces and list protocols.
bool native_particle_id(uint32_t id){return id>=1536&&id<1546;}
void particle_generation_descriptors(PostStage& result,std::vector<VkDescriptorSetLayoutBinding>& bindings,const SpvReflectShaderModule& module,uint32_t kind){
    auto shader_stage=kind<8?VK_SHADER_STAGE_COMPUTE_BIT:kind==8?VK_SHADER_STAGE_VERTEX_BIT:VK_SHADER_STAGE_FRAGMENT_BIT;
    if(module.push_constant_block_count)throw std::runtime_error("particle generation push constants unsupported");
    for(uint32_t i=0;i<module.descriptor_binding_count;++i){auto& b=module.descriptor_bindings[i];uint32_t logical=b.binding%1024;auto type=VkDescriptorType(b.descriptor_type);
        if(b.set||b.count!=1||(kind==8?b.binding<1024||b.binding>=2048:b.binding>=1024))throw std::runtime_error("particle generation register relocation/set mismatch");
        auto local=b;local.binding=logical;bool catalog=reflect_catalog_resource(result,local);if(catalog)result.catalog.back().binding=b.binding;
        bool extras=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&logical==6&&b.block.size>0&&b.block.size<=320;
        bool uniform=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&((logical==0&&b.block.size==1280)||(logical==1&&b.block.size==16)||(logical==4&&b.block.size==1200));
        bool storage=type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER&&(kind<8?(logical==129||logical==384||logical==385||logical==387||logical==388||(logical>=513&&logical<=516)):kind==8&&(logical==128||logical==129||logical==130));
        if(storage){uint32_t expected=logical==128||logical==384?64u:logical==129?464u:4u;uint32_t stride=0;if(b.block.member_count){auto& member=b.block.members[0];stride=member.array.stride;if(!stride&&member.type_description)stride=member.type_description->traits.array.stride;}
            if(stride?stride!=expected:expected!=4||logical<513||logical>516||b.block.member_count!=1||b.block.members[0].offset!=0||b.block.members[0].size!=4)throw std::runtime_error("particle generation storage stride mismatch: kind="+std::to_string(kind)+" binding="+std::to_string(b.binding)+" expected="+std::to_string(expected)+" stride="+std::to_string(stride)+" size="+std::to_string(b.block.size));}
        bool texel=kind<8&&type==VK_DESCRIPTOR_TYPE_STORAGE_TEXEL_BUFFER&&(logical==386||logical==387)&&b.image.image_format==SpvImageFormatR32ui;
        bool image=type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&((logical==128&&kind!=8)||(logical==129&&kind==9)||(logical==130&&kind!=8)||logical==139||logical==144)&&!b.image.ms&&b.image.dim==(logical==139?SpvDimCube:SpvDim2D)&&b.image.arrayed==(logical==144||(kind==9&&logical==129));
        bool sampler=type==VK_DESCRIPTOR_TYPE_SAMPLER&&((logical>=256&&logical<=259)||logical==271);
        if(!(catalog||extras||uniform||storage||texel||image||sampler))throw std::runtime_error("particle generation descriptor contract mismatch");
        if(std::any_of(bindings.begin(),bindings.end(),[&](auto old){return old.binding==b.binding;}))throw std::runtime_error("particle generation duplicate physical binding");
        result.bindings.push_back({b.binding,type});bindings.push_back({b.binding,type,1,VkShaderStageFlags(shader_stage),nullptr});
    }
}
PostStage build_particle_generation(uint32_t id,const SevkPostprocessModule* modules,uint32_t count){
    uint32_t kind=id-1536;PostStage result{};SpvReflectShaderModule reflected[2]{};VkShaderModule shaders[2]{};
    try {
        std::vector<VkDescriptorSetLayoutBinding> bindings;uint32_t module_count=kind==9?2u:1u;
        for(uint32_t k=0;k<module_count;++k){uint32_t stage=kind==9?1544+k:id;const SevkPostprocessModule* source=nullptr;for(uint32_t i=0;i<count;++i)if(modules[i].stage==stage)source=&modules[i];if(!source)throw std::runtime_error("incomplete particle generation closure");
            shaders[k]=world_shader(source->spirv,source->bytes,stage<1544?VK_SHADER_STAGE_COMPUTE_BIT:stage==1544?VK_SHADER_STAGE_VERTEX_BIT:VK_SHADER_STAGE_FRAGMENT_BIT,reflected[k]);particle_generation_descriptors(result,bindings,reflected[k],stage-1536);
        }
        if(kind<8){auto work=reflected[0].entry_points[0].local_size;uint32_t x=kind==2||kind==3?128u:kind==4||kind==7?1u:256u,y=kind==2||kind==3?8u:1u;if(work.x!=x||work.y!=y||work.z!=1)throw std::runtime_error("particle generation workgroup contract mismatch");}
        else {validate_foliage_link(reflected[0],reflected[1]);for(uint32_t i=0;i<reflected[0].input_variable_count;++i)if(!(reflected[0].input_variables[i]->decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN))throw std::runtime_error("particle generation requires SV_VertexID without vertex streams");
            bool outputs[2]{};for(uint32_t i=0;i<reflected[1].output_variable_count;++i){auto& v=*reflected[1].output_variables[i];bool floating=v.format==SPV_REFLECT_FORMAT_R32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT;
                if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN||v.location>=2||outputs[v.location]||!floating)throw std::runtime_error("particle generation OIT output mismatch");outputs[v.location]=true;}if(!outputs[0]||!outputs[1])throw std::runtime_error("particle generation OIT output missing");}
        VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=uint32_t(bindings.size());set.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&set,nullptr,&result.layout),"particle generation descriptors");result.pipeline_layout=world_pipeline_layout(result.layout);
        if(kind<8){result.compute=true;VkComputePipelineCreateInfo c{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};c.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,shaders[0],"__compute_shader",nullptr};c.layout=result.pipeline_layout;check(vkCreateComputePipelines(device,pipeline_cache,1,&c,nullptr,&result.pipeline),"particle generation compute");}
        else {VkFormat formats[]{VK_FORMAT_R16G16B16A16_SFLOAT,VK_FORMAT_R16_SFLOAT};result.pipeline=world_graphics(shaders[0],"__vertex_shader",shaders[1],"__pixel_shader",result.pipeline_layout,{},{},formats,2,true,false,false,false,3,true);}
        for(uint32_t k=0;k<module_count;++k){spvReflectDestroyShaderModule(&reflected[k]);vkDestroyShaderModule(device,shaders[k],nullptr);}return result;
    }catch(...){for(uint32_t k=0;k<2;++k){if(reflected[k]._internal)spvReflectDestroyShaderModule(&reflected[k]);if(shaders[k])vkDestroyShaderModule(device,shaders[k],nullptr);}post_destroy_stage(result);throw;}
}
