#pragma once
#include <stdint.h>
#if defined(_WIN32)
#define SEVK_API __declspec(dllexport)
#define SEVK_CALL __cdecl
#else
#define SEVK_API __attribute__((visibility("default")))
#define SEVK_CALL
#endif
#ifdef __cplusplus
extern "C" {
#endif
#define SEVK_ABI_VERSION 1u
#include "se_old_order.h"
#include "se_media.h"
#include "se_gpu_opaque.h"
#include "se_pass_scene.h"
#include "se_old_cells.h"
#include "se_gpu_opaque_reference.h"
#include "se_critical_profile.h"
/* All callers use natural alignment (pack 8), UTF-8, and IEEE float32.
 * Calls are serialized by the owner render thread. Input data is copied before return.
 * Return values: 0 success, 1 suspended/outdated surface, negative failure.
 * Platform: 0 offscreen; 1 Win32 HWND; 2 externally-created surface (SDL3 callback).
 * The surface callback and extension names remain valid until sevk_destroy.
 */
typedef uint64_t (SEVK_CALL *SevkCreateSurface)(void* instance, void* window, void* user);
typedef struct SevkCreateInfo {
    uint32_t size, abi_version, platform, enable_validation;
    void* window;
    uint32_t width, height, vsync, adapter_index;
    const char* shader_directory;
    const char* cache_directory;
    const char* const* instance_extensions;
    /* reserved: Keen TextureSampler anisotropy. 0 and 1 stay trilinear.
     * 4, 8 and 16 match MyTextureAnisoFiltering. Sprites stay linear. */
    uint32_t instance_extension_count, reserved;
    SevkCreateSurface create_surface;
    void* surface_user;
    void* get_instance_proc_addr;
} SevkCreateInfo;
typedef struct SevkProbe {
    uint32_t size, abi_version, loader_api, device_api;
    uint32_t device_count, compatible_device_count, features, validation_available;
    /* features bits 0..5 and 10 (geometry), 11 (extended storage formats), 12 (independent blend), 13 (robust buffer access), 14 (depth clamp) required; bit6 host image copy, bit7 attachment local
     * read, bit8 depth/stencil local read, bit9 multisampled local read optional.
     * Availability does not imply enablement; optional paths remain disabled. */
    char device_name[256];
    char detail[1024];
} SevkProbe;
typedef struct SevkVertex { float position[3], normal[3], uv[2]; } SevkVertex;
/* Additive ABI-1 stock GBuffer extension. NORMAL0 is two packed integer words;
 * TANGENT0 contains the stock normalized packed bytes. */
typedef struct SevkGBufferVertex { float position[4]; uint32_t normal[2]; float tangent[4], uv[2]; } SevkGBufferVertex;
typedef struct SevkGBufferDraw { uint64_t mesh_id, color_id, normal_id, extensions_id, alpha_id; uint8_t object_constants[128]; } SevkGBufferDraw;
typedef struct SevkGBufferStats { uint32_t size, abi_version, width, height, ready, reserved[3]; uint64_t frames, draws; } SevkGBufferStats;
typedef struct SevkFrameInfo {
    float view_projection[16];
    float camera_position[4];
    float sunlight_direction[4], sunlight_color[4], ambient_color[4], clear_color[4];
} SevkFrameInfo;
typedef struct SevkDrawMesh {
    uint64_t mesh_id, texture_id, normal_texture_id, extensions_texture_id, alpha_texture_id;
    float world[16], tint[4], color_mask_hsv[4];
    float dither, emissivity;
    uint32_t flags, reserved; /* bits: 0 cutout,1 unlit,2 premultiplied blend,3 additive,
        4 metalness colorable,5..7 native map presence,8 skybox,9 no shadow,
        10 managed linear-tint marker,11 additive-top depth bypass */
} SevkDrawMesh;
typedef struct SevkLight {
    float position[3], range, color[3], intensity, direction[3], cone_cosine;
    uint32_t flags, reserved[3]; /* bit0 spot; bit1 stock attenuation metadata:
        reserved[0..2] float bits for transformed falloff, diffuse factor, gloss factor */
} SevkLight;
typedef struct SevkSprite {
    uint64_t texture_id;
    float destination[4], uv[4], color[4];
    float angle_radians, origin[2]; /* origin in local destination pixels */
    int32_t clip[4]; /* x,y,width,height; width<0 disables scissor */
    uint32_t flags; /* bit0 additive; bit1 premultiplied texture input;
                     * bit2 straight input converted to premultiplied output.
                     * Bits1/2 premultiply linear tint RGB by tint alpha, use ONE blending.
                     * Zero retains the straight-alpha ABI default. */
} SevkSprite;
/* DebugDrawLine3D vertex: camera-relative position and sRGB-encoded RGBA8 color (stock MyLinesRenderer). */
typedef struct SevkLineVertex { float position[3]; uint32_t color; } SevkLineVertex;
typedef struct SevkStats {
    uint32_t size, abi_version, api_version, validation_enabled;
    uint64_t submitted_frames, presented_frames, mesh_draws, sprite_draws;
    uint64_t uploaded_bytes, textures, meshes, validation_errors, completed_timeline;
    uint32_t width, height, surface_ready, device_lost;
    char device_name[256];
    char detail[512];
} SevkStats;
/* format: 0 RGBA8, 1 BGRA8, 2 BC1, 3 BC2, 4 BC3, 5 BC4, 6 BC5,
 * 7 BC6H unsigned, 8 BC7, 9 RG16F (two half-float channels). srgb applies to formats with an sRGB variant.
 * Blocks contain mip 0 only; exact byte count is checked. id 0 is reserved white.
 */
SEVK_API uint32_t SEVK_CALL sevk_api_version(void);
SEVK_API int32_t SEVK_CALL sevk_probe(SevkProbe* probe);
SEVK_API int32_t SEVK_CALL sevk_create(const SevkCreateInfo* info, void** context);
/* Replaces the material sampler. 0/1 trilinear, 4/8/16 anisotropic. Waits for the device. */
SEVK_API int32_t SEVK_CALL sevk_set_texture_anisotropy(void* context, uint32_t samples);
SEVK_API int32_t SEVK_CALL sevk_resize(void* context, uint32_t width, uint32_t height, uint32_t vsync);
/* Separates the physical presentation extent from the game's logical render/HUD extent. */
SEVK_API int32_t SEVK_CALL sevk_resize_output(void* context, uint32_t width, uint32_t height, uint32_t logical_width, uint32_t logical_height, uint32_t vsync);
/* Win32: monitor is an HMONITOR. enabled requests application-controlled exclusive
 * fullscreen for the next sevk_resize. The swapchain stays windowed until acquire
 * succeeds. acquired is 1 only while that exclusive mode is held. */
SEVK_API int32_t SEVK_CALL sevk_set_exclusive_fullscreen(void* context, uint32_t enabled, void* monitor);
SEVK_API int32_t SEVK_CALL sevk_get_exclusive_fullscreen(void* context, uint32_t* acquired);
SEVK_API int32_t SEVK_CALL sevk_upload_texture(void* context, uint64_t id, uint32_t width, uint32_t height, uint32_t format, uint32_t srgb, const void* data, uint64_t bytes);
/* Tightly packed levels from largest to smallest, with each dimension halved
 * to at least one. Existing sevk_upload_texture remains a one-level upload. */
SEVK_API int32_t SEVK_CALL sevk_upload_texture_mips(void* context, uint64_t id, uint32_t width, uint32_t height, uint32_t format, uint32_t srgb, const void* data, uint64_t bytes, uint32_t mip_count);
SEVK_API int32_t SEVK_CALL sevk_upload_mesh(void* context, uint64_t id, const SevkVertex* vertices, uint32_t vertex_count, const uint32_t* indices, uint32_t index_count);
/* Stock world lighting accepts up to 4096 points plus 64 spotlight identities.
   Spotlight indices must remain below 64 for redraw/exclusion masks. The scene
   owner applies stock's 32-spot and per-quality point budgets. Large batches
   require initialized stock world pipelines; preview constants retain 64 entries.
   The queued Frame's maxTileLights must cover the submitted point count. */
SEVK_API int32_t SEVK_CALL sevk_set_lights(void* context, const SevkLight* lights, uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_set_shadow_cascades(void* context, const float* matrices, uint32_t count, const float* splits);
/* Owner publishes fitted matrices plus a per-frame execution mask. Retained images
   keep their own matrices; this does not infer reuse from scene packet hashes. */
SEVK_API int32_t SEVK_CALL sevk_set_shadow_schedule(void* context,uint32_t enabled,uint32_t update_mask,uint32_t valid_mask);
/* Color/depth ranges share one allocation. Removing/replacing the source leaves
   registered ranges valid until each range's last GPU use has completed. */
SEVK_API int32_t SEVK_CALL sevk_register_world_mesh_range(void* context,uint64_t id,uint64_t source,uint32_t first_index,uint32_t index_count);
typedef struct SevkShadowFeedback {uint32_t size,abi_version;uint64_t completion;uint32_t pixels[8],update_mask,valid_mask;} SevkShadowFeedback;
SEVK_API int32_t SEVK_CALL sevk_init_shadow_usage(void* context,const void* data,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_get_shadow_feedback(void* context,SevkShadowFeedback* feedback);
/* CPU ownership state for queued world cascade contents. No GPU readback/wait.
   A missing bit requires the scheduler to prepare that cascade's depth view. */
SEVK_API int32_t SEVK_CALL sevk_get_shadow_cache_mask(void* context,uint32_t* mask);
SEVK_API int32_t SEVK_CALL sevk_set_shadow_pass_constants(void* context,const void* data,uint32_t bytes);
SEVK_API int32_t SEVK_CALL sevk_readback_shadow(void* context,uint32_t cascade,void* data,uint64_t bytes);
// Completed main-frame R8 inputs: 0 = screen AO, 1 = resolved sun visibility.
// Completed diagnostic input: 0 AO R8, 1 sun R8, 2/3 pre/post FXAA RGBA8,
// 4 raw GBuffer0 sRGB RGBA8 (including the LOD/pipeline-marker alpha).
SEVK_API int32_t SEVK_CALL sevk_readback_lighting_input(void* context,uint32_t input,void* data,uint64_t bytes);
// Optional diagnostics; disabled by default. Stage snapshots are RGBA32F:
// 0 after directional/ambient, 1 after points, 2 after spots; 3 current HDR.
// Rendering accumulates points, spots, then additive directional/ambient:
// stage 0 includes local lights; stages 1/2 exclude directional lighting.
// Stage 3 needs no opt-in. Stages 4..10 require the optional seven-output
// FP32 diagnostic shader installed by sevk_init_world_lighting(kind=5).
// Disabling waits and releases all snapshot/term images.
SEVK_API int32_t SEVK_CALL sevk_set_lighting_diagnostics(void* context,uint32_t enabled);
// Isolated term-pass control only: 0 actual stock sampler (default),
// 1 anisotropy enabled with maxAnisotropy=1. Main lighting is unaffected.
// Disabling diagnostics releases this sampler and resets the mode to zero.
SEVK_API int32_t SEVK_CALL sevk_set_lighting_diagnostic_sampler(void* context,uint32_t mode);
SEVK_API int32_t SEVK_CALL sevk_readback_lighting_stage(void* context,uint32_t stage,void* data,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_render_texture(void* context, uint64_t id, uint32_t width, uint32_t height, const float* clear_color, const SevkSprite* sprites, uint32_t count);
/* Physical attachment size remains width/height; sprite coordinates and clips
 * use the logical viewport (game texture size multiplied by AspectRatio). */
SEVK_API int32_t SEVK_CALL sevk_render_texture_aspect(void* context, uint64_t id, uint32_t width, uint32_t height, const float* clear_color, const SevkSprite* sprites, uint32_t count, float logical_width, float logical_height);
SEVK_API int32_t SEVK_CALL sevk_render_texture_mips(void* context, uint64_t id, uint32_t width, uint32_t height, const float* clear_color, const SevkSprite* sprites, uint32_t count, float logical_width, float logical_height, uint32_t generate_mips);
SEVK_API int32_t SEVK_CALL sevk_begin_frame(void* context, const SevkFrameInfo* frame);
SEVK_API int32_t SEVK_CALL sevk_draw_mesh(void* context, const SevkDrawMesh* draw);
SEVK_API int32_t SEVK_CALL sevk_draw_sprites(void* context, const SevkSprite* sprites, uint32_t count);
/* Line list drawn after the game scene, before sprites. flags bit0: test against scene depth (DepthRead). */
SEVK_API int32_t SEVK_CALL sevk_draw_lines(void* context, const SevkLineVertex* vertices, uint32_t count, uint32_t flags);
SEVK_API int32_t SEVK_CALL sevk_end_frame(void* context);
/* Starts end_frame on a native worker. Later calls wait for it and replay
 * whitelisted void calls (queued with copied arguments) in issue order. */
SEVK_API int32_t SEVK_CALL sevk_end_frame_async(void* context);
SEVK_API int32_t SEVK_CALL sevk_remove_resource(void* context, uint32_t kind, uint64_t id); /* 0 texture,1 mesh,2 stock GBuffer mesh */
SEVK_API int32_t SEVK_CALL sevk_get_stats(void* context, SevkStats* stats);
SEVK_API int32_t SEVK_CALL sevk_request_readback(void* context);
SEVK_API int32_t SEVK_CALL sevk_readback(void* context, void* rgba8, uint64_t bytes);
SEVK_API void SEVK_CALL sevk_destroy(void* context);
SEVK_API const char* SEVK_CALL sevk_last_error(void* context);
/* Independent stock pass, between main frames. Inputs are consumed before
 * return. Diagnostic readback may stall. Attachments: 0 RGBA8 sRGB,
 * 1 A2B10G10R10 UNORM, 2 RGBA8 UNORM, 3 D32 float. Lighting/composition
 * and asynchronous production scheduling remain separate. */
SEVK_API int32_t SEVK_CALL sevk_init_gbuffer(void* context, const void* vertex_spirv, uint64_t vertex_bytes, const void* pixel_spirv, uint64_t pixel_bytes);
SEVK_API int32_t SEVK_CALL sevk_upload_gbuffer_mesh(void* context, uint64_t id, const SevkGBufferVertex* vertices, uint32_t vertex_count, const uint32_t* indices, uint32_t index_count);
SEVK_API int32_t SEVK_CALL sevk_render_gbuffer(void* context, uint32_t width, uint32_t height, const void* frame_constants, uint32_t frame_bytes, const float* projection, const SevkGBufferDraw* draws, uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_readback_gbuffer(void* context, uint32_t attachment, void* pixels, uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_get_gbuffer_stats(void* context, SevkGBufferStats* stats);
typedef struct SevkWorkStats {
    uint32_t size,abi_version;
    uint64_t upload_batches,upload_slot_waits,world_frames,world_draws,world_instances,skinned_draws,cube_draws,lighting_passes;
    uint32_t spot_us,shadow_us,main_us,probe_us,prepare_us,tail_us,wait_us,acquire_us,reset_us;
} SevkWorkStats;
SEVK_API int32_t SEVK_CALL sevk_begin_uploads(void* context);
SEVK_API int32_t SEVK_CALL sevk_end_uploads(void* context);
SEVK_API int32_t SEVK_CALL sevk_get_work_stats(void* context, SevkWorkStats* stats);
/* Independent extension: the original SevkWorkStats layout remains compatible. */
typedef struct SevkPerformanceStats {
    uint32_t size,abi_version,compose_us,sprite_us,finish_us,submit_us,present_us;
    uint32_t material_hits,material_misses,material_resets,main_commands,shadow_commands,spot_commands,probe_commands,logical_draws,submitted_draws;
    uint32_t queue_us, geometry_us, upload_us, descriptors_us, sort_us;
} SevkPerformanceStats;
SEVK_API int32_t SEVK_CALL sevk_get_performance_stats(void* context, SevkPerformanceStats* stats);
/* Read-only actual created presentation state. mode is UINT32_MAX offscreen. */
typedef struct SevkPresentationStats {
    uint32_t size,abi_version,swapchain,surface_ready,vsync,mode,images,generation;
} SevkPresentationStats;
SEVK_API int32_t SEVK_CALL sevk_get_presentation_stats(void* context, SevkPresentationStats* stats);
typedef struct SevkPresentFlowStats {
    uint32_t size,abi_version,enabled,pending;
    uint64_t completed_calls,last_completed_ns,total_interval_ns,total_call_ns;
    uint32_t last_call_us,last_join_us,result,reserved;
} SevkPresentFlowStats;
SEVK_API int32_t SEVK_CALL sevk_set_async_present(void* context,uint32_t enabled);
SEVK_API int32_t SEVK_CALL sevk_get_present_flow_stats(void* context,SevkPresentFlowStats* stats);
/* Benchmark-only completion samples, monotonically sequenced; no wait for pending present.
 * oldest reports retention loss. Timestamps describe queue-present call completion, not scanout. */
typedef struct SevkPresentSample {uint64_t sequence,end_ns,interval_ns,call_ns;} SevkPresentSample;
SEVK_API int32_t SEVK_CALL sevk_get_present_samples(void* context,uint64_t after,SevkPresentSample* samples,uint32_t capacity,uint32_t* count,uint64_t* oldest);
typedef struct SevkQueueValidationStats {
    uint32_t size,abi_version,enabled,draws,hits,texture_checks,voxel_checks,mesh_checks;
} SevkQueueValidationStats;
SEVK_API int32_t SEVK_CALL sevk_set_texture_validation_cache(void* context, uint32_t enabled);
SEVK_API int32_t SEVK_CALL sevk_get_queue_validation_stats(void* context, SevkQueueValidationStats* stats);
typedef struct SevkIndirectStats {
    uint32_t size,abi_version,supported,enabled,main_groups,shadow_groups,grouped_draws,prepared_commands;
} SevkIndirectStats;
SEVK_API int32_t SEVK_CALL sevk_set_world_indirect(void* context, uint32_t enabled);
/* Stock main lighting precision: 0 R11G11B10, 1 RGBA16F (HqTarget).
 * Changes require a closed frame/upload batch. HDR readback remains RGBA16F. */
SEVK_API int32_t SEVK_CALL sevk_set_hdr_quality(void* context, uint32_t high_quality);
typedef struct SevkObjectRecordStats { uint32_t size,abi_version,enabled,records,selected,changed;uint64_t live_bytes,capacity_bytes,uploaded_bytes; } SevkObjectRecordStats;
SEVK_API int32_t SEVK_CALL sevk_get_object_record_stats(void* context,SevkObjectRecordStats* stats);
typedef struct SevkCascadeRecordStats {uint32_t size,abi_version,enabled,tasks,commands,reserved[3];} SevkCascadeRecordStats;
SEVK_API int32_t SEVK_CALL sevk_set_parallel_cascades(void* context,uint32_t enabled);
SEVK_API int32_t SEVK_CALL sevk_get_cascade_record_stats(void* context,SevkCascadeRecordStats* stats);
typedef struct SevkLightTileStats {uint32_t size,abi_version,enabled,dispatches,tiles,points,have_result,readback_us;uint64_t frame,completed_frame,assignments,full_assignments;} SevkLightTileStats;
SEVK_API int32_t SEVK_CALL sevk_init_light_tiles(void* context,const void* spirv,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_set_light_tiles(void* context,uint32_t enabled);
SEVK_API int32_t SEVK_CALL sevk_get_light_tile_stats(void* context,SevkLightTileStats* stats);
SEVK_API int32_t SEVK_CALL sevk_get_indirect_stats(void* context, SevkIndirectStats* stats);
typedef struct SevkGeometryArenaStats {
    uint32_t size,abi_version,enabled,vertex_pages,index_pages,reserved[3];
    uint64_t vertex_live,index_live,vertex_capacity,index_capacity;
} SevkGeometryArenaStats;
SEVK_API int32_t SEVK_CALL sevk_set_geometry_arena(void* context, uint32_t enabled);
SEVK_API int32_t SEVK_CALL sevk_get_geometry_arena_stats(void* context, SevkGeometryArenaStats* stats);
typedef struct SevkWorldVertex { SevkGBufferVertex material; float weights[4]; uint32_t indices[4]; } SevkWorldVertex;
/* Pipeline ids: rigid/skinned/generic/cube/deformed-cube 0..4;
 * +5 alpha masked, +10 metalness colorable, +32 depth-only partner.
 * Generic stream: transposed affine rows[3], HSV/dither. Cube stream:
 * eight packed byte4 bones, translation/packed rotation, HSV/dither. */
typedef struct SevkWorldDraw {
    uint32_t size,pipeline,instance_count,flags; /*0x02000000 excludes spotlight depth; 0x04000000 identifies OLD geometry in stock main-pass ordering; 0x08000000 stock back-cull / 0x10000000 stock no-cull (mutually exclusive); 0x20000000 OLD depth no-cull override; zero retains raw legacy raster defaults; cascade mask remains independent.*/
    uint64_t mesh,color,normal,extensions,alpha;
    const void* object_constants; uint32_t object_bytes,reserved; /* reserved: cascade bits, bit c draws cascade c */
    const void* instances;
    float origin[3], radius; /* camera-relative center; radius < 0 disables spotlight distance culling */
} SevkWorldDraw;
SEVK_API int32_t SEVK_CALL sevk_init_world_pipeline(void* context,uint32_t pipeline,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes);
/* Kind 0 rigid, 1 generic instance, 2 cube, 3 deformed cube. Writes RG16F object motion. */
SEVK_API int32_t SEVK_CALL sevk_init_object_motion(void* context,uint32_t kind,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes);
SEVK_API int32_t SEVK_CALL sevk_init_indexed_attachment(void* context,uint32_t slot,uint32_t format,uint32_t kind,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes);
SEVK_API int32_t SEVK_CALL sevk_init_indexed_motion(void* context,uint32_t kind,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes);
/* Extra GBuffer color target. Slot 0..2 is SV_Target4..6. Format 0 RGBA8, 1 RGBA16F,
 * 2 R32F, 3 RG16F, 4 R32_UINT, 5 RG32F. Kind matches object motion. The pixel shader
 * writes SV_Target0 and declares no descriptors. Stock color targets stay three. */
/* Declares a pack attachment slot's format for owner-emitted SV_Target4..6 without a replay write shader. */
SEVK_API int32_t SEVK_CALL sevk_declare_gbuffer_attachment(void* context,uint32_t slot,uint32_t format);
SEVK_API int32_t SEVK_CALL sevk_init_gbuffer_attachment(void* context,uint32_t slot,uint32_t format,uint32_t kind,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes);
/* Lighting kind 0 fullscreen VS, 1 LightDir, 2 LightPoint, 3 LightSpot. */
SEVK_API int32_t SEVK_CALL sevk_init_world_lighting(void* context,uint32_t kind,const void* spirv,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_upload_world_mesh(void* context,uint64_t id,const SevkWorldVertex* vertices,uint32_t vertex_count,const uint32_t* indices,uint32_t index_count);
SEVK_API int32_t SEVK_CALL sevk_queue_world(void* context,const void* frame,uint32_t frame_bytes,const float* projection,const float* inverse_view_projection,const SevkWorldDraw* draws,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_queue_world_retained(void* context,const void* frame,uint32_t frame_bytes,const float* projection,const float* inverse_view_projection,const SevkWorldDraw* draws,uint32_t count,const uint64_t* object_ids);
/* Blob draws encode byte offset + 1 in pointer fields (zero means absent).
   Constants occupy consecutive device-aligned slices, instances use 64-byte
   alignment. The call copies all bytes before returning and never retains
   caller addresses. All legacy layout/resource validation still applies. */
SEVK_API int32_t SEVK_CALL sevk_world_object_alignment(void* context,uint32_t* alignment);
SEVK_API int32_t SEVK_CALL sevk_queue_world_blob(void* context,const void* frame,uint32_t frame_bytes,const float* projection,const float* inverse_view_projection,const SevkWorldDraw* draws,uint32_t count,const uint8_t* objects,uint32_t object_bytes,const uint8_t* instances,uint32_t instance_bytes);
typedef struct SevkWorldRecipe {uint32_t size,pipeline,object_bytes,reserved;uint64_t mesh,color,normal,extensions,alpha;} SevkWorldRecipe;
typedef struct SevkRecipeDraw {uint64_t recipe;uint32_t instance_count,flags,cascade_mask,object_offset,instance_offset,reserved;float origin[3],radius;} SevkRecipeDraw;
typedef struct SevkRecipeStats {uint32_t size,abi_version,enabled,draws,registered,revalidated,materials_reused,geometry_reused,live,retired;} SevkRecipeStats;
typedef struct SevkModelPart {uint64_t recipe;uint32_t object_offset,reserved;} SevkModelPart;
typedef struct SevkModelInstance {
    uint64_t model;
    uint32_t order,flags,cascade_mask,cell;
    int32_t x,y,z;
    float origin[3],radius;
    uint32_t reserved; /* Optional first-seen main-group order, <=order; zero uses order. */
    float instance[16];
} SevkModelInstance;
typedef struct SevkModelGroupStats {
    uint32_t size,abi_version,enabled,instances,groups,packets,templates,legacy_draws;
    uint64_t object_bytes,instance_bytes;
} SevkModelGroupStats;
SEVK_API int32_t SEVK_CALL sevk_register_model(void* ctx,const SevkModelPart* parts,uint32_t count,const uint8_t* objects,uint32_t object_bytes,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_register_model_ordered(void* ctx,const SevkModelPart* parts,const SevkOldPartOrder* orders,uint32_t count,const uint8_t* objects,uint32_t object_bytes,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_clear_models(void* ctx);
SEVK_API int32_t SEVK_CALL sevk_append_models(void* ctx,const SevkModelInstance* instances,uint32_t count,const uint32_t* legacy_order,uint32_t legacy_count,uint32_t* legacy_remap);
/* Optional ordering metadata for the queued legacy draws, before append_models.
 * group is its first logical submission order; part is its preprocessed ordinal.
 * Geometry draw IDs, object offsets and public draw layouts remain unchanged. */
typedef struct SevkMainDrawOrder {uint32_t group,part;} SevkMainDrawOrder;
SEVK_API int32_t SEVK_CALL sevk_world_main_order(void* ctx,const SevkMainDrawOrder* orders,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_world_old_order(void* ctx,const SevkOldPartOrder* orders,uint32_t count);
/* Logical owner per queued draw; equal stock OLD keys order by owner, then submission. */
SEVK_API int32_t SEVK_CALL sevk_world_draw_owners(void* ctx,const uint32_t* owners,uint32_t count);
typedef struct SevkMainOwnerOrder {uint32_t owner,group;} SevkMainOwnerOrder;
SEVK_API int32_t SEVK_CALL sevk_world_main_owners(void* ctx,const SevkMainOwnerOrder* owners,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_get_model_group_stats(void* ctx,SevkModelGroupStats* stats);
SEVK_API int32_t SEVK_CALL sevk_register_world_recipe(void* context,const SevkWorldRecipe* recipe,uint64_t* handle);
SEVK_API int32_t SEVK_CALL sevk_queue_world_recipes(void* context,const void* frame,uint32_t frame_bytes,const float* projection,const float* inverse,const SevkRecipeDraw* draws,uint32_t count,const uint8_t* objects,uint32_t object_bytes,const uint8_t* instances,uint32_t instance_bytes);
SEVK_API int32_t SEVK_CALL sevk_get_recipe_stats(void* context,SevkRecipeStats* stats);
typedef struct SevkQueueWorkStats {uint32_t size,abi_version,enabled,workers,draws,validation_us,copy_us,reserved;} SevkQueueWorkStats;
SEVK_API int32_t SEVK_CALL sevk_set_parallel_world_queue(void* context,uint32_t enabled);
/* Off-critical-path native work: async pass packing and deferred OLD cell emission. Output is unchanged. */
SEVK_API int32_t SEVK_CALL sevk_set_async_policy(void* context,uint32_t packing,uint32_t cells);
SEVK_API int32_t SEVK_CALL sevk_get_queue_work_stats(void* context,SevkQueueWorkStats* stats);
SEVK_API int32_t SEVK_CALL sevk_upload_world_cube(void* context,uint64_t id,uint32_t width,uint32_t height,uint32_t format,uint32_t srgb,const void* bytes,uint64_t byte_count);
SEVK_API int32_t SEVK_CALL sevk_upload_world_cube_mips(void* context,uint64_t id,uint32_t width,uint32_t height,uint32_t format,uint32_t srgb,const void* bytes,uint64_t byte_count,uint32_t mip_count);
SEVK_API int32_t SEVK_CALL sevk_set_world_skybox(void* context,uint64_t id);
SEVK_API int32_t SEVK_CALL sevk_set_world_skyboxes(void* context,uint64_t visible,uint64_t indirect);
typedef struct SevkVoxelStats { uint32_t size,abi_version;uint64_t frames,draws,meshes,arrays,materials; } SevkVoxelStats;
SEVK_API int32_t SEVK_CALL sevk_get_voxel_stats(void* ctx,SevkVoxelStats* stats);
SEVK_API int32_t SEVK_CALL sevk_build_voxel_array(void* ctx,uint64_t id,uint32_t size,uint32_t srgb,const uint64_t* source_textures,uint32_t layers);
/* Voxel vertex is the stock 24-byte packed VRage format. Arrays contain
 * each layer's tightly packed mip chain. Kind 5 removes arrays, kind 6 material groups. */
SEVK_API int32_t SEVK_CALL sevk_upload_voxel_mesh(void* ctx,uint64_t id,const void* vertices,uint32_t vertex_count,const uint32_t* indices,uint32_t index_count);
SEVK_API int32_t SEVK_CALL sevk_upload_voxel_array(void* ctx,uint64_t id,uint32_t width,uint32_t height,uint32_t format,uint32_t srgb,uint32_t layers,uint32_t mips,const void* data,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_set_voxel_material(void* ctx,uint64_t id,const void* constants,uint32_t bytes,const uint64_t* arrays,uint64_t random_texture);
/* Copied, additive ABI-1 environment packets. Kind 0 atmosphere; 1 clouds.
 * Constants are the reflected 144-byte stock buffers. IDs are render IDs.
 * Stage 0 atmosphere CS, 1 atmosphere PS, 2 clouds VS, 3 clouds PS. */
typedef struct SevkEnvironmentDraw {
    uint32_t size,kind,flags,reserved;
    uint64_t id,mesh,color,alpha;
    uint8_t constants[144];
} SevkEnvironmentDraw;
typedef struct SevkEnvironmentStats {
    uint32_t size,abi_version;
    uint64_t frames,atmosphere_draws,cloud_draws,lut_dispatches,luts,foliage_draws,foliage_points;
} SevkEnvironmentStats;
SEVK_API int32_t SEVK_CALL sevk_init_environment(void* ctx,uint32_t stage,const void* spirv,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_queue_environment(void* ctx,const SevkEnvironmentDraw* draws,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_get_environment_stats(void* ctx,SevkEnvironmentStats* stats);
typedef struct SevkFoliagePoint { float position[3]; uint32_t normal_seed_material; } SevkFoliagePoint;
typedef struct SevkFoliageDraw { uint32_t size,kind,reserved[2];uint64_t mesh,color,normal;uint8_t object_constants[128]; } SevkFoliageDraw;
SEVK_API int32_t SEVK_CALL sevk_upload_foliage(void* ctx,uint64_t id,const SevkFoliagePoint* points,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_queue_foliage(void* ctx,const SevkFoliageDraw* draws,uint32_t count,const void* materials,uint32_t bytes);
/* ABI-1 copied glass packets. Pipeline 0..4 glass rigid/skinned/generic/cube/deformed; 5..9 stock Holo partners.
 * Material is the reflected 96-byte TransparentConstants. Objects/instances are copied before return. */
/* Glass geometry.flags: bit0 NEW visible back-cull (ignored for OLD), bit1 OLD source pipeline,
   bits2/3 OLD far/near depth membership, bits4/5 OLD far/near pass requested.
   No OLD bit means NEW geometric layers. Decal depth always back-culls. */
typedef struct SevkTransparentDraw { SevkWorldDraw geometry;uint8_t material[96]; } SevkTransparentDraw;
typedef struct SevkTransparencyStats {uint32_t size,abi_version;uint64_t frames,glass_draws,instances,resolves,billboard_draws,decal_draws,glass_layer_draws;} SevkTransparencyStats;
SEVK_API int32_t SEVK_CALL sevk_init_transparency(void* ctx,uint32_t id,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes);
SEVK_API int32_t SEVK_CALL sevk_queue_transparency(void* ctx,const SevkTransparentDraw* draws,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_get_transparency_stats(void* ctx,SevkTransparencyStats* stats);
/* Four tightly packed float3/float2 vertices, then the stock 48-byte BillboardData.
 * pipeline flags: bit0 lit, bit1 soft, bit2 cutout, bit3 additive, bit4 single-channel; bucket 0 bottom,1 standard,2 top. */
typedef struct SevkBillboardDraw {uint64_t texture;uint32_t flags,bucket;float vertices[20];uint8_t data[48];} SevkBillboardDraw;
SEVK_API int32_t SEVK_CALL sevk_set_billboard_projections(void* context, const float* matrices);
SEVK_API int32_t SEVK_CALL sevk_queue_billboards(void* ctx,const SevkBillboardDraw* draws,uint32_t count);
/* Emitters are stock 464-byte layout; no pointer survives queue return.
 * reset=1 discards particles at scene unload; array references an uploaded texture2DArray. */
typedef struct SevkParticleStats {uint32_t size,abi_version;uint64_t frames,dispatches,draws;uint32_t alive,dead,indices,reserved;} SevkParticleStats;
SEVK_API int32_t SEVK_CALL sevk_init_particles(void* ctx,uint32_t stage,const void* spirv,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_queue_particles(void* ctx,const void* emitters,uint32_t count,uint32_t bytes,uint64_t texture_array,uint32_t reset);
SEVK_API int32_t SEVK_CALL sevk_get_particle_stats(void* ctx,SevkParticleStats* stats);
/* Feature bit28: bounded diagnostic reads of completed native particle buffers.
 * kind0=data,1=dead,2..3=alive,4..7=counters,8=draw args,9=sim args,10=active. */
typedef struct SevkParticleReadback {uint32_t size,abi_version,kind,reserved;uint64_t offset,bytes;void* data;} SevkParticleReadback;
SEVK_API int32_t SEVK_CALL sevk_readback_particle_buffer(void* ctx,const SevkParticleReadback* request);
typedef struct SevkDecalDraw {uint32_t size,role,reserved[2];uint64_t alpha,color,normal,extensions;uint8_t constants[128];} SevkDecalDraw;
SEVK_API int32_t SEVK_CALL sevk_init_decals(void* ctx,uint32_t role,const void* vertex,uint64_t vertex_bytes,const void* pixel,uint64_t pixel_bytes);
SEVK_API int32_t SEVK_CALL sevk_queue_decals(void* ctx,const SevkDecalDraw* draws,uint32_t count);
/* Camera-relative positions, radius in physical pixels; copied before return.
 * Results only contain completed frame slots; this call never waits for GPU work. */
typedef struct SevkFlareQuery {uint64_t id;float position[3],radius;} SevkFlareQuery;
/* NDC center/depth, positive NDC half extents and stock normalization area.
   Pixel normalization is area * width * height / 4. */
typedef struct SevkFlareRasterQuery {uint64_t id;float center[3],half_extent[2],area;} SevkFlareRasterQuery;
typedef struct SevkFlareResult {uint64_t id;float occlusion;uint32_t reserved;} SevkFlareResult;
SEVK_API int32_t SEVK_CALL sevk_queue_flares(void* ctx,const float* view_projection,const SevkFlareQuery* queries,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_queue_flare_raster(void* ctx,const SevkFlareRasterQuery* queries,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_get_flare_results(void* ctx,SevkFlareResult* results,uint32_t capacity,uint32_t* count);
/* Copied postprocess configuration, no managed storage survives this call.
 * flags: enabled, eye adaptation, bloom, HQ bloom, anti-flicker, FXAA,
 * SSAO, SSAO blur, chromatic/vignette, reset exposure (bits 0..9). */
typedef struct SevkPostprocessConfig {uint32_t size,abi_version,flags,bloom_size,chromatic_iterations,reserved;uint64_t dirt;uint8_t histogram[64],ssao[288];} SevkPostprocessConfig;
typedef struct SevkPostprocessStats {uint32_t size,abi_version;uint64_t frames,exposure_passes,histogram_dispatches,bloom_passes,tone_dispatches,fxaa_passes,ssao_passes,chromatic_dispatches,hbao_passes,shadow_dispatches; } SevkPostprocessStats;
SEVK_API int32_t SEVK_CALL sevk_set_shadow_options(void* context, uint32_t flags);
SEVK_API int32_t SEVK_CALL sevk_init_postprocess(void* ctx,uint32_t stage,const void* spirv,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_set_postprocess(void* ctx,const SevkPostprocessConfig* config);
SEVK_API int32_t SEVK_CALL sevk_get_postprocess_stats(void* ctx,SevkPostprocessStats* stats);
/* HBAO uses reflected 200-byte global data and sixteen immutable jitter slices.
 * flags enabled/blur/clamp; coarse permutation background/foreground/threshold/GBuffer. */
typedef struct SevkHbaoConfig {uint32_t size,abi_version,flags,coarse,blur,reserved;uint8_t constants[200];} SevkHbaoConfig;
SEVK_API int32_t SEVK_CALL sevk_set_hbao(void* ctx,const SevkHbaoConfig* config);
/* Sixteen stock MyHBAO per-pass jitter float4 values, copied before mutation. */
SEVK_API int32_t SEVK_CALL sevk_set_hbao_jitter(void* ctx,const float* values,uint32_t count);
typedef struct SevkProbeConfig {uint32_t size,abi_version,resolution,flags;uint64_t brdf;float blend;uint32_t reserved;uint8_t frames[7680];float projections[96];} SevkProbeConfig;
typedef struct SevkProbeStats {uint32_t size,abi_version;uint64_t faces,geometry_draws,prefilter_passes,blend_passes,cycles,atmosphere_passes;uint32_t ready,next_face;} SevkProbeStats;
typedef struct SevkProbeScheduleStats {uint32_t size,abi_version,stage,busy;uint64_t cycles,reserved;} SevkProbeScheduleStats;
SEVK_API int32_t SEVK_CALL sevk_init_probes(void* ctx,uint32_t stage,const void* spirv,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_set_probes(void* ctx,const SevkProbeConfig* config);
SEVK_API int32_t SEVK_CALL sevk_set_probes_prefilter(void* ctx,const SevkProbeConfig* config,uint32_t skip_levels);
SEVK_API int32_t SEVK_CALL sevk_get_probe_stats(void* ctx,SevkProbeStats* stats);
SEVK_API int32_t SEVK_CALL sevk_get_probe_schedule_stats(void* ctx,SevkProbeScheduleStats* stats);
/* Explicit verification capture: waits for completion, returns RGBA16F bytes.
 * kind 0/1 original close/far, 2/3 filtered, 4/5 blended final. */
SEVK_API int32_t SEVK_CALL sevk_readback_probe(void* ctx,uint32_t kind,uint32_t face,uint32_t mip,void* pixels,uint64_t capacity);
SEVK_API int32_t SEVK_CALL sevk_init_stock_shadows(void* ctx,const void* spirv,uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_set_probe_atmosphere(void* ctx,uint64_t id,float intensity,float desaturation,const float* sun_direction);
SEVK_API int32_t SEVK_CALL sevk_set_shadow_quality(void* ctx,uint32_t resolution,float bias);
typedef struct SevkOcclusionDraw {uint64_t id;float minimum[3],extents[3];uint32_t flags,reserved;} SevkOcclusionDraw;
typedef struct SevkOcclusionResult {uint64_t id;uint32_t visible,reserved;} SevkOcclusionResult;
typedef struct SevkOcclusionStats {uint32_t size,abi_version;uint64_t frames,queries,results,occluded,token;} SevkOcclusionStats;
SEVK_API int32_t SEVK_CALL sevk_init_occlusion(void* ctx,const void* vs,uint64_t vs_bytes,const void* ps,uint64_t ps_bytes);
SEVK_API int32_t SEVK_CALL sevk_queue_occlusion(void* ctx,uint64_t token,const SevkOcclusionDraw* queries,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_queue_shadow_occlusion(void* context,uint32_t cascade,uint64_t token,const SevkOcclusionDraw* queries,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_get_shadow_occlusion(void* context,uint32_t cascade,SevkOcclusionResult* results,uint32_t capacity,uint32_t* count,uint64_t* token);
SEVK_API int32_t SEVK_CALL sevk_get_occlusion(void* ctx,SevkOcclusionResult* results,uint32_t capacity,uint32_t* count,uint64_t* token);
SEVK_API int32_t SEVK_CALL sevk_get_occlusion_stats(void* ctx,SevkOcclusionStats* stats);
typedef struct SevkShadowExclusion {uint32_t light,draw;} SevkShadowExclusion;
SEVK_API int32_t SEVK_CALL sevk_set_shadow_exclusions(void* ctx,const SevkShadowExclusion* exclusions,uint32_t count);
typedef struct SevkSpot {uint32_t light,flags;uint64_t cookie;float projection[16];} SevkSpot;
typedef struct SevkSpotStats {uint32_t size,abi_version;uint64_t frames,maps,draws,cookies;} SevkSpotStats;
SEVK_API int32_t SEVK_CALL sevk_set_spots(void* ctx,uint32_t resolution,const SevkSpot* spots,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_set_spot_redraw(void* ctx,uint64_t static_mask,uint64_t dynamic_mask);
SEVK_API int32_t SEVK_CALL sevk_get_spot_stats(void* ctx,SevkSpotStats* stats);
typedef struct SevkHighlightDraw {uint32_t draw,first_index,index_count,first_instance,instance_count,flags;float color[4],thickness;uint32_t reserved;} SevkHighlightDraw;
typedef struct SevkHighlightStats {uint32_t size,abi_version;uint64_t frames,draws,blur_passes,compositions;} SevkHighlightStats;
SEVK_API int32_t SEVK_CALL sevk_queue_highlights(void* ctx,const SevkHighlightDraw* draws,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_get_highlight_stats(void* ctx,SevkHighlightStats* stats);
/* World resolution is independent of physical presentation/UI resolution.
 * Completed GPU timestamps are read without waiting by this status API. */
typedef struct SevkResolutionStats {uint32_t size,abi_version,width,height,output_width,output_height,timing_supported,reserved;uint64_t samples;float fraction,gpu_ms;} SevkResolutionStats;
SEVK_API int32_t SEVK_CALL sevk_set_render_fraction(void* ctx,float fraction);
/* Resolve temporal-upscaler overrides before building frame/postprocess constants.
 * width/height are logical world output dimensions, independent of the swapchain. */
SEVK_API int32_t SEVK_CALL sevk_resolve_render_resolution(void* ctx,uint32_t width,uint32_t height,uint32_t* render_width,uint32_t* render_height,float* fraction);
SEVK_API int32_t SEVK_CALL sevk_get_resolution_stats(void* ctx,SevkResolutionStats* stats);
/* Anomaly guest bus. ABI 1 additive. Empty pass lists do not record work.
 * Marks: 1 AfterLighting, 2 AfterAtmosphere, 3 AfterTransparent,
 * 4 BeforeTonemap, 5 AfterTonemap, 6 AfterUpscale.
 * sizeof(SevkGuestPass) is 416 with natural alignment.
 * reserved bit0 depth-tests, bit1 dispatches compute, bit2 volume constants,
 * bit3 copies sampled[0] into storage and generates its mip chain (no shader draw).
 * bit4 clears compute storage to zero before dispatch, bit5 supplies b0 from
 * uniforms instead of generated frame constants (up to 256 bytes). Compute
 * width/height are invocation counts; workgroup size comes from SPIR-V.
 * bit6 draws copied triangles with MIN distance blending (geometry_vertex
 * entry), bit7 applies a copied viewport, bit8 clears color to 100000 metres.
 * Geometry/viewport constants: matrices 0..127, float4 x/y/w/h at 128,
 * uint vertex count at 144. Guest image format 6 is RGBA32F. Readback returns
 * owned images' native texel bytes (2/4/8/16 bytes), without color conversion.
 * The HDR sentinel always expands to RGBA16F, including normal-quality R11G11B10.
 * Feature bit21: reserved flag 4096 queues an Anomaly-owned isolated mark-1
 * fullscreen producer after opaque GBuffer/decal writes, before AO/lighting.
 * It requires compose=5, an owned offscreen image and no other packet flags.
 * Public replay marks and callback phases are unchanged.
 * Query sevk_guest_features before using additive packet flags.
 * Sampled sentinels: HDR ..1, depth ..2, GBuffer0..2 ..3..5, upscale ..6, display ..7,
 * motion ..8, attachment0 ..9. Catalog velocity is ..A, volumeReactive is ..B, reactiveMask is ..C. */
typedef struct SevkGuestPass {
    uint32_t size, abi_version, mark, compose, width, height;
    uint64_t pipeline, storage;
    uint32_t uniform_bytes, reserved;
    uint8_t uniforms[256];
    uint64_t sampled[14];
} SevkGuestPass;
/* Extended guest resources, gated by sevk_guest_features bit9. ABI-1 packets
 * are unchanged. contract=1 enables t0..47, b0..7 (<=4096 bytes each), s0..5,
 * compute u0..4 and 3D invocation counts; contract=2 is two RGBA16F MRTs.
 * s5 is a clamp comparison sampler (LESS_OR_EQUAL). Constants are copied by
 * submit; caller pointers need only survive the call. Outputs cannot alias
 * sampled inputs or one another. flags currently permits bit4 (zero clear).
 * sizeof(SevkGuestPass2)=568 with natural alignment on 64-bit hosts. */
/* Image kind 7 is sampled D32 depth (2D, single mip); kind 8 is R16F.
 * Extended storage-image formats and dimensions must match SPIR-V exactly.
 * Base-level readback includes all depth slices/array layers. */
typedef struct SevkGuestPass2 {
    uint32_t size, abi_version, mark, compose, width, height, depth, flags;
    uint64_t pipeline;
    uint64_t sampled[48], storage[5];
    const void* constants[8];
    uint32_t constant_bytes[8];
    uint32_t reserved[2];
} SevkGuestPass2;
SEVK_API int32_t SEVK_CALL sevk_guest_create_pipeline2(void* ctx, const void* vs, uint64_t vs_bytes, const char* vs_entry, const void* ps, uint64_t ps_bytes, const char* ps_entry, uint32_t contract, uint64_t* pipeline);
SEVK_API int32_t SEVK_CALL sevk_guest_submit2(void* ctx, const SevkGuestPass2* passes, uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_guest_reset(void* ctx);
/* Feature bit23: classic fullscreen true-null sampled/structured/typed SRVs.
 * Packet ABI is unchanged. Resource pipelines require device nullDescriptor;
 * SevkStageBindingState.flags bit0 reports that device capability. Extended
 * contracts retain required-input validation. */
/* Feature bit24 extends the native generation transaction to atmosphere
 * compute/main-view/probe modules 512..514. Feature bit25 adds foliage
 * modules 768..772 as a required shared-VS/two-GS/two-PS closure.
 * Feature bit26 adds decal VS/PS pairs 1024..1033; each pair is indivisible.
 * Feature bit31 adds fixed-layout stock world geometry VS/PS pairs
 * 4096+2*pipeline and4097+2*pipeline; generation module limit8192.
 * Native-cell/GPU-owned owners and geometry reserved resources remain separate.
 * Feature bit30 adds native OIT resolve owner2048 with both HDR formats.
 * Feature bit29 adds screen-space shadow/usage compute owners1792..1793.
 * Feature bit28 adds particle owner closure 1536..1545, including stage-local
 * VS bindings and completed indirect participation.
 * Feature bit27 adds probe processing owners 1280..1283. The stock dormant
 * factor-blend owner retains zero execution until a real invocation exists.
 * Existing ABI-1 module/state packets retain their size and version checks. */
SEVK_API uint32_t SEVK_CALL sevk_guest_features(void);
/* Feature bit22: frame-stamped, copied catalog reservations for native owners.
 * Register slots use t5..t9; fresh mask bit0 describes the separate velocity ID,
 * other fresh bits are subsets of requested. Attachment bits use t6..t9.
 * Null-resource fidelity requires the device's nullDescriptor capability. */
typedef struct SevkStageBindings {
    uint32_t size, abi_version, stage, frame_index;
    uint32_t requested_mask, fresh_mask, attachment_mask, reserved;
    uint64_t velocity, images[10];
} SevkStageBindings;
typedef struct SevkStageBindingState {
    uint32_t size, abi_version, flags, rows, frame_index, reserved;
    uint64_t resolved, missing, mismatched;
} SevkStageBindingState;
SEVK_API int32_t SEVK_CALL sevk_set_stage_bindings(void* ctx, const SevkStageBindings* rows, uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_get_stage_binding_state(void* ctx, SevkStageBindingState* state);

/* Feature bit13: idempotent target creation, timeline retirement and queued
 * floating-point clears. No ABI-1 pass layout changes. Operations: 1 release
 * image, 2 release pipeline, 3 clear image at mark (1..6). Bit22 adds mark0 global
 * clears before native owners/public phases. Commands are copied. */
typedef struct SevkGuestResourceCommand {
    uint32_t size, abi_version, operation, mark;
    uint64_t image, source;
    float value[4];
} SevkGuestResourceCommand;
SEVK_API int32_t SEVK_CALL sevk_guest_resource_command(void* ctx, const SevkGuestResourceCommand* command);
/* Feature bit14. Recording a pass and completing its submission are distinct
 * from pipeline creation. last_frame is UINT32_MAX until a draw/dispatch records. */
typedef struct SevkGuestExecutionStatus {
    uint32_t size, abi_version;
    uint64_t pipeline, executions, last_completion, completed_timeline;
    uint32_t last_frame, active;
} SevkGuestExecutionStatus;
SEVK_API int32_t SEVK_CALL sevk_guest_execution_status(void* ctx, SevkGuestExecutionStatus* status);
/* Feature bit15: Anomaly's shared CPU environment calculation, matched to a
 * single immutable renderer frame. Changes only guest data, never stock light. */
typedef struct SevkGuestEnvironment {
    uint32_t size, abi_version, frame_index, reserved;
    float sun_color[3], sun_diffuse;
    float sun_toward[3], sky_luma;
    float sky_ambient[3], safety_scale;
    float planet_air_top, visual_atmo_ceil, skip_floor, skip_mul;
    float camera_delta[3], pad;
} SevkGuestEnvironment;
SEVK_API int32_t SEVK_CALL sevk_guest_frame_environment(void* ctx, const SevkGuestEnvironment* environment);
/* Feature bit12: extended flags 1024/2048 run before/after ordinary passes
 * within their mark. Catalog mask bits0/1/2 report current recorded velocity,
 * volumeReactive/reactiveMask. Reset and BeginFrame revoke older products.
 * Extra attachment sentinels are 0xFFFF000000000010..12, separate from catalogs. */
SEVK_API uint32_t SEVK_CALL sevk_guest_catalog_mask(void* ctx);
/* Feature bit11: independent scene-depth views. Configure before BeginFrame,
 * using <=3 guest kind-7 targets and copied camera-relative matrices. World
 * draw reserved bits8..10 select these views, independent of stock bits0..7.
 * The regular engine depth materials render before GBuffer/guest consumers.
 * Requests are consumed once; ready is a current-frame recorded-view mask. */
SEVK_API int32_t SEVK_CALL sevk_guest_depth_views(void* ctx,const uint64_t* images,const float* matrices,uint32_t count);
SEVK_API uint32_t SEVK_CALL sevk_guest_depth_ready(void* ctx);
/* Feature bit10 permits guest pipeline/image creation between BeginFrame and
 * EndFrame. The allocation window closes before world recording. Guest image
 * replacement waits for older submissions; structured updates retain their
 * frame-fenced copies. These calls share the renderer API mutex. */
SEVK_API int32_t SEVK_CALL sevk_guest_create_pipeline(void* ctx, const void* vs, uint64_t vs_bytes, const char* vs_entry, const void* ps, uint64_t ps_bytes, const char* ps_entry, uint64_t* pipeline);
SEVK_API int32_t SEVK_CALL sevk_guest_create_image(void* ctx, uint64_t id, uint32_t width, uint32_t height, uint32_t depth, uint32_t format);
SEVK_API int32_t SEVK_CALL sevk_guest_update_image(void* ctx, uint64_t id, const void* data, uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_guest_submit(void* ctx, const SevkGuestPass* passes, uint32_t count);
/* HDR sentinel readback requires a completed world frame and returns RGBA16F.
 * Feature bit16: renderable AfterUpscale output without vendor services,
 * output snapshot sampling for offscreen consumers, Upscale RGBA16F readback. */
SEVK_API int32_t SEVK_CALL sevk_guest_readback(void* ctx, uint64_t id, void* rgba, uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_guest_replay_now(void* ctx, uint32_t mark);
SEVK_API uint64_t SEVK_CALL sevk_guest_replay_count(void* ctx);
/* Renderer frame for the fullscreen extras CB and FrameTemporal. size 344.
 * far_plane, output size, and service flags are appended. Flags: 1 HDR
 * swapchain, 2 extra present, 4 temporal upscale. */
typedef struct SevkGuestFrame {
    uint32_t size, abi_version;
    uint32_t width, height, frame_index, history_valid;
    float jitter_x, jitter_y, safety_scale, sun_diffuse;
    float sky_luma, air_top, visual_ceil, skip_floor;
    float skip_mul, proj_scale_x, proj_scale_y, pad0;
    float sun_color[3], sun_pad;
    float sun_toward[3], toward_pad;
    float sky_ambient[3], ambient_pad;
    float camera_delta[3], delta_pad;
    float view_proj[16];
    float prev_view_proj[16];
    float camera_to_world[16];
    float far_plane, output_width, output_height;
    uint32_t service_flags;
} SevkGuestFrame;
SEVK_API int32_t SEVK_CALL sevk_get_guest_frame(void* ctx, SevkGuestFrame* frame);
SEVK_API int32_t SEVK_CALL sevk_set_view_clip(void* ctx, float far_plane);
SEVK_API int32_t SEVK_CALL sevk_invalidate_guest_history(void* ctx);
SEVK_API int32_t SEVK_CALL sevk_set_display_flags(void* ctx, uint32_t flags);
/* Upscale slot owner from Anomaly ClaimUpscale. Empty releases the slot.
 * se-dlss evaluates the AfterTonemap image. Other ids leave stock compose. */
SEVK_API int32_t SEVK_CALL sevk_set_upscale_owner(void* ctx, const char* id);
/* HDR UI transform for the presented HUD on an HDR swapchain, matching HdrRender's DX11 UI composite:
 * paper_white is scene paper white times UI gain in scRGB units (1 = 80 nits), shoulder is the inverse-
 * Reinhard UI shoulder in [0,0.95), peak caps UI in scRGB units (0 = uncapped). paper_white 0 disables. */
SEVK_API int32_t SEVK_CALL sevk_set_hdr_ui(void* ctx, float paper_white, float shoulder, float peak);
/* HdrRender's DX11 billboard lifts under HDR display tenancy: emissive LDR billboards (queued with flag 32, Keen's
 * ColorIntensity > 1 producer signal) scale by ldr_intensity while the tenant grades; PostPP billboards (Rich HUD)
 * take the sevk_set_hdr_ui transform on an HDR swapchain. ldr_intensity 1 disables the emissive lift. */
SEVK_API int32_t SEVK_CALL sevk_set_hdr_billboards(void* ctx, float ldr_intensity);
/* Diagnostics: Keen auto-exposure from the latest completed frame, as EyeAdaptation writes avgLuminance:
 * values[0] desired and values[1] adapted exposure in log2 stops (tonemapping scales by exp2(values[1])),
 * values[2] 1 once a frame has been read back, values[3] reserved. */
SEVK_API int32_t SEVK_CALL sevk_get_exposure(void* ctx, float* values);
/* Diagnostics: lets HDR display tenancy grade without an HDR swapchain (offscreen tests). */
SEVK_API int32_t SEVK_CALL sevk_force_hdr_display(void* ctx, uint32_t force);
/* HDR display contract (P4). Display flags: 1 FP16 HDR, 2 frame generation, 4 temporal upscale, 16 display tenant,
 * 32 allow tearing (IMMEDIATE without vsync), 64 low latency (one frame in flight). The DXGI fields describe the
 * window's monitor on Windows (display_known=0 elsewhere); luminance is in nits. */
typedef struct SevkDisplayInfo {
    uint32_t size, abi_version;
    uint32_t hdr_capable, hdr_active, tenant_graded, present_mode, tearing_supported, tearing_active, low_latency;
    uint32_t display_known, display_hdr10, bits_per_color;
    float max_luminance, max_full_frame_luminance, min_luminance, reserved;
} SevkDisplayInfo;
SEVK_API int32_t SEVK_CALL sevk_get_display_info(void* ctx, SevkDisplayInfo* info);
/* Raw RGBA16F scRGB presentation capture (after sevk_request_readback on an FP16 swapchain). */
SEVK_API int32_t SEVK_CALL sevk_readback_hdr(void* ctx, void* rgba16f, uint64_t bytes);
/* Tenant compute session (Anomaly RayScene v1 on Vulkan). A tenant owns storage buffers, 2D/array images
 * and compute/fullscreen pipelines (SPIR-V, set 0, guest register bands) and records one command list per
 * frame. The renderer runs it after deferred lighting and before AfterLighting guest passes; renderer HDR is
 * the only renderer draw target. Tenant images stay in GENERAL layout and start zeroed.
 * Command ops: 1 dispatch (x,y,z groups), 2 fullscreen draw (x,y viewport, 1-3 targets with layers),
 * 3 fill buffer (value[0] over offset/bytes, bytes 0 = rest), 4 clear image (value bits; flags 1 integer),
 * 5 copy buffer (source -> targets[0], offset/bytes), 6 copy image (source -> targets[0], equal shape),
 * 7 upload buffer (blob[blob_offset, +bytes] -> targets[0] at offset), 8 upload image layer (layers[0]),
 * 9 build BLAS targets[0] (ray query): x triangles of uint32 index triples from buffer targets[1] at offset over float3
 *   positions in buffer source (value[0] stride, value[1] vertex count); flags 1 builds x 24-byte AABBs from source at offset.
 *   Geometry is non-opaque, so ray queries see every candidate,
 * 10 build TLAS targets[0] from x 80-byte blob records {VkTransformMatrixKHR rows, custom index, mask, flags, pad, BLAS id, 0}.
 * Binding kinds: 0 null (zero dummy), 1 tenant image (sampled or storage per shader), 2 tenant buffer,
 * 3 uniform from blob (blob_offset as offset, bytes), 4 renderer image id (guest sentinels), 5 world texture id,
 * 6 world frame block (b0, 1280 bytes), 7 sampler (id 0 linear wrap, 1 linear clamp, 2 point clamp),
 * 8 top-level acceleration structure (unbound or unbuilt: an empty TLAS). SPV_KHR_ray_query is accepted on ray-query devices. */
typedef struct SevkTenantCommand {
    uint32_t op, binding_first, binding_count, target_count;
    uint32_t x, y, z, flags;
    uint64_t pipeline, source;
    uint64_t targets[3];
    uint32_t layers[3], reserved;
    uint64_t offset, blob_offset, bytes;
    uint32_t value[4];
} SevkTenantCommand;
typedef struct SevkTenantBinding { uint32_t binding, kind; uint64_t id, offset, bytes; } SevkTenantBinding;
typedef struct SevkTenantStats {
    uint32_t size, buffers, images, pipelines;
    uint64_t bytes, frames, commands, dispatches, draws, rejected, builds;
    uint32_t accels, features; /* features bit0: ray query, bit1: GPU timestamps */
    double gpu_ms; /* GPU time of the latest completed tenant list */
    uint64_t measurements; /* completed timestamp pairs */
} SevkTenantStats;
/* Image formats are VkFormat values; raster target format 0 means the renderer HDR format. flags bit0: array view. */
SEVK_API int32_t SEVK_CALL sevk_tenant_create_buffer(void* ctx, uint64_t bytes, uint64_t* id);
SEVK_API int32_t SEVK_CALL sevk_tenant_create_image(void* ctx, uint32_t width, uint32_t height, uint32_t layers, uint32_t format, uint32_t flags, uint64_t* id);
SEVK_API int32_t SEVK_CALL sevk_tenant_create_compute(void* ctx, const void* spirv, uint64_t bytes, const char* entry, uint64_t* id);
SEVK_API int32_t SEVK_CALL sevk_tenant_create_raster(void* ctx, const void* vs, uint64_t vs_bytes, const char* vs_entry, const void* ps, uint64_t ps_bytes, const char* ps_entry, uint32_t target_count, const uint32_t* formats, uint64_t* id);
/* Retires a resource or pipeline once the GPU finishes with it; refused while this frame's commands use it. */
SEVK_API int32_t SEVK_CALL sevk_tenant_destroy(void* ctx, uint64_t id);
/* Acceleration structure id (top 0 bottom, 1 top); storage is allocated by its builds. Needs ray query. */
SEVK_API int32_t SEVK_CALL sevk_tenant_create_accel(void* ctx, uint32_t top, uint64_t* id);
/* Appends to this frame's list (between begin and end frame); the blob is copied. */
SEVK_API int32_t SEVK_CALL sevk_tenant_record(void* ctx, const SevkTenantCommand* commands, uint32_t count, const SevkTenantBinding* bindings, uint32_t binding_count, const void* blob, uint64_t blob_bytes);
/* Diagnostics: waits for the GPU and copies a buffer range or one image layer (bytes = width*height*texel). */
SEVK_API int32_t SEVK_CALL sevk_tenant_readback(void* ctx, uint64_t id, uint32_t layer, uint64_t offset, void* data, uint64_t bytes);
SEVK_API int32_t SEVK_CALL sevk_tenant_stats(void* ctx, SevkTenantStats* stats);
/* Driver NGX for DLSS. The plugin passes the directory that contains the
 * feature library (nvngx_dlss.dll / libnvidia-ngx-dlss.so). mode is the
 * DLSS quality enum. preset 0 selects preset K. Calls copy the request
 * and return; init and evaluate run on the render thread. */
SEVK_API int32_t SEVK_CALL sevk_ngx_configure(uint32_t enabled, uint32_t mode, uint32_t preset, const char* feature_directory);
/* DLSS evaluation sharpness in [0,1], as DX11 SE-DLSS passes it; transformer presets may ignore it. */
SEVK_API int32_t SEVK_CALL sevk_ngx_set_sharpness(float sharpness);
SEVK_API int32_t SEVK_CALL sevk_ngx_status(char* text, uint32_t capacity);
/* Multi-line key=value snapshot for the plugin status panel. Same labels
 * the DX11 Show Status dialog prints: gpu, vendor, ready, supported, ishdr,
 * state, evals, failed, in, out, hdr, want, jitter (milli-pixels), history,
 * motion, feature. */
SEVK_API int32_t SEVK_CALL sevk_ngx_debug(char* text, uint32_t capacity);
/* FidelityFX Vulkan upscaler for FRS. The plugin passes the directory that
 * contains amd_fidelityfx_vk.dll or libamd_fidelityfx_vk.so. mode is the FRS
 * quality enum (0 quality, 1 balanced, 2 performance, 3 ultra, 4 native).
 * The library's default provider is FSR 3.1.4. DX11 stays on FSR 2.2.1. */
SEVK_API int32_t SEVK_CALL sevk_fsr_configure(uint32_t enabled, uint32_t mode, float sharpness, const char* feature_directory);
SEVK_API int32_t SEVK_CALL sevk_fsr_status(char* text, uint32_t capacity);
/* Multi-line key=value snapshot for the FRS status panel. Same sections as
 * the DX11 Show Status dialog: gpu, vendor, ready, supported, ishdr, state,
 * version, evals, failed, path, in, out, hdr, want, jitter (milli-pixels),
 * history, motion, feature. */
SEVK_API int32_t SEVK_CALL sevk_fsr_debug(char* text, uint32_t capacity);
/* Frame generation status for the same debug page as the DX11 warp.
 * The caller sets size and abi_version. The renderer fills the rest.
 * enabled is display flag 2. path is the last warp decision. */
typedef struct SevkFrameGenStatus {
    uint32_t size, abi_version, enabled, loaded, ready, eligible, vsync, refresh_hz;
    uint32_t width, height, generate_count, skip_count, used_velocity, last_failed;
    float game_fps, display_fps, mv_scale_x, mv_scale_y;
    char path[64];
    char error[192];
    char device[128];
    char evidence[192];
} SevkFrameGenStatus;
SEVK_API int32_t SEVK_CALL sevk_fg_status(SevkFrameGenStatus* status);
#ifdef __cplusplus
static_assert(sizeof(SevkFrameGenStatus) == 648, "framegen status is 648 bytes");
#endif
/* Hardware ray query. ABI 1 additive. supported/ray_query are 0 when the
 * device lacks the extensions, and enable 0 records no build and no dispatch.
 * Caps are the frame limits; exceeding them refuses that frame. */
typedef struct SevkRayCapabilities {
    uint32_t size, abi_version, supported, ray_query;
    uint32_t max_primitives, max_instances, shader_ready, refused;
    uint64_t builds, dispatches;
} SevkRayCapabilities;
typedef struct SevkRayOptions {
    uint32_t size, abi_version, enable, shadows;
    float exposure, shadow_bias, solar_radians;
    uint32_t reserved;
} SevkRayOptions;
SEVK_API int32_t SEVK_CALL sevk_get_ray_capabilities(void* ctx, SevkRayCapabilities* caps);
SEVK_API int32_t SEVK_CALL sevk_set_ray_options(void* ctx, const SevkRayOptions* options);
/* Detached native generations: bit19 postprocess IDs 0..61/128..143; bit20 adds
 * lighting IDs 256 Dir, 257 Point, 258 Spot, 259 shadowed Spot to the same atomic batch.
 * Existing ABI-1 layouts and transport sizes stay unchanged. */
typedef struct SevkPostprocessModule {uint32_t size,abi_version,stage,reserved;const void* spirv;uint64_t bytes;} SevkPostprocessModule;
typedef struct SevkPostprocessGenerationStats {
    uint32_t size,abi_version;uint64_t active,pending,executions,last_completion,completed;
    uint32_t last_frame,prepared,retired,format;uint64_t revision;
} SevkPostprocessGenerationStats;
SEVK_API int32_t SEVK_CALL sevk_prepare_postprocess_generation(void*,uint64_t,const SevkPostprocessModule*,uint32_t,uint64_t*);
SEVK_API int32_t SEVK_CALL sevk_activate_postprocess_generation(void*,uint64_t);
SEVK_API int32_t SEVK_CALL sevk_discard_postprocess_generation(void*,uint64_t);
SEVK_API int32_t SEVK_CALL sevk_get_postprocess_generation(void*,uint32_t,SevkPostprocessGenerationStats*);
/* Native owner extensions. Capability flags1/2/4: cells/GPU NEW/GPU OLD generations.
 * The implementation capability is separate from device admission/initialized owners. */
typedef struct SevkNativeOwnerCapabilities {uint32_t size,abi_version;uint64_t flags;uint32_t max_modules,max_stage_bindings;} SevkNativeOwnerCapabilities;
typedef struct SevkNativeGeneration {uint32_t size,abi_version;uint64_t expected_revision;uint32_t flags,count;const SevkPostprocessModule* modules;} SevkNativeGeneration;
/* DX11 GBuffer velocity developer probe: 0 off, 1 TargetClear, 2 MrtWrite, 3 HistoryCoverage, 4 PassEndClear. */
SEVK_API int32_t SEVK_CALL sevk_set_gbuffer_velocity_probe(void* ctx,uint32_t mode);
/* Temporal jitter owner: force=1 runs it without a temporal upscaler; state receives pixel x/y, clip x/y, active, phases, index, frames. */
SEVK_API int32_t SEVK_CALL sevk_force_jitter(void* ctx,uint32_t force);
/* Diagnostics: 1 resets and starts the SE_VULKAN_GPU_PASS_TIMING / SE_VULKAN_RECORD_TIMING accumulators, 0 stops them. */
SEVK_API int32_t SEVK_CALL sevk_set_timing_reports(void* ctx,uint32_t active);
SEVK_API int32_t SEVK_CALL sevk_get_jitter(void* ctx,float* state,uint32_t count);
SEVK_API int32_t SEVK_CALL sevk_native_owner_capabilities(void* ctx,SevkNativeOwnerCapabilities* state);
/* flags1 marks a stock restore: execute stock without pack-only GPU production instrumentation. */
SEVK_API int32_t SEVK_CALL sevk_prepare_native_generation(void* ctx,const SevkNativeGeneration* request,uint64_t* generation);

#ifdef __cplusplus
}
#endif
