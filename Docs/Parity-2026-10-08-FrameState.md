# Submitted frame-state comparison

Private paused saved-camera DX11 capture37e31612 and Vulkan captureb9d4b3d8
record actual 1280-byte CPU frame blocks. DX11 is hooked after MyCommon updates
its constants; Vulkan is hooked on the main frame builder's returned bytes.
Shader member offsets decode corresponding scalar/vector fields. Probe frames
are excluded from main-view capture. Independent loads/sun timing and matrix
storage conventions prohibit a whole-block equality or causal image claim.

The audit finds forwardPassAmbient0.3 in DX11 but0 in Vulkan's main frame.
Stock MyCommon adds MyEnvironmentProbe.LastAmbient to the copied environment light.
Vulkan already gathered the matching value for probe capture but passed the
unaugmented light to its main frame. Main frame and probe configuration now use
the same augmented light. The corrected paused capture359eaae6 records exactly
0.3 at the shader ABI offset, matching DX11. DistanceFade is3000 in stock and0
in Vulkan; the builder now copies renderConfiguration.User.DistanceFade, and
corrected capture records3000. The installed vertex shader uses this threshold
and its tenth as fade width, so zero was not a neutral substitute.

Audited sun color, diffuse/specular/gloss factors, ambient diffuse/specular,
skybox/atmosphere brightness and texture multipliers match. Sun direction differs
slightly with independent load time. world_offset differs intentionally at this
boundary: stock stores world camera offset, while native inputs already use
camera-relative transforms. Do not copy that field blindly and subtract twice.

The actual stock probe settings are DrawDistance130, TimeOut0.3, resolution512,
AmbientScale0.1, min0.02/max0.3. Vulkan's forced minimum five-second hold after a
capture is not stock scheduling. Stock counts timeout from cycle start and uses
staged capture/postprocess/mips/prefilter; zero timeout processes all faces each
frame. The method named RenderTransparent delegates to RenderForward; tracing
that installed implementation shows atmosphere only, not glass/billboards.
Native already has this atmosphere pass, so the method name does not prove
missing transparent probe content. The scheduling contract remains open and
is not fixed by this frame-state correction.

Both corrected net48/net10 builds pass. World GPU suite370 checks and scene113
checks pass with zero synchronization validation errors. Corrected actual bytes
match the intended fields; no frame-speed gain claimed. Viewing the fresh paused
images still shows darker walls, glossier/darker floor, overbright LCD and
particle differences. Full DX11 pixel parity remains unproven and not achieved.

Evidence: FrameState-Comparison.json, FrameState-Corrected-Comparison.json,
FrameState-Dx11-Launch.json, FrameState-Vulkan-Launch.json,
FrameState-Corrected-Launch.json and private captures' frame-state.json/.bin and
view.png. The new harness diagnostic reports actual CPU constants, including
thread IDs, separately from performance sampling. Source checkpoint unchanged.
The full <4.17 ms total-frame target remains open; accepted async-default gain
is retained. Next macro scope must address scene/packet/command work and full
probe scheduling/lighting ownership without trading away stock quality.
