using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.FileSystem;
using VRage.Library.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

class Program {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int WorldPipelineFn(IntPtr context,uint id,byte[] vertex,ulong vertexBytes,byte[] pixel,ulong pixelBytes);
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,n)=>{string path=Path.Combine(bin,n.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};return Run(bin);}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string bin){
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        var segments=DepthGeometrySegments.Build(new[]{new DepthGeometryPart(0,3,MyMeshDrawTechnique.MESH),new DepthGeometryPart(3,6,MyMeshDrawTechnique.MESH),new DepthGeometryPart(9,3,MyMeshDrawTechnique.ALPHA_MASKED),new DepthGeometryPart(12,3,MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED),new DepthGeometryPart(15,3,MyMeshDrawTechnique.DECAL),new DepthGeometryPart(18,3,MyMeshDrawTechnique.GLASS),new DepthGeometryPart(21,3,MyMeshDrawTechnique.MESH),new DepthGeometryPart(24,3,MyMeshDrawTechnique.MESH,false),new DepthGeometryPart(27,3,MyMeshDrawTechnique.MESH)});
        Check(segments.Count==5 && segments[0].Count==9,"consecutive solids merge, alpha boundaries and skipped/hidden ranges remain separate");
        Check(segments[1].Alpha && segments[1].MaterialPart==2 && segments[2].Alpha && segments[2].MaterialPart==3,"each alpha segment keeps its own material and single-sided technique identity");
        Check(segments.Sum(s=>s.Count)==21,"dedicated depth coverage excludes only stock skipped techniques and explicit visibility");
        string output=Path.GetFullPath("TestResults/DepthGeometryGpuTests"),content=Path.GetFullPath(Path.Combine(bin,"../Content"));Directory.CreateDirectory(output);MyFileSystem.Init(content,output);ParallelTasks.Parallel.Scheduler=new ParallelTasks.FakeTaskScheduler();
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(content,"Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(content,"Shaders"));backend.SharedModelGeometryEnabled=true;backend.ModelBatchingEnabled=true;backend.BulkModelsEnabled=true;
        MatrixD projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;
        var shadow=MatrixD.CreateLookAt(new Vector3D(0,0,10),new Vector3D(0,0,-5),Vector3D.Up)*MatrixD.CreateOrthographic(20,20,1,100);
        backend.SetShadowQuality(256,0);backend.SetShadowCascades(NativeBackend.MatrixValues(shadow),new[]{100f});
        backend.SetShadowSchedule(true,1,1);
        void Publish(){backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"frame publication");backend.EndFrame();}
        NativeWorldVertex V(float x,float y,float z)=>new(){Material=new(){X=x,Y=y,Z=z,Scale=1,NormalX=65535,TangentX=1,TangentW=1,U=(x+1)/2,V=(y+1)/2},WeightX=1};
        backend.UploadWorldMesh(9000,new[]{V(-1,-1,-3),V(1,-1,-3),V(0,1,-3),V(-1,-1,-6),V(1,-1,-6),V(0,1,-6)},new uint[]{0,1,2,3,4,5});ulong uploaded=backend.GetStats().UploadedBytes;
        backend.RegisterWorldMeshRange(9001,9000,0,3);backend.RegisterWorldMeshRange(9002,9000,3,3);Check(backend.GetStats().UploadedBytes==uploaded,"color/depth range registration allocates no duplicate geometry upload");
        float[] Range(ulong mesh){backend.BeginPacketCollection();backend.AddWorldPacket(0,mesh,0,0,0,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),null,1,4096|16384,1);Publish();return backend.ReadbackShadow(0,256);}
        var first=Range(9001);Check(first.Any(f=>f<1),"shared depth range actually renders a silhouette");
        // Exercise the actual stock builder and installer, including the auxiliary
        // cascade/spot variants. A rejected candidate must leave covered pixels exact.
        var native=NativeLibrary.Load(NativeBackend.LibraryPath);
        try{
            var init=Marshal.GetDelegateForFunctionPointer<WorldPipelineFn>(NativeLibrary.GetExport(native,"sevk_init_world_pipeline"));
            var context=(IntPtr)typeof(NativeBackend).GetField("context",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;
            foreach(uint id in new uint[]{0,32}){
                var spec=WorldShaderVariants.Materials.Single(v=>v.Id==id);
                byte[] vertex=SpirvRuntimeModule.FromArtifact(library.Load(spec.Vertex)),pixel=SpirvRuntimeModule.FromArtifact(library.Load(spec.Pixel));
                Check(init(context,id,vertex,(ulong)vertex.Length,pixel,(ulong)pixel.Length)==0,"stock geometry owner installs its complete candidate bundle id"+id);
                Check(Range(9001).SequenceEqual(first),"rebuilt stock geometry preserves exact shadow coverage id"+id);
                byte[] wrongStage=vertex;
                Check(init(context,id,vertex,(ulong)vertex.Length,wrongStage,(ulong)wrongStage.Length)!=0,"wrong-stage candidate refuses before live installation id"+id);
                Check(Range(9001).SequenceEqual(first),"failed candidate preserves prior stock shadow variants id"+id);
            }
        }finally{NativeLibrary.Free(native);}

        backend.RemoveResource(3,9000);Check(Range(9001).SequenceEqual(first),"retiring source allocation leaves the registered view alive through GPU completion");
        backend.UploadWorldMesh(9000,new[]{V(-2,-2,-9),V(2,-2,-9),V(0,2,-9)},new uint[]{0,1,2});Check(Range(9001).SequenceEqual(first),"source replacement cannot change the retained range's geometry generation");
        backend.RemoveResource(3,9001);var second=Range(9002);Check(second.Any(f=>f<1)&&!second.SequenceEqual(first),"last surviving range retains its distinct index coverage");backend.RemoveResource(3,9002);backend.RemoveResource(3,9000);
        using var scene=new VulkanScene(backend);scene.ContentFailed+=(p,e)=>throw new Exception(p,e);scene.Process(new MyRenderMessageCreateRenderEntity{ID=10,Model=Path.Combine(content,"Models/Cubes/Large/Shelf_1.mwm"),DebugName="depth segment test",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        float[] Scene(bool dedicated){backend.RetainedSubmissionEnabled=dedicated;backend.BeginPacketCollection();scene.SetCamera(Vector3D.Zero,MyTimeSpan.Zero,1f/60);scene.Draw(backend);Publish();return backend.ReadbackShadow(0,256);}
        var reference=Scene(false);uint before=backend.GetPerformanceStats().ShadowCommands;Check(reference.Any(v=>v<1),"installed multisegment model casts real depth");
        var merged=Scene(true);Console.WriteLine(JsonSerializer.Serialize(new{Before=before,After=backend.GetPerformanceStats().ShadowCommands,CoverageDifferences=reference.Zip(merged).Count(p=>(p.First<1)!=(p.Second<1)),Changed=reference.Zip(merged).Count(p=>p.First!=p.Second),MaxDifference=reference.Zip(merged).Max(p=>Math.Abs(p.First-p.Second))}));
        Check(merged.SequenceEqual(reference),"merged solid depth preserves all reference silhouettes first frame slot");uint after=backend.GetPerformanceStats().ShadowCommands;
        Check(Scene(true).SequenceEqual(reference),"merged solid depth preserves all reference silhouettes second frame slot");Check(after<before,"dedicated geometry reduces actual native depth commands");
        backend.BeginLogicalSelectionCapture();Scene(false);var logicalReference=backend.EndLogicalSelectionCapture();backend.BeginLogicalSelectionCapture();Scene(true);var logicalDepth=backend.EndLogicalSelectionCapture();
        Check(logicalReference.Where(d=>(d.Flags&(4096|8192))==0).Select(d=>d.Mesh).SequenceEqual(logicalDepth.Where(d=>(d.Flags&(4096|8192))==0).Select(d=>d.Mesh)),"dedicated depth leaves main-view geometry coverage unchanged");
        var actorMap=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;object data=actorMap[(uint)10]!;object model=data.GetType().GetField("Model")!.GetValue(data)!;var parts=(System.Collections.IList)model.GetType().GetField("Parts")!.GetValue(model)!;string material=(string)parts[0]!.GetType().GetField("MaterialName")!.GetValue(parts[0])!;
        scene.Process(new MyRenderMessageUpdateModelProperties{ID=10,MaterialName=VRage.Utils.MyStringId.GetOrCompute(material),FlagsChange=new RenderFlagsChange{Remove=RenderFlags.Visible}});var hidden=Scene(false);Check(Scene(true).SequenceEqual(hidden),"per-material visibility breaks depth merges without reviving hidden triangles");
        scene.Process(new MyRenderMessageUpdateModelHighlight{ID=10,Thickness=2,OutlineColor=Color.Yellow,InstanceIndex=-1});var highlight=Scene(false);Check(Scene(true).SequenceEqual(highlight),"authored highlight consumer takes the explicit reference fallback");
        Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"shared geometry and depth replay have zero core/synchronization errors");File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,BeforeCommands=before,AfterCommands=after,ValidationEnabled=backend.GetStats().ValidationEnabled,SyncValidationRequested=Environment.GetEnvironmentVariable("VK_VALIDATION_VALIDATE_SYNC")=="1",ValidationErrors=backend.GetStats().ValidationErrors},new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
}
