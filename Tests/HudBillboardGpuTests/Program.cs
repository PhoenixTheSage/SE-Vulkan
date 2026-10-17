using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRage.FileSystem;
using VRage.Library.Utils;
using VRage.Utils;
using VRageRender;
class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NgxConfigure(uint enabled,uint mode,uint preset,[MarshalAs(UnmanagedType.LPUTF8Str)]string directory);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetOwner(IntPtr context,[MarshalAs(UnmanagedType.LPUTF8Str)]string owner);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NgxStatus(StringBuilder text,uint capacity);
    static int Main(string[] args)
    {
        string bin=Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving+=(_,n)=>{string path=Path.Combine(bin,n.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};
        return Run(bin,args.Length>1?args[1]:null);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static int Run(string bin,string featureDirectory)
    {
        string output=Path.GetFullPath("TestResults/HudBillboardGpuTests");Directory.CreateDirectory(output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        string device=NativeBackend.Probe().DeviceName;
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));
        backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));
        backend.BeginUploads();backend.UploadTexture(3,1,1,0,new byte[]{255,255,255,255},false);backend.EndUploads();
        MyFileSystem.Init(Path.GetFullPath(Path.Combine(bin,"../Content")),Path.Combine(output,"User"));
        ParallelTasks.Parallel.Scheduler=new ParallelTasks.FakeTaskScheduler();
        using var scene=new VulkanScene(backend);
        scene.TextureCache.BindOwnerThread();scene.SetCamera(Vector3D.Zero,MyTimeSpan.Zero,1f/60);
        scene.RenderSettings=new MyRenderSettings {DrawBillboards=true,DrawBillboardsPostPP=true};
        var material=new MyTransparentMaterial(MyStringId.GetOrCompute("HudGpuRegression"),MyTransparentMaterialTextureType.FileTexture,
            "Textures/GUI/Blank.dds",null,0,false,false,Vector4.One,Vector4.Zero,Vector4.One,Vector4.One,false,false);
        MyTransparentMaterials.AddMaterial(material);
        var shared=new MySharedData();
        // RHF uses camera NearPlaneDistance (0.05) for its pixel-to-world plane.
        MatrixD projection=Matrix.CreatePerspectiveFovRhInfiniteComplementary((float)Math.PI/2,1,.05f);
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunDiscIntensity=0;light.AmbientDiffuseFactor=0;light.SunDiffuseFactor=0;
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.Data.ChromaticFactor=0;post.Settings.Data.VignetteStart=0;
        byte[] Render(float? z,bool producer=false)
        {
            backend.BeginPacketCollection();
            if(producer)
            {
                shared.Billboards.Read.Billboards.Clear();
                if(z.HasValue)shared.Billboards.Read.Billboards.Add(new MyTriangleBillboard {Material=material.Id,BlendType=MyBillboard.BlendTypeEnum.PostPP,CustomViewProjection=-1,
                    Color=new Vector4(1,0,0,1),ColorIntensity=1,Position0=new(-.02,-.02,z.Value),Position1=new(.02,-.02,z.Value),Position2=new(0,.02,z.Value),UV0=Vector2.Zero,UV1=Vector2.UnitX,UV2=new(.5f,1)});
                backend.BeginUploads();scene.DrawBillboards(shared);backend.EndUploads();
            }
            else if(z.HasValue)
            {
                var data=new byte[48];Buffer.BlockCopy(new float[]{1,0,0,1},0,data,0,16);Buffer.BlockCopy(BitConverter.GetBytes(-1),0,data,16,4);
                // A MyTriangleBillboard occupies one quad slot; V3 repeats V2.
                float d=z.Value,s=.02f;
                backend.BillboardPackets.Add(new NativeBillboardDraw{Texture=3,Flags=8,Bucket=4,Data=data,Vertices=new[]{-s,-s,d,0,0,s,-s,d,1,0,0,s,d,.5f,1,0,s,d,.5f,1}});
            }
            var size=backend.ResolveRenderResolution(64,64);
            post.Configure(backend,size.X,size.Y,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size.X,size.Y,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            if(!backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black))throw new Exception("Frame did not begin");
            backend.RequestReadback();backend.EndFrame();return backend.Readback(64,64);
        }
        var baseline=Render(null);var results=new List<object>();bool passed=true;
        foreach(float z in new[]{-.06f,-.05f,-.049999f})
        {
            byte[] image=Render(z);int changed=Enumerable.Range(0,64*64).Count(i=>image[i*4]>baseline[i*4]+50);
            Console.WriteLine($"PostPP RHF plane z={z}: {changed} red pixels");results.Add(new{Z=z,ChangedPixels=changed});
            passed &= changed>200;
            File.WriteAllBytes(Path.Combine(output,$"plane-{z}.rgba"),image);
        }
        byte[] produced=Render(-.049999f,producer:true);
        int producerPixels=Enumerable.Range(0,64*64).Count(i=>produced[i*4]>baseline[i*4]+50);
        Console.WriteLine($"Production transient triangle/material/cache path: {producerPixels} red pixels");passed &= producerPixels>200;
        var ngxResults=new List<object>();
        if(featureDirectory!=null)
        {
            IntPtr module=NativeLibrary.Load(NativeBackend.LibraryPath);
            var configure=Marshal.GetDelegateForFunctionPointer<NgxConfigure>(NativeLibrary.GetExport(module,"sevk_ngx_configure"));
            var owner=Marshal.GetDelegateForFunctionPointer<SetOwner>(NativeLibrary.GetExport(module,"sevk_set_upscale_owner"));
            var status=Marshal.GetDelegateForFunctionPointer<NgxStatus>(NativeLibrary.GetExport(module,"sevk_ngx_status"));
            IntPtr context=(IntPtr)typeof(NativeBackend).GetField("context",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(backend);
            try
            {
                if(owner(context,"se-dlss")!=0)throw new Exception("NGX owner claim failed");
                foreach(uint mode in new[]{4u,0u})
                {
                    if(configure(1,mode,0,Path.GetFullPath(featureDirectory))!=0)throw new Exception("NGX configure failed");
                    Render(null,producer:true);var image=Render(-.049999f,producer:true);
                    var text=new StringBuilder(1024);status(text,1024);
                    int pixels=Enumerable.Range(0,64*64).Count(i=>image[i*4]>baseline[i*4]+50);
                    var closed=Render(null,producer:true);
                    int historyPixels=Enumerable.Range(0,64*64).Count(i=>closed[i*4]>baseline[i*4]+50);
                    Console.WriteLine($"NGX mode {mode}: {text}; HUD {pixels} red pixels; closed-menu history {historyPixels}");
                    passed &= text.ToString()=="NGX evaluating" && pixels==producerPixels && historyPixels==0;
                    ngxResults.Add(new{Mode=mode,Status=text.ToString(),HudPixels=pixels,ClosedHistoryPixels=historyPixels});
                }
            }
            finally{configure(0,4,0,null);owner(context,"");Render(null,producer:true);NativeLibrary.Free(module);}
        }
        var stats=backend.GetStats();Console.WriteLine($"Validation errors: {stats.ValidationErrors}");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Device=device,NativeLibrary=NativeBackend.LibraryPath,Planes=results,ProducerPixels=producerPixels,Ngx=ngxResults,stats.ValidationEnabled,stats.ValidationErrors},new JsonSerializerOptions{WriteIndented=true}));
        if(!passed || stats.ValidationEnabled==0 || stats.ValidationErrors!=0)throw new Exception("HUD near-plane rasterization or validation regression");
        return 0;
    }
}
