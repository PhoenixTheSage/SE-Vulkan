static_assert(sizeof(SevkModelPart)==16 && sizeof(SevkModelInstance)==120 && sizeof(SevkModelGroupStats)==48);
uint64_t register_model(const SevkModelPart* parts,uint32_t count,const uint8_t* objects,uint32_t bytes,const SevkOldPartOrder* orders=nullptr){
    auto& models=world.models;
    if(recording || !parts || !count || count>4096 || !objects || bytes>16u*1024*1024 || models.definitions.size()>=65536 || models.resident_bytes+bytes>256ull*1024*1024)throw std::runtime_error("invalid/budgeted model registration");
    ModelDefinition definition{};definition.parts.reserve(count);uint32_t cursor=0;
    for(uint32_t i=0;i<count;++i){const auto& p=parts[i];uint32_t index=uint32_t(p.recipe)-1,generation=uint32_t(p.recipe>>32);
        if(index>=world.recipes.size() || (p.reserved&~(512u|4096u|16384u|0x3c000000u)) || (p.reserved&0x18000000u)==0x18000000u || !generation)throw std::runtime_error("invalid model part recipe");
        auto& recipe=world.recipes[index];if(!recipe.alive || recipe.generation!=generation)throw std::runtime_error("stale model part recipe");validate_world_recipe(recipe);
        if(recipe.packet.pipeline>=20 || world.pipelines[recipe.packet.pipeline].type!=2 || p.object_offset!=cursor || uint64_t(cursor)+recipe.packet.object_bytes>bytes)throw std::runtime_error("model requires canonical generic object spans");
        if(orders){validate_old_order(orders[i]);if(orders[i].valid&&!(p.reserved&0x04000000u))throw std::runtime_error("ordered model lost OLD provenance");}
        definition.parts.push_back({&recipe,generation,std::vector<uint8_t>(objects+cursor,objects+cursor+recipe.packet.object_bytes),p.reserved,orders?orders[i]:SevkOldPartOrder{}});cursor+=recipe.packet.object_bytes;
    }
    if(cursor!=bytes)throw std::runtime_error("model object blob mismatch");
    definition.generation=++models.generation;if(!definition.generation)definition.generation=++models.generation;
    auto generation=definition.generation;models.definitions.push_back(std::move(definition));models.resident_bytes+=bytes;
    return (uint64_t(generation)<<32)|uint64_t(models.definitions.size());
}
void clear_models(){
    if(recording)throw std::runtime_error("model registration cannot clear during recording");
    auto& m=world.models;m.definitions.clear();m.lookup.clear();m.groups.clear();m.active.clear();m.merged.clear();m.resident_bytes=0;m.stats={sizeof(SevkModelGroupStats),SEVK_ABI_VERSION};
}
void append_models(const SevkModelInstance* instances,uint32_t count,const uint32_t* legacy_order,uint32_t legacy_count,uint32_t* remap){settle_cells();
    if(recording || !world.current().queued || world.retained_objects || (!instances&&count) || count>65536 || legacy_count!=world.current().draws.size() || (legacy_count&&(!legacy_order||!remap)))throw std::runtime_error("invalid bulk model frame contract");
    world.current().queued=false; // A failed expansion must never publish a partial frame.
    auto started=std::chrono::steady_clock::now();auto& m=world.models;
    if(m.lookup.size()>32768){m.lookup.clear();m.groups.clear();}m.active.clear();++m.frame;
    m.stats={sizeof(SevkModelGroupStats),SEVK_ABI_VERSION,1,count,0,0,uint32_t(m.definitions.size()),legacy_count};
    for(uint32_t i=1;i<legacy_count;++i)if(legacy_order[i]<=legacy_order[i-1])throw std::runtime_error("noncanonical legacy model ordering");
    for(uint32_t i=0;i<count;++i){const auto& source=instances[i];uint32_t index=uint32_t(source.model)-1,generation=uint32_t(source.model>>32);
        if(index>=m.definitions.size() || !generation || m.definitions[index].generation!=generation || (source.cell!=16&&source.cell!=64) || (i && source.order<=instances[i-1].order))throw std::runtime_error("invalid model instance handle/order/cell");
        ModelGroupKey key{source.model,source.flags,source.cascade_mask,source.cell,source.x,source.y,source.z,source.reserved};
        auto [entry,added]=m.lookup.try_emplace(key,uint32_t(m.groups.size()));if(added){m.groups.emplace_back();m.groups.back().key=key;}
        auto& group=m.groups[entry->second];
        if(group.frame!=m.frame){group.frame=m.frame;group.count=0;group.order=source.order;group.instances.clear();std::memcpy(group.origin,source.origin,12);group.radius=source.radius;m.active.push_back(entry->second);}
        if(group.count>=1000000)throw std::runtime_error("model group instance budget exceeded");
        auto first=reinterpret_cast<const uint8_t*>(source.instance);group.instances.insert(group.instances.end(),first,first+64);
        if(group.count){if(group.radius<0 || source.radius<0)group.radius=-1;else{float shift[3]{source.origin[0]-group.origin[0],source.origin[1]-group.origin[1],source.origin[2]-group.origin[2]};float distance=std::sqrt(shift[0]*shift[0]+shift[1]*shift[1]+shift[2]*shift[2]);
            if(distance+source.radius>group.radius){if(distance+group.radius<=source.radius){std::memcpy(group.origin,source.origin,12);group.radius=source.radius;}else{float combined=(distance+group.radius+source.radius)*.5f;if(distance>0)for(uint32_t c=0;c<3;++c)group.origin[c]+=shift[c]*((combined-group.radius)/distance);group.radius=combined;}}}}
        ++group.count;
    }
    uint64_t object_bytes=world.current().draw_objects.size(),instance_bytes=world.current().instances.data.size(),packets=legacy_count;
    for(auto index:m.active){const auto& group=m.groups[index];const auto& definition=m.definitions[uint32_t(group.key.model)-1];packets+=definition.parts.size();instance_bytes+=group.instances.size();for(const auto& part:definition.parts){
        if(!part.recipe->alive || part.recipe->generation!=part.generation)throw std::runtime_error("retired model recipe dependency");validate_world_recipe(*part.recipe);object_bytes+=world_align(part.constants.size());}}
    if(packets>65536 || object_bytes>256ull*1024*1024 || instance_bytes>256ull*1024*1024)throw std::runtime_error("expanded model frame exceeds bounded upload budget");
    m.stats.groups=uint32_t(m.active.size());m.stats.packets=uint32_t(packets-legacy_count);m.stats.object_bytes=object_bytes-world.current().draw_objects.size();m.stats.instance_bytes=instance_bytes-world.current().instances.data.size();
    world.current().draw_objects.reserve(size_t(object_bytes));world.current().instances.data.reserve(size_t(instance_bytes));m.merged.clear();m.merged.reserve(size_t(packets));uint32_t legacy=0;
    // First-seen model groups and legacy submissions are both ordered streams.
    // Merge once and return physical legacy indices to every identity consumer.
    for(uint32_t i=0;i<legacy_count;++i)if(!world.current().draws[i].main_order)world.current().draws[i].main_order=legacy_order[i];
    for(auto index:m.active){const auto& group=m.groups[index];while(legacy<legacy_count && legacy_order[legacy]<group.order){remap[legacy]=uint32_t(m.merged.size());m.merged.push_back(world.current().draws[legacy++]);}
        if(legacy<legacy_count && legacy_order[legacy]==group.order)throw std::runtime_error("overlapping model/legacy order");
        uint32_t instance_at=uint32_t(world.current().instances.data.size());world.current().instances.data.insert(world.current().instances.data.end(),group.instances.begin(),group.instances.end());
        const auto& definition=m.definitions[uint32_t(group.key.model)-1];for(const auto& part:definition.parts){auto& recipe=*part.recipe;WorldDraw draw{};draw.packet=recipe.packet;draw.packet.instance_count=group.count;draw.packet.flags=group.key.flags|part.view_flags;draw.packet.reserved=group.key.cascades;std::memcpy(draw.packet.origin,group.origin,12);draw.packet.radius=group.radius;draw.recipe=&recipe;draw.recipe_generation=part.generation;draw.geometry=recipe.geometry;draw.object_at=uint32_t(world.current().draw_objects.size());draw.object_n=uint32_t(part.constants.size());draw.instance_at=instance_at;draw.instance_n=uint32_t(group.instances.size());
            draw.main_order=group.key.main_order?group.key.main_order:group.order;draw.main_part=uint32_t(&part-definition.parts.data());draw.old_order=part.old_order;world.current().draw_objects.resize(world.current().draw_objects.size()+size_t(world_align(draw.object_n)));std::memcpy(world.current().draw_objects.data()+draw.object_at,part.constants.data(),draw.object_n);m.merged.push_back(draw);}
    }
    while(legacy<legacy_count){remap[legacy]=uint32_t(m.merged.size());m.merged.push_back(world.current().draws[legacy++]);}world.current().draws.swap(m.merged);
    world.current().validation_stats.draws=uint32_t(packets);world.current().queue_work.draws=uint32_t(packets);world.current().recipe_stats.draws=uint32_t(packets);world.current().recipe_stats.geometry_reused=uint32_t(packets);
    world.current().queue_us+=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-started).count());world.current().queued=true;
}
