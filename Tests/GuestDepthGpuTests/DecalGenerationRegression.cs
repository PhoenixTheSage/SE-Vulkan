using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;
using VRage.Import;

partial class Program
{
    static int DecalGenerationRegression(Assembly core,string assets,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light,VulkanShaderLibrary library)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.decals";
        int checks=0;var failures=new List<string>();void Check(bool ok,string text){checks++;Console.WriteLine((ok?"PASS ":"FAIL ")+text);if(!ok)failures.Add(text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-2,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(y+1)/2}};}
        backend.UploadWorldMesh(954001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,2,1});backend.UploadTexture(954002,1,1,0,new byte[]{255,255,255,255},false);backend.UploadTexture(954003,1,1,0,new byte[]{128,128,255,100},false);
        NativeDecalDraw Draw(uint role,float fade=1){MatrixD w=MatrixD.CreateScale(2,2,1)*MatrixD.CreateTranslation(0,0,-2);var c=new byte[128];Buffer.BlockCopy(new[]{(float)w.M11,(float)w.M21,(float)w.M31,(float)w.M41,(float)w.M12,(float)w.M22,(float)w.M32,(float)w.M42,(float)w.M13,(float)w.M23,(float)w.M33,(float)w.M43},0,c,0,48);BitConverter.GetBytes(fade).CopyTo(c,48);Buffer.BlockCopy(NativeBackend.MatrixValues(MatrixD.Invert(w)),0,c,64,64);return new(){Size=176,Role=role,Alpha=954002,Color=954002,Normal=954003,Extensions=954002,Constants=c};}
        uint frame=0;
        (byte[][] G,float[] H) Frame(uint role,bool pipelined=false,float fade=1){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();backend.DecalPackets.Add(Draw(role,fade));
            if(role==8){var m=new byte[96];/* Zero-alpha glass contributes depth layers without altering the OIT color oracle. */backend.AddGlassPacket(0,954001,954002,954003,WorldConstants.Object(backend.GlassLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),m);}
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"decal owner frame begins role="+role+" pipelined="+pipelined);Check(Call("Backend.GuestFrame","Capture") is true,"decal owner captures immutable frame metadata");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
            var h=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)programType.GetField("Id").GetValue(p)=="anomaly.owned.HistoryCopy");var bytes=new byte[64*64*8];typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(backend,new object[]{(ulong)programType.GetField("StorageImage").GetValue(h),bytes});return (Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray(),Enumerable.Range(0,bytes.Length/2).Select(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*2))).ToArray());
        }
        string root=Path.Combine(output,"DecalPack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Overlay/Decals"));File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\"}");string file=Path.Combine(root,"Overlay/Decals/Decals.hlsl");
        const string shader="""
Texture2D<float4> VertexInput:register(t7);Texture2D<float4> PixelInput:register(t8);SamplerState FirstPoint:register(s1);
cbuffer Extras:register(b6){float2 RenderSize;}
cbuffer DecalData:register(b2){float4 Data[4096];}
struct Vertex{float4 p:SV_Position;};
Vertex __vertex_shader(uint id:SV_VertexID){float2 xy=id%8==1?float2(0,.6):id%8==2?float2(.6,-.6):float2(-.6,-.6);Vertex o;o.p=float4(xy+float2(VertexInput.SampleLevel(FirstPoint,float2(.5,.5),0).r+Data[3].x-1,0)*RenderSize.x/64,1,1);return o;}
#ifdef RENDER_TO_TRANSPARENT
struct Out{float4 a:SV_Target0;float4 b:SV_Target1;};
void __pixel_shader(Vertex v,out Out o){o.a=float4(.25,.5,PixelInput.Load(int3(0,0,0)).b,1);o.b=1;}
#else
struct Out{float4 a:SV_Target0;float4 b:SV_Target1;float4 c:SV_Target2;};
void __pixel_shader(Vertex v,out Out o){o.a=float4(.25,.5,PixelInput.Load(int3(0,0,0)).b,1);o.b=float4(.5,.25,1,1);o.c=float4(.125,.25,.5,1);}
#endif
""";
        void Register(string source){File.WriteAllText(file,source);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        void Clear(int slot,float r,float g,float b)=>Call("Buffers.CatalogImages","ClearColorTarget",owner+".t"+slot,r,g,b,0f);
        int at=(32*64+32)*4;
        void Opaque(uint role,byte[][] data,byte[][] stock){
            Console.WriteLine("decal role"+role+" center="+string.Join(",",data[0].Skip(at).Take(4))+" normal="+BitConverter.ToUInt32(data[1],at)+" material="+string.Join(",",data[2].Skip(at).Take(4)));
            if((role&1)!=0)Check(Math.Abs(data[0][at]-137)<=1&&Math.Abs(data[0][at+1]-188)<=1&&data[0][at+2]==0,"role"+role+" writes independently expected sRGB color");else Check(data[0].SequenceEqual(stock[0]),"normal-only decal preserves the entire color target");
            if((role&2)!=0){uint normal=BitConverter.ToUInt32(data[1],at);Check(Math.Abs((int)(normal&1023)-512)<=1&&Math.Abs((int)((normal>>10)&1023)-256)<=1&&((normal>>20)&1023)==1023,"role"+role+" writes independently expected packed normal");}else Check(data[1].SequenceEqual(stock[1]),"color-only decal preserves the entire normal target");
            Check(Enumerable.Range(0,3).All(k=>(role&(1u<<k))!=0?Math.Abs(data[2][at+k]-new[]{32,64,128}[k])<=1:data[2][at+k]==stock[2][at+k]),"role"+role+" preserves native per-channel material masks");Check(data[3].SequenceEqual(stock[3]),"decal replacement leaves depth unchanged role"+role);
        }
        try{
            Check((GuestBridge.SupportedPacketFlags&67108864)!=0,"native decal generations advertise bit26");var roles=new uint[]{1,2,3,7,8};var stock=roles.ToDictionary(r=>r,r=>Frame(r));
            foreach(int slot in new[]{7,8}){Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,owner+".t"+slot,1,1,10) is true,"decal catalog target allocated t"+slot);Call("Shaders.ShaderBindRegistry","RequestSrv","Decals",owner+".t"+slot,slot);}Clear(7,0,0,0);Clear(8,0,0,0);Register(shader);
            foreach(uint role in roles){var value=Frame(role,true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"decal full family prepares: "+Property("Backend.GuestPostprocessStages","Error"));if(role!=8)Opaque(role,value.G,stock[role].G);else {Console.WriteLine("decal HDR="+string.Join(",",value.H.Skip(at).Take(4)));Check(Math.Abs(value.H[at]-.25)<.003&&Math.Abs(value.H[at+1]-.5)<.003&&Math.Abs(value.H[at+2])<.003,"transparent decal writes expected OIT-composed HDR");}var synchronous=Frame(role);Check(synchronous.G.Zip(value.G,(a,b)=>a.SequenceEqual(b)).All(v=>v)&&synchronous.H.SequenceEqual(value.H),"synchronous and pipelined decal generation outputs match exactly role"+role);uint id=(uint)(1025+2*Array.IndexOf(roles,role));var state=backend.GetPostprocessGeneration(id);Check(state.Active!=0&&state.Executions>0&&state.LastFrame==frame&&state.LastCompletion<=state.Completed&&backend.GetPostprocessGeneration(id-1).Active==state.Active,"actual decal VS/PS pair activates and completes role"+role);}
            ulong token=backend.GetPostprocessGeneration(1025).Active;Check(Enumerable.Range(1024,10).All(i=>backend.GetPostprocessGeneration((uint)i).Active==token),"all five captured decal pairs activate atomically");
            Clear(8,0,0,1);var blue=Frame(1);Check(blue.G[0][at+2]==255,"pixel-only decal catalog resource changes native GBuffer output");Clear(7,.75f,0,0);var moved=Frame(1,true);Check(moved.G[0][at+2]==stock[1].G[0][at+2]&&moved.G[0][at+4*20+2]==255,"vertex-only catalog resource shifts actual decal raster coverage");Clear(7,0,0,0);
            var dynamic=Frame(1,fade:.25f);Check(dynamic.G[0][at+2]==stock[1].G[0][at+2]&&dynamic.G[0][at-4*20+2]==255,"native dynamic decal b2 constants affect the same invocation");
            Register(shader.Replace("o.a=float4(","o.a=Broken("));Frame(1);Check(Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(1025).Active==token,"compile failure retains every valid decal pair");
            try{backend.PreparePostprocessGeneration(new Dictionary<uint,VulkanShaderArtifact>{{1024,library.Load(TransparencyShaderVariants.Decals[0].Vertex)}},"fixture.incomplete.decal");throw new Exception("incomplete decal pair accepted");}catch(InvalidOperationException error){Check(error.Message.Contains("incomplete"),"native packet refuses an incomplete decal pair");}
            Call("Backend.GuestPassList","UnregisterRoot",owner);foreach(uint role in roles){var restored=Frame(role,true);Check(restored.G.Zip(stock[role].G,(a,b)=>a.SequenceEqual(b)).All(v=>v)&&restored.H.SequenceEqual(stock[role].H),"unregister restores exact stock decal output role"+role);}Frame(1);Check(backend.GetPostprocessGeneration(1025).Retired==0,"decal pipelines/layouts retire after last completed submission");Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"decal owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Buffers.BufferCatalog","UnpublishAll",owner);}
        if(failures.Count!=0)throw new Exception("Decal contract failures: "+string.Join("; ",failures));
        return checks;
    }
}
