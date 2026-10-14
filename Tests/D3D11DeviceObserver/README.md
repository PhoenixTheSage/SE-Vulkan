# Isolated managed D3D11 device observer

This test-only Pulsar local plugin instruments the installed
`SharpDX.Direct3D11.dll` before it loads. It records each managed call of its
`D3D11CreateDevice`, `D3D11CreateDeviceAndSwapChain` (when present), and
`D3D11On12CreateDevice` native imports, followed by the returned HRESULT. The
installed game has CreateDevice and On12CreateDevice import call sites; its
CreateWithSwapChain helpers use CreateDevice plus DXGI rather than importing
CreateDeviceAndSwapChain.

Set `SE_VULKAN_D3D11_REPORT_DIR` only in an isolated launcher child environment,
add the built `D3D11DeviceObserver.dll` as `Local/D3D11DeviceObserver/plugin.dll`,
and enable `D3D11DeviceObserver.dll` in that child's profile. The observer disables
Pulsar's lazy preloader cache in memory. No report directory means no patching.
Do not add it to an existing profile or package it with the renderer.

`d3d11-observer.json` reports exact original wrapper IL hashes, import/call-site
inventory, attempts, successful/failed HRESULTs, bounded call-stack events,
prepatch/load timing, and a milestone immediately before MyProgram calls
InitializeRender. That method's body and production Vulkan factory fingerprint
are unchanged. Negative evidence requires early readiness, the loaded assembly
stamp (if SharpDX loaded), and no observer errors.

The observer uses only BCL types at runtime and has no SharpDX or
VRage.Render11 reference. Its coverage is **managed SharpDX calls only**. Native
Steam/NVIDIA/other overlays and native libraries can create devices outside
these wrappers and are outside its observation. DLL presence alone is not
device-creation evidence. This does not establish absence of every native D3D11
device in the process.

Build with `dotnet build Tests/D3D11DeviceObserver/D3D11DeviceObserver.csproj -c Release`.
The independent test executable patches clean installed assemblies in memory,
checks the original factory fingerprint, loads patched SharpDX bytes, and creates
one CPU WARP device as a positive control:

```
dotnet build Tests/D3D11DeviceObserver/Tests/D3D11DeviceObserver.Tests.csproj -c Release
Tests/D3D11DeviceObserver/Tests/bin/Release/net48/D3D11DeviceObserver.Tests.exe <game Bin64> <private report directory>
```
