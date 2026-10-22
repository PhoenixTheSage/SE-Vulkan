// Screen-space CSM evaluation and optional per-view usage statistics.
bool native_shadow_id(uint32_t id){return id>=1792&&id<1794;}
PostStage build_shadow_generation(uint32_t id,const void* data,uint64_t bytes){
    bool usage=id==1793;PostStage result{};SpvReflectShaderModule reflected{};VkShaderModule module{};
    try{
        // DXC erases HLSL unorm<float> to R32f. The owning CSM target is R8_UNORM.
        // Adapt only the driver-facing copy; authenticated source/artifacts stay intact.
        if(!data||bytes<20||bytes%4||bytes>16*1024*1024)throw std::runtime_error("invalid shadow SPIR-V size");
        std::vector<uint32_t> executable(bytes/4);std::memcpy(executable.data(),data,size_t(bytes));bool extended=false,adapted=false;
        for(size_t i=5;i<executable.size();){uint32_t length=executable[i]>>16,op=executable[i]&65535;if(!length||length>executable.size()-i)throw std::runtime_error("malformed shadow SPIR-V");
            if(op==SpvOpCapability&&length==2&&executable[i+1]==SpvCapabilityStorageImageExtendedFormats)extended=true;
            if(!usage&&op==SpvOpTypeImage&&length==9&&executable[i+3]==SpvDim2D&&executable[i+7]==2&&executable[i+8]==SpvImageFormatR32f){executable[i+8]=SpvImageFormatR8;adapted=true;}i+=length;}
        if(adapted&&!extended)executable.insert(executable.begin()+5,{uint32_t(2u<<16)|SpvOpCapability,SpvCapabilityStorageImageExtendedFormats});
        module=world_shader(executable.data(),executable.size()*4,VK_SHADER_STAGE_COMPUTE_BIT,reflected);auto work=reflected.entry_points[0].local_size;
        if(work.x!=16||work.y!=16||work.z!=1||reflected.push_constant_block_count)throw std::runtime_error("shadow generation compute interface mismatch");
        std::vector<VkDescriptorSetLayoutBinding> bindings;bool output=false;
        for(uint32_t i=0;i<reflected.descriptor_binding_count;++i){auto& b=reflected.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
            bool catalog=reflect_catalog_resource(result,b);bool extras=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.binding==6&&b.block.size>0&&b.block.size<=320;
            bool uniform=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.block.size>0&&((b.binding==0&&b.block.size<=1280)||(b.binding==4&&b.block.size<=1200)||(usage&&b.binding==2&&b.block.size<=32));
            bool image=type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&(b.binding==128||!usage&&b.binding==144)&&b.image.dim==SpvDim2D&&!b.image.ms&&b.image.arrayed==(b.binding==144);
            bool sampler=type==VK_DESCRIPTOR_TYPE_SAMPLER&&(b.binding==271||b.binding>=256&&b.binding<=259);
            bool target=!usage&&type==VK_DESCRIPTOR_TYPE_STORAGE_IMAGE&&b.binding==384&&b.image.dim==SpvDim2D&&!b.image.arrayed&&!b.image.ms&&b.image.image_format==SpvImageFormatR8;
            bool stats=usage&&type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER&&b.binding==385&&b.block.member_count==1;
            if(stats){auto& member=b.block.members[0];uint32_t stride=member.array.stride;if(!stride&&member.type_description)stride=member.type_description->traits.array.stride;stats=stride==4;}
            if(b.set||b.count!=1||!(catalog||extras||uniform||image||sampler||target||stats))throw std::runtime_error("shadow generation descriptor contract mismatch");
            output|=target||stats;result.bindings.push_back({b.binding,type});bindings.push_back({b.binding,type,1,VK_SHADER_STAGE_COMPUTE_BIT,nullptr});
        }
        if(!output)throw std::runtime_error("shadow generation output missing");
        VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=uint32_t(bindings.size());set.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&set,nullptr,&result.layout),"shadow generation descriptors");result.pipeline_layout=world_pipeline_layout(result.layout);result.compute=true;
        VkComputePipelineCreateInfo pipeline{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};pipeline.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,module,"__compute_shader",nullptr};pipeline.layout=result.pipeline_layout;check(vkCreateComputePipelines(device,pipeline_cache,1,&pipeline,nullptr,&result.pipeline),"shadow generation pipeline");spvReflectDestroyShaderModule(&reflected);vkDestroyShaderModule(device,module,nullptr);return result;
    }catch(...){if(reflected._internal)spvReflectDestroyShaderModule(&reflected);if(module)vkDestroyShaderModule(device,module,nullptr);post_destroy_stage(result);throw;}
}
