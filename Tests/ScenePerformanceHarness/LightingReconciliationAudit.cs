using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using HarmonyLib;
using Sandbox;
using VRageRender;

namespace ScenePerformanceHarness;
public sealed partial class Plugin {
    static bool lightingReconciliation;
    static bool lightingRetainedSpots;
    static bool lightingGpuScene,lightingNativePass;static uint lightingSpotResolution;static string lightingProperty;
    static volatile int lightingRequested=-1,lightingActive=-1,lightingCaptured=-1;
    static int lightingFrames;static bool lightingReadRequested;static ulong lightingBeforePresent;
    static double lightingStarted;
    static string lightingReports;
    static readonly string[] lightingNames={"control","treatment","control-return"};
    static NativeLight[] lightingLights,lightingFrozenLights;
    static NativeSpot[] lightingSpots,lightingFrozenSpots;
    static NativeProbeConfig lightingProbe,lightingFrozenProbe;
    static byte[] lightingFrame,lightingFrozenFrame;
    static NativePostprocessConfig lightingPost,lightingFrozenPost;
    static bool lightingHaveFrozenProbe,lightingHaveFrozenPost;
    static string LightingHash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static byte[] LightingJson(object value)=>JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions{IncludeFields=true});
    static NativeLight[] CloneLights(NativeLight[] values)=>values.Select(x=>new NativeLight{PositionRange=(float[])x.PositionRange.Clone(),ColorIntensity=(float[])x.ColorIntensity.Clone(),DirectionCone=(float[])x.DirectionCone.Clone(),Flags=x.Flags,Reserved0=x.Reserved0,Reserved1=x.Reserved1,Reserved2=x.Reserved2}).ToArray();
    static NativeSpot[] CloneSpots(NativeSpot[] values)=>values.Select(x=>new NativeSpot{Light=x.Light,Flags=x.Flags,Cookie=x.Cookie,Projection=(float[])x.Projection.Clone()}).ToArray();
    void InstallLightingReconciliation(){
        lightingReconciliation=Environment.GetEnvironmentVariable("SE_VULKAN_LIGHTING_RECONCILIATION")=="1";
        lightingRetainedSpots=Environment.GetEnvironmentVariable("SE_VULKAN_LIGHTING_RETAINED_SPOTS")=="1";
        lightingGpuScene=Environment.GetEnvironmentVariable("SE_VULKAN_LIGHTING_GPU_SCENE")=="1";
        lightingNativePass=Environment.GetEnvironmentVariable("SE_VULKAN_LIGHTING_NATIVE_PASS")=="1";
        // Any boolean scene policy can be the OFF/ON/OFF treatment.
        lightingProperty=Environment.GetEnvironmentVariable("SE_VULKAN_LIGHTING_PROPERTY") is {Length:>0} name?name:null;
        if(!lightingReconciliation)return;if(!selectionAudit||expected!="Vulkan")throw new Exception("Lighting reconciliation requires private paused Vulkan selection capture.");
        lightingReports=reports;
        lightingRequested=lightingActive=lightingCaptured=-1;lightingFrozenLights=null;lightingFrozenSpots=null;lightingFrozenFrame=null;lightingHaveFrozenProbe=lightingHaveFrozenPost=false;
        harmony.Patch(AccessTools.Method(typeof(NativeBackend),nameof(NativeBackend.SetLights)),prefix:new HarmonyMethod(typeof(Plugin),nameof(LightingSetLights)));
        harmony.Patch(AccessTools.Method(typeof(NativeBackend),nameof(NativeBackend.SetSpots)),prefix:new HarmonyMethod(typeof(Plugin),nameof(LightingSetSpots)));
        // The one-argument SetProbes forwards here; patch the complete overload.
        harmony.Patch(AccessTools.Method(typeof(NativeBackend),nameof(NativeBackend.SetProbes),new[]{typeof(NativeProbeConfig),typeof(uint)}),prefix:new HarmonyMethod(typeof(Plugin),nameof(LightingSetProbes)));
        // Probe configuration queued through the snapshot path (absent in older pinned plugins).
        var deferredProbes=AccessTools.Method(typeof(NativeBackend),"SetProbesDeferred",new[]{typeof(NativeProbeConfig),typeof(uint)});if(deferredProbes!=null)harmony.Patch(deferredProbes,prefix:new HarmonyMethod(typeof(Plugin),nameof(LightingSetProbes)));
        harmony.Patch(AccessTools.Method(typeof(NativeBackend),nameof(NativeBackend.SetPostprocess)),prefix:new HarmonyMethod(typeof(Plugin),nameof(LightingSetPostprocess)));
        harmony.Patch(AccessTools.Method(typeof(NativeBackend),nameof(NativeBackend.QueueWorld)),prefix:new HarmonyMethod(typeof(Plugin),nameof(LightingQueueWorld)));
        harmony.Patch(AccessTools.Method(typeof(MyVulkanRender),nameof(MyVulkanRender.Present)),prefix:new HarmonyMethod(typeof(Plugin),nameof(LightingPresent)));
        harmony.Patch(AccessTools.Method(typeof(VulkanProbes),nameof(VulkanProbes.Configure)),prefix:new HarmonyMethod(typeof(Plugin),nameof(LightingConfigureProbe)));
    }
    static void LightingConfigureProbe(VulkanProbes __instance){if(lightingActive>=0)__instance.Settings.TimeOut=0;}
    static void LightingSetLights(ref NativeLight[] lights){if(lightingActive<0)return;if(lightingFrozenLights!=null)lights=CloneLights(lightingFrozenLights);lightingLights=CloneLights(lights);}
    static void LightingSetSpots(uint resolution,ref NativeSpot[] spots){lightingSpotResolution=resolution;if(lightingActive<0)return;if(lightingFrozenSpots!=null)spots=CloneSpots(lightingFrozenSpots);lightingSpots=CloneSpots(spots);}
    static void LightingSetProbes(ref NativeProbeConfig config){if(lightingActive<0)return;if(lightingHaveFrozenProbe){config.Frames=(byte[])lightingFrozenProbe.Frames.Clone();config.Projections=(float[])lightingFrozenProbe.Projections.Clone();config.Brdf=lightingFrozenProbe.Brdf;}lightingProbe=config;lightingProbe.Frames=(byte[])config.Frames.Clone();lightingProbe.Projections=(float[])config.Projections.Clone();}
    static void LightingSetPostprocess(ref NativePostprocessConfig config){if(lightingActive<0)return;if(lightingHaveFrozenPost)config=lightingFrozenPost;lightingPost=config;}
    static void LightingQueueWorld(NativeBackend __instance,ref byte[] frame){if(lightingActive<0)return;if(lightingFrozenFrame!=null)frame=(byte[])lightingFrozenFrame.Clone();lightingFrame=(byte[])frame.Clone();if(!lightingRetainedSpots)__instance.SetSpotRedraw(ulong.MaxValue,ulong.MaxValue);}
    static void LightingPresent(object __instance){if(!lightingReadRequested||SplitFrames)return;var backend=(NativeBackend)BackendField.GetValue(__instance);lightingBeforePresent=backend.GetStats().SubmittedFrames;backend.RequestReadback();}
    // Separate and not inlined: pinned baseline plugins predate the packet signatures.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void EnableSignatures(object renderer)=>((NativeBackend)BackendField.GetValue(renderer)).PacketSignatureEnabled=true;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void WriteSignatures(NativeBackend backend,string path){
        File.WriteAllText(Path.Combine(path,"packets.sig"),backend.LastPacketSignature.ToString("X16")+" "+backend.LastPacketSignatureCount);
        File.WriteAllText(Path.Combine(path,"billboards.txt"),backend.LastBillboardSignature.ToString("X16")+" "+backend.BillboardPackets.Count);
        try{WriteGlassSignature(backend,path);}catch(MissingMemberException){}}
    // Separate again: plugins before the glass signature lack LastGlassSignature.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void WriteGlassSignature(NativeBackend backend,string path)=>File.WriteAllText(Path.Combine(path,"glass.sig"),backend.LastGlassSignature.ToString("X16")+" "+backend.LastGlassSignatureCount);
    static void LightingBeginDraw(object renderer){
        if(!lightingReconciliation||lightingRequested<0||renderer is not MyVulkanRender)return;
        var scene=SceneField.GetValue(renderer);var property=AccessTools.Property(scene.GetType(),lightingProperty??(lightingGpuScene?"GpuOpaqueOwnershipEnabled":lightingNativePass?"NativePassSceneEnabled":"ResidentProxiesEnabled"));if(property==null)throw new Exception("Pinned diagnostic plugin lacks the requested ownership switch.");
        if(lightingActive!=lightingRequested){lightingActive=lightingRequested;lightingFrames=0;lightingStarted=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;property.SetValue(scene,lightingActive==1);}
        try{EnableSignatures(renderer);}catch(MissingMethodException){}
        var shadows=Member(renderer,"shadows");((MyShadowsSettings)Member(shadows,"Settings")).Data.UpdateCascadesEveryFrame=true;
        lightingReadRequested=lightingFrames>=40&&Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency-lightingStarted>=2&&lightingCaptured!=lightingActive;
    }
    // Under the frame split the launch thread submits the frame, so the render-thread readback below never lands. The
    // game's screenshot path (captures joined by the renderer) works there: view.png only, no probe or shadow dumps.
    static int lightingShotFrame=-1;
    static bool LightingSplitShot(object renderer){
        string path=Path.Combine(lightingReports,"Lighting",lightingNames[lightingActive]),png=Path.Combine(path,"view.png");
        if(lightingShotFrame<0){Directory.CreateDirectory(path);MyRenderProxy.TakeScreenshot(VRageMath.Vector2.One,png,debug:false,ignoreSprites:false,showNotification:false);lightingShotFrame=lightingFrames;return false;}
        if(lightingFrames-lightingShotFrame<10||!File.Exists(png))return false;
        var scene=SceneField.GetValue(renderer);var cellCount=AccessTools.Property(scene.GetType(),"NativeCellCount");
        File.WriteAllText(Path.Combine(path,"state.json"),JsonSerializer.Serialize(new{Phase=lightingNames[lightingActive],SameProcess=true,Split=true,Paused=MySandboxGame.IsPaused,Frames=lightingFrames,NativeCellsOwned=cellCount?.GetValue(scene),Scope="Split-mode screenshot only: lights frozen through the same SetLights/SetSpots/QueueWorld hooks; no probe, cascade or spot readback."},new JsonSerializerOptions{WriteIndented=true}));
        if(lightingActive==0){lightingFrozenLights=CloneLights(lightingLights);lightingFrozenSpots=CloneSpots(lightingSpots);lightingFrozenFrame=(byte[])lightingFrame.Clone();lightingFrozenProbe=lightingProbe;lightingHaveFrozenProbe=true;lightingFrozenPost=lightingPost;lightingHaveFrozenPost=true;}
        lightingShotFrame=-1;return true;
    }
    static void LightingAfterPresent(object renderer){
        if(!lightingReconciliation||lightingActive<0||renderer is not MyVulkanRender)return;lightingFrames++;if(!lightingReadRequested)return;
        if(SplitFrames){if(LightingSplitShot(renderer)){lightingReadRequested=false;lightingCaptured=lightingActive;}return;}
        var backend=(NativeBackend)BackendField.GetValue(renderer);var schedule=backend.GetProbeScheduleStats();if(schedule.Busy!=0||schedule.Stage!=0)throw new Exception("Lighting snapshot lacks a completed all-face probe cycle.");
        if(backend.GetStats().SubmittedFrames==lightingBeforePresent)return;
        string path=Path.Combine(lightingReports,"Lighting",lightingNames[lightingActive]);Directory.CreateDirectory(path);
        var pixels=backend.Readback(1920,1080);File.WriteAllBytes(Path.Combine(path,"view.rgba8"),pixels);AccessTools.Method(AccessTools.TypeByName("ClientPlugin.Rendering.Content.PngWriter"),"Save").Invoke(null,new object[]{Path.Combine(path,"view.png"),1920,1080,pixels});
        var hashes=new Dictionary<string,string>();for(uint kind=0;kind<6;kind++)for(uint face=0;face<6;face++){byte[] bytes=backend.ReadbackProbe(kind,face,0,(int)lightingProbe.Resolution);string name=$"probe-{kind}-{face}.f16";File.WriteAllBytes(Path.Combine(path,name),bytes);hashes[name]=LightingHash(bytes);}
        var shadows=Member(renderer,"shadows");int resolution=Convert.ToInt32(Member(shadows,"lastResolution"));uint count=(uint)((MyShadowsSettings)Member(shadows,"Settings")).Data.CascadesCount;
        for(uint cascade=0;cascade<count;cascade++){float[] values=backend.ReadbackShadow(cascade,resolution);byte[] bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);string name=$"cascade-{cascade}.f32";File.WriteAllBytes(Path.Combine(path,name),bytes);hashes[name]=LightingHash(bytes);}
        // Spotlight shadow maps of the replayed spot set: a missing or extra
        // caster under one ownership shows up here as a depth difference.
        if(lightingSpots!=null&&lightingSpotResolution!=0)foreach(var spot in lightingSpots){if((spot.Flags&1)==0)continue;float[] values=backend.ReadbackSpot(spot.Light,(int)lightingSpotResolution);byte[] bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);string name=$"spot-{spot.Light}.f32";File.WriteAllBytes(Path.Combine(path,name),bytes);hashes[name]=LightingHash(bytes);}
        // Packet signature gate: the queued world packets of this frame, for forked-producer toggles.
        try{WriteSignatures(backend,path);}catch(MissingMethodException){}
        File.WriteAllBytes(Path.Combine(path,"main-frame.bin"),lightingFrame);File.WriteAllBytes(Path.Combine(path,"probe-frames.bin"),lightingProbe.Frames);
        var scene=SceneField.GetValue(renderer);var lightOwners=Items(Member(scene,"lights")).Select(component=>new{Owner=Member(Member(component,"Owner"),"ID"),Name=Member(Member(component,"Owner"),"DebugName"),Position=AuditValue(Member(component,"Position")),Data=AuditValue(Member(component,"Data"))}).ToArray();
        object audit=new{Phase=lightingNames[lightingActive],SameProcess=true,GpuScenePolicy=lightingGpuScene,RetainedSpotPolicy=lightingRetainedSpots,Paused=MySandboxGame.IsPaused,Frames=lightingFrames,Width=1920,Height=1080,MainFrameHash=LightingHash(lightingFrame),Lights=lightingLights,LightHash=LightingHash(LightingJson(lightingLights)),Spots=lightingSpots,SpotHash=LightingHash(LightingJson(lightingSpots)),Postprocess=lightingPost,PostprocessHash=LightingHash(LightingJson(lightingPost)),ProbeFrameHash=LightingHash(lightingProbe.Frames),ProbeProjectionHash=LightingHash(LightingJson(lightingProbe.Projections)),Probe=backend.GetProbeStats(),Schedule=schedule,ProbeResolution=lightingProbe.Resolution,CascadeResolution=resolution,CascadeMatrices=AuditValue(Member(shadows,"worldMatrices")),LightOwners=lightOwners,ImageHashes=hashes,Scope="Diagnostic OFF/ON/OFF in one paused private loaded world. After control capture, actual local lights, spot projections, main/probe constants and postprocess config are replayed. Probe timeout zero and all cascades refreshed identically; retained spotlight policy is identified separately. No ordinary performance or production quality-setting claim."};
        File.WriteAllText(Path.Combine(path,"state.json"),JsonSerializer.Serialize(audit,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        // Native OLD cell ownership for this phase, so a toggled policy is proven active.
        var cellCount=AccessTools.Property(scene.GetType(),"NativeCellCount");if(cellCount!=null){var cellStats=backend.GetCellStats();File.WriteAllText(Path.Combine(path,"cells.json"),JsonSerializer.Serialize(new{Owned=cellCount.GetValue(scene),cellStats.Live,cellStats.Drawn,cellStats.Draws,cellStats.ObjectBytes,cellStats.InstanceBytes}));}
        if(lightingActive==0){lightingFrozenLights=CloneLights(lightingLights);lightingFrozenSpots=CloneSpots(lightingSpots);lightingFrozenFrame=(byte[])lightingFrame.Clone();lightingFrozenProbe=lightingProbe;lightingHaveFrozenProbe=true;lightingFrozenPost=lightingPost;lightingHaveFrozenPost=true;}
        lightingReadRequested=false;lightingCaptured=lightingActive;
    }
    void CaptureLightingReconciliation(){
        double now=lifetime.Elapsed.TotalSeconds;
        if(selectionStage==0){MySandboxGame.PausePush();ownsSelectionPause=true;selectionUntil=now+8;selectionStage=1;return;}
        if(!MySandboxGame.IsPaused)throw new Exception("Lighting reconciliation lost its pause.");if(selectionStage==1&&now<selectionUntil)return;
        if(selectionStage==1){FreezeSystemFrame();selectionArmed=true;lightingRequested=0;selectionStage=2;selectionUntil=now+120;return;}
        if(lightingCaptured<lightingRequested){if(now>=selectionUntil)throw new Exception("Lighting phase exceeded120seconds.");return;}
        if(lightingRequested<2){lightingRequested++;selectionUntil=now+120;return;}
        File.WriteAllText(Path.Combine(reports,"result.json"),JsonSerializer.Serialize(new{Backend=expected,Paused=true,Instrumented=true,LightingReconciliation=true,SameProcess=true,Phases=lightingNames,Width=1920,Height=1080,Scope="Lighting correctness diagnostics only; no FPS claim."},new JsonSerializerOptions{WriteIndented=true}));WriteSystemParityAudit();ReleaseSelectionPause();finished=true;MySandboxGame.ExitThreadSafe();
    }
}
