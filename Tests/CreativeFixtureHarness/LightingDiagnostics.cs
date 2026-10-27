using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using ClientPlugin.Bootstrap;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using HarmonyLib;
using Sandbox.Engine.Platform.VideoMode;
using VRageRender;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    static Plugin lightingOwner;
    static Harmony lightingHarmony;
    static MethodInfo lightingDx11Capture;
    static MethodInfo lightingDx11Inputs;
    static MethodInfo lightingDx11Fxaa;
    static int lightingPostprocessReady;
    static int lightingInputsReady;
    static int lightingPending,lightingQueued;
    static ulong lightingPipeline;
    static byte[] lightingFrame;
    static byte[] lightingWorldFrame;
    static byte[] lightingHbaoConstants;
    static string lightingShadowState;
    static string lightingLodState;
    static string lightingActorState;
    const int LightingHbaoSeed=20261018;
    static bool lightingHbaoPinned;
    const ulong LightingOutput=0x3fff000000001000UL;
    static readonly string[] LightingNames={"color-lod","normal-ao","metal-gloss-emissive","depth","hdr"};
    internal const string LightingCopySource=@"
Texture2D<float4> G0:register(t0);Texture2D<float4> G1:register(t1);Texture2D<float4> G2:register(t2);
Texture2D<float> Depth:register(t3);Texture2D<float4> Hdr:register(t4);
RWTexture2D<float4> A:register(u0);RWTexture2D<float4> B:register(u1);RWTexture2D<float4> C:register(u2);
RWTexture2D<float4> D:register(u3);RWTexture2D<float4> E:register(u4);
[numthreads(8,8,1)]void main(uint3 p:SV_DispatchThreadID){uint w,h;A.GetDimensions(w,h);if(any(p.xy>=uint2(w,h)))return;
A[p.xy]=G0.Load(int3(p.xy,0));B[p.xy]=G1.Load(int3(p.xy,0));C[p.xy]=G2.Load(int3(p.xy,0));
D[p.xy]=float4(Depth.Load(int3(p.xy,0)),0,0,1);E[p.xy]=Hdr.Load(int3(p.xy,0));}";

    void InitializeLightingDiagnostics()
    {
        if(Environment.GetEnvironmentVariable("SE_VULKAN_LIGHTING_DIAGNOSTICS")!="1"&&!glassOverlapMode&&!meshDecalMode)return;
        lightingOwner=this;lightingHarmony=new Harmony("se-vulkan.private-lighting-diagnostics");
        if(meshDecalMode&&Environment.GetEnvironmentVariable("SE_VULKAN_MESH_IMMEDIATE_PROBES")=="1")
            lightingHarmony.Patch(AccessTools.Method(typeof(MyRenderProxy),"UpdateRenderEnvironment"),prefix:new HarmonyMethod(typeof(Plugin),nameof(PinMeshImmediateProbes)));
        if(expectedBackend.ToString()=="Vulkan") {
            lightingHarmony.Patch(AccessTools.Method(typeof(NativeBackend),"InitializeHbaoJitter"),prefix:new HarmonyMethod(typeof(Plugin),nameof(PinLightingHbaoJitter)));
            lightingHarmony.Patch(AccessTools.Method(typeof(NativeBackend),"SetHbao"),prefix:new HarmonyMethod(typeof(Plugin),nameof(EnsureVulkanLightingHbaoJitter)));
            if(meshDecalMode){lightingHarmony.Patch(AccessTools.Method(typeof(ClientPlugin.Rendering.Scene.VulkanScene),"BuildScene"),postfix:new HarmonyMethod(typeof(Plugin),nameof(ObserveMeshDecalScene)));lightingHarmony.Patch(AccessTools.Method(typeof(NativeBackend),"EndFrame"),postfix:new HarmonyMethod(typeof(Plugin),nameof(ObserveMeshDecalVulkan)));lightingHarmony.Patch(AccessTools.Method(typeof(ClientPlugin.Rendering.Shaders.WorldConstants),"Frame"),postfix:new HarmonyMethod(typeof(Plugin),nameof(CaptureLightingFrame)));return;}
            if(glassOverlapMode){
                lightingHarmony.Patch(AccessTools.Method(typeof(ClientPlugin.Rendering.Scene.VulkanScene),"BuildScene"),postfix:new HarmonyMethod(typeof(Plugin),nameof(ObserveGlassOverlapScene)));
                lightingHarmony.Patch(AccessTools.Method(typeof(ClientPlugin.Rendering.Shaders.WorldConstants),"Frame"),postfix:new HarmonyMethod(typeof(Plugin),nameof(CaptureLightingFrame)));
                lightingHarmony.Patch(AccessTools.Method(typeof(NativeBackend),"EndFrame"),postfix:new HarmonyMethod(typeof(Plugin),nameof(ObserveGlassOverlapVulkan)));return;
            }
            lightingHarmony.Patch(AccessTools.Method(typeof(ClientPlugin.Rendering.Scene.VulkanScene),"BuildScene"),postfix:new HarmonyMethod(typeof(Plugin),nameof(CaptureLightingLods)));
            lightingHarmony.Patch(AccessTools.Method(typeof(BackendRuntime),"RaiseFrame"),postfix:new HarmonyMethod(typeof(Plugin),nameof(QueueLightingReadback)));
            lightingHarmony.Patch(AccessTools.Method(typeof(NativeBackend),"EndFrame"),postfix:new HarmonyMethod(typeof(Plugin),nameof(CompleteLightingReadback)));
            lightingHarmony.Patch(AccessTools.Method(typeof(ClientPlugin.Rendering.Shaders.WorldConstants),"Frame"),postfix:new HarmonyMethod(typeof(Plugin),nameof(CaptureLightingFrame)));
            lightingHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("ClientPlugin.Rendering.VulkanShadows"),"Update"),postfix:new HarmonyMethod(typeof(Plugin),nameof(CaptureLightingShadows)));
        } else {
            var hbao=AccessTools.TypeByName("VRageRender.MyHBAO");
            lightingHarmony.Patch(AccessTools.Method(hbao,"InitializeConstantBuffer"),prefix:new HarmonyMethod(typeof(Plugin),nameof(PinLightingHbaoJitter)));
            lightingHarmony.Patch(AccessTools.Method(hbao,"Run"),prefix:new HarmonyMethod(typeof(Plugin),nameof(EnsureDx11LightingHbaoJitter)));
            lightingHarmony.Patch(AccessTools.Method(hbao,"InitConstantBuffer"),postfix:new HarmonyMethod(typeof(Plugin),nameof(CaptureDx11HbaoConstants)));
            if(meshDecalMode){
                string meshCompanion=Path.Combine(Path.GetDirectoryName(reports),"Pulsar/Legacy/Local/CreativeFixtureHarness/CreativeFixtureHarness.Dx11.dll");
                lightingDx11Capture=Assembly.LoadFrom(meshCompanion).GetType("CreativeFixtureHarness.Dx11LightingReadback",true).GetMethod("Capture",BindingFlags.Public|BindingFlags.Static);
                lightingDx11Inputs=lightingDx11Capture.DeclaringType.GetMethod("CaptureInputs",BindingFlags.Public|BindingFlags.Static);
                lightingHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyTransparentRendering"),"Render"),postfix:new HarmonyMethod(typeof(Plugin),nameof(ObserveMeshDecalDx11)));
                lightingHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRage.Render11.LightingStage.MyLightsRendering"),"RenderDirectionalEnvironmentLight"),prefix:new HarmonyMethod(typeof(Plugin),nameof(CaptureMeshDecalDx11Inputs)));
                lightingHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyToneMapping"),"Run"),prefix:new HarmonyMethod(typeof(Plugin),nameof(CaptureMeshDecalDx11Buffers)));return;
            }
            if(glassOverlapMode){
                lightingHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyTransparentRendering"),"Render"),prefix:new HarmonyMethod(typeof(Plugin),nameof(ObserveGlassOverlapDx11Begin)),postfix:new HarmonyMethod(typeof(Plugin),nameof(ObserveGlassOverlapDx11)));
                lightingHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyTransparentModelRenderer"),"Render"),prefix:new HarmonyMethod(typeof(Plugin),nameof(ObserveGlassOverlapDx11OldBegin)));
                lightingHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRage.Render11.GeometryStage2.Rendering.MyGeometryRenderer"),"RenderTransparent"),prefix:new HarmonyMethod(typeof(Plugin),nameof(ObserveGlassOverlapDx11NewBegin)));
                return;
            }
            string companion=Path.Combine(Path.GetDirectoryName(reports),"Pulsar/Legacy/Local/CreativeFixtureHarness/CreativeFixtureHarness.Dx11.dll");
            lightingDx11Capture=Assembly.LoadFrom(companion).GetType("CreativeFixtureHarness.Dx11LightingReadback",true).GetMethod("Capture",BindingFlags.Public|BindingFlags.Static);
            lightingDx11Inputs=lightingDx11Capture.DeclaringType.GetMethod("CaptureInputs",BindingFlags.Public|BindingFlags.Static);
            lightingDx11Fxaa=lightingDx11Capture.DeclaringType.GetMethod("CaptureFxaa",BindingFlags.Public|BindingFlags.Static);
            var fxaa=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyFXAA"),"Run");
            lightingHarmony.Patch(fxaa,prefix:new HarmonyMethod(typeof(Plugin),nameof(CaptureDx11BeforeFxaa)),postfix:new HarmonyMethod(typeof(Plugin),nameof(CaptureDx11AfterFxaa)));
            lightingHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRage.Render11.LightingStage.MyLightsRendering"),"RenderDirectionalEnvironmentLight"),prefix:new HarmonyMethod(typeof(Plugin),nameof(CaptureDx11LightingInputs)));
            var tone=AccessTools.TypeByName("VRageRender.MyToneMapping");
            if(tone==null)throw new InvalidOperationException("Installed DX11 tone-mapping stage is unavailable.");
            lightingHarmony.Patch(AccessTools.Method(tone,"Run"),prefix:new HarmonyMethod(typeof(Plugin),nameof(CaptureDx11Lighting)));
        }
    }
    static void PinLightingHbaoJitter(ref int? randomSeed){randomSeed=LightingHbaoSeed;lightingHbaoPinned=true;}
    static void EnsureVulkanLightingHbaoJitter(NativeBackend __instance,NativeHbaoConfig __0){if(!lightingHbaoPinned)__instance.InitializeHbaoJitter(LightingHbaoSeed);if(Volatile.Read(ref lightingPending)==1||lightingOwner?.meshDecalMode==true){lightingHbaoConstants=new byte[208];Buffer.BlockCopy(__0.Constants,0,lightingHbaoConstants,0,200);}}
    static void EnsureDx11LightingHbaoJitter(object[] __args){if(!lightingHbaoPinned)AccessTools.Method(AccessTools.TypeByName("VRageRender.MyHBAO"),"InitializeConstantBuffer").Invoke(null,new object[]{__args[0],(int?)LightingHbaoSeed});}
    static void CaptureDx11HbaoConstants(object __result){
        if(Volatile.Read(ref lightingPending)!=1)return;int size=Marshal.SizeOf(__result);if(size!=208)throw new InvalidOperationException("Unexpected stock HBAO global layout: "+size);
        var memory=Marshal.AllocHGlobal(size);try{Marshal.StructureToPtr(__result,memory,false);var bytes=new byte[size];Marshal.Copy(memory,bytes,0,size);lightingHbaoConstants=bytes;}finally{Marshal.FreeHGlobal(memory);}
    }
    static void SaveLightingHbaoConstants(){if(lightingHbaoConstants==null)throw new InvalidOperationException("Actual HBAO constants were not observed for the lighting frame.");File.WriteAllBytes(Path.Combine(lightingOwner.reports,"lighting-buffers/hbao-constants.bin"),lightingHbaoConstants);}
    static MyRenderSettings1 LightingQuality()=>new MyRenderSettings1{
        HqTarget=Environment.GetEnvironmentVariable("SE_VULKAN_MESH_HDR16")=="1",HqDepth=true,DRScaling=false,AmbientOcclusionEnabled=true,
        AntialiasingMode=(MyAntialiasingMode)1,ShadowQuality=(MyShadowsQuality)2,
        TextureQuality=(MyTextureQuality)2,VoxelTextureQuality=(MyTextureQuality)2,
        AnisotropicFiltering=(MyTextureAnisoFiltering)4,ModelQuality=(MyRenderQualityEnum)2,VoxelQuality=(MyRenderQualityEnum)2,
        ShadowGPUQuality=(MyRenderQualityEnum)2,ShadowMemoryQuality=(MyRenderQualityEnum)2,
        VoxelShaderQuality=(MyRenderQualityEnum)2,AlphaMaskedShaderQuality=(MyRenderQualityEnum)2,
        AtmosphereShaderQuality=(MyRenderQualityEnum)2,LightsQuality=(MyRenderQualityEnum)2,ParticleQuality=(MyRenderQualityEnum)2,
        GrassDrawDistance=1000,GrassDensityFactor=3,DistanceFade=2000};
    static void PinMeshImmediateProbes(ref MyEnvironmentData data){data.EnvironmentProbe.TimeOut=0;}
    void PinLightingGraphics()
    {
        if(lightingOwner!=this)return;
        var graphics=MyVideoSettingsManager.CurrentGraphicsSettings;
        graphics.PerformanceSettings.RenderSettings=LightingQuality();graphics.PostProcessingEnabled=true;graphics.FlaresIntensity=1;graphics.FieldOfView=(float)(Math.PI*70/180);
        MyVideoSettingsManager.Apply(graphics);MyVideoSettingsManager.SaveCurrentSettings();
        // Stock MyRenderSettings1.Equals omits HqTarget, so Apply can skip
        // forwarding a preset whose only change is the HDR target format.
        if(MyRenderProxy.Settings.User.HqTarget!=graphics.PerformanceSettings.RenderSettings.HqTarget)
            MyRenderProxy.SwitchRenderSettings(graphics.PerformanceSettings.RenderSettings);
    }
    void RequestLightingDiagnostics(){if(lightingOwner==this){
        if(!lightingHbaoPinned)throw new InvalidOperationException("Lighting capture requires its actual HBAO random seed pinned.");
        if(Environment.GetEnvironmentVariable("SE_VULKAN_STATIC_LIGHTING_DIAGNOSTICS")=="1" && (!staticLightingTurbineHidden || turbine.Render.Visible))throw new InvalidOperationException("Static lighting capture requires the asynchronous turbine caster hidden.");
        SaveCaptureQuality();
        lightingShadowState=null;lightingLodState=null;lightingActorState=null;lightingInputsReady=0;lightingPostprocessReady=0;lightingHbaoConstants=null;Interlocked.Exchange(ref lightingPending,1);
    }}
    void SaveCaptureQuality(){
        if(!lightingHbaoPinned)throw new InvalidOperationException("Capture quality requires the actual HBAO seed pinned.");
        var expected=LightingQuality();var actual=MyRenderProxy.Settings.User;
        if(!actual.Equals(ref expected)||actual.HqTarget!=expected.HqTarget)throw new InvalidOperationException("Lighting comparison lost its pinned render-quality settings.");
        var settings=typeof(MyRenderSettings1).GetFields(BindingFlags.Instance|BindingFlags.Public).Select(f=>new XElement("Setting",new XAttribute("Name",f.Name),new XAttribute("Value",Convert.ToString(f.GetValue(actual),System.Globalization.CultureInfo.InvariantCulture))));
        var graphics=MyVideoSettingsManager.CurrentGraphicsSettings;
        new XDocument(new XElement("LightingQuality",new XAttribute("Schema",1),settings,
            new XElement("Setting",new XAttribute("Name","Diagnostics.HbaoSeed"),new XAttribute("Value",LightingHbaoSeed)),
            new XElement("Setting",new XAttribute("Name","Graphics.FieldOfView"),new XAttribute("Value",graphics.FieldOfView.ToString("R",System.Globalization.CultureInfo.InvariantCulture))),
            new XElement("Setting",new XAttribute("Name","Graphics.FlaresIntensity"),new XAttribute("Value",graphics.FlaresIntensity.ToString("R",System.Globalization.CultureInfo.InvariantCulture))),
            new XElement("Setting",new XAttribute("Name","Graphics.PostProcessingEnabled"),new XAttribute("Value",graphics.PostProcessingEnabled)))).Save(Path.Combine(reports,"lighting-quality.xml"));
    }
    static void CaptureLightingFrame(int width,int height,byte[] __result)
    {if(lightingOwner!=null && width==640 && height==480)lightingWorldFrame=(byte[])__result.Clone();}
    static void CaptureLightingShadows(object __instance)
    {
        if(Volatile.Read(ref lightingPending)!=1)return;
        var type=__instance.GetType();object Value(string name)=>type.GetProperty(name,BindingFlags.Instance|BindingFlags.Public).GetValue(__instance,null);
        lightingShadowState=new XElement("ShadowState",new XAttribute("StockScheduling",Value("StockSchedulingEnabled")),new XAttribute("StockFitting",Value("StockFittingEnabled")),new XAttribute("UpdateMask",Value("UpdateMask")),new XAttribute("ValidMask",Value("ValidMask")),new XAttribute("DrawDistance",Value("DrawDistance"))).ToString();
    }
    static void CaptureLightingLods(ClientPlugin.Rendering.Scene.VulkanScene __instance,NativeBackend __0)
    {
        if(Volatile.Read(ref lightingPending)!=1)return;
        var stats=__instance.GetActorLodStats();var views=__instance.GetNewActorViewStats();
        EnsureLightingTerms(__0);__0.SetLightingDiagnostics(true);
        lightingScene=__instance;
        lightingLodState=new XElement("ActorLodState",new XAttribute("Enabled",__instance.ActorLodsEnabled),new XAttribute("NewViewsEnabled",__instance.NewActorViewsEnabled),
            new XAttribute("SelectedActors",stats.SelectedActors),new XAttribute("ReducedActors",stats.ReducedActors),new XAttribute("TransitionActors",stats.TransitionActors),new XAttribute("NewViewActors",views.Actors)).ToString();
        object Field(object value,string name)=>AccessTools.Field(value.GetType(),name).GetValue(value);
        var actorMap=(System.Collections.IDictionary)AccessTools.Field(typeof(ClientPlugin.Rendering.Scene.VulkanScene),"actors").GetValue(__instance);
        var visible=(System.Collections.Generic.HashSet<uint>)AccessTools.Field(typeof(ClientPlugin.Rendering.Scene.VulkanScene),"visible").GetValue(__instance);
        var camera=__0.CameraPosition;
        var root=new XElement("LightingActors",new XAttribute("Schema",1),new XAttribute("CameraX",camera.X),new XAttribute("CameraY",camera.Y),new XAttribute("CameraZ",camera.Z),
            new XAttribute("ViewProjectionByteOffset",new ReflectedConstantBuffer(__0.WorldFrameLayout,"Frame").OffsetOf("frame_.Environment.view_projection_matrix")));
        foreach(System.Collections.DictionaryEntry entry in actorMap){
            var actor=entry.Value;var model=Field(actor,"Model");if(model==null)continue;
            var selected=Field(actor,"RenderModel");var bounds=(VRageMath.BoundingBoxD)Field(actor,"Bounds");
            root.Add(new XElement("Actor",new XAttribute("Id",entry.Key),new XAttribute("Visible",visible.Contains((uint)entry.Key)),new XAttribute("Model",Field(model,"Name")),
                new XAttribute("SelectedModel",selected==null?"":Field(selected,"Name")),new XAttribute("ShaderLod",Field(actor,"RenderLod")),new XAttribute("Flags",Field(actor,"Flags")),
                new XAttribute("Character",Field(actor,"Character")),new XAttribute("SkinMatrices",((Array)Field(actor,"SkinMatrices"))?.Length??0),new XAttribute("StaticInstances",((Array)Field(actor,"StaticInstances"))?.Length??0),
                new XAttribute("MinX",bounds.Min.X),new XAttribute("MinY",bounds.Min.Y),new XAttribute("MinZ",bounds.Min.Z),new XAttribute("MaxX",bounds.Max.X),new XAttribute("MaxY",bounds.Max.Y),new XAttribute("MaxZ",bounds.Max.Z),
                new XElement("LodDistances",((float[])Field(model,"LodDistances")).Select(distance=>new XElement("Distance",distance)))));
        }
        lightingActorState=root.ToString();
    }
    static void QueueLightingReadback()
    {
        if(Volatile.Read(ref lightingPending)!=1 || lightingQueued!=0 || lightingShadowState==null)return;
        try {
            var frame=new byte[344];if(!GuestBridge.TryGetFrame(frame))throw new InvalidOperationException("Current guest frame unavailable for capture.");
            int width=(int)BitConverter.ToUInt32(frame,8),height=(int)BitConverter.ToUInt32(frame,12);
            if(width!=640 || height!=480)throw new InvalidOperationException("Lighting readback requires the actual fixed 640x480 viewport.");
            if(lightingPipeline==0) {
                string file=Path.Combine(lightingOwner.reports,"LightingBufferCopy.hlsl");File.WriteAllText(file,LightingCopySource);
                using(var compiler=new VulkanShaderCompiler(Path.Combine(NativeBackend.AssetsRoot,"Tools/win-x64"),Path.Combine(Path.GetDirectoryName(lightingOwner.reports),"LightingShaderCache"))) {
                    var result=compiler.Compile(new VulkanShaderRequest{SourcePath=file,ContentDirectory=lightingOwner.reports,Profile="cs_5_0",EntryPoint="main"});
                    lightingPipeline=GuestBridge.CreateExtendedPipeline(new byte[]{1,0,0,0},SpirvRuntimeModule.Prepare(File.ReadAllBytes(result.SpirvPath)),"compute","main",1);
                }
                if(lightingPipeline==0)throw new InvalidOperationException("Lighting copy pipeline unavailable.");
                for(int i=0;i<5;i++)GuestBridge.CreateImage(LightingOutput+(ulong)i,width,height,1,6);
            }
            if(!GuestBridge.SubmitExtended(4,5,width,height,1,lightingPipeline,Enumerable.Range(0,5).Select(i=>LightingOutput+(ulong)i).ToArray(),null,
                new[]{0xffff000000000003UL,0xffff000000000004UL,0xffff000000000005UL,0xffff000000000002UL,0xffff000000000001UL},1024))throw new InvalidOperationException("Lighting copy batch unavailable.");
            lightingFrame=frame;lightingQueued=1;
        }catch(Exception error){LightingCaptureFailure(error);}
    }
    static void CompleteLightingReadback(NativeBackend __instance)
    {
        if(Interlocked.Exchange(ref lightingQueued,0)!=1)return;
        try {
            string folder=Path.Combine(lightingOwner.reports,"lighting-buffers");Directory.CreateDirectory(folder);
            SaveLightingHbaoConstants();SaveLightingFlares();SaveLightingProbes(__instance,folder);SaveLightingGlass(__instance);SaveLightingStages(__instance,folder);
            if(lightingLodState==null)throw new InvalidOperationException("Actual actor LOD state was not observed for the lighting frame.");File.WriteAllText(Path.Combine(folder,"actor-lod-state.xml"),lightingLodState);
            if(lightingActorState==null)throw new InvalidOperationException("Actual actor state was not observed for the lighting frame.");File.WriteAllText(Path.Combine(folder,"actors.xml"),lightingActorState);
            for(int i=0;i<5;i++){var bytes=new byte[640*480*16];GuestBridge.Readback(LightingOutput+(ulong)i,bytes);File.WriteAllBytes(Path.Combine(folder,LightingNames[i]+".rgba32f"),bytes);}
            for(uint i=0;i<2;i++)File.WriteAllBytes(Path.Combine(folder,i==0?"screen-ao.r8":"sun-visibility.r8"),__instance.ReadbackLightingInput(i,640,480));
            File.WriteAllBytes(Path.Combine(folder,"pre-fxaa.rgba8"),__instance.ReadbackFxaaInput(false,640,480));
            File.WriteAllBytes(Path.Combine(folder,"post-fxaa.rgba8"),__instance.ReadbackFxaaInput(true,640,480));lightingPostprocessReady=3;
            if(lightingShadowState==null)throw new InvalidOperationException("Current shadow mode was not observed before lighting capture.");File.WriteAllText(Path.Combine(folder,"shadow-state.xml"),lightingShadowState);
            if(lightingWorldFrame==null || lightingWorldFrame.Length!=1280)throw new InvalidOperationException("Current main frame constants were not captured.");
            File.WriteAllBytes(Path.Combine(folder,"frame.bin"),lightingWorldFrame);File.WriteAllBytes(Path.Combine(folder,"guest-frame.bin"),lightingFrame);
            var stats=__instance.GetStats();
            new XDocument(new XElement("LightingBuffers",new XAttribute("Backend","Vulkan"),new XAttribute("Width",640),new XAttribute("Height",480),
                new XAttribute("Frame",BitConverter.ToUInt32(lightingFrame,16)),new XAttribute("DeviceEpoch",GuestBridge.ResourceEpoch),new XAttribute("Thread",Thread.CurrentThread.ManagedThreadId),
                new XAttribute("ValidationErrors",stats.ValidationErrors),new XAttribute("Stage","BeforeTonemap"),new XAttribute("LightingInputs",1),new XAttribute("PostprocessInputs",1),new XAttribute("CapturedUtc",DateTime.UtcNow.ToString("o")),
                new XElement("Scope","Current-frame decoded GBuffer/depth and linear HDR before tone mapping; producer medium disabled."))).Save(Path.Combine(folder,"complete.xml"));
            Interlocked.Exchange(ref lightingPending,2);
        }catch(Exception error){LightingCaptureFailure(error);}
    }
    static void LightingCaptureFailure(Exception error)
    {Interlocked.Exchange(ref lightingPending,-1);lightingQueued=0;File.WriteAllText(Path.Combine(lightingOwner.reports,"lighting-capture-failed.txt"),error.ToString());}
    static void CaptureDx11Lighting(object[] __args)
    {
        if(Volatile.Read(ref lightingInputsReady)!=1)return;
        if(Interlocked.CompareExchange(ref lightingPending,2,1)!=1)return;
        try{lightingDx11Capture.Invoke(null,new object[]{lightingOwner.reports,__args[0],LightingCopySource});SaveLightingHbaoConstants();SaveLightingFlares();SaveLightingGlass(null);lightingPostprocessReady=1;}catch(Exception error){LightingCaptureFailure(error);}
    }
    static void CaptureDx11BeforeFxaa(object[] __args){
        if(Volatile.Read(ref lightingPostprocessReady)!=1)return;
        try{lightingDx11Fxaa.Invoke(null,new object[]{lightingOwner.reports,__args[0],__args[2],false});lightingPostprocessReady=2;}catch(Exception error){LightingCaptureFailure(error);}
    }
    static void CaptureDx11AfterFxaa(object[] __args){
        if(Volatile.Read(ref lightingPostprocessReady)!=2)return;
        try{lightingDx11Fxaa.Invoke(null,new object[]{lightingOwner.reports,__args[0],__args[1],true});var path=Path.Combine(lightingOwner.reports,"lighting-buffers/complete.xml");var document=XDocument.Load(path);document.Root.SetAttributeValue("PostprocessInputs",1);document.Save(path);lightingPostprocessReady=3;}catch(Exception error){LightingCaptureFailure(error);}
    }
    static void CaptureDx11LightingInputs(object[] __args)
    {
        if(Volatile.Read(ref lightingPending)!=1 || Volatile.Read(ref lightingInputsReady)!=0)return;
        try{lightingDx11Inputs.Invoke(null,new object[]{__args[0],__args[1],__args[2]});Interlocked.Exchange(ref lightingInputsReady,1);}catch(Exception error){LightingCaptureFailure(error);}
    }
    void ShutdownLightingDiagnostics(){lightingHarmony?.UnpatchAll(lightingHarmony.Id);lightingOwner=null;}
}
