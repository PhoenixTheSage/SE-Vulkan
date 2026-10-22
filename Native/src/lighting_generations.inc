// Lighting shares the captured native generation transaction with postprocessing.
// Descriptor layouts remain renderer-owned; detached bundles own only pipelines.
bool native_lighting_id(uint32_t id){return id>=256&&id<260;}
void validate_lighting_generation(const SpvReflectShaderModule& r,uint32_t kind){
    if(r.push_constant_block_count)throw std::runtime_error("lighting replacement push constants are unsupported");
    for(uint32_t i=0;i<r.descriptor_binding_count;++i){auto& b=r.descriptor_bindings[i];
        bool uniform=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&((b.binding==0&&b.block.size==1280)||(kind>=3&&b.binding==1&&b.block.size==208));
        bool image=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&(b.binding==128||b.binding==129||b.binding==130||b.binding==131||b.binding==138||b.binding==139||b.binding==140||b.binding==143||b.binding==145||b.binding==147||(kind>=3&&(b.binding==141||(kind==4&&b.binding==142))));
        if(image){bool cube=b.binding==138||b.binding==139||b.binding==145;image=b.image.dim==(cube?SpvDimCube:SpvDim2D)&&!b.image.arrayed&&!b.image.ms;}
        bool buffer=kind==2&&b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_STORAGE_BUFFER&&(b.binding==141||b.binding==142);
        bool sampler=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER&&(b.binding==256||b.binding==258||b.binding==259||(kind==4&&b.binding==271));
        if(b.set||b.count!=1||!(uniform||image||buffer||sampler))throw std::runtime_error("lighting replacement exceeds the owner resource contract");
    }
    bool output=false;
    for(uint32_t i=0;i<r.output_variable_count;++i){auto& v=*r.output_variables[i];
        bool floating=v.format==SPV_REFLECT_FORMAT_R32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32_SFLOAT||v.format==SPV_REFLECT_FORMAT_R32G32B32A32_SFLOAT;
        if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN||v.location||output||!floating)throw std::runtime_error("lighting replacement output contract mismatch");output=true;
    }
    if(!output)throw std::runtime_error("lighting replacement output is missing");
    for(uint32_t i=0;i<r.input_variable_count;++i){auto& v=*r.input_variables[i];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;
        if(v.location||v.format!=SPV_REFLECT_FORMAT_R32G32_SFLOAT||v.decoration_flags&SPV_REFLECT_DECORATION_FLAT)throw std::runtime_error("lighting replacement fullscreen input mismatch");}
}
PostStage build_lighting_generation(uint32_t id,const void* data,uint64_t bytes){
    uint32_t kind=id-255;if(!native_lighting_id(id)||!world.fullscreen||!world.lights[kind-1])throw std::runtime_error("lighting owner is unavailable");
    PostStage result{};SpvReflectShaderModule reflected{};VkShaderModule module{};
    try{
        module=world_shader(data,bytes,VK_SHADER_STAGE_FRAGMENT_BIT,reflected);validate_lighting_generation(reflected,kind);
        auto layout=kind>=3?world.spot_pipeline_layout:world.light_pipeline_layout;
        hdr_pipeline_pair(result.pipeline,result.other_hdr,[&](VkFormat format){return world_graphics(world.fullscreen,"__vertex_shader",module,"__pixel_shader",layout,{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,&format,1,false,false,true);});
        spvReflectDestroyShaderModule(&reflected);vkDestroyShaderModule(device,module,nullptr);return result;
    }catch(...){if(reflected._internal)spvReflectDestroyShaderModule(&reflected);if(module)vkDestroyShaderModule(device,module,nullptr);post_destroy_stage(result);throw;}
}
