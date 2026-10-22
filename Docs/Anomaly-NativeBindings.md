# Native Anomaly catalog bindings

Anomaly owns registrations, immutable request snapshots and catalog publication.
SE-Vulkan owns compiled native programs, descriptor writes, barriers and retirement.
Native bit22 adds copied resource rows and status queries. It does not advertise
the remaining geometry/atmosphere/transparency owners or same-draw attachments.

## Wire and frame contract

SevkStageBindings is 120 bytes, ABI1: size/version, owner ID, frame index,
requested/fresh/attachment masks, reserved zero, global velocity ID and ten image
IDs. Requested slots are t5..t9. Fresh bit0 applies to global velocity; the other
fresh bits and attachment bits are subsets of requested. Attachment slots are
t6..t9. Image IDs outside requested registers must be zero. Size, version,
reserved fields, duplicate/unknown owners, malformed masks and stale frames
refuse transactionally. Rows require the open allocation callback and are copied
before return. Existing ABI1 pass/frame structures remain unchanged.

SevkStageBindingState is 48 bytes. Its flags report device nullDescriptor support;
frame/row fields describe copied rows, and counters describe catalog descriptor
resolution, missing resources and mismatched resource types. They are descriptor
records, not GPU execution/completion. Use the native shader variant generation,
execution count, last frame and completion status for that distinction.

The core freezes the reservation model and attachment/history metadata before
portable callbacks. It resolves current publication IDs after queued commands
have been copied. New registrations made inside callbacks take effect next frame.
History aliases permit previous-frame reads; ordinary renderer images require
current catalog-epoch production. Reserved historyColor additionally requires
the owned producer to have recorded the immediately preceding frame. Device
epochs prevent old-device references from entering new frame rows.

Persistent catalog targets retain their initialized contents. GuestRendererImage
exposes RequiresCurrentFrame; its existing constructor defaults to true. The
additional bool constructor describes explicitly persistent imported resources.
Catalog target initialization/clear uses private mark0 under bit22, before native
owners and public phases. Older hosts retain the previous mark1 transport. Explicit
portable callback commands retain their requested phase. Public replay still
accepts only marks1..6; synchronous manual replay flushes pending global clears.

## Resource and layout contract

Lighting's directional, point, spot and shadowed-spot owners use independent
layouts when catalog/extras bindings are present. Base-only generations reuse
engine layouts and descriptor sets, including after restoration. Pipeline and
layout generations retire together after their last submission. Legacy diagnostic
replacement releases the active custom layout and clears generation identity.

Native postprocess/HBAO layouts accept b6 prefixes up to the fixed 320-byte extras
layout and t5..t9 catalog resources. A stock owner t5 input remains its engine
input unless an explicit matching reservation overrides it. Attachment
reservations take precedence over general lighting requests. Engine constants,
samplers and resource bindings keep their existing slots. Lighting exposes the
stock s1 point-clamp sampler alongside the existing standard sampler bindings.

Catalog views preserve 2D/3D/array/depth/numeric type checks. Structured buffers use
the protected frame slot and copied pending data. Missing or mismatched resources
bind genuine typed null descriptors; mismatch counters expose incompatible
publications. Published structured storage is not silently treated as a typed
buffer SRV. Device nullDescriptor capability is required by reserved resource
programs; unsupported devices refuse the candidate and retain the prior valid
generation/stock. Enabling nullDescriptor does not enable broader robustness2
policies for stock rendering.

Extras share one CPU writer with fullscreen commands: actual current velocity
availability is independent of camera history validity, and native owner dimensions
are full raster dimensions even for quarter-size HBAO draws. Sun/camera/safety
fields come from the immutable shared frame environment. Jitter integration remains
the separate stage7 contract.

Null texel and image-query semantics follow the
[Khronos image specification](https://docs.vulkan.org/spec/latest/chapters/images.html).
Independent DX11 WARP probes establish zero dimensions/texels for null 2D, 3D,
array and typed buffers; structured nulls report zero elements and static stride16.
DX11 WARP and HAL both store the tested packed-HDR color as (0.5,1,0.625);
the Vulkan binding gate uses that measured control rather than wider tolerances.

The core ShaderBindRegistry.GetNativeState returns NativeStageBindingStatus with
transport/device capability, registered reservations, queued owner/frame and
catalog descriptor counters. Registration, compilation, queued descriptors and GPU
execution remain separate states. Aggregate named-stage/same-draw capabilities
remain false until all legal contracts and remaining owners have execution proof.


## Automated qualification

The focused resource gate passes194 checks; the combined actual-core gate passes
766 including the separate catalog cleanup change. The separate native family gate passes382, executing all82 catalogued
postprocess/lighting variants, HBAO replacement/restoration and stock highlight
geometry, blur and composition, with zero core/synchronization errors. Stock
highlight blur's engine-owned t5 stays outside catalog classification; the older
bit21 library independently passes167 focused stock-highlight checks. The native
suite has16 passes and one configured gpu_opaque_reference skip.

Actual older-host admission passes6 refusal/stock-retention checks. Independent
DX11 packed/null probes pass on net48 and net10. Use GuestDepthGpuTests with
--native-bindings-only or its default combined lane, PostprocessGpuTests with
--generations, and --stock-highlight-only for the maintained stock control.
Remaining stage owners, full same-draw attachments, HDR/jitter
integration and ecosystem qualification remain separate work.


Core publication cleanup now shares target/upload ownership, uses case-insensitive
names and reserved-name checks, preserves cloned CPU payloads for republish, and
retires uploaded images on replacement/unpublish/public pack unregister. Legacy
public target format0/1 aliases match existing RGBA8/FP16 callers. The actual-core
catalog fixture passes102 checks, including pipelined sampling before replacement,
structured/mip uploads and eight unregister cycles preserving another owner.
The combined766-check gate validates this alongside the native binding, recreation,
provider and shader-generation paths with zero core/synchronization errors.
Native retirement still uses the existing bit13 completion contract. Broader
memory/performance qualification and DX11 target integration remain open.


Reserved color history now shares one frame/device snapshot with fullscreen and
portable callbacks. The snapshot validates producer identity, exact previous
frame, epoch and render-size continuity. Capture retains the named owned shader
and appends its copied command after all AfterUpscale/AfterFullscreen consumers.
The captured HDR target is raster-sized, matching installed DX11 code; ordinary
AfterUpscale passes retain output-sized scaling. The focused125-check history gate
passes with zero validation errors, including current capture payloads, all three
consumer positions, odd resize, DRS and rejection/recovery. The final combined
actual-core gate passes891 checks with zero core/synchronization errors.


The shared catalog contract now centralizes all20 names/reservations and the six
canonical color formats plus legacy0/1. Pending DX11 target requests replay from
the core model; allocation/clear/retirement use the renderer queue and Keen's pool.
Cross-facade ownership and retirement prevent replay resurrection. The complete
PublishedBuffer reflection surface is available through the core; raw DX11 calls
remain device-only and Vulkan resources remain opaque. Structured Width is the
element count, with matching stride/availability metadata. Native target snapshots
invalidate after logical replacement or changed dimensions and report known DXGI
formats. The native catalog focus105 passes with zero validation errors; the
actual-core/linked-production DX11 WARP gate passes134 checks on both runtimes.
The final combined894-check gate passes with zero core/synchronization errors.
No native ABI change.


CPU upload mode changes now reset dimension metadata and release inactive CPU
payloads. Invalid RGBA sizes, mip chains and structured strides leave the prior
publication intact; structured strides follow DX11's aligned 2048-byte maximum.
Volume mip upload copies each logical mip extent and excludes trailing padding,
so later mip offsets match DX11 subresources and survive device recreation.
The upload-transition WARP gate passes143 checks on each runtime and the native
catalog focus passes109 checks with zero validation errors. The combined898-check
GPU gate passes with zero core/synchronization errors; no native ABI/library change.


### Classic fullscreen null SRVs — bit23

Ordinary fullscreen inputs now use genuine null descriptors for absent or
incompatible catalog images/buffers. Texture2D, Texture3D, Texture2DArray and
typed-buffer dimensions/reads are zero; StructuredBuffer dimensions report zero
elements and the shader's declared stride. Sampling a null image returns zero.
Compatible 2D/3D/structured resources still bind their actual payloads. Structured
uploads are not typed-buffer views. Extended volume/MRT inputs remain required.

This requires coordinated SE-Vulkan native feature bit23 and device nullDescriptor
support. ABI-1 packet layouts are unchanged. The compiled SPIR-V resource bindings
select the requirement; optimized-out unused declarations do not cause refusal.
Older hosts report capability-missing with an update message before native pipeline
allocation. ShaderPackRegistry.GetCapabilities().ClassicNullResourcesSupported
reports host/device support, independently of current-frame shader production.
No device robustness2 buffer/image access modes are enabled by this change.

The actual-core focused GPU gate passes100 checks with core/synchronization
validation enabled and zero errors, including both recording modes, missing
resources, type changes and unregister. Seven older-bit22-host checks pass against
the previously accepted native library. The shared CPU groups and both managed
builds pass. The final combined998-check GPU gate passes with validation enabled
and zero errors. Native CTest executes16 passes plus the configured reference
skip. Other native owners,
same-draw attachments, stage7 and broad automated stage8 remain open.


### Environment-family native owners (focused execution gate, 22 October)

Native bit24 adds captured atmosphere compute/main-view/probe owners512..514 to
the shared preparation/activation transaction. Changed compute generations dirty
cached LUTs while preserving their actual image layout and synchronization. Probe
atmosphere retains its two-target format and scope-specific stock inputs. Native
bit25 adds foliage768..772: one shared vertex module and two geometry/pixel pairs
form an indivisible generation closure. Each raster invocation binds the merged
module resource contract; execution counters identify every participating stage.
Layouts and pipelines retire together after their last completed submission.
Catalog t5..t9 and b6 extras remain shared host contracts. ABI-1 packets retain
their layouts. Older hosts receive coordinated-update refusal.

The integrated environment owner batch passes170 actual-core GPU checks with
core/synchronization validation enabled and zero errors. Independent outputs cover
LUT/main-view atmosphere, both probe MRTs on all six faces, and shared foliage VS,
each GS and each PS. Vertex/geometry/pixel-only catalog resources, first-use s1,
probe-scope constants, immutable extras, both HDR formats, synchronous/pipelined
execution, incomplete closure/interface/compile rejection, exact stock restoration
and completion-based retirement are covered. Managed net48/net10 and native builds
pass. The previous combined998 gate predates this batch; it is not expanded proof.
Aggregate named-stage/attachment capabilities remain false. Remaining stage6
owners/attachments precede stage7 and stage8, including matched Windows loaded-world
acceptance. Native mip blits do not establish arbitrary HistoryColor execution.

The coordinated native binding transport accepts all87 current owner rows within
its90-row bound; an oversized packet leaves the copied rows unchanged. Eight
real older-bit23-host checks prove actionable bit24/25 refusal before compilation
or native candidate allocation. This is focused stage6 evidence.


### Native decal pair generations — focused stage6 execution gate

Native bit26 adds five captured stock VS/PS pairs (1024..1033), corresponding to
native decal roles1,2,3,7,8. A pair prepares and activates together; fragment
invocations carry the merged vertex/pixel resource contract. The original
indexed-volume invocation, dynamic b2 batch offsets, per-channel GBuffer masks,
read-only depth and transparent OIT outputs remain native-owned. Catalog requests
can explicitly replace material t5/t6; absent requests retain their stock inputs.
Pipeline/layout retirement follows the last completed submission. ABI-1 packet
layouts remain unchanged. The module/row transport bound is100 (92 actual binding
owners after shared-module aliases merge). Older hosts require coordinated bit26
support before pack compilation or native candidate allocation.

The focused98-check actual-core GPU gate proves all ten modules with independent
GBuffer and OIT output equations, vertex/pixel catalog inputs, dynamic b2, atomic
activation, failed-compilation/incomplete-pair rollback, exact stock restoration
and retirement. Each role produces exactly matching synchronous/pipelined output, with zero
core/synchronization errors.
The updated170-check environment gate passes with all92 binding-owner rows and
oversized-packet preservation. Ten real older-host checks cover bits24/25/26.
Native CTest executes16 passes plus its configured reference skip; managed net48/
net10 builds and focused CPU native-stage/binding contracts pass. The extended
full recreation gate now includes these fixtures but has not been rerun for this
batch. This evidence does not establish every legal shader interface, other native
stages, full attachment semantics or parity. Both aggregate named-stage and same-draw attachment flags
remain false. Stage7 and stage8 follow the remaining stage6 work.


### Native probe processing generations — focused stage6 execution gate

Native bit27 extends the generation transaction to captured probe-processing
owners1280..1283: prefilter, forward sky, six-MRT publication copy and stock-dormant
factor blending. Selected generations activate at one frame boundary and old
pipelines/layouts retire after completed submission. Native scope binds the
actual probe b0 and complete144-byte forward b2 (view, projection, sun, size)
when a replacement reads beyond the stock64-byte view. Stock uploads retain
the64-byte path.
Catalog t5..t9 overrides are explicit; unrequested stock input slots retain their
ownership, including the publication copy's sixth-face t5. Native mip/base-level
image copies remain separate from named shader execution. ABI-1 packets retain
their layouts; module/row capacity is104 with96 possible descriptor owners.

Installed DX11 MyEnvironmentProbe fixes BLEND_USING_FACTOR=false and invokes
EnvProbeCopy with blendStrength, so EnvProbeBlend can activate but has zero GPU
execution in that stock path. Its compilation/activation must never imply
production. This does not claim dispatch support for dormant EnvProbe compute
entry points or complete geometry/EnvProbe parity.

The focused326-check actual-core GPU gate passes independent per-face/per-mip
equations on all six probe cubes, exact synchronous/pipelined outputs, held-capture
execution status, frame-copied catalog changes, invalid compilation/MRT rollback,
exact stock restoration and retirement with zero validation errors. The existing
stock-probe regression passes427 checks with zero errors, and six real bit26-host
checks prove actionable bit27 refusal before compilation/allocation. Both managed
frameworks and the native build pass. The final combined496-check environment/probe gate passes all96 binding-owner
rows and oversized-packet preservation with zero errors. Native CTest and focused
CPU native-stage/binding contracts pass; the broader recreation suite includes
the new fixtures but has not been rerun for this batch.
Both aggregate named-stage and same-draw attachment capabilities remain false;
remaining stage6 work precedes stages7 and8, including Windows live acceptance.


### Native particle generations — focused stage6 execution gate

Native bit28 adds the complete particle closure1536..1545: eight compute variants
and the shared VS/PS render pair. A selected particle source rebuilds the entire
list-protocol family and activates it atomically at a frame boundary. Old
pipelines/layouts retire together after completed submission. Workgroup sizes,
64/464/4-byte native storage layouts, typed argument formats and stage-specific
inputs validate before activation. Stock buffer ownership and OIT/depth behavior
remain native. VS descriptor bindings retain their1024-offset relocation; merged
logical t5..t9 reservations and b6 extras bind the corresponding physical slots.
The module/row bound is114 with105 possible binding owners after aliases merge.

Direct nonzero dispatches report submitted participation separately from
completion. Indirect simulation/raster participation is credited only when
completed GPU argument readback proves nonzero X/Y/Z and index/instance counts,
with the original
frame and matching generation token. Empty work cannot advance those execution
records; stale generations cannot credit a newer program.

The checked40-byte ABI-1 particle readback request validates size/version, buffer
kind, aligned range and a64KiB bound outside recording. The diagnostic native
buffers stay renderer-owned; this adds no pack discovery or execution host.
Older hosts actionably refuse bit28 before compilation, candidate allocation or
readback symbol lookup. Existing packet layouts and DX11 callback signatures stay
unchanged.

The focused84-check actual-core GPU gate proves all eight compute modules with
independent GPU sentinel writes and the raster pair with expected OIT pixels,
VS/PS/compute resources, empty index/instance/X/Y/Z participation and recovery,
wrong workgroup/
stride/stage-input refusal, malformed readback size/version/range, rollback, exact
stock restoration and retirement. The existing216-check stock transparency and
particle lifecycle gate and updated170-check environment/105-row transport gate
pass, all with zero core/synchronization errors. Seven real previous-bit27-host
checks pass. Both managed frameworks, native CTest and focused CPU contracts pass.
The extended full recreation fixture compiles but has not been rerun for this
batch. Both aggregate flags remain false; this does not close all legal shader
interfaces, remaining native owners, same-invocation attachments or full parity.


### Native screen-shadow generations — focused stage6 execution gate

Native bit29 adds CSM evaluation1792 and optional per-view usage statistics1793
to the captured-source generation transaction. Each native owner receives its
actual frame/screen/cascade inputs, shared reserved t5..t9 and b6 extras, fixed
16x16x1 dispatch contract and native output. Activation moves complete pipeline/
layout bundles at a frame boundary; retired generations wait for completion.
ABI-1 module/state packets remain unchanged. Capacity116 includes107 possible
binding-owner rows. Host capability checks now follow ascending feature versions
so global defines report the first unavailable owner contract consistently.

DXC encodes HLSL unorm<float> storage as R32f. The derived CSM executable adapts
that storage-image type to the owning R8_UNORM target and adds the required
StorageImageExtendedFormats capability, already enabled by SE-Vulkan. Original
authenticated shader artifacts remain intact. Other storage formats are refused.

The43-check actual-core GPU fixture passes independent CSM UNORM equations, exact
per-cascade usage counts, frame catalog updates, atomic activation, synchronous/
pipelined output, inactive usage status, compile/workgroup rollback, exact stock
restoration and retirement with zero core/synchronization errors. Native and
managed net48/net10 builds pass. Six real bit28-host refusal checks, the170-check environment/107-row transport
gate and36-check stock shadow-occlusion regression pass with zero validation
errors. Native CTest executes16 passes plus the configured reference skip;
focused CPU contracts and the developer canvas check pass. The extended full
recreation fixture compiles but has not been rerun for this batch.
This covers two screen-space compute owners; shadow geometry, other Shadows
sources and full same-invocation attachments remain separate stage6 work. Both
aggregate capability flags remain false. Stage7 and stage8 follow stage6.
