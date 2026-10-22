// Five stock decal VS/PS owners. Each pair activates as one dependency closure.
bool native_decal_id(uint32_t id){return id>=1024&&id<1034;}
uint32_t decal_generation_role(uint32_t id){static constexpr uint32_t roles[]{1,2,3,7,8};return roles[(id-1024)/2];}
uint32_t decal_generation_pixel(uint32_t role){for(uint32_t id=1025;id<1034;id+=2)if(decal_generation_role(id)==role)return id;throw std::runtime_error("invalid decal generation role");}
PostStage build_decal_generation(uint32_t pixel_id,const SevkPostprocessModule* modules,uint32_t count){
    uint32_t role=decal_generation_role(pixel_id);SpvReflectShaderModule reflected[2]{};VkShaderModule shaders[2]{};PostStage result{};
    try {
        std::vector<VkDescriptorSetLayoutBinding> bindings;std::map<uint32_t,std::array<uint32_t,7>> signatures;
        for(uint32_t k=0;k<2;++k){const SevkPostprocessModule* source=nullptr;for(uint32_t i=0;i<count;++i)if(modules[i].stage==pixel_id-1+k)source=&modules[i];
            if(!source)throw std::runtime_error("incomplete decal generation pair");auto shader_stage=k?VK_SHADER_STAGE_FRAGMENT_BIT:VK_SHADER_STAGE_VERTEX_BIT;
            shaders[k]=world_shader(source->spirv,source->bytes,shader_stage,reflected[k]);auto& module=reflected[k];if(module.push_constant_block_count)throw std::runtime_error("decal generation push constants unsupported");
            for(uint32_t i=0;i<module.descriptor_binding_count;++i){auto& b=module.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
                bool extras=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.binding==6&&b.block.size>0&&b.block.size<=320;
                bool reserved=b.binding>=133&&b.binding<=137&&(type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE||type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER||type==VK_DESCRIPTOR_TYPE_UNIFORM_TEXEL_BUFFER);
                bool uniform=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&((b.binding==0&&b.block.size==1280)||(b.binding==2&&b.block.size==65536)||(role==8&&b.binding==4&&b.block.size==1200));
                bool image=type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&(b.binding==128||b.binding==129||(b.binding>=131&&b.binding<=134)||(role==8&&(b.binding==139||b.binding==144||b.binding==145)))&&!b.image.ms&&b.image.dim==(b.binding==139||b.binding==145?SpvDimCube:SpvDim2D)&&b.image.arrayed==(b.binding==144);
                bool sampler=type==VK_DESCRIPTOR_TYPE_SAMPLER&&((b.binding>=256&&b.binding<=259)||(role==8&&b.binding==271));
                if(b.set||b.count!=1||!(extras||reserved||uniform||image||sampler))throw std::runtime_error("decal generation descriptor contract mismatch");
                uint32_t number=b.type_description&&(b.type_description->type_flags&SPV_REFLECT_TYPE_FLAG_INT)?(b.type_description->traits.numeric.scalar.signedness?1u:2u):0u;
                std::array<uint32_t,7> signature{uint32_t(type),b.block.size,uint32_t(b.image.dim),b.image.arrayed,b.image.depth,b.image.ms,number};auto found=signatures.find(b.binding);
                if(found!=signatures.end()){if(found->second!=signature)throw std::runtime_error("decal shader-stage resource declarations disagree");for(auto& old:bindings)if(old.binding==b.binding)old.stageFlags|=shader_stage;}
                else {signatures.emplace(b.binding,signature);if(reserved&&!reflect_catalog_resource(result,b,image))throw std::runtime_error("decal catalog resource unsupported");
                    if(b.binding==2&&type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER)type=VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC;
                    result.bindings.push_back({b.binding,type});bindings.push_back({b.binding,type,1,VkShaderStageFlags(shader_stage),nullptr});}
            }
        }
        validate_foliage_link(reflected[0],reflected[1]);
        for(uint32_t i=0;i<reflected[0].input_variable_count;++i)if(!(reflected[0].input_variables[i]->decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN))throw std::runtime_error("decal generation requires SV_VertexID without vertex streams");
        bool outputs[3]{};uint32_t targets=role==8?2u:3u;
        for(uint32_t i=0;i<reflected[1].output_variable_count;++i){auto& v=*reflected[1].output_variables[i];bool floating=v.format==SPV_REFLECT_FORMAT_R32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT;
            if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN||v.location>=targets||outputs[v.location]||!floating)throw std::runtime_error("decal generation output mismatch");outputs[v.location]=true;}
        for(uint32_t i=0;i<targets;++i)if(!outputs[i])throw std::runtime_error("decal generation output missing");
        VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=uint32_t(bindings.size());set.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&set,nullptr,&result.layout),"decal generation descriptors");result.pipeline_layout=world_pipeline_layout(result.layout);
        const VkFormat formats[]{VK_FORMAT_R16G16B16A16_SFLOAT,VK_FORMAT_R16_SFLOAT};result.pipeline=world_graphics(shaders[0],"__vertex_shader",shaders[1],"__pixel_shader",result.pipeline_layout,{},{},role==8?formats:stock_formats.data(),targets,true,false,false,false,role==8?3:16+role,true);
        for(uint32_t k=0;k<2;++k){spvReflectDestroyShaderModule(&reflected[k]);vkDestroyShaderModule(device,shaders[k],nullptr);}return result;
    }catch(...){for(uint32_t k=0;k<2;++k){if(reflected[k]._internal)spvReflectDestroyShaderModule(&reflected[k]);if(shaders[k])vkDestroyShaderModule(device,shaders[k],nullptr);}post_destroy_stage(result);throw;}
}
