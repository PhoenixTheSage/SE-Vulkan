const SevkSpot* find_spot(uint32_t light){for(auto& s:spots.config)if(s.light==light)return &s;return nullptr;}
void cleanup_spot_copy() noexcept {
    if(spots.copy_pipeline)vkDestroyPipeline(device,spots.copy_pipeline,nullptr);
    if(spots.copy_pool)vkDestroyDescriptorPool(device,spots.copy_pool,nullptr);
    if(spots.copy_layout)vkDestroyPipelineLayout(device,spots.copy_layout,nullptr);
    if(spots.copy_set_layout)vkDestroyDescriptorSetLayout(device,spots.copy_set_layout,nullptr);
    spots.copy_pipeline={};spots.copy_pool={};spots.copy_layout={};spots.copy_set_layout={};
}
void prepare_spot_copy(uint32_t light,Image& frozen){
    if(!spots.copy_pipeline){
        VkDescriptorSetLayoutBinding binding{0,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,1,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr};
        VkDescriptorSetLayoutCreateInfo info{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};info.bindingCount=1;info.pBindings=&binding;
        check(vkCreateDescriptorSetLayout(device,&info,nullptr,&spots.copy_set_layout),"spot copy layout");spots.copy_layout=world_pipeline_layout(spots.copy_set_layout);
        VkDescriptorPoolSize size{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,128};VkDescriptorPoolCreateInfo pool{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};pool.maxSets=128;pool.poolSizeCount=1;pool.pPoolSizes=&size;
        check(vkCreateDescriptorPool(device,&pool,nullptr,&spots.copy_pool),"spot copy pool");
        std::array<VkDescriptorSetLayout,128> layouts;layouts.fill(spots.copy_set_layout);VkDescriptorSetAllocateInfo allocate{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};allocate.descriptorPool=spots.copy_pool;allocate.descriptorSetCount=128;allocate.pSetLayouts=layouts.data();
        check(vkAllocateDescriptorSets(device,&allocate,&spots.copy_sets[0][0]),"spot copy descriptors");
        auto vs=world_file_shader("spot_depth_copy_vs",VK_SHADER_STAGE_VERTEX_BIT),ps=world_file_shader("spot_depth_copy_ps");
        try{spots.copy_pipeline=world_graphics(vs,"main",ps,"main",spots.copy_layout,{},{},nullptr,0,true,false,false,true);}catch(...){vkDestroyShaderModule(device,vs,nullptr);vkDestroyShaderModule(device,ps,nullptr);throw;}
        vkDestroyShaderModule(device,vs,nullptr);vkDestroyShaderModule(device,ps,nullptr);
    }
    // Only the fenced current slot's descriptor is changed; the frozen image
    // allocation is retired by the renderer's last-use completion policy.
    VkDescriptorImageInfo image{VK_NULL_HANDLE,frozen.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};VkWriteDescriptorSet write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};write.dstSet=spots.copy_sets[slot_index][light];write.dstBinding=0;write.descriptorType=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;write.descriptorCount=1;write.pImageInfo=&image;vkUpdateDescriptorSets(device,1,&write,0,nullptr);
}
void set_spot_redraw(uint64_t static_mask,uint64_t dynamic_mask){if(recording)throw std::runtime_error("spotlight redraw must precede the frame");spots.static_dirty=static_mask;spots.dynamic_dirty=dynamic_mask;}
void set_spots(uint32_t resolution,const SevkSpot* config,uint32_t count){
    if(recording || count>64 || (!config && count) || resolution<256 || resolution>2048 || (resolution&(resolution-1)))throw std::runtime_error("invalid spotlight configuration");
    uint64_t seen=0;uint32_t shadowed=0;std::vector<SevkSpot> copied;copied.reserve(count);
    for(uint32_t i=0;i<count;++i){auto s=config[i];if(s.light>=light_count || !(lights[s.light].flags&1) || s.flags>1 || (seen&(uint64_t(1)<<s.light)) || !textures.count(s.cookie))throw std::runtime_error("invalid spotlight light/cookie/identity");seen|=uint64_t(1)<<s.light;shadowed+=s.flags&1;for(float f:s.projection)if(!std::isfinite(f))throw std::runtime_error("nonfinite spotlight projection");copied.push_back(s);}
    if(shadowed>16)throw std::runtime_error("spotlight shadow budget exceeds sixteen maps");spots.config=std::move(copied);spots.resolution=resolution;
}
void copy_spot_depth(VkCommandBuffer cmd,Image& src,VkImageLayout src_layout,Image& dst,VkImageLayout dst_layout){
    if(src_layout!=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)barrier(cmd,src.handle,src_layout,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,src_layout==VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL?VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT:VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,src_layout==VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL?VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT:VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
    VkPipelineStageFlags2 dst_stage=dst_layout==VK_IMAGE_LAYOUT_UNDEFINED?VK_PIPELINE_STAGE_2_NONE:dst_layout==VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL?VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT:VK_PIPELINE_STAGE_2_TRANSFER_BIT;VkAccessFlags2 dst_access=dst_layout==VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL?VK_ACCESS_2_SHADER_SAMPLED_READ_BIT:0;
    if(dst_layout!=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)barrier(cmd,dst.handle,dst_layout,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,dst_stage,dst_access,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT);
    VkImageCopy region{};region.srcSubresource=region.dstSubresource={VK_IMAGE_ASPECT_DEPTH_BIT,0,0,1};region.extent={spots.resolution,spots.resolution,1};vkCmdCopyImage(cmd,src.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,dst.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&region);
}
// The light's static/dynamic split can remain unchanged while its shader
// sees a new fade threshold or camera-relative transform. Hash each bound
// source span once, then combine only casters intersecting each light.
void update_spot_fade_dependencies(WorldSlot& slot,const std::vector<uint32_t>& objects,const std::vector<uint32_t>& instances){
    if(std::none_of(spots.config.begin(),spots.config.end(),[](const auto& light){return (light.flags&1)!=0;}))return;
    constexpr uint64_t seed=14695981039346656037ull;
    auto mix=[](uint64_t& hash,uint64_t value){hash^=value;hash*=1099511628211ull;};
    // Hash the slot's host copies of the uploaded bytes; mapped upload
    // memory may be uncached device-local BAR and must not be read back.
    auto bytes=[&](uint64_t& hash,const std::vector<uint8_t>& source,uint32_t offset,uint32_t count){
        if(uint64_t(offset)+count>source.size())throw std::runtime_error("spot fade dependency outside bound buffer");
        auto data=source.data()+offset;
        while(count>=8){uint64_t value;std::memcpy(&value,data,8);mix(hash,value);data+=8;count-=8;}
        while(count){mix(hash,*data++);--count;}
    };
    struct Dependency{uint32_t draw;uint64_t hash;};std::vector<Dependency> dependencies;
    std::unordered_map<uint64_t,uint64_t> source_hashes;
    for(uint32_t i:world.current().order){const auto& draw=world.current().draws[i];const auto& packet=draw.packet;
        if(packet.pipeline<2560 || (packet.flags&(512u|8192u|0x2000000u)))continue;
        uint64_t hash=seed;mix(hash,world_distance_fade_bits());mix(hash,packet.mesh);mix(hash,packet.pipeline);mix(hash,packet.alpha);mix(hash,draw.geometry->ray_stamp);mix(hash,packet.instance_count);
        bytes(hash,slot.host_objects,objects[i],draw.object_n);
        if(draw.instance_n){uint64_t key=(uint64_t(instances[i])<<32)|draw.instance_n;auto found=source_hashes.find(key);
            if(found==source_hashes.end()){uint64_t source=seed;bytes(source,slot.host_instances,instances[i],draw.instance_n);found=source_hashes.emplace(key,source).first;}mix(hash,found->second);}
        dependencies.push_back({i,hash});
    }
    auto visible=[&](uint32_t i,uint32_t light){const auto& packet=world.current().draws[i].packet;
        if((packet.flags&(512u|8192u|0x2000000u)) || spots.exclusions.count((uint64_t(light)<<32)|i))return false;
        float delta[]{packet.origin[0]-lights[light].position[0],packet.origin[1]-lights[light].position[1],packet.origin[2]-lights[light].position[2]};
        return spot_sphere_visible(delta,lights[light].direction,lights[light].range,lights[light].cone_cosine,packet.radius);
    };
    for(const auto& light:spots.config){if(!(light.flags&1))continue;uint64_t bit=uint64_t(1)<<light.light,statics=seed,dynamics=seed;bool has_dynamic=false;
        for(const auto& dependency:dependencies)if(visible(dependency.draw,light.light)){
            bool dynamic=world.current().draws[dependency.draw].packet.flags&0x1000000u;mix(dynamic?dynamics:statics,dependency.hash);has_dynamic|=dynamic;
        }
        if(spots.fade_valid&bit){
            bool changed_static=statics!=spots.fade_static[light.light],changed_dynamic=dynamics!=spots.fade_dynamic[light.light];
            if(changed_static){spots.static_dirty|=bit;
                // Rebuilding the frozen base also replaces the final image;
                // retain any unchanged dynamic overlay on that light.
                if(!has_dynamic)for(uint32_t i:world.current().order)if((world.current().draws[i].packet.flags&0x1000000u)&&visible(i,light.light)){has_dynamic=true;break;}
            }
            if(changed_dynamic || (changed_static&&has_dynamic))spots.dynamic_dirty|=bit;
        }
        spots.fade_static[light.light]=statics;spots.fade_dynamic[light.light]=dynamics;spots.fade_valid|=bit;
    }
}
void render_spot_shadows(VkCommandBuffer cmd,WorldSlot& slot,const std::vector<uint32_t>& objects,const std::vector<uint32_t>& instances){
    critical_profile::Scope critical_spots(critical_profile::SpotRecord,true);
    // Cull once per active light, preserving material/geometry sort order for
    // both the frozen static map and its dynamic overlay.
    auto prepare_casters=[&](uint32_t light,bool include_static){
        auto& statics=spots.static_casters[light];auto& dynamics=spots.dynamic_casters[light];statics.clear();dynamics.clear();
        for(uint32_t j:world.current().order){auto& draw=world.current().draws[j];uint32_t flags=draw.packet.flags;if(flags&(512|8192|0x2000000u))continue;bool dynamic=(flags&0x1000000u)!=0;if(!include_static && !dynamic)continue;if(spots.exclusions.count((uint64_t(light)<<32)|j))continue;
            float delta[]{draw.packet.origin[0]-lights[light].position[0],draw.packet.origin[1]-lights[light].position[1],draw.packet.origin[2]-lights[light].position[2]};if(!spot_sphere_visible(delta,lights[light].direction,lights[light].range,lights[light].cone_cosine,draw.packet.radius))continue;
            (dynamic?dynamics:statics).push_back(j);
        }
    };
    // Records one light's casters into target and returns the draw count; the
    // caller adds it to spots.stats.draws and performance.spot_commands.
    auto draw_casters=[&](VkCommandBuffer target,uint32_t light,bool only_dynamic)->uint32_t{uint32_t counted=0;struct SpotBind{VkPipeline pipe{};VkDescriptorSet set{};uint32_t offset0=~0u,offset1=~0u,streams{},instance{};VkBuffer vertex{},index{};VkDeviceSize vertex_at{},index_at{};WorldRasterCache raster{};} bound{};
        for(uint32_t j:only_dynamic?spots.dynamic_casters[light]:spots.static_casters[light]){auto& draw=world.current().draws[j];auto& mesh=*draw.geometry;auto set=draw.material;auto& pipeline=world.pipelines[32+draw.packet.pipeline];uint32_t offsets[]{uint32_t(world_align(64)*(9+light)),objects[j]};VkPipeline handle=shadow_pass_constants_provided?pipeline.stock_spot:pipeline.handle;if(!handle)throw std::runtime_error("stock spotlight rasterizer unavailable");if(bound.pipe!=handle){vkCmdBindPipeline(target,VK_PIPELINE_BIND_POINT_GRAPHICS,handle);bound.pipe=handle;if(world_voxel(draw.packet.pipeline))bound.raster.valid=false;}if(!world_voxel(draw.packet.pipeline))world_model_raster(target,draw.packet.pipeline,draw.packet.flags,true,VK_CULL_MODE_NONE,shadow_pass_constants_provided,&bound.raster);if(bound.set!=set||bound.offset0!=offsets[0]||bound.offset1!=offsets[1]){vkCmdBindDescriptorSets(target,VK_PIPELINE_BIND_POINT_GRAPHICS,world.material_pipeline_layout,0,1,&set,2,offsets);bound.set=set;bound.offset0=offsets[0];bound.offset1=offsets[1];}uint32_t streams=world.pipelines[draw.packet.pipeline].type>=2?2u:1u;if(bound.vertex!=mesh.vertices.handle||bound.index!=mesh.indices.handle||bound.streams!=streams||bound.instance!=instances[j]||bound.vertex_at!=mesh.vertex_offset||bound.index_at!=mesh.index_offset){VkBuffer buffers[]{mesh.vertices.handle,slot.instances.handle};VkDeviceSize stream_offsets[]{mesh.vertex_offset,instances[j]};vkCmdBindVertexBuffers(target,0,streams,buffers,stream_offsets);vkCmdBindIndexBuffer(target,mesh.indices.handle,mesh.index_offset,VK_INDEX_TYPE_UINT32);bound.vertex=mesh.vertices.handle;bound.index=mesh.indices.handle;bound.streams=streams;bound.instance=instances[j];bound.vertex_at=mesh.vertex_offset;bound.index_at=mesh.index_offset;}vkCmdDrawIndexed(target,mesh.index_count,draw.packet.instance_count,0,0,0);geometry_note_execution(32+draw.packet.pipeline);++counted;}uint32_t base=pass_scene.spot_views[light];for(auto& view:opaque_reference.views)if(opaque_stock.ready&&opaque_reference.frame_ready&&(view.flags&128)&&view.light==light){base=view.id;break;}uint32_t native_commands=base?record_pass_view(target,slot,base+(only_dynamic?1u:0u),true,nullptr,nullptr,light):0;counted+=native_commands;return counted;};
    auto begin_depth=[&](Image& image,VkImageLayout old_layout,bool clear,bool secondary=false){VkPipelineStageFlags2 stage=old_layout==VK_IMAGE_LAYOUT_UNDEFINED?VK_PIPELINE_STAGE_2_NONE:(old_layout==VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL||old_layout==VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)?VK_PIPELINE_STAGE_2_TRANSFER_BIT:VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;VkAccessFlags2 access=old_layout==VK_IMAGE_LAYOUT_UNDEFINED?0:old_layout==VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL?VK_ACCESS_2_TRANSFER_WRITE_BIT:old_layout==VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL?VK_ACCESS_2_TRANSFER_READ_BIT:VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;barrier(cmd,image.handle,old_layout,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,stage,access,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT|VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT);VkRenderingAttachmentInfo depth{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};depth.imageView=image.view;depth.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;depth.loadOp=clear?VK_ATTACHMENT_LOAD_OP_CLEAR:VK_ATTACHMENT_LOAD_OP_LOAD;depth.storeOp=VK_ATTACHMENT_STORE_OP_STORE;depth.clearValue.depthStencil={1,0};VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.renderArea={{0,0},{spots.resolution,spots.resolution}};rendering.layerCount=1;rendering.pDepthAttachment=&depth;if(secondary)rendering.flags=VK_RENDERING_CONTENTS_SECONDARY_COMMAND_BUFFERS_BIT;vkCmdBeginRendering(cmd,&rendering);if(!secondary)world_viewport(cmd,spots.resolution,spots.resolution);};
    // Parallel recording keeps the primary barrier/copy/rendering sequence and
    // every caster command identical: workers only cull each light and record
    // the draws inside its depth scopes into secondaries executed in place.
    // GPU-visibility views (conditional rendering) and native pass views
    // (shared slot descriptor pool) stay on the serial recorder.
    bool spot_parallel=world.parallel_spots && !pass_scene.ready && !(opaque_stock.ready&&opaque_reference.frame_ready);
    for(uint32_t i=0;spot_parallel && i<64;++i)if(pass_scene.spot_views[i])spot_parallel=false;
    world.spot_tasks=0;
    if(spot_parallel){
        struct SpotPlan{uint32_t light;bool fresh,rebuild,dynamic;VkCommandBuffer statics{},dynamics{};uint32_t draws{};};std::vector<SpotPlan> plans;
        for(uint32_t i=0;i<64;++i){auto config=find_spot(i);if(!config || !(config->flags&1)){retire_image(spots.images[i]);retire_image(spots.frozen[i]);spots.sizes[i]=spots.frozen_sizes[i]=0;spots.frozen_ready[i]=0;spots.frozen_layout[i]=VK_IMAGE_LAYOUT_UNDEFINED;continue;}
            auto& image=spots.images[i];bool fresh=spots.sizes[i]!=spots.resolution || !image.handle;bool rebuild=fresh || ((spots.static_dirty|pass_scene.static_dirty)&(uint64_t(1)<<i)) || !spots.frozen[i].handle || !spots.frozen_ready[i] || spots.frozen_sizes[i]!=spots.resolution;bool dynamic=(spots.dynamic_dirty&(uint64_t(1)<<i))!=0;if(!rebuild && !dynamic)continue;VkImageUsageFlags usage=VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT;if(fresh){retire_image(image);image=make_image(spots.resolution,spots.resolution,VK_FORMAT_D32_SFLOAT,usage,VK_IMAGE_ASPECT_DEPTH_BIT);spots.sizes[i]=spots.resolution;}if(!spots.frozen[i].handle || spots.frozen_sizes[i]!=spots.resolution){retire_image(spots.frozen[i]);spots.frozen[i]=make_image(spots.resolution,spots.resolution,VK_FORMAT_D32_SFLOAT,usage,VK_IMAGE_ASPECT_DEPTH_BIT);spots.frozen_sizes[i]=spots.resolution;spots.frozen_ready[i]=0;spots.frozen_layout[i]=VK_IMAGE_LAYOUT_UNDEFINED;rebuild=true;}
            plans.push_back({i,fresh,rebuild,dynamic});}
        uint32_t workers=std::min<uint32_t>(uint32_t(plans.size()),std::clamp(std::thread::hardware_concurrency()/4u,1u,4u));
        // Each worker owns one pool per frame slot; this slot's frame has completed.
        for(uint32_t k=0;k<workers;++k){auto& pool=world.spot_pools[slot_index][k];if(!pool){VkCommandPoolCreateInfo info{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};info.queueFamilyIndex=queue_family;check(vkCreateCommandPool(device,&info,nullptr,&pool),"create frame-owned spot pool");}
            check(vkResetCommandPool(device,pool,0),"reset completed spot pool");world.spot_used[slot_index][k]=0;}
        auto spot_secondary=[&](uint32_t k){auto& list=world.spot_commands[slot_index][k];auto& used=world.spot_used[slot_index][k];
            if(used==list.size()){VkCommandBufferAllocateInfo allocation{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};allocation.commandPool=world.spot_pools[slot_index][k];allocation.level=VK_COMMAND_BUFFER_LEVEL_SECONDARY;allocation.commandBufferCount=1;VkCommandBuffer created{};check(vkAllocateCommandBuffers(device,&allocation,&created),"allocate spot secondary");list.push_back(created);}
            VkCommandBuffer command=list[used++];
            VkCommandBufferInheritanceRenderingInfo rendering{VK_STRUCTURE_TYPE_COMMAND_BUFFER_INHERITANCE_RENDERING_INFO};rendering.depthAttachmentFormat=VK_FORMAT_D32_SFLOAT;rendering.rasterizationSamples=VK_SAMPLE_COUNT_1_BIT;
            VkCommandBufferInheritanceInfo inheritance{VK_STRUCTURE_TYPE_COMMAND_BUFFER_INHERITANCE_INFO,&rendering};
            VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};begin.flags=VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT|VK_COMMAND_BUFFER_USAGE_RENDER_PASS_CONTINUE_BIT;begin.pInheritanceInfo=&inheritance;
            check(vkBeginCommandBuffer(command,&begin),"begin spot secondary");world_viewport(command,spots.resolution,spots.resolution);return command;};
        auto record=[&](uint32_t k){critical_profile::Bind lane(&critical.workers[8+k]);critical_profile::Scope task(critical_profile::SpotRecord,true);
            for(size_t p=k;p<plans.size();p+=workers){auto& plan=plans[p];prepare_casters(plan.light,plan.rebuild);
                auto scope=[&](bool only_dynamic){VkCommandBuffer command=spot_secondary(k);plan.draws+=draw_casters(command,plan.light,only_dynamic);check(vkEndCommandBuffer(command),"end spot secondary");return command;};
                if(plan.rebuild){plan.statics=scope(false);if(plan.dynamic)plan.dynamics=scope(true);}else plan.dynamics=scope(true);}};
        std::vector<std::future<void>> jobs;for(uint32_t k=1;k<workers;++k)jobs.push_back(std::async(std::launch::async,record,k));
        std::exception_ptr failure;try{if(workers)record(0);}catch(...){failure=std::current_exception();}
        for(auto& job:jobs)try{job.get();}catch(...){if(!failure)failure=std::current_exception();}
        if(failure)std::rethrow_exception(failure);
        world.spot_tasks=workers;
        for(auto& plan:plans){uint32_t i=plan.light;auto& image=spots.images[i];bool fresh=plan.fresh,dynamic=plan.dynamic;spots.stats.draws+=plan.draws;performance.spot_commands+=plan.draws;
            if(plan.rebuild){begin_depth(image,fresh?VK_IMAGE_LAYOUT_UNDEFINED:VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,true,true);vkCmdExecuteCommands(cmd,1,&plan.statics);vkCmdEndRendering(cmd);copy_spot_depth(cmd,image,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,spots.frozen[i],spots.frozen_layout[i]);spots.frozen_ready[i]=1;barrier(cmd,spots.frozen[i].handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);spots.frozen_layout[i]=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;if(dynamic){barrier(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT|VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);VkRenderingAttachmentInfo depth{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};depth.imageView=image.view;depth.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;depth.loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;depth.storeOp=VK_ATTACHMENT_STORE_OP_STORE;VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.flags=VK_RENDERING_CONTENTS_SECONDARY_COMMAND_BUFFERS_BIT;rendering.renderArea={{0,0},{spots.resolution,spots.resolution}};rendering.layerCount=1;rendering.pDepthAttachment=&depth;vkCmdBeginRendering(cmd,&rendering);vkCmdExecuteCommands(cmd,1,&plan.dynamics);vkCmdEndRendering(cmd);}barrier(cmd,image.handle,dynamic?VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL:VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,dynamic?VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT:VK_PIPELINE_STAGE_2_TRANSFER_BIT,dynamic?VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT:VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);}
            else{copy_spot_depth(cmd,spots.frozen[i],spots.frozen_layout[i],image,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);spots.frozen_layout[i]=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;begin_depth(image,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,false,true);vkCmdExecuteCommands(cmd,1,&plan.dynamics);vkCmdEndRendering(cmd);barrier(cmd,image.handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);}++spots.stats.maps;}
    }else
    for(uint32_t i=0;i<64;++i){auto config=find_spot(i);if(!config || !(config->flags&1)){retire_image(spots.images[i]);retire_image(spots.frozen[i]);spots.sizes[i]=spots.frozen_sizes[i]=0;spots.frozen_ready[i]=0;spots.frozen_layout[i]=VK_IMAGE_LAYOUT_UNDEFINED;continue;}
        auto& image=spots.images[i];bool fresh=spots.sizes[i]!=spots.resolution || !image.handle;bool rebuild=fresh || ((spots.static_dirty|pass_scene.static_dirty)&(uint64_t(1)<<i)) || !spots.frozen[i].handle || !spots.frozen_ready[i] || spots.frozen_sizes[i]!=spots.resolution;bool dynamic=(spots.dynamic_dirty&(uint64_t(1)<<i))!=0;uint32_t gpu_view=UINT32_MAX;if(opaque_stock.ready&&opaque_reference.frame_ready)for(uint32_t v=0;v<opaque_reference.view_count;v++)if((opaque_reference.views[v].flags&128)&&opaque_reference.views[v].light==i){gpu_view=v;break;}if(!rebuild && !dynamic&&gpu_view==UINT32_MAX)continue;VkImageUsageFlags usage=VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT;if(fresh){retire_image(image);image=make_image(spots.resolution,spots.resolution,VK_FORMAT_D32_SFLOAT,usage,VK_IMAGE_ASPECT_DEPTH_BIT);spots.sizes[i]=spots.resolution;}if(!spots.frozen[i].handle || spots.frozen_sizes[i]!=spots.resolution){retire_image(spots.frozen[i]);spots.frozen[i]=make_image(spots.resolution,spots.resolution,VK_FORMAT_D32_SFLOAT,usage,VK_IMAGE_ASPECT_DEPTH_BIT);spots.frozen_sizes[i]=spots.resolution;spots.frozen_ready[i]=0;spots.frozen_layout[i]=VK_IMAGE_LAYOUT_UNDEFINED;rebuild=true;}
        if(gpu_view!=UINT32_MAX){
            prepare_casters(i,true);auto& frozen=spots.frozen[i];prepare_spot_copy(i,frozen);begin_depth(frozen,spots.frozen_layout[i],!spots.frozen_ready[i]);
            VkConditionalRenderingBeginInfoEXT conditional{VK_STRUCTURE_TYPE_CONDITIONAL_RENDERING_BEGIN_INFO_EXT};conditional.buffer=opaque_reference.slots[slot_index].counts.handle;conditional.offset=(opaque_reference.view_count*opaque_reference.batch_count+1+gpu_view)*4ull;vkCmdBeginConditionalRenderingEXT(cmd,&conditional);
            VkClearAttachment clear{VK_IMAGE_ASPECT_DEPTH_BIT,0,{}};clear.clearValue.depthStencil={1,0};VkClearRect rect{{{0,0},{spots.resolution,spots.resolution}},0,1};vkCmdClearAttachments(cmd,1,&clear,1,&rect);{uint32_t n=draw_casters(cmd,i,false);spots.stats.draws+=n;performance.spot_commands+=n;}vkCmdEndConditionalRenderingEXT(cmd);vkCmdEndRendering(cmd);
            barrier(cmd,frozen.handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);spots.frozen_ready[i]=1;spots.frozen_layout[i]=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            uint32_t overlay=gpu_view;for(uint32_t v=0;v<opaque_reference.view_count;v++)if((opaque_reference.views[v].flags&136)==136&&opaque_reference.views[v].light==i){overlay=v;break;}
            begin_depth(image,fresh?VK_IMAGE_LAYOUT_UNDEFINED:VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,fresh);conditional.offset=(opaque_reference.view_count*opaque_reference.batch_count+1+overlay)*4ull;vkCmdBeginConditionalRenderingEXT(cmd,&conditional);
            vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,spots.copy_pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,spots.copy_layout,0,1,&spots.copy_sets[slot_index][i],0,nullptr);vkCmdDraw(cmd,3,1,0,0);{uint32_t n=draw_casters(cmd,i,true);spots.stats.draws+=n;performance.spot_commands+=n;}vkCmdEndConditionalRenderingEXT(cmd);vkCmdEndRendering(cmd);barrier(cmd,image.handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);++spots.stats.maps;continue;
        }
        prepare_casters(i,rebuild);
        if(rebuild){begin_depth(image,fresh?VK_IMAGE_LAYOUT_UNDEFINED:VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,true);{uint32_t n=draw_casters(cmd,i,false);spots.stats.draws+=n;performance.spot_commands+=n;}vkCmdEndRendering(cmd);copy_spot_depth(cmd,image,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,spots.frozen[i],spots.frozen_layout[i]);spots.frozen_ready[i]=1;barrier(cmd,spots.frozen[i].handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);spots.frozen_layout[i]=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;if(dynamic){barrier(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT|VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);VkRenderingAttachmentInfo depth{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};depth.imageView=image.view;depth.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;depth.loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;depth.storeOp=VK_ATTACHMENT_STORE_OP_STORE;VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.renderArea={{0,0},{spots.resolution,spots.resolution}};rendering.layerCount=1;rendering.pDepthAttachment=&depth;vkCmdBeginRendering(cmd,&rendering);world_viewport(cmd,spots.resolution,spots.resolution);{uint32_t n=draw_casters(cmd,i,true);spots.stats.draws+=n;performance.spot_commands+=n;}vkCmdEndRendering(cmd);}barrier(cmd,image.handle,dynamic?VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL:VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,dynamic?VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT:VK_PIPELINE_STAGE_2_TRANSFER_BIT,dynamic?VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT:VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);}
        else{copy_spot_depth(cmd,spots.frozen[i],spots.frozen_layout[i],image,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);spots.frozen_layout[i]=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;begin_depth(image,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,false);{uint32_t n=draw_casters(cmd,i,true);spots.stats.draws+=n;performance.spot_commands+=n;}vkCmdEndRendering(cmd);barrier(cmd,image.handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);}++spots.stats.maps;
    }
    pass_scene.static_dirty=0;
    if(!spots.config.empty())++spots.stats.frames;
}
