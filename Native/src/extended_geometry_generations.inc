// Additional real raster owners, deliberately distinct from classic world IDs.
bool native_cell_id(uint32_t id){return id>=16384&&id<16440;}
bool native_gpu_new_id(uint32_t id){return id>=32768&&id<32824;}
bool native_gpu_old_id(uint32_t id){return id>=49152&&id<54464;}
bool native_extended_geometry_id(uint32_t id){return native_cell_id(id)||native_gpu_new_id(id)||native_gpu_old_id(id);}
bool native_gpu_owner_id(uint32_t id){return native_gpu_new_id(id)||native_gpu_old_id(id);}
uint32_t extended_geometry_base(uint32_t id){return native_cell_id(id)?16384:native_gpu_new_id(id)?32768:49152;}
std::vector<PostStage>& extended_geometry_states(uint32_t id){return native_cell_id(id)?post.cells:native_gpu_new_id(id)?post.gpu_new:post.gpu_old;}
PostStage* extended_geometry_state(uint32_t id){auto& states=extended_geometry_states(id);return states.empty()?nullptr:&states.at(id-extended_geometry_base(id));}
bool extended_geometry_available(uint32_t id){uint32_t pipeline=(id-extended_geometry_base(id))/2;return native_cell_id(id)?pass_scene.pipelines[pipeline].back!=VK_NULL_HANDLE:(native_gpu_new_id(id)?opaque_stock.newer[pipeline]:opaque_stock.older[pipeline]).back!=VK_NULL_HANDLE;}
PostStage build_extended_geometry_generation(uint32_t id,const SevkPostprocessModule* modules,uint32_t count){
    const SevkPostprocessModule *vertex=nullptr,*pixel=nullptr;
    for(uint32_t i=0;i<count;++i){if(modules[i].stage==id-1)vertex=&modules[i];if(modules[i].stage==id)pixel=&modules[i];}
    if(!vertex||!pixel)throw std::runtime_error("incomplete extended geometry generation pair");PostStage result{};uint32_t pipeline=(id-extended_geometry_base(id))/2;
    if(native_cell_id(id)){auto p=build_pass_pipeline(pipeline,vertex->spirv,vertex->bytes,pixel->spirv,pixel->bytes);result.pipeline=p.back;result.other_hdr=p.no_cull;}
    else{auto p=build_opaque_stock_pipeline(pipeline,native_gpu_old_id(id)?1:0,vertex->spirv,vertex->bytes,pixel->spirv,pixel->bytes);result.pipeline=p.back;result.other_hdr=p.no_cull;result.pipeline_layout=p.layout;}
    return result;
}
void extended_geometry_activate(uint32_t id,PostStage& candidate){
    auto& current=*extended_geometry_state(id);PostStage retired=std::move(current);uint32_t pipeline=(id-extended_geometry_base(id))/2;
    if(id&1u){if(native_cell_id(id)){auto& live=pass_scene.pipelines[pipeline];retired.pipeline=live.back;retired.other_hdr=live.no_cull;live={candidate.pipeline,candidate.other_hdr};}
        else{auto& live=native_gpu_new_id(id)?opaque_stock.newer[pipeline]:opaque_stock.older[pipeline];retired.pipeline=live.back;retired.other_hdr=live.no_cull;retired.pipeline_layout=live.layout;live={candidate.pipeline,candidate.other_hdr,candidate.pipeline_layout};}
        uint32_t role=native_gpu_old_id(id)?world_unfade(pipeline)/32:pipeline<12?pipeline/4:0;if(role==1)geometry_invalidate_shadows();}
    post.retired.emplace_back(next_completion,std::move(retired));current=std::move(candidate);current.pipeline=current.other_hdr=VK_NULL_HANDLE;current.pipeline_layout=VK_NULL_HANDLE;
}
void extended_geometry_note(uint32_t pixel){
    auto* state=extended_geometry_state(pixel);if(!state||!state->generation||!state->track_geometry)return;if(gpu_owner_condition!=UINT32_MAX){gpu_owner_record_direct(pixel,gpu_owner_condition);return;}
    for(uint32_t id:{pixel-1,pixel}){auto& stage=*extended_geometry_state(id);std::atomic_ref<uint64_t>(stage.executions).fetch_add(1,std::memory_order_relaxed);std::atomic_ref<uint64_t>(stage.last_completion).store(next_completion+1,std::memory_order_relaxed);std::atomic_ref<uint32_t>(stage.last_frame).store(guest.frame.frame_index,std::memory_order_relaxed);}
}
