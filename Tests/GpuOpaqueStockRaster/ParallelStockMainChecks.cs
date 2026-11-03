using System.Reflection;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRage.Import;

partial class Program
{
    static string ParallelMainLane=>Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_PARALLEL_MAIN")=="0"?"serial":"parallel";
    static string ParallelMainOutput=>Path.GetFullPath("TestResults/GpuOpaqueStockRaster/ParallelMain");
    static string ParallelMainTiming=>Environment.GetEnvironmentVariable("SE_VULKAN_RECORD_TIMING");
    static void PrepareParallelMainAudit()
    {
        Directory.CreateDirectory(ParallelMainOutput);
        if(string.IsNullOrEmpty(ParallelMainTiming))throw new Exception("Launch --parallel-main-only with SE_VULKAN_RECORD_TIMING set before process start");
    }
    static void VerifyParallelMainAudit()
    {
        // Run returns after disposing the backend, which writes these counters.
        using var document=JsonDocument.Parse(File.ReadAllText(ParallelMainTiming));
        var root=document.RootElement;double frames=root.GetProperty("Frames").GetDouble();
        double Mean(string name){var row=root.GetProperty("Counters").EnumerateArray().Single(r=>r.GetProperty("Name").GetString()==name);return row.GetProperty("PerFrame").GetDouble()*frames/row.GetProperty("Samples").GetDouble();}
        if(Mean("new main entries")<800)throw new Exception("Mixed main fixture did not reach the parallel admission threshold");
        double workers=Mean("new main workers");
        if(ParallelMainLane=="parallel"?workers<3.99:workers!=0)throw new Exception("Mixed main fixture did not exercise requested recording lane: "+workers);
        Console.WriteLine("PASS actual mixed main entries="+Mean("new main entries")+" workers="+workers+" lane="+ParallelMainLane);
    }
    static unsafe void ParallelStockMainChecks(NativeBackend backend,Func<byte[]> draw,Action<bool,string> check,
        NativePassActor control,NativeGpuOpaqueActor input,byte[] placeholder,NativePassFrame frame,
        NativePassView[] views,NativeGpuOpaqueView[] gpuViews)
    {
        backend.ClearGpuScene();backend.ClearPassFrame();backend.SetWorldIndirect(true);
        backend.UploadTexture(14,1,1,0,new byte[]{20,90,230,0},true);
        var resident=typeof(NativeBackend).GetMethod("SetResidentMainOwner",BindingFlags.NonPublic|BindingFlags.Instance)!;
        uint normal=VF_Packer.PackNormal(Vector3.Backward);
        NativeWorldVertex V(float x,float y)=>new(){Material=new(){X=x,Y=y,Z=0,Scale=1,NormalX=normal&65535,NormalY=normal>>16,TangentX=1,TangentW=1},WeightX=1};
        NativePassPart Part(ulong mesh,ulong texture)=>new(){Mesh=mesh,Color=texture,Normal=11,Extensions=12,Count=3,Material=10,Flags=2};
        var managedKeys=new List<object>();
        ulong common=backend.RegisterGpuModel(new[]{Part(1,10)},false);
        int nativeCount=0;
        // One distinct family per owner gives >=800 stock entries. Most share a
        // merged run: its continuation crosses worker boundaries. At intervals,
        // native and managed owners interrupt it and must stay in list order.
        for(uint i=0;i<800;i++)
        {
            uint owner=20000+i;object key="parallel-stock-"+i;
            resident.Invoke(backend,new[]{(object)owner,key});
            var actor=new NativeGpuSceneActor{Family=backend.RegisterGpuFamily(Array.Empty<float>(),new[]{common}),Group=owner,SourceCount=1,Input=input,Selector=new(){Other=-1}};
            actor.Input.Owner=owner;actor.Input.Colour[3]=1;
            for(int b=0;b<128;b++)actor.Object[b]=placeholder[b];
            backend.RegisterGpuActor(ref actor,Array.Empty<NativeGpuOpaqueAppearance>());
            if(i%191==190)
            {
                ulong mesh=100+(uint)nativeCount;backend.UploadWorldMesh(mesh,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});
                ulong model=backend.RegisterPassModel(new[]{Part(mesh,14)}),family=backend.RegisterPassFamily(Array.Empty<float>());backend.BindPassLevel(family,0,model);
                var fallback=control;fallback.Handle=0;fallback.Owner=30000+(uint)nativeCount;fallback.Family=family;
                backend.RegisterPassActor(ref fallback,Array.Empty<NativePassAppearance>());
                resident.Invoke(backend,new object[]{fallback.Owner,"parallel-native-"+nativeCount});nativeCount++;
                object managed="parallel-managed-"+i;using(backend.WithMainGroup(managed)){}managedKeys.Add(managed);
            }
        }
        backend.PublishGpuScene(2,0,0);
        uint epoch=1000;
        for(int turn=0;turn<4;turn++)
        for(int slot=0;slot<2;slot++)
        {
            backend.BeginPacketCollection();
            foreach(var key in managedKeys)using(backend.WithMainGroup(key))backend.AddWorldPacket(0,1,14,11,12,0,
                WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(0,0,-5),Vector3.One,Vector3.Zero,1,0),flags:512,recycleConstants:false);
            backend.PreparePassFrame(ref frame,views,views.Length);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);
            var gpuFrame=new NativeGpuOpaqueFrame{Epoch=++epoch,Coefficient=1,DistanceMult=1,Flags=3,SmoothCameraDistance=100,MaximumSeconds=1,MinimumSeconds=.25f};
            backend.QueueGpuSceneFrame(ref gpuFrame,gpuViews);var image=draw();
            var products=new[]{image}.Concat(Enumerable.Range(0,4).Select(c=>backend.ReadbackWorldGbuffer((uint)c,64,64))).ToArray();
            for(int channel=0;channel<products.Length;channel++)
            {
                string serial=Path.Combine(ParallelMainOutput,$"serial-{turn}-{slot}-{channel}.bin");
                if(ParallelMainLane=="parallel")check(File.ReadAllBytes(serial).SequenceEqual(products[channel]),$"mixed parallel main matches serial channel {channel} turn {turn} slot {slot}");
                File.WriteAllBytes(Path.Combine(ParallelMainOutput,$"{ParallelMainLane}-{turn}-{slot}-{channel}.bin"),products[channel]);
            }
            check(backend.GetStats().ValidationErrors==0,"mixed main has no core/synchronization errors turn "+turn+" slot "+slot);
        }
        check(nativeCount==4&&managedKeys.Count==4,"mixed main includes four native and four managed fallback insertion points");
    }
}
