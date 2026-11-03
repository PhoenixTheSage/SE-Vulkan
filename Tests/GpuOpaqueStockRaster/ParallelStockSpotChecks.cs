using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

partial class Program
{
    const int ParallelSpotStages=13,ParallelSpotFrames=ParallelSpotStages*2;
    static string ParallelSpotLane=>Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_PARALLEL_SPOTS")=="0"?"serial":"parallel";
    static string ParallelSpotOutput=>Path.GetFullPath("TestResults/GpuOpaqueStockRaster/ParallelSpots");
    static string ParallelSpotTiming=>Environment.GetEnvironmentVariable("SE_VULKAN_RECORD_TIMING");
    static void PrepareParallelSpotAudit()
    {
        Directory.CreateDirectory(ParallelSpotOutput);
        if(string.IsNullOrWhiteSpace(ParallelSpotTiming))throw new ArgumentException("Launch --parallel-spot-only with SE_VULKAN_RECORD_TIMING set before process start");
    }
    static void VerifyParallelSpotAudit()
    {
        using var document=JsonDocument.Parse(File.ReadAllText(ParallelSpotTiming));
        var root=document.RootElement;double frames=root.GetProperty("Frames").GetDouble();
        double Total(string name){foreach(var row in root.GetProperty("Counters").EnumerateArray())if(row.GetProperty("Name").GetString()==name)return row.GetProperty("PerFrame").GetDouble()*frames;return 0;}
        double workers=Total("GPU spot workers"),plans=Total("GPU spot plans"),expected=4*ParallelSpotFrames;
        if(ParallelSpotLane=="parallel"?(Math.Abs(workers-expected)>.01||Math.Abs(plans-expected)>.01):workers!=0)
            throw new Exception($"Wrong GPU spot admission: lane={ParallelSpotLane}, worker total={workers}, plan total={plans}, expected={expected}");
        double Mean(string name){var row=root.GetProperty("Counters").EnumerateArray().Single(r=>r.GetProperty("Name").GetString()==name);return row.GetProperty("PerFrame").GetDouble()*frames/row.GetProperty("Samples").GetDouble();}
        bool fused=Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_FUSED_RETAINED")!="0";
        if(Math.Abs(Mean("opaque compute dispatches")-(fused?8:10))>.01||Math.Abs(Mean("opaque fused retained")-(fused?1:0))>.001)throw new Exception("Conditional fixture did not exercise requested compute dispatch gate");
        Console.WriteLine($"PASS actual GPU spot admission: {ParallelSpotFrames} mixed frames, workers per positive frame={workers/ParallelSpotFrames}, plans per positive frame={plans/ParallelSpotFrames}, lane={ParallelSpotLane}");
        Console.WriteLine($"PASS actual opaque compute dispatches={Mean("opaque compute dispatches")} fused={fused}");
    }
    static unsafe void ParallelStockSpotChecks(NativeBackend backend,Func<byte[]> draw,Action<bool,string> check,
        NativePassActor control,NativeGpuOpaqueActor input,byte[] placeholder,NativePassFrame frame,MatrixD projection,MatrixD unusedShadow)
    {
        const int lightCount=4,resolution=256,viewCount=64;
        backend.ClearGpuScene();backend.ClearPassFrame();
        backend.UploadTexture(14,1,1,0,new byte[]{20,90,230,0},true);
        NativePassPart Part(ulong texture)=>new(){Mesh=1,Color=texture,Normal=11,Extensions=12,Count=3,Material=10,Flags=2};
        NativeGpuSceneActor GpuActor(uint id,bool dynamic,double x,double y)
        {
            ulong model=backend.RegisterGpuModel(new[]{Part(dynamic?14u:10u)},false);
            var actor=new NativeGpuSceneActor{Family=backend.RegisterGpuFamily(Array.Empty<float>(),new[]{model}),Group=id,SourceCount=1,Input=input,Selector=new(){Other=-1}};
            actor.Input.Owner=id;actor.Input.Flags=1u|16u|(dynamic?8u:0u);actor.Input.Colour[3]=1;
            actor.Input.Translation[0]=x;actor.Input.Translation[1]=y;
            actor.Input.Minimum[0]=x-1;actor.Input.Maximum[0]=x+1;actor.Input.Minimum[1]=y-1;actor.Input.Maximum[1]=y+1;
            for(int b=0;b<128;b++)actor.Object[b]=placeholder[b];
            actor.Handle=backend.RegisterGpuActor(ref actor,Array.Empty<NativeGpuOpaqueAppearance>());return actor;
        }
        NativePassActor CpuActor(uint id,bool dynamic,double x,double y)
        {
            ulong model=backend.RegisterPassModel(new[]{Part(14)}),family=backend.RegisterPassFamily(Array.Empty<float>());backend.BindPassLevel(family,0,model);
            var actor=control;actor.Handle=0;actor.Owner=id;actor.Group=id;actor.Family=family;actor.Flags=(actor.Flags&~64u)|(dynamic?64u:0u);
            actor.Translation[0]=x;actor.Translation[1]=y;actor.Bounds[0]=x-1;actor.Bounds[3]=x+1;actor.Bounds[1]=y-1;actor.Bounds[4]=y+1;
            actor.Handle=backend.RegisterPassActor(ref actor,Array.Empty<NativePassAppearance>());return actor;
        }
        var gpuStatic=GpuActor(40000,false,0,0);var gpuDynamic=GpuActor(40001,true,0,2);
        var nativeStatic=CpuActor(41000,false,-2.5,0);var nativeDynamic=CpuActor(41001,true,-2.5,2);
        double managedStaticX=2.5,managedDynamicY=2;
        byte[] Object(double x,double y)=>WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(x,y,-5),Vector3.One,Vector3.Zero,1,0);
        byte[] managedStatic=Object(managedStaticX,0),managedDynamic=Object(2.5,managedDynamicY);
        var lights=new NativeLight[lightCount];var spots=new NativeSpot[lightCount];
        var gpuViews=new NativeGpuOpaqueView[viewCount];gpuViews[0]=new(){Flags=1,Id=0};
        var passViews=new List<NativePassView>{new(){Id=0,Kind=0,Flags=1},new(){Id=1,Kind=1,Flags=1}};
        var main=NativeBackend.MatrixValues(projection);for(int i=0;i<16;i++)gpuViews[0].Projection[i]=main[i];
        for(int light=0;light<lightCount;light++)
        {
            var eye=new Vector3D((light-1.5)*2,0,10);var direction=Vector3D.Normalize(new Vector3D(0,0,-5)-eye);
            var matrix=MatrixD.CreateLookAt(eye,new Vector3D(0,0,-5),Vector3D.Up)*MatrixD.CreateOrthographic(16,16,1,100);
            lights[light]=new NativeLight{PositionRange=new[]{(float)eye.X,(float)eye.Y,(float)eye.Z,40f},ColorIntensity=new[]{1f,1f,1f,1f},DirectionCone=new[]{(float)direction.X,(float)direction.Y,(float)direction.Z,.5f},Flags=1};
            spots[light]=new NativeSpot{Light=(uint)light,Flags=1,Projection=NativeBackend.MatrixValues(matrix)};
            for(int role=0;role<2;role++)
            {
                int packed=1+2*light+role;uint id=(uint)(15+2*light+role);
                gpuViews[packed]=new NativeGpuOpaqueView{Flags=1u|2u|128u|(role==0?16u:8u),Id=id,Light=(uint)light};
                var values=NativeBackend.MatrixValues(matrix);for(int i=0;i<16;i++)gpuViews[packed].Projection[i]=values[i];
                passViews.Add(new NativePassView{Id=id,Kind=1,Flags=1u|(role==0?8u:4u),Light=(uint)light});
            }
        }
        // Combined static+dynamic conditional sources exercise the union's
        // in-place atomic publication. Put one at the 64-view boundary; leave
        // unrelated views disabled, with unique original IDs for view mapping.
        for(int light=0;light<lightCount;light++){
            int packed=light==3?63:9+light;gpuViews[packed]=new NativeGpuOpaqueView{Flags=1|2|8|16|128,Id=(uint)(light==3?63:23+light),Light=(uint)light};
            for(int i=0;i<16;i++)gpuViews[packed].Projection[i]=spots[light].Projection[i];}
        var usedIds=new HashSet<uint>(gpuViews.Where(v=>v.Flags!=0).Select(v=>v.Id));uint spare=1;
        for(int packed=12;packed<63;packed++){while(usedIds.Contains(spare))spare++;gpuViews[packed].Id=spare++;}
        gpuViews[62].Flags=2|16|128;gpuViews[62].Light=3; // Disabled static source, deliberately still in the union.
        backend.SetLights(lights);backend.SetSpots(resolution,spots);backend.PublishGpuScene(viewCount,0,0);
        var symbol=(IntPtr)typeof(NativeBackend).GetMethod("GetSymbol",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{"sevk_opaque_predicate_readback"})!;
        var context=(IntPtr)typeof(NativeBackend).GetField("context",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(backend)!;
        var read=(delegate* unmanaged[Cdecl]<IntPtr,uint*,uint,uint*,int>)symbol;
        uint[] ReadPredicates(){var values=new uint[viewCount];uint errors=0;fixed(uint* data=values)check(read(context,data,viewCount,&errors)==0,"read exact GPU conditional predicates");check(errors==0,"GPU predicate producer reports no overflow");return values;}
        void CommitGpu(NativeGpuSceneActor actor){backend.UpdateGpuActors(new[]{actor},Array.Empty<NativeGpuOpaqueAppearance>());}
        void CommitNative(NativePassActor actor){backend.UpdatePassActors(new[]{actor},1,Array.Empty<NativePassAppearance>(),0);}
        string[] labels={"cold","retained","gpu dynamic pose","native dynamic pose","managed dynamic pose","retained dynamic","gpu static pose","native static pose","managed static pose","retained static","dynamic hidden","dynamic restored","retained final"};
        byte[][] previousDepths=null;uint epoch=2000;
        for(int stage=0;stage<ParallelSpotStages;stage++)
        {
            bool hidden=stage==10;
            switch(stage)
            {
                case 2:gpuDynamic.Input.Translation[1]+=.4;gpuDynamic.Input.Minimum[1]+=.4;gpuDynamic.Input.Maximum[1]+=.4;gpuDynamic.Input.DepthRevision++;CommitGpu(gpuDynamic);break;
                case 3:nativeDynamic.Translation[2]+=.6;nativeDynamic.Bounds[2]+=.6;nativeDynamic.Bounds[5]+=.6;CommitNative(nativeDynamic);break;
                case 4:managedDynamicY+=.4;managedDynamic=Object(2.5,managedDynamicY);break;
                case 6:gpuStatic.Input.Translation[0]+=.4;gpuStatic.Input.Minimum[0]+=.4;gpuStatic.Input.Maximum[0]+=.4;gpuStatic.Input.DepthRevision++;CommitGpu(gpuStatic);break;
                case 7:nativeStatic.Translation[2]+=.6;nativeStatic.Bounds[2]+=.6;nativeStatic.Bounds[5]+=.6;CommitNative(nativeStatic);break;
                case 8:managedStaticX+=.4;managedStatic=Object(managedStaticX,0);break;
                case 10:gpuDynamic.Input.Flags&=~1u;nativeDynamic.Flags&=~1u;CommitGpu(gpuDynamic);CommitNative(nativeDynamic);break;
                case 11:gpuDynamic.Input.Flags|=1u;nativeDynamic.Flags|=1u;CommitGpu(gpuDynamic);CommitNative(nativeDynamic);break;
            }
            bool retained=stage==1||stage==5||stage==9||stage==12;
            bool staticChange=stage==0||stage>=6&&stage<=8;
            bool dynamicChange=stage==0||stage>=2&&stage<=4||stage==10||stage==11;
            for(int slot=0;slot<2;slot++)
            {
                backend.BeginPacketCollection();
                backend.AddWorldPacket(0,1,14,11,12,0,managedStatic,origin:new Vector3((float)managedStaticX,0,-5),radius:1.5f,recycleConstants:false);
                if(!hidden)backend.AddWorldPacket(0,1,14,11,12,0,managedDynamic,flags:0x1000000u,origin:new Vector3(2.5f,(float)managedDynamicY,-5),radius:1.5f,recycleConstants:false);
                backend.PreparePassFrame(ref frame,passViews.ToArray(),passViews.Count);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);
                backend.SetSpotRedraw(staticChange?15u:0u,dynamicChange?15u:0u);
                var gpuFrame=new NativeGpuOpaqueFrame{Epoch=++epoch,Coefficient=1,DistanceMult=1,Flags=3,SmoothCameraDistance=100,MaximumSeconds=1,MinimumSeconds=.25f};
                backend.QueueGpuSceneFrame(ref gpuFrame,gpuViews);var image=draw();var predicates=ReadPredicates();
                for(int light=0;light<lightCount;light++)
                {
                    check(predicates[1+2*light]==(staticChange?1u:0u),$"static predicate light {light} {labels[stage]} slot {slot}");
                    check(predicates[2+2*light]==(dynamicChange||staticChange?1u:0u),$"overlay predicate light {light} {labels[stage]} slot {slot}");
                    int combined=light==3?63:9+light;
                    check(predicates[combined]==(staticChange?1u:0u),$"combined static/dynamic predicate light {light} {labels[stage]} slot {slot}");
                }
                check(predicates[62]==(staticChange?1u:0u),"disabled static predicate retains legacy union semantics");
                var depths=Enumerable.Range(0,lightCount).Select(i=>backend.ReadbackSpot((uint)i,resolution)).ToArray();
                check(depths.All(d=>d.Any(v=>v<1)),"all four lights have independent nonempty caster coverage");
                var depthBytes=depths.Select(d=>MemoryMarshal.AsBytes(d.AsSpan()).ToArray()).ToArray();
                if(retained)check(previousDepths!=null&&depthBytes.Zip(previousDepths,(a,b)=>a.SequenceEqual(b)).All(x=>x),"false predicates retain every light exactly: "+labels[stage]);
                if(slot==0&&stage!=0&&!retained)check(previousDepths!=null&&depthBytes.Zip(previousDepths,(a,b)=>!a.SequenceEqual(b)).Any(x=>x),"isolated caster/visibility mutation changes real spotlight depth: "+labels[stage]);
                var products=new[]{image}.Concat(Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64))).Concat(depthBytes).ToArray();
                for(int channel=0;channel<products.Length;channel++)
                {
                    string baseline=Path.Combine(ParallelSpotOutput,$"serial-{stage}-{slot}-{channel}.bin");
                    if(ParallelSpotLane=="parallel")check(File.ReadAllBytes(baseline).SequenceEqual(products[channel]),$"parallel conditional spot matches serial stage {stage} slot {slot} product {channel}");
                    File.WriteAllBytes(Path.Combine(ParallelSpotOutput,$"{ParallelSpotLane}-{stage}-{slot}-{channel}.bin"),products[channel]);
                }
                File.WriteAllText(Path.Combine(ParallelSpotOutput,$"{ParallelSpotLane}-{stage}-{slot}-predicates.json"),JsonSerializer.Serialize(new{Stage=labels[stage],Slot=slot,Predicates=predicates}));
                previousDepths=depthBytes;
            }
        }
        var stats=backend.GetStats();check(stats.ValidationEnabled!=0&&stats.ValidationErrors==0,"four-light conditional fixture has validation enabled and zero core/synchronization errors");
    }
}
