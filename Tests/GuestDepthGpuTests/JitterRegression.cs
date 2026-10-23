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
    // Stage-7 temporal jitter owner: raster jittered, motion/velocity and published transforms unjittered, cuts restart.
    static int JitterRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.jitter-velocity";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        var stages=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);stages.GetField("PreparationBound",stat).SetValue(null,true);
        stages.GetField("catalog",stat).SetValue(null,backend.PostprocessCatalog().Where(row=>(uint)row[0]==4096||(uint)row[0]==4097).ToArray());stages.GetField("catalogEpoch",stat).SetValue(null,Property("Backend.GuestBridgeClient","ResourceEpoch"));
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.UploadWorldMesh(947001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2,2,1,0});backend.UploadTexture(947002,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(947003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(947004,1,1,0,new byte[]{255,1,0,0},false);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        float[] published=null;float[] unjittered=null;
        (byte[] G,float[] M,float[] J) Frame(int step){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.AddWorldPacket(0,947001,947002,947003,947004,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(.1*step,0,0),Vector3.One,Vector3.Zero,1,0),null,1,512|16384,0);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"jitter frame step="+step);var jitter=backend.GetJitter();
            Call("Backend.GuestFrame","Capture");published=new[]{(float)Call("Backend.GuestFrame","F32",24),(float)Call("Backend.GuestFrame","F32",28)};
            var m=(Matrix)Call("Backend.GuestFrame","MatrixAt",136);unjittered=new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
            Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);backend.EndFrame();
            return(backend.ReadbackGbufferColor(64,64),backend.ReadbackMotion(64,64),jitter);
        }
        // Four steps establish matching draw history; moving=false keeps the triangle still.
        (byte[] G,float[] M,float[] J) Run(bool moving=true){(byte[],float[],float[]) last=default;for(int step=0;step<4;++step)last=Frame(moving?step:0);return last;}
        Vector2 Analytic(){Vector4D Clip(double x){var p=Vector4D.Transform(new Vector4D(x,0,-3,1),projection);return p/p.W;}var curr=Clip(.3);var prev=Clip(.2);return new Vector2((float)((prev.X-curr.X)*.5*64),(float)((curr.Y-prev.Y)*.5*64));}
        float Halton(int i,int b){float f=1,r=0;while(i>0){f/=b;r+=f*(i%b);i/=b;}return r;}
        int centre=(32*64+32)*2,corner=(2*64+2)*2;var expected=Analytic();
        bool Near(float[] m,Vector2 v,float t)=>Math.Abs(m[centre]-v.X)<=t&&Math.Abs(m[centre+1]-v.Y)<=t;
        string root=Path.Combine(output,"JitterPack-"+Guid.NewGuid().ToString("N")),file=Path.Combine(root,"Overlay/Geometry/Passes/GBuffer/PixelStage.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\",\"exclusive\":[\"GBuffer\"]}");File.WriteAllText(file,VelocityGBufferPixelStage(core,bin));
        try{
            backend.ForceJitter(false);var baseline=Run();var baseStatic=Run(false);var baseUnjittered=(float[])unjittered.Clone();
            Check(baseline.J[4]==0&&baseline.J[0]==0&&baseline.J[1]==0,"jitter is inactive without a temporal upscaler");
            Check(Near(baseline.M,expected,.02f)&&baseStatic.M[centre]==0&&baseStatic.M[centre+1]==0,"unjittered baseline: analytic motion when moving, zero when still");
            backend.ForceJitter(true);var sequence=new List<float[]>();for(int i=0;i<9;++i)sequence.Add(Frame(0).J);
            Check(sequence[0][4]==1&&sequence[0][5]==8,"forced owner is active with FSR's 8-phase sequence at 1x scale");
            Check(Enumerable.Range(0,9).All(i=>Math.Abs(sequence[i][0]-(Halton(i%8+1,2)-.5f))<1e-6&&Math.Abs(sequence[i][1]-(Halton(i%8+1,3)-.5f))<1e-6),"jitter follows Halton(2,3) minus 0.5 and wraps after 8 phases");
            Check(Enumerable.Range(0,9).All(i=>Math.Abs(sequence[i][2]-2*sequence[i][0]/64)<1e-6&&Math.Abs(sequence[i][3]+2*sequence[i][1]/64)<1e-6),"clip offsets are 2*px/width and -2*py/height");
            var still=Run(false);Console.WriteLine("JITTER still motion="+still.M[centre]+","+still.M[centre+1]+" jitter="+still.J[0]+","+still.J[1]);
            Check(Math.Abs(still.M[centre])<1e-3&&Math.Abs(still.M[centre+1])<1e-3&&still.M[corner]==0,"a still scene keeps zero motion while the raster jitters (replay cancels the jitter)");
            Check(!still.G.SequenceEqual(baseStatic.G),"jittered raster moves the GBuffer coverage at triangle edges");
            Check(Math.Abs(published[0]-still.J[0])<1e-6&&Math.Abs(published[1]-still.J[1])<1e-6,"guest frame publishes the native pixel jitter");
            Check(unjittered.Zip(baseUnjittered,(a,b)=>Math.Abs(a-b)<1e-6).All(v=>v),"published UnjitteredViewProj is unchanged by jitter");
            var moving=Run();Console.WriteLine("JITTER moving motion="+moving.M[centre]+","+moving.M[centre+1]+" analytic="+expected);
            Check(Near(moving.M,expected,.02f)&&moving.M[corner]==0,"replayed motion stays analytic (unjittered) while jittered");
            Call("Backend.GuestPassList","RegisterRoot",owner,root,null);var owned=Run();Console.WriteLine("JITTER owner motion="+owned.M[centre]+","+owned.M[centre+1]+" error="+Property("Backend.GuestPostprocessStages","Error"));
            Check(Property("Backend.GuestPostprocessStages","Error")==null&&backend.GetPostprocessGeneration(4097).Executions>0&&Near(owned.M,expected,.02f),"velocity owner motion stays analytic: AnomalyUnjitteredViewProj is unjittered");
            var ownedStill=Run(false);Check(Math.Abs(ownedStill.M[centre])<1e-3&&Math.Abs(ownedStill.M[centre+1])<1e-3,"velocity owner reports zero motion for a still scene while jittered");
            Call("Backend.GuestPassList","UnregisterRoot",owner);
            Frame(0);Frame(0);typeof(NativeBackend).GetMethod("InvalidateGuestHistory",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(backend,null);var cut=Frame(0).J;
            Check(Math.Abs(cut[0]-(Halton(1,2)-.5f))<1e-6&&Math.Abs(cut[1]-(Halton(1,3)-.5f))<1e-6,"a camera cut (invalidated history) restarts the sequence");
            backend.ForceJitter(false);var restored=Run();var restoredStill=Run(false);
            Check(restored.M.SequenceEqual(baseline.M)&&restored.G.SequenceEqual(baseline.G)&&restoredStill.G.SequenceEqual(baseStatic.G),"disabling the owner restores exact unjittered motion and GBuffer bits");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"jitter owner raises zero core/synchronization errors");
        }finally{backend.ForceJitter(false);Call("Backend.GuestPassList","UnregisterRoot",owner);stages.GetField("catalog",stat).SetValue(null,null);stages.GetField("catalogEpoch",stat).SetValue(null,-1L);}
        return checks;
    }
}
