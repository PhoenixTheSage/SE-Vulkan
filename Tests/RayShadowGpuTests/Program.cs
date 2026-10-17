using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRage.Import;
using VRageMath;
using VRageRender;

static class Program
{
    [StructLayout(LayoutKind.Sequential)] struct Options { public uint Size,Abi,Enable,Shadows;public float Exposure,Bias,Solar;public uint Reserved; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetOptions(IntPtr ctx,ref Options options);
    static int Main(string[] args) {
        string bin=Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving+=(_,n)=>File.Exists(Path.Combine(bin,n.Name+".dll"))?AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin,n.Name+".dll")):null;
        return Run(bin);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string bin) {
        int checks=0;void Check(bool valid,string text){if(!valid)throw new Exception(text);++checks;Console.WriteLine("PASS "+text);}
        const int size=64;string output=Path.GetFullPath("TestResults/RayShadowGpuTests");Directory.CreateDirectory(output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(size,size,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));
        Check(backend.GetStats().ValidationEnabled!=0,"Khronos validation is active");
        Check(backend.GetCriticalRayState().RayQuery!=0,"hardware ray query is available");
        var ctx=(IntPtr)typeof(NativeBackend).GetField("context",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;
        var module=NativeLibrary.Load(NativeBackend.LibraryPath);var set=Marshal.GetDelegateForFunctionPointer<SetOptions>(NativeLibrary.GetExport(module,"sevk_set_ray_options"));
        NativeWorldVertex Vertex(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);Vector4 tangent=new(1,0,0,1);uint t=VF_Packer.PackTangentSign(ref tangent);return new(){Material=new(){X=x,Y=y,Z=0,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=(t&255)/255f,TangentY=((t>>8)&255)/255f,TangentZ=((t>>16)&255)/255f,TangentW=(t>>24)/255f,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        var vertices=new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(1,1),Vertex(-1,1)};uint[] indices={0,1,2,0,2,3};
        backend.BeginUploads();backend.UploadTexture(10,1,1,0,new byte[]{80,180,80,0},true);backend.UploadTexture(11,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(12,1,1,0,new byte[]{255,1,0,0},false);backend.UploadWorldMesh(1,vertices,indices);backend.UploadWorldMesh(2,vertices,indices);backend.EndUploads();
        MatrixD projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        byte[] Instance(MatrixD m){float[] rows={(float)m.M11,(float)m.M21,(float)m.M31,(float)m.M41,(float)m.M12,(float)m.M22,(float)m.M32,(float)m.M42,(float)m.M13,(float)m.M23,(float)m.M33,(float)m.M43,0,0,0,0};var bytes=new byte[64];Buffer.BlockCopy(rows,0,bytes,0,64);return bytes;}
        byte[] Render(bool rays,bool blocker,double yaw,bool generic=false,bool capture=true,uint repeats=1,bool shadows=true) {
            var options=new Options{Size=32,Abi=1,Enable=rays?1u:0u,Shadows=shadows?1u:0u,Exposure=1,Bias=.01f};Check(set(ctx,ref options)==0,"ray options accepted");
            var rotation=MatrixD.CreateRotationY(yaw);var view=MatrixD.Transpose(rotation);var toSun=new Vector3(.6f,0,.8f);var sun=-Vector3.TransformNormal(toSun,(Matrix)rotation);
            var light=MyEnvironmentLightData.Default;light.SunLightDirection=sun;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;light.SunSpecularFactor=0;light.SunDiscIntensity=0;light.ShadowFadeoutMultiplier=0;
            backend.BeginPacketCollection();
            void Draw(ulong mesh,MatrixD world){uint pipeline=generic?2u:0u,count=generic&&mesh==1?repeats:1;byte[] stream=null;if(generic){var one=Instance(world);stream=new byte[count*64];for(int i=0;i<count;++i)Buffer.BlockCopy(one,0,stream,i*64,64);}backend.AddWorldPacket(pipeline,mesh,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[pipeline],generic?MatrixD.Identity:world,Vector3.One,Vector3.Zero,0,0),stream,count);}
            Draw(1,MatrixD.CreateScale(3)*MatrixD.CreateTranslation(0,0,-6)*rotation);
            if(blocker)Draw(2,MatrixD.CreateScale(.6)*MatrixD.CreateTranslation(1.5,0,-4)*rotation);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,view,projection,size,size,light),NativeBackend.MatrixValues(view*projection),NativeBackend.MatrixValues(MatrixD.Invert(view*projection)));
            Check(backend.BeginFrame(view*projection,sun,new Vector3(2),Vector3.Zero,Color.Black),"ray fixture frame begins");backend.EndFrame();return capture?backend.Readback(size,size):Array.Empty<byte>();
        }
        var fallback=Render(false,false,0,true);var beforeRefusal=backend.GetCriticalRayState();
        Check(fallback.SequenceEqual(Render(true,false,0,true,repeats:8193)),"over-budget ray frames preserve stock pixels without an exposure or lighting jump");
        var refused=backend.GetCriticalRayState();Check(refused.Refused==beforeRefusal.Refused+1&&refused.Dispatches==beforeRefusal.Dispatches,"instance-budget refusal records no ray dispatch");
        var noRayShadow=Render(false,true,0);var beforeOff=backend.GetCriticalRayState();
        Check(noRayShadow.SequenceEqual(Render(true,true,0,shadows:false)),"disabled ray shadows preserve native output");
        Check(backend.GetCriticalRayState().Dispatches==beforeOff.Dispatches,"disabled ray shadows issue no dispatch");
        for(int i=0;i<6;++i)Render(true,true,0,true,capture:false);
        backend.Readback(size,size); // Complete the frames after exercising both pending slots.
        int Pixel(byte[] image,int x,int y)=>image[(y*size+x)*4]+image[(y*size+x)*4+1]+image[(y*size+x)*4+2];
        foreach(bool generic in new[]{false,true})foreach(int angle in new[]{0,90,180,270}){
            double yaw=angle*Math.PI/180;var native=Render(false,false,yaw,generic);var unblocked=Render(true,false,yaw,generic);
            Check(native.SequenceEqual(unblocked),$"unblocked rays preserve stock material/background pixels at yaw {angle}, generic={generic}");
            var reference=Render(false,true,yaw,generic);var shadow=Render(true,true,yaw,generic);File.WriteAllBytes(Path.Combine(output,$"shadow-{generic}-{angle}.rgba8"),shadow);
            File.WriteAllBytes(Path.Combine(output,$"reference-{generic}-{angle}.rgba8"),reference);Console.WriteLine($"Receiver native/ray: {Pixel(reference,32,32)}/{Pixel(shadow,32,32)}; dispatches={backend.GetCriticalRayState().Dispatches}");
            Check(Pixel(reference,32,32)>Pixel(shadow,32,32)+25,$"offset opaque blocker shadows the receiver at yaw {angle}, generic={generic}");
            Check(Pixel(reference,20,20)==Pixel(shadow,20,20),$"unshadowed receiver keeps its color at yaw {angle}, generic={generic}");
        }
        var state=backend.GetCriticalRayState();Check(state.Builds>=2&&state.Dispatches>=16,"multiple BLAS builds and both frame slots executed real ray queries");
        Check(backend.GetStats().ValidationErrors==0,"ray builds, scratch reuse and visibility consumers have zero core/synchronization errors");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,Ray=state,Validation=backend.GetStats().ValidationErrors},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));Console.WriteLine($"PASS {checks} ray shadow GPU checks");return 0;
    }
}
