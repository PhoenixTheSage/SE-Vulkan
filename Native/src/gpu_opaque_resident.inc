static_assert(sizeof(SevkGpuSceneActor)==560);
uint64_t register_gpu_scene_model(const SevkPassPart* parts,uint32_t count,uint32_t old,const SevkOldPartOrder* orders=nullptr){
    if(recording||!opaque_caps.enabled||old>1||!parts||!count||count>4096)throw std::runtime_error("invalid resident GPU model registration");gpu_scene::Model model;model.old=old!=0;
    for(uint32_t i=0;i<count;i++){auto& p=parts[i];auto found=world.meshes.find(p.mesh);if(p.reserved||p.flags>15||found==world.meshes.end()||uint64_t(p.first)+p.count>found->second.index_count||p.shader>=(old?2592u:20u) || (old && world_unfade(p.shader)>=32))throw std::runtime_error("invalid resident model geometry/state");
        auto& mesh=found->second;if(mesh.vertex_stride!=sizeof(SevkWorldVertex)||mesh.vertex_offset%sizeof(SevkWorldVertex)||mesh.vertex_offset/sizeof(SevkWorldVertex)>INT32_MAX)throw std::runtime_error("resident model requires stock shared vertex geometry");for(auto id:{p.colour,p.normal,p.extensions,p.alpha})if(!textures.count(id))throw std::runtime_error("resident material binding missing");
        uint32_t colour=old?p.shader:p.shader<4?p.shader:12+p.shader-4,depth=old?32+world_base_pipeline(p.shader):4+bool(p.flags&1),forward=old?64+p.shader:8+p.shader;
        auto& main=old?opaque_stock.older[colour]:opaque_stock.newer[colour];auto& ds=old?opaque_stock.older[depth]:opaque_stock.newer[depth];if(!main.back||(!(p.flags&4)&&!ds.back))throw std::runtime_error("resident model shader not admitted");
        bool ff=!(p.flags&8);if(ff){if((!old&&p.shader>=4)||forward>=(old?2656u:28u) || (old && world_unfade(forward)>=96)||!(old?opaque_stock.older[forward]:opaque_stock.newer[forward]).back)throw std::runtime_error("resident forward shader not admitted");}
        model.parts.push_back({{p.mesh,mesh.ray_stamp,(uint64_t)mesh.vertices.handle,(uint64_t)mesh.indices.handle,p.first,p.count,int32_t(mesh.vertex_offset/sizeof(SevkWorldVertex))},{p.material,colour,depth,ff?forward:UINT32_MAX,(p.flags&2)?1u:0u,{p.colour,p.normal,p.extensions,p.alpha}},true,!(p.flags&4),ff,bool(p.flags&1)});
        if(orders){validate_old_order(orders[i]);if(!old&&orders[i].valid)throw std::runtime_error("NEW resident model supplied OLD identity");model.parts.back().old_order=orders[i];}
    }return gpu_resident.registry.register_model(std::move(model));
}
gpu_scene::Actor gpu_scene_actor_input(const SevkGpuSceneActor& input,const SevkOpaqueAppearance* appearances,uint32_t count){
    bool replace=input.appearance_count!=UINT32_MAX;
    if(input.size!=sizeof(input)||input.abi_version!=SEVK_ABI_VERSION||(replace&&input.appearance_count>gpu_opaque::appearance_limit)||input.appearance_first>count||(replace&&input.appearance_count>count-input.appearance_first)||(!appearances&&count))throw std::runtime_error("invalid resident actor source commit");
    gpu_scene::Actor actor;actor.input=input.input;actor.family=input.family;actor.source=input.source;actor.group=input.group;actor.source_first=input.source_first;actor.source_count=input.source_count;actor.selector=input.selector;std::copy_n(input.object,128,actor.object.begin());
    actor.replace_appearance=replace;for(uint32_t i=0;replace&&i<input.appearance_count;i++){auto& p=appearances[input.appearance_first+i];gpu_scene::Appearance a;a.material=p.material;a.visible=p.visible;for(uint32_t c=0;c<4;c++)a.colour[c]=uint16_t(p.colour[c/2]>>(16*(c%2)));std::copy_n(p.old_colour,4,a.old_colour.begin());actor.appearances.push_back(a);}return actor;
}
void publish_gpu_scene(uint32_t views,uint32_t references,uint32_t draws){
    critical_profile::Scope profile(critical_profile::PassPublish,true);
    if(recording||batching_uploads||!opaque_caps.enabled)throw std::runtime_error("resident publication outside owner epoch");auto& resident=gpu_resident;
    lazy_batch_suppressed=true;struct Unsuppress{bool& flag;~Unsuppress(){flag=false;}} unsuppress{lazy_batch_suppressed};
    if(resident.published&&opaque_reference.initialized){wait(next_completion);check(vmaInvalidateAllocation(allocator,opaque_reference.selectors.memory,0,opaque_reference.selectors.bytes),"capture completed selectors for topology replacement");auto states=static_cast<const SevkOpaqueSelector*>(opaque_reference.selectors.mapped);for(uint32_t i=0;i<resident.upload.handles.size();i++)if(resident.registry.contains(resident.upload.handles[i]))resident.registry.selector_handoff(resident.upload.handles[i],states[i]);}
    gpu_scene::Upload upload;{GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::Snapshot);upload=resident.registry.upload();}if(upload.actors.empty())throw std::runtime_error("empty resident GPU publication");
    // Preflight retained source identity before allocating a new topology. These
    // are shared world allocations, never copied geometry for another consumer.
    for(auto& family:upload.source.families)for(auto& model:family->levels)for(auto& part:model->parts){auto it=world.meshes.find(part.geometry.mesh);if(it==world.meshes.end()||it->second.ray_stamp!=part.geometry.generation||(uint64_t)it->second.vertices.handle!=part.geometry.vertex_page||(uint64_t)it->second.indices.handle!=part.geometry.index_page)throw std::runtime_error("resident model geometry generation changed");}
    if(!references||!draws){uint64_t refs=0,commands=upload.parts.size()*2ull;for(auto& actor:upload.actors){auto& f=upload.families[actor.family];uint32_t colour=0,depth=0;for(uint32_t lod=0;lod<=f.threshold_count;lod++){uint32_t c=0,d=0;for(uint32_t p=f.first_part;p<f.first_part+f.part_count;p++)if(upload.parts[p].lod==lod){c+=bool(upload.parts[p].flags&2);d+=bool(upload.parts[p].flags&1);}colour=std::max(colour,c);depth=std::max(depth,d);}refs+=(actor.flags&2)?uint64_t(std::max(colour,depth))*2*views:uint64_t(colour)*8+uint64_t(depth)*(views>7?views-7:views);if(actor.flags&2)commands+=f.part_count*2ull;}if(refs>UINT32_MAX||commands>UINT32_MAX)throw std::runtime_error("resident capacity overflow");references=std::max(1u,uint32_t(refs));draws=std::max(1u,uint32_t(commands));}
    SevkOpaqueReferenceConfig config{};config.size=sizeof(config);config.abi_version=SEVK_ABI_VERSION;config.actor_count=uint32_t(upload.actors.size());config.family_count=uint32_t(upload.families.size());config.part_count=uint32_t(upload.parts.size());config.view_count=views;config.source_count=uint32_t(upload.sources.size()/64);config.reference_capacity=references;config.draw_capacity=draws;config.reserved=uint32_t(upload.batches.size());config.actors=upload.actors.data();config.families=upload.families.data();config.parts=upload.parts.data();config.source_instances=upload.sources.data();config.material_textures=upload.textures.data();config.material_count=uint32_t(upload.textures.size());
    init_opaque_reference(config,true);set_opaque_stock_batches(upload.batches.data(),uint32_t(upload.batches.size()),upload.part_meshes.data(),uint32_t(upload.part_meshes.size()),upload.objects.data(),uint32_t(upload.objects.size()));
    update_opaque_reference_appearances(upload.appearance_updates.data(),uint32_t(upload.appearance_updates.size()),upload.appearances.data(),uint32_t(upload.appearances.size()));
    for(uint32_t i=0;i<upload.selectors.size();i++)std::memcpy(static_cast<uint8_t*>(opaque_reference.selectors.mapped)+i*sizeof(SevkOpaqueSelector),&upload.selectors[i],sizeof(SevkOpaqueSelector));check(vmaFlushAllocation(allocator,opaque_reference.selectors.memory,0,opaque_reference.selectors.bytes),"publish resident selector handoff");
    resident.group_actors.clear();resident.queries.clear();for(uint32_t i=0;i<upload.source.actors.size();i++){auto& actor=upload.source.actors[i];if(actor.input.group){auto& members=resident.group_actors[actor.input.group];if(members.empty())resident.queries.push_back({actor.input.group,0,actor.input.input.owner,0});members.push_back(i);}}
    resident.actor_indices.clear();resident.family_indices.clear();resident.source_offsets.clear();
    for(uint32_t i=0;i<upload.handles.size();i++)resident.actor_indices.emplace(upload.handles[i],i);
    for(uint32_t i=0;i<upload.source.families.size();i++)resident.family_indices.emplace(upload.source.families[i],i);
    uint32_t source_offset=0;for(auto& source:upload.source.sources){resident.source_offsets.emplace(source,source_offset);source_offset+=uint32_t(source->bytes.size()/64);}
    resident.topology_counts=resident.registry.topology_counts();
    resident.topology=upload.source.topology;resident.revision=upload.source.revision;resident.upload=std::move(upload);resident.registry.acknowledge(resident.revision);resident.published=true;++resident.publications;
}
// Rebinding an actor to already published immutable tables does not replace
// buffers, descriptor sets, selectors or retained light images. Prove the
// complete output budget before touching committed inputs. New tables or a
// capacity boundary use the explicit cold publication path.
bool gpu_scene_patch_bindings(const std::vector<gpu_scene::ResidentActor>& changed){
    GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::OwnedBindings);
    auto& resident=gpu_resident;auto& r=opaque_reference;
    struct Patch {uint32_t family,first,count;std::shared_ptr<const gpu_scene::Family> replacement;std::vector<std::array<uint64_t,4>> textures;};
    std::vector<Patch> patches;std::set<uint32_t> replaced;
    auto next_indices=resident.family_indices;
    for(auto& actor:changed){if(next_indices.contains(actor.family))continue;
        if(!gpu_owned_inputs)return false;auto index=resident.actor_indices.find(actor.handle);if(index==resident.actor_indices.end())return false;
        uint32_t family=resident.upload.actors[index->second].family;auto& previous=resident.upload.source.families[family];
        if(replaced.contains(family)||resident.registry.family_in_use(previous)||!gpu_scene::compatible_bindings(*previous,*actor.family))return false;
        auto bindings=gpu_scene::binding_textures(*actor.family);auto& input=r.family_inputs[family];if(bindings.size()!=input.part_count)return false;
        for(auto& entry:bindings)for(uint64_t texture:entry)if(!textures.contains(texture))return false;
        replaced.insert(family);next_indices.erase(previous);next_indices.emplace(actor.family,family);patches.push_back({family,input.first_part,input.part_count,actor.family,std::move(bindings)});
    }
    auto prospective=r.actors;bool geometry_changed=false;
    for(auto& actor:changed){auto family=next_indices.find(actor.family);if(family==next_indices.end())return false;
        auto index=resident.actor_indices.at(actor.handle);auto input=actor.input.input;input.family=family->second;input.source_first=actor.source?resident.source_offsets.at(actor.source)+actor.input.source_first:0;input.source_count=actor.input.source_count;
        geometry_changed|=input.family!=prospective[index].family||((input.flags^prospective[index].flags)&2)!=0;prospective[index]=input;}
    if(geometry_changed){try{validate_opaque_reference_capacity(prospective);}catch(const std::runtime_error&){return false;}}
    if(patches.empty())return true;
    try{validate_opaque_stock_geometry();}catch(const std::runtime_error&){return false;}
    // Plan every descriptor index and part change before committing. Descriptor
    // and part tables are applied to each slot only after its fence completes.
    auto next_materials=r.material_ids;std::vector<uint32_t> references(next_materials.size());
    for(auto& part:r.gpu_part_inputs)for(uint32_t texture:part.textures)++references.at(texture);
    for(auto& patch:patches)for(uint32_t p=patch.first;p<patch.first+patch.count;p++)for(uint32_t texture:r.gpu_part_inputs[p].textures)--references[texture];
    for(uint32_t t=1;t<references.size();t++)if(!references[t])next_materials[t]=0;
    struct PartBinding {uint32_t part;std::array<uint32_t,4> indices;};std::vector<PartBinding> bindings;
    for(auto& patch:patches)for(uint32_t p=0;p<patch.count;p++){PartBinding binding{patch.first+p,{}};
        for(uint32_t n=0;n<4;n++){uint64_t texture=patch.textures[p][n];auto found=std::find(next_materials.begin(),next_materials.end(),texture);
            uint32_t at;if(found!=next_materials.end())at=uint32_t(found-next_materials.begin());else{auto free=std::find(references.begin()+1,references.end(),0u);if(free==references.end())return false;at=uint32_t(free-references.begin());next_materials[at]=texture;}
            ++references[at];binding.indices[n]=at;}bindings.push_back(binding);}
    for(auto& slot:r.slots)slot.pending_parts.reserve(slot.pending_parts.size()+patches.size());
    resident.family_indices=std::move(next_indices);r.material_ids=std::move(next_materials);r.materials_dirty={true,true};opaque_stock.materials_dirty={true,true};
    for(auto& binding:bindings)for(uint32_t n=0;n<4;n++){r.part_inputs[binding.part].textures[n]=binding.indices[n];r.gpu_part_inputs[binding.part].textures[n]=binding.indices[n];resident.upload.parts[binding.part].textures[n]=binding.indices[n];}
    for(auto& patch:patches){resident.upload.source.families[patch.family]=patch.replacement;for(auto& slot:r.slots)slot.pending_parts.emplace_back(patch.first,patch.count);}
    return true;
}
bool gpu_scene_can_rebind(const std::vector<gpu_scene::ResidentActor>& changed){
    auto& resident=gpu_resident;auto& counts=resident.registry.topology_counts();
    if(counts[0]!=resident.topology_counts[0]||counts[1]!=resident.topology_counts[1])return false;
    for(auto& actor:changed)if(!resident.actor_indices.contains(actor.handle)||(actor.source&&!resident.source_offsets.contains(actor.source)))return false;
    return gpu_scene_patch_bindings(changed);
}
void gpu_scene_rebind_root(uint32_t index,const gpu_scene::ResidentActor& actor){
    auto& resident=gpu_resident;auto old=resident.upload.source.actors[index].input.group,next=actor.input.group;if(old==next)return;
    if(old){auto found=resident.group_actors.find(old);if(found!=resident.group_actors.end()){auto& members=found->second;members.erase(std::remove(members.begin(),members.end(),index),members.end());
        if(members.empty()){resident.group_actors.erase(found);std::erase_if(resident.queries,[&](auto& q){return q.group==old;});}
        else for(auto& q:resident.queries)if(q.group==old)q.representative=opaque_reference.actors[members.front()].owner;}}
    if(next){auto& members=resident.group_actors[next];if(members.empty())resident.queries.push_back({next,0,actor.input.input.owner,0});members.push_back(index);}
}
void queue_gpu_scene_frame(const SevkOpaqueFrame& frame,const SevkOpaqueView* views,uint32_t count){
    critical_profile::Scope profile(critical_profile::PassPrepare,true);
    auto& resident=gpu_resident;++resident.frames;if(!resident.published)throw std::runtime_error("resident topology has not been published");
    auto changed=resident.registry.changed(resident.revision);
    if(resident.topology!=resident.registry.topology()&&(!gpu_resident_deltas||!gpu_scene_can_rebind(changed))){publish_gpu_scene(opaque_reference.view_capacity,0,0);changed=resident.registry.changed(resident.revision);}
    if(!changed.empty()){
        std::unordered_map<uint64_t,uint32_t> indices;{GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::DeltaIndices);if(!gpu_resident_deltas)for(uint32_t i=0;i<resident.upload.handles.size();i++)indices.emplace(resident.upload.handles[i],i);}
        std::vector<uint32_t> ids;std::vector<SevkOpaqueActor> actors;std::vector<uint8_t> objects;std::vector<SevkOpaqueAppearance> appearances;std::vector<SevkOpaqueAppearanceUpdate> appearance_updates;
        for(auto& actor:changed){uint32_t index=gpu_resident_deltas?resident.actor_indices.at(actor.handle):indices.at(actor.handle);auto input=actor.input.input;
            input.family=gpu_resident_deltas?resident.family_indices.at(actor.family):resident.upload.actors[index].family;
            input.source_first=gpu_resident_deltas?(actor.source?resident.source_offsets.at(actor.source)+actor.input.source_first:0):resident.upload.actors[index].source_first- resident.upload.source.actors[index].input.source_first+actor.input.source_first;
            input.source_count=actor.input.source_count;ids.push_back(index);actors.push_back(input);objects.insert(objects.end(),actor.input.object.begin(),actor.input.object.end());if(actor.appearance_revision>resident.revision)appearance_updates.push_back({index,uint32_t(appearances.size()),uint32_t(actor.input.appearances.size()),0});
            actors.back().depth_revision=uint32_t(actor.revision);actors.back().hidden_views[0]=opaque_reference.actors[index].hidden_views[0];actors.back().hidden_views[1]=opaque_reference.actors[index].hidden_views[1];if(actor.appearance_revision>resident.revision)for(auto& a:actor.input.appearances){SevkOpaqueAppearance entry{a.material,a.visible,{uint32_t(a.colour[0])|(uint32_t(a.colour[1])<<16),uint32_t(a.colour[2])|(uint32_t(a.colour[3])<<16)},{}};std::copy(a.old_colour.begin(),a.old_colour.end(),entry.old_colour);appearances.push_back(entry);}}
        update_opaque_reference_actors(ids.data(),actors.data(),uint32_t(ids.size()),objects.data(),uint32_t(objects.size()),gpu_resident_deltas);update_opaque_reference_appearances(appearance_updates.data(),uint32_t(appearance_updates.size()),appearances.data(),uint32_t(appearances.size()));
        if(gpu_resident_deltas)for(uint32_t i=0;i<changed.size();i++){gpu_scene_rebind_root(ids[i],changed[i]);resident.upload.source.actors[ids[i]]=changed[i];resident.upload.actors[ids[i]]=actors[i];}
        resident.topology=resident.registry.topology();resident.topology_counts=resident.registry.topology_counts();resident.revision=resident.registry.revision();resident.registry.acknowledge(resident.revision);
    }resident.view_map.fill(UINT32_MAX);for(uint32_t i=0;i<count;i++){if(views[i].id>=64||resident.view_map[views[i].id]!=UINT32_MAX)throw std::runtime_error("duplicate resident view identity");resident.view_map[views[i].id]=i;}queue_opaque_reference_frame(frame,views,count);
}
void gpu_scene_queries(const SevkOpaqueView* views,uint32_t count,SevkPassQuery* queries,uint32_t capacity,uint32_t& total){
    auto& resident=gpu_resident;if(recording||!resident.published||!views||!count||count>opaque_reference.view_capacity)throw std::runtime_error("invalid resident root-query frame");uint64_t mask=0;for(uint32_t v=0;v<count;v++)if((views[v].flags&1)&&views[v].id<9)mask|=uint64_t(1)<<views[v].id;
    std::vector<SevkPassQuery> needed;for(auto& q:resident.queries){bool visible=false;for(uint32_t index:resident.group_actors.at(q.group))visible|=(opaque_reference.actors[index].flags&1)!=0;if(visible){auto entry=q;entry.views=mask;needed.push_back(entry);}}
    total=uint32_t(needed.size());if(queries){if(capacity<total)throw std::runtime_error("resident query capacity");std::copy(needed.begin(),needed.end(),queries);}

}
void gpu_scene_visibility(const SevkPassQueryResult* results,uint32_t count){
    auto& resident=gpu_resident;if(recording||!resident.published||(!results&&count))throw std::runtime_error("invalid resident root visibility");std::unordered_map<uint64_t,uint64_t> hidden;for(uint32_t i=0;i<count;i++)hidden[results[i].group]=results[i].hidden_views;
    for(auto& [group,members]:resident.group_actors){uint64_t mask=hidden[group],packed=0;for(uint32_t id=0;id<64;id++)if((mask&(uint64_t(1)<<id))&&resident.view_map[id]<opaque_reference.view_count)packed|=uint64_t(1)<<resident.view_map[id];for(uint32_t index:members){auto& a=opaque_reference.actors[index];if(a.hidden_views[0]!=uint32_t(packed)||a.hidden_views[1]!=uint32_t(packed>>32)){a.hidden_views[0]=uint32_t(packed);a.hidden_views[1]=uint32_t(packed>>32);opaque_reference.versions[index]=++opaque_reference.revision;}}}
}
void gpu_scene_selector(uint64_t handle,SevkOpaqueSelector& selector){
    if(recording)throw std::runtime_error("selector handoff during recording");auto& resident=gpu_resident;selector=resident.registry.actor(handle).input.selector;
    if(resident.published){auto found=std::find(resident.upload.handles.begin(),resident.upload.handles.end(),handle);if(found!=resident.upload.handles.end()){wait(next_completion);check(vmaInvalidateAllocation(allocator,opaque_reference.selectors.memory,0,opaque_reference.selectors.bytes),"completed resident selector handoff");selector=static_cast<const SevkOpaqueSelector*>(opaque_reference.selectors.mapped)[found-resident.upload.handles.begin()];resident.registry.selector_handoff(handle,selector);}}
}
