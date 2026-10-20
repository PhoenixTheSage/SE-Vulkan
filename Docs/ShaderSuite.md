# Installed vanilla shader suite

The catalog covers the current Space Engineers 1.210.014 PC renderer's installed shaders. Its scope is **SPIR-V compilation, validation and reflected layouts**. Native render-pass integration and visual verification are independent milestones. The Creative world now uses stock material/depth/lighting shaders with native HDR/depth composition; sprites and billboards retain their existing native paths.

## Inventory and reproduction

`Tests/ShaderSuiteCapture` loads the installed renderer assembly and calls only its CPU cache/permutation builders. It does not initialize `MyRender11`, invoke shader creation, execute the queued compilation list, or create a D3D device. It captures the actual cache annotations, material restrictions, vertex-component declarations and ordering. It supplements these with the runtime GeometryStage2 technique/pass/texture combinations, old-renderer texture-index and impostor flags, and merge/single-instance paths.

The resulting runtime catalog contains **20,736 unique variant records across 118 HLSL entry-point files**, with a hash audit of all **217** shader/include/configuration files. Entry points, source profiles, definitions, vertex layouts, origins and source content hashes are recorded. Counted variants include the stock generator's broad precompile combinations, which are wider than a single world's active shaders. This is a versioned vanilla inventory, not an exhaustive enumeration of arbitrary content-mod macros or future game versions.

Five installed files are unreferenced legacy sources, kept explicitly in the inventory rather than silently omitted:

- `EnvProbe/EnvProbe.hlsl`: marked `@skipCache`, has no current shader creation references, and calls obsolete GGX helper names. The current probe shader entry points are separate catalog rows.
- `Geometry/Materials/AlphaMaskedArray/Vertex.hlsl` and `Pixel.hlsl`: absent from the stock material and technique mappings. Current array/impostor variants use `AlphaMasked`.
- `Geometry/Materials/Test/Vertex.hlsl` and `Pixel.hlsl`: refer to obsolete interfaces and material members and are absent from current material maps.

These five files are **not converted or counted as runtime support**. Unknown unclassified HLSL files prevent packaging. Shader-suite readiness does not claim every historical file in the install is compilable.

Run from this repository with the installed game and previously provisioned compiler tools:

```powershell
.\scripts\build-shader-suite.ps1 -GameBin 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64' -Package
```

The runner writes its inventory, bounded compiler diagnostics, progress checkpoints, complete results and material-interface verification under `TestResults/ShaderSuite/Current`. Packaging requires every captured runtime variant to validate and produces `Assets/Shaders/Vanilla` plus `Docs/ShaderSuiteCoverage.json`. The source HLSL remains in the user's game install; it is not copied into the package or repository. Generated SPIR-V and metadata are local assets, excluded from Git pending an immutable release distribution.

## Translation contract

Pinned DXC produces SPIR-V 1.6 for `vulkan1.3`; pinned SPIRV-Tools validates for **Vulkan 1.4 with scalar block layout**. Compilation uses legacy macros, row-major DirectX layouts, automatic resource shifts and DirectX pixel-position W behavior. Raster Y orientation is the pipeline/viewport's responsibility.

Descriptors retain source register spaces with separate b/t/s/u ranges starting at **0/128/256/384**. Append/consume/structured UAV counters occupy **512 + source UAV index** in the source descriptor set. Counter-only access may omit the owner's data descriptor; its logical owner and counter binding are still reflected. This separate counter descriptor follows [DXC's SPIR-V resource contract](https://github.com/microsoft/DirectXShaderCompiler/blob/main/docs/SPIR-V.rst). No unused included resources are retained merely to preserve register declarations: vanilla includes intentionally alias inactive resources.

The compatibility mirror applies ordinary substitution to the HBAO header's invalid punctuation token-pasting syntax. The unused stock `TemperatureFilter` missing-return diagnostic remains a warning; its code is not replaced. Three **exact-source-hash-guarded** repairs address stock texture-array defects: the undeclared alpha layer index, an instancing/texture-index semantic collision, and the GBuffer decal branch's incorrect 2D resource name. Changed game files and content mods do not receive those hash-specific repairs. Applied fixes are recorded beside raw dependency hashes in metadata.

Reflection includes descriptor kinds, constant/storage buffer layouts, array/matrix strides, push constants, stage inputs/outputs and interpolation flags, execution models, compute workgroup sizes, SPIR-V capabilities/extensions and UAV counter ownership. All **9,528** material vertex/pixel partners have matching reflected locations/types and all **11,332** shared descriptors have matching complete layouts. Vulkan pipeline formats, descriptor contents, synchronization, stream output/transform feedback and actual images require native pass implementation and validation. A compiled module's capabilities are requirements for its future native pass; compilation alone does not enable those device features or extensions.

## Cache, package and diagnostics

Bulk compilation uses one immutable source mirror and a frozen tool fingerprint; ordinary compilation continues to detect edits to source, includes, definitions, tools, ABI and translation contracts. The suite checks installed source hashes before and after the run and checks each compiled dependency against the captured inventory. Existing cache entries are revalidated before reuse.

The package deduplicates identical SPIR-V binaries while retaining each variant's source/definition identity and cache key. Its compressed catalog records source, module and metadata hashes. `VulkanShaderLibrary` rejects changed game assemblies/content, catalog tampering, missing assets, invalid paths and corrupt loaded modules. It supplies reflected artifacts for later pass integration; it does not create pipelines or mark effects Active.

Pulsar status contains **Vanilla shader conversion**, **Vanilla Standard GBuffer**, **Vanilla shader pass integration** and **Upload scheduling**. A matching packaged catalog reports conversion Ready with zero observed frames. Preflight verifies the selected stock/supplemental artifacts and native extensions before activation. Startup creates forty material/depth pipelines and the stock directional/point/spot lighting passes. Observed world, material draw, instance, skinning, cube, lighting and upload counters promote the applicable subsystems to Active. Visual parity remains a separate acceptance decision.

## First native stock material pass

The additive ABI-1 extension in `Native/src/stock_gbuffer.inc` executes the installed ordinary packed-layout **Standard** and **AlphaMasked** GBuffer variants. It reflects vertex inputs, stages, entry points, descriptors and constant-buffer sizes before creating a dynamic-rendering pipeline. Unsupported extensions/capabilities are rejected by this pass. The shader catalog retains all other variants for their later feature-gated passes.

The three color attachments match `MyGBuffer`: RGBA8 sRGB base color/LOD, A2B10G10R10 UNORM view normal/AO and RGBA8 UNORM metalness/gloss/emissivity/coverage. Depth is D32 float with reversed comparison and a zero clear. The pass binds source register mappings, the stock 8x8 dither and reflected 1280-byte Frame, 64-byte Projection and 128-byte Object blocks. `ReflectedConstantBuffer` writes nested fields using declared offsets and rejects incompatible names, types and sizes.

Meshes and constants are copied before the C ABI returns. Mesh resources persist until replacement, kind-2 removal, pipeline reset or renderer destruction. Material textures use the existing texture namespace. Object constants use aligned dynamic-buffer offsets; one batch submission and wait covers the complete independent correctness pass. The production world pass described below uses asynchronous resource uploads and fenced graphics slots.

`Tests/GBufferGpuSmoke` passes **30 checks** on the local Vulkan 1.4 GPU with core and synchronization validation: **10 Standard frames / 11 draws**, then **3 AlphaMasked frames / 3 draws**, with **zero validation errors**. Readbacks verify material channels, matrix layouts, projection/normal transforms, batched object offsets, depth ordering, exact alpha cutouts, construction dithering, resize/clear, replacement, removal and invalid inputs. Evidence is in `TestResults/GBufferGpuSmoke/result.json`; the checked-in pass registry is `ShaderPassCoverage.json`. These fixture draws do not establish game-world visual parity.

## Driver-facing SPIR-V

DXC's `-fspv-reflect` retains HLSL semantic, user-type and logical counter annotations in the authenticated catalog. Their declarations require optional Vulkan Google extensions when submitted directly to a driver. `SpirvRuntimeModule` verifies the original module hash and removes only those reflection annotations and their two declarations from a copied driver-facing module. Actual descriptor bindings, UAV counter buffers, offsets, interfaces and executable capabilities remain unchanged. The source contracts are [HLSL functionality annotations](https://github.khronos.org/SPIRV-Registry/extensions/GOOGLE/SPV_GOOGLE_hlsl_functionality1.html) and [user-type annotations](https://github.khronos.org/SPIRV-Registry/extensions/GOOGLE/SPV_GOOGLE_user_type.html).

`Tests/RuntimeShaderTests` prepares all **1,401** catalog modules, checks idempotence, compares reflected executable layouts/interfaces/capabilities and validates every result for Vulkan 1.4. All **1,401** pass; optional execution features/extensions for future passes are preserved and still need device enablement. Runtime preparation does not change the catalog, cache identities or source reflection metadata.

## Production stock world pass

`world_pipeline.inc`, `world_render.inc` and `upload_batches.inc` implement forty stock material/depth pipelines, stock fullscreen directional/point/spot lighting, the three source GBuffer attachments and reversed depth. The stock shaders consume imported packed normals/tangents, material DDS maps, reflected 1280-byte Frame/64-byte Projection constants and 128-byte or 3968-byte Object constants. Generic/static groups use 64-byte hardware instance streams; cubes preserve packed rotation/bones/pattern offsets; characters use 60-matrix palettes with persistent remapped geometry. Four cascade depth passes reuse the same GPU deformation streams. Native glue resolves the first-slice PCF shadow mask and composites fixed-exposure HDR/depth before billboards and HUD.

Eight world/depth, seven glass and seven Holo combinations are absent from vanilla's precompile annotations. `Tests/WorldShaderPackage` builds their forty-four stages from the same installed Standard/AlphaMasked/Glass/Holo sources and generated declarations. `Extra/manifest.xml` authenticates binary, metadata and definition hashes; its hash is recorded in `coverage.xml`. The supplement is separate from the unchanged 20,736-row stock inventory. It is packaged before startup, so rendering does not launch DXC. `build-shader-suite.ps1 -Package` also builds this supplement.

The packaging command also runs `Tests/LightTileShaderPackage` for the derived
stock depth/frustum light selector. Its `Ordered4096` contract uses a 4096-bit
selection mask and preserves ascending accumulation order. It supports the
stock 256/512/2048/4096 point-light quality budgets; the frame declares the
matching tile stride. `LightTiles/manifest.xml` authenticates source,
binary and metadata, with its checksum in `coverage.xml`. Generated assets
remain outside Git and must be regenerated when this transformation changes.

`Tests/WorldPassGpuTests` passes **61 checks**, including all twenty main material variants, bone-palette-only animation, two generic instances in one draw, packed cube rotation/deformation, point/spot lighting, shadow replay, copied packet ownership, pending upload replacement/removal, minimize/resize/restore and unload. Its **41 frames / 41 material draws / 43 instances / 83 lighting passes** have **zero core/synchronization validation errors**. Four initial geometry/material uploads use one asynchronous batch. A production scene bridge separately imports actual armor/astronaut MWM and skybox DDS and verifies changed pixels and complete resource unload.

Stock lighting integration currently uses a neutral diffuse ambient cube, conservative point-light tile lists and unshadowed spotlights with a neutral reflector mask. Prefiltered/specular probes, BRDF LUT integration, full stock cascade filtering, GPU light culling, broad material/transparency visual parity and remaining postprocess passes remain pending. The voxel and main-view environment integrations are described below. Compiled modules for those systems do not count as implemented effects.

## Stock asteroid integration

`scripts/generate-voxel-variants.py` selects eight existing validated catalog stages for TriplanarSingle/TriplanarMulti main and depth partners. No replacement of stock voxel HLSL was needed. Together with the 40 world pipelines these form 44 stock material/depth pipelines. A ninth custom glue shader, `voxel_copy_ps`, normalizes mixed DDS textures into voxel arrays while preserving alpha and color encoding. Compilation coverage remains 20,736 variants / 1,401 unique modules; integration coverage is recorded separately in `ShaderPassCoverage.json`.

## Stock planet environment integration

`scripts/generate-environment-variants.py` selects nine exact catalog stages: atmosphere precompute CS and high-quality PS, cloud VS/PS, foliage VS, grass GS/PS and rock GS/PS. They execute in `environment.inc` using reflected register/layout contracts. No additional stock shader conversion or HLSL redistribution is required. Main-view atmosphere uses a cached optical-depth LUT, clouds blend with read-only reversed terrain depth, and persistent foliage points expand through stock geometry shaders into the GBuffer. World/sky/cloud/atmosphere fog and foliage wind consume copied simulation settings.

Atmosphere probes/MSAA/quality variants, weather-particle visual parity, adaptive exposure and complete visual parity remain pending. The stock point-generation distribution currently runs once per cell replacement on the CPU; the catalog's stream-output GS is retained for a future native generation optimization. See `Planets.md` and the 35-check environment/42-check voxel GPU suites for scope and evidence.

## Stock transparency integration

Phase #4 selects stock glass main/depth-normal passes, 32 billboard pairs, five decal roles, ten particle stages and OIT resolve. Fourteen supplemental glass/Holo pairs support cube/deformed main paths and all ten depth-normal streams. Compilation coverage remains **20,736 variants / 1,401 unique modules**, with **44 supplemental stages** tracked separately. Particle VS descriptors are moved into a disjoint +1024 binding range on an authenticated executable copy. All 1401 modules validate after reflection-annotation removal and vertex binding relocation; native reflection checks actual relocated descriptor contracts. See `Transparency.md` for execution and visual-acceptance scope.

## Phase #5 selection

The catalog remains 20,736 variants / 1,401 unique modules. Runtime initialization additionally selects stock probe capture/filter/atmosphere, BRDF texture, CSM/spot shadows, postprocessing/AO and highlight passes. The supplemental package now contains 128 stages (64 pairs), including source forward and highlight combinations. There are also 128 selected postprocessing/blur stages; these are catalog selections, not new supplemental binaries. See [Phase #5 contracts](Postprocessing.md) and the independent GPU fixture evidence.

Run `python scripts/update-phase5-coverage.py` after repackaging the catalog or changing production stage selections. The registry currently identifies 316 source-selected stock catalog variants; this is separate from per-variant runtime and visual verification.
