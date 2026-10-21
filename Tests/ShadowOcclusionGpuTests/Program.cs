using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.FileSystem;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using VRage.Library.Utils;
using System.Reflection;

class Program {
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,n)=>{string file=Path.Combine(bin,n.Name+".dll");return File.Exists(file)?AssemblyLoadContext.Default.LoadFromAssemblyPath(file):null;};return Run(bin);}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string bin){
        int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        string output=Path.GetFullPath("TestResults/ShadowOcclusionGpuTests"),content=Path.GetFullPath(Path.Combine(bin,"../Content"));Directory.CreateDirectory(output);MyFileSystem.Init(content,output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(content,"Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(content,"Shaders"));
        var projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        // The stock CSM comparison is less-or-equal: moving toward the sun
        // must decrease shadow depth. Exercise the actual default fitter.
        var defaultShadowType=typeof(NativeBackend).Assembly.GetType("ClientPlugin.Rendering.VulkanShadows",true)!;
        var shadowOwner=Activator.CreateInstance(defaultShadowType,true)!;
        Check((bool)defaultShadowType.GetProperty("StockSchedulingEnabled")!.GetValue(shadowOwner)! &&
            (bool)defaultShadowType.GetProperty("StockFittingEnabled")!.GetValue(shadowOwner)!,
            "production shadow defaults select stock scheduling and fitting");
        var render=new MyRenderSettings();render.EnableShadows=true;var user=render.User;user.ShadowQuality=MyShadowsQuality.HIGH;render.User=user;
        var sun=Vector3.Normalize(new Vector3(.3f,-.5f,-1));
        defaultShadowType.GetMethod("Update")!.Invoke(shadowOwner,new object[]{backend,MatrixD.Identity,sun,render,projection,Vector3D.Zero});
        var fitted=(MatrixD[])defaultShadowType.GetField("worldMatrices",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(shadowOwner)!;
        var point=new Vector3D(0,0,-10);var nearerSun=point-(Vector3D)sun;
        Check(Vector3D.Transform(nearerSun,fitted[0]).Z<Vector3D.Transform(point,fitted[0]).Z,"default cascade depth decreases toward the sun for stock less-or-equal comparison");
        defaultShadowType.GetProperty("StockFittingEnabled")!.SetValue(shadowOwner,false);
        defaultShadowType.GetProperty("StockSchedulingEnabled")!.SetValue(shadowOwner,false);
        defaultShadowType.GetMethod("Update")!.Invoke(shadowOwner,new object[]{backend,MatrixD.Identity,sun,render,projection,Vector3D.Zero});
        Check(Vector3D.Transform(nearerSun,fitted[0]).Z<Vector3D.Transform(point,fitted[0]).Z,"simplified fallback also decreases depth toward the sun");
        var front=MatrixD.CreateLookAt(new Vector3D(0,0,10),new Vector3D(0,0,-5),Vector3D.Up)*MatrixD.CreateOrthographic(20,20,1,100);
        var back=MatrixD.CreateLookAt(new Vector3D(0,0,-20),new Vector3D(0,0,-5),Vector3D.Up)*MatrixD.CreateOrthographic(20,20,1,100);
        backend.SetShadowQuality(256,0);backend.SetShadowCascades(NativeBackend.MatrixValues(front).Concat(NativeBackend.MatrixValues(back)).ToArray(),new[]{100f,200f});backend.SetShadowSchedule(true,3,3);
        NativeWorldVertex V(float x,float y)=>new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=65535,TangentX=1,TangentW=1},WeightX=1};
        backend.UploadWorldMesh(1,new[]{V(-5,-5),V(5,-5),V(5,5),V(-5,5)},new uint[]{0,1,2,2,3,0});
        var query=new[]{new NativePackedOcclusionDraw{Id=77,MinimumX=-.1f,MinimumY=-.1f,MinimumZ=-6.1f,ExtentX=.2f,ExtentY=.2f,ExtentZ=.2f}};
        ulong mainToken=0;Action prepare=null;void Publish(bool wall=true){backend.BeginPacketCollection();if(mainToken!=0)backend.QueueOcclusion(mainToken,query,1);if(wall)backend.AddWorldPacket(0,1,0,0,0,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),null,1,0,3);prepare?.Invoke();backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,MyEnvironmentLightData.Default),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"frame publication");backend.EndFrame();}
        Publish();var reference0=backend.ReadbackShadow(0,256);var reference1=backend.ReadbackShadow(1,256);
        Check(reference0.Any(x=>x<1)&&reference1.Any(x=>x<1),"opposing caster views both contain the wall");
        for(ulong token=1;token<=2;token++){
            backend.QueueShadowOcclusion(0,token,query,1);backend.QueueShadowOcclusion(1,token,query,1);mainToken=token;Publish();
            Check(backend.ReadbackShadow(0,256).SequenceEqual(reference0)&&backend.ReadbackShadow(1,256).SequenceEqual(reference1),"read-only queries preserve retained shadow depth in both frame slots");
            var a=backend.GetShadowOcclusion(0,out var at);var b=backend.GetShadowOcclusion(1,out var bt);var main=backend.GetOcclusion(out var mt);
            Console.WriteLine(JsonSerializer.Serialize(new{Expected=token,A=at,B=bt,Main=mt,ARows=a,BRows=b,MainRows=main},new JsonSerializerOptions{IncludeFields=true}));
            Check(at==token&&bt==token&&mt==token&&a.Length==1&&b.Length==1&&main.Length==1,"same owner identity has independent per-view tokens and results");
            Check(a[0].Visible==0&&b[0].Visible!=0&&main[0].Visible==0,"caster view occlusion is independent from opposite caster view and main visibility");
        }
        backend.SetShadowSchedule(true,1,3);backend.QueueShadowOcclusion(0,3,query,1);backend.QueueShadowOcclusion(1,3,query,1);Publish(false);backend.ReadbackShadow(0,256);
        Check(backend.GetShadowOcclusion(0,out var t0).Single().Visible!=0&&t0==3,"removing occluder makes the scheduled view visible");
        Check(backend.GetShadowOcclusion(1,out var t1).Single().Visible!=0&&t1==2,"unscheduled view does not execute or publish a new query");
        backend.SetShadowSchedule(true,3,3);Publish(false);backend.ReadbackShadow(1,256);
        Check(backend.GetShadowOcclusion(1,out t1).Single().Visible!=0&&t1==3,"delayed scheduled view publishes its own pending token");
        var bounds=new BoundingBoxD(new Vector3D(-1,-1,-9),new Vector3D(1,1,-7));var states=new[]{new StockGroupVisibility(),new StockGroupVisibility()};
        foreach(var state in states){state.Observe(1,bounds,Vector3D.Zero,.1f);state.Observe(2,bounds,Vector3D.Zero,.1f);}
        Check(states[0].Accept(states[0].Epoch,2,2,bounds.GetInflated(bounds.Size*.5),bounds,false)&&states[0].Observe(3,bounds,Vector3D.Zero,.1f)&&!states[1].Observe(3,bounds,Vector3D.Zero,.1f),"one cascade's accepted group result cannot hide another cascade");
        var epoch=states[0].Epoch;Check(!states[0].Observe(5,bounds,Vector3D.Zero,.1f)&&!states[0].Accept(epoch,2,5,bounds,bounds,false),"stock global-frame reentry rejects an old pending hidden result");
        ParallelTasks.Parallel.Scheduler=new ParallelTasks.FakeTaskScheduler();mainToken=0;
        // These checks cover the managed group bridge; native pass ownership (the
        // product default) would route its actors around it.
        using var scene=new VulkanScene(backend){StockShadowOcclusionEnabled=true,GroupOcclusionEnabled=true,NativePassSceneEnabled=false};scene.ContentFailed+=(path,error)=>throw new Exception(path,error);
        var shadowType=typeof(VulkanScene).Assembly.GetType("ClientPlugin.Rendering.VulkanShadows",true)!;object shadows=Activator.CreateInstance(shadowType,true)!;
        shadowType.GetProperty("StockSchedulingEnabled")!.SetValue(shadows,true);shadowType.GetProperty("StockFittingEnabled")!.SetValue(shadows,true);shadowType.GetProperty("UpdateMask")!.SetValue(shadows,3u);
        ((Vector3D[])shadowType.GetField("ViewOrigins")!.GetValue(shadows)!)[0]=new Vector3D(0,0,10);((Vector3D[])shadowType.GetField("ViewOrigins")!.GetValue(shadows)!)[1]=new Vector3D(0,0,-20);
        typeof(VulkanScene).GetField("ShadowSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(scene,shadows);typeof(VulkanScene).GetField("CascadeMaskFor",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(scene,(Func<BoundingBoxD,Vector3D,uint>)((_,_)=>3));
        scene.Process(new MyRenderMessageCreateManualCullObject{ID=100,DebugName="cascade query group",WorldMatrix=MatrixD.Identity});
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=10,Model=Path.Combine(content,"Models/Cubes/Large/ArmorCenter.mwm"),DebugName="grouped caster",WorldMatrix=MatrixD.CreateTranslation(0,0,-10),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        scene.Process(new MyRenderMessageSetParentCullObject{ID=10,CullObjectID=100,ChildToParent=Matrix.CreateTranslation(0,0,-10)});
        prepare=()=>{scene.SetCamera(Vector3D.Zero,MyTimeSpan.Zero,1f/60);scene.Draw(backend);};
        for(int i=0;i<8;i++){Publish();backend.ReadbackShadow(0,256);}
        backend.BeginLogicalSelectionCapture();Publish();backend.ReadbackShadow(0,256);var selected=backend.EndLogicalSelectionCapture().Where(x=>x.Owner==10).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(scene.GetShadowOcclusionStats()));
        Check(selected.Length>0&&selected.All(x=>(x.Cascades&1)==0)&&selected.Any(x=>(x.Cascades&2)!=0),"managed group bridge removes only the hidden cascade's caster membership");
        scene.RenderSettings.DisableShadowCascadeOcclusionQueries=true;backend.BeginLogicalSelectionCapture();Publish();backend.ReadbackShadow(0,256);selected=backend.EndLogicalSelectionCapture().Where(x=>x.Owner==10).ToArray();
        Check(selected.Any(x=>(x.Cascades&3)==3),"stock shadow-query debug override restores full caster selection");
        scene.RenderSettings.DisableShadowCascadeOcclusionQueries=false;scene.Process(new MyRenderMessageRemoveRenderObject{ID=100});Publish();backend.ReadbackShadow(0,256);
        scene.Clear();Publish(false);backend.ReadbackShadow(0,256);
        Check(backend.GetShadowOcclusion(0,out _).All(x=>x.Reserved==0),"destroyed group and scene teardown do not replay stale query owners");
        Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"zero core and synchronization validation errors");File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationEnabled=backend.GetStats().ValidationEnabled,SyncValidationRequested=Environment.GetEnvironmentVariable("VK_VALIDATION_VALIDATE_SYNC")=="1",ValidationErrors=backend.GetStats().ValidationErrors},new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
}
