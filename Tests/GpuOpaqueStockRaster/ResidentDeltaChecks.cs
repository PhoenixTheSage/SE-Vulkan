using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

partial class Program {
static unsafe void ResidentDeltaChecks(NativeBackend backend,Func<byte[]> draw,Action<bool,string> check,
    NativePassActor control,NativeGpuOpaqueActor input,byte[] placeholder,NativePassFrame frame,
    NativePassView[] views,NativeGpuOpaqueView[] gpuViews,VulkanShaderLibrary library){
    bool deltas=Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_RESIDENT_DELTAS")=="1";input.Colour[3]=1;
    backend.ClearGpuScene();backend.ClearPassFrame();
    backend.UploadTexture(14,1,1,0,new byte[]{210,35,70,0},true);
    NativePassPart Part(ulong texture)=>new(){Mesh=1,Color=texture,Normal=11,Extensions=12,First=0,Count=3,Material=10,Flags=2};
    byte[] Oracle(ulong texture,out float[] depth){
        ulong model=backend.RegisterPassModel(new[]{Part(texture)}),family=backend.RegisterPassFamily(Array.Empty<float>());backend.BindPassLevel(family,0,model);
        var actor=control;actor.Handle=0;actor.Owner=11000;actor.Family=family;actor.AppearanceCount=0;actor.Handle=backend.RegisterPassActor(ref actor,Array.Empty<NativePassAppearance>());
        backend.BeginPacketCollection();backend.PreparePassFrame(ref frame,views,views.Length);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);var image=draw();depth=backend.ReadbackShadow(0,256);
        backend.RemovePassResource(2,actor.Handle);backend.ClearPassFrame();return image;
    }
    var first=Oracle(10,out var firstDepth);var second=Oracle(14,out var secondDepth);check(!first.SequenceEqual(second),"resident delta fixture has distinct material images");
    ulong a=backend.RegisterGpuModel(new[]{Part(10)},false),b=backend.RegisterGpuModel(new[]{Part(14)},false);
    ulong fa=backend.RegisterGpuFamily(Array.Empty<float>(),new[]{a}),fb=backend.RegisterGpuFamily(Array.Empty<float>(),new[]{b});
    var actor=new NativeGpuSceneActor{Family=fa,Group=701,SourceCount=1,Input=input,Selector=new(){Other=-1}};actor.Input.Owner=11000;
    for(int i=0;i<128;i++)actor.Object[i]=placeholder[i];actor.Handle=backend.RegisterGpuActor(ref actor,Array.Empty<NativeGpuOpaqueAppearance>());
    var dormant=actor;dormant.Handle=0;dormant.Family=fb;dormant.Group=702;dormant.Input.Owner=11001;dormant.Input.Flags&=~1u;dormant.Handle=backend.RegisterGpuActor(ref dormant,Array.Empty<NativeGpuOpaqueAppearance>());
    backend.PublishGpuScene(2,0,0);uint epoch=12000;
    byte[] Frame(){backend.BeginPacketCollection();var f=new NativeGpuOpaqueFrame{Epoch=++epoch,Coefficient=1,DistanceMult=1,Flags=3};backend.QueueGpuSceneFrame(ref f,gpuViews);backend.PublishGpuVisibility(Array.Empty<NativePassQueryResult>());return draw();}
    check(Frame().SequenceEqual(first),"resident delta initial family matches native control");var publications=backend.GetGpuTopologyCounts()[5];
    for(int turn=0;turn<4;turn++){
        actor.Family=turn%2==0?fb:fa;backend.UpdateGpuActors(new[]{actor},Array.Empty<NativeGpuOpaqueAppearance>());
        var expected=turn%2==0?second:first;var depth=turn%2==0?secondDepth:firstDepth;
        for(int slot=0;slot<2;slot++){check(Frame().SequenceEqual(expected),"published family rebind preserves exact colour in slot "+slot);check(backend.ReadbackShadow(0,256).SequenceEqual(depth),"published family rebind preserves exact depth in slot "+slot);}
    }
    if(deltas)check(backend.GetGpuTopologyCounts()[5]==publications,"existing family rebinds retain publication and GPU buffers");
    actor.Group=702;backend.UpdateGpuActors(new[]{actor},Array.Empty<NativeGpuOpaqueAppearance>());Frame();var queries=backend.GetGpuQueries(gpuViews);
    check(queries.Length==1&&queries[0].Group==702,"root rebind updates sparse query ownership and removes empty root");
    backend.BeginPacketCollection();var hiddenFrame=new NativeGpuOpaqueFrame{Epoch=++epoch,Coefficient=1,DistanceMult=1,Flags=3};backend.QueueGpuSceneFrame(ref hiddenFrame,gpuViews);backend.PublishGpuVisibility(new[]{new NativePassQueryResult{Group=702,HiddenViews=1}});
    check(!draw().SequenceEqual(first),"new root visibility applies to rebound actor");check(backend.ReadbackShadow(0,256).SequenceEqual(firstDepth),"root rebind keeps main and caster visibility independent");
    check(Frame().SequenceEqual(first)&&Frame().SequenceEqual(first),"root rebind restores exact colour in both slots");
    if(deltas)check(backend.GetGpuTopologyCounts()[5]==publications,"root rebind retains immutable publication");
    var selector=backend.GetGpuSelector(actor.Handle);check(selector.Initialized!=0&&selector.Epoch==epoch,"resident rebind selector advances exactly once at current epoch");
    if(Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_OWNED_INPUTS")=="1"){
        backend.UploadTexture(15,1,1,0,new byte[]{30,190,75,0},true);var third=Oracle(15,out var thirdDepth);
        ulong c=backend.RegisterGpuModel(new[]{Part(15)},false),fc=backend.RegisterGpuFamily(Array.Empty<float>(),new[]{c});
        for(int turn=0;turn<4;turn++){actor.Family=turn%2==0?fc:fa;backend.UpdateGpuActors(new[]{actor},Array.Empty<NativeGpuOpaqueAppearance>());
            for(int slot=0;slot<2;slot++){check(Frame().SequenceEqual(turn%2==0?third:first),"exclusive new binding family matches native colour in slot "+slot);check(backend.ReadbackShadow(0,256).SequenceEqual(turn%2==0?thirdDepth:firstDepth),"exclusive binding retains depth in slot "+slot);}}
        check(backend.GetGpuTopologyCounts()[5]==publications,"exclusive texture-only families retain publication and selector buffers");
        dormant.Family=fa;backend.UpdateGpuActors(new[]{dormant},Array.Empty<NativeGpuOpaqueAppearance>());Frame();
        actor.Family=fc;backend.UpdateGpuActors(new[]{actor},Array.Empty<NativeGpuOpaqueAppearance>());
        check(Frame().SequenceEqual(third)&&Frame().SequenceEqual(third),"shared-family cold fallback preserves rebound colour in both slots");
        check(backend.GetGpuTopologyCounts()[5]==publications+1,"invisible shared owner prevents exclusive binding replacement");
        actor.Input.Flags&=~1u;dormant.Input.Flags|=1u;backend.UpdateGpuActors(new[]{actor,dormant},Array.Empty<NativeGpuOpaqueAppearance>());
        check(Frame().SequenceEqual(first)&&Frame().SequenceEqual(first),"shared family retains original texture when dormant owner becomes visible");
    }
    backend.ClearGpuScene();

    byte[] Source(float x){var bytes=new byte[64];BitConverter.GetBytes(1f).CopyTo(bytes,0);BitConverter.GetBytes(1f).CopyTo(bytes,20);BitConverter.GetBytes(1f).CopyTo(bytes,40);BitConverter.GetBytes(x).CopyTo(bytes,12);return bytes;}
    var sa=Source(0);var sb=Source(.5f);var constants=WorldConstants.Object(library.Load(WorldShaderVariants.Materials.Single(v=>v.Id==2).Vertex).Layout,MatrixD.CreateTranslation(0,0,-5),Vector3.One,Vector3.Zero,0,0);
    byte[] OldOracle(byte[] source,out float[] depth){backend.BeginPacketCollection();backend.AddWorldPacket(2,3,10,11,12,0,constants,source,1,0,1,new Vector3(0,0,-5),2,false);var image=draw();depth=backend.ReadbackShadow(0,256);return image;}
    var oldFirst=OldOracle(sa,out var oldFirstDepth);var oldSecond=OldOracle(sb,out var oldSecondDepth);check(!oldFirst.SequenceEqual(oldSecond),"source rebind fixture has distinct transforms");
    var oldPart=new NativePassPart{Mesh=3,Color=10,Normal=11,Extensions=12,First=0,Count=3,Material=10,Shader=2,Flags=2|8};
    var oldModel=backend.RegisterGpuModel(new[]{oldPart},true);var oldFamily=backend.RegisterGpuFamily(Array.Empty<float>(),new[]{oldModel});var sourceA=backend.RegisterGpuSource(sa,false);var sourceB=backend.RegisterGpuSource(sb,false);
    actor=new(){Family=oldFamily,Source=sourceA,SourceCount=1,Input=input,Selector=new(){Other=-1}};actor.Input.Owner=12000;actor.Input.Flags|=2;actor.Input.Inverse[0]=actor.Input.Inverse[5]=actor.Input.Inverse[10]=1;
    for(int i=0;i<128;i++)actor.Object[i]=constants[i];actor.Handle=backend.RegisterGpuActor(ref actor,Array.Empty<NativeGpuOpaqueAppearance>());
    dormant=actor;dormant.Handle=0;dormant.Input.Owner=12001;dormant.Input.Flags&=~1u;dormant.Source=sourceB;backend.RegisterGpuActor(ref dormant,Array.Empty<NativeGpuOpaqueAppearance>());backend.PublishGpuScene(2,0,0);
    check(Frame().SequenceEqual(oldFirst),"resident source initial image matches OLD control");publications=backend.GetGpuTopologyCounts()[5];
    for(int turn=0;turn<4;turn++){actor.Source=turn%2==0?sourceB:sourceA;backend.UpdateGpuActors(new[]{actor},Array.Empty<NativeGpuOpaqueAppearance>());for(int slot=0;slot<2;slot++){check(Frame().SequenceEqual(turn%2==0?oldSecond:oldFirst),"resident source rebind matches OLD colour in slot "+slot);check(backend.ReadbackShadow(0,256).SequenceEqual(turn%2==0?oldSecondDepth:oldFirstDepth),"resident source rebind matches OLD depth in slot "+slot);}}
    if(deltas)check(backend.GetGpuTopologyCounts()[5]==publications,"existing source rebind retains publication and GPU selectors");backend.ClearGpuScene();
}
}
