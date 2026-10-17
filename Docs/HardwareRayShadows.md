# Hardware ray shadow repair, 2026-10-15

The original hardware ray bridge added unshaded sun and point-light RGB to the
already-lit HDR image. It did not evaluate albedo, surface normals or the BRDF,
and duplicated lighting supplied by the stock deferred passes. Its geometry
budget caused that contribution to appear or disappear as camera culling changed.
This produced the Pertam flat-gray image and dark silhouettes.

Independent correctness faults accompanied that integration error:

- Rigid-object transforms were read at byte zero; the authenticated stock
  shader puts their rows at offsets 32, 48 and 64. The producer now uses reflected
  offsets from the selected pipeline.
- Packed 3x4 rows were transposed again when building instances. They already
  match Vulkan's row-major affine layout.
- The queued inverse view-projection was used as an inverse projection and
  multiplied by an inverse view again, applying camera rotation twice.
- Sun rays followed the light's incoming direction instead of tracing toward it.
- Sequential BLAS builds reused scratch with no destination write dependency.
  Scratch address alignment was also hardcoded rather than queried.
- One descriptor set was updated across frame slots. Sets now belong to the
  completed frame slot, and BLAS backing buffers are freed during cleanup.

The corrected pass follows stock shadow resolve and precedes directional
lighting. It reconstructs camera-relative world coordinates once and writes
only additional certified opaque sun occlusion into the existing R8 visibility
mask. Stock lighting evaluates materials, BRDF, ambient, local lights and exposure.
Unblocked rays leave native visibility untouched. Disabled, unsupported, empty
or over-budget cases retain native lighting and shadow coverage.

The TLAS currently includes visible, opaque rigid/generic world draws. Native
cascades retain voxel, skinned, packed-cube, cutout, distant and off-screen
coverage. Alpha-tested geometry is excluded from opaque certification. This is a
conservative sun-shadow supplement: hardware point/spot tracing, reflections,
indirect illumination and the software temporal reconstruction stack are not
implemented by this pass. Storage Exposure is not an extra hardware light gain;
the renderer's normal exposure controls its material lighting.

`Tests/RayShadowGpuTests` uses real Vulkan ray queries on a colored receiver and
offset blocker. Four camera rotations and both rigid/generic transforms verify
real receiver shadows, identical unblocked/background pixels, budget fallback,
disabled shadows, pending frame slots, and clean synchronization/cleanup.
Run it with the pinned validation layer and synchronization validation enabled:

```powershell
dotnet run --project Tests/RayShadowGpuTests -c Release -p:RunPostBuildEvent=Never -- "T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64"
```

The CPU ray-policy target also checks affine rotation/translation against a
known transformed point. Numerical source-of-truth contracts:
[Vulkan instance transforms](https://docs.vulkan.org/refpages/latest/refpages/source/VkTransformMatrixKHR.html)
and [acceleration-structure scratch synchronization](https://docs.vulkan.org/spec/latest/chapters/accelstructures.html).
