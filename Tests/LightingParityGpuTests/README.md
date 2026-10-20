# Stock direct PBR cross-API reference

From the repository root:

```powershell
dotnet run --project Tests/LightingParityGpuTests -c Release -p:RunPostBuildEvent=Never -- '<installed game Bin64>'
```

The identical installed `Lighting/Light.hlsli` MainDirectionalLight kernel runs
through DX11 WARP and Vulkan hardware with identical copied stock frame bytes.
256 cases sweep four metalness, gloss, view and normal values. Results are FP32;
the fixed tolerance is `absolute <= 1e-4 + 2e-4 * abs(reference)`. Nonfinite data,
missing validation or Vulkan validation errors fail. Artifacts are under
`TestResults/LightingParityGpuTests`.
Add `--hardware-reference` to run the same 256-case kernel on the actual DX11
GPU in `TestResults/LightingParityGpuTests-Hardware`. This isolates direct PBR
math; it does not include the implementation-dependent cube filter kernel.

The test uses the repository's current Release native DLL in private staged
assets, the normal Vulkan shader compiler/runtime preparation, and existing
extended compute guest transport. It does not modify normal deployed assets.

This is a kernel oracle, not a full renderer comparison. It excludes scene
surface data, shadow visibility, environment probes, exposure and presentation.
Captured ambient investigation can replay the installed stock ambient shader
on DX11 WARP with each backend's saved GBuffer, frame constants, screen AO,
sun visibility, published close/far probes and the installed RG16F BRDF LUT:

```powershell
dotnet run --project Tests/LightingParityGpuTests -c Release -p:RunPostBuildEvent=Never -- '<Bin64>' --captured-ambient '<DX11 lighting-buffers folder>' '<Vulkan lighting-buffers folder>'
```

Results go to `TestResults/CapturedAmbient/<run ID>`. The six float planes are
described in `scope.txt`. `--swap-probes` deliberately exchanges the inputs
and writes a separate `-swapped` result for diagnosing descriptor reversal.
`--linear-cube` selects a trilinear clamp cube control in a separate
`-linear-cube` folder. `--terms` appends diffuse/indirect-AO and direct/shadow
planes in a separate `-terms` folder for comparison with native FP32 terms.
`--hardware` repeats the same copied-input replay on a real DX11 adapter in a
separate `-hardware` folder, recording its driver and adapter in `scope.txt`.
WARP sampling agreement alone does not establish hardware DX11 agreement.
The one-mip BRDF lookup uses explicit LOD zero because compute shaders have no
implicit derivatives. Stock CPU frame storage is compiled column-major; native
frame storage is compiled row-major. No exposure fitting or resizing occurs.
This excludes local lights, transparency and later composition; it diagnoses
ambient terms and does not establish complete renderer parity.

`--cube-sampling` runs a separate 6,144-case authored-cube filtering control.
Explicit fractional LOD samples through the authenticated runtime path must be
bit-identical to the native isotropic sampler; explicit gradients must retain
anisotropy. `--cube-sampling-raw` bypasses only the DX11 cube-LOD adaptation to
retain a separate before control. Both report WARP comparisons and require
zero enabled core/synchronization validation errors. Their private output
folders are `TestResults/CubeSampling` and `TestResults/CubeSamplingRaw`.
The control additionally accepts `--hardware` for the actual DX11 GPU,
`--mip-ramp` for spatially constant, mip-encoded colors, and `--unit-direction`
for normalized cube directions; each writes a separate suffixed directory.
`--max-one-sampler` requires the raw route and changes only the extra native
term-pass sampler. It checks unchanged nonzero main HDR and exact restoration
after diagnostics are disabled. Isotropic gradient controls are measurements,
not an accepted parity mapping. Sampler equality against WARP is insufficient
for a hardware DX11 claim.
