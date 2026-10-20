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
void invalidate_world_recipes(uint32_t kind,uint64_t id,bool retire=false){
    pass_scene.scene.invalidate_dependency(kind,id);
    if(kind==0&&opaque_reference.initialized&&std::find(opaque_reference.material_ids.begin(),opaque_reference.material_ids.end(),id)!=opaque_reference.material_ids.end())opaque_reference.materials_dirty={true,true};
    if(kind==0&&opaque_stock.ready&&std::find(opaque_reference.material_ids.begin(),opaque_reference.material_ids.end(),id)!=opaque_reference.material_ids.end())opaque_stock.materials_dirty={true,true};
    auto found=world.recipe_dependencies.find({kind,id});if(found==world.recipe_dependencies.end())return;
    if(retire){auto indices=std::vector<uint32_t>(found->second.begin(),found->second.end());for(uint32_t index:indices){auto& r=world.recipes[index];if(!r.alive)continue;r.valid=false;r.alive=false;r.material_epochs={};remove_recipe_dependencies(r);world.free_recipes.push_back(index);--world.recipe_live;++world.recipe_retired;}}
    else for(uint32_t index:found->second){auto& r=world.recipes[index];r.valid=false;r.material_epochs={};}
}
void validate_world_recipe(WorldRecipe& r){
    if(r.valid)return;
    auto packet=r.packet;packet.object_constants=reinterpret_cast<void*>(uintptr_t(1));packet.instances=reinterpret_cast<void*>(uintptr_t(1));packet.instance_count=1;
    if(world_blended_decal(packet.pipeline))packet.flags|=512u|16384u; // Recipes validate resources independently of per-frame view flags.
    SevkQueueValidationStats stats{sizeof(SevkQueueValidationStats),SEVK_ABI_VERSION};WorldTextureValidationCache<> cache;validate_world_packet(packet,stats,cache);
    r.geometry=&world.meshes.at(packet.mesh);
    if(world_voxel(packet.pipeline)){const auto& m=world.voxel_materials.at(packet.color);for(uint32_t i=0;i<3;i++)r.arrays[i]=m.arrays[i];r.random=m.random;}
    update_recipe_dependencies(r);r.valid=true;++world.current().recipe_stats.revalidated;
}
uint64_t register_world_recipe(const SevkWorldRecipe& source){
    if(recording || source.size!=sizeof(source) || source.reserved)throw std::runtime_error("invalid world recipe registration");
    WorldRecipe r{};r.alive=true;r.packet.size=sizeof(SevkWorldDraw);r.packet.pipeline=source.pipeline;r.packet.mesh=source.mesh;r.packet.color=source.color;r.packet.normal=source.normal;r.packet.extensions=source.extensions;r.packet.alpha=source.alpha;r.packet.object_bytes=source.object_bytes;
    validate_world_recipe(r); // Validate before publishing a handle or consuming a reusable slot.
    uint32_t index;
    if(world.free_recipes.empty()){if(world.recipes.size()>=65536)throw std::runtime_error("world recipe table exceeds65536 live/retired slots");index=uint32_t(world.recipes.size());world.recipes.emplace_back();}
    else {index=world.free_recipes.back();world.free_recipes.pop_back();}
    r.index=index;r.generation=++recipe_generation;if(!r.generation)r.generation=++recipe_generation;
    world.recipes[index]=std::move(r);update_recipe_dependencies(world.recipes[index]);++world.recipe_live;++world.recipe_registered;
    return (uint64_t(world.recipes[index].generation)<<32)|(uint64_t(index)+1);
}
void queue_world_recipes(const void* frame,uint32_t frame_bytes,const float* projection,const float* inverse,const SevkRecipeDraw* draws,uint32_t count,const uint8_t* objects,uint32_t object_bytes,const uint8_t* instances,uint32_t instance_bytes){
    critical_profile::Scope critical_queue(critical_profile::QueueOwner,true);
    auto queued_at=std::chrono::steady_clock::now();
    if(recording)throw std::runtime_error("world recipe frame/blob contract mismatch");
    begin_next_submission();
    world.current().queued=false;world.current().draws.clear();
    world.current().recipe_stats=SevkRecipeStats{sizeof(SevkRecipeStats),SEVK_ABI_VERSION,1,count,world.recipe_registered,0,0,0,world.recipe_live,world.recipe_retired};
    world.current().queue_work=SevkQueueWorkStats{sizeof(SevkQueueWorkStats),SEVK_ABI_VERSION};world.current().queue_work.workers=1;
    world.current().validation_stats=SevkQueueValidationStats{sizeof(SevkQueueValidationStats),SEVK_ABI_VERSION,world.texture_validation_cache?1u:0u};
    if(!frame || frame_bytes!=1280 || !projection || !inverse || (!draws&&count) || count>65536 || object_bytes>256u*1024*1024 || instance_bytes>256u*1024*1024 || (object_bytes&&!objects) || (instance_bytes&&!instances) || !world.lights[0] || !world.lights[1] || !world.lights[2])throw std::runtime_error("world recipe frame/blob contract mismatch");
    if(world.retained_objects){for(auto& slot:world.slots)slot.object_versions.clear();}world.retained_objects=false;
    world.current().draws.resize(count);uint64_t object_cursor=0;
    for(uint32_t i=0;i<count;i++){
        const auto& d=draws[i];uint32_t index=uint32_t(d.recipe)-1,generation=uint32_t(d.recipe>>32);
        if(index>=world.recipes.size() || !generation || d.reserved || !d.instance_count || d.instance_count>1000000)throw std::runtime_error("invalid world recipe handle/count");
        auto& recipe=world.recipes[index];if(!recipe.alive || recipe.generation!=generation)throw std::runtime_error("stale world recipe generation");
        validate_world_recipe(recipe);const auto& packet=recipe.packet;bool instanced=world.pipelines[packet.pipeline].type>=2;
        if(world_blended_decal(packet.pipeline) && (d.flags&(512u|16384u))!=(512u|16384u))throw std::runtime_error("mesh decal recipe must exclude depth and forward passes");
        if(!instanced && d.instance_count!=1)throw std::runtime_error("rigid/skinned recipe instance count");
        if(d.object_offset!=object_cursor || d.object_offset%world.alignment || uint64_t(d.object_offset)+packet.object_bytes>object_bytes)throw std::runtime_error("invalid recipe object span");
        object_cursor+=world_align(packet.object_bytes);
        WorldDraw own{};own.packet=packet;own.packet.instance_count=d.instance_count;own.packet.flags=d.flags;own.packet.reserved=d.cascade_mask;std::memcpy(own.packet.origin,d.origin,12);own.packet.radius=d.radius;own.recipe=&recipe;own.recipe_generation=generation;own.geometry=recipe.geometry;own.object_at=d.object_offset;own.object_n=packet.object_bytes;
        if(instanced){uint64_t bytes=uint64_t(d.instance_count)*64;if(d.instance_offset%64 || uint64_t(d.instance_offset)+bytes>instance_bytes)throw std::runtime_error("invalid recipe instance span");own.instance_at=d.instance_offset;own.instance_n=uint32_t(bytes);}
        else if(d.instance_offset!=UINT32_MAX)throw std::runtime_error("noninstanced recipe stream must be absent");
        world.current().draws[i]=own;++world.current().validation_stats.draws;
    }
    if(object_cursor!=object_bytes)throw std::runtime_error("recipe object blob size mismatch");
    world.current().queue_work.validation_us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-queued_at).count());
    auto copy_at=std::chrono::steady_clock::now();world.current().draw_objects.clear();world.current().instances.clear();if(object_bytes)world.current().draw_objects.assign(objects,objects+object_bytes);if(instance_bytes)world.current().instances.data.assign(instances,instances+instance_bytes);
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
