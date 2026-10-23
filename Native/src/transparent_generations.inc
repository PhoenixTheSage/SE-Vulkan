// Stock glass/holo owners. 2304+2*i is owner i's VS and 2305+2*i its PS: i<10 draws
// Geometry/Passes/Transparent into OIT, i>=10 the TransparentForDecals depth layers of pipeline i-10.
bool native_transparent_id(uint32_t id){return id>=2304&&id<2344;}
bool transparent_generation_available(uint32_t id){uint32_t i=(id-2304)/2;return (i<10?transparency.glass[i]:transparency.glass_depth[i-10]).handle!=VK_NULL_HANDLE;}
const PostStage* transparent_generation(uint32_t owner){auto& stage=post.transparent[owner-2304];return stage.generation?&stage:nullptr;}
PostStage build_transparent_generation(uint32_t pixel_id,const SevkPostprocessModule* modules,uint32_t count){
    uint32_t owner=(pixel_id-2304)/2;bool for_decals=owner>=10;uint32_t id=for_decals?owner-10:owner,object_bytes=id%5==1?3968:128;
    SpvReflectShaderModule reflected[2]{};VkShaderModule shaders[2]{};PostStage result{};
    try {
        std::vector<VkDescriptorSetLayoutBinding> bindings;std::map<uint32_t,std::array<uint32_t,7>> signatures;
        for(uint32_t k=0;k<2;++k){const SevkPostprocessModule* source=nullptr;for(uint32_t i=0;i<count;++i)if(modules[i].stage==pixel_id-1+k)source=&modules[i];
            if(!source)throw std::runtime_error("incomplete transparent generation pair");auto shader_stage=k?VK_SHADER_STAGE_FRAGMENT_BIT:VK_SHADER_STAGE_VERTEX_BIT;
            shaders[k]=world_shader(source->spirv,source->bytes,shader_stage,reflected[k]);auto& module=reflected[k];
            for(uint32_t i=0;i<module.descriptor_binding_count;++i){auto& b=module.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
                // The stock glass contract, plus pack extras (b6) and reserved catalog resources.
                bool extras=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&b.binding==6&&b.block.size>0&&b.block.size<=320;
                bool reserved=b.binding>=133&&b.binding<=137&&(type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE||type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER||type==VK_DESCRIPTOR_TYPE_UNIFORM_TEXEL_BUFFER);
                bool uniform=type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&((b.binding==0&&b.block.size==1280)||(b.binding==1&&b.block.size==64)||(b.binding==2&&b.block.size==object_bytes)||(b.binding==3&&b.block.size==96)||(b.binding==4&&b.block.size==1200));
                bool image=type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&(b.binding==128||b.binding==129||b.binding==130||b.binding==139||b.binding==143||b.binding==144||b.binding==145||b.binding==156)&&!b.image.ms&&b.image.dim==(b.binding==139||b.binding==145?SpvDimCube:SpvDim2D)&&b.image.arrayed==(b.binding==144);
                bool sampler=type==VK_DESCRIPTOR_TYPE_SAMPLER&&((b.binding>=256&&b.binding<=259)||b.binding==271);
                if(b.set||b.count!=1||!(extras||reserved||uniform||image||sampler))throw std::runtime_error("transparent generation descriptor contract mismatch: "+std::to_string(b.binding));
                uint32_t number=b.type_description&&(b.type_description->type_flags&SPV_REFLECT_TYPE_FLAG_INT)?(b.type_description->traits.numeric.scalar.signedness?1u:2u):0u;
                std::array<uint32_t,7> signature{uint32_t(type),b.block.size,uint32_t(b.image.dim),b.image.arrayed,b.image.depth,b.image.ms,number};auto found=signatures.find(b.binding);
                if(found!=signatures.end()){if(found->second!=signature)throw std::runtime_error("transparent shader-stage resource declarations disagree");for(auto& old:bindings)if(old.binding==b.binding)old.stageFlags|=shader_stage;}
                else {signatures.emplace(b.binding,signature);if(reserved&&!reflect_catalog_resource(result,b,image))throw std::runtime_error("transparent catalog resource unsupported");
                    if((b.binding==2||b.binding==3)&&type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER)type=VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC;
                    result.bindings.push_back({b.binding,type});bindings.push_back({b.binding,type,1,VkShaderStageFlags(shader_stage),nullptr});}
            }
        }
        validate_foliage_link(reflected[0],reflected[1]);
        uint32_t targets=for_decals?1u:2u;
        for(uint32_t i=0;i<reflected[1].output_variable_count;++i){auto& v=*reflected[1].output_variables[i];if(!(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)&&v.location>=targets)throw std::runtime_error("transparent generation output mismatch");}
        // Dynamic offsets follow binding order.
        std::sort(result.bindings.begin(),result.bindings.end());
        VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=uint32_t(bindings.size());set.pBindings=bindings.data();check(vkCreateDescriptorSetLayout(device,&set,nullptr,&result.layout),"transparent generation descriptors");result.pipeline_layout=world_pipeline_layout(result.layout);
        build_glass_pipelines(id,for_decals,shaders[0],reflected[0],shaders[1],reflected[1],result.pipeline_layout,result.pipeline,result.other_hdr,result.geometry.handle);
        for(uint32_t k=0;k<2;++k){spvReflectDestroyShaderModule(&reflected[k]);vkDestroyShaderModule(device,shaders[k],nullptr);}return result;
    }catch(...){for(uint32_t k=0;k<2;++k){if(reflected[k]._internal)spvReflectDestroyShaderModule(&reflected[k]);if(shaders[k])vkDestroyShaderModule(device,shaders[k],nullptr);}post_destroy_stage(result);throw;}
}
// Live pipelines stay in the transparency slots; the stage keeps the generation's layout and bindings.
void transparent_activate(uint32_t id,PostStage& entry){
    uint32_t index=id-2304;PostStage retired=std::move(post.transparent[index]);
    if(index%2){uint32_t i=index/2;bool depth=i>=10;auto& open=depth?transparency.glass_depth[i-10]:transparency.glass[i];auto& culled=depth?transparency.glass_depth_culled[i-10]:transparency.glass_culled[i];
        retired.pipeline=open.handle;retired.other_hdr=culled.handle;open.handle=entry.pipeline;culled.handle=entry.other_hdr;
        if(depth){auto& old=transparency.glass_depth_old[i-10];retired.geometry.handle=old.handle;old.handle=entry.geometry.handle;}}
    post.retired.emplace_back(next_completion,std::move(retired));post.transparent[index]=std::move(entry);
    auto& kept=post.transparent[index];kept.pipeline=kept.other_hdr=VK_NULL_HANDLE;kept.geometry.handle=VK_NULL_HANDLE;
}
// Writes one reflected owner binding with the stock glass resource it names.
void write_glass_owner(VkDescriptorSet set,WorldSlot& slot,uint32_t owner,const PostStage& stage,uint32_t binding,VkDescriptorType type,const SevkWorldDraw& g,VkImageView depth){
    auto catalog=std::find_if(stage.catalog.begin(),stage.catalog.end(),[&](auto r){return r.binding==binding;});
    if(catalog!=stage.catalog.end()){write_stage_catalog(set,owner,*catalog);return;}
    if(binding==6){write_stage_extras(set,owner);return;}
    if(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER||type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC){
        if(binding==0)world_write_buffer(set,0,slot.frame,1280,type);else if(binding==1)world_write_buffer(set,1,slot.projections,64,type);
        else if(binding==2)world_write_buffer(set,2,transparency.objects[slot_index],g.object_bytes,type);else if(binding==3)world_write_buffer(set,3,transparency.materials[slot_index],96,type);
        else world_write_buffer(set,4,transparency.shadow_constants[slot_index],1200,type);
    }else if(type==VK_DESCRIPTOR_TYPE_SAMPLER)world_write_image(set,binding,{},binding==271?shadow_sampler:binding==257&&depth?transparency.point_sampler:environment_generation_sampler(binding),true);
    else{VkImageView view=binding==128?(depth?depth:textures.at(g.color).image.view):binding==129?textures.at(g.normal).image.view:binding==130?textures.at(g.color).image.view:
        binding==139?(probes.ready?probes.final[0].image.view:world.ambient.view):binding==145?(probes.ready?probes.final[1].image.view:world.ambient.view):binding==143?textures.at(probes.ready?probes.config.brdf:0).image.view:binding==144?transparency.csm.view:stock.dither.view;
        world_write_image(set,binding,view);}
}
// Binds an owner generation's set for one glass draw and returns false for the stock path.
bool bind_glass_owner(VkCommandBuffer cmd,WorldSlot& slot,uint32_t owner,const SevkWorldDraw& g,uint32_t object_offset,uint32_t material_offset,VkImageView depth={}){
    auto* stage=transparent_generation(owner);if(!stage)return false;
    auto set=world_set(slot,stage->layout);std::vector<uint32_t> dynamic;
    for(auto [binding,type]:stage->bindings){write_glass_owner(set,slot,owner,*stage,binding,type,g,depth);if(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC)dynamic.push_back(binding==2?object_offset:material_offset);}
    vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,stage->pipeline_layout,0,1,&set,uint32_t(dynamic.size()),dynamic.data());post_note_execution(owner-1);post_note_execution(owner);return true;
}
