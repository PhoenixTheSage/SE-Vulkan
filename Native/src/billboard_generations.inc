// Stock billboard owners from Transparent/Billboards.hlsl. Variant k (0..31) is the VS/PS pair 2560+2*k/2561+2*k,
// and 2624 is the LDR vertex shader that the even additive variants use for their LDR and PostPP output programs.
// One source file serves every variant, so the 65 modules activate as one closure.
bool native_billboard_id(uint32_t id){return id>=2560&&id<2625;}
bool billboard_generation_available(uint32_t id){if(id<2624)return transparency.billboards[(id-2560)/2]!=VK_NULL_HANDLE;for(uint32_t k=8;k<32;++k)if((k&8)&&!(k&1)&&transparency.output_billboards[k].vs)return true;return false;}
const PostStage* billboard_generation(uint32_t k){auto& stage=post.billboards[2*k+1];return stage.generation?&stage:nullptr;}
PostStage build_billboard_generation(uint32_t k,const SevkPostprocessModule* modules,uint32_t count){
    bool additive=(k&8)!=0,ldr_vertex=additive&&!(k&1);uint32_t ids[]{2560+2*k,2561+2*k,2624};uint32_t used=ldr_vertex?3:2;
    SpvReflectShaderModule reflected[3]{};VkShaderModule shaders[3]{};PostStage result{};
    try {
        std::vector<VkDescriptorSetLayoutBinding> bindings;std::map<uint32_t,std::array<uint32_t,7>> signatures;
        for(uint32_t m=0;m<used;++m){const SevkPostprocessModule* source=nullptr;for(uint32_t i=0;i<count;++i)if(modules[i].stage==ids[m])source=&modules[i];
            if(!source)throw std::runtime_error("incomplete billboard generation closure");auto shader_stage=m==1?VK_SHADER_STAGE_FRAGMENT_BIT:VK_SHADER_STAGE_VERTEX_BIT;
            shaders[m]=world_shader(source->spirv,source->bytes,shader_stage,reflected[m]);auto& module=reflected[m];if(module.push_constant_block_count)throw std::runtime_error("billboard generation push constants unsupported");
            for(uint32_t i=0;i<module.descriptor_binding_count;++i){auto& b=module.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
                // The stock billboard contract: no named stage carries reserved catalog bindings or extras.
                bool uniform=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&((b.binding==0&&b.block.size==1280)||(b.binding==2&&b.block.size==2048)||(b.binding==4&&b.block.size==1200));
                bool image=type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&(b.binding==128||b.binding==129||b.binding==139||b.binding==144)&&!b.image.ms&&b.image.dim==(b.binding==139?SpvDimCube:SpvDim2D)&&b.image.arrayed==(b.binding==144);
                bool storage=type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER&&b.binding==158;
                bool sampler=type==VK_DESCRIPTOR_TYPE_SAMPLER&&(b.binding==258||b.binding==259||b.binding==271);
                if(b.set||b.count!=1||!(uniform||image||storage||sampler))throw std::runtime_error("billboard generation descriptor contract mismatch: "+std::to_string(b.binding));
                std::array<uint32_t,7> signature{uint32_t(type),b.block.size,uint32_t(b.image.dim),b.image.arrayed,b.image.depth,b.image.ms,0};auto found=signatures.find(b.binding);
                if(found!=signatures.end()){if(found->second!=signature)throw std::runtime_error("billboard shader-stage resource declarations disagree");for(auto& old:bindings)if(old.binding==b.binding)old.stageFlags|=shader_stage;}
                else {signatures.emplace(b.binding,signature);result.bindings.push_back({b.binding,type});bindings.push_back({b.binding,type,1,VkShaderStageFlags(shader_stage),nullptr});}
            }
        }
        validate_stage_link(reflected[0],reflected[1],"billboard");if(ldr_vertex)validate_stage_link(reflected[2],reflected[1],"billboard");
        for(uint32_t i=0;i<reflected[1].output_variable_count;++i){auto& v=*reflected[1].output_variables[i];if(!(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)&&v.location>=(additive?1u:2u))throw std::runtime_error("billboard generation output mismatch");}
        VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=uint32_t(bindings.size());set.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&set,nullptr,&result.layout),"billboard generation descriptors");result.pipeline_layout=world_pipeline_layout(result.layout);
        // Slots: HDR, other HDR, top, other-HDR top, LDR. Additive variants own all five and a PostPP output program.
        result.pipelines.assign(5,VK_NULL_HANDLE);auto attributes=billboard_attributes(reflected[0]);
        auto hdr=[&](bool depth_test,uint32_t at){hdr_pipeline_pair(result.pipelines[at],result.pipelines[at+1],[&](VkFormat format){return build_billboard_pipeline(shaders[0],shaders[1],result.pipeline_layout,attributes,k,false,format,depth_test);});};
        if(!additive)result.pipelines[0]=build_billboard_pipeline(shaders[0],shaders[1],result.pipeline_layout,attributes,k,false,VK_FORMAT_R16G16B16A16_SFLOAT);
        else{hdr(true,0);hdr(false,2);uint32_t vertex=ldr_vertex?2:0;result.attributes=billboard_attributes(reflected[vertex]);
            result.pipelines[4]=build_billboard_pipeline(shaders[vertex],shaders[1],result.pipeline_layout,result.attributes,k,true,VK_FORMAT_R8G8B8A8_SRGB);
            // The PostPP output program keeps its modules; its pipeline is built at the output format on first use.
            result.modules={shaders[vertex],shaders[1]};shaders[vertex]=shaders[1]=VK_NULL_HANDLE;}
        for(uint32_t m=0;m<used;++m){spvReflectDestroyShaderModule(&reflected[m]);if(shaders[m])vkDestroyShaderModule(device,shaders[m],nullptr);}return result;
    }catch(...){for(uint32_t m=0;m<3;++m){if(reflected[m]._internal)spvReflectDestroyShaderModule(&reflected[m]);if(shaders[m])vkDestroyShaderModule(device,shaders[m],nullptr);}post_destroy_stage(result);throw;}
}
// Live pipelines stay in the transparency slots; the stage keeps the generation's layout and bindings.
void billboard_activate(uint32_t id,PostStage& entry){
    uint32_t index=id-2560;PostStage retired=std::move(post.billboards[index]);
    if(id<2624&&(index&1)){uint32_t k=index/2;VkPipeline* live[]{&transparency.billboards[k],&transparency.other_hdr_billboards[k],&transparency.top_billboards[k],&transparency.other_hdr_top_billboards[k],&transparency.ldr_billboards[k]};
        retired.pipelines.assign(5,VK_NULL_HANDLE);for(uint32_t i=0;i<5;++i){retired.pipelines[i]=*live[i];*live[i]=entry.pipelines[i];}entry.pipelines.clear();
        if(k&8){auto& output=transparency.output_billboards[k];retired.pipelines.push_back(output.pipeline);retired.pipelines.push_back(output.ldr_hdr);output.ldr_hdr=VK_NULL_HANDLE;retired.modules={output.vs,output.ps};
            output.vs=entry.modules[0];output.ps=entry.modules[1];output.attributes=std::move(entry.attributes);output.layout=entry.pipeline_layout;output.pipeline=VK_NULL_HANDLE;output.format=VK_FORMAT_UNDEFINED;entry.modules.clear();}}
    post.retired.emplace_back(next_completion,std::move(retired));post.billboards[index]=std::move(entry);
}
// Writes one reflected owner binding with the stock billboard resource it names.
void write_billboard_owner(VkDescriptorSet set,WorldSlot& slot,const PostStage& stage,const SevkBillboardDraw& d,bool output){
    for(auto [binding,type]:stage.bindings){
        if(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER){if(binding==0)world_write_buffer(set,0,output?jitter_unjittered_frame(slot):slot.frame,1280,type);else if(binding==2)world_write_buffer(set,2,transparency.billboard_projections[slot_index],2048,type);else world_write_buffer(set,4,transparency.shadow_constants[slot_index],1200,type);}
        else if(type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER)world_write_buffer(set,158,transparency.billboard_data[slot_index],transparency.billboard_data[slot_index].bytes,type);
        else if(type==VK_DESCRIPTOR_TYPE_SAMPLER)world_write_image(set,binding,{},binding==271?shadow_sampler:standard_sampler(binding),true);
        else if(binding==129){VkDescriptorImageInfo image{{},output?transparency.output_depth.view:world.gbuffer[3].view,output?VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL};VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstSet=set;w.dstBinding=129;w.descriptorCount=1;w.descriptorType=type;w.pImageInfo=&image;vkUpdateDescriptorSets(device,1,&w,0,nullptr);}
        else world_write_image(set,binding,binding==128?textures.at(d.texture).image.view:binding==139?(probes.ready?probes.final[0].image.view:world.ambient.view):transparency.csm.view);
    }
}
void billboard_note_execution(uint32_t k,bool ldr){post_note_execution(2560+2*k);post_note_execution(2561+2*k);if(ldr&&!(k&1))post_note_execution(2624);}
