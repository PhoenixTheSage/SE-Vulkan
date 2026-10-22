// Frame data is copied before recording. Native owners never retain managed memory.
bool native_binding_owner(uint32_t id){return id<62 || (id>=128&&id<144) || (id>=256&&id<260) || (id>=512&&id<515) || id==770 || id==772 || (id>=1025&&id<1034&&(id%2)) || (id>=1280&&id<1284);}
void clear_stage_bindings(){
    guest.stage_bindings.clear();guest.bindings_frame=UINT32_MAX;
    guest.binding_resolved=guest.binding_missing=guest.binding_mismatched=0;
}
void set_stage_bindings(const SevkStageBindings* rows,uint32_t count){
    static_assert(sizeof(SevkStageBindings)==120);static_assert(sizeof(SevkStageBindingState)==48);
    if(!recording||!guest.resources_open||count>104||(!rows&&count))throw std::runtime_error("native stage bindings require the open frame callback");
    guest_refresh_frame();std::unordered_map<uint32_t,SevkStageBindings> next;
    for(uint32_t i=0;i<count;++i){const auto& r=rows[i];
        if(r.size!=sizeof(r)||r.abi_version!=SEVK_ABI_VERSION||r.reserved||r.frame_index!=guest.frame.frame_index||!native_binding_owner(r.stage)||
            (r.requested_mask&~0x3e0u)||(r.fresh_mask&~(r.requested_mask|1u))||(r.attachment_mask&~r.requested_mask)||(r.attachment_mask&~0x3c0u)||!next.emplace(r.stage,r).second)
            throw std::runtime_error("invalid/duplicate/stale native stage binding row");
        for(uint32_t slot=0;slot<10;++slot)if(r.images[slot]&&(slot<5||!(r.requested_mask&(1u<<slot))))
            throw std::runtime_error("native binding image occupies an unrequested register");

    }
    guest.stage_bindings.swap(next);guest.bindings_frame=guest.frame.frame_index;
    guest.binding_resolved=guest.binding_missing=guest.binding_mismatched=0;
}
void stage_binding_state(SevkStageBindingState* out){
    if(!out||out->size!=sizeof(*out)||out->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("native stage binding state ABI mismatch");
    *out={sizeof(*out),SEVK_ABI_VERSION,uint32_t(null_descriptors),uint32_t(guest.stage_bindings.size()),guest.bindings_frame,0,
        guest.binding_resolved,guest.binding_missing,guest.binding_mismatched};
}
const SevkStageBindings* stage_binding_row(uint32_t id){
    if(guest.bindings_frame!=guest.frame.frame_index)return nullptr;
    auto row=guest.stage_bindings.find(id);return row==guest.stage_bindings.end()?nullptr:&row->second;
}
bool reflect_catalog_resource(PostStage& stage,const SpvReflectDescriptorBinding& b,bool owner_input=false){
    auto type=VkDescriptorType(b.descriptor_type);
    if(b.binding<133||b.binding>137)return false;
    if(type!=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&type!=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER&&type!=VK_DESCRIPTOR_TYPE_UNIFORM_TEXEL_BUFFER)return false;
    if(b.set||b.count!=1||!null_descriptors)throw std::runtime_error("native catalog bindings require one set0 resource and device nullDescriptor support");
    if(type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&b.image.ms)throw std::runtime_error("catalog images are not multisampled");
    uint32_t number=b.type_description&&(b.type_description->type_flags&SPV_REFLECT_TYPE_FLAG_INT)?(b.type_description->traits.numeric.scalar.signedness?1u:2u):0u;
    stage.catalog.push_back({b.binding,uint32_t(b.image.dim),b.image.arrayed,b.image.depth,number,type,owner_input});return true;
}
GuestImage* stage_catalog_image(uint64_t id,bool fresh){
    auto found=guest.images.find(id);
    if(found==guest.images.end()||(!found->second.is_buffer&&found->second.layout!=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)||
        (fresh&&found->second.produced_epoch!=guest.catalog_epoch))return nullptr;
    return &found->second;
}
VkImageView stage_catalog_view(uint64_t id,bool fresh,const NativeCatalogResource& expected,bool& mismatch){
    auto source=stage_catalog_image(id,fresh);
    if(source){
        bool compatible=!source->is_buffer&&expected.number==0&&
            expected.dim==uint32_t(source->depth>1?SpvDim3D:SpvDim2D)&&expected.arrayed==uint32_t(source->layers>1)&&
            (expected.depth!=1||source->aspect==VK_IMAGE_ASPECT_DEPTH_BIT);
        if(!compatible){mismatch=true;return {};}return source->image.view;
    }
    VkImageView builtin{};bool depth=false;
    if(id==kGuestDepth){builtin=world.gbuffer[3].view;depth=true;}
    else if(id>=kGuestG0&&id<=kGuestG2)builtin=world.gbuffer[id-kGuestG0].view;
    else if(id==kGuestMotion)builtin=world.motion.view;
    if(builtin){if(expected.dim!=SpvDim2D||expected.arrayed||expected.number||(expected.depth==1&&!depth)){mismatch=true;return {};}return builtin;}
    if(id>=kGuestAttach0&&id<kGuestAttach0+3){
        auto& target=world.attach[id-kGuestAttach0];
        if(!target.sampled||!target.live||!target.image.view||(fresh&&target.produced_epoch!=guest.catalog_epoch))return {};
        uint32_t number=target.format==4?2u:0u;
        if(expected.dim!=SpvDim2D||expected.arrayed||expected.depth==1||expected.number!=number){mismatch=true;return {};}
        return target.image.view;
    }
    return {};
}
bool write_stage_catalog(VkDescriptorSet set,uint32_t id,const NativeCatalogResource& resource){
    auto row=stage_binding_row(id);uint32_t slot=resource.binding-128;
    uint64_t image=row&&(row->requested_mask&(1u<<slot))?row->images[slot]:0;
    bool fresh=row&&(row->fresh_mask&(1u<<slot)),mismatch=false,resolved=false;
    VkWriteDescriptorSet write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};write.dstSet=set;write.dstBinding=resource.binding;write.descriptorCount=1;write.descriptorType=resource.type;
    VkDescriptorImageInfo texture{{},{},VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
    VkDescriptorBufferInfo buffer{{},0,VK_WHOLE_SIZE};VkBufferView texel{};
    if(resource.type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE){texture.imageView=stage_catalog_view(image,fresh,resource,mismatch);resolved=texture.imageView!=VK_NULL_HANDLE;write.pImageInfo=&texture;}
    else if(resource.type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER){
        auto source=stage_catalog_image(image,fresh);
        if(source&&source->is_buffer){
            int pool=guest_pool_index();auto& storage=source->storage[pool];
            if(source->uploaded[pool]!=source->revision){std::memcpy(storage.mapped,source->pending.data(),source->width);check(vmaFlushAllocation(allocator,storage.memory,0,source->width),"flush catalog frame buffer");source->uploaded[pool]=source->revision;}
            buffer={storage.handle,0,source->width};resolved=true;
        }else if(source)mismatch=true;
        write.pBufferInfo=&buffer;
    }else{
        // Published structured storage is not a DX typed-buffer SRV. Keep a
        // genuine null descriptor until the catalog provides that resource type.
        mismatch=image!=0;write.pTexelBufferView=&texel;
    }
    if(!null_descriptors&&!resolved)throw std::runtime_error("missing native catalog input requires nullDescriptor");
    vkUpdateDescriptorSets(device,1,&write,0,nullptr);
    if(resolved)++guest.binding_resolved;else ++guest.binding_missing;if(mismatch)++guest.binding_mismatched;
    return resolved;
}
void fill_guest_extras(GuestExtras& extras,float width,float height,float scene_w,float scene_h,uint32_t velocity,uint32_t attachments){
    extras.render[0]=width;extras.render[1]=height;extras.scene[0]=scene_w;extras.scene[1]=scene_h;
    extras.inv_render[0]=1.f/width;extras.inv_render[1]=1.f/height;extras.inv_scene[0]=1.f/scene_w;extras.inv_scene[1]=1.f/scene_h;
    extras.camera_pad[0]=guest.frame.far_plane>1.f?guest.frame.far_plane:1000000.f;
    extras.has_velocity=velocity;extras.history_valid=guest.frame.history_valid;extras.attach_count=attachments;extras.frame_index=guest.frame.frame_index;extras.safety=guest.frame.safety_scale;
    extras.air_top=guest.frame.air_top;extras.visual_ceil=guest.frame.visual_ceil;extras.skip_floor=guest.skip_floor;extras.skip_mul=guest.skip_mul;
    std::memcpy(extras.unjittered,guest.frame.view_proj,64);std::memcpy(extras.prev,guest.frame.prev_view_proj,64);std::memcpy(extras.camera,guest.frame.camera_to_world,48);
    extras.camera[3]=guest.frame.camera_to_world[12];extras.camera[7]=guest.frame.camera_to_world[13];extras.camera[11]=guest.frame.camera_to_world[14];
    extras.proj_scale[0]=guest.frame.proj_scale_x;extras.proj_scale[1]=guest.frame.proj_scale_y;
    std::memcpy(extras.sun_color,guest.frame.sun_color,12);extras.sun_diffuse=guest.frame.sun_diffuse;
    std::memcpy(extras.sun_toward,guest.frame.sun_toward,12);extras.sky_luma=guest.frame.sky_luma;std::memcpy(extras.sky_ambient,guest.frame.sky_ambient,12);
}
void write_stage_extras(VkDescriptorSet set,uint32_t id){
    guest_refresh_frame();guest_ensure_samplers();auto row=stage_binding_row(id);
    NativeCatalogResource image{133,SpvDim2D,0,0,0,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE};bool mismatch=false;
    uint32_t velocity=row&&stage_catalog_view(row->velocity,(row->fresh_mask&1u)!=0,image,mismatch)?1u:0u,attachments=0;
    if(row)for(uint32_t slot=5;slot<10;++slot)if(row->attachment_mask&(1u<<slot)){
        uint64_t id=row->images[slot];bool fresh=(row->fresh_mask&(1u<<slot))!=0;
        auto source=stage_catalog_image(id,fresh);
        bool available=source&&!source->is_buffer&&source->image.view;
        if(id>=kGuestAttach0&&id<kGuestAttach0+3){auto& target=world.attach[id-kGuestAttach0];available=target.live&&target.sampled&&target.image.view&&(!fresh||target.produced_epoch==guest.catalog_epoch);}
        if(available)++attachments;
    }
    float width=float(std::max(1u,guest.frame.width)),height=float(std::max(1u,guest.frame.height));
    GuestExtras extras{};fill_guest_extras(extras,width,height,width,height,velocity,attachments);
    int pool=guest_pool_index();guest_pool(pool);auto offset=guest_uniform(pool,&extras,sizeof(extras));if(offset==~0u)throw std::runtime_error("native extras uniform arena exhausted");
    VkDescriptorBufferInfo info{guest.bumps[pool].buffer.handle,offset,sizeof(extras)};VkWriteDescriptorSet write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};write.dstSet=set;write.dstBinding=6;write.descriptorCount=1;write.descriptorType=VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;write.pBufferInfo=&info;vkUpdateDescriptorSets(device,1,&write,0,nullptr);
}
