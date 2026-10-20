#pragma once
#include "se_old_order.h"
/* Shared GPU preparation schema. The reference entry points are verification
 * only; gpu_scene entry points register committed resident inputs against shared
 * world geometry. Readback waits are verification/handoff operations. */
typedef struct SevkOpaqueActor {
    double translation[3], minimum[3], maximum[3], local_minimum[3], local_maximum[3], inverse[12];
    float basis[9], scale, colour[4];
    uint32_t owner,generation,family,flags,source_first,source_count;
    uint32_t key_colour[2];
    uint32_t appearance_count,hidden_views[2],depth_revision;
} SevkOpaqueActor; /* visible1, old2, parented4, dynamic8, casts16, cube32, skip_main64, skip_forward128 */
typedef struct SevkOpaqueFamily {
    uint32_t generation, first_part, part_count, threshold_count;
    float thresholds[16];
} SevkOpaqueFamily;
typedef struct SevkOpaquePart {
    uint32_t family,lod,first_index,index_count,material,flags,generation,reserved;
    uint32_t textures[4];
    int32_t vertex_offset;uint32_t padding;
    uint32_t draw_first,draw_capacity;
    uint32_t coverage_count,coverage_materials[32];
} SevkOpaquePart; /* depth1, colour2, full_segment4, split_piece8, skip_forward16, skip_main32 */
typedef struct SevkOpaqueView {
    double planes[24],minimum_size_squared,maximum_distance;
    float projection[16];
    int32_t minimum_lod,shift,visible_shift;
    uint32_t flags;
    uint32_t id,light;
} SevkOpaqueView; /* enabled1, depth2, frustum4, dynamic8, static16, forward32 */
typedef struct SevkOpaqueFrame {
    double camera[3],smooth_camera_distance;
    float delta,coefficient,distance_add,distance_mult;
    float hysteresis,dead_zone,dead_zone_mult,maximum_seconds,minimum_seconds;
    uint32_t flags,selection,epoch;
} SevkOpaqueFrame; /* updates1, explicit2 */
typedef struct SevkOpaqueSelector {
    int32_t current,other;
    float transition,start;
    uint32_t initialized,border,generation,epoch;
    double last_camera[3];
} SevkOpaqueSelector;
typedef struct SevkOpaqueReference {
    uint32_t actor,view,material,reserved;
    float rows[12],alpha;
    uint32_t pad[3];
} SevkOpaqueReference;
typedef struct SevkOpaqueDrawRecord {uint32_t actor,part,reference,old;} SevkOpaqueDrawRecord;
typedef struct SevkOpaqueIndirect {uint32_t index_count,instance_count,first_index;int32_t vertex_offset;uint32_t first_instance;} SevkOpaqueIndirect;
typedef struct SevkOpaqueSelection {int32_t current,other;float alpha,other_alpha;} SevkOpaqueSelection;
typedef struct SevkOpaqueReferenceConfig {
    uint32_t size,abi_version,actor_count,family_count,part_count,view_count,vertex_count,index_count;
    uint32_t source_count,reference_capacity,draw_capacity,reserved;
    const SevkOpaqueActor* actors;const SevkOpaqueFamily* families;const SevkOpaquePart* parts;
    const float* vertices;const uint32_t* indices;const uint8_t* source_instances;
    const uint64_t* material_textures;uint32_t material_count,material_reserved;
} SevkOpaqueReferenceConfig;
SEVK_API int32_t SEVK_CALL sevk_opaque_reference_init(void* ctx,const SevkOpaqueReferenceConfig* config);
SEVK_API int32_t SEVK_CALL sevk_opaque_reference_actor(void* ctx,uint32_t index,const SevkOpaqueActor* actor);
SEVK_API int32_t SEVK_CALL sevk_opaque_reference_actors(void* ctx,const uint32_t* indices,const SevkOpaqueActor* actors,uint32_t count,const void* objects,uint32_t object_bytes);
typedef struct SevkOpaqueAppearance {uint32_t material,visible,colour[2];float old_colour[4];} SevkOpaqueAppearance;
typedef struct SevkOpaqueAppearanceUpdate {uint32_t actor,first,count,reserved;} SevkOpaqueAppearanceUpdate;
typedef struct SevkGpuSceneActor {
    uint32_t size,abi_version;uint64_t handle,family,source,group;
    uint32_t source_first,source_count,appearance_first,appearance_count;
    SevkOpaqueActor input;SevkOpaqueSelector selector;uint8_t object[128];
} SevkGpuSceneActor;
/* appearance_count == UINT32_MAX preserves the resident table on pose updates.
 * Registration and family replacement require an explicit complete table. */
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_model(void* ctx,const SevkPassPart* parts,uint32_t count,uint32_t old,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_model_ordered(void* ctx,const SevkPassPart* parts,const SevkOldPartOrder* orders,uint32_t count,uint32_t old,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_family(void* ctx,const float* distances,uint32_t count,const uint64_t* levels,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_source(void* ctx,const void* bytes,uint32_t count,uint32_t cube,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_actor(void* ctx,const SevkGpuSceneActor* actor,const SevkOpaqueAppearance* appearances,uint32_t count,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_updates(void* ctx,const SevkGpuSceneActor* actors,uint32_t count,const SevkOpaqueAppearance* appearances,uint32_t appearance_count);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_publish(void* ctx,uint32_t view_capacity,uint32_t reference_capacity,uint32_t draw_capacity);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_frame(void* ctx,const SevkOpaqueFrame* frame,const SevkOpaqueView* views,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_selector(void* ctx,uint64_t actor,SevkOpaqueSelector* selector);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_remove(void* ctx,uint32_t kind,uint64_t handle);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_queries(void* ctx,const SevkOpaqueView* views,uint32_t count,SevkPassQuery* queries,uint32_t capacity,uint32_t* query_count);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_visibility(void* ctx,const SevkPassQueryResult* results,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_clear(void* ctx);
SEVK_API int32_t SEVK_CALL sevk_opaque_reference_appearances(void* ctx,const SevkOpaqueAppearanceUpdate* updates,uint32_t count,const SevkOpaqueAppearance* appearances,uint32_t appearance_count);
SEVK_API int32_t SEVK_CALL sevk_opaque_reference_frame(void* ctx,const SevkOpaqueFrame* frame,const SevkOpaqueView* views,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_opaque_reference_depth(void* ctx,uint32_t view,float* pixels,uint32_t count,double* camera,float* projection);
SEVK_API int32_t SEVK_CALL sevk_opaque_stock_pipeline(void* ctx,uint32_t id,uint32_t old,const void* vs,uint64_t vb,const void* ps,uint64_t pb);
SEVK_API int32_t SEVK_CALL sevk_opaque_stock_inputs(void* ctx,uint64_t mesh,const void* objects,uint32_t object_bytes,uint32_t old,uint32_t main_pipeline,uint32_t depth_pipeline);
typedef struct SevkOpaqueStockBatch {uint64_t mesh;uint32_t old,main_pipeline,depth_pipeline,forward_pipeline,flags,reserved;} SevkOpaqueStockBatch;
/* Each part's reserved field is its immutable batch index. Meshes in one batch
 * must share vertex/index pages; argument offsets address those pages directly. */
SEVK_API int32_t SEVK_CALL sevk_opaque_stock_batches(void* ctx,const SevkOpaqueStockBatch* batches,uint32_t batch_count,const uint64_t* part_meshes,uint32_t part_count,const void* objects,uint32_t object_bytes);
SEVK_API int32_t SEVK_CALL sevk_opaque_reference_readback(void* ctx,SevkOpaqueSelector* selectors,uint32_t actor_count,
    SevkOpaqueSelection* selections,uint32_t selection_count,SevkOpaqueReference* references,uint32_t reference_capacity,
    SevkOpaqueIndirect* args,SevkOpaqueDrawRecord* draws,uint32_t draw_capacity,uint32_t* counts,uint32_t view_count,uint32_t* errors);

SEVK_API int32_t SEVK_CALL sevk_readback_spot(void* ctx,uint32_t light,void* data,uint64_t bytes);

SEVK_API int32_t SEVK_CALL sevk_gpu_scene_supported(void* ctx,uint32_t* supported);

SEVK_API int32_t SEVK_CALL sevk_gpu_scene_topology_counts(void* ctx,uint64_t* counts,uint32_t count);

SEVK_API int32_t SEVK_CALL sevk_gpu_scene_compute_times(void* ctx,uint64_t* values,uint32_t count);
/* Optional diagnostic pairs: calls, nanoseconds for each internal phase. */
SEVK_API int32_t SEVK_CALL sevk_gpu_scene_cpu_costs(void* ctx,uint64_t* values,uint32_t count);
