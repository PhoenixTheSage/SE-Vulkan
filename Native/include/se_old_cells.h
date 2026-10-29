#pragma once
#include <stdint.h>
#include "se_old_order.h"
/* Native OLD cube grid cell producer. Managed code commits static cell inputs
 * and per-LOD prepared parts; native reproduces the retained-submission frame
 * decision and appends the same world draws after the managed packets. */
typedef struct SevkCellPart {
    uint64_t recipe;
    uint32_t view_flags,row0,row1,row2,alpha_offset,main_part,constant_offset,constant_bytes;
    SevkOldPartOrder old_order;
} SevkCellPart;
typedef struct SevkCellLevel {uint32_t model,part_offset,part_count,reserved;} SevkCellLevel;
typedef struct SevkCellLod {
    int32_t current,next;float transition,start;uint32_t initialized,border;
    int32_t shadow_current,shadow_next;float shadow_alpha;uint32_t reserved;
} SevkCellLod;
/* flags: 1 visible, 2 skip main, 4 cast flag, 8 casts, 16 dynamic shadow,
 * 32 parented, 64 LOD eligible, 128 cube skinning, 256 draw outside view distance. */
typedef struct SevkCell {
    uint32_t size,owner,flags,instance_count;
    uint64_t group,instance_offset;
    double bounds[6];float local_minimum[3],local_maximum[3];
    double world[16],inverse[16];
    uint32_t level_offset,level_count,threshold_offset,threshold_count;
    SevkCellLod lod;
} SevkCell;
/* Pose/ownership-only commit; flags as SevkCell except bit 64 (LOD eligibility), which stays. */
typedef struct SevkCellPose {
    uint32_t size,flags;uint64_t group;double bounds[6];float local_minimum[3],local_maximum[3];double world[16],inverse[16];
} SevkCellPose;
typedef struct SevkCellCascade {uint32_t active,size_test,fitted,reserved;double size_squared;double planes[24];} SevkCellCascade;
/* flags: 1 NEW actor views, 2 stock far cascades, 4 explicit LOD, 8 LOD updates. */
typedef struct SevkCellFrame {
    uint32_t size,flags;double camera[3];double main_planes[24];
    double probe_distance,shadow_distance;SevkCellCascade cascades[8];
    float coefficient,delta,maximum_seconds,distance_add,distance_mult;int32_t selection,minimum_lod,shift;
} SevkCellFrame;
typedef struct SevkCellQuery {uint64_t group;uint32_t representative,cascades,main,reserved;} SevkCellQuery;
typedef struct SevkCellResult {uint64_t group;uint32_t main_hidden,cascades_hidden;} SevkCellResult;
typedef struct SevkCellChange {double centre[3],radius;} SevkCellChange;
typedef struct SevkCellStats {uint32_t size,live,drawn,draws;uint64_t object_bytes,instance_bytes;} SevkCellStats;
/* handles[i]==0 registers a cell; output handles are written in order. */
SEVK_API int32_t SEVK_CALL sevk_cells_commit(void* ctx,const SevkCell* cells,uint32_t count,const uint64_t* handles,uint64_t* output,const SevkCellLevel* levels,uint32_t level_count,const SevkCellPart* parts,uint32_t part_count,const float* thresholds,uint32_t threshold_count,const uint8_t* bytes,uint64_t byte_count);
SEVK_API int32_t SEVK_CALL sevk_cells_update_poses(void* ctx,const uint64_t* handles,const SevkCellPose* poses,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_cells_remove(void* ctx,uint64_t handle,SevkCellLod* lod);
SEVK_API int32_t SEVK_CALL sevk_cells_prepare(void* ctx,const SevkCellFrame* frame);
/* Early preparation on a background job; a later sevk_cells_prepare with a byte-identical frame and no cell
   change in between reuses it. */
SEVK_API int32_t SEVK_CALL sevk_cells_prepare_async(void* ctx,const SevkCellFrame* frame);
SEVK_API int32_t SEVK_CALL sevk_cells_preparation(void* ctx,SevkCellQuery* queries,uint32_t query_capacity,uint32_t* query_count,uint32_t* visible,uint32_t visible_capacity,uint32_t* visible_count,SevkCellChange* changes,uint32_t change_capacity,uint32_t* change_count);
SEVK_API int32_t SEVK_CALL sevk_cells_publish(void* ctx,const SevkCellResult* results,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_cells_cancel(void* ctx);
SEVK_API int32_t SEVK_CALL sevk_cells_emit(void* ctx);
SEVK_API int32_t SEVK_CALL sevk_cells_stats(void* ctx,SevkCellStats* stats);
