# Native Vulkan engine

`scripts/build-native.ps1 -Configuration Release -Test -Deploy` builds the x64
library, validates shaders, runs native tests, and places local runtime assets
under `Assets`. CMake 3.24+, Visual Studio C++20 Build Tools, Git, Python, and a
Vulkan driver are required. A Vulkan SDK installation is unnecessary.

Linux builds use `cmake -S Native -B Native/build -DCMAKE_BUILD_TYPE=Release`, then
`cmake --build Native/build --target se_vulkan_native sevk_smoke` and
`ctest --test-dir Native/build --output-on-failure`. Package only this project's
artifacts with `cmake --install Native/build --component SEVK --prefix Native/package`.
The CI includes Ubuntu and Windows Debug/Release builds. A GPU test returns skip
code 77 when the host has no Vulkan 1.4 device; this is not a rendering pass.

On Windows, `scripts/build-native.ps1 -CrossLinux -Configuration Release -Deploy`
builds an ELF64 library using SHA-verified Zig 0.14.1 and the already-built host
shader tools. `-Configuration Debug` builds a separate Linux debug tree. These
are cross-compilation checks; Linux runtime, X11, and Wayland tests still require
a Linux host. The target is x86-64 glibc 2.31.

`scripts/build-linux-tools.ps1 -Deploy` packages pinned Linux DXC and cross-built
SPIRV-Tools under `Assets/Tools/linux-x64`, retaining `bin`/`lib` and licenses.
Upstream DXC requires glibc 2.34. These optional compilation tools are separate
from renderer startup. `scripts/verify-native-assets.py` checks packaged PE/ELF
architecture, all native exports and local hashes.

The ABI is specified in `include/se_vulkan.h`. Managed callers own the render
thread, serialize API calls, and submit resource uploads before `begin_frame`.
Draw data is copied. Texture and mesh destruction is deferred against a GPU
timeline. UI sprites use pixel coordinates and normalized `(x,y,width,height)`
UV rectangles; negative scissor width disables clipping. Matrices are row major,
camera-relative, with DirectX clip Z in `[0,1]`. Main rendering uses reversed Z,
so the bridge converts forward game depth to `W-Z`. Shadow matrices use ordinary
forward depth. Negative Vulkan viewports handle Y; do not also invert matrices.

The renderer uses Vulkan 1.4 core, dynamic rendering, synchronization2, timeline
semaphores, VMA device memory, staging uploads, binary acquire/present semaphores,
shader reflection, and a validated driver-specific pipeline cache. It supports
RGBA/BGRA and BC1–BC7 mip-chain uploads, textured indexed geometry, alpha cutouts,
ColorMetal/AddMaps paint and emissivity, normal maps, dithered previews, point/spot
lights with source falloff/diffuse metadata, four 1024-pixel depth shadow cascades with PCF, skybox geometry, fixed
exposure ACES tone mapping, alpha and additive geometry/sprites, sprite
scissors/rotation, and sampled sRGB offscreen LCDs. Geometry uses premultiplied
blend factors; AdditiveTop (flag 2048) bypasses scene depth, while ordinary
additive geometry respects it. Sprite ABI flag zero retains straight-alpha
blending; flag 2 accepts premultiplied texture input and flag 4 converts straight
input to premultiplied output. Both stock modes multiply linear tint RGB by tint
alpha and blend with ONE. Main presentation/offscreen attachments and LCDs use
sRGB formats so blending happens in linear space and display encoding happens
once. Surfaces without RGBA8/BGRA8 sRGB support are rejected with a diagnostic.
The original texture upload export remains a one-level operation; the mip-chain
export checks exact tightly packed level sizes and exposes all levels to sampling.
Sprites use stock linear clamp sampling; world materials use wrap sampling.
The offscreen aspect export keeps the physical attachment extent while using
the game's logical viewport for sprite coordinates and clipping. Scissor
endpoints scale independently, and rotation occurs before viewport scaling.
Imported tangent bases, full metallic/gloss BRDF, stock texture anisotropy/LOD
bias, and complete postprocessing remain parity work; derivative normal mapping
uses the stock no-precomputed-tangent X convention. Skybox cube-face loading
currently retains level zero, while ordinary two-dimensional DDS files retain
their mip chains.
The managed bridge preserves stock HalfVector4 packet precision before viewport
scaling. Vertex and index uploads share one staging allocation/submission/wait
per mesh, with distinct transfer-to-vertex and transfer-to-index barriers.
Staging uploads and LCD rendering currently wait for their upload timeline;
asynchronous streaming/batching is later performance work.

Windows presentation attaches to the game's HWND. Linux supplies SDL3's instance
extensions, `SDL_Vulkan_GetVkGetInstanceProcAddr`, and a surface callback. The
caller must invoke SDL surface creation on the window's owning thread; native code
does not import SDL or create another window. The callback-created surface is
owned and destroyed by this engine, while the SDL window is owned by the game.

Screenshots call `sevk_request_readback` before `end_frame`; the renderer copies
the acquired surface image before presentation. Afterwards `sevk_readback` waits
only for that capture and returns RGBA8 using actual `SevkStats.width/height`.
Reading a presented swapchain image without first requesting capture is rejected.
Offscreen test contexts can also read their last completed frame directly.

Pinned upstream dependencies are in CMake: Vulkan-Headers 1.4.321, volk SDK
1.4.321.0, VMA 3.3.0, SPIRV-Tools/Headers/Reflect SDK 1.4.321.0, and DXC
1.8.2505.1. DXC Windows/Linux archives have enforced SHA-256 checks. SPIR-V 1.6
is compiled for DXC's Vulkan 1.3 environment and validated against Vulkan 1.4;
scalar block layouts are explicitly enabled. Runtime startup uses the compiled
shader assets and needs no shader compiler. Optional content-shader tooling is
packaged separately with its upstream licenses.

Native tests verify ABI packing and invalid inputs, reject a mocked older loader
and physical device, and reject each missing required feature independently.
An API 1.4 device with all optional host-copy/local-read features absent reaches
device creation with the six used features explicitly enabled and unused 1.4
features disabled. The mock verifies cleanup before the real GPU tests run. Tests
assert actual GPU pixels for sprites/meshes/LCD/local lights/shadows and reversed
depth, midgray gamma, cutouts/dither, mip selection, clamp edges, derivative normal
X convention, stock light attenuation, and additive bucket depth behavior. They
exercise resize/minimize and resource lifetimes, and test a hidden Windows
surface with capture. The Windows presentation test checks D3D11 is not loaded.
Alpha tests compare straight/premultiplied edge coverage, midtone tint over a
nonblack background, and LCD compositing. `Tests/SpriteGpuTests` also compares a
real installed FontDataPA BC7 base and mip-two edge texels with the stock
preprocessing/shader formula using an independent Pillow reference decode. Raw GUI RGBA uploads use
the stock gamma-2.2 premultiply table in their separate managed cache variant.
These tests do not establish in-game parity. Linux/SDL and validation-layer runs
require their respective host facilities; an absent validation layer is reported
by probe/status, and zero validation errors without that layer proves nothing.
