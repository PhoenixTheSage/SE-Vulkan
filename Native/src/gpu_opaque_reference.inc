// Owner-only verification lane. No WorldDraw packets or CPU selected groups.
struct OpaqueReferenceControl {SevkOpaqueFrame frame;uint32_t actor_count,family_count,part_count,view_count,reference_capacity,draw_capacity,stage,batch,distance_fade,padding;};
static_assert(sizeof(SevkOpaqueActor)==320&&sizeof(SevkOpaqueFamily)==80&&sizeof(SevkOpaquePart)==196);
static_assert(sizeof(SevkOpaqueView)==296&&sizeof(SevkOpaqueFrame)==80&&sizeof(SevkOpaqueSelector)==56);
static_assert(sizeof(SevkOpaqueReference)==80&&sizeof(SevkOpaqueSelection)==16&&sizeof(OpaqueReferenceControl)==120);
void cleanup_opaque_reference(){
    auto& s=opaque_reference;
    for(auto& slot:s.slots)for(auto* b:{&slot.actors,&slot.views,&slot.selections,&slot.groups,&slot.references,&slot.args,&slot.draws,&slot.counts,&slot.stock_objects,&slot.stock_colour,&slot.stock_depth,&slot.stock_draws,&slot.scan,&slot.object_inputs,&slot.parts})destroy_buffer(*b);
    for(auto* b:{&s.families,&s.parts,&s.sources,&s.vertices,&s.indices,&s.selectors,&s.spot_signatures,&s.role_parts})destroy_buffer(*b);
    if(s.compute_timing)vkDestroyQueryPool(device,s.compute_timing,nullptr);
    if(s.compute)vkDestroyPipeline(device,s.compute,nullptr);if(s.graphics)vkDestroyPipeline(device,s.graphics,nullptr);
    if(s.depth_pipeline)vkDestroyPipeline(device,s.depth_pipeline,nullptr);for(auto& d:s.depths)destroy_image(d.image);
    for(auto module:s.modules)if(module)vkDestroyShaderModule(device,module,nullptr);
    if(s.pool)vkDestroyDescriptorPool(device,s.pool,nullptr);if(s.layout)vkDestroyPipelineLayout(device,s.layout,nullptr);
    if(s.descriptor_layout)vkDestroyDescriptorSetLayout(device,s.descriptor_layout,nullptr);s={};
}
void validate_opaque_reference_actor(const SevkOpaqueActor& a,bool validate_source=true,bool committed_pose=false){
    auto& s=opaque_reference;
    if(!a.owner||!a.generation||a.appearance_count>gpu_opaque::appearance_limit||a.family>=s.family_inputs.size()||(a.flags&~255u)||((a.flags&32)&&!(a.flags&2))||!a.source_count||
        ((a.flags&2)&&uint64_t(a.source_first)+a.source_count>s.source_count)||(!(a.flags&2)&&a.source_count!=1))throw std::runtime_error("invalid opaque actor identity/source range");
    if(!committed_pose){for(uint32_t i=0;i<3;i++)if(!std::isfinite(a.translation[i])||std::abs(a.translation[i])>1e12||!std::isfinite(a.minimum[i])||std::abs(a.minimum[i])>1e12||!std::isfinite(a.maximum[i])||std::abs(a.maximum[i])>1e12||a.minimum[i]>a.maximum[i]||
        !std::isfinite(a.local_minimum[i])||!std::isfinite(a.local_maximum[i])||a.local_minimum[i]>a.local_maximum[i])throw std::runtime_error("invalid opaque bounds");
    for(auto value:a.inverse)if(!std::isfinite(value))throw std::runtime_error("invalid opaque parent inverse");
    for(auto value:a.basis)if(!std::isfinite(value))throw std::runtime_error("invalid opaque basis");
    for(auto value:a.colour)if(!std::isfinite(value))throw std::runtime_error("invalid opaque appearance");
    if(!std::isfinite(a.scale))throw std::runtime_error("invalid opaque scale");}
    if(validate_source&&(a.flags&32))for(uint32_t i=a.source_first;i<a.source_first+a.source_count;i++){
        auto* row=s.source_inputs.data()+uint64_t(i)*64;float packed[4];std::memcpy(packed,row+32,16);
        // Only the standalone numerical reference lacks bone deformation.
        // Shared stock geometry binds the authenticated deformed vertex shader.
        if((row[15]&&!s.stock_geometry)||!std::isfinite(packed[3])||packed[3]<0||packed[3]>35||std::floor(packed[3])!=packed[3])throw std::runtime_error("opaque cube is deformed or has invalid orientation");
        uint32_t forward=uint32_t(packed[3])/6,up=uint32_t(packed[3])%6;if(forward/2==up/2)throw std::runtime_error("opaque cube has parallel axes");
        for(uint32_t k=0;k<3;k++)if(!std::isfinite(packed[k]))throw std::runtime_error("opaque cube translation nonfinite");
    }
}
void validate_opaque_reference_capacity(const std::vector<SevkOpaqueActor>& actors){
    GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::Capacity);
    auto& s=opaque_reference;uint64_t refs=0,draws=s.part_count*2ull;std::unordered_map<uint32_t,bool> kinds;std::vector<uint32_t> old_counts(s.family_inputs.size());
    for(auto& a:actors){bool old=(a.flags&2)!=0;auto [it,added]=kinds.emplace(a.family,old);
        if(!added&&it->second!=old)throw std::runtime_error("opaque family mixes OLD/NEW contracts");
        uint64_t maximum=s.family_inputs[a.family].part_count*2ull;refs+=maximum;if(old){draws+=maximum;++old_counts[a.family];}
    }
    std::vector<std::pair<uint32_t,uint32_t>> maxima(s.family_inputs.size());for(uint32_t f=0;f<s.family_inputs.size();f++){auto& family=s.family_inputs[f];for(uint32_t lod=0;lod<=family.threshold_count;lod++){uint32_t colour=0,depth=0;for(uint32_t p=family.first_part;p<family.first_part+family.part_count;p++)if(s.part_inputs[p].lod==lod){colour+=bool(s.part_inputs[p].flags&2);depth+=bool(s.part_inputs[p].flags&1);}maxima[f].first=std::max(maxima[f].first,colour);maxima[f].second=std::max(maxima[f].second,depth);}}
    uint64_t colour=0,depth=0,forward=0;for(auto& actor:actors){auto m=maxima[actor.family];colour+=m.first*2ull;depth+=m.second*((actor.flags&2)?2ull:1ull);forward+=m.first*((actor.flags&2)?2ull:1ull);}
    if(std::max({colour,depth,forward})>s.reference_capacity||draws>s.draw_capacity)throw std::runtime_error("opaque worst-case output capacity exceeded");
    if(!s.ranges.empty()){std::vector<uint64_t> required(s.batch_count);for(auto& part:s.part_inputs){required[part.reserved]+=2;if(gpu_resident_deltas)required[part.reserved]+=2ull*old_counts[part.family];else for(auto& actor:actors)if((actor.flags&2)&&actor.family==part.family)required[part.reserved]+=2;}for(uint32_t b=0;b<s.batch_count;b++)if(required[b]>s.ranges[b].capacity)throw std::runtime_error("opaque commit exceeds immutable batch capacity");}
    s.reference_colour_bound=colour;s.reference_depth_bound=depth;s.reference_forward_bound=forward;

}
Buffer opaque_reference_buffer(uint64_t bytes,VkBufferUsageFlags usage,const void* data=nullptr,bool gpu_working=false){
    if(bytes>gpu_opaque::storage_range)throw std::runtime_error("opaque buffer limit");
    bool local=std::getenv("SE_VULKAN_GPU_OPAQUE_DEVICE_LOCAL")&&std::string(std::getenv("SE_VULKAN_GPU_OPAQUE_DEVICE_LOCAL"))=="1";auto b=make_buffer(std::max(bytes,uint64_t(4)),usage|VK_BUFFER_USAGE_TRANSFER_SRC_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT,!(local&&gpu_working),true,local);
    try{if(b.mapped){if(data)std::memcpy(b.mapped,data,size_t(bytes));else std::memset(b.mapped,0,size_t(b.bytes));check(vmaFlushAllocation(allocator,b.memory,0,b.bytes),"flush opaque immutable input");}else{if(data)throw std::runtime_error("GPU working buffer initialization must be explicit");immediate([&](VkCommandBuffer cmd){vkCmdFillBuffer(cmd,b.handle,0,b.bytes,0);VkMemoryBarrier2 m{VK_STRUCTURE_TYPE_MEMORY_BARRIER_2};m.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;m.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;m.dstStageMask=VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT|VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT|VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_DRAW_INDIRECT_BIT;m.dstAccessMask=VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT|VK_ACCESS_2_UNIFORM_READ_BIT|VK_ACCESS_2_INDIRECT_COMMAND_READ_BIT;VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.memoryBarrierCount=1;d.pMemoryBarriers=&m;vkCmdPipelineBarrier2(cmd,&d);});}}catch(...){destroy_buffer(b);throw;}return b;
}
void opaque_reference_read(Buffer& buffer,void* output,uint64_t bytes){
    if(bytes>buffer.bytes)throw std::runtime_error("opaque verification range");if(buffer.mapped){check(vmaInvalidateAllocation(allocator,buffer.memory,0,bytes),"invalidate opaque verification");std::memcpy(output,buffer.mapped,bytes);return;}
    auto staging=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);try{immediate([&](VkCommandBuffer cmd){VkMemoryBarrier2 m{VK_STRUCTURE_TYPE_MEMORY_BARRIER_2};m.srcStageMask=VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;m.srcAccessMask=VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;m.dstStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;m.dstAccessMask=VK_ACCESS_2_TRANSFER_READ_BIT;VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.memoryBarrierCount=1;d.pMemoryBarriers=&m;vkCmdPipelineBarrier2(cmd,&d);VkBufferCopy copy{0,0,bytes};vkCmdCopyBuffer(cmd,buffer.handle,staging.handle,1,&copy);m.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;m.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;m.dstStageMask=VK_PIPELINE_STAGE_2_HOST_BIT;m.dstAccessMask=VK_ACCESS_2_HOST_READ_BIT;vkCmdPipelineBarrier2(cmd,&d);});check(vmaInvalidateAllocation(allocator,staging.memory,0,bytes),"invalidate explicit opaque readback");std::memcpy(output,staging.mapped,bytes);}catch(...){destroy_buffer(staging);throw;}destroy_buffer(staging);
}
void opaque_reference_placeholder(Buffer& buffer){
    // MyRenderPassUtils.GetPlaceholderObjectCB: NEW draws share an identity
    // object, not the first resident actor's OLD compatibility constants.
    std::array<uint32_t,32> placeholder{};
    for(uint32_t word:{8u,13u,18u,24u,25u,26u})placeholder[word]=std::bit_cast<uint32_t>(1.f);
    placeholder[23]=255;const void* data=placeholder.data();
    if(buffer.mapped){std::memcpy(buffer.mapped,data,128);check(vmaFlushAllocation(allocator,buffer.memory,0,128),"flush NEW stock object placeholder");return;}
    auto staging=make_buffer(128,VK_BUFFER_USAGE_TRANSFER_SRC_BIT,true);std::memcpy(staging.mapped,data,128);check(vmaFlushAllocation(allocator,staging.memory,0,128),"flush stock placeholder staging");try{immediate([&](VkCommandBuffer cmd){VkBufferCopy copy{0,0,128};vkCmdCopyBuffer(cmd,staging.handle,buffer.handle,1,&copy);VkMemoryBarrier2 m{VK_STRUCTURE_TYPE_MEMORY_BARRIER_2};m.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;m.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;m.dstStageMask=VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT|VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT|VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;m.dstAccessMask=VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT|VK_ACCESS_2_UNIFORM_READ_BIT;VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.memoryBarrierCount=1;d.pMemoryBarriers=&m;vkCmdPipelineBarrier2(cmd,&d);});}catch(...){destroy_buffer(staging);throw;}destroy_buffer(staging);
}
VkShaderModule opaque_reference_shader(const char* name){
    auto path=std::filesystem::path(shader_directory)/(std::string(name)+".spv");std::ifstream file(path,std::ios::binary|std::ios::ate);
    if(!file||file.tellg()<20||file.tellg()>16*1024*1024||file.tellg()%4)throw std::runtime_error("opaque shader missing");
    std::vector<uint32_t> words(size_t(file.tellg())/4);file.seekg(0);file.read(reinterpret_cast<char*>(words.data()),words.size()*4);
    VkShaderModuleCreateInfo create{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};create.codeSize=words.size()*4;create.pCode=words.data();
    VkShaderModule result{};check(vkCreateShaderModule(device,&create,nullptr,&result),"create admitted opaque shader");return result;
}
void init_opaque_reference(const SevkOpaqueReferenceConfig& c,bool shared_geometry=false){
    GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::Initialize);
    if(frame_open()||batching_uploads||!opaque_caps.enabled||c.size!=sizeof(c)||c.abi_version!=SEVK_ABI_VERSION||
        !c.actor_count||c.actor_count>8192||!c.family_count||c.family_count>8192||!c.part_count||c.part_count>gpu_opaque::max_parts||!c.view_count||c.view_count>64||
        !c.actors||!c.families||!c.parts||(!shared_geometry&&(!c.vertex_count||!c.index_count||!c.vertices||!c.indices))||
        (!shared_geometry&&c.draw_capacity>opaque_caps.draw_capacity)||!c.draw_capacity||!c.reference_capacity||(c.source_count&&!c.source_instances)||c.material_count>gpu_opaque::texture_capacity||(c.material_count&&!c.material_textures)||c.reserved>gpu_opaque::max_batches||c.material_reserved||
        c.source_count>gpu_opaque::storage_range/64||c.vertex_count>gpu_opaque::storage_range/12||c.index_count>gpu_opaque::storage_range/4||c.reference_capacity>gpu_opaque::storage_range/128-1||
        uint64_t(c.draw_capacity)*c.view_count>gpu_opaque::storage_range/32)throw std::runtime_error("invalid/admission-disabled opaque reference configuration: enabled="+std::to_string(opaque_caps.enabled)+", actors="+std::to_string(c.actor_count)+", families="+std::to_string(c.family_count)+", parts="+std::to_string(c.part_count)+", views="+std::to_string(c.view_count)+", sources="+std::to_string(c.source_count)+", references="+std::to_string(c.reference_capacity)+", draws="+std::to_string(c.draw_capacity)+", batches="+std::to_string(c.reserved)+", textures="+std::to_string(c.material_count));
    wait(next_completion);auto previous=std::move(opaque_reference);opaque_reference={};auto& s=opaque_reference;
    try{
        s.stock_geometry=shared_geometry;
        s.view_batches=std::getenv("SE_VULKAN_GPU_OPAQUE_VIEW_BATCHES")&&std::string(std::getenv("SE_VULKAN_GPU_OPAQUE_VIEW_BATCHES"))=="1";
        s.role_streams=std::getenv("SE_VULKAN_GPU_OPAQUE_ROLE_STREAMS")&&std::string(std::getenv("SE_VULKAN_GPU_OPAQUE_ROLE_STREAMS"))=="1";
        s.actors.assign(c.actors,c.actors+c.actor_count);s.part_inputs.assign(c.parts,c.parts+c.part_count);s.batch_count=std::max(1u,c.reserved);s.family_inputs.assign(c.families,c.families+c.family_count);
        if(c.source_count)s.source_inputs.assign(c.source_instances,c.source_instances+c.source_count*64ull);++opaque_source_epoch;
        s.material_ids.resize(gpu_opaque::texture_capacity);for(uint32_t t=0;t<c.material_count;t++)s.material_ids[t]=c.material_textures[t];
        s.part_count=c.part_count;s.view_count=s.view_capacity=c.view_count;s.reference_capacity=c.reference_capacity;s.draw_capacity=c.draw_capacity;s.source_count=c.source_count;
        for(uint32_t f=0;f<c.family_count;f++){auto& family=c.families[f];if(!family.generation||family.threshold_count>16||uint64_t(family.first_part)+family.part_count>c.part_count)throw std::runtime_error("invalid opaque immutable family");
            float last=-1;for(uint32_t l=0;l<family.threshold_count;l++){float value=family.thresholds[l];if(!std::isfinite(value)||value<0||value<last)throw std::runtime_error("invalid opaque LOD thresholds");last=value;}
            std::vector<bool> ready(family.threshold_count+1);for(uint32_t p=family.first_part;p<family.first_part+family.part_count;p++){auto& part=c.parts[p];if(part.family!=f||part.lod>family.threshold_count||!part.generation||part.reserved>=s.batch_count||part.vertex_offset||part.padding||part.draw_first||part.draw_capacity||!part.index_count||part.index_count%3||part.first_index%3||(!shared_geometry&&uint64_t(part.first_index)+part.index_count>c.index_count)||!(part.flags&3)||(part.flags&~63u)||((part.flags&12)==12)||((part.flags&12)&&!part.coverage_count)||part.coverage_count>32||(!shared_geometry&&part.material>=gpu_opaque::texture_capacity))throw std::runtime_error("invalid opaque immutable part");ready[part.lod]=true;}
            if(std::any_of(ready.begin(),ready.end(),[](bool r){return !r;}))throw std::runtime_error("opaque ownership requires all authored LODs");
        }
        for(auto& a:s.actors)validate_opaque_reference_actor(a);validate_opaque_reference_capacity(s.actors);
        // Partition one per-view argument arena. Adding a pipeline/page batch
        // does not multiply the whole scene's argument capacity by batch count.
        {GpuSceneCostScope ranges_cost(gpu_scene_costs,GpuSceneCosts::InitRanges);
        s.ranges.resize(s.batch_count);uint64_t total=0;std::vector<uint32_t> old_counts(c.family_count);
        if(gpu_resident_deltas)for(auto& actor:s.actors)if(actor.flags&2)++old_counts[actor.family];
        for(auto& part:s.part_inputs){uint64_t capacity=2;
            if(gpu_resident_deltas)capacity+=2ull*old_counts[part.family];else for(auto& actor:s.actors)if((actor.flags&2)&&actor.family==part.family)capacity+=2;
            if(capacity>UINT32_MAX-s.ranges[part.reserved].capacity)throw std::runtime_error("opaque batch capacity overflow");s.ranges[part.reserved].capacity+=uint32_t(capacity);}
        for(auto& range:s.ranges){if(range.capacity>opaque_caps.max_draw_count)throw std::runtime_error("opaque batch exceeds device indirect-count limit");range.first=uint32_t(total);total+=range.capacity;if(total>s.draw_capacity)throw std::runtime_error("opaque batch ranges exceed argument arena");}
        for(auto& part:s.part_inputs){auto& range=s.ranges[part.reserved];part.draw_first=range.first;part.draw_capacity=range.capacity;}}

        for(uint32_t i=0;i<c.index_count;i++)if(c.indices[i]>=c.vertex_count)throw std::runtime_error("opaque index outside geometry");
        for(uint64_t i=0;i<c.vertex_count*3ull;i++)if(!std::isfinite(c.vertices[i]))throw std::runtime_error("opaque vertex nonfinite");
        {GpuSceneCostScope buffers_cost(gpu_scene_costs,GpuSceneCosts::InitBuffers);
        s.versions.assign(c.actor_count,1);s.revision=1;constexpr auto storage=VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
        s.depths.resize(c.view_count);
        s.families=opaque_reference_buffer(c.family_count*sizeof(SevkOpaqueFamily),storage,c.families);
        s.parts=opaque_reference_buffer(c.part_count*sizeof(SevkOpaquePart),storage,s.part_inputs.data());
        s.gpu_part_inputs=s.part_inputs;
        s.recording_families=gpu_opaque::recording_family_batches(s.part_inputs,c.family_count);
        // Internal immutable selected-LOD lists. Public family/part ABI and
        // original part identities remain unchanged for coverage/readback.
        std::vector<uint32_t> role_parts(c.family_count*17*2*2);
        for(uint32_t f=0;f<c.family_count;f++)for(uint32_t lod=0;lod<=c.families[f].threshold_count;lod++)for(uint32_t role=0;role<2;role++){
            uint32_t at=((f*17+lod)*2+role)*2;role_parts[at]=uint32_t(role_parts.size());
            auto& family=c.families[f];for(uint32_t p=family.first_part;p<family.first_part+family.part_count;p++)
                if(c.parts[p].lod==lod&&(c.parts[p].flags&(role?1u:2u))){role_parts.push_back(p);++role_parts[at+1];}
        }
        s.role_parts=opaque_reference_buffer(role_parts.size()*4ull,storage,role_parts.data());
        s.sources=opaque_reference_buffer(c.source_count*64ull,storage,c.source_instances);
        if(!shared_geometry){s.vertices=opaque_reference_buffer(c.vertex_count*12ull,VK_BUFFER_USAGE_VERTEX_BUFFER_BIT,c.vertices);s.indices=opaque_reference_buffer(c.index_count*4ull,VK_BUFFER_USAGE_INDEX_BUFFER_BIT,c.indices);}
        s.selectors=opaque_reference_buffer(c.actor_count*sizeof(SevkOpaqueSelector),storage);s.spot_signatures=opaque_reference_buffer(c.actor_count*64ull*48,storage);
        s.object_inputs.reset(c.actor_count);
        for(auto& slot:s.slots){slot.versions.resize(c.actor_count);slot.appearance_versions.resize(c.actor_count);
            if(gpu_owned_inputs)slot.parts=opaque_reference_buffer(c.part_count*sizeof(SevkOpaquePart),storage,s.gpu_part_inputs.data());
            slot.actors=opaque_reference_buffer(c.actor_count*sizeof(SevkOpaqueActor),storage);slot.object_inputs=opaque_reference_buffer(s.object_inputs.bytes.size(),storage);
            slot.views=opaque_reference_buffer(c.view_count*sizeof(SevkOpaqueView),storage);
            slot.selections=opaque_reference_buffer(c.actor_count*c.view_count*sizeof(SevkOpaqueSelection),storage,nullptr,true);
            slot.groups=opaque_reference_buffer(c.part_count*c.view_count*2ull*12,storage|VK_BUFFER_USAGE_TRANSFER_DST_BIT,nullptr,true);
            slot.references=opaque_reference_buffer(c.reference_capacity*sizeof(SevkOpaqueReference),storage,nullptr,true);
            slot.args=opaque_reference_buffer(c.draw_capacity*c.view_count*sizeof(SevkOpaqueIndirect),storage|VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT,nullptr,true);
            slot.draws=opaque_reference_buffer(c.draw_capacity*c.view_count*sizeof(SevkOpaqueDrawRecord),storage,nullptr,true);
            slot.counts=opaque_reference_buffer((c.view_count*s.batch_count+1+c.view_count)*4ull,storage|(opaque_conditional?VK_BUFFER_USAGE_CONDITIONAL_RENDERING_BIT_EXT:0)|VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT,nullptr,true);
            slot.stock_objects=opaque_reference_buffer((c.reference_capacity+1)*128ull,storage|VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,nullptr,true);
            slot.stock_colour=opaque_reference_buffer(c.reference_capacity*64ull,storage,nullptr,true);
            slot.stock_depth=opaque_reference_buffer(c.reference_capacity*48ull,storage,nullptr,true);
            slot.stock_draws=opaque_reference_buffer(c.draw_capacity*c.view_count*32ull,storage,nullptr,true);
            uint32_t blocks=(c.part_count*c.view_count*2+63)/64;slot.scan=opaque_reference_buffer((blocks+(blocks+63)/64)*4ull,storage,nullptr,true);
        }
        }
        {GpuSceneCostScope pipeline_cost(gpu_scene_costs,GpuSceneCosts::InitPipeline);
        std::array<VkDescriptorSetLayoutBinding,22> bindings{};for(uint32_t b=0;b<12;b++)bindings[b]={b,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,1,VK_SHADER_STAGE_COMPUTE_BIT|VK_SHADER_STAGE_VERTEX_BIT,nullptr};
        for(uint32_t b=14;b<22;b++)bindings[b]={b,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,1,VK_SHADER_STAGE_COMPUTE_BIT,nullptr};
        bindings[12]={12,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,gpu_opaque::texture_capacity,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr};bindings[13]={13,VK_DESCRIPTOR_TYPE_SAMPLER,1,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr};
        VkDescriptorSetLayoutCreateInfo descriptor{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};descriptor.bindingCount=uint32_t(bindings.size());descriptor.pBindings=bindings.data();
        check(vkCreateDescriptorSetLayout(device,&descriptor,nullptr,&s.descriptor_layout),"create opaque descriptor layout");
        VkPushConstantRange push{VK_SHADER_STAGE_COMPUTE_BIT|VK_SHADER_STAGE_VERTEX_BIT,0,sizeof(OpaqueReferenceControl)};
        VkPipelineLayoutCreateInfo layout{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};layout.setLayoutCount=1;layout.pSetLayouts=&s.descriptor_layout;layout.pushConstantRangeCount=1;layout.pPushConstantRanges=&push;
        check(vkCreatePipelineLayout(device,&layout,nullptr,&s.layout),"create opaque pipeline layout");
        VkDescriptorPoolSize sizes[]{{VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,40},{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,gpu_opaque::texture_capacity*2},{VK_DESCRIPTOR_TYPE_SAMPLER,2}};VkDescriptorPoolCreateInfo pool{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};pool.maxSets=2;pool.poolSizeCount=3;pool.pPoolSizes=sizes;
        check(vkCreateDescriptorPool(device,&pool,nullptr,&s.pool),"create opaque fenced descriptor pool");
        for(auto& slot:s.slots){VkDescriptorSetAllocateInfo alloc{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};alloc.descriptorPool=s.pool;alloc.descriptorSetCount=1;alloc.pSetLayouts=&s.descriptor_layout;
            check(vkAllocateDescriptorSets(device,&alloc,&slot.set),"allocate opaque slot descriptor");
            std::array<Buffer*,12> buffers{&slot.actors,&s.families,gpu_owned_inputs?&slot.parts:&s.parts,&slot.views,&s.selectors,&slot.selections,&slot.groups,&slot.references,&slot.args,&slot.draws,&slot.counts,&s.sources};
            std::array<VkDescriptorBufferInfo,12> infos{};std::array<VkWriteDescriptorSet,12> writes{};
            for(uint32_t b=0;b<12;b++){infos[b]={buffers[b]->handle,0,buffers[b]->bytes};writes[b]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[b].dstSet=slot.set;writes[b].dstBinding=b;writes[b].descriptorCount=1;writes[b].descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;writes[b].pBufferInfo=&infos[b];}
            vkUpdateDescriptorSets(device,12,writes.data(),0,nullptr);
            VkDescriptorBufferInfo role_info{s.role_parts.handle,0,s.role_parts.bytes};VkWriteDescriptorSet role_write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};role_write.dstSet=slot.set;role_write.dstBinding=21;role_write.descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;role_write.descriptorCount=1;role_write.pBufferInfo=&role_info;vkUpdateDescriptorSets(device,1,&role_write,0,nullptr);
            std::array<Buffer*,5> added{&slot.stock_objects,&slot.stock_colour,&slot.stock_depth,&slot.object_inputs,&slot.stock_draws};std::array<VkDescriptorBufferInfo,5> added_infos{};std::array<VkWriteDescriptorSet,5> added_writes{};
            for(uint32_t b=0;b<5;b++){added_infos[b]={added[b]->handle,0,added[b]->bytes};auto& w=added_writes[b];w.sType=VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;w.dstSet=slot.set;w.dstBinding=b+14;w.descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;w.descriptorCount=1;w.pBufferInfo=&added_infos[b];}vkUpdateDescriptorSets(device,5,added_writes.data(),0,nullptr);
            VkDescriptorBufferInfo scan_info{slot.scan.handle,0,slot.scan.bytes};VkWriteDescriptorSet scan_write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};scan_write.dstSet=slot.set;scan_write.dstBinding=19;scan_write.descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;scan_write.descriptorCount=1;scan_write.pBufferInfo=&scan_info;vkUpdateDescriptorSets(device,1,&scan_write,0,nullptr);VkDescriptorBufferInfo signatures{s.spot_signatures.handle,0,s.spot_signatures.bytes};VkWriteDescriptorSet sw{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};sw.dstSet=slot.set;sw.dstBinding=20;sw.descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;sw.descriptorCount=1;sw.pBufferInfo=&signatures;vkUpdateDescriptorSets(device,1,&sw,0,nullptr);
            std::vector<VkDescriptorImageInfo> images(gpu_opaque::texture_capacity);
            for(uint32_t t=0;t<images.size();t++){uint64_t id=t<c.material_count?c.material_textures[t]:0;auto found=textures.find(id);if(found==textures.end())throw std::runtime_error("opaque immutable material texture unavailable");images[t]={VK_NULL_HANDLE,found->second.image.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};}
            VkDescriptorImageInfo sampling{sampler,VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};VkWriteDescriptorSet texture_writes[2]{};
            for(auto& w:texture_writes){w.sType=VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;w.dstSet=slot.set;}
            texture_writes[0].dstBinding=12;texture_writes[0].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;texture_writes[0].descriptorCount=gpu_opaque::texture_capacity;texture_writes[0].pImageInfo=images.data();
            texture_writes[1].dstBinding=13;texture_writes[1].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLER;texture_writes[1].descriptorCount=1;texture_writes[1].pImageInfo=&sampling;vkUpdateDescriptorSets(device,2,texture_writes,0,nullptr);
        }
        s.modules[0]=opaque_reference_shader("gpu_opaque_reference_cs");
        VkComputePipelineCreateInfo compute{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};compute.layout=s.layout;compute.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,s.modules[0],"main",nullptr};
        check(vkCreateComputePipelines(device,pipeline_cache,1,&compute,nullptr,&s.compute),"create opaque resident selection/emission pipeline");
        if(!shared_geometry){s.modules[1]=opaque_reference_shader("gpu_opaque_reference_vs");s.modules[2]=opaque_reference_shader("gpu_opaque_reference_ps");s.graphics=world_graphics(s.modules[1],"main",s.modules[2],"main",s.layout,{{0,12,VK_VERTEX_INPUT_RATE_VERTEX}},{{0,0,VK_FORMAT_R32G32B32_SFLOAT,0}},&color_format,1,true);
            s.modules[3]=opaque_reference_shader("gpu_opaque_reference_depth_ps");s.depth_pipeline=world_graphics(s.modules[1],"main",s.modules[3],"main",s.layout,{{0,12,VK_VERTEX_INPUT_RATE_VERTEX}},{{0,0,VK_FORMAT_R32G32B32_SFLOAT,0}},nullptr,0,true,true);}
        if(timestamp_bits&&std::getenv("SE_VULKAN_GPU_OPAQUE_TIMING")&&std::string(std::getenv("SE_VULKAN_GPU_OPAQUE_TIMING"))=="1"){VkQueryPoolCreateInfo q{VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO};q.queryType=VK_QUERY_TYPE_TIMESTAMP;q.queryCount=22;check(vkCreateQueryPool(device,&q,nullptr,&s.compute_timing),"create resident compute timing queries");}
        }
        s.initialized=true;
        auto current=std::move(opaque_reference);opaque_reference=std::move(previous);cleanup_opaque_reference();opaque_reference=std::move(current);opaque_stock.ready=false;
    }catch(...){cleanup_opaque_reference();opaque_reference=std::move(previous);throw;}
}
void update_opaque_reference_actors(const uint32_t* indices,const SevkOpaqueActor* actors,uint32_t count,const void* objects,uint32_t bytes,bool resident_rebind=false){
    GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::DeltaInputs);
    auto& s=opaque_reference;if(own_frame_open()||!s.initialized||count>s.actors.size()||(!indices&&count)||(!actors&&count)||(objects?uint64_t(count)*128!=bytes:bytes!=0))throw std::runtime_error("invalid opaque batched commit");
    std::vector<SevkOpaqueActor> next;if(gpu_resident_deltas)next.resize(count);else next=s.actors;std::unordered_set<uint32_t> unique;bool capacity_changed=false;
    // Only the resident registry supplies the committed_pose path: its batch
    // already validated pose values and distinct generation-checked handles.
    // Published actor_indices is injective. Direct reference callers retain
    // full validation and duplicate-index rejection.
    bool committed_pose=gpu_resident_deltas&&resident_rebind;
    for(uint32_t i=0;i<count;i++){uint32_t index=indices[i];if(index>=s.actors.size()||(!committed_pose&&!unique.insert(index).second))throw std::runtime_error("opaque duplicate/stale commit index");
        auto& actor=actors[i];const auto& old=s.actors[index];bool same_source=actor.source_first==old.source_first&&actor.source_count==old.source_count&&((actor.flags^old.flags)&32)==0;
        validate_opaque_reference_actor(actor,!gpu_resident_deltas||!same_source,committed_pose);
        bool compatible=false;if(resident_rebind&&actor.owner==old.owner&&((actor.flags^old.flags)&2)==0){auto& a=s.family_inputs[actor.family];auto& b=s.family_inputs[old.family];compatible=a.threshold_count==b.threshold_count&&std::equal(a.thresholds,a.thresholds+a.threshold_count,b.thresholds);}
        if((actor.owner!=old.owner||actor.family!=old.family||((actor.flags^old.flags)&34))&&actor.generation==old.generation&&!compatible)throw std::runtime_error("opaque identity/model replacement requires a new generation");capacity_changed|=actor.family!=old.family||((actor.flags^old.flags)&2)!=0;auto& staged=next[gpu_resident_deltas?i:index];staged=actor;staged.appearance_count=old.appearance_count;}
    // Pose, visibility, appearance and source-count commits cannot increase
    // geometry-group/reference capacity. Only family/OLD membership changes
    // invalidate those topology-derived bounds; every input is still validated.
    if(capacity_changed){if(gpu_resident_deltas){auto complete=s.actors;for(uint32_t i=0;i<count;i++)complete[indices[i]]=next[i];validate_opaque_reference_capacity(complete);}else validate_opaque_reference_capacity(next);}
    if(opaque_stock.ready&&capacity_changed)for(uint32_t i=0;i<count;i++){auto& actor=next[gpu_resident_deltas?i:indices[i]];auto& family=s.family_inputs[actor.family];for(uint32_t p=family.first_part;p<family.first_part+family.part_count;p++)if(bool(actor.flags&2)!=bool(opaque_stock.batches[s.part_inputs[p].reserved].input.old))throw std::runtime_error("opaque commit changes batch input contract");}
    // Validate the whole batch before mutation. No GPU wait or selector readback;
    // the owning slot applies actor/object bytes only after its existing fence.
    for(uint32_t i=0;i<count;i++){uint32_t index=indices[i];s.actors[index]=next[gpu_resident_deltas?i:index];if(objects)s.object_inputs.object(index,static_cast<const uint8_t*>(objects)+i*128);s.versions[index]=++s.revision;}
}
void update_opaque_reference_appearances(const SevkOpaqueAppearanceUpdate* updates,uint32_t count,const SevkOpaqueAppearance* entries,uint32_t total){
    GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::DeltaAppearance);
    auto& s=opaque_reference;if(own_frame_open()||!s.initialized||count>s.actors.size()||(!updates&&count)||(!entries&&total))throw std::runtime_error("invalid appearance batch");std::unordered_set<uint32_t> unique;
    std::vector<gpu_opaque::AppearanceArena::Update> staged;staged.reserve(count);
    for(uint32_t i=0;i<count;i++){auto& u=updates[i];if(u.actor>=s.actors.size()||u.reserved||u.count>gpu_opaque::appearance_limit||u.first>total||u.count>total-u.first||!unique.insert(u.actor).second)throw std::runtime_error("invalid appearance update range");std::unordered_set<uint32_t> materials;
        for(uint32_t j=0;j<u.count;j++){auto& e=entries[u.first+j];if(e.visible>1||!materials.insert(e.material).second)throw std::runtime_error("ambiguous material appearance");for(float f:e.old_colour)if(!std::isfinite(f))throw std::runtime_error("OLD material appearance nonfinite");}
        gpu_opaque::AppearanceArena::Update item{u.actor,{}};if(u.count)item.entries.assign(entries+u.first,entries+u.first+u.count);
        std::sort(item.entries.begin(),item.entries.end(),[](auto& a,auto& b){return a.material<b.material;});staged.push_back(std::move(item));}
    s.object_inputs.commit(staged);
    for(auto& u:staged)if(s.actors[u.actor].appearance_count!=u.entries.size()){s.actors[u.actor].appearance_count=uint32_t(u.entries.size());s.versions[u.actor]=++s.revision;}
}
void update_opaque_reference_actor(uint32_t index,const SevkOpaqueActor& actor){update_opaque_reference_actors(&index,&actor,1,nullptr,0);}
void queue_opaque_reference_frame(const SevkOpaqueFrame& frame,const SevkOpaqueView* views,uint32_t count){
    GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::FrameValidation);
    auto& s=opaque_reference;if(own_frame_open()||!s.initialized||!views||(!count||count>s.view_capacity)||!frame.epoch||(frame.flags&~3u))throw std::runtime_error("invalid opaque frame");
    for(uint64_t id:s.material_ids)if(textures.find(id)==textures.end())throw std::runtime_error("opaque material resource missing; ownership requires re-registration/fallback");
    for(auto value:frame.camera)if(!std::isfinite(value)||std::abs(value)>1e12)throw std::runtime_error("opaque camera outside admitted precision domain");
    if(!std::isfinite(frame.smooth_camera_distance)||frame.smooth_camera_distance<0)throw std::runtime_error("opaque jump distance");
    const float values[]{frame.delta,frame.coefficient,frame.distance_add,frame.distance_mult,frame.hysteresis,frame.dead_zone,frame.dead_zone_mult,frame.maximum_seconds,frame.minimum_seconds};
    for(float value:values)if(!std::isfinite(value))throw std::runtime_error("opaque LOD setting nonfinite");
    for(uint32_t v=0;v<count;v++){if((views[v].flags&~255u)||views[v].id>=64||views[v].light>=64||((views[v].flags&128)&&(!(views[v].flags&24)||!opaque_conditional))||views[v].minimum_lod<0)throw std::runtime_error("opaque view flags/LOD");for(double p:views[v].planes)if(!std::isfinite(p))throw std::runtime_error("opaque plane nonfinite");for(float p:views[v].projection)if(!std::isfinite(p))throw std::runtime_error("opaque projection nonfinite");
        if(!std::isfinite(views[v].minimum_size_squared)||views[v].minimum_size_squared<0||!std::isfinite(views[v].maximum_distance)||views[v].maximum_distance<0)throw std::runtime_error("opaque pruning nonfinite");}
    uint64_t needed=0;for(uint32_t v=0;v<count;v++)if(views[v].flags&1)needed+=(views[v].flags&2)?s.reference_depth_bound:(views[v].flags&32)?s.reference_forward_bound:s.reference_colour_bound;
    if(needed>s.reference_capacity)throw std::runtime_error("current resident views exceed reference capacity");
    s.next_frame=frame;s.next_view_count=count;s.next_views.assign(views,views+count);s.next_ready=true;
}
void opaque_reference_dependency(VkCommandBuffer cmd,VkPipelineStageFlags2 source,VkAccessFlags2 src,VkPipelineStageFlags2 destination,VkAccessFlags2 dst){
    VkMemoryBarrier2 barrier{VK_STRUCTURE_TYPE_MEMORY_BARRIER_2};barrier.srcStageMask=source;barrier.srcAccessMask=src;barrier.dstStageMask=destination;barrier.dstAccessMask=dst;
    VkDependencyInfo info{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};info.memoryBarrierCount=1;info.pMemoryBarriers=&barrier;vkCmdPipelineBarrier2(cmd,&info);
}
OpaqueReferenceControl opaque_reference_control(){
    auto& s=opaque_reference;
    // The standalone reference has no stock Frame.hlsli. Only derived OLD
    // depth-fade casters consume this signature; keep their layout check strict.
    bool needs_distance_fade=std::any_of(s.gpu_part_inputs.begin(),s.gpu_part_inputs.end(),
        [](const auto& part){return (part.flags&1u) && part.padding;});
    uint32_t distance_fade=needs_distance_fade?world_distance_fade_bits():0u;
    return {s.frame,uint32_t(s.actors.size()),uint32_t(s.family_inputs.size()),s.part_count,s.view_count,s.reference_capacity,s.draw_capacity,0,s.batch_count,distance_fade,0};
}
// Plan 4 milestone 2: begin_frame, with no recording in flight, latches the frame inputs Build queued and copies
// Build's committed actor rows into this slot, so Build's next frame (scene calls alongside the recording) only
// writes the pending inputs and the host rows. The recording reads frame/views/view_count, the slot and rec_actors.
void latch_opaque_reference_frame(){
    auto& s=opaque_reference;if(!s.next_ready)return;
    s.frame=s.next_frame;s.view_count=s.next_view_count;s.views.swap(s.next_views);s.frame_ready=true;s.next_ready=false;gpu_resident.view_map=gpu_resident.next_view_map;
    if(s.rec_actors.size()!=s.actors.size()){s.rec_actors=s.actors;s.rec_versions=s.versions;}
    else for(size_t a=0;a<s.actors.size();a++)if(s.rec_versions[a]!=s.versions[a]){s.rec_actors[a]=s.actors[a];s.rec_versions[a]=s.versions[a];}
    record_cv.notify_all();
}
void latch_opaque_reference_slot(){
    auto& s=opaque_reference;if(!s.initialized||!s.frame_ready)return;auto& slot=s.slots[slot_index];
    // begin_frame has waited THIS slot. Descriptors never change while in use.
    if(gpu_owned_inputs){for(auto [first,count]:slot.pending_parts){auto at=uint64_t(first)*sizeof(SevkOpaquePart),bytes=uint64_t(count)*sizeof(SevkOpaquePart);std::memcpy(static_cast<uint8_t*>(slot.parts.mapped)+at,s.gpu_part_inputs.data()+first,bytes);check(vmaFlushAllocation(allocator,slot.parts.memory,at,bytes),"flush fenced resident part bindings");}slot.pending_parts.clear();}
    bool rebuilt=slot.object_inputs.bytes<s.object_inputs.bytes.size();
    if(rebuilt){auto replacement=opaque_reference_buffer(s.object_inputs.bytes.size(),VK_BUFFER_USAGE_STORAGE_BUFFER_BIT);destroy_buffer(slot.object_inputs);slot.object_inputs=replacement;
        VkDescriptorBufferInfo info{replacement.handle,0,replacement.bytes};VkWriteDescriptorSet write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};write.dstSet=slot.set;write.dstBinding=17;write.descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;write.descriptorCount=1;write.pBufferInfo=&info;vkUpdateDescriptorSets(device,1,&write,0,nullptr);
        std::memcpy(slot.object_inputs.mapped,s.object_inputs.bytes.data(),s.object_inputs.bytes.size());}
    for(uint32_t a=0;a<s.actors.size();a++){
        if(slot.versions[a]!=s.versions[a]){std::memcpy(static_cast<uint8_t*>(slot.actors.mapped)+a*sizeof(SevkOpaqueActor),&s.actors[a],sizeof(SevkOpaqueActor));if(!rebuilt)std::memcpy(static_cast<uint8_t*>(slot.object_inputs.mapped)+uint64_t(a)*gpu_opaque::appearance_header_stride,s.object_inputs.bytes.data()+uint64_t(a)*gpu_opaque::appearance_header_stride,128);slot.versions[a]=s.versions[a];}
        if(rebuilt||slot.appearance_versions[a]!=s.object_inputs.versions[a]){auto& range=s.object_inputs.ranges[a];
            if(!rebuilt){std::memcpy(static_cast<uint8_t*>(slot.object_inputs.mapped)+uint64_t(a)*gpu_opaque::appearance_header_stride+128,&range.first,4);if(range.count)std::memcpy(static_cast<uint8_t*>(slot.object_inputs.mapped)+range.first,s.object_inputs.bytes.data()+range.first,range.count*sizeof(SevkOpaqueAppearance));}
            slot.appearance_versions[a]=s.object_inputs.versions[a];}
    }
    check(vmaFlushAllocation(allocator,slot.actors.memory,0,slot.actors.bytes),"flush committed opaque actors");check(vmaFlushAllocation(allocator,slot.object_inputs.memory,0,slot.object_inputs.bytes),"flush fenced stock object commits");
}
void prepare_opaque_reference(VkCommandBuffer cmd){
    auto& s=opaque_reference;if(!s.initialized||!s.frame_ready)return;auto& slot=s.slots[slot_index];
    // begin_frame has waited THIS slot and latched its actor rows (latch_opaque_reference_slot).
    if(s.materials_dirty[slot_index]){
        std::vector<VkDescriptorImageInfo> images(s.material_ids.size());for(uint32_t t=0;t<images.size();t++){auto found=textures.find(s.material_ids[t]);if(found==textures.end())throw std::runtime_error("opaque material disappeared after frame publication");images[t]={VK_NULL_HANDLE,found->second.image.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};}
        VkWriteDescriptorSet write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};write.dstSet=slot.set;write.dstBinding=12;write.descriptorType=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;write.descriptorCount=uint32_t(images.size());write.pImageInfo=images.data();vkUpdateDescriptorSets(device,1,&write,0,nullptr);s.materials_dirty[slot_index]=false;
    }
    for(auto& view:s.views)if(view.flags&128){uint64_t bit=uint64_t(1)<<view.light;if(!spots.frozen_ready[view.light]||spots.frozen_sizes[view.light]!=spots.resolution||(((view.flags&16)?(spots.static_dirty|pass_static_dirty()):spots.dynamic_dirty)&bit))view.flags|=64;}std::memcpy(slot.views.mapped,s.views.data(),s.views.size()*sizeof(SevkOpaqueView));check(vmaFlushAllocation(allocator,slot.views.memory,0,slot.views.bytes),"flush opaque current views");
    if(s.compute_timing&&s.compute_timing_valid[slot_index]){uint64_t values[22]{};auto result=vkGetQueryPoolResults(device,s.compute_timing,slot_index*11,11,sizeof(values),values,16,VK_QUERY_RESULT_64_BIT|VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);if(result!=VK_NOT_READY){check(result,"read completed resident compute timings");bool ready=true;for(uint32_t i=0;i<11;i++)ready&=values[i*2+1]!=0;if(ready){uint64_t mask=timestamp_bits==64?~0ull:(uint64_t(1)<<timestamp_bits)-1;for(uint32_t i=0;i<10;i++)opaque_compute_times[i+1]+=uint64_t(double((values[(i+1)*2]-values[i*2])&mask)*timestamp_period);++opaque_compute_times[0];s.compute_timing_valid[slot_index]=false;}}}
    // Selectors are GPU-owned; dependency includes the previous submitted frame.
    opaque_reference_dependency(cmd,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT|VK_PIPELINE_STAGE_2_HOST_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT|VK_ACCESS_2_HOST_WRITE_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT|VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
    vkCmdFillBuffer(cmd,slot.groups.handle,0,slot.groups.bytes,0);vkCmdFillBuffer(cmd,slot.counts.handle,0,slot.counts.bytes,0);
    opaque_reference_dependency(cmd,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
    vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,s.compute);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,s.layout,0,1,&slot.set,0,nullptr);
    auto control=opaque_reference_control();
    if(s.role_streams)control.frame.flags|=4; // Internal dispatch option; not a caller frame flag.
    bool view_masks=std::getenv("SE_VULKAN_GPU_OPAQUE_VIEW_MASKS")&&std::strcmp(std::getenv("SE_VULKAN_GPU_OPAQUE_VIEW_MASKS"),"1")==0;
    if(view_masks)control.frame.flags|=8;
    uint32_t groups=s.part_count*s.view_count*2,blocks=(groups+63)/64;
    uint32_t timing_at=slot_index*11;if(s.compute_timing){vkCmdResetQueryPool(cmd,s.compute_timing,timing_at,11);vkCmdWriteTimestamp2(cmd,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,s.compute_timing,timing_at++);}
    for(uint32_t stage:{0u,9u,8u,7u,1u,4u,5u,6u,2u,3u}){control.stage=stage;vkCmdPushConstants(cmd,s.layout,VK_SHADER_STAGE_COMPUTE_BIT|VK_SHADER_STAGE_VERTEX_BIT,0,sizeof(control),&control);
        uint32_t threads=(stage==9||stage==2)?uint32_t(s.actors.size())*s.view_count:stage==8?s.view_count:stage==5?1:stage==4?blocks:(stage==1||stage==6||stage==3||stage==7)?groups:uint32_t(s.actors.size());uint32_t workgroups=(threads+63)/64,x=std::min(workgroups,65535u),y=(workgroups+x-1)/x;vkCmdDispatch(cmd,x,y,1);
        opaque_reference_dependency(cmd,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);if(s.compute_timing)vkCmdWriteTimestamp2(cmd,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,s.compute_timing,timing_at++);}if(s.compute_timing)s.compute_timing_valid[slot_index]=true;
    opaque_reference_dependency(cmd,VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,
        VK_PIPELINE_STAGE_2_DRAW_INDIRECT_BIT|VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT|VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|(opaque_conditional?VK_PIPELINE_STAGE_2_CONDITIONAL_RENDERING_BIT_EXT:0)|VK_PIPELINE_STAGE_2_HOST_BIT,
        (opaque_conditional?VK_ACCESS_2_CONDITIONAL_RENDERING_READ_BIT_EXT:0)|VK_ACCESS_2_INDIRECT_COMMAND_READ_BIT|VK_ACCESS_2_SHADER_STORAGE_READ_BIT|VK_ACCESS_2_HOST_READ_BIT);
    s.last_slot=slot_index;s.published=true;
}
void bind_opaque_reference_batch(VkCommandBuffer cmd,uint32_t view,bool depth_pass){
    auto& s=opaque_reference;if(!s.initialized||!s.frame_ready)return;auto& slot=s.slots[slot_index];
    vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,depth_pass?s.depth_pipeline:s.graphics);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,s.layout,0,1,&slot.set,0,nullptr);
    VkDeviceSize offset=0;vkCmdBindVertexBuffers(cmd,0,1,&s.vertices.handle,&offset);vkCmdBindIndexBuffer(cmd,s.indices.handle,0,VK_INDEX_TYPE_UINT32);
    VkViewport viewport{0,float(extent.height),float(extent.width),-float(extent.height),0,1};VkRect2D scissor{{0,0},extent};vkCmdSetViewport(cmd,0,1,&viewport);vkCmdSetScissor(cmd,0,1,&scissor);
    auto control=opaque_reference_control();control.batch=view;vkCmdPushConstants(cmd,s.layout,VK_SHADER_STAGE_COMPUTE_BIT|VK_SHADER_STAGE_VERTEX_BIT,0,sizeof(control),&control);
    vkCmdDrawIndexedIndirectCount(cmd,slot.args.handle,uint64_t(view)*s.draw_capacity*sizeof(SevkOpaqueIndirect),slot.counts.handle,view*4,s.draw_capacity,sizeof(SevkOpaqueIndirect));
}
void record_opaque_reference_depths(VkCommandBuffer cmd){
    if(opaque_stock.ready)return;
    auto& s=opaque_reference;if(!s.initialized||!s.frame_ready)return;
    for(uint32_t v=0;v<s.view_count;v++)if((s.views[v].flags&3)==3){auto& d=s.depths[v];
        if(!d.image.handle||d.width!=extent.width||d.height!=extent.height){
            if(d.image.handle){Texture old{};old.image=d.image;retired.push_back({next_completion,old,{}});}
            d.image=make_image(extent.width,extent.height,VK_FORMAT_D32_SFLOAT,VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT,VK_IMAGE_ASPECT_DEPTH_BIT);d.width=extent.width;d.height=extent.height;d.valid=false;
        }
        barrier(cmd,d.image.handle,d.valid?VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL:VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,
            d.valid?VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT:VK_PIPELINE_STAGE_2_NONE,d.valid?VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT:0,
            VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);
        VkRenderingAttachmentInfo attachment{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};attachment.imageView=d.image.view;attachment.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;attachment.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;attachment.storeOp=VK_ATTACHMENT_STORE_OP_STORE;attachment.clearValue.depthStencil={1,0};
        VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.renderArea={{0,0},extent};rendering.layerCount=1;rendering.pDepthAttachment=&attachment;vkCmdBeginRendering(cmd,&rendering);bind_opaque_reference_batch(cmd,v,true);vkCmdEndRendering(cmd);
        std::memcpy(d.camera,s.frame.camera,sizeof(d.camera));std::memcpy(d.projection,s.views[v].projection,sizeof(d.projection));d.valid=true;
    }
}
void record_opaque_reference(VkCommandBuffer cmd){
    auto& s=opaque_reference;if(!s.initialized||!s.frame_ready)return;
    if(opaque_stock.ready){s.frame_ready=false;return;}
    if((s.views[0].flags&3)==1)bind_opaque_reference_batch(cmd,0,false);s.frame_ready=false;
}
void readback_opaque_reference_depth(uint32_t view,float* pixels,uint32_t count,double* camera,float* projection){
    auto& s=opaque_reference;if(recording||batching_uploads||!s.initialized||view>=s.depths.size()||!pixels||!camera||!projection)throw std::runtime_error("invalid opaque depth verification");
    auto& d=s.depths[view];if(!d.valid||count!=uint64_t(d.width)*d.height)throw std::runtime_error("opaque depth verification exact retained image size");
    wait(next_completion);Buffer staging=make_buffer(count*4ull,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);
    try{immediate([&](VkCommandBuffer cmd){
        barrier(cmd,d.image.handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,
            VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
        VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_DEPTH_BIT,0,0,1};copy.imageExtent={d.width,d.height,1};vkCmdCopyImageToBuffer(cmd,d.image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,staging.handle,1,&copy);
        barrier(cmd,d.image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,
            VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);
        opaque_reference_dependency(cmd,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_HOST_BIT,VK_ACCESS_2_HOST_READ_BIT);
    });check(vmaInvalidateAllocation(allocator,staging.memory,0,staging.bytes),"invalidate opaque depth verification");std::memcpy(pixels,staging.mapped,staging.bytes);std::memcpy(camera,d.camera,sizeof(d.camera));std::memcpy(projection,d.projection,sizeof(d.projection));
    }catch(...){destroy_buffer(staging);throw;}destroy_buffer(staging);
}
void readback_opaque_reference(SevkOpaqueSelector* selectors,uint32_t actor_count,SevkOpaqueSelection* selections,uint32_t selection_count,SevkOpaqueReference* refs,uint32_t ref_capacity,
    SevkOpaqueIndirect* args,SevkOpaqueDrawRecord* draws,uint32_t draw_capacity,uint32_t* counts,uint32_t view_count,uint32_t* errors){
    auto& s=opaque_reference;if(recording||!s.published||!selectors||!selections||!refs||!args||!draws||!counts||!errors||actor_count!=s.actors.size()||selection_count!=actor_count*s.view_count||ref_capacity!=s.reference_capacity||draw_capacity!=s.draw_capacity*s.view_count||view_count!=s.view_count*s.batch_count)throw std::runtime_error("opaque exact verification readback size");
    wait(next_completion);auto& slot=s.slots[s.last_slot];
    opaque_reference_read(s.selectors,selectors,s.selectors.bytes);opaque_reference_read(slot.selections,selections,selection_count*sizeof(SevkOpaqueSelection));opaque_reference_read(slot.references,refs,slot.references.bytes);opaque_reference_read(slot.args,args,draw_capacity*sizeof(SevkOpaqueIndirect));opaque_reference_read(slot.draws,draws,draw_capacity*sizeof(SevkOpaqueDrawRecord));std::vector<uint32_t> result(view_count+1);opaque_reference_read(slot.counts,result.data(),result.size()*4ull);std::copy_n(result.data(),view_count,counts);*errors=result.back();
}
