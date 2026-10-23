// Foliage's shared vertex and two GS/PS pairs activate as one dependency closure.
bool native_foliage_id(uint32_t id){return id>=768&&id<773;}
// Cross-stage linkage follows Vulkan interface matching (stage_interface.h), checked before activation.
void validate_stage_link(const SpvReflectShaderModule& producer,const SpvReflectShaderModule& consumer,const char* role){validate_stage_interface(stage_outputs(producer,role),consumer,interface_widening,role);}
void validate_fullscreen_inputs(const SpvReflectShaderModule& consumer,const char* role){if(fullscreen_outputs.empty())throw std::runtime_error(std::string(role)+" needs the stock fullscreen vertex shader");validate_stage_interface(fullscreen_outputs,consumer,interface_widening,role);}
PostStage build_foliage_generation(uint32_t pixel_id,const SevkPostprocessModule* modules,uint32_t count){
    if(pixel_id!=770&&pixel_id!=772)throw std::runtime_error("invalid foliage owner");
    uint32_t ids[]{768,pixel_id-1,pixel_id};VkShaderStageFlagBits stages[]{VK_SHADER_STAGE_VERTEX_BIT,VK_SHADER_STAGE_GEOMETRY_BIT,VK_SHADER_STAGE_FRAGMENT_BIT};
    SpvReflectShaderModule reflected[3]{};VkShaderModule shaders[3]{};PostStage result{};
    try {
        std::vector<VkDescriptorSetLayoutBinding> bindings;
        std::map<uint32_t,std::array<uint32_t,7>> signatures;
        for(uint32_t k=0;k<3;++k){const SevkPostprocessModule* source=nullptr;for(uint32_t i=0;i<count;++i)if(modules[i].stage==ids[k])source=&modules[i];
            if(!source)throw std::runtime_error("incomplete foliage generation closure");
            shaders[k]=world_shader(source->spirv,source->bytes,stages[k],reflected[k]);auto& module=reflected[k];
            if(module.push_constant_block_count)throw std::runtime_error("foliage generation push constants unsupported");
            for(uint32_t i=0;i<module.descriptor_binding_count;++i){auto& b=module.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
                bool extras=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.binding==6&&b.block.size>0&&b.block.size<=320;
                bool reserved=b.binding>=133&&b.binding<=137&&(type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE||type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER||type==VK_DESCRIPTOR_TYPE_UNIFORM_TEXEL_BUFFER);
                bool uniform=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&((b.binding==0&&b.block.size==1280)||(b.binding==1&&b.block.size==64)||(b.binding==2&&b.block.size==128)||(b.binding==4&&b.block.size==4096));
                bool image=type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&b.image.dim==SpvDim2D&&!b.image.ms&&((b.binding==128||b.binding==129)?b.image.arrayed:b.binding==156&&!b.image.arrayed);
                bool sampler=type==VK_DESCRIPTOR_TYPE_SAMPLER&&b.binding>=256&&b.binding<=259;
                if(b.set||b.count!=1||!(extras||reserved||uniform||image||sampler))throw std::runtime_error("foliage generation descriptor contract mismatch");
                uint32_t number=b.type_description&&(b.type_description->type_flags&SPV_REFLECT_TYPE_FLAG_INT)?(b.type_description->traits.numeric.scalar.signedness?1u:2u):0u;
                std::array<uint32_t,7> signature{uint32_t(type),b.block.size,uint32_t(b.image.dim),b.image.arrayed,b.image.depth,b.image.ms,number};
                auto found=signatures.find(b.binding);
                if(found!=signatures.end()){
                    if(found->second!=signature)throw std::runtime_error("foliage shader-stage resource declarations disagree");
                    for(auto& old:bindings)if(old.binding==b.binding)old.stageFlags|=stages[k];
                }else{
                    signatures.emplace(b.binding,signature);
                    if(reserved&&!reflect_catalog_resource(result,b))throw std::runtime_error("foliage catalog resource unsupported");
                    result.bindings.push_back({b.binding,type});bindings.push_back({b.binding,type,1,VkShaderStageFlags(stages[k]),nullptr});
                }
            }
        }
        validate_stage_link(reflected[0],reflected[1],"foliage");validate_stage_link(reflected[1],reflected[2],"foliage");
        std::vector<VkVertexInputAttributeDescription> attributes;
        for(uint32_t i=0;i<reflected[0].input_variable_count;++i){auto& v=*reflected[0].input_variables[i];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;
            std::string name=v.name?v.name:"";name=name.substr(name.find_last_of('.')+1);
            if((name=="POSITION"||name=="POSITION0")&&v.format==SPV_REFLECT_FORMAT_R32G32B32_SFLOAT)attributes.push_back({v.location,0,VK_FORMAT_R32G32B32_SFLOAT,0});
            else if(name=="TEXCOORD0"&&v.format==SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT)attributes.push_back({v.location,0,VK_FORMAT_R8G8B8A8_UNORM,12});
            else throw std::runtime_error("foliage generation vertex input contract mismatch");
        }
        bool outputs[3]{};
        for(uint32_t i=0;i<reflected[2].output_variable_count;++i){auto& v=*reflected[2].output_variables[i];
            bool floating=v.format==SPV_REFLECT_FORMAT_R32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT;
            if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN||v.location>=3||outputs[v.location]||!floating)throw std::runtime_error("foliage generation GBuffer output mismatch");outputs[v.location]=true;}
        for(bool output:outputs)if(!output)throw std::runtime_error("foliage generation GBuffer output incomplete");
        VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=uint32_t(bindings.size());set.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&set,nullptr,&result.layout),"foliage generation descriptors");result.pipeline_layout=world_pipeline_layout(result.layout);
        result.pipeline=world_graphics(shaders[0],"__vertex_shader",shaders[2],"__pixel_shader",result.pipeline_layout,{{0,16,VK_VERTEX_INPUT_RATE_VERTEX}},attributes,stock_formats.data(),3,true,false,false,false,0,false,shaders[1],true);
        for(uint32_t k=0;k<3;++k){spvReflectDestroyShaderModule(&reflected[k]);vkDestroyShaderModule(device,shaders[k],nullptr);}return result;
    }catch(...){for(uint32_t k=0;k<3;++k){if(reflected[k]._internal)spvReflectDestroyShaderModule(&reflected[k]);if(shaders[k])vkDestroyShaderModule(device,shaders[k],nullptr);}post_destroy_stage(result);throw;}
}
