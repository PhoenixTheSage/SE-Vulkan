using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
partial class Program
{
    // Voxel GBuffer owners: DX11 compiles voxel GBuffer permutations with the shared pass files and gives them
    // camera-only motion. Voxels are static, so per-draw currToPrev from camera-relative rows is that motion.
    static int VoxelOwnerRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.voxel-owner";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        var all=backend.PostprocessCatalog();
        Check(new uint[]{20,21,52,53}.All(p=>all.Any(r=>(uint)r[0]==4096+2*p)&&all.Any(r=>(uint)r[0]==4097+2*p)),"catalog lists voxel GBuffer (20, 21) and depth (52, 53) owner pairs");
        Check(all.Where(r=>(uint)r[0]>=4136&&(uint)r[0]<4140).All(r=>((string)r[1]).StartsWith("Geometry/Passes/GBuffer/"))&&all.Where(r=>(uint)r[0]>=4200&&(uint)r[0]<4204).All(r=>((string)r[1]).StartsWith("Geometry/Passes/Depth/")),"voxel owners are keyed by the shared GBuffer and Depth pass files");
        var stages=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);stages.GetField("PreparationBound",stat).SetValue(null,true);
        var catalog=all.Where(row=>(uint)row[0]==4136||(uint)row[0]==4137).ToArray();
        stages.GetField("catalog",stat).SetValue(null,catalog);stages.GetField("catalogEpoch",stat).SetValue(null,Property("Backend.GuestBridgeClient","ResourceEpoch"));
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        MyVertexFormatVoxelSingleData V(float x,float y,int weight=0)=>new(){Position=new Vector3(x,y,-3),Material=new Byte4{PackedValue=(uint)(0|1<<8|2<<16|weight<<24)},PackedNormal=new Byte4{PackedValue=VF_Packer.PackNormal(Vector3.Backward)}};
        byte[] constants=new byte[30720];void Floats(int at,params float[] f){foreach(float v in f){Buffer.BlockCopy(BitConverter.GetBytes(v),0,constants,at,4);at+=4;}}void Int(int at,int v)=>Buffer.BlockCopy(BitConverter.GetBytes(v),0,constants,at,4);
        for(int material=0;material<3;++material){int at=material*240;for(int t=0;t<2;++t)Floats(at+t*64,1,100,2,2,.1f,1000,1,1,.01f,5000,2,0,.001f,10000,3,1);Int(at+140,1);for(int level=0;level<3;++level)for(int role=0;role<6;++role)Int(at+144+32*level+role*4,material*6+level*2+(role%2));}
        byte[] cm=new byte[72],ng=new byte[72],ext=new byte[72];for(int i=0;i<18;++i){cm[i*4+i/6]=220;ng[i*4]=128;ng[i*4+1]=128;ng[i*4+2]=255;ng[i*4+3]=50;ext[i*4]=255;}
        backend.BeginUploads();backend.UploadVoxelArray(948100,1,1,0,true,18,1,cm);backend.UploadVoxelArray(948101,1,1,0,false,18,1,ng);backend.UploadVoxelArray(948102,1,1,0,false,18,1,ext);backend.SetVoxelMaterial(948103,constants,new ulong[]{948100,948101,948102});backend.UploadVoxelMesh(948104,new[]{V(-1,-1),V(1,-1,1),V(0,1,2)},new uint[]{0,1,2});backend.EndUploads();
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        uint frame=0;
        (byte[] G,float[] M) Frame(int step,bool pipelined=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.AddWorldPacket(20,948104,948103,0,0,0,VulkanVoxelMaterials.Object(backend.WorldLayouts[20],MatrixD.CreateTranslation(.1*step,0,0),Vector3D.Zero,0,0,null,default),null,1,512u);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"voxel owner frame step="+step+" pipelined="+pipelined);
            Call("Backend.GuestFrame","Capture");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return(backend.ReadbackGbufferColor(64,64),backend.ReadbackMotion(64,64));
        }
        (byte[] G,float[] M) Run(bool pipelined=false){(byte[],float[]) last=default;for(int step=0;step<4;++step)last=Frame(step,pipelined&&step==3);return last;}
        Vector2 Analytic(){Vector4D Clip(double x){var p=Vector4D.Transform(new Vector4D(x,0,-3,1),projection);return p/p.W;}var curr=Clip(.3);var prev=Clip(.2);return new Vector2((float)((prev.X-curr.X)*.5*64),(float)((curr.Y-prev.Y)*.5*64));}
        int centre=(32*64+32)*2,corner=(2*64+2)*2,colour=(32*64+32)*4;var expected=Analytic();
        bool Near(float[] m)=>Math.Abs(m[centre]-expected.X)<=.02f&&Math.Abs(m[centre+1]-expected.Y)<=.02f;
        bool Magenta(byte[] g)=>g[colour]==255&&g[colour+1]==0&&g[colour+2]==255;
        string root=Path.Combine(output,"VoxelOwnerPack-"+Guid.NewGuid().ToString("N")),file=Path.Combine(root,"Overlay/Geometry/Passes/GBuffer/PixelStage.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\",\"exclusive\":[\"GBuffer\"]}");
        string magenta=VelocityGBufferPixelStage(core,bin).Replace("ApplyMultipliers(material_output);","ApplyMultipliers(material_output);material_output.base_color=float3(1,0,1);");
        void Register(string source){File.WriteAllText(file,source);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        try{
            var stock=Run();Console.WriteLine("VOXEL stock motion="+stock.M[centre]+","+stock.M[centre+1]+" colour="+string.Join(",",stock.G.Skip(colour).Take(3)));
            Check(!Magenta(stock.G)&&stock.G.Skip(colour).Take(3).Any(v=>v!=0),"stock voxel surface renders its own material");
            Register(magenta);var owned=Run(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"velocity-aware voxel GBuffer owner prepares: "+Property("Backend.GuestPostprocessStages","Error"));
            Console.WriteLine("VOXEL analytic="+expected+" owned="+owned.M[centre]+","+owned.M[centre+1]);
            var state=backend.GetPostprocessGeneration(4137);Check(Magenta(owned.G)&&state.Active!=0&&state.Executions>0&&state.LastFrame==frame,"voxel owner pair executes the replacement material");
            Check(Near(owned.M)&&owned.M[corner]==0&&owned.M[corner+1]==0,"voxel owner writes camera-only motion (static geometry, camera-relative rows) matching the analytic value");
            Check(owned.M.SequenceEqual(Run().M)&&owned.G.SequenceEqual(Frame(3).G),"voxel owner motion and material agree across recording modes");
            Register(magenta.Replace("float2 anomaly_velocity = input.anomaly_velocity;","float2 anomaly_velocity = float2(3, -2);"));var custom=Run(true);
            Check(Property("Backend.GuestPostprocessStages","Error")==null&&custom.M[centre]==3&&custom.M[centre+1]==-2&&custom.M[corner]==0,"pack-written velocity reaches Target3 from the voxel owning invocation");
            Call("Backend.GuestPassList","UnregisterRoot",owner);var restored=Run(true);
            Check(restored.M.SequenceEqual(stock.M)&&restored.G.SequenceEqual(stock.G),"unregister restores exact stock voxel material and motion bits");
            Run();Check(backend.GetPostprocessGeneration(4137).Retired==0,"voxel owner bundles retire after completed submissions");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"voxel owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);stages.GetField("catalog",stat).SetValue(null,null);stages.GetField("catalogEpoch",stat).SetValue(null,-1L);}
        return checks;
    }
}
