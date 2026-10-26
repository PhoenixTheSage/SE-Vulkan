using System;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;
public sealed class VulkanProbes
{
    public MyEnvironmentProbeData Settings=MyEnvironmentProbeData.Default;
    public int Resolution=256;
    private bool reset=true,haveOrigin;
    private MyTextureDebugMultipliers textureMultipliers=MyTextureDebugMultipliers.Defaults;
    private byte[] heldFrames; private float[] heldMatrices;private int captureSkip=-1;
    private Vector3D origin;private float nextCapture,captureStarted;private ulong completedCycle;private int captureResolution;
    public float LastAmbient {get;private set;}
    public void Process(MyRenderMessageBase message){if(message is MyRenderMessageUpdateRenderEnvironment env){textureMultipliers=env.Data.TextureMultipliers;if(!Settings.Equals(env.Data.EnvironmentProbe) || Resolution!=env.Data.EnvMapResolution)reset=true;Settings=env.Data.EnvironmentProbe;Resolution=env.Data.EnvMapResolution;}if(message.MessageType==MyRenderMessageEnum.UnloadData){reset=true;haveOrigin=false;textureMultipliers=MyTextureDebugMultipliers.Defaults;}}
    public static MatrixD FaceView(int face){Vector3D[] directions={Vector3D.Left,Vector3D.Right,Vector3D.Up,Vector3D.Down,Vector3D.Backward,Vector3D.Forward};return MatrixD.CreateLookAt(Vector3D.Zero,directions[face],face==2?-Vector3D.Backward:face==3?-Vector3D.Forward:Vector3D.Up);}
    // Side-effect-free copy of Configure's hold/reset/origin decision: the flags and face projections it
    // will publish this frame, for preparing the native pass scene before LightingSetup.
    public (uint Flags,float[] Projections) Plan(NativeBackend backend,Vector3D camera,int skipIBLevels,float time)
    {
        int resolution=Resolution;bool r=reset||captureSkip!=Math.Max(0,skipIBLevels)||captureResolution!=resolution;
        var stats=backend.GetProbeStats();var schedule=backend.GetProbeScheduleStats();bool idle=schedule.Busy==0||r||Settings.TimeOut<=0;
        float next=stats.Cycles!=completedCycle?captureStarted+Math.Max(0,Settings.TimeOut):nextCapture;
        bool hold=Settings.TimeOut>0 && !r && haveOrigin && stats.Ready!=0 && idle && time<next;
        float[] matrices;
        if(hold && heldMatrices!=null)matrices=heldMatrices;
        else{var projection=(MatrixD)Matrix.CreatePerspectiveFovRhInfiniteComplementary((float)Math.PI/2,1,.01f);matrices=new float[96];var at=hold?origin:camera;
            for(int face=0;face<6;face++){var view=FaceView(face);view.Translation=Vector3D.TransformNormal(camera-at,view);Array.Copy(NativeBackend.MatrixValues(view*projection),0,matrices,face*16,16);}}
        return (1u|(r?2u:0)|(hold?4u:0)|(Settings.TimeOut<=0?16u:8u),matrices);
    }
    public void Configure(NativeBackend backend,Vector3D camera,MyEnvironmentLightData light,MyPostprocessSettings postprocess,MatrixD orientation,ulong brdf,float delta,float time,Func<float> gatherAmbient=null,MyRenderSettings? renderSettings=null)
    {
        int resolution=Resolution;if(resolution<16 || resolution>512 || (resolution&(resolution-1))!=0)throw new ArgumentException("Probe resolution must be a power of two from 16 through 512.");
        int skipLevels=Math.Max(0,light.SkipIBLevels);if(captureSkip!=skipLevels){reset=true;captureSkip=skipLevels;}
        if(captureResolution!=resolution){reset=true;captureResolution=resolution;}
        // Stock UpdateCullQuery retains timed capture/publication history on
        // camera movement, including teleports. AddProbe uses the current
        // camera for each face; movement does not request AddAllProbes.
        NativeProbeStats stats;NativeProbeScheduleStats schedule;
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ProbeNative))stats=backend.GetProbeScheduleStatsForConfigure(out schedule);bool idle=schedule.Busy==0||reset||Settings.TimeOut<=0;
        if(stats.Cycles!=completedCycle){completedCycle=stats.Cycles;nextCapture=captureStarted+Math.Max(0,Settings.TimeOut);}
        bool hold=Settings.TimeOut>0 && !reset && haveOrigin && stats.Ready!=0 && idle && time<nextCapture;
        if(reset || !haveOrigin){origin=camera;haveOrigin=true;nextCapture=time;completedCycle=0;}
        // Stock AddProbe uses the current camera for each captured face,
        // including movement during a staged refresh.
        if(!hold)origin=camera;
        if(!hold&&idle){captureStarted=time;nextCapture=time+Math.Max(0,Settings.TimeOut);using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ProbeAmbient))LastAmbient=gatherAmbient?.Invoke()??0;}
        light.AmbientForwardPass+=LastAmbient;
        byte[] frames; float[] matrices;
        if(hold && heldFrames!=null){frames=heldFrames;matrices=heldMatrices;}
        else using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ProbeFaceFrames))
        {
            var projection=(MatrixD)Matrix.CreatePerspectiveFovRhInfiniteComplementary((float)Math.PI/2,1,.01f);frames=new byte[7680];matrices=new float[96];
            for(int face=0;face<6;face++){var view=FaceView(face);view.Translation=Vector3D.TransformNormal(camera-origin,view);byte[] frame=WorldConstants.Frame(backend.WorldFrameLayout,view,projection,resolution,resolution,light,postprocess.Data.BloomEmissiveness,orientation,time:time,delta:delta,postprocess:postprocess,textureMultipliers:textureMultipliers,renderSettings:renderSettings);Buffer.BlockCopy(frame,0,frames,face*1280,1280);Array.Copy(NativeBackend.MatrixValues(view*projection),0,matrices,face*16,16);}
            heldFrames=frames;heldMatrices=matrices;
        }
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ProbeNative))backend.SetProbesForConfigure(new NativeProbeConfig{Size=8096,AbiVersion=NativeBackend.AbiVersion,Resolution=(uint)resolution,Flags=1u|(reset?2u:0)|(hold?4u:0)|(Settings.TimeOut<=0?16u:8u),Brdf=brdf,Blend=Settings.TimeOut<=0?1:1/Math.Max(10f,1f/delta*Settings.TimeOut),Frames=frames,Projections=matrices},(uint)skipLevels);reset=false;
    }
}
