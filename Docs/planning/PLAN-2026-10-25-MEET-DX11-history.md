# Plan history: meet or beat DX11 (archive)

Archived 2026-10-30. This is the full plan as it stood before it was condensed:
Phase 4 milestone notes, Plan 2, the step-by-step status, designs and evidence
lists. The working plan is `PLAN-2026-10-25-MEET-DX11.md`; measurements and run
IDs are in `Docs/Performance-2026-10-25-Attribution.md`.

---

## Original title: Plan: meet or beat DX11

Goal: private Pertam, 1080p, product defaults. Present cadence **below 4.17 ms
on both mean and P95**, in three both-order pairs, with parity gates unchanged
(see `PERFORMANCE_STRUCTURAL_PLAN.md`, final acceptance).

**History of this plan:**
- Started 2026-10-25.
- Rewritten 2026-10-26 after steps 1–9.
- Item 0 rewritten 2026-10-27 after Phase 4 step 1 and the frame-split
  research.
- Updated 2026-10-28 after Phase 4 milestones 1–5. The Phase 4 milestone
  sheet (the approved plan for the split) is merged in below ("Phase 4: the
  frame split"); this document is now its source.
- Updated again 2026-10-28 (late):
  - milestone 6 (the build thread) and its diagnosis;
  - 1b closed, and 1a re-scoped to 1a′ and done;
  - Plan 2 (the four Build-chain costs) written and built;
  - a register of wrong assumptions ("Already tried, and wrong
    assumptions"). Read it before proposing work.
- Updated 2026-10-29: Plan 2 measured (batches 49 and 50). The always-on
  changes take 0.35 ms off Build; the opt-ins and thread pinning don't move
  cadence, because the recording chain now paces the frame.

History, measurements and run IDs are in
`Docs/Performance-2026-10-25-Attribution.md`.

## Where we are (2026-10-30)

**After the recording and Build trims (`515174c`, `8c3838c`):** split 4.72–5.10 ms
against DX11 4.22–4.36, depending on the session. Across one evening the same code's
GPU frame drifted 2.35 → 2.70 ms and DX11 itself 4.22 → 4.36, so changes under about
0.15 ms can't be read across sessions; use same-session interleaved A/B only. Three
chains sit at about 4.5–4.7 ms each, and shortening one hands the frame to the next:
- **recording:** laps about 3.5–3.6 ms, plus a submit blocked about 0.3–0.4 ms behind the
  present lock and a 0.5–0.7 ms gap before the next launch;
- **Build:** about 3.9 ms of its own work (publication 1.3, `QueueWorld` 0.8, traversal 0.5,
  voxels and billboards 0.4);
- **GPU + present:** GPU frame 2.5–2.7 ms plus an in-game present call of about 1.4 ms,
  which a standalone probe of the same present structure does in 0.2 ms (cause open).

## Where we were (2026-10-29)

**After GPU light tiles (late 2026-10-29):** split **4.55–4.58 ms** against
DX11 4.22–4.23 (batch 83). The GPU frame dropped 2.87 → 2.35 ms: the point
pass looped over all 93 lights per pixel until GPU tile selection, wrongly
closed in batch 76, actually ran (attribution doc, "The GPU frame:
point-light tiles and the spot scissor"). The GPU + present chain is now
about 3.75 ms. The **recording chain** paces the frame at about 4.6 ms
(laps 3.7, blocked submit 0.22, lock release 0.08, launch gap 0.59); the
Build chain is close behind at about 4.3 ms. Both must come down about
0.45 ms.

Present-type switching (our own DXGI swapchain) and overlay layers are
closed; fixes must not need driver or system settings.

**Earlier, after C1 and the decoupled present (`26862d2`):** split 4.92–5.29 ms
against DX11 4.22–4.24 in the same sessions. Three chains now pace the
frame, each about 4.3–4.9 ms:
- **Recording:** laps 3.45 ms, plus about 0.5 ms of `vkQueueSubmit2`
  blocked behind the present call's driver lock, 0.1 ms lock release, and
  about 0.86 ms from a recording's end to the next launch (the build thread
  reaches `begin_frame` about 0.47 ms after the end, `begin_frame` takes
  0.31 ms).
- **Build:** about 4.3 ms of its own work.
- **GPU + present:** the GPU frame (about 2.95 ms) and the present call
  (1.3–1.4 ms after the GPU goes idle) serialize, because the next submit
  can't start while the present holds the driver lock: about 4.45 ms.

**The benchmark runs windowed on a 240 Hz monitor**, so 4.17 ms is exactly
one refresh; DX11's 4.22–4.24 is effectively DWM-paced. Beating it means
presenting faster than the refresh (mailbox discards), which is the path
where the driver's present costs 1.3 ms.

### Earlier status (2026-10-28)

| | Vulkan (current defaults) | DX11 |
|---|---|---|
| Present interval, mean, quiet box (2026-10-27 pairs) | 5.32–5.55 orbit / 5.44–5.47 interior (before the split work) | 4.27–4.47 |
| Present interval, mean, 2026-10-28 session (slow box) | main 7.09–7.12 orbit / 6.51 interior; pre-split 6.40–6.54 / 5.86–5.87 | 4.41 orbit / 4.68 interior |
| Present interval, P95 | about 7.4–7.9 (2026-10-27) | about 5.1–5.7 |
| Render thread, Draw (CpuDetail) | about 5.0–5.4 ms serial (2026-10-27); 7.57 against 7.08 pre-split in the 2026-10-28 pair | — |
| Game-loop gap outside Draw | about 0.8 ms | — |
| Recording laps total | about 3.1 ms | — |
| GPU, full frame | about 2.9–3.1 ms | — |

**Frame split status (CpuDetail pair `a1`, late 2026-10-28; CpuDetail adds
overhead, so compare only within the pair):**
- Split 5.90–6.05 ms present, single-threaded 6.13–6.53: the split is ahead
  of single-threaded, but nowhere near 4.17.
- The split runs **two pacing chains of about equal length**:
  - Build, about 5.1 ms of its own work;
  - the native recording, **5.16 ms from launch to end**.

  One recording slot makes Build(N+1) wait for recording(N) at `EndUploads`
  (0.5–0.6 ms).
- **The 4.17 target needs both chains under about 4.0 ms.** A Build-only
  saving shows up as wait, not cadence.
- The split also makes Build's phases use 11–24% more CPU cycles than single-threaded
  (cache contention, not preemption).

The 2026-10-28 session ran about 20% slow across the board: pre-split
measured 6.4–6.5 ms against 5.3–5.5 the day before. Compare only within a
session's pairs.

History of the mean:
- 2026-10-25 start: 11.3–11.5 ms.
- Step 6: 6.6–6.9 ms.
- Step 7: 6.2 ms.
- Step 8 defaults: 5.32 / 5.52 ms.
- Phase 4 step 1 (overlap inside one frame, opt-in): flat.
- Phase 4 milestones 1–5 (the frame split, still on one thread): about
  0.5–0.6 ms slower than pre-split in same-session pairs. This is the
  single-thread cost of the copies the split needs. It pays back only when
  Apply and Build overlap (milestone 6), and is being trimmed first (see
  "Phase 4", "The single-thread cost").

**The gap:** about 1.1–1.3 ms on the mean and about 2.3 ms on P95 against the
pre-split build. All of it is render-thread time. Every render-thread cut
measured since step 8 is worth about 0.1 ms or less, so the gap needs overlap
across frames: the frame split.

The second scene, the interior with the user's full plugin set idle, runs
7.66 ms against 5.98 alone. The Vulkan-side waits are fixed; the rest is
Anomaly framework cost (see "Plugin set" below).

## How a frame is gated today

Three threads pipeline the work:
- the render thread runs managed `Draw` for frame N, now as `ApplyFrame` then
  `BuildFrame`, back to back;
- the recording thread records N−1;
- the present thread presents N−2.

Recording releases `api_mutex` once it has snapshotted the scene state it
reads (step 7), so N's publication and `QueueWorld` run while N−1 records.
Ordinary native calls that still wait for recording total about 0.08 ms per
frame, at streaming sites only. The 2026-10-28 census found 1,509 pipelined
waits in 9,029 frames, nearly all texture uploads.

- **The wall is the render thread.** Draw is one serial chain (CpuDetail, ms
  per frame, step 8–9 runs, before the split):

  | Order | Step | ms |
  |---|---|---|
  | 1 | Messages | 0.35 |
  | 2 | Clipmaps | 0.10 |
  | 3 | Particles and decals (mostly the game's own particle manager) | 0.33 |
  | 4 | Scene update: transforms 0.14, updater 0.08, spatial index 0.04, shadow poses 0.14, occlusion 0.28 (bounds refresh, now incremental) | about 0.7 |
  | 5 | Traversal | 0.38 |
  | 6 | Voxels and flares | 0.15 |
  | 7 | Lighting setup | 0.23 |
  | 8 | Native pass publication (prepare 0.42–0.49, publish 0.55, cell publish 0.26) | 1.7 |
  | 9 | QueueWorld (native intake 0.23, managed blob packing 0.16, transparency 0.07, the rest 0.16) | 0.63 |
  | 10 | Sprites, `BeginFrame`, `EndFrameAsync` | small |

  After the split, 1–4 plus billboards, probe ambient and the stats overlay
  are Apply; the hand-off follows; Build is the clipmap update and 5–10.
- **Recording has headroom.** About 3.1 ms of laps, against a render thread
  of about 5.4. The spot grid and parallel main cut 0.37 ms of recording, and
  that is banked for when the render thread gets faster.
- **P95 tails are not garbage collection.** On the slowest frames, nearly
  every phase runs 0.3–0.9 ms long at once (the render thread losing CPU), and
  some frames add a single stall of about 3 ms that lands in a different phase
  each time. The game runs its render thread at Normal priority during
  gameplay. Allocation is flat at about 300 KB per frame before and after the
  split.
- **Present and GPU are not walls.**

## Done

| Item | What | Toggle (default on unless noted) |
|---|---|---|
| Sessions 1–2 | Instrumentation; LCD renders in place; object motion only with a consumer; harness telemetry stride; snapshot stats reads; per-worker spot pools; allocation passes 1–2 | — |
| 1.4 | Stable main order: skip native-owned actors in traversal | `SE_VULKAN_STABLE_MAIN_ORDER=0` |
| Sessions 4–6 | Voxel plan-ahead; deferred world intake and cell publish | `SE_VULKAN_VOXEL_PLAN_AHEAD=0`; deferred intake and publish opt-in (no gain, re-measured with recording headroom) |
| Steps 1–3 | Shared native job pool; managed render jobs; emitters; packet signature; voxel emit-ahead; resident-only textures; flare and billboard fork | `SE_VULKAN_JOB_*`, `SE_VULKAN_MANAGED_JOBS=0`, `SE_VULKAN_VOXEL_EMIT_AHEAD=0`, `SE_VULKAN_EFFECTS_AHEAD=0` |
| Steps 4–5 | Acquire off the render thread; surface polling; present after GPU completion; probe scheduling from the launch snapshot; lazy acquire; sort reuse | `SE_VULKAN_DEFERRED_ACQUIRE=0`, `SE_VULKAN_PRESENT_AFTER_GPU=0`, `SE_VULKAN_PROBE_STATS_SNAPSHOT=0`, `SE_VULKAN_SORT_REUSE=0` |
| Step 6 | Native-owned actors out of the candidate index; particle reflection memoized; view lanes; parallel stream copy; split recording laps | `SE_VULKAN_INDEX_OWNED=1`, `SE_VULKAN_VIEW_LANES` (default 4) |
| Step 7 | Early release of `api_mutex` after recording's snapshot; scene calls; probe plan; plugin-wait fixes (stream textures, ray capabilities, upscaler resolution) | `SE_VULKAN_EARLY_RELEASE=0` |
| Step 8 | Spot caster grid; parallel NEW main recording; allocation fixes (publication buffers, `NativeSpriteBlit`, pooled frusta) | `SE_VULKAN_SPOT_GRID=0`, `SE_VULKAN_PARALLEL_MAIN=0`; allocation fixes always on; `SE_VULKAN_PASS_MATERIAL_REUSE`, `SE_VULKAN_PASS_LOD_DEDUPE` opt-in (no effect) |
| Step 9 | View cache; incremental occlusion group bounds (verified exact); early prepare fixed (scene call, snapshot probe plan) and on | `SE_VULKAN_PASS_VIEW_CACHE=0`, `SE_VULKAN_INCREMENTAL_GROUP_BOUNDS=0`, `SE_VULKAN_EARLY_PASS_PREPARE=0` |
| Phase 4 step 1 | Overlapped native publication inside the frame: preparation during the traversal, publish as a job over voxels and lighting setup, republish on a probe-plan mismatch | opt-in `SE_VULKAN_OVERLAP_PUBLICATION=1` (flat: join wait 0.29 ms and `api_mutex` blocking eat the about 0.4 ms window) |
| Phase 4 milestones 1–5 | The frame split restructured on one thread: Apply/Build boundary and `FramePacket`; actor frames and Build-owned scene state; per-frame outputs; subsystem owners; native frame gate (see "Phase 4") | always on (the structure); `SE_VULKAN_APPLY_BUILD_ORDER=0` restores the old scene-update order for the lane; `SE_VULKAN_FRAME_PACKET_VERIFY=1` verify mode; `SE_VULKAN_FRAME_GATE=1` and `SE_VULKAN_SCENE_CALL_ASSERT=1` opt-in |
| Phase 4 milestone 6 | The build thread; hand-off races fixed; Build runs at Highest; join Build before texture-mutating messages | opt-in `SE_VULKAN_FRAME_SPLIT=1`; `SE_VULKAN_STAGE_SPIN_US`, `SE_VULKAN_BUILD_PRIORITY=above`, `SE_VULKAN_RECORD_JOBS_FIRST=1` (no gain) |
| 1b | Per-view publication batch cache: 93% hit, about 40 µs saved; closed | opt-in `SE_VULKAN_PASS_BATCH_CACHE=1` |
| 1a′ | Step 1: pass glass cache (−0.12 ms publication), plus the glass signature gate. Step 2: mixed glass actors on the prepared path (a wash, +16 packets). Step 3: skinned constants cache (−0.01 ms). Legacy-path census. | glass cache **default on** (`SE_VULKAN_PASS_GLASS_CACHE=0` off); opt-in `SE_VULKAN_PREPARED_MIXED=1`, `SE_VULKAN_SKIN_CONSTANTS_CACHE=1`, `SE_VULKAN_LEGACY_CENSUS=1` |
| Plan 2 (built, not measured) | Typed frame constants; recipe draws built in place; one-copy arena; early cell preparation; selection trims; thread placement toggles; cache-line separation of recipe material caches and world submissions; batched constant recycling | always on: frame writer (`SE_VULKAN_FRAME_CONSTANTS_VERIFY=1` checks it), in-place recipe build, selection trims, alignment, batched recycle. Opt-in: `SE_VULKAN_PARALLEL_RECIPES=1`, `SE_VULKAN_WORLD_ARENA=1`, `SE_VULKAN_EARLY_CELL_PREPARE=1`, `SE_VULKAN_BUILD_CPU`, `SE_VULKAN_STAGE_CPU`, `SE_VULKAN_JOB_AFFINITY`, `SE_VULKAN_TOPOLOGY_LOG=1`, `SE_VULKAN_PLAN2_COUNTERS=1` |
| Lanes | Render-thread priority; GC latency mode | opt-in: `SE_VULKAN_RENDER_PRIORITY=high` (possibly 0.1–0.2 ms, unsafe as a default on CPUs with few cores); `SE_VULKAN_GC_LATENCY=sustained` (no gain) |
| Plugin set | Anomaly bridge reflection cached and pass ordering once per frame; SSGI and Clouds idle when off | in the plugin repositories |

The attribution doc covers how each gate was found and what each step
measured:
- "Why the scheduler steps did not move the cadence";
- "Render-thread native call census";
- "Step 7: early release of the recording lock";
- "Second scene and the user's plugin set";
- "Step 8 …" and "Step 8 benchmark";
- "Quick checks and steps 2–3";
- "Steps 4 and 6a, and new defaults";
- "Phase 4 step 1: overlapped native publication";
- "Phase 4 frame split, milestone 1" through "milestone 5" (one section per
  milestone and slice, with gates and run IDs).

## Next, ranked (2026-10-29)

Done since the last ranking:
- milestone 6, the build thread (opt-in);
- 1b (closed);
- 1a′ steps 1–3;
- Plan 2 A, B and D built, gated and measured (batches 49 and 50; see
  "Plan 2 measured");
- the quiet-box VAL and VALX reruns (clean).

1. **Plan 2 C: the recording chain. This is now the pacing limit.** Every
   Build saving since `a1` (0.35 ms always-on, early cells, parallel
   recipes) lands on `EndUploads` instead of cadence.
   - C1: laps priced (batch 52, attribution doc "C1: recording laps").
     Recording 5.4–5.7 ms launch to end under the split; top laps spot
     shadows 0.73, NEW main 0.64, pass streams 0.55, CSM copy 0.53 (+0.10
     under the split); about 1.1–1.3 ms outside the laps.
   - **C1 in progress (2026-10-29):** step 0 diagnostics and seven trims
     built (attribution doc, "C1 step 0 and the first recording trims").
     Split present 5.78 → **4.87–5.17 ms**; recording laps 4.44 → 4.07 ms.
     The recording then waited about 0.45 ms for the previous present.
   - **Decoupled present built (2026-10-29):** split **4.92–4.93 ms**
     against DX11 4.24 in the same session (attribution doc, "The present
     chain and the decoupled present"). Next: the 0.5 ms the recording
     still pays after `frame end` (copy wait, submit), the gap between a
     recording's end and the next launch, then the Build chain.
   - C2: design the second recording slot once C1 shows how short the
     recording gets.
2. **Done 2026-10-29: early cells and parallel recipes default on**
   (`SE_VULKAN_EARLY_CELL_PREPARE=0` / `SE_VULKAN_PARALLEL_RECIPES=0` turn
   them off). Both passed the frozen-light lane; −0.14 ms `CellPublish` and
   −0.03 to −0.15 ms on the build thread. Their savings reach cadence only
   after C.
3. **The arena's 1.5 ms regression:** repeat `NPXA` against `NPXCP` in one
   pair before judging it. Keep it off until then.
4. **Plan 2 B5, resident world draws.** Count how many of the ~3,000 draws
   repeat frame to frame first; design only if the census justifies it. Like
   every Build saving, it waits on C.
5. **Milestone 7 remainder:** input-latency measurement, user plugin set
   with the split, and the split default.
6. **P95 on the render thread** (scheduling attribution).
7. **Plugin set** (Anomaly consumer-driven producer passes).

## Already tried, and wrong assumptions (read before proposing work)

Each row is an assumption a plan made that the code or the data proved wrong,
or work that was tried and closed. Don't redo these without new evidence.

| Assumption or idea | What is actually true | Found by | Status |
|---|---|---|---|
| The split's `EndUploads` wait is Build's own cost | It is Build(N+1) waiting for recording(N): one recording slot, and the recording runs about 5.2 ms from launch to end | pipeline census `m6k`, `a1` | Fix is Plan 2 C (shorten the recording, second slot), not Build work |
| "Zero owner waits" ruled out the recording wait | The counter keyed on `frame_owner`, which `end_frame` clears; it now keys on `scene_owner` | `m6k` | Fixed |
| The recording slows under the split from job-priority inversion | Fix B (recording jobs first) left the laps unchanged; the `m6k` lap growth was a busy box | A/B `m6l` | Opt-in `SE_VULKAN_RECORD_JOBS_FIRST=1`; don't retry |
| The split's slowdown is preemption or scheduling | Build's phases use **11–24% more CPU cycles** (Handoff +48%): contention for caches and memory | `QueryThreadCycleTime`, `a1` | Plan 2 D (placement test, false sharing) |
| Caching publication rows (1b) saves the publish | 93% of view entries hit, but a hit still pays row mapping and pushes: about 40 µs saved. The first version also wiped every entry on any view change (global shape bump) | `m1c`, `m1d` | Closed; opt-in `SE_VULKAN_PASS_BATCH_CACHE=1` |
| The traversal loops over about 1,865 candidates (original 1a: parallel traversal) | Stable main order already skips pass- and cell-owned actors; about 52 reach the loop and 45 emit. `PlanCandidates` never runs (under 256) | CpuDetail `m6m` | 1a re-scoped to 1a′; fan-out not worth it |
| Legacy rigid actors take the slow path for highlights or light exclusion | All 9 on Pertam are stock old-pipeline blocks with glass (LCDs, cockpits, a text panel, a store block); none were excluded actors | `SE_VULKAN_LEGACY_CENSUS=1` | Moving them to the prepared path is a wash and adds 16 packets; keep it off |
| A skinned palette can be reused while `SkinMatrices` is unchanged | An animating character replaces the array every bone update; the saving is only the constants lookups (about 0.01 ms) | pair `a1` | Opt-in; low value |
| The packet signature gates glass | Glass packets were not in it | 1a′ step 1 | `glass.sig` added to the lighting lane |
| Recipe validation costs 0.22 ms per frame | Recipe validity was already cached (`validate_world_recipe` returns at once when valid). The cost is building about 3,000 `WorldDraw` records through a temporary and a second copy | code reading, Plan 2 B3 | Built in place (always on); "revalidate only changed draws" is moot |
| Native pass selection is serial and can be parallelised (Plan 2 A1) | Already parallel: `parallel_selection` is on by default and splits about 1,870 selections into up to 8 ranges, so 0.41 ms is parallel wall time. Merge (0.16) is the serial part | code reading | Only per-row trims built; don't re-parallelise |
| Folding OLD cell publication into the pass publish shares culling (Plan 2 A2) | The cells already prepare on `CellView` jobs, and pass and cell actor sets are disjoint; nothing to share | code reading | Built instead: early cell preparation overlapping voxels and lighting |
| The managed CPU profiler's counters false-share between threads | It already keeps a separate lane per thread (`BuildCurrent`) | code reading, Plan 2 D2 | Ruled out |
| The actor snapshots and hand-off copy far more than needed (Plan 2 D3) | They are dirty-only already; the 0.13 ms hand-off runs on the render thread, not Build | code reading | Only the batched constant recycle was worth doing |
| VAL's unload `VK_ERROR_OUT_OF_DATE_KHR` comes from the latest change | Intermittent since before the glass cache (`m1b`, `m6d`, `val-batch`): `accept_present` marked the surface dirty, then fell through and threw | VAL history | Fixed in `0e263e9` (returns after marking the surface dirty); confirm on the VAL rerun |
| `BillboardPacketTests` fails because of the frame-split work | Stale test: since milestone 6, collected billboards reach emission only after the hand-off (`renderer.Latch()`), which the test never called | side session | Test fixed on main (`f39b252`); production unaffected |
| A validation run's slowness means the code is slow | VAL at about 580 ms per frame and VALX "too few frames" in the Plan 2 gates came from another game on the box (about 7 cores, GPU frame 195 ms) | process sample | Check box load before validation runs too, not only before benchmarks |
| Pinning the build and recording threads apart removes the split's cycle inflation (Plan 2 D1) | On the i9-9900K (one shared 16 MB L3, no CCX) pinning to separate cores or SMT siblings, with workers kept off, both make the build thread and `EndUploads` longer | batch 50 `NPXSEP`/`NPXSMT` | D1 closed; no pinning. The `SE_VULKAN_BUILD_CPU`/`STAGE_CPU`/`JOB_AFFINITY` toggles stay as diagnostics |
| A Build-chain saving shortens the split's cadence | Under the split it turns into `EndUploads` wait: early cells cut `CellPublish` 0.14 ms and `EndUploads` grew 0.10; all three opt-ins together grew it 0.32 | batches 49–50 | Plan 2 C first; measure Build work by phase, not cadence |
| A present-call wait can be hidden by presenting later (`SE_VULKAN_PRESENT_GATE`) | Presenting while the next frame runs on the GPU makes the present wait behind it (3.9 ms calls, lock held throughout), the GPU frame grows to 3.7 ms and cadence drops to 6.1 ms | batch 68 | Off; kept as a diagnostic |
| GPU tiled light selection (`SE_VULKAN_LIGHT_TILES=1`) doesn't cut deferred lighting (batch 76) | **Wrong: the tiles never ran.** `Run-Isolated.ps1` writes `SE_VULKAN_LIGHT_TILES` from its own `-LightTiles` switch and overwrote the lane's `1` with `0`. The stock point shader does read the per-tile lists; we gave every tile all 93 point lights. With `-LightTiles` a tile holds 3.5 lights and the point pass drops 0.56 → 0.05 ms (selection 0.06 ms) | batches 81–82, `point lights` / `point tile lights` counters | Default on since 2026-10-29 (`SE_VULKAN_LIGHT_TILES=0` reverts); product lanes pass `-LightTiles` |
| Running each spot light as a full-screen pass is the deferred lighting cost | All spot passes together are 0.05 ms; scissoring each to its projected cone cut covered pixels 15.7 → 10.4 M a frame and saved about 0.02 ms. The point pass was the cost | batches 80–81 (lighting sub-marks) | Spot scissor kept (identical output, `SE_VULKAN_SPOT_SCISSOR=0` reverts) |
| Taking the frame's `vkQueueSubmit2` (0.22 ms blocked behind the present lock) off the recording thread shortens the cadence | A FIFO submit thread (`SE_VULKAN_ASYNC_SUBMIT=1`) cuts the tail submit to 1 µs, but three interleaved rounds give 4.72 against 4.73 ms: the build thread arrives late more often (waited frames 80% → 63%) and paces the frame instead | batches 84–85 | Opt-in; Build chain next |
| The upload batch must join the present before submitting | Only on the direct path, where the present shares the frame queue. With the decoupled present it waited about 2 ms behind the worker in the 4% of frames that flush uploads | pipeline census `UploadPresentJoinUs`, batch 83 | Skipped when decoupled |
| Fanning the pass stream copy out to the pool speeds it up | 210 KB in about 1,330 streams: the fan-out cost 0.28 ms of scheduling and joins; serial is 0.18 ms | batch 87 | Pool only from 2 MB |
| Pass material reuse saves the per-group material lookups | It hits 26% of Pertam frames and saves nothing | batch 87 | Off |
| Shortening the recording laps shortens the cadence | Laps −0.17 to −0.55 ms, cadence flat: the launch gap and blocked submit grow, because Build and GPU + present are as long | batch 90 | Cut all three chains together |
| With Build now the limit, overlapped publication (`SE_VULKAN_OVERLAP_PUBLICATION=1`) pays | Much worse: 6.07 / 6.29 against 5.11 / 5.04 ms, launch gap about 2.3 ms | batch 100 | Off; don't retry |
| Async submit pays once Build got shorter | Still neutral (5.07 against 5.10 ms over three rounds): the build thread, at about 4.5 ms of its own work in that session, paces the frame | batch 97 | Opt-in |
| Recording-lap cuts move the cadence | Parallel prepare loops (geometry, sort keys, cascade casters) cut laps about 0.1–0.15 ms, but the launch gap grows the same amount; Build is the limit. Workers must not use the recording thread's `thread_local` sort keys (the first version crashed at world load) | batches 98–99 | Kept (`SE_VULKAN_PARALLEL_PREPARE=0` reverts); pays once Build is shorter |
| The async OLD-cell emission finishes before the recording ends | It runs about 390 µs from `QueueWorld`, and the first call after the recording still waits 50–120 µs for it. Its join can't move later: replayed deferred calls may register recipes the emission reads | cells-emit counters, batch 96 | Open |
| The in-game present waits behind the next frame's GPU work | Holding the next frame's submit until every queued present returned (diagnostic `SE_VULKAN_SUBMIT_AFTER_PRESENT`, removed) left the present call at 1.415 ms against 1.347. Extra device allocations (up to 4,000) don't slow the probe's present either | batch 91, `present_probe` | Not ordering, not allocation count |
| A swapchain setting explains the in-game 1.4 ms present | The probe stays at 0.2 ms with `TRANSFER_SRC` usage, sRGB, 3 images, borderless or windowed, any exe name; exclusive info is only chained on request | `present_probe`, 2026-10-29 | Cause still inside the game process; open |
| A harness env toggle passed through `Vars` or `BENCH_EXTRA_ENV` reaches the game | Not for toggles `Run-Isolated.ps1` knows: it writes every one of them, `0` unless its switch is passed | batch 82 | Pass the harness switch; check a counter proves the policy ran |
| The present call blocks on a full flip queue | 3, 4 and 6 swapchain images all give a 1.3–1.4 ms present call | batch 71 (`SE_VULKAN_SWAP_IMAGES`) | Not the cause |
| Presenting from the async compute family (family 2) lets the present overlap the next frame | Same 1.37 ms present when the GPU is idle; with the gate on it still waits behind the next frame (3.65 ms) and stalls the recording (laps 5.1 ms) | batches 72, 74 | The driver's present waits for the whole GPU to go idle. Family 2 kept (no cost, `SE_VULKAN_PRESENT_FAMILY=0` reverts) |
| The present call's 1.4 ms is driver CPU work | 0.47 ms CPU (thread cycles), about 1 ms blocked, holding the lock that stalls other threads' submits | batch 73 | GPU+present floor is about GPU frame + 1.0–1.4 ms |
| The NVIDIA windowed Vulkan present costs 1.3–1.4 ms by itself | A standalone probe with our present structure (windowed 1920×1080 at 240 Hz, mailbox, 3 images, a worker that waits for the copy on a second queue and then presents, GPU kept busy about 2.9 ms per frame) presents in 0.19–0.29 ms. It stays there with sRGB or UNORM images, borderless 5120×1440, the exe renamed `Interim.exe` or `SpaceEngineers.exe`, and a window thread that pumps only every 16 or 100 ms. Inside the game it is 1.5 ms. Disabling the EOS overlay, Steam overlay or Fossilize layer one at a time left split cadence at 5.56–5.61 ms | `present_probe` (scratchpad), batch 79 | Cause inside the game process still open. Overlay checks closed: not worth the time (user, 2026-10-29) |
| Presenting through our own DXGI flip-model swapchain (D3D12 interop) avoids the floor | In the probe, Present(0, 0) without tearing locks to the refresh (4.171 ms); only `DXGI_PRESENT_ALLOW_TEARING` runs faster (3.2–3.6 ms), and that tears in borderless | `present_probe` dxgi / dxgitear | Abandoned: no present-type switching (user, 2026-10-29). Driver settings are out too: a fix may not need the player to change anything |
| DX11's 4.24 ms is CPU-bound like ours | The harness is windowed on a 240 Hz monitor (4.17 ms per refresh); DX11 sits at the refresh | config and log, 2026-10-29 | The target means presenting faster than the refresh |
| A faster recording shortens the cadence directly | Once the recording got short, it waited about 0.45 ms for the previous present: `vkQueuePresentKHR` holds its queue about 1.2 ms after the GPU finishes, and both acquire and submit had to join it | lap `join present`, batch 59 | Decoupled present (compose offscreen, present worker on a second queue) |
| Presenting on a second queue is independent of the frame queue | The driver's present waits for the frame queue to make progress; a frame GPU-waiting on a copy the worker had not submitted deadlocked at startup | watchdog dump, batch 62 | Frame waits for the copy on the CPU |
| `vkQueueWaitIdle(queue)` makes swapchain recreation safe | Not with a second present queue: an access violation at world load under validation until the present queue was idled too | VALX, batch 64 | `resize_output` idles both queues |
| The recording stage takes about 0.4 ms to start (`m6l`) | It wakes in about 3 µs. The 0.4–0.5 ms before the first lap was `settle_cells()` running the deferred OLD-cell emission on the recording thread | C1 step 0 counters | Emission now runs async on a worker after `EmitCells` |
| Recording laps that cost hundreds of µs must be lock waits | Thread cycles per lap show 3.5 cycles/µs (the full 3.6 GHz TSC) on every lap except the job joins and the swapchain wait: it is real CPU work, much of it in the driver (`vkCmdCopyImage` of depth cascades: about 0.34 ms) | lap cycles, batch 56 | Move driver-heavy recording to worker secondaries |
| Spot static maps rebuild because the scene changes | Moving grids update at 60 Hz against about 180 rendered frames/s; a caster settled into the static map after one still frame, so about 3.9 maps a frame rebuilt | C1 counters | 30-frame settle hold; final depth unchanged |
| The `CSM copy` lap is the CSM copy | A lap runs from its mark to the next mark. `CSM copy` covers `prepare_csm` (a 1.2 KB constant fill and up to 4 layer copies) **plus** `render_probes` up to its first mark: `plan_probes`, `probe_targets`, `prepare_probe_face`, and every probe postprocess, mip and prefilter op, which have no marks. Likewise `prepare pass streams` ends at `prepare sort` and includes the material sets | code reading, C1 | Read lap names as "from here to the next mark"; add marks before trimming |
| A combined-toggle lane shows each toggle's effect | `NPX2` (all three) hid a 1.5 ms arena-alone result and showed an `EndUploads` growth no single toggle reproduces | batch 50 | Benchmark opt-ins one per lane |
| Overlapped publication within one frame (Phase 4 step 1) | Flat: join wait 0.29 ms and `api_mutex` blocking eat the about 0.4 ms window | Phase 4 step 1 | Opt-in; superseded by the split |
| Deferred world intake and cell publish move work off the critical path | They lengthen recording, and `BeginFrame` waits for it more often | sessions 4–6 | Opt-in; no gain |


## Phase 4: the frame split

### Why

Phase 4 step 1 (opt-in `SE_VULKAN_OVERLAP_PUBLICATION=1`, `8588f01`) ran the
native publish as a job over voxels and lighting setup. Paired runs were flat
(orbit 5.45 vs 5.46, interior 5.455 vs 5.425). The overlapped window is only
about 0.4 ms, and the step lost most of it:
- the render thread waits 0.29 ms at the join (the job runs 0.66 ms);
- lighting-setup calls block on `api_mutex` behind the publish, 0.08 ms per
  frame;
- the job competes with the flare and billboard jobs for workers.

Only overlap across frames is sized to the 1.2 ms gap.

### The split

Run the second half of frame N on a dedicated build thread while the render
thread does the first half of frame N+1:
- **Apply(N+1)**, on the render thread: message intake, camera and settings,
  particles and decals, scene update, billboard collection, probe ambient, the
  stats overlay, sprite collection.
- **Build(N)**, on the build thread: the clipmap update, shadow fit, voxel
  plan, traversal, occlusion queries, voxels and flares, lighting setup,
  publication, `QueueWorld`, then submission (`BeginFrame`, sprites,
  `RaiseFrame`, `EndFrameAsync`).

**Projected cadence**, from the 2026-10-28 frame profiles: about
max(Apply + gap, Build).
- Apply is about 1.8 ms and the game-loop gap 0.8 ms.
- Build is about 5.2 ms on the slow 2026-10-28 box (including the hand-off),
  about 4.2 ms on the 2026-10-27 box state.
- So the result is 5.2 ms against 7.1 today on the slow box, and about
  4.2 ms on a normal box: at the 4.17 target with no margin.
- The 2026-10-27 estimate (Build 3.5–3.9 ms) did not include the hand-off
  cost.
- The cost is one extra frame of input latency, which gets measured.

### Hard constraints (2026-10-27 research)

*Game contract* (`VRage.Render/.../MyRenderThread.cs:237-340`,
`MySharedData.cs`):
- The order is `RenderFrame` → `BeforeRender` → `Draw` → `AfterRender` →
  `Present`.
- `AfterRender` commits `VisibleObjects.Write`. The game reads it for entity,
  cube-grid and skinned-animation decisions.
- `GetRenderFrame` swaps the billboard read buffers during message intake.
- Non-persistent messages are disposed when their frame retires.
- Render already draws the latest committed update frame, about one frame
  behind update, and frames can be redrawn.

*Managed sharing* (as found; how each was resolved is under "Milestones"):
- The traversal read the game's `MyActor` objects live (`WorldMatrix`,
  `IsVisible` with its parent walk, `IsDestroyed`, `Parent`). Message handlers
  mutate them, and `Remove` destructs them.
- `ActorData` mixed three kinds of fields:
  - message- and update-owned (model, appearance, instances, skin, `Bounds`,
    shadow pose, pass and cell ownership);
  - Build-owned (LOD state, `MainGroupKeys`, `SkinnedMeshes`);
  - written by both (`NewLod` and `NewViewCamera`, via
    `RemoveNativePassActor` and the fallbacks).
- The occlusion and shadow-group buffers split Begin/Query/End between update
  and Build, and depend on `FrameCounter`, which update increments.
- `BeginPacketCollection` / `BeginMainGroups` clear the `NativeBackend`
  packet lists Build appends to. All emission state is one instance.
- Messages rewrite the camera, frusta, `Environment.CameraPosition`,
  `RenderSettings`, `ShadowSettings`, `light` and `renderConfiguration`.
  `shadows.Update` rewrites the `ShadowSystem` arrays.
- Owner-thread checks: `VulkanTextureCache.RequireOwner` (and `ResidentOnly`
  assumed no removal during a fork), and `VulkanVoxels.RequireOwner`.
- `FrameProfiler` statics were unsynchronized. `RendererCpuProfiler` records
  only on its `BeginFrame` thread.

*Native* (`Native/src/renderer.cpp`):
- `api_mutex` keeps concurrent calls memory-safe, but the order checks assume
  one caller timeline:
  - between `begin_frame` and `end_frame_async`, `recording` is set and
    `upload_texture`, `set_lights`, `pass_register_*`, `cells_commit` and
    `remove` throw;
  - while a pass frame is prepared, `pass_scene.idle()` rejects resident
    mutation;
  - the deferred queue replays every caller's calls on whichever thread calls
    next.
- `renderer->error` was a non-atomic string.
- World sort reuse is `thread_local`, so sorting must stay on one thread.

### Approach

- Build reads only an immutable `FramePacket` plus state that Build alone
  owns. Apply writes only Apply-owned state and the next packet.
- The hand-off is one point on the render thread: join Build(N), swap, launch
  Build(N+1).
- Scene-mutating native calls move into Build's prologue, so the two threads
  never interleave the native scene order.
- Milestones 1–5 restructure the code on one thread with no behaviour change,
  each gated by the packet signature. Threading lands last.

The hand-off uses four patterns:
- **Applied copies with change flags**, folded at the latch (settings, scene
  state).
- **Queued op logs**, replayed at the latch or in Build's prologue (Build ops,
  environment, voxel messages).
- **Double buffers** (light frames, sprite batches, decal lists, particle
  submissions, actor frames by parity).
- **The thread-static `building` flag** for code both sides share (spot
  marks, billboard misting).

### Milestones

| # | Milestone | Status | Commit(s) |
|---|---|---|---|
| 1 | Apply/Build boundary on one thread | Done 2026-10-27 | `087c8fe` |
| 2 | Actor snapshot and ownership split (5 slices) | Done 2026-10-27 | `e8df786`, `5539809`, `dea6ac7`, `7523464`, `b4655bc` |
| 3 | Per-frame emission and outputs | Done 2026-10-27 | `cf2470e` |
| 4 | Subsystem ownership (5 slices) | Done 2026-10-27 | `c2fd7cc`, `7e5a200`, `3f860e0`, `7451130` |
| 5 | Native frame gate | Done 2026-10-28 | `4a62d78` |
| — | Trim the single-thread cost | Done 2026-10-28 (about 0.05–0.1 ms; the copy cost remains) | uncommitted |
| 6 | The build thread (`SE_VULKAN_FRAME_SPLIT=1`) | Implemented 2026-10-28; split-on gates pass; orbit pair only about 0.2 ms faster (Build now waits on the previous frame's recording) | uncommitted |
| 7 | Validate, measure, document | — | — |

**1. Apply/Build boundary on one thread.**
- `MyVulkanRender.Draw` became `ApplyFrame()` → latch → `BuildFrame(FramePacket)`
  → `PublishVisible()`.
- `FramePacket` (`ClientPlugin/Rendering/FramePacket.cs`) carries:
  - camera, frusta and projections;
  - light, texture multipliers, render configuration and device settings;
  - back buffer, gameplay frame, draw time, animation delta;
  - probe ambient;
  - the voxel clipmap inputs;
  - the particle-camera reset.
- The scene's own inputs latch through `VulkanScene.LatchFrame` (`Next` →
  Build fields, `Environment` → `FrameView`).
- `SE_VULKAN_APPLY_BUILD_ORDER=0` (or the `ApplyBuildOrder` property) runs
  the scene update at its old place, for the same-process lane.
- Gate: packet signature identical in the same-process lane; suites and VAL
  clean.

**2. Actor snapshot and ownership split.**
- Slice 1: `ActorFrame` in two parity slots per actor, refreshed in Apply for
  dirty actors. Build reads it instead of `data.Actor.*`.
- Slice 2: scene containers are Build-owned (candidate tree, groups, native
  ownership); Apply queues `BuildOps`; dirty marks fold at the latch;
  `buildActors` mirrors `actors` by replaying adds and removes.
- Slice 3: per-actor fields through `W()` into `ActorData.A`, folded at the
  latch; materials copy-on-write; LOD resets as latch events; queued mesh
  releases.
- Slice 4: message-owned scene state (HSV, bloom, skybox, LOD settings,
  highlights, instance buffers, frame counter) as Applied copies.
- Slice 5: light snapshots, probe ambient in Apply, billboard parents,
  `CharacterStatus`.
- Verify mode `SE_VULKAN_FRAME_PACKET_VERIFY=1`: 0 mismatches in every slice
  (about 47–64 million checks per world).

**3. Per-frame emission and outputs.**
- Packet collection moved into Build. Apply's only emitters, particles and
  decals, hand their output over at the latch, so no backend-wide
  double-buffered `FrameEmission` was needed.
- Sprites are double-buffered (`main` / `buildMain`).
- The visible set goes to `frameVisible`, which `PublishVisible` copies into
  `VisibleObjects.Write` before `Draw` returns. Visibility lags one frame.

**4. Subsystem ownership.**
- Slice 1: probe, shadow and postprocess settings use Applied copies and
  `Latch()`.
- Slice 2: environment message ops are queued and replayed at the latch.
- Slice 3: voxels.
  - Messages are queued, and the clipmap update runs at Build's start (or on
    the render thread when no frame is built, for the loading screen).
  - `BindOwnerThread` moves the owner check.
- Slice 4: the texture cache and media.
  - Off-owner cube and neutral misses queue an owner request, which
    `DrainPendingLoads` serves.
  - Closed-video textures are freed at the hand-off.
- Slice 5: `FrameProfiler` per-thread laps. `RendererCpuProfiler`'s Build
  lane waits for milestone 6.
- Particles and the LCD `RenderOffscreenTexture` stay in Apply.

**5. Native.**
- `begin_frame` records the frame owner.
- With `SE_VULKAN_FRAME_GATE=1` (or `SE_VULKAN_FRAME_SPLIT=1`), calls from
  another thread wait for the frame to close instead of throwing.
- Scene calls from a non-owner thread are counted, and
  `SE_VULKAN_SCENE_CALL_ASSERT=1` prints the first.
- `sevk_last_error` is per-thread.
- Census (pipeline report, orbit): 0 off-owner calls, 0 gate waits and 0
  off-owner scene calls, gate on or off.
- InteropTests covers the gate with two threads.

**6. The build thread** (`SE_VULKAN_FRAME_SPLIT=1`, opt-in). Implemented
2026-10-28; the first design notes below are kept, and "As built" records what
landed.

*As built:*
- **`FrameBuildThread`.** A long-lived thread ("Vulkan frame build",
  AboveNormal) with one frame in flight. `Join` rethrows Build's failure on the
  render thread, where `Fatal` reports it.
- **`MyVulkanRender.DrawSplit`.** In order:
  1. `ApplyFrame` (Apply opens no upload batch);
  2. `JoinBuild` (phase `BuildJoin`);
  3. `PublishVisible` (Build(N−1)'s visible set);
  4. `Handoff` (the latches, plus the voxel message latch, the debug-line
     latch and deferred model releases);
  5. `SampleDiagnostics`, once a second, with Build joined;
  6. launch `BuildOnThread`.

  `BuildOnThread` opens Build's upload batch, runs `BuildFrame`, then
  `SubmitFrame` (guest frame callback, `EndFrameAsync`, shadow commit,
  profiler commit). `Present` returns at once, unless Draw built the frame
  itself.
- **Frames built on the render thread:** a frame with a screenshot request
  (Present submits it, so the readback holds it), and the no-frame path (it
  joins first).
- **Joins before** `UnloadData`, `SwitchRenderSettings`,
  `SwitchDeviceSettings`, `ApplySettings` and device disposal.
- **Upload batch.** The native batch is global and a frame must not begin
  while one is open, so Build owns it: opened at Build's start and flushed
  before `BeginFrame`. Apply uploads made during that window join it and are
  flushed before the frame; later ones submit immediately.
- **The native frame gate** turns on with `SE_VULKAN_FRAME_SPLIT=1`. Apply's
  ordinary native calls during Build's open frame wait instead of throwing.
- **Races found and closed** (all identical on one thread):
  - Build read `ActorData.Bounds`, which Apply's spatial refresh writes, in the
    traversal, group bounds, cells, pass input, guest depth and guest scene.
    They now read `ActorFrame.Bounds`, which verify mode proves equal.
  - Guest shadow inputs iterated Apply's `actors`; they now iterate
    `buildActors`, in the same order.
  - The voxel message queue was appended by Apply and replayed by Build. With
    the split, the hand-off latches it (`LatchedMessages`).
  - Debug lines were added by Apply and drawn by Build. With the split, the
    hand-off latches them.
  - `PutRuntime` released a replaced model's meshes during Apply. With the
    split, the release waits for the hand-off (`DeferReleases`).
  - The guest view clip was set during message intake. Build now sets it when
    the frame's far plane changes.
  - `FrameProfiler.Capture` (the stats overlay, in Apply) now locks against
    `Commit` (Build).
- **Profilers.**
  - `RendererCpuProfiler` has a Build lane (`BeginBuild`/`EndBuild`, phase
    `BuildThread`), folded into the render thread's next completed frame.
  - `FrameProfiler` keeps a lap buffer per frame that the build thread adopts
    (`Current`/`Adopt`/`Detach`). `FrameProfilerTests` covers the hand-over.
- **Split mode forces** `ApplyBuildOrder` on and verify mode off. Verify
  compares Build's copies with live state, which moves on under a concurrent
  Apply.
- **Anomaly:** documented in `Docs/Vulkan.md` ("Callback thread") and
  `wiki/Framework-gaps.md` ("Vulkan frame callbacks on SE-Vulkan's build
  thread"), with the contract proposal.

*First split-on runs (2026-10-28) found five more races, all split-only and
all fixed:*
- **Voxel textures.** Voxel materials load their textures synchronously, but
  the texture cache loaded only on its owner (Apply), so Build got 0 and
  threw "Voxel DDS missing".
  - The build thread is now a second loading thread (`BindLoaderThread`).
  - Loads re-check the cache under `loadGate`, and handles come from an
    atomic counter.
  - The owner's unload, generated-texture and clear paths take the same lock.
- **Build ops.** `buildOps` was appended by Apply while `FlushBuildOps`
  iterated it ("Collection was modified"). The latch now moves it to
  `latchedBuildOps`.
- **Billboards.** `Collect` (Apply) cleared and refilled the lists Build
  emits. Collection now goes into `collected*` buffers, swapped in at the
  latch; the old-order lane collects inside Build and latches at once.
- **The model cache.** It is a plain `Dictionary`, loaded by Apply
  (`SetModel`, `PutRuntime`) and by Build (`GetLod` misses). Loads now lock,
  and mesh ids are atomic.
- **`OldResources`.** Apply registers identities (preload messages, runtime
  models) while Build resolves identities, variants, layouts and techniques.
  Every use now takes its lock.

*Harness sampling.* The harness read present-flow and performance stats on
every frame in its Present prefix, on the render thread. Under the split those
reads wait for Build's open frame and recording: 3,820 gate waits, 19.8 s,
about 2.7 ms per frame, 95% from telemetry. It now samples on the build thread
(a `SubmitFrame` prefix) right before the native launch, the same point as the
pipelined Present prefix; pinned baselines without the hook are unaffected.

*Results (orbit, unpaired, slow box):*
- Split census run `Vulkan-58c250fc…`:
  - present interval 6.28 ms, P95 8.98;
  - gate waits 72 (0.36 s, almost all the once-a-second diagnostics sample);
  - no exceptions;
  - every off-owner scene call comes from the frames the split builds on
    the render thread (screenshot frames, with Build joined).
- Earlier split runs, before the harness fix: CpuDetail `Vulkan-fe0f8f36…`
  had Build 5.22 ms on its thread, and Apply's Draw 2.23 ms including a
  0.62 ms join.
- VAL with the split `Vulkan-87398742…` and without `Vulkan-ed4c45ce…`: 0
  validation errors.
- Single-thread gates after the milestone 6 changes:
  - verify 0 mismatches, orbit 32.9 and interior 48.7 million checks;
  - the lane's `packets.sig` identical in all three phases;
  - the suites pass.
- **Same-session pair (2026-10-28, orbit and interior interleaved, plus
  DX11).** The box ran about 25% slow (DX11 5.50 / 5.52 against 4.41 / 4.68
  earlier this session).

  | | Orbit present ms | Interior present ms |
  |---|---|---|
  | Single thread | 8.30 (`Vulkan-6afa3f45…`), 8.30 (`Vulkan-5b6598c0…`) | 6.32 (`Vulkan-90b2c461…`), 8.38 (`Vulkan-6af556b8…`) |
  | Split | 8.11 (`Vulkan-97d03693…`), 8.07 (`Vulkan-24e7f4bb…`) | 8.08 (`Vulkan-b821d718…`), 8.03 (`Vulkan-9152ccf7…`) |

  - **Orbit:** the split is about 0.2 ms (2.5%) faster.
  - **Interior:** unusable. The box state changed between the two
    single-thread runs (6.20 → 8.20 ms render-thread total).
  - No exceptions in any run.
- **Why the overlap mostly vanishes (corrected by the census pair).**
  - The render thread loops Draw→Present→Draw with about 0.03 ms between
    frames, and the split's Draw is Apply plus the join. So Build(N) sets the
    cadence.
  - First guess, now disproved: Build waits on the previous frame's
    recording. The census pair (`NPCP` / `NPXCP`, same session) counts zero
    recording waits and zero early-release waits on the build thread. The
    extra 0.5 ms per frame of recording waits falls on Apply's ordinary
    calls on the render thread, which then idles 4.1–4.6 ms in `BuildJoin`
    anyway.
  - What happens instead: `BuildThread` runs 6.48 / 5.81 ms, about the whole
    single-thread frame (6.43 / 5.86). Build shed Apply (about 1.4 ms) and
    grew by the same amount.
    - Its phases run 10–25% slower (Scene 2.85 → 3.63, QueueWorld 0.72 →
      0.95, publication 1.28 → 1.51).
    - So do the recording's own sections (Prepare 1.00 → 1.39, Main
      0.95 → 1.27).
    - `EndUploads` stands out at 0.15 → 0.70 ms.
  - Working hypothesis: CPU contention. Render, build, recording worker,
    7 job workers, 2 stage threads and the game update thread are hot at
    once on 16 logical CPUs. The recording used to overlap Apply; now it
    overlaps Build and Build's jobs.
  - Present cadence in that pair: single 6.53 / 5.95, split 6.84 / 6.13 (the
    box drifted faster). No gain, no exceptions. Details: attribution doc,
    "The census pair".
- **Contention test (same day):** job-pool contention is ruled out. At 5
  job workers the split runs 6.05 / 6.08 ms against 6.11 / 6.02 at 7, with
  Build's phases unchanged.
  - The native upload flush is cheap: work on 6–10% of frames, a present
    join of 0.13 ms per frame, deferred replay unchanged. So `EndUploads`'
    0.3–0.5 ms growth is a managed-side stall (GC or preemption at the
    P/Invoke return) or uncounted lock/gate time.
  - The recording waits belong to a third thread (zero owner waits even
    single-threaded), off the critical path.
  - In this batch the split was 0.46–0.55 ms faster than the one
    single-thread run, but that run came first, so it is not a clean pair.
- **Next (diagnosis before more code):**
  - time `sevk_end_uploads` natively from entry to exit against the managed
    phase;
  - count GCs per frame in both modes;
  - a clean interleaved single/split pair at the current state.

*Residual risk:* the audit covered Build's reads of per-actor bounds, the
actor and light containers, the subsystem queues and the native call
contract. Other Apply-written fields that Build reads would be wrong-frame
reads, not crashes. Candidates are the remaining `ActorData` fields that
messages write outside `W()`. The split-on gates (VAL, the census's
off-owner scene calls, gameplay soak) are how they would show.

*Design notes (2026-10-27):*
- A dedicated long-lived thread (not the job pool) at AboveNormal priority,
  with a two-slot hand-off: semaphores plus the `FramePacket`.
- `Draw(N+1)`:
  1. `ApplyFrame()`;
  2. join Build(N) (`Phase.BuildJoin`);
  3. copy visible objects, run deferred destructions;
  4. latch (swap the packet and outputs);
  5. launch Build(N+1);
  6. return.
- `Present` keeps only render-thread duties; `CompleteCaptures` joins the
  build whose frame holds the readback.
- Join synchronously on unload, device-settings changes and swapchain resize.
- Switch the frame gate on by default with the split (the census shows it
  never fires on one thread).
- `RendererCpuProfiler` gets a Build lane bound to the build thread.
  `FrameProfiler` needs per-frame Pending buffers so Build(N) and Apply(N+1)
  laps do not mix.
- Guest callbacks (`RaiseScene`, `RaisePreparation`, `RaiseFrame`) run on the
  build thread every frame.
  - This is Vulkan-only; DX11's frame is owned by the game's `MyRender11`.
  - Document it in Anomaly `Docs/Vulkan.md` and `wiki/Framework-gaps.md`,
    with the proposed contract "one thread per frame, not necessarily the
    game render thread".

**7. Validate, measure, document.**
- Gates with the split on, plus the user's plugin set (`NPU`/`INU`).
- Input latency: a camera-message-to-present timestamp, with and without the
  split and against DX11.
- One batch: NP vs NP+split on both worlds, both orders, plus DX11, with a
  CpuDetail run for the Apply, Build and join phases.
- Decide the default from the data.

### The single-thread cost (2026-10-28)

**End-of-milestone benchmark** (interleaved, same native DLL on both sides;
the pre-split plugin is built from `8588f01` and stored in
`TestResults/PreSplit-8588f01-binaries`):

| Run | Orbit present ms | Interior present ms |
|---|---|---|
| Current (1st) | 7.09 | 8.06 (disturbed run) |
| Pre-split (1st) | 6.54 | 5.86 |
| Pre-split (2nd) | 6.40 | 5.87 |
| Current (2nd) | 7.12 | 6.51 |
| DX11 | 4.41 | 4.68 |

**CpuDetail pair**, same session, run 1 of each (current
`Vulkan-12613c83…` against pre-split `Vulkan-c7f79605…`):
- Draw: 7.57 against 7.08 ms (+0.49).
- The new work, measured by phases added for this purpose:

  | Phase | ms/frame | What |
  |---|---|---|
  | `BuildPrologue` | 0.26 | `BeginPacketCollection` and the particle and decal submit. Particle drawing dropped about 0.13 because its native calls moved here, so about 0.13 is new. |
  | `Handoff` | 0.18 | `LatchFrame` (settings, folds of actor fields, scene state and dirty sets) and the subsystem latches |
  | `LightSnapshot` | 0.15 | every light's owner state and two data structs, every frame |
  | `ActorSnapshot` | 0.09 | changed actors, refreshed in both parity slots |

- Everything else is within ±0.1 ms. Occlusion and its bounds dropped about
  0.18 ms.
- Native waits are not the cause.
- Allocation is unchanged (about 300 KB per frame).

**Trims (done 2026-10-28, uncommitted)**:
- **Light snapshot.** Copy the data structs only when `UpdateData` ran
  (`DataRevision`), and the owner matrix only when its `WorldMatrixIndex`
  moves. `UpdateData` is the only writer: the base `MyLightComponent` never
  writes `m_data`.
- **Actor snapshot.** An actor marked last frame but not this one copies its
  other slot instead of being recomputed. Verify mode guards it.
- **Dirty-set folds.** Swap Build's and Apply's native-pass, cell, cell-pose
  and GPU dirty sets when Build's is empty, instead of `UnionWith`. Apply
  sets only Add and Clear, so contents and iteration order are the same.
- **Sub-phases.** `HandoffFields`, `HandoffSceneState`, `HandoffDirty`,
  `HandoffLatches`, `PrologueCollection` and `PrologueSubmit` price the
  remainder.

**Results after the first two trims**:
- Verify run `Vulkan-8695fa3f…`: 0 mismatches in 42.9 million checks.
- CpuDetail run `Vulkan-ed583bd9…`, a single run (the box was faster than
  during the pair, so only the new phases compare):

  | Phase | Before | After |
  |---|---|---|
  | `LightSnapshot` | 0.150 | 0.061 |
  | `ActorSnapshot` | 0.087 | 0.061 |
  | `Handoff` | 0.183 | 0.145: fields 0.048, dirty sets and settings 0.085, latches 0.001, scene state 0.000 |
  | `BuildPrologue` | 0.257 | 0.227: `BeginPacketCollection` 0.196, submit 0.025 |

- **`BeginPacketCollection` is not new.** Before the split it ran at the same
  point of Draw outside any phase, so its 0.2 ms was simply unmeasured.
  Trimming it (its `ClearPassFrame` native call and list clears) is ordinary
  Build work.
- **The split's own cost** is now about 0.27 ms (snapshots 0.12, hand-off
  0.145, moved submit 0.025), down from about 0.42 ms. The dirty-set swap
  (built after this run) targets most of the 0.085.

**Trim gate batch (2026-10-28, with all three trims):**
- **Gates:** all pass.
  - Verify: 0 mismatches. Orbit `Vulkan-f308b16d…` 32.9 million checks,
    interior `Vulkan-d5146466…` 44.8 million.
  - Same-process lane `Vulkan-65d1bd48…`: `packets.sig` identical in all three
    phases.
  - GPU suites under validation pass, plus DepthGeometry 41, GuestDepth 1,900,
    Voxel 51, Sprite 29 and the Interop gate check (19).
  - VAL `Vulkan-2e44aabc…`: 0 errors.
- **CpuDetail pair**, same session. The box ran about 13% slower than for the
  first pair; the second pre-split run was disturbed (P95 16.7).

  | | Current 1 | Pre-split 1 | Current 2 |
  |---|---|---|---|
  | Present ms | 9.63 | 9.12 | 9.76 |
  | Draw ms | 8.57 | 8.09 | 8.69 |

  The split's own phases in run 1:

  | Phase | ms/frame |
  |---|---|
  | `Handoff` | 0.163: fields 0.075, dirty sets and settings 0.074 |
  | `ActorSnapshot` | 0.096 |
  | `LightSnapshot` | 0.079 |
  | `PrologueSubmit` (moved from `ParticleDraw`, which dropped 0.073) | 0.036 |

  Smaller Apply-side growth: shadow poses +0.045, spatial index +0.024,
  messages +0.023. That is the cost of the `W()`, `NoteActorFrame` and
  dirty-set marks.
- **Reading.** The Draw gap stays about 0.49 ms, 6% here against 7% in the
  first pair. The trims recovered roughly 0.05–0.1 ms (normalized), within
  the noise of single pairs.
- **What is left is the copy cost itself.** It is spread over a dozen small
  sites, none above 0.1 ms after the trims.
- **Further trimming is low-value.** Overlap (milestone 6) moves about 2.3 ms
  of Apply off Build's critical path, against a 0.5 ms copy cost.
- **Next:** milestone 6. Re-check the remaining cost only if the split stays
  off by default.

### Why the split does not pay yet (analysis, 2026-10-28)

Sources: the census pairs `m6g` (single / split / split / single) and `m6h`
(single, then split at 7 / 5 / 5 / 7 job workers), CpuDetail rows, and a
code read of `DrawSplit`, `BuildFrame`, the native call wrappers and the
upload batch. Numbers are from the slow 2026-10-28 box, `m6h` unless noted.

**The frame under the split.**

```
render:  Apply(N+1) 1.55 | BuildJoin 4.25 ................| serial 0.25 | Apply(N+2) ...
build:   ............ Build(N) + submit 5.80 ..............| idle         | Build(N+1) ...
cadence = Build thread + the serial hand-off = 5.80 + 0.25 ≈ 6.05 ms
```

- **Build is the whole critical path.** The render thread idles 4.1–4.6 ms per
  frame in `BuildJoin`. Nothing on the render thread (Apply, its native
  waits, the game loop) gates the cadence.
- **The decomposition.** Single-threaded, the same work is:
  - Apply about 1.55 ms: Messages 0.43, Particles 0.37, UpdateScene about
    0.65, billboard collection, probe ambient, stats overlay;
  - Build about 4.25 ms;
  - submission about 0.6 ms (Present: guest `RaiseFrame`, `EndFrameAsync`,
    profiler commit, harness sample).

  So Build plus submission is about 4.85 ms single-threaded and 5.80 ms on
  the build thread.

**Against the design budget: a double failure.** The split was designed to
land at about Apply 2.0 and Build 3.6–3.9 ms on a quiet box (cadence under
4.17), and to beat single-threaded, not match it.

| | Design, quiet box | Design, scaled ×1.2 for this box | Measured here |
|---|---|---|---|
| Apply | about 1.6 + 0.8 game-loop gap | about 2.9 | 1.55 + 0.04 gap |
| Build + submit | 3.6–3.9 | 4.3–4.7 | 5.80 |
| Cadence | 3.6–3.9 | 4.3–4.7 | 6.02–6.11 (single thread 6.57) |

The ×1.2 factor is the pre-split build at 6.4–6.5 here against 5.3–5.5 on
a quiet day.

- **Failure 1: Build is about 1.1–1.5 ms over budget.** Of that:
  - about 0.6 ms predates the build thread: the single-thread Build + submit
    (about 4.85) already sat at or above the top of the scaled budget, after
    milestones 1–5 added about 0.5–0.6 ms of copies;
  - about 0.95 ms comes from running on the build thread (loss 2 below).
- **Failure 2: the overlap buys almost nothing.** It was meant to hide Apply
  plus the 0.8 ms game-loop gap, about 2.4 ms. The gap is gone under the
  split (0.04 ms), Apply is only 1.55 ms, and loss 2 plus the serial hand-off
  eat most of what's left.
- **The bar for every fix below** is Build ≈ 3.6–3.9 ms on a quiet box, not
  parity with single-threaded.

**Three losses, in order of size.**

1. **The split is lopsided (structural).** Apply is about 25% of the frame,
   so the best the split can do, with no other loss, is take Apply off the
   critical path: about 1.55 ms here, cadence about 4.85 + 0.25 = 5.1
   against 6.5. Everything else in this list eats into that 1.55.
2. **Build runs about 0.95 ms (20%) slower on the build thread.**
   - **`EndUploads`, 0.14 → 0.44–0.66 ms (about 0.4 of it).** It is not the
     native flush: work on 6–10% of frames, a present join of 0.13 ms per
     frame, submit about 0, deferred replay unchanged, no recording wait on
     the owner, and `api_mutex` lock waits of 0.01 ms per frame. Still open:
     the native entry-to-exit timer and the GC report (pending the pair).
     A code-read difference that could matter: single-threaded, the upload
     batch opens before Apply (`Draw`), so Apply's uploads (texture drain,
     media, message-driven meshes) join the batch. Under the split only
     Build opens it, so Apply's uploads are submitted on their own outside
     any batch, or land in Build's batch at random.
   - **Broad 5–15% on Build's managed phases (about 0.5).** Scene's Build
     half, QueueWorld 0.71 → 0.82, publication 1.28 → 1.40, Handoff 0.08 →
     0.15, Clipmaps 0.07 → 0.13. The recording's own sections slow too
     (Prepare 0.97 → 1.08, Main 0.92 → 1.03).
     - Ruled out: job-pool size (5 against 7 workers, no change); waits on
       the recording (zero owner and scene waits); the new managed locks
       (`GetLod`'s fast path takes none, and the `OldResources` locks run
       only in actor setup and messages).
     - Left, untested: (a) Apply now runs concurrently with Build's first
       1.5 ms, on top of the recording and its jobs, which already
       overlapped Build single-threaded (the recording's ~3.7 ms of laps
       covers Apply and the start of Build either way). That is one more hot
       thread, sharing cores (SMT siblings) or boost clocks. (b) The data
       Apply writes (actor and light snapshots, latches, scene fields) is
       read by Build on another core: cache misses and false sharing on
       shared objects such as `VulkanScene`, instead of hits on one core.
       (c) GC suspensions with two allocating threads (allocation is about
       170 KB per frame on the render thread; about 4% of frames overlap a
       GC in both modes).
3. **The serial hand-off, about 0.25–0.3 ms per frame.** Present cadence
   minus `BuildThread` is 0.31 / 0.29 ms. Between Build(N) finishing and
   Build(N+1) starting there are:
   - two thread wakes (the render thread out of `BuildJoin`, the build thread
     out of its start semaphore), each a kernel wake under load;
   - `PublishVisible` (the visible set into the game's buffer);
   - `Handoff` (0.15–0.17 ms under the split);
   - `SampleDiagnostics` (once a second);
   - `FrameProfiler` detach and launch.

   None of it overlaps anything.

**The interleaved pair (`m6i`, CpuDetail + census + GC report).** Present
cadence: single 6.70 / 6.41 ms, split 6.28 ms. The second split run (9.22)
is discarded: every phase ran about 50% slow, single-threaded ones included,
so the box was busy.

| | Single | Split |
|---|---|---|
| Native `end_uploads`, µs per call, entry to exit | 199 / 166 | 421 / 448 |
| Managed `EndUploads`, ms | 0.27 / 0.19 | 0.52 |
| Gen0 GCs, total pause in 30 s | 191 / 199, 493 / 482 ms | 205, 548 ms |

- **`EndUploads` is native time, not a managed stall.** In the product
  configuration it is `EndOpenUploads`, at the very end of Build, right
  before `BeginFrame` (the mid-Build `EndUploads` belongs to the opt-in GPU
  scene). It is Build's first ordinary native call, so it absorbs any wait
  for recording(N) before frame N+1 can begin. `BeginFrame(N+1)` cannot
  start until recording(N) ends, since there is one recording slot.
- **Open contradiction.** The recording's laps sum to about 4.1 ms, while
  Build(N+1) takes about 5 ms to reach `EndUploads`, so Build should not
  wait. Either the recording's wall time is longer than its laps (worker
  gaps, waits on jobs, fences or swapchain acquire), or the zero
  "owner waits" counter (keyed on the thread that last began a frame) is
  wrong.
  - New census to settle it: `WaitThreads` (recording waits keyed by the OS
    thread description; .NET publishes `Thread.Name` there), and
    `EndUploadInFlight` / `EndUploadInFlightUs` (`end_uploads` calls that
    began with a recording in flight).
- **GC is not split-specific** (about the same in both modes). It is a P95
  problem in its own right: about 6.5 gen0 collections a second at about
  2.5 ms each, from about 51 MB/s allocated process-wide.
- **`frame-profile.json` `Total` is latency, not double counting.** It runs
  from Apply's start to Build's commit: 11.7–13.8 ms under the split against
  about 6.5 single-threaded, the designed extra frame of CPU-side latency.

**Fixes in progress:**
- **Fix A, serial hand-off (built, not yet measured).** `DrawSplit` swaps
  Build(N)'s visible set into `publishedVisible` and publishes it after
  launching Build(N+1). The serial section is now scoped as the
  `SplitSerial` phase, so the remaining two-thread-wake cost is cadence
  minus `BuildThread` minus `SplitSerial`.
- **Next, from the census:**
  - if Build waits on the recording, the recording's wall time is the pacing
    chain (recording(N) → BeginFrame(N+1)), and the fixes move to the
    recording: shorten its critical section, release earlier, or give it a
    second slot;
  - if not, find the native wait inside `end_uploads` directly.

**The census settles it (`m6k`, 2026-10-28): the recording paces the split.**

| | Single `Vulkan-3962f837…` | Split `Vulkan-88d9fb24…` |
|---|---|---|
| Present ms (slow, noisy batch) | 7.23 | 8.35 |
| Recording, launch to end, ms | 5.54 | **6.28** |
| Recording laps Prepare / Main / Spot / Tail | 1.22 / 1.15 / 0.88 / 0.66 | **1.98 / 1.75 / 1.17 / 0.86** |
| Recording waits on the build thread (`WaitThreads`) | — | 0.56 ms per frame |
| `end_uploads` starting with a recording in flight | 9% of calls, 1.0 ms each | **29% of calls, 1.43 ms each** |

- **The first diagnosis was right after all.** Build waits for recording(N)
  at its last upload flush before `BeginFrame(N+1)`, since one recording slot
  serializes them. The "zero owner waits" counter that seemed to rule this
  out keyed on `frame_owner`, which `end_frame` clears between frames. It now
  keys on the persistent `scene_owner`.
- **The recording is the stage that paces the frame, and the split slows
  it by about 2 ms of laps.** The growth is in its job-parallel sections
  (Prepare, Main, Spot).
- **Cause: job-priority inversion.** The pool runs High jobs first.
  - Build's publication fan-out (PassView and CellView, through
    `native_pass_view_jobs` `parallel_for`) is High.
  - The recording's fan-out (Cascade, Upload, Spot in `spots.inc`, and the
    PassPack background packing it consumes) is Normal.
  - Single-threaded, the recording overlapped Apply, which submits no jobs.
    Under the split it overlaps Build(N+1), whose High publication jobs jump
    ahead of it.
- **Fix B (built, not yet measured).** Under `SE_VULKAN_FRAME_SPLIT=1`,
  `job_system.h` makes jobs submitted from the pacing stage High and every
  other job Normal, in submission order. The pacing stage is the recording
  thread and jobs it fans out (inherited through `tl_pacing_stage`).
  PassPack(N), queued before Build(N+1)'s publication, keeps its place.
  `SE_VULKAN_RECORD_JOBS_FIRST=0` restores the per-call priorities for A/B.
  Off the split, nothing changes.

**Fix B A/B (`m6l`, 2026-10-28): no gain; now opt-in.** Interleaved split
runs with fix B on and off, then single.

| Run | Present | BuildThread | Recording launch → start / record | Build waits on recording | Recording laps P / M / S |
|---|---|---|---|---|---|
| Split, B on `Vulkan-5f5d3a5a…` (first run) | 6.76 | 6.46 | 0.44 / 4.94 | 0.65 | 1.33 / 1.22 / 0.96 |
| Split, B off `Vulkan-628015df…` | 6.09 | 5.80 | 0.40 / 4.59 | 0.69 | 1.09 / 1.03 / 0.73 |
| Split, B off `Vulkan-3f0b1ed1…` | 6.21 | 5.92 | 0.42 / 4.68 | 0.68 | 1.14 / 1.06 / 0.77 |
| Split, B on `Vulkan-a1f26377…` | 6.31 | 6.02 | 0.42 / 4.68 | 0.67 | 1.14 / 1.07 / 0.78 |
| Single `Vulkan-8d4102c1…` | 6.19 | — | 0.38 / 4.27 | 0.22 | 0.99 / 0.96 / 0.74 |

- **Job priority is not the cause.** With fix B the recording's laps don't
  change (1.14 / 1.07 against 1.14 / 1.06). The large lap growth in `m6k`
  (Prepare 1.98) was the busy box, not the split. Fix B is now opt-in
  (`SE_VULKAN_RECORD_JOBS_FIRST=1`).
- **The corrected model.** The split runs two chains of about equal length
  that serialize at `BeginFrame`, because there is one recording slot:
  - the recording: launch → start 0.4 ms (stage-thread wake and `api_mutex`
    hand-over, dead time in both modes), then 4.6–4.7 ms recording, about
    0.35 ms (8%) more than single-threaded;
  - Build: about 5.1–5.2 ms of its own work (`BuildThread` minus its
    0.68 ms wait), against about 4.6 for the same work single-threaded.

  Build(N+1) reaches its last upload flush before `BeginFrame` about when
  recording(N) ends, and waits on 42% of frames (0.68 ms per frame on
  average). Cadence is about Build + its wait + serial (0.18–0.19 ms) +
  wakes, about 6.1–6.2 ms: parity with single-threaded (6.19).
- **What has to shrink, all of it.** Removing the wait alone gives about
  5.4 ms. Also removing Build's split inflation (about 0.5–0.6 ms) gives
  about 4.8. The scaled budget (4.3–4.7 on this box) needs both chains
  under it:
  1. **Recording launch latency (0.4 ms of dead time per frame).** The
     stage thread wakes and then takes `api_mutex` only after the
     launching call returns. Hand the lock over directly or spin the stage
     thread, and measure.
  2. **Build's split inflation (about 0.5–0.6 ms).** Still uncharacterized;
     it is spread at 5–15% across Build's phases. Next tests: core
     placement (ideal processor) and false sharing on shared scene objects.
  3. **The one-slot serialization.** Let `BeginFrame(N+1)` start while
     recording(N) runs (a second frame-recording slot). This is the deeper
     pipeline: a large native change, but it turns the two chains from
     serial at `BeginFrame` into overlapped.
  4. **Make both chains cheaper.** Build: 1b incremental publication and 1a
     parallel traversal. Recording: its 4.3 ms single-threaded already sits
     at the top of the budget.

**The real gap is larger: count from pre-Phase 4.** The current
single-threaded baseline already carries milestones 1–5's restructure cost
(about 0.5–0.6 ms of copies, snapshots and latches; the trims recovered only
0.05–0.1). Against the pre-Phase 4 build (pins `PS0` / `INPS0`), Build on its
thread is about 1.0–1.2 ms slower for the same work: restructure cost plus
separate-thread inflation. Both have to come back.

**Fix round 2 (2026-10-28, built, not measured; the user asked for all of it
before the next benchmark).**
- **Item 1, recording launch latency.**
  - Native `Stage` threads spin up to `SE_VULKAN_STAGE_SPIN_US` (300 µs under
    the split, otherwise 0) before blocking, so a relaunch inside the window
    skips the kernel wake.
  - Census `LaunchLapUs[5]`: launch → stage entry, `api_mutex` hand-over,
    `settle_cells` (deferred world queue and cell emission), the early
    release (render-group and stream copies, probe plan), and the
    end-of-recording relock.
  - Most of the 0.4 ms "launch → record" was recording prologue work, not
    just the wake. The laps show how it splits.
- **Item 2, Build inflation.**
  - **2a, direct scene commits.** `call_deferrable_scene` runs as a scene
    call once the recording has early-released, instead of copying, queuing
    and replaying. Under the split Build runs almost entirely while the
    previous frame records, so every pass and cell commit took the queue
    (`NativePassCommits` +44%, `CellCommits` +67%, `BuildPrologue` +44%).
    `SE_VULKAN_DIRECT_SCENE_COMMITS=0` restores the queue.
  - **2b, priority.** The build thread runs Highest (it is the critical
    path); `SE_VULKAN_BUILD_PRIORITY=above` restores AboveNormal.
  - Phase growth split against single-threaded (`m6l`):
    - `EndUploads` +0.46 ms (the recording wait, item 3);
    - native-call phases +40–80%;
    - Handoff copies +70–95% (cross-core reads of what Build wrote);
    - managed loops +10–20%.
  - CPU: i9-9900K, 8 identical cores (16 threads), one L3, so not a hybrid
    E-core effect.
  - Still open: the restructure cost itself (snapshots, latches, hand-off
    copies) and the remaining managed-loop inflation.
- **Item 3, the one-slot serialization (bounded version).**
  - **Recording relock fairness.** The finished recording re-takes
    `api_mutex`, which Build's back-to-back scene calls held. `std::mutex`
    isn't fair, so scene calls now yield while `relock_wanted` is set.
  - **Not done, a true second recording slot.** `begin_frame` frees sets
    from the shared descriptor pool, resets slot command pools, reads
    global lights and shadow cascades, and collects GPU results; `end_frame`
    reads every subsystem's single-instance frame state (lights, spots,
    particles, billboards, decals, highlights, environment, foliage,
    transparency, shadows, sprites, lines, guests). Only the world/pass side
    is double-buffered (early release). A second slot means double-buffering
    all of it plus two-thread-safe descriptor and command pools: a
    multi-step refactor that needs a gate at each subsystem.
- **Item 4, cheaper Build (1a / 1b): not started.** 1a's candidate planning
  is already parallel (`PlanCandidates`); what remains is the serial emission
  loop with its shared state (scopes, batching dictionaries, prepared
  replay, glass, decal and highlight lists, the visible set, occlusion
  queries). 1b is the native per-actor batch cache in pass publication.
  Each has its own correctness gates (packet signature, pass-scene unit
  tests, glass/decal/highlight counts).

**Pricing fix round 2 (`m6m`, 2026-10-28).** VALX clean (validation on, 0
errors). Then split / single / pre-split / pre-split / single / split.

| Run | Present ms | P95 ms | BuildThread | Build waits on recording | Recording |
|---|---|---|---|---|---|
| Split `Vulkan-e660f081…` | 5.45 | 7.86 | 5.20 | 0.71 ms/frame | 4.84 |
| Single `Vulkan-63ec766c…` | 6.67 | 9.97 | — | 0.16 | 4.79 |
| Pre-split PS0 `Vulkan-3a2d28e9…` | 5.40 | 7.65 | — | — | — |
| Pre-split PS0 `Vulkan-79e14d2e…` | 5.40 | 7.66 | — | — | — |
| Single `Vulkan-aeb0debb…` | 5.94 | 8.22 | — | 0.14 | 4.55 |
| Split `Vulkan-b0d05ba0…` | 6.27 | 9.60 | 5.98 | 0.81 | 5.23 |

- **Averages over the symmetric order:** split 5.86, single 6.31,
  pre-split **5.40**.
  - The split now beats current single-threaded by about 0.45 ms.
  - It is still about 0.46 ms behind the pre-Phase 4 build, and its two
    runs differ by 0.8 ms.
- **Item 1 landed.** Under the split, `LaunchLapUs`: stage wake 0.011–0.013
  ms (spin; 0.02 single-threaded), `api_mutex` hand-over 0.001, relock
  0.013 (fairness; 0.074–0.078 single-threaded). What remains of the launch
  is `settle_cells`, 0.25–0.30 ms (the deferred world queue and deferred
  cell emission), plus the early release at 0.07–0.10 ms. `settle_cells` is
  now the largest launch item.
- **Build still waits on the recording 0.7–0.8 ms per frame.** That is the
  one-slot serialization (item 3, full form).

**1b, the publication batch cache (built, unit-gated, not yet measured in
game).**
- `native_pass::Scene::batch_cache`, on by default;
  `SE_VULKAN_PASS_BATCH_CACHE=0` turns it off.
- **How it works.** Each actor keeps last publication's rows (by stable batch
  id), glass tickets and static spot memberships, or its fallback. They stay
  valid while its commit epoch, published view mask, LOD state
  (current / previous / transition) and the scene's structure serial match.
  - The serial bumps on model or family registration and removal, level
    binding, dependency invalidation, and a change in the views' publication
    shape.
  - A hit replays rows through a per-chunk batch-id → local-index array,
    instead of LOD and readiness resolution, hidden-material scans and a
    batch-key hash per view.
  - First-selection batch order and every output list are unchanged.
- **Gates.**
  - `sevk_native_pass_scene` runs every existing check in all 8 cache,
    dedupe and view-cache modes, plus a new differential test: 24 seeds ×
    150 frames of random poses, appearance, removals, registrations,
    invalidations, lazy rebinding, camera motion, hidden queries,
    view-shape changes and cancels. Groups, streams (bytes, owners,
    identities), glass tickets, fallbacks, visible owners and spot updates
    must match the uncached path every frame. The test requires real groups
    and a cache hit rate over 50%.
  - The test caught a divergence in its own first version (a random draw
    inside the per-scene lambda); it now passes.
  - `NativePassGpuTests` 126 PASS with zero validation errors; `InteropTests`
    18 PASS.
- **Profile.** `SevkPassProfile` grows to 168 bytes; the export still
  accepts 112 and 136. New fields: `SelectUs`, `MergeUs`, `SettleUs` (the
  join at the top of `publish`) and `BatchCacheMisses`, so the next run
  splits publication's 0.67 ms.
- **Still to do for 1b.**
  - In-game gates: packet signature on orbit and interior, the frozen-light
    lane, and a VAL run.
  - A paired measurement.

**1b in game (`m1b`, 2026-10-28): the per-actor cache lost; reworked per view.**
- **Gates passed with the per-actor version.**
  - Frozen-light lane with `PassBatchCacheEnabled` off / on / off
    (`Vulkan-77f20ec4…`): `packets.sig`, every cascade and probe image and
    `main-frame.bin` byte-identical. `billboards.txt`, `state.json` and
    `view.*` differ every phase, as in earlier lanes.
  - VAL `Vulkan-770ad9a0…` and VALX `Vulkan-7c6990d8…`: validation on,
    0 errors. VAL's unload `VK_ERROR_OUT_OF_DATE_KHR` is the known one.
- **Pair, split, cache on / off / off / on.**

  | | Cache on | Cache off |
  |---|---|---|
  | Present ms | 5.59, 5.85 | 5.52, 5.48 |
  | Native publish µs | 725, 745 | 618, 613 |
  | Select µs | 525, 531 | 389, 382 |
  | Merge µs | 148, 160 | 142, 141 |
  | Cache misses | 833–839 of 1,868 actors | — |

  Settle is 0 µs: publication doesn't wait on preparation.
- **Why it lost.** The key included each actor's whole published view mask,
  which moves every frame in game (probe faces rotate, occlusion hides
  views). So 45% of actors missed, and a miss costs more than the uncached
  path (recording plus the registry lock).
- **Rework (built, unit-gated, not yet measured in game).** The cache is per
  (actor, view). Each view's entry (rows by batch id, glass tickets, spot
  membership) is valid for the same view position and main flag while the
  actor's commit epoch, LOD state and the structure serial hold. A rotating
  probe face computes only that view; the rest replay.
  - The differential test now rotates three probe-style views.
  - It passes (24 seeds × 150 frames), with misses under half of view
    entries.
  - `NativePassGpuTests` 126 PASS, zero validation errors, cache on.
- **Default.** The cache is off by default (`SE_VULKAN_PASS_BATCH_CACHE=1`
  enables it) until a pair shows the per-view form wins.

**1b per-view pair (`m1c`, 2026-10-28): lost again.**

| | Cache on | Cache off |
|---|---|---|
| Present ms | 5.66, 5.53 | 5.52, 5.43 |
| Native publish µs | 792, 773 | 616, 607 |
| Select µs | 580, 568 | 386, 382 |
| Cache misses | 1,223, 1,194 | — |

- **Likely cause (code reading, not yet measured).**
  - `publish()` bumps `structure_serial` whenever `shape_of(frame.views)`
    changes, and that drops every actor's entries. The shape covers which
    views are in the list. In game, probe faces join and leave the list from
    frame to frame, so the cache is wiped on most frames.
  - Entries also match on `view_index`, the position in the list, which
    shifts when an earlier view leaves.
  - The differential test rotated probes by toggling `enabled` on views that
    stay in the list, so it never hit either path.
- **Fix to try.**
  - Drop the global shape bump. Validate each entry against its own view
    instead: store that view's kind, LOD minimum, shifts and static flag in
    the entry, and stop matching on `view_index` (replay already uses the
    current `View` pointer).
  - Report `BatchCacheEntries` next to the misses.
  - Make the differential test add and remove views from the list.
- **Then:** unit gates, then one pair. If hits alone still cost more than the
  uncached selection, drop 1b and move to 1a.

**1b fix built and gated (2026-10-28), pair pending.**
- **The fix.**
  - `publish()` no longer bumps the serial when the view list changes.
  - Each entry stores `shape_of(view)` for its own view (id, kind, LOD
    minimum and shifts, static flag) and is reused when that shape and the
    main flag match, wherever the view sits in the list.
- **Reporting.** `SevkPassProfile` gains `batch_cache_entries`, so it is now
  176 bytes; 112, 136 and 168 are still accepted. The managed struct gains
  `BatchCacheEntries`, and the report writes it.
- **Test.** The differential test now removes probe faces 3–5 from the list
  instead of disabling them, and drops view 9 on about a third of frames. It
  prints the totals.
  - With the old global bump temporarily put back, it fails ("batch cache
    rarely hit").
  - With the fix, it passes: 141,065 of 501,634 view entries recomputed
    (28%).
- **Gates.** `build-native.ps1 -Test -Deploy` 19/19. `NativePassGpuTests`
  with `SE_VULKAN_PASS_BATCH_CACHE=1`: 126 checks, 0 validation errors.

**1b fixed pair (`m1d`, 2026-10-28): the cache hits, but saves almost
nothing.** Split, cache on / off / off / on. The box ran about 0.4 ms slower
than in `m1c`, for every lane.

| | Cache on | Cache off |
|---|---|---|
| Present ms | 5.92, 5.94 | 5.92, 5.98 |
| Native publish µs | 624, 616 | 666, 658 |
| Select µs | 403, 396 | 417, 410 |
| Merge µs | 164, 164 | 153, 151 |
| Misses / view entries | 197 / 2,764, 190 / 2,746 (7%) | — |

- **The fix works.** Misses fell from about 1,200 per frame to about 190, a
  7% miss rate.
- **But a hit is nearly as expensive as computing.**
  - Selection is 14 µs cheaper and publish 42 µs cheaper.
  - Merge is 12 µs dearer (id-based merge).
  - Present cadence is flat, within noise.
  - Most of selection's cost is the per-row batch mapping and row pushes that
    a hit still does. The LOD and key work the cache skips is only about 15%
    of it.
- **Decision.** Close 1b. Leave it built and off by default
  (`SE_VULKAN_PASS_BATCH_CACHE=1`): a 40 µs ceiling does not touch the
  ~1.0–1.2 ms Build gap. Next is 1a, parallel traversal emission, plus the
  Build-thread inflation work.

**1a re-scoped (2026-10-28): the traversal is no longer the big loop.**

What the traversal handles now (CpuDetail, `m6m` split `e660f081…` / single
`aeb0debb…`, per frame):

| Phase | Split ms | Single ms | Calls |
|---|---|---|---|
| ActorTraversal (inclusive) | 0.380 | 0.405 | 1 |
| Actors reaching the loop body | | | 52 |
| PreparedActors (incl. PreparedReplay) | 0.087 | 0.102 | 35 |
| LegacyRigidActors | 0.110 | 0.101 | 9 |
| LegacyOpaqueParts (inside legacy) | 0.091 | 0.097 | 86 |
| SkinnedActors | 0.052 | 0.064 | 1 |
| NewViewPlan + NewViewEmission | 0.042 | 0.050 | 13 |
| CandidateQuery | 0.019 | 0.020 | 1 |
| GlassParts (mostly pass glass tickets, in publication) | 0.216 | 0.199 | 79 |
| CandidatePlan (parallel planning) | 0 | 0 | 0 |

- **Stable main order already removed about 1,800 pass- and cell-owned
  actors from the loop.** The original 1a design (chunked traversal of
  ~1,865 candidates with per-chunk dictionaries) targets a loop that no
  longer exists. About 52 actors reach the body, and 45 emit: 35 prepared
  replays, 9 legacy rigid actors (86 parts) and 1 skinned actor.
- `PlanCandidates` never runs (0 calls): the candidate list is under its
  256 threshold.
- **Ceiling.** Parallel emission of 45 actors would save at most about
  0.2 ms, after per-chunk emitters that support OLD-order scopes (today
  `AddEmitterPacket` throws on them), main groups, owner scopes, highlights
  and glass. That is a lot of correctness surface for a fifth of the
  1.0–1.2 ms gap.
- **The cost is per item, not per loop.**
  - A legacy rigid actor costs about 12 µs; a part about 1 µs, mostly
    texture lookups and `WorldConstants.Object`.
  - The one skinned actor costs 52–64 µs: it rebuilds a 60-bone palette
    per skin part every frame.
  - A pass glass ticket costs about 2.7 µs: `MyStringId.GetOrCompute`,
    `TryGetMaterial` and a fresh constants buffer per part.

**Plan: 1a′, make the remaining per-item work cheap (both modes), then
decide on fan-out.**
1. **Glass ticket cache** (about 0.15 ms ceiling).
   - Cache each pass actor's glass parts per LOD: material, glass material,
     layout and flags, keyed like the prepared submission (material
     revision, colour, HSV, dithering, texture generation).
   - Per frame, only the world matrix and alpha change. Write them into a
     recycled constants buffer.
   - Gate: packet signature, frozen-light lane and glass counts.
2. **Legacy rigid actors through the prepared path** (about 0.08 ms).
   - Find why 9 actors still take the legacy path: highlights, light
     exclusion, `SubmissionOpaque` false, or glass parts. Then route the
     opaque parts of mixed actors through `SubmitPreparedActor` and keep only
     glass on the legacy path.
   - Gate: packet signature and the highlight and decal fixtures.
3. **Skinned palette reuse** (about 0.04 ms).
   - Build each skin part's bone palette once per `SkinMatrices` change
     (the arrays are replace-on-write), not once per frame.
   - Gate: packet signature with the character in view.
4. **Only then, if traversal is still over about 0.25 ms: overlap instead of
   fan-out.** Run the whole managed traversal as one job, alongside Build's
   native commit block (`NativePassCommits` and `CellCommits`, about 0.45 ms
   of native calls), with one bound emitter.
   - Needs OLD-order support in `PacketEmitter` (an emitter-local OLD order
     spliced at its serial position) and the main-group and owner scopes
     moved into the emitter.
   - Same packet-signature gate.

Expected: about 0.25–0.3 ms off Build in both modes from 1–3, without new
threading.

**Bigger levers on the same Build chain.** They decide the 4.17 target more
than 1a does:
- `EndUploads`, the wait on recording(N): 0.5–0.7 ms, from the one-slot
  serialization.
- `NativePassPublication`: 1.24 ms, of which native publish 0.58.
- `QueueWorld`: 0.7 ms (the pointer-based queue ABI drops one copy of every
  draw's constants).
- Split inflation: about 0.5 ms.

Measurement: one pair after 1–3 land, single-threaded and split, with
CpuDetail, compared on `ActorTraversal`, `GlassParts` and Build.

**1a′ step 1, pass glass cache: built and gated (2026-10-28), not measured.**
- **The cache.** `DrawNativePassGlass` keeps, per actor and per LOD model,
  each glass part's mesh, texture handles, 96-byte glass material and an
  Object constants template. Per ticket it copies the template and writes
  only the world matrix and the alpha (`custom_alpha.x`).
  - An entry holds while these match: the model reference, material
    revision, colour, HSV plus colorize, dithering, texture generation, and
    the four transparent multipliers.
  - An entry with a texture still loading (handle 0 off the texture owner)
    is used once and not kept.
  - Unsupported-material counts are replayed.
  - `SE_VULKAN_PASS_GLASS_CACHE=1` or the `PassGlassCacheEnabled` lane
    toggle; off by default until a pair.
- **A new gate: the glass signature.** Glass packets were not in the packet
  signature. `NativeBackend.LastGlassSignature` now hashes them: pipeline,
  mesh, textures, count, flags, decal distance, constants, material and
  instances. The lane writes `glass.sig`, in a separate no-inline method so
  pinned plugins still load.
- **Gates.**
  - Frozen-light lane `Vulkan-27268ce6…` (cache off / on / off):
    `glass.sig` (111 packets), `packets.sig` (2,126), `cells.json`,
    `main-frame.bin` and every cascade, probe and spot image are identical.
    `billboards.txt`, `state.json` and `view.*` differ every phase, as
    always.
  - `NativePassGpuTests` with the cache on: 126 checks, 0 validation
    errors.
  - `stderr.txt` is clean.
- **Not covered.** A generated texture recreated under the same name gets a
  new handle without a generation bump. It would be missed, as it is by the
  prepared submission. Glass rarely uses generated textures.

**1a′ steps 2–3: built and gated (2026-10-28), not measured.**
- **Who takes the legacy path.** A census (`SE_VULKAN_LEGACY_CENSUS=1` logs
  the reasons every 600 frames) on the lane's Pertam view:
  - all 9 legacy rigid actors are stock old-pipeline blocks with glass parts:
    4 `LCDPanelWide`, 2 small `Cockpit`, 1 `CockpitIndustrial`, 1 `TextPanel`
    and 1 `StoreBlock`;
  - the one skinned actor is the astronaut, which is also highlighted.
  - No actor took the legacy path for a light exclusion.
- **Step 2, mixed actors on the prepared path** (`SE_VULKAN_PREPARED_MIXED=1`
  or the `PreparedMixedEnabled` lane toggle; off by default).
  - An actor with glass parts builds its prepared submission from its opaque
    parts only, replays it, then emits only its glass parts per part. The glass
    packets keep their part index and OLD order.
  - An all-glass actor (empty submission) skips the replay.
  - Gate, frozen-light lane `Vulkan-075fe41f…` (off / on / off):
    `main-frame.bin`, every cascade, probe and spot image, `cells.json` and
    `glass.sig` (111 packets) are identical. The census shows the 9 actors
    leaving the legacy path during the treatment.
  - `packets.sig` changes, as expected: the prepared path batches differently.
    The treatment has **2,142 world packets instead of 2,126**. Prepared rigid
    batching takes only untinted parts (or tinted pipelines with a generic
    variant), while `AddRigidWorldPacket` also batches tinted ones. So the
    pair has to show that the CPU saving outweighs 16 more draws.
- **Step 3, skinned constants cache** (`SE_VULKAN_SKIN_CONSTANTS_CACHE=1` or
  `SkinConstantsCacheEnabled`; off by default).
  - Each opaque skin part keeps an `Object` constants template (identity
    matrix, no bones), keyed on model, part, skin part, layout, tint, HSV,
    emissivity, LOD alpha, dithering and LOD.
  - It also keeps the 60-bone palette of the `SkinMatrices` array it was built
    from. Bone updates replace that array, so a reference match reuses the
    palette.
  - Per frame it rents a block, copies the template, and writes the world
    matrix and the palette. Glass skin parts keep the old path.
  - An animating character replaces `SkinMatrices` every update, so the
    palette mostly rebuilds. The saving is mainly the `WorldConstants.Object`
    name lookups.
  - Gate, lane `Vulkan-a16f0df7…` (off / on / off, astronaut in view): every
    artifact identical, including `packets.sig` (2,126) and `glass.sig` (111).
- `stderr.txt` is clean in both lanes. The GPU suites don't reach these
  managed paths; the lanes are the gate.

**1a′ steps 1–3 pair `a1` (2026-10-28).** This is eight CpuDetail runs,
split and single, with all three caches on against off. The table is in the
attribution doc.
- **Glass cache:** `GlassParts` −0.13 ms and `NativePassPublication`
  −0.11 to −0.12 ms in both modes. This is the only real saving.
- **Mixed actors:** the prepared path costs what the legacy path saved
  (±0.04 ms), `ActorTraversal` is flat, and it adds 16 packets. Keep it off.
- **Skin cache:** about −0.01 ms. The astronaut's bones change every update.
- **Present interval:** −0.08 ms (split, 5.90/6.02 against 6.02/6.05) and
  −0.10 ms (single), within noise.
- **Decision.** Default the glass cache on after a VAL run with it on. The
  skin cache is harmless, so it can be defaulted with it or left off. Leave
  mixed actors off.
- **1a′ is done.** Step 4 (overlapping the traversal) would act on a 0.43 ms
  traversal that is now mostly gates and prepared replays, and does not
  justify the emitter work.
- **The Build gap is in the bigger levers:**
  - `NativePassPublication`, now 1.18–1.29 ms;
  - `QueueWorld`, 0.70–0.88 ms;
  - the `EndUploads` wait, 0.5–0.6 ms split;
  - split inflation.

**Glass cache default on (2026-10-28).** `SE_VULKAN_PASS_GLASS_CACHE` now
defaults on (`=0` turns it off). Mixed actors and the skin cache stay opt-in.
- VAL `Vulkan-e4c7b30b…` and VALX `Vulkan-c37ee8ef…` with the cache on: 0
  `VUID` messages.
- Both ended with the known unload `VK_ERROR_OUT_OF_DATE_KHR`, after their
  reports were written. It also appears in `m1b`, `m6d` and `val-batch`, all
  before the cache existed, so it is not caused by the cache. It is still an
  unhandled shutdown exception and needs its own fix.
- **Fixed (2026-10-28):** `accept_present` marked the surface dirty on
  `OUT_OF_DATE` but then fell into the `else if` that calls `check_gpu`, so
  it threw. It now returns after marking the surface dirty. Confirmed by VAL
  `Vulkan-41534165…` at `0e263e9` (Plan 2 included): 295 frames, validation
  on with 0 errors, and a clean unload with no `OUT_OF_DATE`. VALX
  `Vulkan-13326538…` (split, `SE_VULKAN_WORLD_ARENA=1`,
  `SE_VULKAN_EARLY_CELL_PREPARE=1`): 304 frames, 0 errors, clean unload;
  Plan 2 counters show arena 599/0 and early cells 599/0 hits/misses.
  This closes item 1, the quiet-box VAL and VALX reruns.

## Plan 2: the four Build-chain costs (2026-10-28; A, B and D built and measured 2026-10-29; C not started)

### What the `a1` data says

Split, caches on, per frame. Calls and nested scopes come from the
renderer CPU phases, the native pass preparation report, the queue-work
report and the pipeline census.

| Cost | Split ms | Single ms | Inside it |
|---|---|---|---|
| `NativePassPublication` | 1.29 | 1.17 | native publish 0.68 (select 0.41, merge 0.16, both serial; emission 1.8 ms of worker time runs in parallel), OLD cell publish 0.32, pass glass 0.10, end visibility 0.07, resources 0.06 |
| `QueueWorld` | 0.87 | 0.72 | managed blob pack 0.21 (428 KB of constants copied into the arena), native queue 0.30 (recipe validation 0.22 and a 52 µs copy for 3,014 draws), auxiliary queues 0.30 (main order, native models, OLD cells, transparency 0.08) |
| `EndUploads` wait | 0.56 | 0.13 | Build(N+1) waiting for recording(N): one recording slot; the recording runs **5.16 ms from launch to end** |
| Split inflation | +0.5–0.6 total | | per phase **+11–24% CPU cycles**, not just wall time |

Three findings shape the plan:
1. **The inflation is extra cycles, not lost time.**
   - `QueryThreadCycleTime` per phase, split against single:
     - Scene +11%, QueueWorld +15%, publication +11%;
     - CellPublish +21%, BuildPrologue +24%, NativePassCommits +19%;
     - Handoff +48%.
   - Preemption or a lower clock would raise wall time with cycles flat.
     This is the same work running slower per instruction: cache and memory
     contention with the recording thread and the job workers (shared L2/L3,
     SMT siblings, or false sharing).
2. **The `EndUploads` wait is not Build's cost. It is the recording chain
   showing through.**
   - The recording (5.16 ms from launch to end) paces the frame as much as
     Build (about 5.1 ms of its own work). With one recording slot, any
     Build saving below the recording's length turns into wait.
   - That is why the glass cache cut publication by 0.12 ms but moved the
     split's cadence by only 0.08.
   - The 4.17 target needs both chains under about 4.0 ms. A Build-only
     plan cannot reach it.
3. **Most of the remaining cost is per-frame work on unchanged state.**
   - About 3,000 world draws are repacked, recopied and revalidated every
     frame.
   - About 1,870 pass actors are reselected and merged every frame.
   - Retained submission rebuilt 0.002 actors a frame.

### Workstreams

Each change keeps its byte-identical gate where the output doesn't change:
- packet and glass signatures and the frozen-light lane;
- `NativePassGpuTests` and `WorldPassGpuTests`;
- VAL and VALX.

Benchmarks run as one batch per milestone, with CpuDetail and the pipeline
census, single and split.

**D. Split inflation (do first: cheap, and it changes how the other gains
land).**
1. **Topology experiment, measure only.**
   - Toggles: `SE_VULKAN_BUILD_CPU` and `SE_VULKAN_STAGE_CPU` set an ideal
     processor or affinity for the build thread and the recording stage
     thread; `SE_VULKAN_JOB_THREADS` keeps workers off those cores.
   - Compare cycles per phase for: same physical core (SMT siblings);
     separate cores, same L2 or CCX; separate CCX.
   - The box's topology comes from `GetLogicalProcessorInformationEx`,
     logged once.
2. **False-sharing audit.** Find state written per frame by both chains,
   then pad, split per thread or batch it:
   - managed: `FrameProfiler` statics, `RendererCpuProfiler` counters, job
     system counters, `NativeBackend` stats structs;
   - native: `performance`, census atomics, `api_mutex`/`record_cv` lines,
     `world.current()` slot headers.
   - Handoff's +48% says the hand-off lines bounce. Measure that with the
     handoff phases.
3. **Trim cross-thread traffic.** The snapshots Apply writes and Build reads:
   `ActorFrameState` and the light and packet copies. Restructuring added
   about 0.5 ms of these. Shrink them to the fields Build actually reads and
   double-buffer in place instead of copying.

   Gate: frame-packet verify (0 mismatches) and the lane.
- **Target:** half the inflation, −0.25 to −0.3 ms on Build.
- **Note:** if affinity is the win, it becomes a policy decision. Pinning in
  a game process can fight the OS scheduler and other plugins, so it ships
  only as an ideal-processor hint with an opt-out.

**B. QueueWorld (trims first, then the structural change).**
1. **Instrument the 0.30 ms of auxiliary queues.** Add scopes for main
   order, native models and OLD cells; today only transparency (0.08) is
   visible.
2. **Frame constants.** `WorldConstants.Frame` walks two structs by
   reflection and boxes every field, every frame and for each probe face.
   Cache the `FieldInfo` lists and offsets, or generate typed writers.
   Byte-identical gate.
3. **Cheaper recipe validation (0.22 ms).**

   > **Corrected after reading the code:** validity was already cached; the
   > cost is building the draw records. See "Plan 2 built".

   It walks `recipe_brief` per
   draw.
   - Keep each draw's last validated `(recipe, generation, object span,
     instance span)`, and revalidate only draws whose tuple changed or
     whose recipe generation moved.
   - Full validation stays on under VAL, and on any mismatch.
   - Native change; malformed input must still be rejected, so the
     negative-test suite in `InteropTests` gates it.
4. **One copy instead of two (about 0.1 ms).**
   - `sevk_world_arena_acquire` returns a native-owned object arena for the
     frame slot. Managed packs constants straight into it, and
     `queue_world_recipes` takes it by reference, with no `draw_objects`
     copy.
   - Needs: slot lifetime against the recording (the arena belongs to the
     slot being queued, never the one recording), and the 256 MB bound.
5. **Structural: resident world draws.**
   - Prepared and rigid-batch draws already have stable identities
     (`PreparedActor`, `RigidBatch` slots).
   - Register them with native as resident draws holding their constants,
     like the pass scene's resident actors.
   - Per frame, managed sends only changed constants (pose and alpha) and
     membership deltas. Only legacy and transient packets (particles-side,
     decals, highlights) keep the per-frame blob.
   - This turns pack, copy and validation from O(draws) into O(changes).
   - It is the largest item in this plan. Gate: packet signature over an
     orbit run (moving camera, so the deltas get exercised) and the lane.
- **Target:** trims 1–4 about −0.3 ms; item 5 a further −0.3 ms or more.

**A. NativePassPublication.**
> **Corrected after reading the code:** selection was already parallel (see
> "Plan 2 built" and the register above). Only the per-row trims were built.

1. **Parallel selection.** Select (0.41) and merge (0.16) run serially
   before the parallel emission.
   - Select per view-chunk on the job pool into per-chunk outputs, and merge
     in view order, so the output is deterministic and identical to today.
   - The differential test (24 seeds × 150 frames) gates it, then the packet
     signature.
   - Ceiling: about −0.3 ms with 4+ workers. Under the split the workers are
     shared with the recording, so measure, don't assume.
2. **Fold OLD cell publication into the pass publish (0.32).**

   > **Superseded:** built as early cell preparation instead. See "Plan 2
   > built".

   - `PublishCells` makes its own pass over the views after the pass
     publish. Publish cells inside the same native call, sharing view setup
     and the per-view culling.
   - Byte-identical gate (`cells.json`, packets).
3. **Temporal selection, only if 1–2 fall short.** 1b showed that caching
   batch rows doesn't pay, because a hit still pays row mapping and pushes.
   - The alternative works at actor level: skip selection for groups whose
     visibility, LOD state and view membership didn't change, and reuse
     last frame's row range.
   - Correctness surface: LOD transitions and smooth-camera hysteresis.
     Design separately before building.
- **Target:** −0.3 to −0.5 ms.

**C. The recording chain and the one-slot serialization (needed for the
target).**
1. **Price the recording's laps under the split.** Prepare / Main / Spot /
   Tail were 1.14 / 1.07 / 0.78 in `m6l`. Add the census fields to the `a1`
   form and find the top three. Each item there is a native recording
   change, with a GPU-suite gate.
2. **A second recording slot.**
   - `BeginFrame(N+1)` and Build's last uploads proceed while recording(N)
     runs, so the two chains overlap instead of meeting at `BeginFrame`.
     This removes the 0.56 ms wait only once Build is shorter than the
     recording.
   - Native work: two frame-recording contexts; upload staging per slot;
     `api_mutex` / `scene_owner` rules for two open frames; and a fence
     before resources are reused.
   - Design it as its own milestone once C1 shows how short the recording
     can get.
- **Target:** the recording under about 4.0 ms. The slot is how Build's
  gains reach the cadence.

### C1 trims (proposed, 2026-10-29)

From reading the four top laps (batch 52 numbers, split). Each needs a
finer measurement first, because three of the four laps hide more than
their name says.

**Step 0: measure before trimming (diagnostics only, one batch).**
- **Probe laps.** Add `cpu_lap` marks in `render_probes`: plan and
  targets, face preparation, postprocess, mips and prefilter. Today all of
  them land in `CSM copy`.
- **Pass streams.** Log stream bytes and group counts per frame in the
  native pass report; split the lap into "stream copy" and "material sets".
- **Spot shadows.** Put `sevk_get_spot_stats` (maps, draws) into a report,
  plus how many lights rebuild their static map and how many only their
  dynamic overlay each frame. Pertam has a flashing light (see memory and
  the lighting-lane notes), which may dirty static maps.
- **Launch to start.** Price the recording stage's wake-up (about 0.4 ms
  in `m6l`) and the other intervals outside the laps.

**Candidate trims, by lap.**

| Lap (split ms) | What it does today | Trim | Expected |
|---|---|---|---|
| `spot shadows` 0.73 | Already parallel: up to 4 workers record per-light secondaries (`Spot` jobs: about 6 per frame, 1.19 ms of worker time). The recording thread records its share, waits, then executes the secondaries with barriers and the frozen-map copies | (a) If step 0 shows static rebuilds every frame, find what dirties them. (b) Cache each light's caster lists while its static map and the caster set are unchanged, and re-cull only the dynamic overlay. (c) Lower the recording thread's own share, so it executes while the workers record | 0.2–0.4 |
| `CSM copy` 0.53 (+0.10 split) | Mostly probe preparation and the unmarked probe ops (postprocess, mips, prefilter) | After step 0: record the unmarked probe ops into a secondary on a worker (cascades already do this), or cache their descriptor sets and constants per face | 0.2–0.4 |
| `gbuffer new main` 0.64 | Builds and stable-sorts the entry list (managed draws, GPU-opaque batches, pass groups) every frame, then records ranges in parallel (`SE_VULKAN_PARALLEL_MAIN`, on) | Reuse the sorted entry list while owner orders and the group keys are unchanged (the same pattern as the world sort reuse); check how the ranges are split against the worker count | 0.1–0.2 |
| `prepare pass streams` 0.55 | Copies every pass stream into the slot's mapped vertex buffer (parallel only above 256 KB), then builds or reuses material sets | Move the stream copy to Build's publication end (the data is final once publication ends) or have emission write straight into the slot buffer; keep the material-set reuse | 0.2–0.4 |
| Outside the laps, 1.1–1.3 | Stage wake-up, frame begin, submit | Spin briefly before the stage blocks, as the hand-off plan suggested; then re-price | 0.1–0.3 |

Together these come to about 0.8–1.7 ms against the 1.5 ms target. That's
enough to justify C1, but not certain; C2 (the second recording slot) still
decides whether Build's savings reach cadence.

**Gates for every trim:** the GPU suites (126 + 267 + 36 + 427 + 542),
the frozen-light lane (spots, cascades and probe images are artifacts), and
VAL/VALX. The spot and probe trims touch recording order, so they need the
lane's per-image comparison, not only the packet signature.

**Backends and Anomaly.** All of this is internal recording on the Vulkan
side; no guest contract changes. If probe ops move to a worker, guest
replay marks inside probes (if any) must stay on the primary; check
`guest_replay` sites before building it. No DX11 work.

### Order and expected result

| Step | Work | Expected effect |
|---|---|---|
| 1 | D1 topology experiment | one benchmark batch; decides D's direction |
| 2 | B1, B2, A2 | instrumentation and byte-identical trims; no behaviour change |
| 3 | B3, A1, D2 | native validation, parallel select, false sharing; one batch |
| 4 | C1 | recording lap pricing, then trims |
| 5 | B4 | arena ABI |
| 6 | D3 | snapshot trimming; one batch |
| 7 | B5 resident world draws and C2 second slot | each designed and gated as a milestone |

- **Build:** about 5.1 ms of its own work, minus D 0.3, B 0.3–0.6 and A
  0.3–0.5, gives roughly 3.8–4.2 ms.
- **Recording:** has to reach the same range through C. Without C, Build's
  gains show up as wait.

**Backends and Anomaly.** This is all internal to the Vulkan renderer:
publication, the world queue, recording slots and thread placement. No
guest-visible contract changes. If A1 or B5 moves guest callbacks
(`RaisePreparation`, `RaiseFrame`) or their timing, the Anomaly
`Docs/Vulkan.md` threading note gets updated. DX11 has no equivalent stages,
so there is no DX11 work.

### Plan 2 built (2026-10-28): A, B and D in code, not measured

Reading the code changed three items from the plan above. Everything is
built, unit-tested and gated (results below). Nothing is benchmarked yet.

**B. World queue.**
- **B1.** New CpuDetail scopes for the auxiliary queues: `MainOrderQueue`,
  `NativeModelQueue` and `CellEmit`. `FrameConstants` now times the frame
  block on its own.
- **B2, frame constants (always on).** `WorldConstants.Frame` now uses a
  per-layout `FrameWriter`:
  - offsets are resolved once;
  - the postprocess and texture-multiplier structs are read through compiled
    getters instead of `FieldInfo.GetValue` (one box per field per call);
  - the main view and every probe face use it.

  The old builder is kept as `FrameReflected`. With
  `SE_VULKAN_FRAME_CONSTANTS_VERIFY=1`, every call compares the two
  byte-for-byte and throws on any difference.
- **B3, recipe draws (always on; the parallel build is opt-in with
  `SE_VULKAN_PARALLEL_RECIPES=1`).**
  - **Correction:** recipe validity was already cached. `validate_world_recipe`
    returns at once for a valid recipe, so the 0.22 ms is not validation. It
    is building about 3,000 `WorldDraw` records (about 180 bytes each) through
    a zeroed temporary and a second copy.
  - Now every check runs first, serially, over the compact `recipe_brief`
    table, with the same checks and messages in the same per-draw order.
  - Then the draws are written in place into the zeroed vector.
  - With the toggle on, frames of 2,048+ draws are built in up to four ranges
    on the job pool.
  - `sevk_set_parallel_recipes` flips it at run time.
- **B4, one-copy arena (opt-in, `SE_VULKAN_WORLD_ARENA=1`).**
  - `sevk_world_object_arena` returns the object blob of the submission the
    next queue will take. That is never the one being recorded; aliasing it
    throws.
  - The recipe path packs constants straight into it, and
    `queue_world_recipes` skips its copy when it is handed that buffer.
  - If any other queue intervened, it copies from the arena instead
    (`arena_misses`), so the fallback stays correct.
  - A native library without the export keeps the managed arena.
- **B5, resident world draws: not built.** It changes how the main order, OLD
  order, owner scopes, highlight indices and model batches address draws.
  Each of those reads physical packet indices today. Its payoff depends on
  how many of the ~3,000 draws really repeat frame to frame. It needs its own
  design pass and gate (a moving-camera packet-signature run), and a census
  of repeating draws should come first.

**A. Publication.**
- **A1, correction: selection is already parallel.**
  `pass_scene.scene.parallel_selection` is on by default
  (`SE_VULKAN_PARALLEL_SELECTION`) and splits about 1,870 selections into up
  to 8 ranges. The 0.41 ms "select" is a parallel wall time.
  - Built: the hidden-material test runs once per actor, not per view and
    LOD.
  - Every chunk's batch map is pre-sized, not just the first.
  - Batch order comes from insertion order, not hashing, so the output is
    unchanged.
- **A2, early cell preparation (opt-in, `SE_VULKAN_EARLY_CELL_PREPARE=1`).**
  The plan's "fold cells into the pass publish" saves little: the cells
  already prepare on `CellView` jobs, and the pass and cell actor sets are
  disjoint. What was built instead:
  - `sevk_cells_prepare_async` runs the cells' (pure) preparation on a
    background job, started next to the early pass preparation, so it
    overlaps voxels and lighting setup.
  - `PublishCells` still calls `sevk_cells_prepare` with the frame it builds
    then. Native reuses the early result only when the frames are
    byte-identical and no cell was committed, moved or removed in between.
    Otherwise it prepares again.
  - Every cells entry point, `settle_cells` and the renderer teardown join
    the job.
  - The selection is a pure function of the cells and the frame; state only
    advances at publish. So a discarded early result costs time, never
    correctness.
- **A3, temporal selection:** not built. The plan made it conditional on A1
  and A2 falling short, which needs the benchmark.

**D. Split inflation.**
- **D1, placement toggles (measurement only).**
  - `SE_VULKAN_BUILD_CPU` (frame build thread) and `SE_VULKAN_STAGE_CPU`
    (native recording stage): `<cpu>` sets the ideal processor, `<cpu>!` pins
    the thread.
  - `SE_VULKAN_JOB_AFFINITY=<hex mask>` pins the job workers.
  - The first placement, or `SE_VULKAN_TOPOLOGY_LOG=1`, logs which logical
    CPUs share a core and an L3.
- **D2, false sharing.**
  - **`WorldRecipe`:** the recording thread writes each recipe's descriptor
    cache per draw, while Build reads the same recipe's packet and geometry
    for the next frame. That cache now sits on its own cache line
    (`alignas(64)`).
  - **`WorldSubmission`:** the two submissions are cache-line aligned, since
    recording bumps one's counters per draw while Build fills the other.
  - **Ruled out:** the managed CPU profiler already keeps a separate lane per
    thread, and the native `performance` counters take few writes per frame.
- **D3, hand-off traffic.**
  - Recycling the frame's ~3,000 world-packet constant blocks now takes the
    pool lock once instead of once per block.
  - The actor snapshots and field folds are already dirty-only. The 0.13 ms
    hand-off runs on the render thread, not Build. So there was nothing
    further to trim without measurement.

**Gates (2026-10-28).**
- **Native unit tests:** 19/19, including `native_pass_scene`, `old_cells`
  and `job_system`.
- **Frozen-light lane, `QueueTrimsEnabled` off / on / off**
  (`Vulkan-dfd957a9…`; the arena and parallel recipe build together, with
  `SE_VULKAN_FRAME_CONSTANTS_VERIFY=1` on):
  - every artifact identical: `packets.sig` (2,126), `glass.sig` (111),
    `cells.json`, `main-frame.bin`, and every cascade, probe and spot image;
  - `view.rgba8` differs every phase, as always;
  - the frame-constants verifier never fired.
- **Frozen-light lane, `EarlyCellPrepareEnabled` off / on / off**
  (`Vulkan-9612be63…`): every artifact identical (`packets.sig` 2,137).
- **GPU suites with every opt-in on:**
  - NativePass 126, SceneSubmission 267, ShadowOcclusion 36, Probe 427,
    WorldPass 542, Interop 18 and HudBillboard pass, all with 0 validation
    errors.
  - The arena hit in VAL (queue copy 6 µs, against 78).
- **`BillboardPacketTests` fails ("forked flares and billboards match serial
  packets"), and fails the same way at `ae76859`.** It is older than this
  work.
  - **Resolved, by a separate session:** the test is stale. Since milestone
    6, collected billboards reach emission only after the hand-off
    (`renderer.Latch()`), which it never called. The first guess (milestone
    4's texture rule) was wrong.
  - The one-line test fix is on main (`f39b252`). Production is unaffected.
- **VAL and VALX did not give usable runs.** Another game was running on the
  box (about 7 cores busy, GPU frame 195 ms), so VAL rendered 47 frames at
  ~580 ms and VALX stopped at "too few rendered frames". Both logged 0
  `VUID` messages. Rerun both on a quiet box before any default changes.
- `SE_VULKAN_PLAN2_COUNTERS=1` logs arena and early-cell hits and misses
  every 600 frames, so the benchmark shows whether they engage. The plain
  lanes can't show that, since the output is identical either way.

### Plan 2 measured (2026-10-29, batches 49 and 50)

Details, run IDs and full tables are in the attribution doc, "Plan 2
batches 49 and 50". CpuDetail on; present interval swung 0.3–0.8 ms between
repeat runs of a lane, so the verdicts come from phases and cycles.

| Change | Measured | Verdict |
|---|---|---|
| Always-on (B2 frame constants, B3 in-place recipes, A1 trims, D2, D3) against pinned `ae76859` | Build thread 6.15 → 5.80 ms; `LightingSetup` 0.27 → 0.15 (B2); `NativeWorldQueue` 0.33 → 0.27 (B3) | Kept (already on) |
| A2 early cell preparation (`NPXE`) | `CellPublish` −0.14, Scene −0.19, 0 misses; `EndUploads` +0.10 | Default on (2026-10-29) |
| B3 parallel recipe build (`NPXR`) | `NativeWorldQueue` −0.03, build thread −0.15, `EndUploads` flat | Default on (2026-10-29) |
| B4 world arena (`NPXA`) | About 1.5 ms slower in both runs, with inflation everywhere (Scene cycles +28%, workers +18%), no new waits; copy 60 → 6–8 µs, 96–97% hits. Not seen inside `NPX2` | Off; repeat pair before any verdict |
| All three together (`NPX2`/`NPC2`) | `CellPublish` −0.13, but `EndUploads` +0.32 under the split | Superseded by the per-toggle lanes |
| D1 placement (`NPXSEP`, `NPXSMT`) | Both longer: build thread +0.18 / +0.13, `EndUploads` +0.25 / +0.12. Box: 8 cores with SMT, one 16 MB L3 | Closed, no pinning |

**What this means:** the target needs both chains under about 4.0 ms. Build
is now about 5.8 ms of wall time including about 0.8 ms of `EndUploads`
wait, and every further Build saving moves into that wait. Plan 2 C (the
recording chain) is next; B5 and A3 only pay after it.

**Crash found and fixed (milestone 6 race).** A VALX run crashed at session
unload: an access violation in native `UploadTexture`, reached from
`VulkanTextureCache.RemoveGenerated` during Apply, after a validation message
`VUID-vkCmdPipelineBarrier2-commandBuffer-recording` (an upload barrier into
a command buffer that was not recording). Apply was replacing native
generated textures while Build was in flight, against milestone 5's rule that
Apply makes no native resource mutations.
- **Fix:** `Process` joins Build before Create, Reset and Destroy
  GeneratedTexture, UnloadTexture and UnloadTextures, which are rare.
- **Gate:** two VALX runs afterwards, 0 validation errors, no fatal errors,
  clean unload.
- **Open:** the exact unlocked native path wasn't found by reading the code.
  Deferring these mutations to the hand-off (instead of a join) is the
  longer-term form.
- The batch monitor now scans `stderr.txt` too. It only read `*.log`, which
  is why this crash wasn't flagged live.

**Not the cause:**
- the native frame gate (0.03–0.04 ms per frame of off-owner waits);
- off-owner scene calls (56 in a run, all at load);
- deferred-queue replay;
- recording waits, which are on a third thread and off the path.

**What to fix, in order.**
1. **`EndUploads`.** Find the stall from the entry-to-exit timer and GC
   report. Restore the single-thread upload contract: open an Apply-side
   batch around Apply's uploads, or make Apply's uploads deferrable into
   Build's batch, so the batch Build closes holds only Build's work.
2. **Shorten the serial hand-off.**
   - Launch Build(N+1) right after `Handoff`.
   - Move `PublishVisible` (double-buffer `frameVisible`) and
     `SampleDiagnostics` after the launch.
   - Spin briefly before blocking in `Join` and in the build thread's start
     wait, since each wait usually ends within microseconds of the other
     thread's signal.
3. **Find the broad inflation.** Two cheap tests:
   - give the build and render threads different physical cores (ideal
     processor or affinity, opt-in);
   - check false sharing by padding or separating the scene fields Apply
     writes from those Build reads.
4. **Rebalance (structural).** Move work that depends only on the packet
   from Build into Apply, for example the lighting and probe and postprocess
   configuration and the environment draw (about 0.25 ms). Then make Build
   itself cheaper: 1b (incremental publication) and 1a (parallel traversal),
   now on the critical path. With Apply about 1.5 ms and Build about 4.2 ms
   on this box, the split only reaches its ceiling once Build is near Apply.

### Critical files

- `ClientPlugin/Rendering/MyVulkanRender.cs`: Draw (Apply, latch, Build), the
  thread (milestone 6).
- `ClientPlugin/Rendering/FramePacket.cs`.
- `ClientPlugin/Rendering/Scene/VulkanScene.cs` and the split partials:
  - `VulkanSceneFrame.cs` (`FrameInputs`, `LatchFrame`, light frames);
  - `VulkanActorFrames.cs` (actor frames, verify);
  - `VulkanSceneHandoff.cs` (Build ops, folds);
  - `VulkanActorFields.cs` (Apply fields);
  - `VulkanSceneState.cs` (Applied scene state).
- The scene partials Build runs: `VulkanActorLods.cs`, `VulkanNewActorViews.cs`,
  `VulkanMainGroups.cs`, `VulkanGroupOcclusion.cs`,
  `VulkanShadowGroupOcclusion.cs`, `VulkanSceneIndex.cs`,
  `VulkanViewEligibility.cs`, `VulkanNativePassScene.cs`, `VulkanOldCells.cs`.
- `ClientPlugin/Rendering/Interop/NativeBackend*.cs`.
- Subsystems:
  - `Scene/VulkanVoxels.cs`, `VulkanEnvironment.cs`, `VulkanParticles.cs`,
    `VulkanDecals.cs`, `VulkanBillboards.cs`;
  - `VulkanSprites*.cs`, `VulkanShadows.cs`, `VulkanMedia*.cs`;
  - `Content/VulkanTextureCache.cs`.
- `ClientPlugin/Diagnostics/FrameProfiler.cs`, `RendererCpuProfiler.cs`.
- `Native/src/renderer.cpp`: call wrappers, frame owner and gate, error.

### Reuse

- **Per-thread emitters and prediction-then-verify:** `NativeEmitters.cs`
  (`PacketEmitter`, `SpliceEmitter`).
- **Snapshot and rollback precedent:** the `ShadowSystem` before/after copies
  and `CancelPreparedFrame` (`VulkanShadows.cs`).
- **Verify-mode pattern:** `SE_VULKAN_INCREMENTAL_GROUP_BOUNDS_VERIFY`, now
  also `SE_VULKAN_FRAME_PACKET_VERIFY`.
- **Cross-thread intake:** the `ConcurrentQueue` callbacks in
  `VulkanSceneIndex.cs`.
- **Native scene-call wrappers and the launch snapshot:** `call_scene`,
  `call_snapshot`.

### Gates

**Milestones 1–5 and the trims (one thread):**
- packet-signature equality in the same-process lane
  (`lighting-lane-pinned.ps1 -Property ApplyBuildOrder`, `packets.sig`);
- verify mode, 0 mismatches on both worlds (lanes NPFV and INFV);
- GPU suites under validation, plus DepthGeometry, GuestDepth, Voxel and
  Sprite;
- the VAL gameplay run.

**Milestone 6:**
- VAL with `SE_VULKAN_FRAME_SPLIT=1` (validation errors 0, no exceptions in
  the render log);
- the pipeline census with the split:
  - `OffOwnerSceneCalls` must be 0;
  - `GateWaits` and `GateWaitUs` sized;
  - no cross-thread throws;
- orbit and interior gameplay runs with the split, plus a CpuDetail run
  (Apply, `BuildJoin`, `BuildThread`);
- a soak with world unload/reload and a resolution change;
- the GPU suites are unaffected: they drive the scene directly, not
  `MyVulkanRender`.

Verify mode and the same-process order lane are single-thread gates: verify
compares against live state, and the lane toggles the order inside one
process.

**Milestone 7:** one batched benchmark per the benchmark rules, plus the
latency comparison.

Commits go to main per milestone, only when asked.

### Risks

- **The margin.** Build projects to about 4.2 ms on a normal box against
  4.17. 1b and 1a, plus the trims, decide it.
- **One frame of extra latency.**
- **Guest plugins on a new thread** (milestone 6 Anomaly documentation; test
  the user's plugin set).
- **Single-thread regression** if the split stays off by default after
  milestone 7: the trims shrink it, but the copies remain.

## Plan 3: a two-stage build (design, 2026-10-30; not started)

### Why

Since the GPU light tiles, the build thread paces the split. In the
2026-10-30 sessions it did about 4.3–4.5 ms of its own work a frame. Every
recording cut since then (stream copy, early NEW main entries, parallel
prepare, async submit) only grew the launch gap by the same amount.
Overlapping publication inside one Build failed (6.1–6.3 ms, batch 100).
Single-phase trims are worth about 0.1 ms each, below this box's
session-to-session drift. A second pipeline stage takes the frame's tail off
the build thread.

### The split (balanced, revised 2026-10-30)

Three threads, one frame each, pipelined. The split follows the line
between managed scene work and native scene work, so each stage gets about
half of today's Build:

| Thread | Works on | Does |
|---|---|---|
| Render thread (Apply) | frame N+2 | message intake, particles, decals, scene update (unchanged) |
| Build stage A ("scene") | frame N+1 | prologue, clipmaps, shadow fit, traversal and packet emission, voxels and billboards, lighting setup, group occlusion queries, and the managed half of publication: pass and cell commits **captured**, not sent |
| Build stage B ("native and launch") | frame N | replay of A's captured native calls (pass and cell commits, setters), native pass and cell preparation and publication, fallback emission, end visibility, `QueueWorld`, `EndUploads`, `BeginFrame`, sprites, `RaiseFrame`, `EndFrameAsync` |
| Recording stage (native) | frame N−1 | unchanged |

**Estimated sizes (2026-10-30 CpuDetail session, Build own work about
4.5 ms):**

| Stage A | ms | Stage B | ms |
|---|---|---|---|
| Prologue (packet collection) | 0.30 | Replay of captured commits and setters | about 0.15 |
| Clipmaps | 0.12 | Native preparation join (now in B, still async) | about 0.1–0.3 |
| Shadow fit | 0.07 | Native pass publish | 0.77 |
| Scene build: traversal, transforms, lights, shadow poses, legacy and skinned actors | about 1.6 | Cell publish | 0.20 |
| Voxels and billboards | 0.41 | Fallbacks and end visibility | about 0.12 |
| Lighting setup | 0.15 | `QueueWorld` (pack and native queue) | 0.84 |
| Commit capture and group queries | about 0.2 | `BeginFrame`, sprites, guest frame, launch | about 0.2 |
| **Total** | **about 2.8** | **Total** | **about 2.4–2.6** |

Moving voxels and billboards to B instead would swap the balance (about
2.4 / 2.9). Voxels are their own subsystem, but A's occlusion revision reads
their mesh updates, so they stay in A by default; the swap is a later
tuning knob.

**Why this line.** The native pass and cell scenes are the heaviest work
left (publish 0.77, cells 0.20, preparation). They already take their
inputs through deferrable scene calls (commits are `call_deferrable_scene`).
So capturing A's calls per frame and replaying them in B reuses the
mechanism the split already relies on. Publication's managed reads (group
occlusion results, LOD) happen in A, which emits the queries and commits.
B needs per-actor state only for fallback emission, so a third
actor-snapshot slot covers it.

### What has to be per-frame

**Managed.**
1. **`FrameEmission`** (the step milestone 3 skipped). It holds:
   - world, highlight, decal, glass, transparency and billboard packets;
   - main groups and scopes, rigid and model batches;
   - shadow exclusions and `CameraPosition`.

   A fills emission N+1, and B adds fallback packets to and packs emission
   N. Single-thread runs keep one emission, so tests that reflect on
   `worldPackets` keep working.
2. **Three actor-snapshot slots instead of two parity slots.** Apply writes
   N+2, A reads N+1 and B reads N (fallbacks). Dirty refreshes have to reach
   every slot as it rotates. Verify mode (`SE_VULKAN_FRAME_PACKET_VERIFY=1`)
   checks it.
3. **Sprites:** a third main buffer.
4. **The visible set** comes from B's end visibility. The game sees it one
   frame later than under today's split. The game already tolerates one
   frame; check LOD and animation decisions with two.
5. **The upload batch.** A uploads while B flushes. A's batch is sealed at
   the A→B hand-off and submitted by B, while A opens the next (native:
   separate seal from submit on `upload_slots`).

**Native.**
1. **Captured calls.** A's native scene calls for frame N are recorded in
   a frame-tagged list and replayed by B(N) in order. That covers:
   - pass and cell commits, and preparation requests;
   - setters: lights, shadow cascades and matrices, spots, probe,
     postprocess and environment configuration, flare queue, guest
     products.

   This extends today's deferred queue with a frame tag. A reads native
   state only from the launch snapshot (flare and occlusion results, shadow
   and probe feedback, and pass and cell preparation results of the frame
   B last published).
2. **Publication snapshots.** B publishes, then captures pass groups and
   streams and OLD-cell draws into a per-frame slot, which recording N
   binds. Today's `rec_view` is taken at recording start, which B(N+1)
   could already have overwritten.
3. **The scene owner** becomes stage B's thread, the only one issuing
   scene calls. The census must show 0 off-owner scene calls.

### Guests and DX11 parity

- `RaiseScene` and `RaisePreparation` run on A, and `RaiseFrame` runs on B,
  so one frame's callbacks span two threads. This extends the build-thread
  contract proposal in Anomaly `wiki/Framework-gaps.md`: callbacks must not
  assume one thread for a whole frame.
- DX11 is unchanged, so this is Vulkan-only and opt-in
  (`SE_VULKAN_BUILD_STAGES=2`).
- Input latency grows by one more frame, about 4–5 ms. Measure it with the
  milestone 7 latency marker before any default.

### Milestones

1. **`FrameEmission`, single thread.** Gate: packet signature identical in
   the same-process lane, and the GPU suites.
2. **Three actor-snapshot slots, single thread.** Gate: verify mode 0
   mismatches, packet signature identical.
3. **Captured native calls, single thread.** A's commits and setters are
   recorded per frame and replayed at B's start; native preparation and
   publication move after the replay. Gate: frozen-light lane identical,
   GPU suites, VAL.
4. **Per-frame publication snapshots and the sealed upload batch.** Gate:
   the same, plus VALX.
5. **The second build thread** (`SE_VULKAN_BUILD_STAGES=2`). Joins on
   unload, settings and device changes, mirroring milestone 6. Gate: census
   with 0 off-owner calls, a soak run with unload and resize, and the user
   plugin set.
6. **Measure and document.** Same-session interleaved A/B, latency (now
   two frames behind Apply), the visible-set lag, and the Anomaly contract
   note.

### What it buys, honestly

- Each build stage is about 2.4–2.8 ms, so Build stops pacing with room for
  this box's drift.
- The recording chain then paces at about 4.4–4.6 ms (2026-10-30 session),
  and the cuts already built and kept, or opt-in, start to pay:
  - async submit, about −0.3 ms;
  - parallel prepare and early NEW main entries, about −0.1 to −0.3 ms.
- That puts recording near 4.0–4.2 ms: borderline against 4.17.
- The GPU + present chain (GPU frame 2.5–2.7 ms plus the 1.4 ms in-game
  present call) is then the next limit. Its present cost is still
  unexplained; the probe does it in 0.2 ms.
- So Plan 3 is necessary but probably not sufficient by itself.

**Effort and risk.** About six working sessions (the balanced split adds
the third actor slot and moves native publication). The risk is like
milestones 2–6: races between stages are the main hazard, and each
milestone's single-thread gate is the defence.

## Other work

### Make Build cheaper (pays directly once Phase 4 exists)

- **1b, full incremental publication** (publish 0.55, prepare about 0.45 ms).
  The view cache and LOD dedupe trimmed the edges; the per-actor batch
  hashing is the remainder. Design below.
- **1a, parallel traversal** (0.38–0.51 ms). Design below.
- **QueueWorld:** a pointer-based queue ABI would drop one of the two copies
  of every draw's constants.

### P95 on the render thread

- **Scheduling.** The tails are broad CPU loss plus about 3 ms stalls. Next:
  - attribute the stalls with an ETW or Nsight Systems CPU trace of a slow
    frame (thread state, preemption, lock waits);
  - decide render priority (game setting, AboveNormal through a hook, or
    affinity) on more than one machine class.
- **Allocation:** about 300 KB per frame remains (messages 70, particles 60,
  publication 70, lighting setup about 36). Not the P95 driver;
  `NativeLight` and `NativeSpot` cannot change without updating the
  frozen-light lane hooks and pinned-baseline compatibility.

### Plugin set (second scene)

7.66 ms against 5.98 alone, with every plugin feature off. Left:
- **Anomaly's six owned producer passes** (CameraVelocity, LinearDepth, HiZ,
  ReactiveClear, LitMips, HistoryCopy) run every frame without consumers,
  about 0.6 ms of GPU. Consumer-driven activation on both backends is filed
  in Anomaly `wiki/Framework-gaps.md`.
- **The guest frame callback:** 0.50 ms per frame remaining.
- **Render priority** (`SE_VULKAN_RENDER_PRIORITY=high`, opt-in): possibly
  0.1–0.2 ms; unsafe as a default on CPUs with few cores.

## Designs

### 1b, incremental native publication

Profile (orbit, per frame):

| | Value |
|---|---|
| Prepare | about 420–450 µs |
| Publish | about 550 µs |
| Commits | about 100 µs |
| Selected actors | about 1,865 |
| Actors committed per frame | about 200–230 |
| Groups | about 2,400 |
| Emitted rows | about 96 |

Already done:
- the view cache (exact per-view reuse for unchanged actors and views);
- LOD dedupe (no effect; opt-in).

Remaining design:
- **Per-actor batch cache.** Keep each actor's batch keys from the last
  publication (model, view, transition, hidden materials) and its LOD state.
  Recompute only actors whose epoch, view mask, LOD band or transition, or
  model readiness changed.
- **Batch order.** Batches keep stable row lists. Order must still equal
  first-selection order over actors, so keep actors in handle order and each
  batch's first actor.
- **Gate:** the pass-scene unit test runs both paths, as it does for the view
  cache and LOD dedupe; also NativePassGpuTests, the frozen-light lane and the
  packet signature.

### 1a, parallel actor traversal

Superseded on 2026-10-28 by "1a re-scoped": with stable main order the loop
sees about 52 actors, not about 1,865. The design below is kept for the
record.

Shared state in the loop:
- emission scopes;
- batching dictionaries;
- bulk models;
- prepared replay lists;
- glass, decal and highlight lists;
- `MarkSpots`;
- the visible set;
- group-occlusion queries;
- counters.

Design:
1. Extend `PacketEmitter` with scope state.
2. Give each chunk its own dictionaries and lists, merged at splice in chunk
   order.
3. Run contiguous candidate chunks on `RenderJobs`.

Gate: the packet signature plus glass, decal and highlight counts.

## Lessons

- **Find the wait before moving work.** Work removed ahead of a blocking
  point only lengthens the wait (sessions 5–6, steps 1–3).
- **Gates move, they don't vanish.** When a lock wall goes, the wait moves to
  the next ordinary call; walk the call sequence with the census `Sites`.
- **Hidden waits make overlap look bad.** Early prepare was 0.6–0.9 ms worse
  until two calls on its path (its own native call, and the probe plan's
  `GetProbeStats`) stopped waiting for recording. Run the census before
  judging any overlap.
- **Counters can miss waits; driver locks are real;** exclude non-pipelined
  frames from pipeline averages.
- **Thread-local markers must cover every worker a phase fans out to.** Mark
  the side that does not fan out.
- **Scene calls overtake queued non-scene calls.** A deferred call that
  changes what scene calls see must be a scene call itself.
- **Moving state mutation earlier must move its resets too.**
- **Recording cuts do nothing while the render thread is the wall,** and
  render-thread cuts do nothing while recording is. Bank the other side's
  headroom and say so.
- **Below about 0.15 ms, a change is noise in single pairs.** Use stacked
  lanes and repeated pairs, and decide defaults on validation plus direction
  rather than size.
- **P95 needs tail attribution before remedies.** Here the tails were
  scheduling, not GC.
- **Never build or deploy during a batch.** Builds steal CPU (a native build
  inflated a run to 6.17 ms), and the harness rebuilds the managed plugin per
  run. Check for Codex, Vortex and other load first.
- **Judge lighting only in the same-process frozen-light lane;** the packet
  signature cannot gate candidate reordering.
- **Harness hooks follow method names and signatures** (`SetProbes`,
  `SetLights`, `SetSpots`); keep hooked paths and pinned-baseline
  compatibility intact.
- **A one-thread restructuring for later overlap has a cost of its own.**
  Milestones 1–4 added about 0.5–0.6 ms of copies and folds. Benchmark
  against a pinned pre-split build at the end of a no-behaviour-change
  series, not only gate it for parity.
- **Cross-session CpuDetail runs are not comparable.** Machine state moved
  whole frames by 1.5 ms between sessions; only same-session pairs attribute.
- **When work moves between phases, frame-profiler laps move with it.**
  Compare region totals, or add phases for the moved work, before reading a
  per-section delta as a cost.
- **Give new hand-off work its own CpuDetail phase when it lands.** The latch
  and snapshots ran unmeasured for four milestones.
- **Read the code before planning a cost away.**
  - Three Plan 2 items were written from phase names and timings alone, and
    two of them were wrong: the "serial" selection was already parallel, and
    the "validation" cost was draw construction.
  - Check `parallel_*` and cache flags, and what a scope actually encloses,
    before proposing to parallelise or cache it.
- **With two pacing chains, price both before cutting one.** Build savings
  under the split turn into `EndUploads` wait while the recording is as long
  as Build.
- **Separate cycles from wall time when a thread slows down.** Rising cycles
  mean contention; flat cycles with rising wall time mean preemption or a
  lower clock. `QueryThreadCycleTime` per phase settles it.
- **Census before optimising a population.** The legacy-path census showed
  the 9 slow-path actors were all old-pipeline glass blocks, not the
  highlighted or excluded actors the plan assumed.
- **A gate must cover the output a change touches.** The packet signature
  never included glass until 1a′ added `glass.sig`. Check what a signature
  hashes before trusting "identical".
- **Identical output does not prove a cache or overlap engages.** Add hit
  counters (`SE_VULKAN_PLAN2_COUNTERS=1`) before benchmarking it.
- **Run an old failing test at the base commit before blaming new work.**
  `BillboardPacketTests` and VAL's unload error both predated the change.

## Method

1. Profile first: `-CpuDetail`, `SE_VULKAN_PIPELINE_REPORT=<path>` (lock and
   call census, frame-gate counts), `SE_VULKAN_RECORD_TIMING` (recording
   laps), and the tails report for P95.
2. Judge throughput by the present interval mean, never `CpuMeanMs` alone.
3. Paired interleaved runs in both orders on an idle box, with a DX11 pair
   in the same set. Use both worlds:
   - `TestResults/Worlds/Pertam-Orbit` (`NP`);
   - `TestResults/Worlds/Pertam-Interior` (`IN`).

   Add `-UserPlugins` lanes for the plugin set; pair against the pre-split
   build (`PS0`, `INPS0`) for split-cost questions.
4. One toggle per change; default on only after its gates pass:
   - packet signature or verify mode;
   - the frozen-light lane;
   - GPU suites under validation;
   - a validation gameplay run.
5. Implement a batch, then test and benchmark it once; no builds during the
   batch.
6. Product-default flags: `-ActorLods -AsyncPresent -NewActorViews
   -RetainedSubmission -StockShadowSchedule -StockShadowFit -WorldRecipes
   -ParallelQueue -ParallelCascades -NativePassScene -GroupOcclusion
   -StockDepthGeometry -NativeCells -NativeMixedOpaque -PipelinedRecording
   -AsyncPassPacking -DeferredCellEmission`.

## Evidence

- Attribution, history and run IDs: `Docs/Performance-2026-10-25-Attribution.md`.
- Paired results in `TestResults/`:
  - `Steps1to3-*`, `Step5-*`, `Step6-*`;
  - `Step7-EarlyRelease-*`, `Step7b-*`, `Step7c-*` (noisy);
  - `Interior-PluginSet-*`;
  - `Step8-Orbit-*`, `Step8-Interior-*`;
  - `QuickChecks-*` (first runs contaminated by a build);
  - `Steps2to3-*`;
  - `Step4-Stacked-*`;
  - `Defaults9-*`;
  - `EarlyPrepareFix-*` (before the probe-plan fix), `EarlyPrepareFix2-*`.
- Phase 4 runs (in `TestResults/ScenePerformance`):
  - milestone 5 benchmark: current `Vulkan-adf9542c…`, `Vulkan-ded72f26…`
    (orbit) and `Vulkan-c109d8c4…`, `Vulkan-bf4ce254…` (interior);
  - pre-split `Vulkan-cf4a9120…`, `Vulkan-d20f24d2…`, `Vulkan-7b8f1752…`,
    `Vulkan-f476d192…`;
  - DX11 `Dx11-48f2ad1a…`, `Dx11-182a65fa…`;
  - CpuDetail pair `Vulkan-12613c83…` / `Vulkan-c7f79605…`;
  - census `Vulkan-b6be1343…` (gate off), `Vulkan-5012ca40…` (gate on).
- Pinned worlds: `TestResults/Worlds/Pertam-Orbit` and
  `TestResults/Worlds/Pertam-Interior`.
- Pinned binaries:
  - `TestResults/Step0-binaries` (`9afdc09`);
  - `TestResults/Step3-binaries` (`73e74b3`);
  - `TestResults/Step5-binaries`;
  - `TestResults/Step6-binaries` (`7705d3b`);
  - `TestResults/PreSplit-8588f01-binaries` (plugin from `8588f01` with the
    milestone 5 native DLL).
- DX11 scheduler: `VRage.Render11/VRage/Render11/Render/MyRenderScheduler.cs`
  in the decompiled game code.
