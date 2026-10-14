using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using ClientPlugin.Rendering;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
namespace ScenePerformanceHarness;

public sealed partial class Plugin {
    sealed class CameraState {
        public MatrixD ViewMatrix;public Matrix ProjectionMatrix,ProjectionFarMatrix;
        public Vector3D CameraPosition;public float FOV,FOVForSkybox,NearPlane,FarPlane,FarFarPlane,ProjectionOffsetX,ProjectionOffsetY;public bool Smooth;
        public static CameraState Copy(MyRenderMessageSetCameraViewMatrix m)=>new(){ViewMatrix=m.ViewMatrix,ProjectionMatrix=m.ProjectionMatrix,ProjectionFarMatrix=m.ProjectionFarMatrix,CameraPosition=m.CameraPosition,FOV=m.FOV,FOVForSkybox=m.FOVForSkybox,NearPlane=m.NearPlane,FarPlane=m.FarPlane,FarFarPlane=m.FarFarPlane,ProjectionOffsetX=m.ProjectionOffsetX,ProjectionOffsetY=m.ProjectionOffsetY,Smooth=m.Smooth};
        public void Apply(MyRenderMessageSetCameraViewMatrix m){m.ViewMatrix=ViewMatrix;m.ProjectionMatrix=ProjectionMatrix;m.ProjectionFarMatrix=ProjectionFarMatrix;m.CameraPosition=CameraPosition;m.FOV=FOV;m.FOVForSkybox=FOVForSkybox;m.NearPlane=NearPlane;m.FarPlane=FarPlane;m.FarFarPlane=FarFarPlane;m.ProjectionOffsetX=ProjectionOffsetX;m.ProjectionOffsetY=ProjectionOffsetY;m.Smooth=Smooth;}
        public bool Matches(CameraState other)=>other!=null&&ViewMatrix==other.ViewMatrix&&ProjectionMatrix==other.ProjectionMatrix&&ProjectionFarMatrix==other.ProjectionFarMatrix&&CameraPosition==other.CameraPosition&&FOV==other.FOV&&FOVForSkybox==other.FOVForSkybox&&NearPlane==other.NearPlane&&FarPlane==other.FarPlane&&FarFarPlane==other.FarFarPlane&&ProjectionOffsetX==other.ProjectionOffsetX&&ProjectionOffsetY==other.ProjectionOffsetY&&Smooth==other.Smooth;
    }
    sealed class MatchedFrame {public int Version=1;public CameraState Camera;public MyEnvironmentData Environment;}
    static readonly JsonSerializerOptions matchJson=new(){IncludeFields=true,IgnoreReadOnlyProperties=true,WriteIndented=true};
    static CameraState latestSystemCamera;static MyEnvironmentData latestSystemEnvironment;static bool haveSystemEnvironment;
    static MatchedFrame matchedFrame;static bool matchFrameLoaded,matchedFrameApplied;static object vulkanShadowSettings;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<int,object> systemViews=new();
    static bool benchmarkFrameLoaded,benchmarkEnvironmentMatched;
    static string benchmarkFrameHash;
    static long benchmarkFrames,benchmarkCameraMismatches,benchmarkEnvironmentMismatches;
    void InstallSystemParityAudit(){
        shadowDepthDirectory=Environment.GetEnvironmentVariable("SE_VULKAN_SHADOW_DEPTH_AUDIT")=="1"?Path.Combine(reports,"ShadowDepth"):null;
        latestSystemCamera=null;matchedFrame=null;haveSystemEnvironment=matchFrameLoaded=matchedFrameApplied=false;systemViews.Clear();
        string benchmarkPath=Environment.GetEnvironmentVariable("SE_VULKAN_BENCHMARK_FRAME");
        benchmarkFrameLoaded=false;benchmarkFrames=benchmarkCameraMismatches=benchmarkEnvironmentMismatches=0;
        if(!selectionAudit&&!workloadAudit&&string.IsNullOrWhiteSpace(benchmarkPath))return;
        string path=Environment.GetEnvironmentVariable("SE_VULKAN_MATCH_FRAME");
        if(!string.IsNullOrWhiteSpace(benchmarkPath)){
            if(selectionAudit||!string.IsNullOrWhiteSpace(path))throw new Exception("BenchmarkFrame and paused MatchFrame are separate capture modes.");
            byte[] bytes=File.ReadAllBytes(benchmarkPath);benchmarkFrameHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            matchedFrame=JsonSerializer.Deserialize<MatchedFrame>(bytes,matchJson)??throw new Exception("Empty benchmark frame");
            if(matchedFrame.Version!=1||matchedFrame.Camera==null||!matchedFrame.Camera.ViewMatrix.IsValid()||!matchedFrame.Camera.ProjectionMatrix.IsValid()||!matchedFrame.Camera.CameraPosition.IsValid())throw new Exception("Invalid benchmark frame contract");
            benchmarkFrameLoaded=true;
            harmony.Patch(AccessTools.Method(typeof(Sandbox.Game.Gui.MyGuiScreenGamePlay),"HandleInput",new[]{typeof(bool)}),prefix:new HarmonyMethod(typeof(Plugin),nameof(SuppressBenchmarkGameplayInput)));
        }
        if(!string.IsNullOrWhiteSpace(path)){if(!selectionAudit)throw new Exception("Matching frame input is restricted to paused diagnostic captures.");matchedFrame=JsonSerializer.Deserialize<MatchedFrame>(File.ReadAllText(path),matchJson)??throw new Exception("Empty matching frame");if(matchedFrame.Version!=1 || matchedFrame.Camera==null || !matchedFrame.Camera.ViewMatrix.IsValid() || !matchedFrame.Camera.CameraPosition.IsValid())throw new Exception("Invalid matching frame contract");matchFrameLoaded=true;}
        var method=expected=="Dx11"?AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"ProcessMessageInternal"):AccessTools.Method(typeof(MyVulkanRender),"Process");
        harmony.Patch(method,prefix:new HarmonyMethod(typeof(Plugin),nameof(ObserveSystemMessage)));
    }
    static void ObserveSystemMessage(MyRenderMessageBase message){
        // Replay throughout the private diagnostic warmup, so delayed stock light
        // updates and retained images converge before the paused selection frame.
        bool replay=(benchmarkFrameLoaded || matchFrameLoaded || selectionArmed) && matchedFrame!=null;
        if(message is MyRenderMessageSetCameraViewMatrix camera){if(replay)matchedFrame.Camera.Apply(camera);latestSystemCamera=CameraState.Copy(camera);}
        if(message is MyRenderMessageUpdateRenderEnvironment environment){if(replay)environment.Data=matchedFrame.Environment;latestSystemEnvironment=environment.Data;haveSystemEnvironment=true;if(benchmarkFrameLoaded)benchmarkEnvironmentMatched=environment.Data.Equals(matchedFrame.Environment);}
    }
    static bool SuppressBenchmarkGameplayInput()=>!benchmarkFrameLoaded||Sandbox.Game.World.MySession.Static==null||!Sandbox.Game.World.MySession.Static.Ready;
    static void ObserveBenchmarkCompletedFrame(){
        if(!benchmarkFrameLoaded)return;benchmarkFrames++;
        if(!matchedFrame.Camera.Matches(latestSystemCamera))benchmarkCameraMismatches++;
        if(!haveSystemEnvironment||!benchmarkEnvironmentMatched)benchmarkEnvironmentMismatches++;
    }
    void WriteBenchmarkFrameAudit(){
        if(!benchmarkFrameLoaded)return;
        var audit=new System.Collections.Generic.Dictionary<string,object>{
            ["SourceSha256"]=benchmarkFrameHash,["Frames"]=benchmarkFrames,["CameraMismatches"]=benchmarkCameraMismatches,
            ["EnvironmentMismatches"]=benchmarkEnvironmentMismatches,["SimulationPaused"]=Sandbox.MySandboxGame.IsPaused,
            ["GameplayInputSuppressed"]=true,["Target"]=matchedFrame,["ObservedCamera"]=latestSystemCamera,
            ["Scope"]="Fixed full camera and environment renderer inputs throughout warmup and sampling. Simulation remains unpaused; private gameplay input is suppressed to keep the stationary workload. All accepted completed frames checked. No reduced rendering quality or phase instrumentation."};
        File.WriteAllText(Path.Combine(reports,"benchmark-frame.json"),JsonSerializer.Serialize(audit,matchJson));
        if(benchmarkFrames!=Frames.Count||benchmarkCameraMismatches!=0||benchmarkEnvironmentMismatches!=0||Sandbox.MySandboxGame.IsPaused)throw new Exception("Unpaused benchmark camera/environment acceptance failed.");
    }
    static void ApplyMatchedFrameOnce(object renderer){
        if(!selectionArmed || matchedFrame==null || matchedFrameApplied)return;
        var camera=new MyRenderMessageSetCameraViewMatrix();matchedFrame.Camera.Apply(camera);
        var environment=new MyRenderMessageUpdateRenderEnvironment{Data=matchedFrame.Environment};
        if(renderer is MyVulkanRender){var process=AccessTools.Method(typeof(MyVulkanRender),"Process");process.Invoke(renderer,new object[]{environment});process.Invoke(renderer,new object[]{camera});}
        else{var process=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"ProcessMessageInternal");process.Invoke(null,new object[]{environment,0});process.Invoke(null,new object[]{camera,0});}
        matchedFrameApplied=true;
    }
    static void FreezeSystemFrame(){if(matchedFrame==null){if(latestSystemCamera==null || !haveSystemEnvironment)throw new Exception("Diagnostic frame lacks camera/environment inputs.");matchedFrame=new MatchedFrame{Camera=latestSystemCamera,Environment=latestSystemEnvironment};}}
    static object AuditSystemView(object query){return new{ViewId=Number(query,"ViewId"),ViewType=Convert.ToString(Member(query,"ViewType")),ViewIndex=Number(query,"ViewIndex"),Frustum=AuditValue(Member(query,"Frustum")),SmallObjects=AuditValue(Member(query,"SmallObjects")),ViewProjection=AuditValue(Member(query,"ViewProjection"))};}
    static object AuditPartRanges(object parts,string name)=>Items(Member(parts,name)).Select(p=>new{FirstIndex=Number(p,"IndexStart"),IndexCount=Number(p,"IndicesCount"),Material=Convert.ToString(Member(p,"Name")),Technique=Convert.ToString(Member(Member(p,"Material"),"Technique")),Binding=AuditBinding(Member(Member(p,"Material"),"Binding")),Triangles=Number(p,"IndicesCount")/3}).ToArray();
    static object AuditBinding(object binding)=>new{Textures=Items(FrameMember(binding,"TextureHandles")).Select(t=>Convert.ToString(FrameMember(t,"Name"))??t?.GetType().FullName).ToArray(),Alpha=Convert.ToString(FrameMember(FrameMember(binding,"SrvAlphamask"),"Name"))};
    void WriteSystemParityAudit(){
        if(matchedFrame==null)return;
        if(matchFrameLoaded && !matchedFrameApplied)throw new Exception("Matching diagnostic input was never applied to the renderer.");
        File.WriteAllText(Path.Combine(reports,"matched-frame.json"),JsonSerializer.Serialize(matchedFrame,matchJson));
        var rendering=AccessTools.TypeByName("VRageRender.MyRender11");
        object shadows=null;if(expected=="Dx11")shadows=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyShadowCascades"),"Settings").GetValue(null);
        File.WriteAllText(Path.Combine(reports,"system-parity.json"),JsonSerializer.Serialize(new{Backend=expected,MatchedInputReplayed=matchFrameLoaded,MatchedInputApplied=matchedFrameApplied,ObservedRendererInput=new{Camera=latestSystemCamera,Environment=latestSystemEnvironment},Graphics=AuditValue(MyRenderProxy.Settings),ShadowSettings=expected=="Dx11"?AuditValue(shadows):vulkanShadowSettings,Views=systemViews.Values.ToArray(),Scope="Paused renderer inputs and per-view selection policy. Replayed camera/environment is diagnostic only; simulation residency can still differ. Ordinary timings use original unpaused inputs. ResourceStreamingPool and VoxelTexturesStreamingPool are backend allocator capacities, not selected quality settings."},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
    }
}
