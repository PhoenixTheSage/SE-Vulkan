static_assert(sizeof(SevkWorldRecipe)==56 && sizeof(SevkRecipeDraw)==48 && sizeof(SevkRecipeStats)==40);
void remove_recipe_dependencies(WorldRecipe& r){
    for(const auto& dep:r.dependencies){auto it=world.recipe_dependencies.find(dep);if(it!=world.recipe_dependencies.end()){it->second.erase(r.index);if(it->second.empty())world.recipe_dependencies.erase(it);}}
    r.dependencies.clear();
}
void update_recipe_dependencies(WorldRecipe& r){
    if(r.index==UINT32_MAX)return;
    remove_recipe_dependencies(r);
    auto add=[&](uint32_t kind,uint64_t id){RecipeDependency dep{kind,id};if(std::find(r.dependencies.begin(),r.dependencies.end(),dep)==r.dependencies.end()){r.dependencies.push_back(dep);world.recipe_dependencies[dep].insert(r.index);}};
    add(3,r.packet.mesh);add(8,r.packet.pipeline);add(8,world_base_pipeline(r.packet.pipeline)+32);
    if(world_voxel(r.packet.pipeline)){add(6,r.packet.color);for(auto id:r.arrays)add(5,id);add(0,r.random);add(0,r.packet.alpha);}
    else for(auto id:{r.packet.color,r.packet.normal,r.packet.extensions,r.packet.alpha})add(0,id);
}
void brief_recipe(const WorldRecipe& r){if(r.index==UINT32_MAX)return;if(world.recipe_brief.size()<=r.index)world.recipe_brief.resize(size_t(r.index)+1);auto& b=world.recipe_brief[r.index];
    b.generation=r.generation;b.object_bytes=r.packet.object_bytes;b.alive=r.alive;b.valid=r.valid;b.instanced=world.pipelines[r.packet.pipeline].type>=2;b.decal=world_blended_decal(r.packet.pipeline);}
void invalidate_world_recipes(uint32_t kind,uint64_t id,bool retire=false){settle_cells();
    pass_scene.scene.invalidate_dependency(kind,id);
    if(kind==0&&opaque_reference.initialized&&std::find(opaque_reference.material_ids.begin(),opaque_reference.material_ids.end(),id)!=opaque_reference.material_ids.end())opaque_reference.materials_dirty={true,true};
    if(kind==0&&opaque_stock.ready&&std::find(opaque_reference.material_ids.begin(),opaque_reference.material_ids.end(),id)!=opaque_reference.material_ids.end())opaque_stock.materials_dirty={true,true};
    auto found=world.recipe_dependencies.find({kind,id});if(found==world.recipe_dependencies.end())return;
    if(retire){auto indices=std::vector<uint32_t>(found->second.begin(),found->second.end());for(uint32_t index:indices){auto& r=world.recipes[index];if(!r.alive)continue;r.valid=false;r.alive=false;r.material_epochs={};brief_recipe(r);remove_recipe_dependencies(r);world.free_recipes.push_back(index);--world.recipe_live;++world.recipe_retired;}}
    else for(uint32_t index:found->second){auto& r=world.recipes[index];r.valid=false;r.material_epochs={};brief_recipe(r);}
}
void validate_world_recipe(WorldRecipe& r){
    if(r.valid)return;
    auto packet=r.packet;packet.object_constants=reinterpret_cast<void*>(uintptr_t(1));packet.instances=reinterpret_cast<void*>(uintptr_t(1));packet.instance_count=1;
    if(world_blended_decal(packet.pipeline))packet.flags|=512u|16384u; // Recipes validate resources independently of per-frame view flags.
    SevkQueueValidationStats stats{sizeof(SevkQueueValidationStats),SEVK_ABI_VERSION};WorldTextureValidationCache<> cache;validate_world_packet(packet,stats,cache);
    r.geometry=&world.meshes.at(packet.mesh);
    if(world_voxel(packet.pipeline)){const auto& m=world.voxel_materials.at(packet.color);for(uint32_t i=0;i<3;i++)r.arrays[i]=m.arrays[i];r.random=m.random;}
    update_recipe_dependencies(r);r.valid=true;brief_recipe(r);++world.current().recipe_stats.revalidated;
}
uint64_t register_world_recipe(const SevkWorldRecipe& source){settle_cells();
    if(recording || source.size!=sizeof(source) || source.reserved)throw std::runtime_error("invalid world recipe registration");
    WorldRecipe r{};r.alive=true;r.packet.size=sizeof(SevkWorldDraw);r.packet.pipeline=source.pipeline;r.packet.mesh=source.mesh;r.packet.color=source.color;r.packet.normal=source.normal;r.packet.extensions=source.extensions;r.packet.alpha=source.alpha;r.packet.object_bytes=source.object_bytes;
    validate_world_recipe(r); // Validate before publishing a handle or consuming a reusable slot.
    uint32_t index;
    if(world.free_recipes.empty()){if(world.recipes.size()>=65536)throw std::runtime_error("world recipe table exceeds65536 live/retired slots");index=uint32_t(world.recipes.size());world.recipes.emplace_back();}
    else {index=world.free_recipes.back();world.free_recipes.pop_back();}
    r.index=index;r.generation=++recipe_generation;if(!r.generation)r.generation=++recipe_generation;
    world.recipes[index]=std::move(r);update_recipe_dependencies(world.recipes[index]);brief_recipe(world.recipes[index]);++world.recipe_live;++world.recipe_registered;
    return (uint64_t(world.recipes[index].generation)<<32)|(uint64_t(index)+1);
}
// Deferred world intake (opt-in, SE_VULKAN_DEFERRED_WORLD_QUEUE=1; off by default because the build then
// lengthens recording and BeginFrame waited for it more often in paired runs): queue_world_recipes checks
// the frame contract and copies the blobs at once, but recipe validation and draw construction, and the
// per-draw OLD order, owner and main-order rows sent after it, run at the first call that reads the queued
// draws: end_frame (on the recording worker when pipelined), or any earlier call through settle_cells().
bool deferred_world_queue=false;
struct PendingWorldQueue {bool active{};std::vector<SevkRecipeDraw> draws;uint32_t object_bytes{},instance_bytes{};
    bool has_old{},has_owners{},has_main{};std::vector<SevkOldPartOrder> old_order;std::vector<uint32_t> owners;std::vector<SevkMainDrawOrder> main_order;} pending_world;
// Draws in the queued frame, including a deferred intake not yet built.
size_t queued_draw_count()const{return pending_world.active?pending_world.draws.size():world.current().draws.size();}
// Plan 2 B3. Every check runs first, serially, over the compact recipe_brief table (the same checks, in the same
// per-draw order, as before; an invalid recipe is revalidated here). Only then are the draws built: written in
// place into the zeroed vector (no temporary and second copy of each ~180-byte WorldDraw), and with
// SE_VULKAN_PARALLEL_RECIPES=1 in disjoint ranges on the job pool for large frames.
bool parallel_recipes=false;
void build_world_recipe_draws(const SevkRecipeDraw* draws,uint32_t count,uint32_t object_bytes,uint32_t instance_bytes){
    auto queued_at=std::chrono::steady_clock::now();
    uint64_t object_cursor=0;
    for(uint32_t i=0;i<count;i++){
        const auto& d=draws[i];uint32_t index=uint32_t(d.recipe)-1,generation=uint32_t(d.recipe>>32);
        if(index>=world.recipes.size() || !generation || d.reserved || !d.instance_count || d.instance_count>1000000)throw std::runtime_error("invalid world recipe handle/count");
        if(index>=world.recipe_brief.size())throw std::runtime_error("stale world recipe generation");
        const auto* brief=&world.recipe_brief[index];if(!brief->alive || brief->generation!=generation)throw std::runtime_error("stale world recipe generation");
        if(!brief->valid){validate_world_recipe(world.recipes[index]);brief=&world.recipe_brief[index];}bool instanced=brief->instanced;
        if(brief->decal && (d.flags&(512u|16384u))!=(512u|16384u))throw std::runtime_error("mesh decal recipe must exclude depth and forward passes");
        if(!instanced && d.instance_count!=1)throw std::runtime_error("rigid/skinned recipe instance count");
        if(d.object_offset!=object_cursor || d.object_offset%world.alignment || uint64_t(d.object_offset)+brief->object_bytes>object_bytes)throw std::runtime_error("invalid recipe object span");
        object_cursor+=world_align(brief->object_bytes);
        if(instanced){uint64_t bytes=uint64_t(d.instance_count)*64;if(d.instance_offset%64 || uint64_t(d.instance_offset)+bytes>instance_bytes)throw std::runtime_error("invalid recipe instance span");}
        else if(d.instance_offset!=UINT32_MAX)throw std::runtime_error("noninstanced recipe stream must be absent");
    }
    if(object_cursor!=object_bytes)throw std::runtime_error("recipe object blob size mismatch");
    auto& out=world.current().draws;out.clear();out.resize(count);
    auto build=[&](uint32_t begin,uint32_t end){
        for(uint32_t i=begin;i<end;i++){
            const auto& d=draws[i];uint32_t index=uint32_t(d.recipe)-1;auto& recipe=world.recipes[index];const auto& packet=recipe.packet;auto& own=out[i];
            own.packet=packet;own.packet.instance_count=d.instance_count;own.packet.flags=d.flags;own.packet.reserved=d.cascade_mask;std::memcpy(own.packet.origin,d.origin,12);own.packet.radius=d.radius;
            own.recipe=&recipe;own.recipe_generation=uint32_t(d.recipe>>32);own.geometry=recipe.geometry;own.object_at=d.object_offset;own.object_n=packet.object_bytes;
            if(world_recipe_instanced(index)){own.instance_at=d.instance_offset;own.instance_n=uint32_t(uint64_t(d.instance_count)*64);}
        }
    };
    if(parallel_recipes && count>=2048){
        uint32_t workers=std::min(4u,sevk_jobs::shared().worker_count()+1);world.current().queue_work.enabled=1;world.current().queue_work.workers=workers;
        sevk_jobs::parallel_for(sevk_jobs::QueueValidate,sevk_jobs::Priority::High,workers,workers,[&](uint32_t worker){build(count*worker/workers,count*(worker+1)/workers);});
    }else build(0,count);
    world.current().validation_stats.draws+=count;
    world.current().queue_work.validation_us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-queued_at).count());
}
bool world_recipe_instanced(uint32_t index)const{return world.recipe_brief[index].instanced;}
void settle_world(){
    if(!pending_world.active)return;auto& q=pending_world;q.active=false;
    build_world_recipe_draws(q.draws.data(),uint32_t(q.draws.size()),q.object_bytes,q.instance_bytes);
    if(q.has_old)apply_world_old_order(q.old_order.data(),uint32_t(q.old_order.size()));
    if(q.has_owners)apply_world_draw_owners(q.owners.data(),uint32_t(q.owners.size()));
    if(q.has_main)apply_world_main_order(q.main_order.data(),uint32_t(q.main_order.size()));
    q.has_old=q.has_owners=q.has_main=false;
}
// The arena lives in the submission the next queue will take (begin_next_submission's choice: the one not being
// recorded). Recording holds the other one, so writing it while recording(N) runs is safe.
int32_t arena_submission=-1;uint32_t arena_bytes=0;uint64_t arena_hits=0,arena_misses=0;
uint8_t* world_object_arena(uint32_t bytes){
    if(frame_open() || !bytes || bytes>256u*1024*1024)throw std::runtime_error("world object arena contract mismatch");
    uint32_t target=overlap_from_caller()?uint32_t(world.recording_submission)^1u:world.active_submission^1u;
    if(world.recording_submission>=0 && target==uint32_t(world.recording_submission))throw std::runtime_error("world object arena would alias the recording submission");
    auto& objects=world.submissions[target].draw_objects;objects.resize(bytes);arena_submission=int32_t(target);arena_bytes=bytes;return objects.data();
}
void queue_world_recipes(const void* frame,uint32_t frame_bytes,const float* projection,const float* inverse,const SevkRecipeDraw* draws,uint32_t count,const uint8_t* objects,uint32_t object_bytes,const uint8_t* instances,uint32_t instance_bytes){settle_cells();
    critical_profile::Scope critical_queue(critical_profile::QueueOwner,true);
    auto queued_at=std::chrono::steady_clock::now();
    if(frame_open())throw std::runtime_error("world recipe frame/blob contract mismatch");
    // Switching off object retention clears per-slot versions that recording may be using.
    if(world.retained_objects)wait_for_recording_end();
    begin_next_submission();
    world.current().queued=false;world.current().draws.clear();
    world.current().recipe_stats=SevkRecipeStats{sizeof(SevkRecipeStats),SEVK_ABI_VERSION,1,count,world.recipe_registered,0,0,0,world.recipe_live,world.recipe_retired};
    world.current().queue_work=SevkQueueWorkStats{sizeof(SevkQueueWorkStats),SEVK_ABI_VERSION};world.current().queue_work.workers=1;
    world.current().validation_stats=SevkQueueValidationStats{sizeof(SevkQueueValidationStats),SEVK_ABI_VERSION,world.texture_validation_cache?1u:0u};
    if(!frame || frame_bytes!=1280 || !projection || !inverse || (!draws&&count) || count>65536 || object_bytes>256u*1024*1024 || instance_bytes>256u*1024*1024 || (object_bytes&&!objects) || (instance_bytes&&!instances) || !world.lights[0] || !world.lights[1] || !world.lights[2])throw std::runtime_error("world recipe frame/blob contract mismatch");
    if(world.retained_objects){for(auto& slot:world.slots)slot.object_versions.clear();}world.retained_objects=false;
    // Malformed handles, counts, spans and invalid recipe resources are rejected by this call even when the
// build is deferred (revalidation is cached once valid); only draw construction waits.
    uint64_t cursor=0;
    if(deferred_world_queue)for(uint32_t i=0;i<count;i++){const auto& d=draws[i];uint32_t index=uint32_t(d.recipe)-1,generation=uint32_t(d.recipe>>32);
        if(index>=world.recipes.size() || !generation || d.reserved || !d.instance_count || d.instance_count>1000000)throw std::runtime_error("invalid world recipe handle/count");
        if(index>=world.recipe_brief.size())throw std::runtime_error("stale world recipe generation");
        const auto* brief=&world.recipe_brief[index];if(!brief->alive || brief->generation!=generation)throw std::runtime_error("stale world recipe generation");
        if(!brief->valid){validate_world_recipe(world.recipes[index]);brief=&world.recipe_brief[index];}bool instanced=brief->instanced;
        if(brief->decal && (d.flags&(512u|16384u))!=(512u|16384u))throw std::runtime_error("mesh decal recipe must exclude depth and forward passes");
        if(!instanced && d.instance_count!=1)throw std::runtime_error("rigid/skinned recipe instance count");
        if(d.object_offset!=cursor || d.object_offset%world.alignment || uint64_t(d.object_offset)+brief->object_bytes>object_bytes)throw std::runtime_error("invalid recipe object span");
        cursor+=world_align(brief->object_bytes);
        if(instanced){if(d.instance_offset%64 || uint64_t(d.instance_offset)+uint64_t(d.instance_count)*64>instance_bytes)throw std::runtime_error("invalid recipe instance span");}
        else if(d.instance_offset!=UINT32_MAX)throw std::runtime_error("noninstanced recipe stream must be absent");}
    if(deferred_world_queue&&cursor!=object_bytes)throw std::runtime_error("recipe object blob size mismatch");
    if(deferred_world_queue){auto& q=pending_world;q.draws.assign(draws,draws+count);q.object_bytes=object_bytes;q.instance_bytes=instance_bytes;q.has_old=q.has_owners=q.has_main=false;q.active=true;world.current().draws.clear();}
    else build_world_recipe_draws(draws,count,object_bytes,instance_bytes);
    auto copy_at=std::chrono::steady_clock::now();
    bool in_arena=arena_submission>=0 && objects==world.submissions[arena_submission].draw_objects.data() && object_bytes==arena_bytes;
    if(in_arena && &world.current()==&world.submissions[arena_submission])++arena_hits; // already in place
    else if(in_arena){++arena_misses;std::vector<uint8_t> moved(objects,objects+object_bytes);world.current().draw_objects.swap(moved);}
    else{world.current().draw_objects.clear();if(object_bytes)world.current().draw_objects.assign(objects,objects+object_bytes);}
    arena_submission=-1;arena_bytes=0;world.current().instances.clear();if(instance_bytes)world.current().instances.data.assign(instances,instances+instance_bytes);
    world.current().queue_work.copy_us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-copy_at).count());world.current().queue_work.draws=count;
    std::memcpy(world.current().frame.data(),frame,1280);std::memcpy(world.current().projection.data(),projection,64);std::memcpy(world.current().inverse.data(),inverse,64);world.current().queued=true;
    world.current().queue_us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-queued_at).count());world.current().recipe_stats.geometry_reused=count;
}
VkDescriptorSet world_recipe_material(WorldSlot& slot,const WorldDraw& draw){
    auto& recipe=*draw.recipe;
    if(recipe.materials[slot_index] && recipe.material_epochs[slot_index]==slot.material_epoch){++performance.material_hits;++world.current().recipe_stats.materials_reused;return recipe.materials[slot_index];}
    bool persistent=false;auto set=world_material(slot,draw,{},&persistent);
    if(persistent){recipe.materials[slot_index]=set;recipe.material_epochs[slot_index]=slot.material_epoch;}
    return set;
}
