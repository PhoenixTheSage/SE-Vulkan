using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.Import;
using VRageMath;
using VRageRender;
partial class Program
{
    // DX11's GBuffer velocity developer probes, driven through Anomaly's Config.VelocityProbe on a classic owner.
    static int VelocityProbeRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.velocity-probe";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        var config=core.GetType("ClientPlugin.Config",true).GetField("Current",stat).GetValue(null);var probeProperty=config.GetType().GetProperty("VelocityProbe");
        void Probe(int mode)=>probeProperty.SetValue(config,Enum.ToObject(probeProperty.PropertyType,mode));
        var stages=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);stages.GetField("PreparationBound",stat).SetValue(null,true);
        var catalog=backend.PostprocessCatalog().Where(row=>(uint)row[0]==4096||(uint)row[0]==4097).ToArray();Check(catalog.Length==2,"fixture selects the rigid classic GBuffer owner pair");
        stages.GetField("catalog",stat).SetValue(null,catalog);stages.GetField("catalogEpoch",stat).SetValue(null,core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetProperty("ResourceEpoch",stat).GetValue(null));
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.UploadWorldMesh(950001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2,2,1,0});backend.UploadTexture(950002,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(950003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(950004,1,1,0,new byte[]{255,1,0,0},false);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        float[] Frame(int step,bool pipelined=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.AddWorldPacket(0,950001,950002,950003,950004,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(.1*step,0,0),Vector3.One,Vector3.Zero,1,0),null,1,512|16384,0);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"probe frame step="+step+" pipelined="+pipelined);
            Call("Backend.GuestFrame","Capture");Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return backend.ReadbackMotion(64,64);
        }
        float[] Run(bool pipelined=false){float[] last=null;for(int step=0;step<4;++step)last=Frame(step,pipelined&&step==3);return last;}
        int centre=(32*64+32)*2,corner=(2*64+2)*2;
        string At(float[] m,int i)=>m[i]+","+m[i+1];
        bool Is(float[] m,int i,float x,float y)=>Math.Abs(m[i]-x)<.002f&&Math.Abs(m[i+1]-y)<.002f;
        string root=Path.Combine(output,"VelocityProbePack-"+Guid.NewGuid().ToString("N")),file=Path.Combine(root,"Overlay/Geometry/Passes/GBuffer/PixelStage.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\",\"exclusive\":[\"GBuffer\"]}");File.WriteAllText(file,VelocityGBufferPixelStage(core,bin));
        try{
            Probe(0);var replay=Run();Console.WriteLine("PROBE replay-only off centre="+At(replay,centre));
            Probe(1);var replayClear=Run(true);Console.WriteLine("PROBE replay-only TargetClear centre="+At(replayClear,centre)+" corner="+At(replayClear,corner));
            Check(Is(replayClear,corner,8,0)&&Is(replayClear,centre,replay[centre],replay[centre+1]),"TargetClear on the replay-only path: uncovered pixels hold +X, replayed geometry overwrites it");
            Probe(0);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);var off=Run(true);Console.WriteLine("PROBE owned off centre="+At(off,centre));
            Check(off[centre]<-1&&off[centre]>-1.2&&Is(off,corner,0,0),"owner emits analytic motion with probes off");
            Probe(1);var clear=Run(true);Console.WriteLine("PROBE TargetClear centre="+At(clear,centre)+" corner="+At(clear,corner));
            Check(Is(clear,corner,8,0)&&Is(clear,centre,off[centre],off[centre+1]),"TargetClear: Target3 clears to +X and the owner overwrites it");
            Probe(2);var mrt=Run(true);Console.WriteLine("PROBE MrtWrite centre="+At(mrt,centre)+" corner="+At(mrt,corner));
            Check(Is(mrt,centre,8,0)&&Is(mrt,corner,-8,0),"MrtWrite: the owning invocation emits +X over the -X sentinel");
            Check(mrt.SequenceEqual(Run()),"MrtWrite agrees across recording modes");
            Probe(3);var coverage=Run(true);Console.WriteLine("PROBE HistoryCoverage centre="+At(coverage,centre)+" corner="+At(coverage,corner));
            Check(Is(coverage,centre,8,0)&&Is(coverage,corner,0,0),"HistoryCoverage: a previous-world hit reports +X");
            {Probe(3);Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);var first=Frame(0,true);Console.WriteLine("PROBE HistoryCoverage first frame centre="+At(first,centre));
                Check(Is(first,centre,0,8)||Is(first,centre,8,0),"HistoryCoverage reports +Y for a miss or +X for a hit, never motion");}
            Probe(4);var end=Run(true);Console.WriteLine("PROBE PassEndClear centre="+At(end,centre)+" corner="+At(end,corner));
            Check(Is(end,centre,8,0)&&Is(end,corner,8,0),"PassEndClear: Target3 holds +X after every owner and replay draw");
            Probe(0);Check(Run(true).SequenceEqual(off),"probe off restores exact owner motion bits");
            Call("Backend.GuestPassList","UnregisterRoot",owner);Check(Run(true).SequenceEqual(replay),"unregister with probes off restores exact replay motion");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"velocity probes raise zero core/synchronization errors");
        }finally{Probe(0);Call("Backend.RenderHost","PrepareShaderGenerations");Call("Backend.GuestPassList","UnregisterRoot",owner);stages.GetField("catalog",stat).SetValue(null,null);stages.GetField("catalogEpoch",stat).SetValue(null,-1L);}
        return checks;
    }
}
