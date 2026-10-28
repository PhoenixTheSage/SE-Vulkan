#pragma once
#include "se_scene_actor.h"
/* Render-owner APIs copy input arrays. Handles are generation checked.
 * Views: 0 main, 1..8 cascades, 9..14 probe faces, 15+2*shadow-slot static spot,
 * 16+2*shadow-slot dynamic spot. light maps the slot to its actual light index.
 * Resource requests do not advance selectors again. */
typedef struct SevkPassPart {uint64_t mesh,colour,normal,extensions,alpha;uint32_t first,count,material,shader,flags,reserved;} SevkPassPart;
typedef struct SevkPassAppearance {uint32_t material,visible;uint16_t colour[4];} SevkPassAppearance;
typedef struct SevkPassActor {
    /* flags bit128: update keeps resident appearance; appearance_count must
     * be zero. Registration always requires a complete appearance input. */
    uint64_t handle,family,group;uint32_t owner,flags;
    double translation[3],bounds[6];float basis[9],scale;
    uint16_t key_colour[3],reserved;SevkSceneLod lod;double last_camera[3];uint32_t have_camera,appearance_offset,appearance_count,padding;
} SevkPassActor;
typedef struct SevkPassView {uint32_t id,kind,flags,light;double planes[24],minimum_size_squared,maximum_distance;int32_t minimum_lod,shift,visible_shift,padding;} SevkPassView;
typedef struct SevkPassQuery {uint64_t group,views;uint32_t representative,reserved;} SevkPassQuery;
typedef struct SevkPassQueryResult {uint64_t group,hidden_views;} SevkPassQueryResult;
typedef struct SevkPassMissing {uint64_t family;uint32_t lod,reserved;} SevkPassMissing;
/* Sparse main-view transparent consumer tickets. Alpha follows the existing
 * glass object shader, not the opaque simple-instance encoding. */
typedef struct SevkPassTransparent {uint64_t actor;uint32_t owner,lod;float alpha;uint32_t reserved;} SevkPassTransparent;
typedef struct SevkPassFallback {uint64_t actor;uint32_t owner,reserved;SevkSceneLod lod;uint32_t padding;} SevkPassFallback;
/* Diagnostic-only group/owner readback; never used to emit managed packets. */
typedef struct SevkPassGroupAudit {uint32_t view,kind,shader,first,count,instances,owner_offset,flags;uint64_t mesh,colour,normal,extensions,alpha;} SevkPassGroupAudit;
typedef struct SevkPassProfile {uint32_t size,abi_version;uint64_t prepares,publishes,committed_actors,selected_actors,emitted_rows,stream_bytes,groups;double commits_us,prepare_us,requests_us,publish_us,emit_us,spots_us;uint64_t appearance_entries;double actor_parse_us,actor_apply_us;double select_us,merge_us,settle_us;uint64_t batch_cache_misses,batch_cache_entries;} SevkPassProfile;
SEVK_API int32_t SEVK_CALL sevk_pass_init_pipeline(void* ctx,uint32_t id,const void* vertex,uint64_t vb,const void* pixel,uint64_t pb);
SEVK_API int32_t SEVK_CALL sevk_pass_set_placeholder(void* ctx,const void* bytes,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_pass_register_model(void* ctx,const SevkPassPart* parts,uint32_t count,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_pass_register_mixed_model(void* ctx,const SevkPassPart* parts,uint32_t count,uint32_t glass,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_pass_transparent_requests(void* ctx,SevkPassTransparent* requests,uint32_t capacity,uint32_t* count);
SEVK_API int32_t SEVK_CALL sevk_pass_register_family(void* ctx,const float* distances,uint32_t count,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_pass_bind_level(void* ctx,uint64_t family,uint32_t lod,uint64_t model);
SEVK_API int32_t SEVK_CALL sevk_pass_register_actor(void* ctx,const SevkPassActor* actor,const SevkPassAppearance* appearance,uint32_t count,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_pass_update_actors(void* ctx,const SevkPassActor* actors,uint32_t count,const SevkPassAppearance* appearance,uint32_t appearance_count);
SEVK_API int32_t SEVK_CALL sevk_pass_remove(void* ctx,uint32_t kind,uint64_t handle);
SEVK_API int32_t SEVK_CALL sevk_pass_prepare(void* ctx,const SevkSceneFrame* frame,const SevkPassView* views,uint32_t count);
/* Same as sevk_pass_prepare, on the pass background worker; preparation readers and publish join it. */
SEVK_API int32_t SEVK_CALL sevk_pass_prepare_async(void* ctx,const SevkSceneFrame* frame,const SevkPassView* views,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_pass_get_preparation(void* ctx,SevkPassQuery* queries,uint32_t query_capacity,uint32_t* query_count,SevkPassMissing* missing,uint32_t missing_capacity,uint32_t* missing_count,SevkPassFallback* fallback,uint32_t fallback_capacity,uint32_t* fallback_count);
SEVK_API int32_t SEVK_CALL sevk_pass_publish(void* ctx,const SevkPassQueryResult* results,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_pass_cancel(void* ctx);
SEVK_API int32_t SEVK_CALL sevk_pass_clear_frame(void* ctx);
SEVK_API int32_t SEVK_CALL sevk_pass_actor_selector(void* ctx,uint64_t actor,uint32_t write,SevkSceneLod* lod,double* camera,uint32_t* have_camera);
SEVK_API int32_t SEVK_CALL sevk_pass_visible_owners(void* ctx,uint32_t* owners,uint32_t capacity,uint32_t* count);
SEVK_API int32_t SEVK_CALL sevk_pass_group_audit(void* ctx,SevkPassGroupAudit* groups,uint32_t group_capacity,uint32_t* group_count,uint32_t* owners,uint32_t owner_capacity,uint32_t* owner_count);
SEVK_API int32_t SEVK_CALL sevk_pass_profile(void* ctx,uint32_t enable,SevkPassProfile* profile);
// Publication batch cache on (1) or off (0) at runtime (SE_VULKAN_PASS_BATCH_CACHE sets the start value); output is identical.
SEVK_API int32_t SEVK_CALL sevk_pass_set_batch_cache(void* ctx,uint32_t enable);
