# Authored MWM tangent basis — parity correction, 2026-10-07

Retained source-consistent importer correction: rendering now consumes stock
Binormals rather than requesting an absent Bitangents tag and always generating
a replacement basis. Authored tangent vectors/sign are packed using the stock
cross(tangent,normal) dot binormal convention, without extra Gram-Schmidt/
normalization. Generated fallback remains for incomplete/runtime geometry.
The same Binormals stream already informed CPU new-model eligibility; this change
integrates it into rendering. No guessed lighting multiplier, shader/quality/LOD
threshold or framebuffer reduction introduced.

Proof

Exact packed tangent/sign bytes match the stock authored inputs and VF_Packer
for all vertices in Shelf_1, InteriorWallSide, CylindricalColumn and SquarePlate.
Scene suite113checks passes, zero core/synchronization errors, including
model/material/LOD/group/highlight/fallback/current per-view/spot and batching paths.
Native sign convention was already correct; the defects were metadata selection
and modifying the authored tangent before repacking. First test compile exposed
namespace aliases (VF_Packer in VRage.Import; importer/descriptors in
VRageRender.Import), corrected before actual validation. No test result from that
failed compile accepted.

Private paused full1080p capture98dcfb... compared with owning-pass DX7147ab...
shows geometry remains plausible, but walls remain darker, floor darker/glossier,
LCD overbright and particle behavior different. It does not prove full DX11 parity
or resolution of the floor/lighting issue. Independent loads/timing/settings can
still affect pixels; packed-byte proof is narrower than visual acceptance.
Source checkpoint unchanged. No ordinary FPS comparison performed for this
one-time import correction; no frame speedup claimed. Previous experimental
10.63–10.95ms timing predates it and remains its original scoped evidence.

Evidence: AuthoredBasis-Checks.txt, SceneSubmissionGpuTests/result.json,
AuthoredBasis-SceneLaunch.json and Vulkan98dcfb.../Reports/view.png; DX7147ab.../
Reports/view.png. Both net48/net10 Release builds pass/deploy via canonical paths.
No C++/GPU queue or descriptor lifetime change. Source MyMwmData.LoadFromFile/
CreateAlteredTangents and installed shader vertex unpacking are primary references,
not copied implementation. Larger lighting/probe/exposure/material and particle
matching still remains. The full <4.17ms + DX11 pixels goal is active.

CPU structural work remains open: complete actor/packet preparation and ownership
reuse/scheduling under the revised mixed-pipeline oracle, using actual phase/stack
profiling. This parity correction does not replace the original performance goal.
