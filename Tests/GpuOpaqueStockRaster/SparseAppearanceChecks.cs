using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

partial class Program
{
static unsafe void SparseAppearanceChecks(NativeBackend backend,Func<byte[]> draw,Action<bool,string> check,
    NativePassActor cpuTemplate,NativeGpuOpaqueActor gpuTemplate,byte[] placeholder,NativePassFrame cpuFrame,
    NativePassView[] cpuViews,NativeGpuOpaqueView[] gpuViews)
{
    backend.ClearGpuScene();backend.ClearPassFrame();backend.BeginPacketCollection();
    const int count=80;var vertices=new List<NativeWorldVertex>();var indices=new List<uint>();
    for(int i=0;i<count;i++){float x=-2+(i%10)*.4f,y=-1.6f+(i/10)*.4f;uint normal=VRage.Import.VF_Packer.PackNormal(Vector3.Backward);
        foreach(var point in new[]{new Vector2(x,y),new Vector2(x+.35f,y),new Vector2(x+.17f,y+.35f)}){
            indices.Add((uint)vertices.Count);vertices.Add(new(){Material=new(){X=point.X,Y=point.Y,Z=0,Scale=1,NormalX=normal&65535,NormalY=normal>>16,TangentX=1,TangentW=1},WeightX=1});}}
    backend.UploadWorldMesh(1000,vertices.ToArray(),indices.ToArray());
    NativePassPart[] Parts(uint basis)=>Enumerable.Range(0,count).Select(i=>new NativePassPart{Mesh=1000,Color=10,Normal=11,Extensions=12,First=(uint)i*3,Count=3,Material=basis+(uint)i,Flags=2}).ToArray();
    var near=Parts(10000);var far=Parts(20000);ulong cpuFamily=backend.RegisterPassFamily(new[]{10f});
    backend.BindPassLevel(cpuFamily,0,backend.RegisterPassModel(near));backend.BindPassLevel(cpuFamily,1,backend.RegisterPassModel(far));
    ulong gpuFamily=backend.RegisterGpuFamily(new[]{10f},new[]{backend.RegisterGpuModel(near,false),backend.RegisterGpuModel(far,false)});
    var input=gpuTemplate;input.Owner=800;input.Colour[3]=1;input.Minimum[0]=input.Minimum[1]=-3;input.Maximum[0]=input.Maximum[1]=3;
    var resident=new NativeGpuSceneActor{Family=gpuFamily,SourceCount=1,Input=input,Selector=new(){Other=-1}};
    for(int i=0;i<128;i++)resident.Object[i]=placeholder[i];resident.Handle=backend.RegisterGpuActor(ref resident,Array.Empty<NativeGpuOpaqueAppearance>());
    NativeGpuOpaqueAppearance Entry(uint id,bool visible){var e=new NativeGpuOpaqueAppearance{Material=id,Visible=visible?1u:0u};e.Colour[0]=e.Colour[1]=0x3c003c00;for(int c=0;c<4;c++)e.OldColour[c]=1;return e;}
    NativeGpuOpaqueAppearance[] Entries(int mode){
        if(mode==3)return Array.Empty<NativeGpuOpaqueAppearance>();
        var entries=Enumerable.Range(0,count*2).Select(i=>Entry(i<count?10000u+(uint)i:20000u+(uint)(i-count),true)).Reverse().ToList();
        // Specific entries must win over a hidden default even when supplied in reverse order.
        entries.Add(Entry(uint.MaxValue,mode!=2));
        if(mode==1){var hidden=entries.FindIndex(e=>e.Material==20037);entries[hidden]=Entry(20037,false);}
        return entries.ToArray();
    }
    foreach(var scenario in new[]{(Lod:0,Mode:0),(Lod:1,Mode:1),(Lod:1,Mode:2),(Lod:1,Mode:3),(Lod:0,Mode:0)}){
        var appearances=Entries(scenario.Mode);backend.ClearGpuScene();backend.BeginPacketCollection();
        var cpu=cpuTemplate;cpu.Owner=800;cpu.Handle=0;cpu.Family=cpuFamily;cpu.Lod=new(){Previous=-1};cpu.Bounds[0]=cpu.Bounds[1]=-3;cpu.Bounds[3]=cpu.Bounds[4]=3;
        var cpuAppearance=new NativePassAppearance[appearances.Length];for(int i=0;i<appearances.Length;i++){
            var a=appearances[i];var value=new NativePassAppearance{Material=a.Material,Visible=a.Visible};for(int c=0;c<4;c++)value.Color[c]=0x3c00;cpuAppearance[i]=value;}
        cpu.AppearanceOffset=0;cpu.AppearanceCount=(uint)cpuAppearance.Length;cpu.Handle=backend.RegisterPassActor(ref cpu,cpuAppearance);cpuFrame.Selection=scenario.Lod;
        backend.PreparePassFrame(ref cpuFrame,cpuViews,cpuViews.Length);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);
        var expected=draw();var expectedDepth=backend.ReadbackShadow(0,256);backend.RemovePassResource(2,cpu.Handle);backend.ClearPassFrame();
        // New handles preserve immutable model/family leases; clear retires registry handles.
        gpuFamily=backend.RegisterGpuFamily(new[]{10f},new[]{backend.RegisterGpuModel(near,false),backend.RegisterGpuModel(far,false)});
        resident.Handle=0;resident.Family=gpuFamily;resident.AppearanceFirst=0;resident.AppearanceCount=(uint)appearances.Length;resident.Selector=new(){Other=-1};resident.Handle=backend.RegisterGpuActor(ref resident,appearances);backend.PublishGpuScene(2,4096,2048);
        for(uint slot=0;slot<2;slot++){
            var frame=new NativeGpuOpaqueFrame{Epoch=1000+slot,Coefficient=1,DistanceMult=1,Flags=3,Selection=(uint)scenario.Lod,SmoothCameraDistance=100,MaximumSeconds=1,MinimumSeconds=.25f};
            backend.QueueGpuSceneFrame(ref frame,gpuViews);var actual=draw();if(!actual.SequenceEqual(expected)){
                Directory.CreateDirectory("TestResults/GpuOpaqueSparseDiagnostics");File.WriteAllBytes("TestResults/GpuOpaqueSparseDiagnostics/expected.rgba",expected);File.WriteAllBytes("TestResults/GpuOpaqueSparseDiagnostics/actual.rgba",actual);
                var selection=backend.GetGpuSelector(resident.Handle);Console.WriteLine("Sparse mismatch current="+selection.Current+" differences="+actual.Zip(expected,(a,b)=>a!=b).Count(x=>x)+" max="+actual.Zip(expected,(a,b)=>Math.Abs(a-b)).Max());}
            check(actual.SequenceEqual(expected),$"sparse >32 material colour/default precedence LOD {scenario.Lod} mode {scenario.Mode} slot {slot}");
            check(backend.ReadbackShadow(0,256).SequenceEqual(expectedDepth),$"sparse late-LOD material visibility and merged depth coverage mode {scenario.Mode} slot {slot}");
        }
        var update=resident;update.AppearanceCount=uint.MaxValue;update.Input.Translation[0]=.1;backend.UpdateGpuActors(new[]{update},Array.Empty<NativeGpuOpaqueAppearance>());
        update.Input.Translation[0]=0;backend.UpdateGpuActors(new[]{update},Array.Empty<NativeGpuOpaqueAppearance>());
        var unchanged=new NativeGpuOpaqueFrame{Epoch=1002,Coefficient=1,DistanceMult=1,Flags=3,Selection=(uint)scenario.Lod,SmoothCameraDistance=100};backend.QueueGpuSceneFrame(ref unchanged,gpuViews);check(draw().SequenceEqual(expected),"sparse pose-only roundtrip preserves colour/visibility tables");
        // Grow and then remove an existing resident table without topology publication.
        update=resident;update.AppearanceCount=0;backend.UpdateGpuActors(new[]{update},Array.Empty<NativeGpuOpaqueAppearance>());
        update.AppearanceCount=(uint)appearances.Length;backend.UpdateGpuActors(new[]{update},appearances);unchanged.Epoch=1003;backend.QueueGpuSceneFrame(ref unchanged,gpuViews);check(draw().SequenceEqual(expected),"sparse existing actor table shrink/growth restores exact image");
        backend.QueueGpuSceneFrame(ref unchanged,gpuViews);draw();check(backend.ReadbackShadow(0,256).SequenceEqual(expectedDepth),"sparse growth reaches both fenced slots without stale depth appearance");
    }
    backend.ClearGpuScene();backend.ClearPassFrame();
    var oldTint=new Vector3(.25f,.75f,1);backend.BeginPacketCollection();
    backend.AddWorldPacket(0,1,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(0,0,-5),oldTint,Vector3.Zero,1,0));
    var oldExpected=draw();var oldDepth=backend.ReadbackShadow(0,256);backend.BeginPacketCollection();
    ulong oldModel=backend.RegisterGpuModel(new[]{new NativePassPart{Mesh=1,Color=10,Normal=11,Extensions=12,First=0,Count=3,Material=30079,Flags=8}},true);
    var identity=new byte[64];BitConverter.GetBytes(1f).CopyTo(identity,0);BitConverter.GetBytes(1f).CopyTo(identity,20);BitConverter.GetBytes(1f).CopyTo(identity,40);
    var oldInput=gpuTemplate;oldInput.Owner=801;oldInput.Flags|=2;oldInput.Colour[3]=1;
    var oldResident=new NativeGpuSceneActor{Family=backend.RegisterGpuFamily(Array.Empty<float>(),new[]{oldModel}),Source=backend.RegisterGpuSource(identity,false),SourceCount=1,Input=oldInput,Selector=new(){Other=-1},AppearanceCount=80};
    for(int i=0;i<128;i++)oldResident.Object[i]=placeholder[i];
    var oldEntries=Enumerable.Range(0,80).Select(i=>Entry(30000+(uint)i,true)).ToArray();oldEntries[79].OldColour[0]=oldTint.X;oldEntries[79].OldColour[1]=oldTint.Y;oldEntries[79].OldColour[2]=oldTint.Z;
    oldResident.Handle=backend.RegisterGpuActor(ref oldResident,oldEntries);backend.PublishGpuScene(2,64,32);
    for(uint slot=0;slot<2;slot++){var oldFrame=new NativeGpuOpaqueFrame{Epoch=1200+slot,Coefficient=1,DistanceMult=1,Flags=3};backend.QueueGpuSceneFrame(ref oldFrame,gpuViews);
        check(draw().SequenceEqual(oldExpected),"OLD float object tint reads material beyond entry 32 in slot "+slot);check(backend.ReadbackShadow(0,256).SequenceEqual(oldDepth),"OLD sparse material preserves exact depth slot "+slot);}
    backend.ClearGpuScene();
}
}
