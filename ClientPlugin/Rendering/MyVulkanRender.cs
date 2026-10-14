using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using ClientPlugin.Bootstrap;
using ClientPlugin.Diagnostics;
using ClientPlugin.Platform;
using ClientPlugin.Settings;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRage.FileSystem;
using VRage.Library.Utils;
using VRage.Profiler;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using VRageRender.Profiler;

namespace ClientPlugin.Rendering;

/// <summary>Owns the Vulkan render thread while preserving VRage's pooled frame protocol.</summary>
public sealed class MyVulkanRender : IMyRender
{
    private sealed class Generated { public int Width, Height; public byte[] Pixels; public ulong Handle; }
    private sealed class Capture { public string Path; public bool Notify, IgnoreSprites; public Vector2 Scale; }
    private readonly MyNullRenderProfiler profiler = new();
    private readonly NativeProbe probe;
    private readonly Action<MyRenderMessageBase> dispatchMessage;
    private readonly Action<MyUpdateFrame> returnPreFrame;
    private readonly HashSet<MyRenderMessageEnum> pendingWarnings = new();
    private readonly Dictionary<string, Generated> generated = new(StringComparer.Ordinal);
    private readonly Queue<Capture> captures = new();
    private NativeBackend backend;
    private VulkanWindowHost host;
    private VulkanScene scene;
    private VulkanParticles particles;
    private VulkanDecals decals;private Vector3D previousParticleCamera;private bool haveParticleCamera;
    private VulkanVoxels voxels;
    private VulkanEnvironment environment;
    private readonly VulkanPostprocess postprocess=new();
    private readonly VulkanProbes probes=new();
    private readonly VulkanResolution resolution=new();
    private MyRenderSettings renderConfiguration=MyRenderSettings.Default;
    private MatrixD cameraWorld=MatrixD.Identity;
    private BoundingFrustumD voxelFrustum,voxelDrawFrustum;
    private float voxelFar=20000;
    private bool haveCamera;
    private bool cameraSmooth;
    private VulkanSprites sprites;
    private VulkanMedia media;
    internal bool MediaAvailable => media?.Available == true;
    internal bool MediaFailed(uint id) => media?.Failed(id) == true;
    private MyAdapterInfo[] adapters;
    private MyRenderDeviceSettings settings;
    private MatrixD viewProjection = MatrixD.Identity;
    private MatrixD cameraView = MatrixD.Identity;
    private MatrixD cameraProjection = MatrixD.Identity;
    private readonly VulkanShadows shadows = new();
    private Vector3D camera;
    private MyEnvironmentLightData light = MyEnvironmentLightData.Default;
    private bool drawScene, frameOpen, disposed;
    private bool startupEvidence;
    private readonly RenderSessionLog sessionLog;
    private readonly RenderFrameClock animationClock = new();
    private DateTime lastSample;
    private string annotation = "Bootstrap";
    private long gameplayFrame;
    private long lcdPasses;
    private ulong observedMeshDraws, observedSpriteDraws, billboardEvidenceFrame;
    private NativeWorkStats observedWork;
    private long observedSkinUploads, observedRotations, observedLcdPasses, observedDitherDraws, observedActorDraws;
    private long observedBillboards, observedUnsupportedBillboards;
    private MyTimeSpan previousDraw;
    public static MyVulkanRender Active { get; private set; }
    public bool IsSupported => probe.CompatibleDeviceCount > 0;
    public string RootDirectory { get; set; }
    public string RootDirectoryEffects { get; set; }
    public string RootDirectoryDebug { get; set; }
    public MySharedData SharedData { get; } = new();
    public MyTimeSpan CurrentDrawTime { get; set; }
    public MyLog Log => sessionLog?.Log ?? MyLog.Default;
    public FrameProcessStatusEnum FrameProcessStatus { get; private set; }
    public bool MessageProcessingSupported => true;
    public MyViewport MainViewport { get; private set; }
    public Vector2I BackBufferResolution { get; private set; }
    public MyMessageQueue OutputQueue => SharedData.RenderOutputMessageQueue;
    public uint GlobalMessageCounter { get; set; }

    static uint AnisotropySamples(MyTextureAnisoFiltering filtering) => filtering switch
    {
        MyTextureAnisoFiltering.ANISO_4 => 4,
        MyTextureAnisoFiltering.ANISO_8 => 8,
        MyTextureAnisoFiltering.ANISO_16 => 16,
        _ => 1
    };

    private MyVulkanRender(NativeProbe probe, RenderSessionLog log)
    { this.probe = probe; sessionLog = log; dispatchMessage = Process; returnPreFrame = SharedData.ReturnPreFrame; Active = this; }
    public static bool TryCreate(MyRenderSettings renderSettings, out IMyRender renderer, out string reason)
    {
        renderer = null;
        var session = RenderSessionLog.Open();
        if (session.OpenError != null) VulkanDiagnostics.Failure("Vulkan render log was not created: " + session.OpenError);
        try
        {
            if(Sandbox.Game.MyPerGameSettings.DefaultRenderDeviceSettings?.UseStereoRendering==true)throw new PlatformNotSupportedException("This profile requests stereo rendering; native Vulkan VR submission is unavailable. Use the DX11 profile for stereo.");
            VulkanDiagnostics.Set("Stereo",SubsystemState.Unavailable,"The SE1 desktop profile is mono. Stereo requires native VR host submission and per-eye acceptance.");
            var probe = NativeBackend.Probe(); VulkanDiagnostics.Probe(probe);
            if (probe.LoaderApi < 0x00404000 || probe.DeviceApi < 0x00404000 || probe.CompatibleDeviceCount == 0)
                throw new PlatformNotSupportedException("Vulkan 1.4 loader/device and required features unavailable: " + probe.Detail);
            foreach (string file in new[] { "mesh_vs.spv", "mesh_ps.spv", "sprite_vs.spv", "sprite_ps.spv", "shadow_vs.spv", "shadow_ps.spv", "world_compose_ps.spv", "world_shadow_ps.spv", "voxel_copy_ps.spv", "flare_occlusion_cs.spv" })
                if (!File.Exists(Path.Combine(NativeBackend.AssetsRoot, "Shaders", "Compiled", file))) throw new FileNotFoundException("Required Vulkan shader asset missing.", file);
            if(!File.Exists(Path.Combine(NativeBackend.AssetsRoot,"Shaders","Vanilla","Extra","manifest.xml")))throw new FileNotFoundException("Stock world supplemental shaders are missing; rebuild the packaged shader suite.");
            ParticleCpuContract.Check(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(MyFileSystem.ContentPath)),"Bin64","VRage.Render11.dll"));ParticleCpuContract.CheckRuntime();
            ShaderSuiteStatus.Preflight(MyFileSystem.ContentPath);
            session.RenderSettings(renderSettings);
            renderer = new MyVulkanRender(probe, session){renderConfiguration=renderSettings};
            reason = "Vulkan 1.4 preflight passed.";
            VulkanDiagnostics.Set("Shaders", SubsystemState.Ready, "Installed stock shader catalog authenticated; runtime pass readiness is tracked separately.");
            return true;
        }
        catch (Exception error)
        {
            reason = error.Message;
            session.Fail("Vulkan renderer start failed", error);
            VulkanDiagnostics.Failure(reason);
            if (renderer == null) session.Close();
            return false;
        }
    }
    public MyRenderDeviceSettings CreateDevice(MyRenderDeviceSettings? settingsToTry, out MyAdapterInfo[] adaptersList)
    {
        try
        {
            sessionLog?.Write("CreateDeviceInternal");
            settings = settingsToTry.GetValueOrDefault();
            if(settings.UseStereoRendering)throw new NotSupportedException("Native Vulkan stereo presentation is unavailable; restart with DX11.");
            settings.BackBufferWidth = Math.Max(640, settings.BackBufferWidth);
            settings.BackBufferHeight = Math.Max(480, settings.BackBufferHeight);
            settings.AdapterOrdinal = settings.NewAdapterOrdinal = 0;
            if (settings.RefreshRate <= 0) settings.RefreshRate = 60000;
            sessionLog?.DeviceSettings(settings);
            sessionLog?.Write("CreateDeviceInternal validation: enabled=" + BackendRuntime.Configuration.EnableValidation + "; available=" + probe.ValidationAvailable);
            var window = MyRenderProxy.RenderThread?.RenderWindow ?? throw new InvalidOperationException("Game render window has not been created.");
            var handleProperty = window.GetType().GetProperty("Handle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? window.GetType().GetProperty("WindowHandle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            IntPtr handle = handleProperty != null ? (IntPtr)handleProperty.GetValue(window, null) : IntPtr.Zero;
            host = new VulkanWindowHost(handle, settings);
            var output = DisplayPlacement.PresentationSize((int)host.Width, (int)host.Height, settings.BackBufferWidth, settings.BackBufferHeight);
            backend = new NativeBackend(host);
            backend.Create((uint)output.Width, (uint)output.Height, settings.VSync,
                BackendRuntime.Configuration.EnableValidation, Path.Combine(MyFileSystem.UserDataPath, "Storage", "Vulkan", "PipelineCache"), 0, AnisotropySamples(renderConfiguration.User.AnisotropicFiltering));
            if (settings.WindowMode == MyWindowModeEnum.Fullscreen) EngageExclusiveFullscreen(settings, (uint)output.Width, (uint)output.Height, settings.VSync);
            else backend.ResizeOutput((uint)output.Width, (uint)output.Height, (uint)settings.BackBufferWidth, (uint)settings.BackBufferHeight, settings.VSync);
            ShaderSuiteStatus.Initialize(MyFileSystem.ContentPath, backend);
            if(!backend.WorldReady)throw new InvalidOperationException("Stock world pipeline initialization failed: "+(ShaderSuiteStatus.LastFailure??"the matching shader catalog was unavailable"));
            scene = new VulkanScene(backend);particles=new VulkanParticles(backend,scene,SharedData);decals=new VulkanDecals(backend,scene); voxels=new VulkanVoxels(backend,scene.TextureCache); environment=new VulkanEnvironment(backend,scene); sprites = new VulkanSprites(scene.TextureCache);
            scene.CascadeMaskFor=shadows.CascadeMask;voxels.CascadeMaskFor=shadows.CascadeMask;
            scene.ShadowSystem=shadows;
            scene.ContentFailed += (path, error) => VulkanDiagnostics.Failure("Content " + path + ": " + error.Message);
            sprites.Warning += VulkanDiagnostics.Failure;
            BackBufferResolution = new Vector2I(settings.BackBufferWidth, settings.BackBufferHeight);
            MainViewport = new MyViewport(settings.BackBufferWidth, settings.BackBufferHeight);
            adapters = new[] { new MyAdapterInfo { Name = probe.DeviceName + " (Vulkan 1.4)", DeviceName = "Vulkan", Description = probe.DeviceName,
                IsDx11Supported = true, IsOutputAttached = true, Has512MBRam = true, MaxTextureSize = 16384,
                DesktopResolution = new Vector2I(host.DesktopBounds.Width, host.DesktopBounds.Height), DesktopBounds = host.DesktopBounds,
                SupportedDisplayModes = host.SupportedDisplayModes } };
            adaptersList = adapters;
            sessionLog?.Write("CreateDeviceInternal device create");
            sessionLog?.Write("CreateDeviceInternal swapchain initialize");
            sessionLog?.DeviceReady(settings, "Vulkan device: '" + (probe.DeviceName ?? "unknown") + "', loader " + VulkanDiagnostics.Version(probe.LoaderApi) + ", device " + VulkanDiagnostics.Version(probe.DeviceApi));
            VulkanDiagnostics.Set("Presentation", SubsystemState.Ready, "Native surface and Vulkan device created; awaiting submitted frames.");
            VulkanDiagnostics.Set("Resources", SubsystemState.Ready, "VMA allocator, copied staging uploads and timeline retirement.");
            foreach (string system in new[] { "Sprites and clipping", "Fonts", "Models and materials", "Actors and instancing", "Character skinning", "RenderDirect animations", "Skybox", "Sun and ambient lighting", "Local lighting", "Cascaded shadows", "Fixed exposure tone mapping", "Construction dithering", "Billboards", "LCD targets", "Screenshot readback" })
                VulkanDiagnostics.Set(system, SubsystemState.Ready, "Implementation online; awaiting observed frames and live visual acceptance.");
            media = new VulkanMedia(backend, scene.TextureCache, sprites);
            VulkanDiagnostics.Set("Creative fixture acceptance", SubsystemState.Unavailable, "Requires live controlled-scene comparison, HUD/LCD verification and Vulkan capture.");
            return settings;
        }
        catch (Exception error)
        {
            sessionLog?.Fail("CreateDevice failed: Regular exception occurred", error);
            BackendRuntime.FailStartup("Vulkan device activation failed: " + error.Message);
            DisposeDevice();
            throw;
        }
    }
    public void DisposeDevice()
    {
        if (disposed) return; disposed = true;
        try { try { host?.RestoreDisplayMode(); media?.Dispose(); particles?.Dispose();environment?.Dispose(); voxels?.Dispose(); scene?.Dispose(); } finally { backend?.Dispose(); } }
        catch (Exception error) { BackendRuntime.FailStartup("Vulkan shutdown failed: " + error.Message); throw; }
        finally { try { sessionLog?.Close(); } catch { } BackendRuntime.EndSession(); if (Active == this) Active = null; }
    }
    public long GetAvailableTextureMemory() => 512L * 1024 * 1024; // Conservative upload budget until memory-budget telemetry is exposed.
    public bool SettingsChanged(MyRenderDeviceSettings value) => !settings.Equals(ref value);
    public void ApplySettings(MyRenderDeviceSettings value)
    {
        if (value.AdapterOrdinal != settings.AdapterOrdinal || value.UseStereoRendering) throw new NotSupportedException("Adapter and stereo changes require supported Vulkan capabilities.");
        sessionLog?.Apply(value);
        host.ApplySettings(value); settings = value;
        var output = DisplayPlacement.PresentationSize((int)host.Width, (int)host.Height, value.BackBufferWidth, value.BackBufferHeight);
        EngageExclusiveFullscreen(value, (uint)output.Width, (uint)output.Height, value.VSync);
        BackBufferResolution = new Vector2I(value.BackBufferWidth, value.BackBufferHeight); MainViewport = new MyViewport(value.BackBufferWidth, value.BackBufferHeight);
        var presented = backend.GetStats();
        sessionLog?.Write("Window client: " + host.Width + "x" + host.Height + "; swapchain " + presented.Width + "x" + presented.Height);
        sessionLog?.Screen(BackBufferResolution.X, BackBufferResolution.Y);
        PublishScreenSize(value.BackBufferWidth, value.BackBufferHeight);
    }
    public void EnqueueMessage(MyRenderMessageBase message, bool limitMaxQueueSize)
    {
        if (disposed) { message.Dispose(); return; }
        if (Thread.CurrentThread == MyRenderProxy.RenderSystemThread) SharedData.MessagesForNextFrame.RenderInput.Enqueue(message);
        else SharedData.CurrentUpdateFrame.RenderInput.Enqueue(message);
    }
    public void EnqueueOutputMessage(MyRenderMessageBase message) => OutputQueue.Enqueue(message);
    public MyRenderProfiler GetRenderProfiler() => profiler;
    public void Draw(bool draw = true)
    {
        FrameProfiler.BeginDraw();
        RendererCpuProfiler.BeginFrame();
        using var drawProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Draw);
        try
        {
            backend.BeginUploads();bool uploadsOpen=true;
            try
            {
            sprites.BeginFrame(); sprites.DrawTimeSeconds = CurrentDrawTime.Seconds; drawScene = false;
            scene.TextureCache.BindOwnerThread(); scene.TextureCache.DrainPendingLoads(); media?.Poll();
            environment.Time=CurrentDrawTime.Seconds; environment.GameplayFrame=gameplayFrame;
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Messages))ProcessMessages(draw);
            FrameProfiler.Lap(FrameProfiler.Section.Messages);
            // Loading transitions may normalize the not-yet-populated sun vector.
            // Keep GPU constants finite until the next environment update arrives.
            if(!light.SunLightDirection.IsValid() || light.SunLightDirection.LengthSquared()<1e-8f)light.SunLightDirection=Vector3.Forward;
            float animationDelta = animationClock.Sample();if(particles.Paused)animationDelta=0;
            // The loading screen stays at 100% until each clipmap raises Loaded.
            // That counter only advances inside Clipmap.Update, and stock runs it
            // even while DrawScene is deferred. Gating it on a scene draw never
            // lets the world leave the loading screen.
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Clipmaps))if(haveCamera)voxels.Update(cameraWorld,voxelFrustum,voxelFar,cameraSmooth);
            FrameProfiler.Lap(FrameProfiler.Section.Clipmaps);
            if (!draw || !host.CanPresent) return;
            var worldResolution=resolution.Update(backend,settings,renderConfiguration,drawScene,animationDelta*1000);
            backend.CameraPosition = camera;
            backend.BeginPacketCollection();
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Particles)){
            if (drawScene)
            {
                scene.ExternalDepthRevision=voxels.MeshUpdates+voxels.LodChanges;scene.HighlightSepia=postprocess.Settings.Data.SepiaStrength;scene.ProbeDrawDistance=probes.Settings.DrawDistance;scene.RenderSettings=renderConfiguration;scene.ShadowDrawDistance=shadows.DrawDistance;scene.SetCamera(camera, CurrentDrawTime, animationDelta);particles.Draw(camera,animationDelta,renderConfiguration);decals.Draw(camera,CurrentDrawTime.Seconds,gameplayFrame,(int)renderConfiguration.User.ParticleQuality);
            }
            FrameProfiler.Lap(FrameProfiler.Section.Particles);
            }
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ShadowFit)){
            if (drawScene) shadows.Update(backend, cameraView, light.SunLightDirection,renderConfiguration,cameraProjection,camera);
            else backend.SetShadowCascades(Array.Empty<float>(), Array.Empty<float>());
            }
            FrameProfiler.Lap(FrameProfiler.Section.Shadows);
            if (drawScene) {scene.ShadowDrawDistance=shadows.DrawDistance;scene.Draw(backend);}
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.VoxelsAndBillboards)){
            if (drawScene)
            {
                voxels.Foliage.Distance=renderConfiguration.User.GrassDrawDistance; voxels.Foliage.Density=renderConfiguration.User.GrassDensityFactor;
                voxels.ProbeDrawDistance=probes.Settings.DrawDistance;voxels.ShadowDrawDistance=shadows.DrawDistance;voxels.Draw(camera,voxelDrawFrustum,SharedData.VisibleObjects.Write);MatrixD flareProjection=cameraProjection;flareProjection.M33=flareProjection.M34-flareProjection.M33;flareProjection.M43=flareProjection.M44-flareProjection.M43;scene.DrawFlares(cameraView,flareProjection,BackBufferResolution.X,BackBufferResolution.Y,renderConfiguration.FlaresIntensity); scene.DrawBillboards(SharedData); if(!scene.NativePassSceneEnabled)scene.AddVisible(SharedData.VisibleObjects.Write);
            }
            FrameProfiler.Lap(FrameProfiler.Section.Voxels);
            }
            // Native uses a negative-height viewport for VRage's zero-to-one clip convention.
            // MyCamera supplies forward depth. Native main rendering uses reversed depth; shadow maps retain forward depth.
            MatrixD reversedProjection = viewProjection;
            reversedProjection.M13 = reversedProjection.M14 - reversedProjection.M13;
            reversedProjection.M23 = reversedProjection.M24 - reversedProjection.M23;
            reversedProjection.M33 = reversedProjection.M34 - reversedProjection.M33;
            reversedProjection.M43 = reversedProjection.M44 - reversedProjection.M43;
            if(drawScene && backend.WorldReady)
            {
                MatrixD projection=cameraProjection;projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
                var frameLight=light;
                using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.LightingSetup)){
                environment.GameplayFrame=gameplayFrame; environment.Draw(camera,cameraView*projection,light.SunLightDirection);
                postprocess.Configure(backend,worldResolution.X,worldResolution.Y,renderConfiguration.User.AntialiasingMode==MyAntialiasingMode.FXAA,string.IsNullOrEmpty(postprocess.Settings.DirtTexture)?0:scene.TextureCache.GetTexture(postprocess.Settings.DirtTexture,false),cameraView,projection);
                probes.Configure(backend,camera,light,postprocess.Settings,scene.BackgroundOrientation,scene.TextureCache.GetTexture("Textures/Miscellaneous/ambient_brdf.dds",false),animationDelta,(float)CurrentDrawTime.Seconds,()=>scene.GatherProbeAmbient(light.AmbientLightsGatherRadius,probes.Settings));
                frameLight.AmbientForwardPass+=probes.LastAmbient;
                FrameProfiler.Lap(FrameProfiler.Section.Setup);
                }
                // Resident topology binds uploaded shared allocation generations.
                // Finish every scene/lighting resource consumer before publishing it.
                if(backend.GpuSceneEnabled){using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.EndUploads))backend.EndUploads();uploadsOpen=false;}
                using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Scene))scene.PublishNativePassScene();if(scene.NativePassSceneEnabled||backend.GpuSceneEnabled)scene.AddVisible(SharedData.VisibleObjects.Write);
                using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.QueueWorld)){
                backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,cameraView,projection,worldResolution.X,worldResolution.Y,frameLight,scene.BloomEmissiveness,scene.BackgroundOrientation,environment.Fog,(float)CurrentDrawTime.Seconds,animationDelta,renderConfiguration,haveParticleCamera?(Vector3)(camera-previousParticleCamera):Vector3.Zero,postprocess.Settings,backend.ProbesReady,resolution.Fraction),
                    NativeBackend.MatrixValues(reversedProjection),NativeBackend.MatrixValues(MatrixD.Invert(reversedProjection)));previousParticleCamera=camera;haveParticleCamera=true;
                }
                FrameProfiler.Lap(FrameProfiler.Section.Queue);
            }
            }
            finally { if(uploadsOpen)using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.EndUploads))backend.EndUploads(); }
            FrameProfiler.Lap(FrameProfiler.Section.Queue);
            MatrixD reversedFrame=viewProjection;reversedFrame.M13=reversedFrame.M14-reversedFrame.M13;reversedFrame.M23=reversedFrame.M24-reversedFrame.M23;reversedFrame.M33=reversedFrame.M34-reversedFrame.M33;reversedFrame.M43=reversedFrame.M44-reversedFrame.M43;
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.FrameSlot))frameOpen = backend.BeginFrame(reversedFrame, light.SunLightDirection, light.SunColorRaw * light.SunDiffuseFactor,
                new Vector3(0.06f * Math.Max(0.1f, light.AmbientDiffuseFactor)), new Color(3, 5, 12));
            FrameProfiler.Lap(FrameProfiler.Section.FrameSlot);
            if (!frameOpen) { shadows.CancelPreparedFrame();FrameProfiler.Commit(Sandbox.MySandboxGame.IsPaused, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0); return; }
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Sprites))backend.DrawSprites(sprites.MainPackets(BackBufferResolution.X, BackBufferResolution.Y));
            FrameProfiler.Lap(FrameProfiler.Section.Sprites);
            previousDraw = CurrentDrawTime;
        }
        catch (Exception error) { Fatal(error); throw; }
    }
    public void Present()
    {
        if (!frameOpen) { RendererCpuProfiler.EndFrame();FrameProfiler.Commit(Sandbox.MySandboxGame.IsPaused, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0); return; }
        try
        {
            if (captures.Count > 0) backend.RequestReadback();
            FrameProfiler.Lap(FrameProfiler.Section.Host);
            long nativeStart = System.Diagnostics.Stopwatch.GetTimestamp();
            GuestBridge.Bind(backend);
            BackendRuntime.RaiseFrame();
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativeRecord))backend.EndFrame(); frameOpen = false;
            shadows.CommitPreparedFrame();
            RendererCpuProfiler.EndFrame();
            FrameProfiler.Since(FrameProfiler.Section.NativeRecord, nativeStart);
            var profiled = backend.GetWorkStats();
            var gpuProfile = backend.GetResolutionStats();
            FrameProfiler.Commit(Sandbox.MySandboxGame.IsPaused, profiled.SpotUs, profiled.ShadowUs, profiled.MainUs, profiled.ProbeUs, profiled.PrepareUs, profiled.TailUs, profiled.WaitUs, profiled.AcquireUs, profiled.ResetUs, profiled.WorldDraws,
                drawScene && gpuProfile.TimingSupported != 0 ? gpuProfile.Samples : 0, gpuProfile.GpuMs);
            CompleteCaptures();
            if ((DateTime.UtcNow - lastSample).TotalSeconds >= 1)
            {
                lastSample = DateTime.UtcNow;
                var stats = backend.GetStats(); VulkanDiagnostics.NativeFrame(stats, scene.ActorCount, scene.InstanceCount, scene.ModelCount, sprites.FontCount);
                var work=backend.GetWorkStats();
                VulkanDiagnostics.Set("Upload scheduling",work.UploadBatches>0?SubsystemState.Active:SubsystemState.Ready,"Timeline batches="+work.UploadBatches+"; outstanding-slot waits="+work.UploadSlotWaits+"; two upload slots, 64 MiB staging cache per slot; main frame uniforms and instance streams use fenced frame slots.",stats.SubmittedFrames);
                if(work.WorldFrames>observedWork.WorldFrames)
                {
                    VulkanDiagnostics.Set("Vanilla shader pass integration",SubsystemState.Active,"Stock material GBuffer, depth, directional/point/spot passes and HDR composition observed. Frames="+work.WorldFrames+"; material draws="+work.WorldDraws+"; instances="+work.WorldInstances+"; light passes="+work.LightingPasses+". Probe/BRDF, postprocessing and effect counters are reported below.",stats.SubmittedFrames);
                    VulkanDiagnostics.Set("Actors and instancing",SubsystemState.Active,"Persistent stock geometry; instance count="+work.WorldInstances+" across "+work.WorldDraws+" material draws. Cube draws="+work.CubeDraws+".",stats.SubmittedFrames);
                    if(work.SkinnedDraws>observedWork.SkinnedDraws)VulkanDiagnostics.Set("Character skinning",SubsystemState.Active,"Stock vertex shader consumed bone palettes; skinned draws="+work.SkinnedDraws+". Immutable vertices remain resident."+scene.CharacterStatus(),stats.SubmittedFrames);
                    VulkanDiagnostics.Set("Sun and ambient lighting",SubsystemState.Active,"Stock deferred directional/ambient lighting consumed the GBuffer. Complete near/far GGX probes and the installed BRDF LUT feed diffuse/specular lighting after probe publication.",stats.SubmittedFrames);
                    var post=backend.GetPostprocessStats();VulkanDiagnostics.Set("Stock postprocessing",post.Frames>0?SubsystemState.Active:SubsystemState.Ready,"Frames="+post.Frames+"; exposure="+post.ExposurePasses+"; histogram="+post.HistogramDispatches+"; bloom="+post.BloomPasses+"; tone="+post.ToneDispatches+"; FXAA="+post.FxaaPasses+"; SSAO="+post.SsaoPasses+"; chromatic/vignette="+post.ChromaticDispatches+"; HBAO="+post.HbaoPasses+"; stock CSM="+post.ShadowDispatches+".",stats.SubmittedFrames);
                }
                VulkanDiagnostics.Set("Presentation", SubsystemState.Active, "Native frame submissions/presentation observed.", stats.SubmittedFrames);
                bool meshEvidence = stats.MeshDraws > observedMeshDraws;
                if (stats.SpriteDraws > observedSpriteDraws) VulkanDiagnostics.Set("Sprites and clipping", SubsystemState.Active, "Sprite draw commands submitted; visual clipping acceptance pending.", stats.SubmittedFrames);
                if (sprites.FontCount > 0 && stats.SpriteDraws > observedSpriteDraws) VulkanDiagnostics.Set("Fonts", SubsystemState.Active, "Game glyph/kerning data loaded; HUD acceptance pending.", stats.SubmittedFrames);
                if (meshEvidence && scene.DrawnActors > observedActorDraws) VulkanDiagnostics.Set("Models and materials", SubsystemState.Active, "MWM/DDS stock material draws observed; advanced material parity pending.", stats.SubmittedFrames);
                if (meshEvidence && scene.SkinnedUploads > observedSkinUploads) VulkanDiagnostics.Set("Character skinning", SubsystemState.Active, "Copied bone transforms produced native vertex uploads; animation acceptance pending." + scene.CharacterStatus(), stats.SubmittedFrames);
                if (meshEvidence && scene.RenderDirectRotationUpdates > observedRotations) VulkanDiagnostics.Set("RenderDirect animations", SubsystemState.Active, "Source rotation animators updated scene transforms " + scene.RenderDirectRotationUpdates + " times; live animation review remains separate.", stats.SubmittedFrames);
                if (lcdPasses > observedLcdPasses) VulkanDiagnostics.Set("LCD targets", SubsystemState.Active, "Offscreen sprite passes=" + lcdPasses + "; source logical viewport and clipping applied; live LCD acceptance remains separate.", stats.SubmittedFrames);
                if (scene.SkyboxReady) VulkanDiagnostics.Set("Skybox", SubsystemState.Active, "DDS cubemap sampled by stock directional-light shader with source orientation.", stats.SubmittedFrames);
                if (scene.LightCount > 0) VulkanDiagnostics.Set("Local lighting", SubsystemState.Active, "Point/spot lights submitted; nearest 64 limit.", stats.SubmittedFrames);
                bool billboardEvidence = meshEvidence && scene.BillboardCount > observedBillboards;
                if (billboardEvidence) billboardEvidenceFrame = stats.SubmittedFrames;
                if (billboardEvidence || scene.UnsupportedBillboards > observedUnsupportedBillboards)
                    VulkanDiagnostics.Set("Billboards", billboardEvidenceFrame != 0 ? SubsystemState.Active : SubsystemState.Ready, "Standard/additive packets submitted=" + scene.BillboardCount + "; unsupported or over-budget packets=" + scene.UnsupportedBillboards + ".", billboardEvidenceFrame);
                if (meshEvidence && scene.DrawnActors > observedActorDraws) VulkanDiagnostics.Set("Cascaded shadows", SubsystemState.Active, "Stock depth partners replay GPU instances/bones in up to eight quality-sized cascades; stock PCF, cascade blending and optional depth-aware Gaussian blur.", stats.SubmittedFrames);
                if (meshEvidence && scene.DitheredDraws > observedDitherDraws) VulkanDiagnostics.Set("Construction dithering", SubsystemState.Active, "Stock material dithering packets submitted=" + scene.DitheredDraws + ".", stats.SubmittedFrames);
                var transparent=backend.GetTransparencyStats();var particle=backend.GetParticleStats();
                VulkanDiagnostics.Set("Glass, decals and OIT",transparent.Frames>0?SubsystemState.Active:SubsystemState.Ready,"Stock glass draws="+transparent.GlassDraws+", instances="+transparent.Instances+", OIT resolves="+transparent.Resolves+", decal draws="+transparent.DecalDraws+", glass layer draws="+transparent.GlassLayerDraws+", copied decal residency="+decals.Count+". Stock near/far probe and BRDF inputs bound.",stats.SubmittedFrames);
                VulkanDiagnostics.Set("GPU particles",particle.Frames>0?SubsystemState.Active:SubsystemState.Ready,"Stock CPU effects="+particles.Effects+", GPU frames="+particle.Frames+", compute dispatches="+particle.Dispatches+", indirect draws="+particle.Draws+", completed alive/dead="+particle.Alive+"/"+particle.Dead+", indices="+particle.Indices+". Counters read only from completed frame slots; no status stalls.",stats.SubmittedFrames);
                VulkanDiagnostics.Set("Flares",scene.FlareQueries>0?SubsystemState.Active:SubsystemState.Ready,"GPU depth queries="+scene.FlareQueries+", additive flare billboards="+scene.FlareBillboards+"; stock intensity curves and types; 16-sample asynchronous occlusion, up to two-frame latency.",stats.SubmittedFrames);
                var probeStats=backend.GetProbeStats();VulkanDiagnostics.Set("Environment probes",probeStats.Ready!=0?SubsystemState.Active:SubsystemState.Ready,"Faces="+probeStats.Faces+"; geometry="+probeStats.GeometryDraws+"; GGX="+probeStats.PrefilterPasses+"; blend="+probeStats.BlendPasses+"; atmosphere="+probeStats.AtmospherePasses+"; published="+probeStats.Ready,stats.SubmittedFrames);
                var visibility=backend.GetOcclusionStats();var ambient=backend.GetPostprocessStats();VulkanDiagnostics.Set("Fixed exposure tone mapping",ambient.ToneDispatches>0?SubsystemState.Active:SubsystemState.Ready,"Stock tone dispatches="+ambient.ToneDispatches+"; fixed or histogram exposure selected by copied postprocess settings.",stats.SubmittedFrames);VulkanDiagnostics.Set("Occlusion and SSAO/HBAO",visibility.Queries>0 || ambient.HbaoPasses>0 || ambient.SsaoPasses>0?SubsystemState.Active:SubsystemState.Ready,"Queries="+visibility.Queries+"; completed="+visibility.Results+"; hidden="+visibility.Occluded+"; scene culls="+scene.OcclusionCulls+"; SSAO="+ambient.SsaoPasses+"; HBAO="+ambient.HbaoPasses,stats.SubmittedFrames);
                var highlight=backend.GetHighlightStats();VulkanDiagnostics.Set("Exposure, bloom and highlights",ambient.Frames>0?SubsystemState.Active:SubsystemState.Ready,"Exposure="+ambient.ExposurePasses+"; bloom="+ambient.BloomPasses+"; highlight geometry="+highlight.Draws+"; blur="+highlight.BlurPasses+"; composition="+highlight.Compositions,stats.SubmittedFrames);
                var dynamicResolution=backend.GetResolutionStats();VulkanDiagnostics.Set("Postprocessing and upscale",ambient.Frames>0?SubsystemState.Active:SubsystemState.Ready,"Stock tone/FXAA/chromatic pipeline; world="+dynamicResolution.Width+"x"+dynamicResolution.Height+"; output="+dynamicResolution.OutputWidth+"x"+dynamicResolution.OutputHeight+"; fraction="+dynamicResolution.Fraction+"; completed GPU ms="+dynamicResolution.GpuMs+"; timestamp samples="+dynamicResolution.Samples+"; stock DRS heuristic and bilinear upscale.",stats.SubmittedFrames);
                var spotStats=backend.GetSpotStats();VulkanDiagnostics.Set("Spotlight shadows",spotStats.Draws>0?SubsystemState.Active:SubsystemState.Ready,"Depth draws="+spotStats.Draws+"; quality map creations="+spotStats.Maps+"; cookie passes="+spotStats.Cookies+"; bounded to 16 nearest shadowed spots.",stats.SubmittedFrames);
                var environmentStats=backend.GetEnvironmentStats();
                VulkanDiagnostics.Set("Planet atmosphere/clouds",environmentStats.Frames>0?SubsystemState.Active:SubsystemState.Ready,"Atmospheres="+environment.AtmosphereCount+", clouds="+environment.CloudCount+", atmosphere draws="+environmentStats.AtmosphereDraws+", cloud draws="+environmentStats.CloudDraws+", LUT dispatches="+environmentStats.LutDispatches+", resident LUTs="+environmentStats.Luts,stats.SubmittedFrames);
                VulkanDiagnostics.Set("Weather fog",environment.FogUpdates>0?SubsystemState.Active:SubsystemState.Ready,"Stock world, sky and atmosphere fog constants; updates="+environment.FogUpdates+". Stock precipitation uses the GPU particle bridge.",stats.SubmittedFrames);
                VulkanDiagnostics.Set("Voxel foliage/wind",environmentStats.FoliageDraws>0?SubsystemState.Active:SubsystemState.Ready,"Stock grass/pebble GS; persistent points generated="+voxels.Foliage.GeneratedPoints+", draws="+environmentStats.FoliageDraws+", submitted points="+environmentStats.FoliagePoints,stats.SubmittedFrames);
                var voxelStats=backend.GetVoxelStats();
                VulkanDiagnostics.Set("Planets and atmosphere",voxels.PlanetCount>0 && voxelStats.Frames>0 && environmentStats.AtmosphereDraws>0?SubsystemState.Active:SubsystemState.Ready,"Planet clipmaps="+voxels.PlanetCount+"; stock spherization, infinite reverse depth, far-frustum culling and main-view scattering. Atmosphere probe capture and stock exposure are integrated; cross-backend image parity remains under review.",stats.SubmittedFrames);
                VulkanDiagnostics.Set("Clouds, fog, foliage and weather",environmentStats.CloudDraws>0 && environmentStats.FoliageDraws>0?SubsystemState.Active:SubsystemState.Ready,"Stock shells, world/sky/atmosphere fog, grass/pebbles and wind integrated; stock precipitation uses GPU particles.",stats.SubmittedFrames);
                VulkanDiagnostics.Set("Voxels",voxelStats.Frames>0?SubsystemState.Active:SubsystemState.Ready,
                    "Stock triplanar/depth; actors="+voxels.ActorCount+", cells="+voxels.CellCount+", visible="+voxels.VisibleCells+", LOD="+voxels.LodStatus+", mesh updates="+voxels.MeshUpdates+", frames="+voxelStats.Frames+", draws="+voxelStats.Draws+", meshes="+voxelStats.Meshes+", arrays="+voxelStats.Arrays+", material groups="+voxelStats.Materials+", resident bytes="+voxels.Materials.ResidentBytes+"; shared 512 MiB atlas pages="+voxels.Materials.AtlasStatus+"; source DDS separate. Atomic LOD swaps; planet spherization supplied; timed fades and user quality mapping remain pending.",stats.SubmittedFrames);
                observedWork=work;
                observedMeshDraws = stats.MeshDraws; observedSpriteDraws = stats.SpriteDraws;
                observedSkinUploads = scene.SkinnedUploads; observedRotations = scene.RenderDirectRotationUpdates;
                observedLcdPasses = lcdPasses; observedDitherDraws = scene.DitheredDraws; observedActorDraws = scene.DrawnActors;
                observedBillboards = scene.BillboardCount; observedUnsupportedBillboards = scene.UnsupportedBillboards;
                // A healthy menu-only session may exit without world meshes. Creative acceptance is tracked independently.
                if (!startupEvidence && stats.PresentedFrames >= 120 && stats.SpriteDraws > 0 && sprites.FontCount > 0 && stats.ValidationErrors == 0)
                {
                    startupEvidence = true; BackendRuntime.MarkStartupSuccessful();
                    VulkanDiagnostics.Set("Bootstrap", SubsystemState.Active, "Factory/device startup healthy; presented UI evidence observed. Creative acceptance is tracked separately.", stats.SubmittedFrames);
                }
            }
        }
        catch (Exception error) { Fatal(error); throw; }
    }
    private void Fatal(Exception error) { frameOpen = false; sessionLog?.Fail("Exception in render! Vulkan native failure: " + error.Message, error); BackendRuntime.FailStartup("Vulkan active-session failure: " + error.Message); VulkanDiagnostics.Set("Presentation", SubsystemState.Failed, error.Message); }
    private void ProcessMessages(bool draw)
    {
        FrameProcessStatus = FrameProcessStatusEnum.NoProcess;
        while (true)
        {
            var frame = SharedData.GetRenderFrame(out bool preFrame);
            if (frame == null) { FrameProcessStatus = FrameProcessStatusEnum.NoFrame; break; }
            if (preFrame) { if (!frame.Processed) FrameProcessStatus = FrameProcessStatusEnum.Skipped; ProcessPreFrame(frame, true); continue; }
            FrameProcessStatus = frame.Processed ? FrameProcessStatusEnum.AlreadyProcessed : FrameProcessStatusEnum.Success;
            ProcessFrame(frame, draw); break;
        }
        ProcessFrame(SharedData.MessagesForNextFrame, draw); ProcessPreFrame(SharedData.MessagesForNextFrame, false);
    }
    private void ProcessFrame(MyUpdateFrame frame, bool draw)
        => RenderFrameProtocol.Process(frame, draw, dispatchMessage);
    private void ProcessPreFrame(MyUpdateFrame frame, bool returnShared)
        => RenderFrameProtocol.Retire(frame, dispatchMessage, returnShared ? returnPreFrame : null);
    private void Process(MyRenderMessageBase message)
    {
        annotation = message.MessageType.ToString();
        probes.Process(message);shadows.Process(message);if(message.MessageType==MyRenderMessageEnum.UnloadData)postprocess.Process(message);
        decals?.Observe(message);if(decals!=null&&decals.Process(message)){VulkanDiagnostics.Message(message.MessageType,MessageDisposition.Handler);return;}
        if(particles!=null && particles.Process(message)){VulkanDiagnostics.Message(message.MessageType,MessageDisposition.Handler);return;}
        if(environment!=null && environment.Process(message)){if(message is MyRenderMessageRemoveRenderObject er)MyRenderProxy.RemoveMessageId(er.ID,MyRenderProxy.GetObjectType(er.ID));VulkanDiagnostics.Message(message.MessageType,MessageDisposition.Handler);return;}
        if(voxels!=null && voxels.Process(message)){if(message is MyRenderMessageRemoveRenderObject vr){var type=MyRenderProxy.GetObjectType(vr.ID);if(type!=MyRenderProxy.ObjectType.Invalid)MyRenderProxy.RemoveMessageId(vr.ID,type);}VulkanDiagnostics.Message(message.MessageType,MessageDisposition.Handler);return;}
        if (media != null && media.Process(message)) { VulkanDiagnostics.Message(message.MessageType, MessageDisposition.Handler); return; }
        if (sprites.Process(message)) { VulkanDiagnostics.Message(message.MessageType, MessageDisposition.Handler); return; }
        if (scene.Process(message))
        {
            if (message is MyRenderMessageRemoveRenderObject remove) MyRenderProxy.RemoveMessageId(remove.ID, MyRenderProxy.GetObjectType(remove.ID));
            if (message.MessageType != MyRenderMessageEnum.SetCameraViewMatrix && message.MessageType != MyRenderMessageEnum.UpdateRenderEnvironment)
            { VulkanDiagnostics.Message(message.MessageType, MessageDisposition.Handler); return; }
        }
        MessageDisposition disposition = MessageDisposition.Handler;
        if(postprocess.Process(message)){VulkanDiagnostics.Message(message.MessageType,MessageDisposition.Handler);return;}
        switch (message.MessageType)
        {
            case MyRenderMessageEnum.DrawCommands:
                foreach (var child in ((MyRenderMessageDrawCommands)message).Messages) Process(child); break;
            case MyRenderMessageEnum.DrawScene: drawScene = true; break;
            case MyRenderMessageEnum.SetCameraViewMatrix:
                var cam = (MyRenderMessageSetCameraViewMatrix)message; haveCamera=true; camera = cam.CameraPosition;cameraWorld=MatrixD.Invert(cam.ViewMatrix);voxelFrustum=new BoundingFrustumD(cam.ViewMatrix*(MatrixD)cam.ProjectionMatrix);voxelFar=cam.FarPlane;GuestBridge.SetViewClip(voxelFar);cameraSmooth=cam.Smooth;voxelDrawFrustum=new BoundingFrustumD(cam.ViewMatrix*(MatrixD)cam.ProjectionFarMatrix);
                cameraView = cam.ViewMatrix; cameraView.Translation = Vector3D.Zero; cameraProjection=cam.FOV>0?ReverseDepth(Matrix.CreatePerspectiveFovRhInfiniteComplementary(cam.FOV,BackBufferResolution.X/(float)BackBufferResolution.Y,cam.NearPlane)):cam.ProjectionMatrix;cameraProjection.M31=cam.ProjectionOffsetX;cameraProjection.M32=cam.ProjectionOffsetY; viewProjection = cameraView * cameraProjection; break;
            case MyRenderMessageEnum.UpdateShadowSettings:disposition=MessageDisposition.Pending;Pending(message,"Count/distance/spread/bias/blur/frozen settings applied; small-object pruning and usage-based cascade cadence remain pending.");break;
            case MyRenderMessageEnum.UpdatePlanetSettings:break;
            case MyRenderMessageEnum.UpdateNewLoddingSettings:disposition=MessageDisposition.Pending;Pending(message,"Atmosphere/cloud fade duration applied; remaining actor LOD settings pending.");break;
            case MyRenderMessageEnum.UpdateRenderEnvironment:
                light = ((MyRenderMessageUpdateRenderEnvironment)message).Data.EnvironmentLight; break;
            case MyRenderMessageEnum.VideoAdaptersRequest:
                var response = MyRenderProxy.MessagePool.Get<MyRenderMessageVideoAdaptersResponse>(MyRenderMessageEnum.VideoAdaptersResponse);
                response.Adapters = adapters; EnqueueOutputMessage(response); break;
            case MyRenderMessageEnum.SwitchRenderSettings:renderConfiguration=((MyRenderMessageSwitchRenderSettings)message).Settings;sessionLog?.RenderSettings(renderConfiguration);backend?.SetTextureAnisotropy(AnisotropySamples(renderConfiguration.User.AnisotropicFiltering));disposition=MessageDisposition.Pending;Pending(message,"Anisotropic filtering, grass distance/density, scaling and wind applied; remaining graphics/debug settings pending.");break;
            case MyRenderMessageEnum.SwitchDeviceSettings: ApplySettings(((MyRenderMessageSwitchDeviceSettings)message).Settings); break;
            case MyRenderMessageEnum.CreateGeneratedTexture:
                var create = (MyRenderMessageCreateGeneratedTexture)message;
                var pixels = ConvertPixels(create.Width, create.Height, create.Data, create.Type == MyGeneratedTextureType.Alphamask);
                var target = new Generated { Width = create.Width, Height = create.Height, Pixels = pixels };
                target.Handle = scene.TextureCache.PutGenerated(create.TextureName, target.Width, target.Height, pixels); generated[create.TextureName] = target; break;
            case MyRenderMessageEnum.ResetGeneratedTexture:
                var reset = (MyRenderMessageResetGeneratedTexture)message;
                if (generated.TryGetValue(reset.TextureName, out var existing)) { existing.Pixels = ConvertPixels(existing.Width, existing.Height, reset.Data, reset.Data != null && reset.Data.Length == existing.Width * existing.Height); existing.Handle = scene.TextureCache.PutGenerated(reset.TextureName, existing.Width, existing.Height, existing.Pixels); } break;
            case MyRenderMessageEnum.DestroyGeneratedTexture:
                var destroy = (MyRenderMessageDestroyGeneratedTexture)message; generated.Remove(destroy.TextureName); scene.TextureCache.RemoveGenerated(destroy.TextureName); break;
            case MyRenderMessageEnum.RenderOffscreenTexture:
                var offscreen = (MyRenderMessageRenderOffscreenTexture)message;
                if (generated.TryGetValue(offscreen.OffscreenTexture, out var surface))
                { backend.RenderTexture(surface.Handle, surface.Width, surface.Height, offscreen.BackgroundColor ?? Color.Transparent, sprites.ConsumeTarget(offscreen.OffscreenTexture), offscreen.AspectRatio); lcdPasses++; }
                else Pending(message, "Generated LCD target was not created: " + offscreen.OffscreenTexture); break;
            case MyRenderMessageEnum.TakeScreenshot:
                var capture = (MyRenderMessageTakeScreenshot)message;
                captures.Enqueue(new Capture { Path = capture.PathToSave, Notify = capture.ShowNotification, IgnoreSprites = capture.IgnoreSprites, Scale = capture.SizeMultiplier }); break;
            case MyRenderMessageEnum.UnloadTextures:
                foreach (string file in ((MyRenderMessageUnloadTextures)message).Files) scene.TextureCache.Unload(file); break;
            case MyRenderMessageEnum.UnloadData:
                sessionLog?.UnloadSession();
                decals?.Clear();haveParticleCamera=false;haveCamera=false;particles?.Clear();environment?.Clear(); voxels?.Clear(); scene.Clear(); sprites.Clear(); generated.Clear(); break;
            case MyRenderMessageEnum.SetFrameTimeStep: animationClock.StepOverride = ((MyRenderMessageSetFrameTimeStep)message).TimeStepInSeconds; break;
            case MyRenderMessageEnum.CollectGarbage: GC.Collect(); break;
            case MyRenderMessageEnum.UpdateGameplayFrame: gameplayFrame = ((MyRenderMessageUpdateGameplayFrame)message).GameplayFrame; break;
            case MyRenderMessageEnum.ScreenshotTaken:
            case MyRenderMessageEnum.ExportToObjComplete:
            case MyRenderMessageEnum.Error:
            case MyRenderMessageEnum.VideoAdaptersResponse:
            case MyRenderMessageEnum.CreatedDeviceSettings:
            case MyRenderMessageEnum.MainThreadCallback:
            case MyRenderMessageEnum.TasksFinished:
            case MyRenderMessageEnum.ParticleEffectRemoved: disposition = MessageDisposition.OutboundResponse; break;
            case MyRenderMessageEnum.InvalidateClipmapRange:
            case MyRenderMessageEnum.ClipmapsReady:
            case MyRenderMessageEnum.RebuildCullingStructure:
            case MyRenderMessageEnum.CreateEffect:
            case MyRenderMessageEnum.UpdateNewPipelineSettings:
            case MyRenderMessageEnum.UpdateMaterialsSettings:
            case MyRenderMessageEnum.UpdateEnvironmentMap:
            case MyRenderMessageEnum.SetDecalsBlacklist:
            case MyRenderMessageEnum.DebugDrawLine3DBatch: disposition = MessageDisposition.BaselineNoOp; break;
            default: disposition = MessageDisposition.Pending; Pending(message, message.MessageType == MyRenderMessageEnum.UpdatePostprocessSettings
                ? "Material BloomEmissiveness applied; exposure, bloom and remaining postprocess settings are pending."
                : "No Vulkan handler; follow the visual parity registry."); break;
        }
        VulkanDiagnostics.Message(message.MessageType, disposition);
    }
    private static MatrixD ReverseDepth(MatrixD matrix){matrix.M13=matrix.M14-matrix.M13;matrix.M23=matrix.M24-matrix.M23;matrix.M33=matrix.M34-matrix.M33;matrix.M43=matrix.M44-matrix.M43;return matrix;}
    private void Pending(MyRenderMessageBase message, string reason)
    { if (pendingWarnings.Add(message.MessageType)) VulkanDiagnostics.Failure(message.MessageType + ": " + reason); }
    private static byte[] ConvertPixels(int width, int height, byte[] bytes, bool mask)
    {
        var result = new byte[checked(width * height * 4)];
        if (bytes == null) return result;
        if (!mask) { if (bytes.Length != result.Length) throw new InvalidDataException("Generated RGBA byte count mismatch."); Buffer.BlockCopy(bytes, 0, result, 0, result.Length); }
        else { if (bytes.Length != width * height) throw new InvalidDataException("Generated alpha byte count mismatch."); for (int i = 0; i < bytes.Length; i++) { result[i * 4] = result[i * 4 + 1] = result[i * 4 + 2] = 255; result[i * 4 + 3] = bytes[i]; } }
        return result;
    }
    private void CompleteCaptures()
    {
        while (captures.Count > 0)
        {
            var capture = captures.Dequeue(); bool success = false;
            try
            {
                if (capture.IgnoreSprites || (capture.Scale != Vector2.One && capture.Scale != Vector2.Zero)) throw new NotSupportedException("Scaled and world-only capture are pending; use a native-size full-frame PNG.");
                string path = capture.Path;
                if (string.IsNullOrEmpty(path)) path = Path.Combine(MyFileSystem.UserDataPath, "Screenshots", "Vulkan-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".png");
                var extent = backend.GetStats();
                PngWriter.Save(path, (int)extent.Width, (int)extent.Height, backend.Readback((int)extent.Width, (int)extent.Height)); capture.Path = path; success = true;
                VulkanDiagnostics.Set("Screenshot readback", SubsystemState.Active, "Native image copied to PNG on explicit request.", extent.SubmittedFrames);
            }
            catch (Exception error) { VulkanDiagnostics.Failure("Screenshot: " + error.Message); }
            var response = MyRenderProxy.MessagePool.Get<MyRenderMessageScreenshotTaken>(MyRenderMessageEnum.ScreenshotTaken);
            response.Success = success; response.Filename = capture.Path; response.ShowNotification = capture.Notify; EnqueueOutputMessage(response);
        }
    }
    private void PublishScreenSize(int width, int height)
    {
        try
        {
            var sandbox = Type.GetType("Sandbox.MySandboxGame, Sandbox.Game", true);
            var update = sandbox.GetMethod("UpdateScreenSize", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int), typeof(int), typeof(MyViewport) }, null)
                ?? throw new MissingMethodException(sandbox.FullName, "UpdateScreenSize");
            var singleton = sandbox.GetField("Static", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingFieldException(sandbox.FullName, "Static");
            var game = singleton.GetValue(null) ?? throw new InvalidOperationException("Game singleton is not initialized.");
            var invoke = sandbox.GetMethod("Invoke", new[] { typeof(Action), typeof(string), typeof(int), typeof(int) })
                ?? throw new MissingMethodException(sandbox.FullName, "Invoke");
            var viewport = MainViewport;
            invoke.Invoke(game, new object[] { (Action)(() =>
            {
                update.Invoke(null, new object[] { width, height, viewport });
                sessionLog?.Write("Game screen size published: " + width + "x" + height);
            }), "Vulkan resolution", -1, 0 });
        }
        catch (Exception error) { sessionLog?.Write("Screen size publish failed: " + error.GetBaseException().Message); }
    }
    private void EngageExclusiveFullscreen(MyRenderDeviceSettings value, uint width, uint height, int vsync)
    {
        bool fullscreen = host != null && !host.IsLinux && value.WindowMode == MyWindowModeEnum.Fullscreen;
        bool exclusive = fullscreen && ExclusiveFullscreenChoice.Enabled;
        if (fullscreen && !exclusive) host.EnsureDisplayMode(value);
        if (exclusive) host.RestoreDisplayMode();
        backend.SetExclusiveFullscreen(exclusive ? host.OutputMonitor : IntPtr.Zero);
        // The swapchain follows the live client while the selected mode remains
        // the logical world/HUD resolution scaled across that output.
        backend.ResizeOutput(width, height, (uint)value.BackBufferWidth, (uint)value.BackBufferHeight, vsync);
        if (!fullscreen) return;
        if (!exclusive)
        {
            sessionLog?.Write(string.IsNullOrEmpty(host.LastDisplayChange) ? "Fullscreen presentation" : "Fullscreen presentation; " + host.LastDisplayChange);
            return;
        }
        if (!backend.RefreshExclusiveFullscreen() && host.EnsureDisplayMode(value))
            backend.RefreshExclusiveFullscreen();
        sessionLog?.Write((backend.ExclusiveFullscreen ? "Exclusive fullscreen acquired" : "Exclusive fullscreen unavailable; display mode fallback") + (string.IsNullOrEmpty(host.LastDisplayChange) ? "" : "; " + host.LastDisplayChange));
    }
    public MyTimeSpan GetDrawCpuNOPGap() => MyTimeSpan.Zero;
    public void UpdateDRSScale() { }
    public void UpdateScreenDeviceState()
    {
        // The selected resolution stays in effect. A moved or unfocused window must not
        // replace it with the client rectangle; the swapchain is recreated when it is out of date.
    }
    public void Ansel_DrawScene() => VulkanDiagnostics.Set("Advanced capture", SubsystemState.Unavailable, "Ansel requires a Vulkan-specific integration.");
    public bool IsVideoValid(uint id) => media?.IsValid(id) == true;
    public VideoState GetVideoState(uint id) => media?.State(id) ?? VideoState.Stopped;
    public void GenerateShaderCache(bool clean, bool fastBuild, OnShaderCacheProgressDelegate progress) => VulkanDiagnostics.Set("Shaders", SubsystemState.Ready, "Installed stock HLSL catalog is authenticated SPIR-V; runtime integration and reload acceptance are tracked separately.");
    public string GetLastExecutedAnnotation() => annotation;
    public string GetStatistics() => VulkanDiagnostics.Report();
    public void SetTimings(MyTimeSpan cpuDraw, MyTimeSpan cpuWait) { }
    public Vector2 GetTextureSize(string name) => scene != null && scene.TextureCache.GetSize(name, out int width, out int height) ? new Vector2(width, height) : Vector2.Zero;
    public bool IsTextureLoaded(string name) => scene != null && scene.TextureCache.IsLoaded(name);
    public float GetDRSDebugData() => resolution.Fraction;
}
