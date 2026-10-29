// Native OLD cube grid cells: committed inputs, frame decision and emission
// of the same recipe world draws the managed retained submission queues.
void cells_commit(const SevkCell* input,uint32_t count,const uint64_t* handles,uint64_t* output,const SevkCellLevel* levels,uint32_t level_count,const SevkCellPart* parts,uint32_t part_count,const float* thresholds,uint32_t threshold_count,const uint8_t* bytes,uint64_t byte_count){settle_cells();cells_drop_async();
    if(recording||count>1000000||(count&&(!input||!handles||!output))||(!levels&&level_count)||(!parts&&part_count)||(!thresholds&&threshold_count)||(!bytes&&byte_count))throw std::runtime_error("invalid OLD cell commit");
    for(uint32_t i=0;i<count;++i){const auto& c=input[i];
        if(c.size!=sizeof(SevkCell)||c.flags>511||c.lod.reserved||uint64_t(c.level_offset)+c.level_count>level_count||uint64_t(c.threshold_offset)+c.threshold_count>threshold_count||c.instance_offset+uint64_t(c.instance_count)*64>byte_count)throw std::runtime_error("invalid OLD cell input");
        for(double v:c.bounds)if(!std::isfinite(v))throw std::runtime_error("nonfinite OLD cell bounds");for(double v:c.world)if(!std::isfinite(v))throw std::runtime_error("nonfinite OLD cell pose");for(double v:c.inverse)if(!std::isfinite(v))throw std::runtime_error("nonfinite OLD cell inverse");
        old_cells::Cell cell{};cell.owner=c.owner;cell.group=c.group;uint32_t f=c.flags;
        cell.visible=f&1;cell.skip_main=f&2;cell.cast_flag=f&4;cell.casts=f&8;cell.dynamic_shadow=f&16;cell.parented=f&32;cell.lod_eligible=f&64;cell.skinning=f&128;cell.outside_far=f&256;
        cell.bounds={{c.bounds[0],c.bounds[1],c.bounds[2]},{c.bounds[3],c.bounds[4],c.bounds[5]}};std::copy_n(c.local_minimum,3,cell.local_minimum);std::copy_n(c.local_maximum,3,cell.local_maximum);
        std::copy_n(c.world,16,cell.world);std::copy_n(c.inverse,16,cell.inverse);
        cell.thresholds.assign(thresholds+c.threshold_offset,thresholds+c.threshold_offset+c.threshold_count);for(float t:cell.thresholds)if(!std::isfinite(t)||t<0)throw std::runtime_error("invalid OLD cell LOD distance");
        for(uint32_t l=0;l<c.level_count;++l){const auto& level=levels[c.level_offset+l];if(level.reserved||uint64_t(level.part_offset)+level.part_count>part_count)throw std::runtime_error("invalid OLD cell level");
            old_cells::Level out{level.model,{}};
            for(uint32_t p=0;p<level.part_count;++p){const auto& part=parts[level.part_offset+p];if(uint64_t(part.constant_offset)+part.constant_bytes>byte_count)throw std::runtime_error("OLD cell constants outside commit");validate_old_order(part.old_order);
                old_cells::Part item{part.recipe,part.view_flags,part.row0,part.row1,part.row2,part.alpha_offset,part.main_part,part.old_order,{}};item.constants.assign(bytes+part.constant_offset,bytes+part.constant_offset+part.constant_bytes);out.parts.push_back(std::move(item));}
            cell.levels.push_back(std::move(out));}
        cell.count=c.instance_count;cell.instances.assign(bytes+c.instance_offset,bytes+c.instance_offset+uint64_t(c.instance_count)*64);
        cell.lod={c.lod.current,c.lod.next,c.lod.transition,c.lod.start,c.lod.initialized!=0,c.lod.border!=0};cell.shadow_current=c.lod.shadow_current;cell.shadow_next=c.lod.shadow_next;cell.shadow_alpha=c.lod.shadow_alpha;
        if(handles[i]){cells.scene.update(handles[i],std::move(cell));output[i]=handles[i];}else output[i]=cells.scene.add(std::move(cell));
    }
}
void cells_update_poses(const uint64_t* handles,const SevkCellPose* poses,uint32_t count){settle_cells();cells_drop_async();
    if(frame_open()||count>1000000||(count&&(!handles||!poses)))throw std::runtime_error("invalid OLD cell pose commit");
    for(uint32_t i=0;i<count;++i){const auto& p=poses[i];if(p.size!=sizeof(SevkCellPose)||p.flags>511)throw std::runtime_error("invalid OLD cell pose");
        for(double v:p.bounds)if(!std::isfinite(v))throw std::runtime_error("nonfinite OLD cell bounds");for(double v:p.world)if(!std::isfinite(v))throw std::runtime_error("nonfinite OLD cell pose");for(double v:p.inverse)if(!std::isfinite(v))throw std::runtime_error("nonfinite OLD cell inverse");
        auto& cell=cells.scene.pose_target(handles[i]);uint32_t f=p.flags;cell.group=p.group;
        cell.visible=f&1;cell.skip_main=f&2;cell.cast_flag=f&4;cell.casts=f&8;cell.dynamic_shadow=f&16;cell.parented=f&32;cell.skinning=f&128;cell.outside_far=f&256;
        cell.bounds={{p.bounds[0],p.bounds[1],p.bounds[2]},{p.bounds[3],p.bounds[4],p.bounds[5]}};std::copy_n(p.local_minimum,3,cell.local_minimum);std::copy_n(p.local_maximum,3,cell.local_maximum);
        std::copy_n(p.world,16,cell.world);std::copy_n(p.inverse,16,cell.inverse);
    }
}
SevkCellLod cells_remove(uint64_t handle){settle_cells();cells_drop_async();
    if(recording)throw std::runtime_error("OLD cell removal during recording");
    const auto& cell=cells.scene.cell(handle);SevkCellLod out{cell.lod.current,cell.lod.next,cell.lod.transition,cell.lod.start,cell.lod.initialized,cell.lod.border,cell.shadow_current,cell.shadow_next,cell.shadow_alpha,0};
    cells.scene.remove(handle);return out;
}
// Plan 2 A2: early cell preparation. cells_prepare_async runs the same (pure) selection on a background job
// during Build, next to the pass scene's early preparation. The later synchronous cells_prepare reuses it only if
// its frame is byte-identical and no cell changed since; otherwise it prepares again. Every cells entry point
// joins the job first (settle_cells, cells_join).
std::unique_ptr<native_pass::Background> cells_background;bool cells_async_running=false,cells_async_ready=false;SevkCellFrame cells_async_frame{};
uint64_t cells_async_hits=0,cells_async_misses=0;
void cells_join(){if(!cells_async_running)return;cells_async_running=false;cells_background->wait();}
// A mutation or a cancel after an early preparation discards it (the scene would refuse the mutation).
void cells_drop_async(){cells_join();if(!cells_async_ready)return;cells_async_ready=false;++cells_async_misses;cells.scene.cancel();}
old_cells::Frame cells_frame(const SevkCellFrame& f){
    if(frame_open()||f.size!=sizeof(SevkCellFrame)||f.flags>15)throw std::runtime_error("invalid OLD cell frame");
    auto plane_set=[](const double* values,scene_actor::Frustum& frustum){for(uint32_t j=0;j<6;j++){const double* p=values+j*4;for(uint32_t k=0;k<4;k++)if(!std::isfinite(p[k]))throw std::runtime_error("nonfinite OLD cell plane");frustum.planes[j]={p[0],p[1],p[2],p[3]};}frustum.enabled=true;};
    old_cells::Frame frame{};frame.camera={f.camera[0],f.camera[1],f.camera[2]};plane_set(f.main_planes,frame.main);frame.new_views=f.flags&1;frame.stock_far=f.flags&2;
    frame.probe_distance=f.probe_distance;frame.shadow_distance=f.shadow_distance;
    for(uint32_t c=0;c<8;c++){const auto& in=f.cascades[c];if(in.reserved||in.active>1||in.size_test>1||in.fitted>1)throw std::runtime_error("invalid OLD cell cascade");auto& out=frame.cascades[c];out.active=in.active;out.size_test=in.size_test;out.fitted=in.fitted;out.size_squared=in.size_squared;if(in.fitted)plane_set(in.planes,out.frustum);}
    frame.global.explicit_lod=f.flags&4;frame.global.updates=f.flags&8;frame.global.selection=f.selection;frame.global.maximum_seconds=f.maximum_seconds;frame.global.distance_add=f.distance_add;frame.global.distance_mult=f.distance_mult;
    frame.pass={f.minimum_lod,f.shift,0};frame.coefficient=f.coefficient;frame.delta=f.delta;
    if(pass_scene.scene.parallel()&&!cells.jobs)cells.jobs=std::make_unique<native_pass::ViewJobs>(sevk_jobs::CellView);else if(!pass_scene.scene.parallel())cells.jobs.reset();
    return frame;
}
void cells_prepare(const SevkCellFrame& f){settle_cells();
    if(cells_async_ready){cells_async_ready=false;if(std::memcmp(&f,&cells_async_frame,sizeof f)==0){++cells_async_hits;return;}++cells_async_misses;cells.scene.cancel();}
    auto frame=cells_frame(f);cells.scene.prepare(frame,cells.jobs.get());
}
void cells_prepare_async(const SevkCellFrame& f){settle_cells();cells_drop_async();
    auto frame=cells_frame(f);if(!cells_background)cells_background=std::make_unique<native_pass::Background>(sevk_jobs::CellView);
    cells_async_frame=f;cells_async_ready=true;cells_async_running=true;
    cells_background->start([this,frame]{cells.scene.prepare(frame,cells.jobs.get());});
}
void cells_publish(const SevkCellResult* results,uint32_t count){settle_cells();if(cells_async_ready){cells_async_ready=false;++cells_async_misses;cells.scene.cancel();}
    if(frame_open()||count>1000000||(!results&&count))throw std::runtime_error("invalid OLD cell visibility publication");
    std::vector<old_cells::Result> values;values.reserve(count);for(uint32_t i=0;i<count;i++){if(results[i].main_hidden>1||results[i].cascades_hidden>255)throw std::runtime_error("invalid OLD cell visibility result");values.push_back({results[i].group,results[i].main_hidden!=0,results[i].cascades_hidden});}
    cells.scene.publish(values,deferred_cell_emission&&deferred_cell_publish);
}
// Called after every managed packet and its order metadata are queued, so
// managed physical indices (highlights, exclusions) are unchanged.
// With deferred emission (default) the draws are appended when recording
// starts, or earlier when a call touches the queued world, recipes or cells,
// so recording sees the same draw list in the same order.
void cells_emit(){
    if(frame_open()||!world.current().queued)throw std::runtime_error("OLD cells must follow the queued world frame");
    if(cells_emit_pending)throw std::runtime_error("OLD cells already emitted for this frame");
    if(deferred_cell_emission){
        // Async emission (default; SE_VULKAN_ASYNC_CELL_EMISSION=0 emits at the start of the recording instead): the
        // emission is the last world queue step, so it runs on a worker into this queued submission while Build moves
        // on. Every later native call and the recording's settle join it first (join_cells_emit), so nothing reads or
        // grows the submission meanwhile. It cost the recording thread about 0.45 ms a frame (C1).
        if(async_cell_emission){WorldSubmission* target=&world.current();cells_emit_async=true;
            sevk_jobs::shared().submit(cells_emit_job,sevk_jobs::Upload,sevk_jobs::Priority::High,[this,target]{emit_cells_into(*target);});return;}
        cells_emit_pending=true;return;}
    emit_cells_now();
}
// Joins an async emission and applies its frame counters on the thread that owns them.
void join_cells_emit(){if(!cells_emit_async)return;cells_emit_async=false;cells_emit_job.wait();}
void apply_cells_emit_counts(){performance.logical_draws+=cells_emit_uncounted;performance.submitted_draws+=cells_emit_uncounted;cells_emit_uncounted=0;}
// The deferred world intake settles first: cell draws append after the queued world draws.
// Opt-in (SE_VULKAN_DEFERRED_CELL_PUBLISH=1), like the deferred world intake.
bool deferred_cell_publish=false;
void settle_cells(){auto at=std::chrono::steady_clock::now();auto part=[&](const char* name){auto now=std::chrono::steady_clock::now();if(tl_recording_thread)record_count(name,std::chrono::duration<double,std::micro>(now-at).count());at=now;};
    cells_join();part("settle cells join us");join_cells_emit();if(!WorldState::tl_scene_side)apply_cells_emit_counts();part("settle cells async join us");settle_world();part("settle world us");if(cells_emit_pending){cells_emit_pending=false;emit_cells_now();part("settle cells emit us");}}
void emit_cells_now(){emit_cells_into(world.current());apply_cells_emit_counts();}
// Writes the frame's OLD cell draws into out. Runs on the recording thread, or on a worker for async emission, so it
// names its submission (world.current() depends on the calling thread) and leaves the frame counters to the caller.
void emit_cells_into(WorldSubmission& out){
    if(!out.queued)throw std::runtime_error("OLD cells must follow the queued world frame");
    cells.stats=SevkCellStats{sizeof(SevkCellStats),cells.scene.live()};if(!cells.scene.ready())return;
    if(tl_recording_thread)cpu_lap("cells finish");bool counted=!cells.scene.building();cells.scene.finish();if(tl_recording_thread)cpu_lap("cells records");if(!counted)cells_emit_uncounted+=uint32_t(cells.scene.emitted().size());
    const auto& draws=cells.scene.emitted();auto camera=cells.scene.current_frame().camera;
    if(out.draws.size()+draws.size()>65536)throw std::runtime_error("OLD cell draws exceed the queued frame budget");if(tl_recording_thread)record_count("cells emitted draws",double(draws.size()));
    std::unordered_map<const old_cells::Cell*,uint32_t> instance_at;uint64_t object_start=out.draw_objects.size(),instance_start=out.instances.data.size();
    // The draw records, validation and offsets stay serial and in order; the constant and instance bytes (most of the
    // 0.5 ms this cost the recording thread, C1) are written afterwards, on the pool for large frames.
    instance_at.reserve(draws.size());out.draws.reserve(out.draws.size()+draws.size());
    uint64_t object_cursor=object_start,instance_cursor=instance_start;std::vector<uint32_t> object_offsets;object_offsets.reserve(draws.size());
    std::vector<std::pair<const old_cells::Cell*,uint32_t>> instance_copies;
    for(const auto& d:draws){
        uint64_t handle=d.part->recipe;uint32_t index=uint32_t(handle)-1,generation=uint32_t(handle>>32);
        if(index>=world.recipes.size()||!generation)throw std::runtime_error("invalid OLD cell recipe");auto& recipe=world.recipes[index];if(!recipe.alive||recipe.generation!=generation)throw std::runtime_error("stale OLD cell recipe generation");
        validate_world_recipe(recipe);const auto& packet=recipe.packet;
        if(world.pipelines[packet.pipeline].type<2||packet.object_bytes!=d.part->constants.size())throw std::runtime_error("OLD cell recipe contract mismatch");
        if(world_blended_decal(packet.pipeline)&&(d.flags&(512u|16384u))!=(512u|16384u))throw std::runtime_error("mesh decal recipe must exclude depth and forward passes");
        WorldDraw own{};own.packet=packet;own.packet.instance_count=d.cell->count;own.packet.flags=d.flags;own.packet.reserved=d.cascades;std::memcpy(own.packet.origin,d.origin,12);own.packet.radius=d.radius;
        own.recipe=&recipe;own.recipe_generation=generation;own.geometry=recipe.geometry;
        own.object_at=uint32_t(object_cursor);own.object_n=packet.object_bytes;object_cursor+=world_align(packet.object_bytes);object_offsets.push_back(own.object_at);
        if(object_cursor>256ull*1024*1024)throw std::runtime_error("OLD cell frame exceeds bounded upload budget");
        auto found=instance_at.find(d.cell);if(found==instance_at.end()){uint32_t at=uint32_t(instance_cursor);instance_cursor+=d.cell->instances.size();if(instance_cursor>256ull*1024*1024)throw std::runtime_error("OLD cell frame exceeds bounded upload budget");instance_copies.push_back({d.cell,at});found=instance_at.emplace(d.cell,at).first;}
        own.instance_at=found->second;own.instance_n=d.cell->count*64;
        // The owner is the OLD order tie-break, matching managed packets.
        own.old_order=d.part->old_order;own.owner=d.cell->owner;if(own.old_order.valid&&!(own.packet.flags&0x04000000u))throw std::runtime_error("OLD metadata requires source OLD provenance");
        own.main_part=d.part->main_part;out.draws.push_back(own);
    }
    if(tl_recording_thread)cpu_lap("cells bytes");out.draw_objects.resize(size_t(object_cursor));out.instances.data.resize(size_t(instance_cursor));
    uint8_t* objects=out.draw_objects.data();uint8_t* instances=out.instances.data.data();uint32_t copies=uint32_t(instance_copies.size());
    auto write=[&](uint32_t k){if(k<draws.size())old_cells::Scene::write_constants(draws[k],camera,objects+object_offsets[k]);else{const auto& [cell,at]=instance_copies[k-draws.size()];std::memcpy(instances+at,cell->instances.data(),cell->instances.size());}};
    uint32_t items=uint32_t(draws.size())+copies;
    if(items>=256)sevk_jobs::parallel_for(sevk_jobs::Upload,sevk_jobs::Priority::High,4,items,write);else for(uint32_t k=0;k<items;++k)write(k);
    uint32_t n=uint32_t(draws.size());out.validation_stats.draws+=n;out.queue_work.draws+=n;out.recipe_stats.draws+=n;out.recipe_stats.geometry_reused+=n;
    cells.stats.drawn=uint32_t(instance_at.size());cells.stats.draws=n;cells.stats.object_bytes=out.draw_objects.size()-object_start;cells.stats.instance_bytes=out.instances.data.size()-instance_start;
    cells.scene.consume();
}
