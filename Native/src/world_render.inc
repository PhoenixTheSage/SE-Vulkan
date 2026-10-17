VkShaderModule world_file_shader(const char* name,VkShaderStageFlagBits stage=VK_SHADER_STAGE_FRAGMENT_BIT){
    auto path=std::filesystem::path(shader_directory)/(std::string(name)+".spv");std::ifstream f(path,std::ios::binary|std::ios::ate);
    if(!f || f.tellg()<20 || f.tellg()>16*1024*1024 || f.tellg()%4)throw std::runtime_error("world glue shader missing: "+path.string());
    std::vector<uint32_t> words(static_cast<size_t>(f.tellg())/4);f.seekg(0);f.read(reinterpret_cast<char*>(words.data()),words.size()*4);
    SpvReflectShaderModule reflection{};auto result=world_shader(words.data(),words.size()*4,stage,reflection);spvReflectDestroyShaderModule(&reflection);return result;
}
void init_world_lighting(uint32_t kind,const void* data,uint64_t bytes){
    if(recording || kind>4)throw std::runtime_error("invalid stock lighting kind");wait(next_completion);init_world_layouts();
    SpvReflectShaderModule r{};VkShaderModule module{};
    try{module=world_shader(data,bytes,kind==0?VK_SHADER_STAGE_VERTEX_BIT:VK_SHADER_STAGE_FRAGMENT_BIT,r);
        const std::vector<VkVertexInputBindingDescription> bindings{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}};
        if(kind==0)for(uint32_t i=0;i<r.input_variable_count;++i){const auto& v=*r.input_variables[i];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;
            if(v.location>1)throw std::runtime_error("unsupported stock fullscreen vertex layout");world.fullscreen_attributes.push_back({v.location,0,static_cast<VkFormat>(v.format),v.location==0?0u:16u});}
        const auto& attributes=world.fullscreen_attributes;
        if(kind==0){if(world.fullscreen)throw std::runtime_error("fullscreen stock shader already initialized");world.fullscreen=module;module={};
            auto compose=world_file_shader("world_compose_ps"),resolve=VkShaderModule{};
            try{resolve=world_file_shader("world_shadow_ps");VkFormat format=VK_FORMAT_R8_UNORM;
                world.compose=world_graphics(world.fullscreen,"__vertex_shader",compose,"main",world.light_pipeline_layout,bindings,attributes,&color_format,1,true,false,false,true);
                world.compose_format=color_format;
                world.resolve=world_graphics(world.fullscreen,"__vertex_shader",resolve,"main",world.light_pipeline_layout,bindings,attributes,&format,1,false);
                vkDestroyShaderModule(device,compose,nullptr);vkDestroyShaderModule(device,resolve,nullptr);
            }catch(...){if(compose)vkDestroyShaderModule(device,compose,nullptr);if(resolve)vkDestroyShaderModule(device,resolve,nullptr);throw;}}
        else {if(!world.fullscreen)throw std::runtime_error("initialize stock fullscreen shader first");
            for(uint32_t i=0;i<r.descriptor_binding_count;++i){auto& b=r.descriptor_bindings[i];bool ok=b.set==0 && b.count==1 && ((b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER && ((b.binding==0 && b.block.size==1280)||(kind>=3 && b.binding==1 && b.block.size==208))) ||
                (b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE && (b.binding==128||b.binding==129||b.binding==130||b.binding==131||b.binding==138||b.binding==139||b.binding==140||b.binding==143||b.binding==145||b.binding==147||(kind>=3 && (b.binding==141 || (kind==4 && b.binding==142))))) ||
                (kind==2 && b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_STORAGE_BUFFER && (b.binding==141||b.binding==142)) || (b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER && (b.binding==256||b.binding==258||b.binding==259||(kind==4 && b.binding==271))));
                if(!ok)throw std::runtime_error("stock lighting reflection exceeds supported resource contract");}
            VkFormat format=VK_FORMAT_R16G16B16A16_SFLOAT;auto layout=kind>=3?world.spot_pipeline_layout:world.light_pipeline_layout;
            auto pipeline=world_graphics(world.fullscreen,"__vertex_shader",module,"__pixel_shader",layout,bindings,attributes,&format,1,false,false,kind>1);
            if(world.lights[kind-1])vkDestroyPipeline(device,world.lights[kind-1],nullptr);world.lights[kind-1]=pipeline;}
        spvReflectDestroyShaderModule(&r);if(module)vkDestroyShaderModule(device,module,nullptr);
    }catch(...){if(r._internal)spvReflectDestroyShaderModule(&r);if(module)vkDestroyShaderModule(device,module,nullptr);throw;}
}
void validate_world_packet(const SevkWorldDraw& d,SevkQueueValidationStats& stats,WorldTextureValidationCache<>& validated){
const auto& pipelines=world.pipelines;const auto& meshes=world.meshes;const auto& voxels=world.voxel_materials;const auto& material_textures=textures;
++stats.draws;++stats.mesh_checks;if(d.size!=sizeof(d)||d.pipeline>=32||!pipelines[d.pipeline].handle||!pipelines[world_base_pipeline(d.pipeline)+32].handle||!meshes.count(d.mesh)||!d.object_constants||d.object_bytes!=pipelines[d.pipeline].object_bytes||!d.instance_count||d.instance_count>1000000)throw std::runtime_error("invalid world draw/pipeline/geometry/constants");
        bool instanced=pipelines[d.pipeline].type>=2;if(instanced && !d.instances)throw std::runtime_error("missing instance stream");if(!instanced && d.instance_count!=1)throw std::runtime_error("rigid/skinned draw cannot consume an instance stream");
        if(world_voxel(d.pipeline)){++stats.voxel_checks;if(!voxels.count(d.color))throw std::runtime_error("missing voxel material group");}
        else {
            auto validate=[&](uint64_t id){++stats.texture_checks;if(!material_textures.count(id))throw std::runtime_error("world draw references missing material texture");};
            if(world.texture_validation_cache){if(validated.validate({d.color,d.normal,d.extensions,d.alpha},validate))++stats.hits;}
            else for(uint64_t id:{d.color,d.normal,d.extensions,d.alpha})validate(id);
        }

}
void begin_next_submission(){
    world.current().queued=false;
    world.active_submission^=1u;
}
void queue_world(const void* frame,uint32_t frame_bytes,const float* projection,const float* inverse,const SevkWorldDraw* draws,uint32_t count,const uint64_t* object_ids=nullptr,const uint8_t* objects=nullptr,uint32_t object_bytes=0,const uint8_t* instances=nullptr,uint32_t instance_bytes=0){
    critical_profile::Scope critical_queue(critical_profile::QueueOwner,true);
    auto queued_at=std::chrono::steady_clock::now();
    if(recording)throw std::runtime_error("world packets must precede begin_frame");
    begin_next_submission();
    world.current().queue_us=0;world.current().queue_work=SevkQueueWorkStats{sizeof(SevkQueueWorkStats),SEVK_ABI_VERSION};
    world.current().recipe_stats=SevkRecipeStats{sizeof(SevkRecipeStats),SEVK_ABI_VERSION};
    world.current().validation_stats=SevkQueueValidationStats{sizeof(SevkQueueValidationStats),SEVK_ABI_VERSION,world.texture_validation_cache?1u:0u};
    WorldTextureValidationCache<> validated_textures;
    world.current().queued=false;world.current().draws.clear();if(!frame && !count)return;
    if(!frame || frame_bytes!=1280 || !projection || !inverse || (!draws && count) || count>65536 || !world.lights[0] || !world.lights[1] || !world.lights[2])throw std::runtime_error("world packet/assets contract mismatch");
    world.current().draws.reserve(count);world.current().instances.clear();
    bool retained=object_ids!=nullptr;if(retained!=world.retained_objects){for(auto& slot:world.slots)slot.object_versions.clear();}
    world.retained_objects=retained;
    if(retained){world.object_records.alignment=uint32_t(world.alignment);world.object_records.begin();}
    uint64_t object_total=0;for(uint32_t i=0;i<count;++i){const auto& d=draws[i];if(d.pipeline>=32 || d.object_bytes!=world.pipelines[d.pipeline].object_bytes)throw std::runtime_error("invalid world object layout");object_total+=world_align(d.object_bytes);}
    if(object_total>256ull*1024*1024)throw std::runtime_error("world object batch exceeds bounded upload budget");
    auto copy_at=std::chrono::steady_clock::now();bool blob=objects!=nullptr;
    if(blob){
        if(retained || object_bytes!=object_total || instance_bytes>256u*1024*1024 || (instance_bytes && !instances))throw std::runtime_error("world blob contract mismatch");
        world.current().draw_objects.assign(objects,objects+object_bytes);
        if(instance_bytes)world.current().instances.data.assign(instances,instances+instance_bytes);
    }else if(!retained)world.current().draw_objects.resize(size_t(object_total));uint32_t object_cursor=0;
    world.current().queue_work.copy_us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-copy_at).count());
    auto validate_at=std::chrono::steady_clock::now();
    if(blob && world.parallel_queue && count>=2048){
        uint32_t workers=std::min(4u,std::max(1u,std::thread::hardware_concurrency()));
        world.current().queue_work.enabled=1;world.current().queue_work.workers=workers;world.current().draws.resize(count);
        // Validate canonical object slice order before sharing disjoint ranges.
        for(uint32_t i=0;i<count;i++){const auto& d=draws[i];uint64_t at=reinterpret_cast<uintptr_t>(d.object_constants)-1;
            if(!d.object_constants || at%world.alignment || at!=object_cursor || at+d.object_bytes>object_bytes)throw std::runtime_error("invalid world object blob offset");object_cursor+=uint32_t(world_align(d.object_bytes));}
        auto range=[&](uint32_t begin,uint32_t end){critical_profile::Bind bind(&critical.workers[8+uint64_t(begin)*workers/count]);critical_profile::Scope task(critical_profile::QueueWorker,true);SevkQueueValidationStats stats{sizeof(SevkQueueValidationStats),SEVK_ABI_VERSION,world.texture_validation_cache?1u:0u};WorldTextureValidationCache<> cache;
            for(uint32_t i=begin;i<end;i++){const auto& d=draws[i];validate_world_packet(d,stats,cache);WorldDraw own{};own.packet=d;own.object_n=d.object_bytes;own.object_at=uint32_t(reinterpret_cast<uintptr_t>(d.object_constants)-1);
                if(world.pipelines[d.pipeline].type>=2){own.instance_n=d.instance_count*64;uint64_t at=reinterpret_cast<uintptr_t>(d.instances)-1;if(at%64 || at+own.instance_n>instance_bytes)throw std::runtime_error("invalid world instance blob offset");own.instance_at=uint32_t(at);}
                own.packet.object_constants=nullptr;own.packet.instances=nullptr;world.current().draws[i]=own;}
            return stats;};
        std::vector<std::future<SevkQueueValidationStats>> tasks;tasks.reserve(workers-1);
        for(uint32_t worker=1;worker<workers;worker++)tasks.emplace_back(std::async(std::launch::async,range,count*worker/workers,count*(worker+1)/workers));
        auto merge=[&](const SevkQueueValidationStats& stats){world.current().validation_stats.draws+=stats.draws;world.current().validation_stats.mesh_checks+=stats.mesh_checks;world.current().validation_stats.texture_checks+=stats.texture_checks;world.current().validation_stats.voxel_checks+=stats.voxel_checks;world.current().validation_stats.hits+=stats.hits;};
        merge(range(0,count/workers));for(auto& task:tasks)merge(task.get());
    }else{
        world.current().queue_work.workers=1;
    for(uint32_t i=0;i<count;++i){const auto& d=draws[i];validate_world_packet(d,world.current().validation_stats,validated_textures);bool instanced=world.pipelines[d.pipeline].type>=2;
        WorldDraw own{};own.packet=d;own.object_n=d.object_bytes;
        if(retained)own.object_at=world.object_records.select(object_ids[i],d.object_constants,d.object_bytes);
        else if(blob){uint64_t offset=reinterpret_cast<uintptr_t>(d.object_constants)-1;if(offset%world.alignment || offset!=object_cursor || offset+d.object_bytes>object_bytes)throw std::runtime_error("invalid world object blob offset");own.object_at=uint32_t(offset);object_cursor+=uint32_t(world_align(d.object_bytes));}
        else{own.object_at=object_cursor;std::memcpy(world.current().draw_objects.data()+object_cursor,d.object_constants,d.object_bytes);object_cursor+=uint32_t(world_align(d.object_bytes));}
        if(instanced){own.instance_n=d.instance_count*64;if(blob){uint64_t offset=reinterpret_cast<uintptr_t>(d.instances)-1;if(offset%64 || offset+own.instance_n>instance_bytes)throw std::runtime_error("invalid world instance blob offset");own.instance_at=uint32_t(offset);}else own.instance_at=world.current().instances.append(d.instances,own.instance_n);}
        own.packet.object_constants=nullptr;own.packet.instances=nullptr;world.current().draws.push_back(own);}
    }
    world.current().queue_work.draws=count;world.current().queue_work.validation_us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-validate_at).count());
    std::memcpy(world.current().frame.data(),frame,1280);std::memcpy(world.current().projection.data(),projection,64);std::memcpy(world.current().inverse.data(),inverse,64);world.current().queued=true;world.current().queue_us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-queued_at).count());
}
VkDescriptorSet world_set(WorldSlot& s,VkDescriptorSetLayout layout,VkDescriptorPool private_pool=nullptr){std::unique_lock<std::mutex> owner(world_descriptor_mutex,std::defer_lock);if(!private_pool)owner.lock();VkDescriptorSetAllocateInfo a{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};a.descriptorPool=private_pool?private_pool:s.pool;a.descriptorSetCount=1;a.pSetLayouts=&layout;VkDescriptorSet result{};check(vkAllocateDescriptorSets(device,&a,&result),"allocate world frame descriptors");return result;}
VkDescriptorSet world_cached_set(WorldSlot& s,VkDescriptorSetLayout layout,bool& keep){VkDescriptorSetAllocateInfo a{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};a.descriptorPool=s.material_pool;a.descriptorSetCount=1;a.pSetLayouts=&layout;VkDescriptorSet result{};if(vkAllocateDescriptorSets(device,&a,&result)==VK_SUCCESS){keep=true;return result;}keep=false;a.descriptorPool=s.pool;check(vkAllocateDescriptorSets(device,&a,&result),"allocate world material descriptors");return result;}
void release_cached_materials(WorldSlot& s){++s.material_epoch;if(s.material_pool)check(vkResetDescriptorPool(device,s.material_pool,0),"recycle cached world descriptors");s.material_cache.clear();s.material_stale=false;s.dirty_textures.clear();s.dirty_arrays.clear();s.dirty_voxels.clear();++performance.material_resets;}
void recycle_cached_materials(){for(auto& s:world.slots)s.material_stale=true;}
void recycle_cached_texture(uint64_t id){invalidate_world_recipes(0,id);for(auto& s:world.slots)s.dirty_textures.insert(id);if(world.shadow_alpha_textures.count(id))saved_shadows=false;}
void recycle_cached_array(uint64_t id){invalidate_world_recipes(5,id);for(auto& s:world.slots)s.dirty_arrays.insert(id);}
void recycle_cached_voxel(uint64_t id){invalidate_world_recipes(6,id);for(auto& s:world.slots)s.dirty_voxels.insert(id);}
// Called only after this descriptor slot's GPU completion. Other slots retain
// their sets and dirty identities until their own completion is known.
void release_dirty_materials(WorldSlot& s){
    if(s.dirty_textures.empty() && s.dirty_arrays.empty() && s.dirty_voxels.empty())return;
    std::vector<VkDescriptorSet> retired_sets;
    for(auto it=s.material_cache.begin();it!=s.material_cache.end();){const auto& k=it->first;
        bool dirty=k.kind&1? s.dirty_voxels.count(k.a)||s.dirty_arrays.count(k.b)||s.dirty_arrays.count(k.c)||s.dirty_arrays.count(k.d)||s.dirty_textures.count(k.e)||s.dirty_textures.count(k.f)||s.dirty_textures.count(0)
                            :s.dirty_textures.count(k.a)||s.dirty_textures.count(k.b)||s.dirty_textures.count(k.c)||s.dirty_textures.count(k.d);
        if(dirty){retired_sets.push_back(it->second);it=s.material_cache.erase(it);}else ++it;
    }
    if(!retired_sets.empty())check(vkFreeDescriptorSets(device,s.material_pool,uint32_t(retired_sets.size()),retired_sets.data()),"free replaced material descriptors");
    s.dirty_textures.clear();s.dirty_arrays.clear();s.dirty_voxels.clear();
}
void world_write_buffer(VkDescriptorSet set,uint32_t binding,Buffer& buffer,uint64_t bytes,VkDescriptorType type){VkDescriptorBufferInfo b{buffer.handle,0,bytes};VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstSet=set;w.dstBinding=binding;w.descriptorCount=1;w.descriptorType=type;w.pBufferInfo=&b;vkUpdateDescriptorSets(device,1,&w,0,nullptr);}
void world_write_image(VkDescriptorSet set,uint32_t binding,VkImageView view,VkSampler sample={},bool sampler_only=false){VkDescriptorImageInfo i{sample,view,sampler_only?VK_IMAGE_LAYOUT_UNDEFINED:VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstSet=set;w.dstBinding=binding;w.descriptorCount=1;w.descriptorType=sampler_only?VK_DESCRIPTOR_TYPE_SAMPLER:VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;w.pImageInfo=&i;vkUpdateDescriptorSets(device,1,&w,0,nullptr);}
WorldSetKey world_set_key(const WorldDraw& draw,uint32_t kind){
    WorldSetKey key{};key.bytes=draw.packet.object_bytes;key.kind=kind|(world_voxel(draw.packet.pipeline)?1u:0u);
    if(world_voxel(draw.packet.pipeline)){const auto& m=world.voxel_materials.at(draw.packet.color);key.a=draw.packet.color;key.b=m.arrays[0];key.c=m.arrays[1];key.d=m.arrays[2];key.e=m.random;key.f=draw.packet.alpha;}
    else {key.a=draw.packet.color;key.b=draw.packet.normal;key.c=draw.packet.extensions;key.d=draw.packet.alpha;}
    return key;
}
VkDescriptorSet world_material(WorldSlot& slot,const WorldDraw& draw,VkDescriptorSetLayout layout={},bool* persistent=nullptr){
    WorldSetKey key=world_set_key(draw,layout?2u:0u);if(!layout){auto cached=slot.material_cache.find(key);if(cached!=slot.material_cache.end()){++performance.material_hits;if(persistent)*persistent=true;return cached->second;}++performance.material_misses;}
    bool keep=false;auto set=layout?world_set(slot,layout):world_cached_set(slot,world.material_layout,keep);
    std::array<VkWriteDescriptorSet,24> writes{};std::array<VkDescriptorBufferInfo,8> buffers{};std::array<VkDescriptorImageInfo,16> images{};uint32_t nw=0,nb=0,ni=0;
    auto buffer=[&](uint32_t binding,Buffer& source,uint64_t bytes,VkDescriptorType type){buffers[nb]={source.handle,0,bytes};writes[nw]=VkWriteDescriptorSet{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[nw].dstSet=set;writes[nw].dstBinding=binding;writes[nw].descriptorCount=1;writes[nw].descriptorType=type;writes[nw].pBufferInfo=&buffers[nb];++nw;++nb;};
    auto image=[&](uint32_t binding,VkImageView view,VkSampler sample={},bool sampler_only=false){images[ni]={sample,view,sampler_only?VK_IMAGE_LAYOUT_UNDEFINED:VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};writes[nw]=VkWriteDescriptorSet{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[nw].dstSet=set;writes[nw].dstBinding=binding;writes[nw].descriptorCount=1;writes[nw].descriptorType=sampler_only?VK_DESCRIPTOR_TYPE_SAMPLER:VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;writes[nw].pImageInfo=&images[ni];++nw;++ni;};
    buffer(0,slot.frame,1280,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);buffer(1,slot.projections,64,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);buffer(2,slot.objects,draw.packet.object_bytes,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);
    if(world_voxel(draw.packet.pipeline)){const auto& m=world.voxel_materials.at(draw.packet.color);buffer(6,const_cast<Buffer&>(m.constants),30720,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);for(uint32_t i=0;i<6;++i)image(138+i,world.arrays.at(m.arrays[i%3]).view);image(157,m.random?textures.at(m.random).image.view:stock.dither.view);}
    const uint64_t ids[]{world_voxel(draw.packet.pipeline)?0:draw.packet.color,world_voxel(draw.packet.pipeline)?0:draw.packet.normal,world_voxel(draw.packet.pipeline)?0:draw.packet.extensions,draw.packet.alpha};for(uint32_t i=0;i<4;++i)image(128+i,textures.at(ids[i]).image.view);
    image(156,stock.dither.view);for(uint32_t i:{258u,259u})image(i,{},sampler,true);vkUpdateDescriptorSets(device,nw,writes.data(),0,nullptr);if(keep)slot.material_cache.emplace(key,set);if(persistent)*persistent=keep;return set;
}
VkDescriptorSet probe_geometry_set(WorldSlot& slot,const WorldDraw& draw,Buffer& frame,Buffer& projection){
    // Probe sets name this slot's probe uniforms and the CSM view. Those are not
    // part of the material key, so the set lives only in the frame pool.
    auto key=world_set_key(draw,2u);auto cached=slot.probe_materials.find(key);if(cached!=slot.probe_materials.end())return cached->second;
    auto set=world_set(slot,probes.material_layout);
    std::array<VkWriteDescriptorSet,24> writes{};std::array<VkDescriptorBufferInfo,8> buffers{};std::array<VkDescriptorImageInfo,16> images{};uint32_t nw=0,nb=0,ni=0;
    auto buffer=[&](uint32_t binding,Buffer& source,uint64_t bytes,VkDescriptorType type){buffers[nb]={source.handle,0,bytes};writes[nw]=VkWriteDescriptorSet{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[nw].dstSet=set;writes[nw].dstBinding=binding;writes[nw].descriptorCount=1;writes[nw].descriptorType=type;writes[nw].pBufferInfo=&buffers[nb];++nw;++nb;};
    auto image=[&](uint32_t binding,VkImageView view,VkSampler sample={},bool sampler_only=false){images[ni]={sample,view,sampler_only?VK_IMAGE_LAYOUT_UNDEFINED:VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};writes[nw]=VkWriteDescriptorSet{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[nw].dstSet=set;writes[nw].dstBinding=binding;writes[nw].descriptorCount=1;writes[nw].descriptorType=sampler_only?VK_DESCRIPTOR_TYPE_SAMPLER:VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;writes[nw].pImageInfo=&images[ni];++nw;++ni;};
    buffer(0,frame,1280,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);buffer(1,projection,64,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);buffer(2,slot.objects,draw.packet.object_bytes,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);buffer(4,transparency.shadow_constants[slot_index],1200,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);buffer(7,probes.forward_projection[slot_index][probes.work_face],64,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
    if(world_voxel(draw.packet.pipeline)){const auto& material=world.voxel_materials.at(draw.packet.color);buffer(6,const_cast<Buffer&>(material.constants),30720,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);for(uint32_t j=0;j<6;++j)image(138+j,world.arrays.at(material.arrays[j%3]).view);image(157,material.random?textures.at(material.random).image.view:stock.dither.view);}
    uint64_t ids[]{world_voxel(draw.packet.pipeline)?0:draw.packet.color,world_voxel(draw.packet.pipeline)?0:draw.packet.normal,world_voxel(draw.packet.pipeline)?0:draw.packet.extensions,draw.packet.alpha};for(uint32_t j=0;j<4;++j)image(128+j,textures.at(ids[j]).image.view);image(144,transparency.csm.view);image(156,stock.dither.view);for(uint32_t j:{258u,259u})image(j,{},sampler,true);image(271,{},shadow_sampler,true);
    vkUpdateDescriptorSets(device,nw,writes.data(),0,nullptr);slot.probe_materials.emplace(key,set);return set;
}
void world_viewport(VkCommandBuffer cmd,uint32_t width,uint32_t height){VkViewport v{0,float(height),float(width),-float(height),0,1};VkRect2D s{{0,0},{width,height}};vkCmdSetViewport(cmd,0,1,&v);vkCmdSetScissor(cmd,0,1,&s);}
void world_targets(){
    if(world.width==extent.width && world.height==extent.height)return;for(auto& i:world.gbuffer)retire_image(i);retire_image(world.hdr);retire_image(world.shadows);world.initialized=false;
    for(uint32_t i=0;i<3;++i)world.gbuffer[i]=make_image(extent.width,extent.height,stock_formats[i],VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT,VK_IMAGE_ASPECT_COLOR_BIT);
    world.gbuffer[3]=make_image(extent.width,extent.height,VK_FORMAT_D32_SFLOAT,VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT,VK_IMAGE_ASPECT_DEPTH_BIT);
    world.hdr=make_image(extent.width,extent.height,VK_FORMAT_R16G16B16A16_SFLOAT,VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT|(ray.supported&&ray.hdr_storage?VK_IMAGE_USAGE_STORAGE_BIT:0),VK_IMAGE_ASPECT_COLOR_BIT);
    world.shadows=make_image(extent.width,extent.height,VK_FORMAT_R8_UNORM,VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_STORAGE_BIT,VK_IMAGE_ASPECT_COLOR_BIT);world.width=extent.width;world.height=extent.height;
}
void world_light_data(WorldSlot& s){
    uint32_t points=0,spots=0;std::array<float,64*12> data{};const float* view=reinterpret_cast<const float*>(world.current().frame.data());
    auto transform=[&](const float* p,float* o){for(uint32_t c=0;c<3;++c)o[c]=p[0]*view[c]+p[1]*view[4+c]+p[2]*view[8+c]+view[12+c];};
    for(uint32_t i=0;i<light_count;++i){const auto& l=lights[i];if(l.flags&1){float* dest=reinterpret_cast<float*>(static_cast<uint8_t*>(s.spots.mapped)+world_align(208)*spots++);std::memset(dest,0,208);for(uint32_t j=0;j<4;++j){dest[j*5]=1;dest[16+j*5]=1;}std::memcpy(dest+32,l.position,12);dest[35]=l.range;
            for(uint32_t c=0;c<3;++c)dest[36+c]=l.color[c]*l.intensity;dest[39]=(l.flags&2)?std::bit_cast<float>(l.reserved[0]):1;dest[40]=(l.flags&2)?std::bit_cast<float>(l.reserved[2]):1;dest[41]=(l.flags&2)?std::bit_cast<float>(l.reserved[1]):1;dest[45]=1;dest[47]=l.cone_cosine;std::memcpy(dest+48,l.direction,12);dest[51]=float(this->spots.resolution);if(auto config=find_spot(i)){std::memcpy(dest+16,config->projection,64);for(uint32_t row=0;row<4;++row){dest[16+row*4]=.5f*(config->projection[row*4]+config->projection[row*4+3]);dest[17+row*4]=.5f*(-config->projection[row*4+1]+config->projection[row*4+3]);}}
        }else {float* dest=data.data()+points++*12;transform(l.position,dest);dest[3]=l.range;for(uint32_t c=0;c<3;++c)dest[4+c]=l.color[c]*l.intensity;dest[7]=(l.flags&2)?std::bit_cast<float>(l.reserved[0]):1;dest[8]=(l.flags&2)?std::bit_cast<float>(l.reserved[2]):1;dest[9]=(l.flags&2)?std::bit_cast<float>(l.reserved[1]):1;}}
    world_buffer(s.point_lights,std::max(1u,points)*48,VK_BUFFER_USAGE_STORAGE_BUFFER_BIT);std::memcpy(s.point_lights.mapped,data.data(),std::max(1u,points)*48);check(vmaFlushAllocation(allocator,s.point_lights.memory,0,VK_WHOLE_SIZE),"flush stock point lights");
    light_tiles.points=points;light_tiles.stats.enabled=light_tiles.requested?1u:0u;light_tiles.stats.dispatches=0;light_tiles.stats.points=points;light_tiles.stats.frame=work.world_frames+1;
    uint32_t tx=(extent.width+15)/16,ty=(extent.height+15)/16,tiles=tx*ty;light_tiles.stats.tiles=tiles;
    if(light_tiles.requested){prepare_light_tile_storage(s,tiles,points);check(vmaFlushAllocation(allocator,s.spots.memory,0,VK_WHOLE_SIZE),"flush stock spot constants");return;}
    world_buffer(s.tiles,uint64_t(tiles)*65*4,VK_BUFFER_USAGE_STORAGE_BUFFER_BIT);auto* indices=static_cast<uint32_t*>(s.tiles.mapped);
    for(uint32_t tile=0;tile<tiles;++tile){indices[tile]=points;for(uint32_t i=0;i<64;++i)indices[tiles+tile*64+i]=i;}check(vmaFlushAllocation(allocator,s.tiles.memory,0,uint64_t(tiles)*65*4),"flush conservative stock tiled light lists");check(vmaFlushAllocation(allocator,s.spots.memory,0,VK_WHOLE_SIZE),"flush stock spot constants");
}
VkDescriptorSet world_light_set(WorldSlot& s,bool spot=false){auto set=world_set(s,spot?world.spot_layout:world.light_layout);
    world_write_buffer(set,0,s.frame,1280,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,1,s.spots,208,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);world_write_buffer(set,4,s.glue,352,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,5,s.dimensions,32,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
    world_write_image(set,128,world.gbuffer[3].view);for(uint32_t i=0;i<3;++i)world_write_image(set,129+i,world.gbuffer[i].view);
    world_write_image(set,138,world.skybox && world.cubes.count(world.skybox)?world.cubes.at(world.skybox).view:world.black.view);world_write_image(set,139,probes.ready?probes.final[1].image.view:world.ambient.view);world_write_image(set,145,probes.ready?probes.final[0].image.view:world.ambient.view);
    world_write_image(set,140,(post.config.flags&1)&&post.ao.image.handle?post.ao.image.view:textures.at(0).image.view);world_write_image(set,143,textures.at(probes.ready?probes.config.brdf:0).image.view);world_write_image(set,147,world.shadows.view);world_write_image(set,152,post.output?post.output->image.view:world.hdr.view);for(uint32_t i=0;i<4;++i)world_write_image(set,148+i,shadow_images[i].view);
    if(spot){world_write_image(set,141,textures.at(0).image.view);world_write_image(set,142,shadow_images[0].view);world_write_image(set,271,{},shadow_sampler,true);}else {world_write_buffer(set,141,s.point_lights,s.point_lights.bytes,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);world_write_buffer(set,142,light_tiles.requested?s.light_tile_gpu:s.tiles,light_tiles.requested?s.light_tile_gpu.bytes:s.tiles.bytes,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);}
    for(uint32_t b:{256u,258u,259u})world_write_image(set,b,{},sampler,true);world_write_image(set,260,{},shadow_sampler,true);return set;}
void world_fullscreen(VkCommandBuffer cmd,VkPipeline pipeline,VkDescriptorSet set,VkPipelineLayout layout,uint32_t offset=0){vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,layout,0,1,&set,1,&offset);VkDeviceSize zero=0;vkCmdBindVertexBuffers(cmd,0,1,&world.quad.handle,&zero);world_viewport(cmd,extent.width,extent.height);vkCmdDraw(cmd,3,1,0,0);}
void prepare_world_indirect(){
    world.indirect_stats=SevkIndirectStats{sizeof(SevkIndirectStats),SEVK_ABI_VERSION,world.indirect_supported?1u:0u,(world.indirect_supported&&world.indirect_requested)?1u:0u};
    if(!world.indirect_stats.enabled)return;
    world.current().main_selection.clear();world.current().indirect_packets.clear();world.current().indirect_commands.clear();
    for(uint32_t i:world.current().order)if(!(world.current().draws[i].packet.flags&(4096|8192)))world.current().main_selection.push_back(i);
    for(const auto& d:world.current().draws){const auto& p=d.packet;
        bool eligible=world.pipelines[p.pipeline].indirect_safe && world.pipelines[world_base_pipeline(p.pipeline)+32].indirect_safe;
        auto& mesh=*d.geometry;
        world.current().indirect_packets.push_back({p.mesh,(uint64_t)d.material,p.pipeline,mesh.index_count,p.instance_count,d.instance_at,d.object_at,d.object_n,eligible,(uint64_t)mesh.vertices.handle,(uint64_t)mesh.indices.handle,uint32_t(mesh.index_offset/4),int32_t(mesh.vertex_offset/mesh.vertex_stride),mesh.vertex_allocation!=0});}
    for(uint32_t pass=0;pass<=shadow_count;++pass){auto& plan=world.current().indirect_plans[pass];
        const auto& selection=pass==0?world.current().main_selection:world.current().cascade_casters[pass-1];
        plan.build(selection,world.current().indirect_packets,world.retained_objects?world.object_records.data:world.current().draw_objects,world.indirect_limit);
        world.current().indirect_bases[pass]=uint32_t(world.current().indirect_commands.size());
        world.current().indirect_commands.insert(world.current().indirect_commands.end(),plan.commands.begin(),plan.commands.end());}
    world.indirect_stats.prepared_commands=uint32_t(world.current().indirect_commands.size());
    if(!world.current().indirect_commands.empty()){auto& buffer=world.indirect_buffers[slot_index];uint64_t bytes=world.current().indirect_commands.size()*sizeof(WorldIndirectPlan::Command);
        world_buffer(buffer,bytes,VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT);std::memcpy(buffer.mapped,world.current().indirect_commands.data(),bytes);
        check(vmaFlushAllocation(allocator,buffer.memory,0,bytes),"flush fenced world indirect commands");}
}
struct GeometryRecordCounts{uint32_t commands{},groups{},packets{};};
GeometryRecordCounts record_world_selection(VkCommandBuffer cmd,WorldSlot& slot,std::span<const uint32_t> selection,int cascade,const WorldIndirectPlan* plan=nullptr,uint32_t base=0){
    critical_profile::Scope critical_geometry(cascade<0?critical_profile::MainRecord:critical_profile::CascadeOwner);
    GeometryRecordCounts counts{};bool voxel_marker=false;
    const auto& object_offsets=world.current().object_offsets;const auto& instance_offsets=world.current().instance_offsets;const auto& materials=world.current().materials;
    struct BindCache{VkPipeline pipe{};VkDescriptorSet set{};uint32_t offset0=~0u,offset1=~0u,streams{},instance{};VkBuffer vertex{},index{};VkDeviceSize vertex_at{},index_at{};} cache{};
    auto emit=[&](uint32_t i,int cascade,uint32_t indirect_count=0,uint32_t command_start=0){const auto& draw=world.current().draws[i];const auto& p=world.pipelines[cascade<0?draw.packet.pipeline:world_base_pipeline(draw.packet.pipeline)+32];if(cascade<0 && world_voxel(draw.packet.pipeline) && !voxel_marker){frame_mark("gbuffer voxels");voxel_marker=true;}auto& mesh=*draw.geometry;uint32_t offsets[]{cascade<0?0:static_cast<uint32_t>(world_align(64)*(cascade+1)),object_offsets[i]};
        VkPipeline handle=cascade>=0 && cascade<8 && shadow_pass_constants_provided?p.stock_cascade:p.handle;if(!handle)throw std::runtime_error("stock shadow rasterizer unavailable");
        if(cache.pipe!=handle){vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,handle);cache.pipe=handle;}
        if(cache.set!=materials[i]||cache.offset0!=offsets[0]||cache.offset1!=offsets[1]){vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,world.material_pipeline_layout,0,1,&materials[i],2,offsets);cache.set=materials[i];cache.offset0=offsets[0];cache.offset1=offsets[1];}
        uint32_t streams=p.type>=2?2u:1u,instance=streams>1&&!indirect_count?instance_offsets[i]:0u;
        VkDeviceSize vertex_at=indirect_count?0:mesh.vertex_offset,index_at=indirect_count?0:mesh.index_offset;
        if(cache.vertex!=mesh.vertices.handle||cache.index!=mesh.indices.handle||cache.streams!=streams||cache.instance!=instance||cache.vertex_at!=vertex_at||cache.index_at!=index_at){VkBuffer buffers[]{mesh.vertices.handle,slot.instances.handle};VkDeviceSize positions[]{vertex_at,instance};vkCmdBindVertexBuffers(cmd,0,streams,buffers,positions);vkCmdBindIndexBuffer(cmd,mesh.indices.handle,index_at,VK_INDEX_TYPE_UINT32);cache.vertex=mesh.vertices.handle;cache.index=mesh.indices.handle;cache.streams=streams;cache.instance=instance;cache.vertex_at=vertex_at;cache.index_at=index_at;}
        if(indirect_count){vkCmdDrawIndexedIndirect(cmd,world.indirect_buffers[slot_index].handle,uint64_t(command_start)*sizeof(WorldIndirectPlan::Command),indirect_count,sizeof(WorldIndirectPlan::Command));counts.packets+=indirect_count;++counts.groups;}
        else vkCmdDrawIndexed(cmd,mesh.index_count,draw.packet.instance_count,0,0,0);
        ++counts.commands;};

    if(plan){for(const auto& run:plan->runs)emit(selection[run.selection_start],cascade,run.indirect?run.packet_count:0,base+run.command_start);}
    else for(uint32_t i:selection)emit(i,cascade);
    return counts;
}
void render_guest_depth_views(VkCommandBuffer cmd,WorldSlot& slot) {
    if(!guest.depth_configured)return;
    for(uint32_t view=0;view<guest.depth_count;view++) {
        auto found=guest.images.find(guest.depth_images[view]);
        if(found==guest.images.end() || found->second.format!=VK_FORMAT_D32_SFLOAT) throw std::runtime_error("guest scene-depth target retired before recording");
        auto& image=found->second;
        barrier(cmd,image.image.handle,image.layout,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,
            VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,image.layout==VK_IMAGE_LAYOUT_UNDEFINED?0:VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);
        VkRenderingAttachmentInfo depth{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};
        depth.imageView=image.image.view;depth.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;depth.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;depth.storeOp=VK_ATTACHMENT_STORE_OP_STORE;depth.clearValue.depthStencil={1,0};
        VkRenderingInfo info{VK_STRUCTURE_TYPE_RENDERING_INFO};info.renderArea={{0,0},{image.width,image.height}};info.layerCount=1;info.pDepthAttachment=&depth;
        vkCmdBeginRendering(cmd,&info);world_viewport(cmd,image.width,image.height);
        std::vector<uint32_t> casters;
        for(uint32_t i:world.current().order) {
            const auto& packet=world.current().draws[i].packet;
            if(!(packet.flags&512u) && (packet.reserved&(1u<<(8+view))))casters.push_back(i);
        }
        // Projection slots 73..75 follow the eight stock views and 64 spots.
        // The regular depth-material variants keep alpha/skin/instance/voxel
        // behavior; these views never borrow or replace stock cascade maps.
        record_world_selection(cmd,slot,casters,int(72+view));
        vkCmdEndRendering(cmd);
        barrier(cmd,image.image.handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,
            VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,
            VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        image.layout=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;guest.depth_ready|=1u<<view;
    }
    guest.depth_configured=false;
}
VkDescriptorPool cascade_descriptor_pool(uint32_t cascade){
    auto& pool=world.cascade_descriptor_pools[slot_index][cascade];
    if(pool)return pool;
    std::array<VkDescriptorPoolSize,5> sizes{{{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,16384},{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC,8192},{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,65536},{VK_DESCRIPTOR_TYPE_SAMPLER,16384},{VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,256}}};
    VkDescriptorPoolCreateInfo info{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};info.maxSets=4096;info.poolSizeCount=uint32_t(sizes.size());info.pPoolSizes=sizes.data();
    check(vkCreateDescriptorPool(device,&info,nullptr,&pool),"create cascade descriptor pool");
    return pool;
}
void prepare_cascade_commands(){
    for(uint32_t c=0;c<shadow_count;++c){if(scheduled_shadows && !(shadow_update_mask&(1u<<c)))continue;auto& pool=world.cascade_pools[slot_index][c];auto& command=world.cascade_commands[slot_index][c];
        if(!pool){VkCommandPoolCreateInfo info{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};info.queueFamilyIndex=queue_family;check(vkCreateCommandPool(device,&info,nullptr,&pool),"create frame-owned cascade pool");
            VkCommandBufferAllocateInfo allocation{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};allocation.commandPool=pool;allocation.level=VK_COMMAND_BUFFER_LEVEL_SECONDARY;allocation.commandBufferCount=1;check(vkAllocateCommandBuffers(device,&allocation,&command),"allocate cascade secondary");}
        check(vkResetCommandPool(device,pool,0),"reset completed cascade pool");
        auto descriptors=cascade_descriptor_pool(c);check(vkResetDescriptorPool(device,descriptors,0),"reset cascade descriptor pool");}
}
GeometryRecordCounts record_cascade_secondary(uint32_t c){
    critical_profile::Bind critical_lane(&critical.workers[c]);
    critical_profile::Scope critical_worker(critical_profile::CascadeWorker,true);
    auto command=world.cascade_commands[slot_index][c];
    VkCommandBufferInheritanceRenderingInfo rendering{VK_STRUCTURE_TYPE_COMMAND_BUFFER_INHERITANCE_RENDERING_INFO};rendering.depthAttachmentFormat=VK_FORMAT_D32_SFLOAT;rendering.rasterizationSamples=VK_SAMPLE_COUNT_1_BIT;
    VkCommandBufferInheritanceInfo inheritance{VK_STRUCTURE_TYPE_COMMAND_BUFFER_INHERITANCE_INFO,&rendering};
    VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};begin.flags=VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT|VK_COMMAND_BUFFER_USAGE_RENDER_PASS_CONTINUE_BIT;begin.pInheritanceInfo=&inheritance;
    check(vkBeginCommandBuffer(command,&begin),"begin cascade secondary");world_viewport(command,shadow_resolution,shadow_resolution);
    auto descriptors=world.cascade_descriptor_pools[slot_index][c];
    auto counts=record_world_selection(command,world.slots[slot_index],world.current().cascade_casters[c],int(c),world.indirect_stats.enabled?&world.current().indirect_plans[c+1]:nullptr,world.current().indirect_bases[c+1]);
    counts.commands+=record_pass_view(command,world.slots[slot_index],c+1,false,nullptr,nullptr,0,descriptors);
    check(vkEndCommandBuffer(command),"end cascade secondary");return counts;
}
void render_world(){
    std::optional<critical_profile::Scope> critical_prepare;critical_prepare.emplace(critical_profile::WorldPrepare,true);
    auto profile_stamp=std::chrono::steady_clock::now();
    auto profile_take=[&](){auto now=std::chrono::steady_clock::now();uint32_t elapsed=(uint32_t)std::chrono::duration_cast<std::chrono::microseconds>(now-profile_stamp).count();profile_stamp=now;return elapsed;};
    prepare_shadow_targets(slots[slot_index].command);world_targets();auto& slot=world.slots[slot_index];auto cmd=slots[slot_index].command;check(vkResetDescriptorPool(device,slot.pool,0),"recycle completed world descriptors");slot.probe_materials.clear();
    std::memcpy(slot.frame.mapped,world.current().frame.data(),1280);std::memcpy(slot.projections.mapped,world.current().projection.data(),64);for(uint32_t i=0;i<8;++i)std::memcpy(static_cast<uint8_t*>(slot.projections.mapped)+world_align(64)*(i+1),shadow_matrices[i],64);
    for(auto& config:spots.config)std::memcpy(static_cast<uint8_t*>(slot.projections.mapped)+world_align(64)*(9+config.light),config.projection,64);
    if(guest.depth_configured)for(uint32_t i=0;i<guest.depth_count;i++)
        std::memcpy(static_cast<uint8_t*>(slot.projections.mapped)+world_align(64)*(73+i),guest.depth_matrices[i],64);
    auto& object_offsets=world.current().object_offsets;auto& instance_offsets=world.current().instance_offsets;object_offsets.clear();instance_offsets.clear();uint64_t object_bytes=world.retained_objects?world.object_records.data.size():world.current().draw_objects.size(),instance_bytes=world.current().instances.data.size();
    object_offsets.reserve(world.current().draws.size());instance_offsets.reserve(world.current().draws.size());
    for(auto& draw:world.current().draws){draw.gpu_object_at=draw.object_at;if(draw.recipe){if(!draw.recipe->alive || draw.recipe->generation!=draw.recipe_generation)throw std::runtime_error("retired queued recipe");validate_world_recipe(*draw.recipe);draw.geometry=draw.recipe->geometry;}else draw.geometry=&world.meshes.at(draw.packet.mesh);object_offsets.push_back(draw.gpu_object_at);instance_offsets.push_back(draw.instance_at);}
    auto perf_prepare_stamp=std::chrono::steady_clock::now();performance.geometry_us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(perf_prepare_stamp-profile_stamp).count());
    auto perf_prepare_take=[&](){auto now=std::chrono::steady_clock::now();auto us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(now-perf_prepare_stamp).count());perf_prepare_stamp=now;return us;};
    bool material_buffers_moved=!slot.objects.handle || slot.objects.bytes<object_bytes || !slot.instances.handle || slot.instances.bytes<std::max(instance_bytes,uint64_t(64)) || !slot.frame.handle || !slot.projections.handle;
    if(material_buffers_moved||slot.material_stale)release_cached_materials(slot);
    else release_dirty_materials(slot);
    world_buffer(slot.objects,std::max(object_bytes,uint64_t(3968)),VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);world_buffer(slot.instances,std::max(instance_bytes,uint64_t(64)),VK_BUFFER_USAGE_VERTEX_BUFFER_BIT);
    world.object_uploaded_bytes=0;
    if(world.retained_objects){
        if(material_buffers_moved)slot.object_versions.clear();
        slot.object_versions.resize((object_bytes+world.alignment-1)/world.alignment);
        uint64_t first=UINT64_MAX,last=0;
        for(uint64_t id:world.object_records.selected){const auto& record=world.object_records.records.at(id);auto& version=slot.object_versions[record.offset/world.alignment];
            if(version==record.revision)continue;
            std::memcpy(static_cast<uint8_t*>(slot.objects.mapped)+record.offset,world.object_records.data.data()+record.offset,record.bytes);
            version=record.revision;first=std::min(first,uint64_t(record.offset));last=std::max(last,uint64_t(record.offset)+record.bytes);world.object_uploaded_bytes+=record.bytes;}
        if(last)check(vmaFlushAllocation(allocator,slot.objects.memory,first,last-first),"flush changed retained objects");
    }else if(object_bytes){std::memcpy(slot.objects.mapped,world.current().draw_objects.data(),size_t(object_bytes));world.object_uploaded_bytes=object_bytes;}
    if(instance_bytes)std::memcpy(slot.instances.mapped,world.current().instances.data.data(),instance_bytes);
    struct Glue{float inverse[16],matrices[64],splits[4];uint32_t count,width,height,reserved;};static_assert(sizeof(Glue)==352);Glue glue{};std::memcpy(glue.inverse,world.current().inverse.data(),64);std::memcpy(glue.matrices,shadow_matrices,sizeof(glue.matrices));std::memcpy(glue.splits,shadow_splits,sizeof(glue.splits));glue.count=std::min(4u,shadow_count);glue.width=extent.width;glue.height=extent.height;std::memcpy(slot.glue.mapped,&glue,sizeof(glue));
    for(auto* b:{&slot.frame,&slot.projections,&slot.instances,&slot.glue})check(vmaFlushAllocation(allocator,b->memory,0,VK_WHOLE_SIZE),"flush batched world uniforms/instances");if(!world.retained_objects)check(vmaFlushAllocation(allocator,slot.objects.memory,0,VK_WHOLE_SIZE),"flush legacy world objects");world_light_data(slot);
    performance.upload_us=perf_prepare_take();
    auto& materials=world.current().materials;materials.clear();materials.reserve(world.current().draws.size());for(auto& draw:world.current().draws){draw.material=draw.recipe?world_recipe_material(slot,draw):world_material(slot,draw);materials.push_back(draw.material);}
    prepare_pass_gpu(slot);
    performance.descriptors_us=perf_prepare_take();
    auto& order=world.current().order;order.resize(world.current().draws.size());for(uint32_t n=0;n<order.size();++n)order[n]=n;
    std::sort(order.begin(),order.end(),[&](uint32_t a,uint32_t b){const auto& A=world.current().draws[a].packet;const auto& B=world.current().draws[b].packet;if(A.pipeline!=B.pipeline)return A.pipeline<B.pipeline;if(A.mesh!=B.mesh)return A.mesh<B.mesh;return materials[a]<materials[b];});
    for(auto& casters:world.current().cascade_casters)casters.clear();
    if(scheduled_shadows)shadow_update_mask|=((1u<<shadow_count)-1)&~shadow_image_mask;
    for(uint32_t i:order){const auto& packet=world.current().draws[i].packet;if(packet.flags&(512|8192))continue;for(uint32_t c=0;c<shadow_count;c++)if((packet.reserved&(1u<<c)) && (!scheduled_shadows || (shadow_update_mask&(1u<<c))))world.current().cascade_casters[c].push_back(i);}
    prepare_world_indirect();
    auto merge_shadow=[&](GeometryRecordCounts count){performance.shadow_commands+=count.commands;world.indirect_stats.shadow_groups+=count.groups;world.indirect_stats.grouped_draws+=count.packets;};
    auto emit_cascade=[&](uint32_t c){merge_shadow(record_world_selection(cmd,slot,world.current().cascade_casters[c],int(c),world.indirect_stats.enabled?&world.current().indirect_plans[c+1]:nullptr,world.current().indirect_bases[c+1]));performance.shadow_commands+=record_pass_view(cmd,slot,c+1);};
    performance.sort_us=perf_prepare_take();work.prepare_us=profile_take();critical_prepare.reset();
    // Membership changes must invalidate cached cascades, even with no moving
    // caster left in the packet (e.g. deletion or visibility changes).
    world.shadow_alpha_textures.clear();bool any_dynamic=false;uint64_t caster_signature=14695981039346656037ull;
    auto hash_caster=[&](uint64_t value){caster_signature^=value;caster_signature*=1099511628211ull;};
    if(!scheduled_shadows)for(const auto& draw:world.current().draws){const auto& p=draw.packet;uint32_t stock_membership=p.reserved&((1u<<shadow_count)-1);if((p.flags&(512|8192)) || !stock_membership)continue;if(p.alpha)world.shadow_alpha_textures.insert(p.alpha);any_dynamic|=(p.flags&0x1000000u)!=0;hash_caster(p.mesh);hash_caster(p.pipeline);hash_caster(p.instance_count);hash_caster(stock_membership);hash_caster(p.alpha);hash_caster(p.flags&~0x1000000u);for(float v:p.origin)hash_caster(std::bit_cast<uint32_t>(v));}
    bool reuse_shadows=saved_shadows && !any_dynamic && caster_signature==saved_caster_signature && saved_shadow_count==shadow_count && std::memcmp(saved_shadow_matrices,shadow_matrices,sizeof(saved_shadow_matrices))==0;
    world.cascade_tasks=world.cascade_task_commands=0;
    std::array<std::future<GeometryRecordCounts>,8> cascade_jobs;
    uint32_t updates=scheduled_shadows?shadow_update_mask:reuse_shadows?0u:(1u<<shadow_count)-1;
    bool parallel=world.parallel_cascades && std::popcount(updates)>1;
    if(parallel){prepare_cascade_commands();for(uint32_t c=0;c<shadow_count;++c)if(updates&(1u<<c)){cascade_jobs[c]=std::async(std::launch::async,[this,c]{return record_cascade_secondary(c);});++world.cascade_tasks;}}
    frame_mark("spot shadows");render_spot_shadows(cmd,slot,object_offsets,instance_offsets);work.spot_us=profile_take();frame_mark("cascades");
    if(updates){for(uint32_t c=0;c<shadow_count;++c){if(!(updates&(1u<<c)))continue;auto& image=shadow_images[c];barrier(cmd,image.handle,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,(VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT),VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);
        VkRenderingAttachmentInfo d{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};d.imageView=image.view;d.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;d.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;d.storeOp=VK_ATTACHMENT_STORE_OP_STORE;d.clearValue.depthStencil={1,0};VkRenderingInfo r{VK_STRUCTURE_TYPE_RENDERING_INFO};r.renderArea={{0,0},{shadow_resolution,shadow_resolution}};r.layerCount=1;r.pDepthAttachment=&d;if(parallel)r.flags=VK_RENDERING_CONTENTS_SECONDARY_COMMAND_BUFFERS_BIT;vkCmdBeginRendering(cmd,&r);world_viewport(cmd,shadow_resolution,shadow_resolution);
        if(parallel){GeometryRecordCounts count;{critical_profile::Scope join(critical_profile::CascadeJoin);count=cascade_jobs[c].get();}merge_shadow(count);world.cascade_task_commands+=count.commands;vkCmdExecuteCommands(cmd,1,&world.cascade_commands[slot_index][c]);}else emit_cascade(c);vkCmdEndRendering(cmd);barrier(cmd,image.handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,(VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT),VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);render_shadow_occlusion(cmd,slot,c);}shadow_image_mask|=updates;std::memcpy(saved_shadow_matrices,shadow_matrices,sizeof(saved_shadow_matrices));saved_shadow_count=shadow_count;saved_caster_signature=caster_signature;saved_shadows=true;}render_guest_depth_views(cmd,slot);work.shadow_us=profile_take();frame_mark("gbuffer");
    for(uint32_t i=0;i<4;++i){bool d=i==3;barrier(cmd,world.gbuffer[i].handle,world.initialized?VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:VK_IMAGE_LAYOUT_UNDEFINED,d?VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL:VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,d?VK_IMAGE_ASPECT_DEPTH_BIT:VK_IMAGE_ASPECT_COLOR_BIT,world.initialized?(VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT):VK_PIPELINE_STAGE_2_NONE,world.initialized?VK_ACCESS_2_SHADER_SAMPLED_READ_BIT:0,d?VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT:VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,d?VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT:VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);}
    std::array<VkRenderingAttachmentInfo,3> colors{};for(uint32_t i=0;i<3;++i){colors[i].sType=VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;colors[i].imageView=world.gbuffer[i].view;colors[i].imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;colors[i].loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;colors[i].storeOp=VK_ATTACHMENT_STORE_OP_STORE;}
    VkRenderingAttachmentInfo d{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};d.imageView=world.gbuffer[3].view;d.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;d.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;d.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
    VkRenderingInfo r{VK_STRUCTURE_TYPE_RENDERING_INFO};r.renderArea={{0,0},extent};r.layerCount=1;r.colorAttachmentCount=3;r.pColorAttachments=colors.data();r.pDepthAttachment=&d;vkCmdBeginRendering(cmd,&r);world_viewport(cmd,extent.width,extent.height);
    prepare_foliage(slot);
    frame_mark("gbuffer geometry");bool voxel_frame=false;
    auto main_packet=[&](uint32_t i,bool record,uint32_t count=0,uint32_t start=0){if(world_voxel(world.current().draws[i].packet.pipeline)){++world.voxel_draws;voxel_frame=true;}++work.world_draws;work.world_instances+=world.current().draws[i].packet.instance_count;auto type=world.pipelines[world.current().draws[i].packet.pipeline].type;if(type==1)++work.skinned_draws;if(type>=3)++work.cube_draws;++stats.mesh_draws;};
    if(!world.indirect_stats.enabled){world.current().main_selection.clear();for(uint32_t i:order)if(!(world.current().draws[i].packet.flags&(4096|8192)))world.current().main_selection.push_back(i);}
    auto main_count=record_world_selection(cmd,slot,world.current().main_selection,-1,world.indirect_stats.enabled?&world.current().indirect_plans[0]:nullptr,world.current().indirect_bases[0]);
    performance.main_commands+=main_count.commands;world.indirect_stats.main_groups+=main_count.groups;world.indirect_stats.grouped_draws+=main_count.packets;
    performance.main_commands+=record_pass_view(cmd,slot,0);
    for(uint32_t i:world.current().main_selection)main_packet(i,false);
    if(voxel_frame)++world.voxel_frames;frame_mark("foliage");render_foliage(cmd,slot);vkCmdEndRendering(cmd);frame_mark("object motion");render_object_motion(cmd,slot);frame_mark("gbuffer attachment");render_gbuffer_attachments(cmd,slot);work.main_us=profile_take();
    for(uint32_t i=0;i<4;++i){bool depth=i==3;barrier(cmd,world.gbuffer[i].handle,depth?VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL:VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,depth?VK_IMAGE_ASPECT_DEPTH_BIT:VK_IMAGE_ASPECT_COLOR_BIT,depth?VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT:VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,depth?VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT|VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT:VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,(VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT),VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);}
    frame_mark("occlusion");render_occlusion(cmd,slot);frame_mark("opaque decals");prepare_decals(cmd,slot);render_opaque_decals(cmd,slot);
    frame_mark("environment prepare");prepare_environment(cmd,slot);
    frame_mark("CSM copy");prepare_csm(cmd);render_probes(cmd,slot,object_offsets,instance_offsets);work.probe_us=profile_take();
    critical_profile::Scope critical_lighting(critical_profile::LightingPostRecord,true);
    frame_mark("postprocess prepare");prepare_postprocess(cmd,slot);
    frame_mark("light tile selection");dispatch_light_tiles(cmd,slot);
    frame_mark("shadow resolve");auto lighting=world_light_set(slot);
    auto attachment=[&](Image& image){barrier(cmd,image.handle,world.initialized?VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,world.initialized?(VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT):VK_PIPELINE_STAGE_2_NONE,world.initialized?VK_ACCESS_2_SHADER_SAMPLED_READ_BIT:0,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);VkRenderingAttachmentInfo a{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};a.imageView=image.view;a.imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;a.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;a.storeOp=VK_ATTACHMENT_STORE_OP_STORE;VkRenderingInfo info{VK_STRUCTURE_TYPE_RENDERING_INFO};info.renderArea={{0,0},extent};info.layerCount=1;info.colorAttachmentCount=1;info.pColorAttachments=&a;vkCmdBeginRendering(cmd,&info);};
    if(post.config.flags&1){render_stock_shadows(cmd,slot);gather_shadow_usage(cmd,slot,0,slot.frame,world.gbuffer[3],extent.width,extent.height);}
    else{attachment(world.shadows);world_fullscreen(cmd,world.resolve,lighting,world.light_pipeline_layout);vkCmdEndRendering(cmd);barrier(cmd,world.shadows.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,(VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT),VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);}
    frame_mark("ray sun visibility");trace_hardware_rays(cmd);frame_mark("deferred lighting");attachment(world.hdr);world_fullscreen(cmd,world.lights[0],lighting,world.light_pipeline_layout);world_fullscreen(cmd,world.lights[1],lighting,world.light_pipeline_layout);work.lighting_passes+=2;
    uint32_t spot_index=0;for(uint32_t i=0;i<light_count;++i)if(lights[i].flags&1){auto set=world_light_set(slot,true);auto config=find_spot(i);if(config){world_write_image(set,141,textures.at(config->cookie).image.view);if(config->cookie)++spots.stats.cookies;if(config->flags&1)world_write_image(set,142,spots.images[i].view);}world_fullscreen(cmd,world.lights[config && (config->flags&1)?3:2],set,world.spot_pipeline_layout,static_cast<uint32_t>(world_align(208)*spot_index++));++work.lighting_passes;}
    vkCmdEndRendering(cmd);guest_replay(cmd,1);frame_mark("environment");render_environment(cmd,slot);guest_replay(cmd,2);frame_mark("flares");render_flares(cmd,slot);frame_mark("transparency");render_transparency(cmd,slot);guest_replay(cmd,3);frame_mark("postprocess");barrier(cmd,world.hdr.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,(VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT)|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);render_postprocess(cmd,slot);work.tail_us=profile_take();world.initialized=true;++work.world_frames;
}
void compose_world(){
    if(world.compose_format!=color_format){wait(next_completion);vkDestroyPipeline(device,world.compose,nullptr);world.compose={};auto shader=world_file_shader("world_compose_ps");
        try{world.compose=world_graphics(world.fullscreen,"__vertex_shader",shader,"main",world.light_pipeline_layout,{{0,24,VK_VERTEX_INPUT_RATE_VERTEX}},world.fullscreen_attributes,&color_format,1,true,false,false,true);world.compose_format=color_format;vkDestroyShaderModule(device,shader,nullptr);}catch(...){vkDestroyShaderModule(device,shader,nullptr);throw;}}
    auto& slot=world.slots[slot_index];float dimensions[]{float(world.width),float(world.height),float(extent.width),float(extent.height),hdr_active?1.f:0.f,0,0,0};std::memcpy(slot.dimensions.mapped,dimensions,32);check(vmaFlushAllocation(allocator,slot.dimensions.memory,0,32),"flush composition dimensions");
    frame_mark("world compose");auto set=world_light_set(slot);if(display_state.compose)world_write_image(set,152,display_state.compose);world_fullscreen(slots[slot_index].command,post.output?post.compose:world.compose,set,world.light_pipeline_layout);
    vkCmdBindDescriptorSets(slots[slot_index].command,VK_PIPELINE_BIND_POINT_GRAPHICS,pipeline_layout,0,1,&slots[slot_index].descriptor,0,nullptr);vkCmdBindDescriptorSets(slots[slot_index].command,VK_PIPELINE_BIND_POINT_GRAPHICS,pipeline_layout,2,1,&shadow_descriptor,0,nullptr);
}
