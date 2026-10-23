static_assert(sizeof(SevkCellPart)==72&&sizeof(SevkCellLevel)==16&&sizeof(SevkCellLod)==40);
static_assert(sizeof(SevkCell)==416&&sizeof(SevkCellCascade)==216&&sizeof(SevkCellFrame)==2000);
static_assert(sizeof(SevkCellQuery)==24&&sizeof(SevkCellResult)==16&&sizeof(SevkCellChange)==32&&sizeof(SevkCellStats)==32);
int32_t SEVK_CALL sevk_cells_commit(void* ctx,const SevkCell* cells,uint32_t count,const uint64_t* handles,uint64_t* output,const SevkCellLevel* levels,uint32_t level_count,const SevkCellPart* parts,uint32_t part_count,const float* thresholds,uint32_t threshold_count,const uint8_t* bytes,uint64_t byte_count){return call(ctx,[&](Renderer& r){r.cells_commit(cells,count,handles,output,levels,level_count,parts,part_count,thresholds,threshold_count,bytes,byte_count);return 0;});}
static_assert(sizeof(SevkCellPose)==344);
int32_t SEVK_CALL sevk_cells_update_poses(void* ctx,const uint64_t* handles,const SevkCellPose* poses,uint32_t count){return call_deferrable(ctx,[handles_copy=copy_in(handles,count),poses_copy=copy_in(poses,count),count](Renderer& r){auto* handles=handles_copy.get();auto* poses=poses_copy.get();r.cells_update_poses(handles,poses,count);return 0;});}
int32_t SEVK_CALL sevk_cells_remove(void* ctx,uint64_t handle,SevkCellLod* lod){return call(ctx,[&](Renderer& r){if(!lod)throw std::runtime_error("missing OLD cell selector output");*lod=r.cells_remove(handle);return 0;});}
int32_t SEVK_CALL sevk_cells_prepare(void* ctx,const SevkCellFrame* frame){return call(ctx,[&](Renderer& r){if(!frame)throw std::runtime_error("missing OLD cell frame");r.cells_prepare(*frame);return 0;});}
int32_t SEVK_CALL sevk_cells_preparation(void* ctx,SevkCellQuery* queries,uint32_t query_capacity,uint32_t* query_count,uint32_t* visible,uint32_t visible_capacity,uint32_t* visible_count,SevkCellChange* changes,uint32_t change_capacity,uint32_t* change_count){return call(ctx,[&](Renderer& r){
    if(!query_count||!visible_count||!change_count||(query_capacity&&!queries)||(visible_capacity&&!visible)||(change_capacity&&!changes))throw std::runtime_error("invalid OLD cell preparation output");
    auto& q=r.cells.scene.requests();auto& v=r.cells.scene.visible_owners();auto& c=r.cells.scene.representation_changes();*query_count=uint32_t(q.size());*visible_count=uint32_t(v.size());*change_count=uint32_t(c.size());
    for(uint32_t i=0;i<q.size()&&i<query_capacity;i++)queries[i]={q[i].group,q[i].representative,q[i].cascades,q[i].main?1u:0u,0};
    for(uint32_t i=0;i<v.size()&&i<visible_capacity;i++)visible[i]=v[i];
    for(uint32_t i=0;i<c.size()&&i<change_capacity;i++)changes[i]={{c[i].centre[0],c[i].centre[1],c[i].centre[2]},c[i].radius};return 0;});}
int32_t SEVK_CALL sevk_cells_publish(void* ctx,const SevkCellResult* results,uint32_t count){return call(ctx,[&](Renderer& r){r.cells_publish(results,count);return 0;});}
int32_t SEVK_CALL sevk_cells_cancel(void* ctx){return call(ctx,[&](Renderer& r){if(r.recording)throw std::runtime_error("OLD cell cancel during recording");r.settle_cells();r.cells.scene.cancel();return 0;});}
int32_t SEVK_CALL sevk_cells_emit(void* ctx){return call(ctx,[&](Renderer& r){r.cells_emit();return 0;});}
int32_t SEVK_CALL sevk_cells_stats(void* ctx,SevkCellStats* stats){return call(ctx,[&](Renderer& r){if(!stats||stats->size!=sizeof(SevkCellStats))throw std::runtime_error("invalid OLD cell stats");r.settle_cells();*stats=r.cells.stats;return 0;});}
