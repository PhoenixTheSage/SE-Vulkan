#pragma once
#include <stdint.h>
/* CPU-only scene contract. All arrays are copied; calls run on the render owner.
 * Actor flags: visible, skip-main, skip-forward, cast-flag, effective-casts.
 * Frame enabled bits: main then eight cascade frusta. Global flags: explicit/update.
 * Result flags: selected/main-candidate/main/probe/spots. Slice flags: main/probe/spots. */
typedef struct SevkSceneLod {int32_t current,previous;float transition,start;uint32_t initialized;} SevkSceneLod;
typedef struct SevkSceneActor {uint64_t handle;double translation[3],bounds[6];float scale;uint32_t flags;double camera[3];SevkSceneLod lod;uint32_t have_camera;} SevkSceneActor;
typedef struct SevkSceneFrame {double camera[3],planes[216];uint32_t enabled,cascade_count;float probe_distance,shadow_distance,coefficient,delta,global[9];uint32_t global_flags;int32_t selection,passes[33];double smooth_distance;} SevkSceneFrame;
typedef struct SevkSceneSpot {float position[3],direction[3],range,cosine;} SevkSceneSpot;
typedef struct SevkSceneSlice {int32_t lod;float alpha;uint32_t flags,cascades;} SevkSceneSlice;
typedef struct SevkSceneResult {SevkSceneLod lod;uint32_t flags,count;int32_t spot_lod;SevkSceneSlice slices[12];} SevkSceneResult;
