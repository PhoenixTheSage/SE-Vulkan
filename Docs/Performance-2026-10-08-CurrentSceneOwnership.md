# Current scene ownership profile

Accepted-policy diagnostic23cb21b2a3d443e6b5900f9cb52956bb recorded1823 complete
frames at1920x1080 with retained submission/recipes, model batches, new actor
views, parallel cascades/queue, async presentation and corrected probes. Frame
wall16.090 ms and owning render-thread CPU15.385 ms are instrumented observations,
not ordinary FPS or a speedup. Phase scopes are inclusive and must not be summed.
The verified earlier ETW remains the exclusive active-CPU evidence; this current
capture refines source ownership with QPC scopes and render-thread cycles.

Current source-owned scopes: scene8.312 ms, actor traversal7.175 ms,
new-view emission2.651 ms, prepared actors1.537 ms (replay1.297 nested),
new-view planning0.838 ms, queue1.522 ms (blob0.634 and native queue0.574 nested),
native recording3.606 ms. Fine actor scopes have no per-phase CPU-ms or allocation
claim. Actor traversal allocates about249.6KB/frame in aggregate, but these scopes
do not assign it to individual helpers. Values are rounded here; exact data is
renderer-cpu-summary.json in the run reports.

Source map: VulkanScene.Draw traverses candidates, owns ancestry visibility and
current bounds; DrawNewActorViews updates current LOD/view/cascade/spot choices,
then SelectRepresentation and DrawWorld re-enter SubmitPreparedActor for every
selected representation. NativeBackend.SubmitPreparedActor/NativeModelBatches
publish model/part packets; QueueWorldBlob builds a second flattened packet and
payload representation; native queue_world_recipes resolves draws before
render_world records them. These are sequential owner boundaries, not separate
exclusive costs that can simply be added.

Stock primary reference: MyGeometryRenderer owns per-view visible instance lists
and MyPreparePass computes LOD groups, counts and offsets, fills instance streams,
then hands complete render data to the dependent render job. Vulkan currently
aggregates after repeated actor/representation re-entry instead. The next product
hypothesis should remove that reconstruction through a retained model/group
contract with bulk current instance/view records. It must preserve current LOD
transitions, appearance changes, bounds, exclusions, highlight identity and all
fallbacks, and avoid per-actor native calls. Prototype only after the complete
registration/frame-publication/invalidation contract is mapped; validate exact
logical/pixel identities and both ordinary orders. No projected saving is claimed.

The rejected actor workers, view-plan cache, object-byte cache and spotlight jobs
remain removed. Local predicate/copy tuning is not the next work item. GPU3.89 ms
alone does not prove full frame below4.17 ms or visual parity.

Restoration note: initial diagnosticf916d836 was invalidated before analysis
because an incremental build reused experimental binaries after timestamp-preserving
source restore. Forced rebuild and absent rejected exports verified the actual
restored native. All seven native tests and113 restored scene checks pass; net48
and net10 Release builds/deploy pass. Managed hash2DEFB46588678CFA52729582550F14E5208627A048E151E6A53DCDA9DF59517D;
nativeC556AB5D83A9B20C96921462FD4E0E75B9EEAF3F86AEF316595226A984AAA25F.
Source checkpointD5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92
remains unchanged. The full performance/parity goal remains active.
