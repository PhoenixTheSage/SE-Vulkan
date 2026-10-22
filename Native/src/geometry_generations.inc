// Logical IDs keep native raster owner identity separate from pack registration.
bool native_geometry_id(uint32_t id){return id>=4096&&id<4096+2*world.pipelines.size();}
uint32_t native_geometry_pipeline(uint32_t id){return (id-4096)/2;}
PostStage build_geometry_generation(uint32_t id,const SevkPostprocessModule* modules,uint32_t count){
    const SevkPostprocessModule *vertex=nullptr,*pixel=nullptr;
    for(uint32_t i=0;i<count;++i){if(modules[i].stage==id-1)vertex=&modules[i];if(modules[i].stage==id)pixel=&modules[i];}
    if(!vertex||!pixel)throw std::runtime_error("incomplete native geometry generation pair");
    PostStage result{};uint32_t fade=world.distance_fade_at;
    result.geometry=build_world_pipeline(native_geometry_pipeline(id),vertex->spirv,vertex->bytes,pixel->spirv,pixel->bytes,fade);
    return result;
}
void geometry_invalidate_shadows(){
    saved_shadows=false;shadow_image_mask=0;for(auto& ready:spots.frozen_ready)ready=0;
}
void geometry_note_execution(uint32_t pipeline){
    if(post.geometry.empty())return;uint32_t vertex=2*pipeline;
    for(uint32_t index:{vertex,vertex+1}){auto& stage=post.geometry[index];if(!stage.generation)continue;
        std::atomic_ref<uint64_t>(stage.executions).fetch_add(1,std::memory_order_relaxed);
        std::atomic_ref<uint64_t>(stage.last_completion).store(next_completion+1,std::memory_order_relaxed);
        std::atomic_ref<uint32_t>(stage.last_frame).store(guest.frame.frame_index,std::memory_order_relaxed);
    }
}
