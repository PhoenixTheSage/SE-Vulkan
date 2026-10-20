using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage.FileSystem;
using VRageMath;
using VRageRender;
using VRage.Import;
using System.Reflection;
using ClientPlugin.Rendering.Scene;
using VRage.Library.Utils;
using VRage.Utils;
using VRageRender.Messages;

partial class Program {
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,n)=>{string file=Path.Combine(bin,n.Name+".dll");return File.Exists(file)?AssemblyLoadContext.Default.LoadFromAssemblyPath(file):null;};return Run(bin,args.Contains("--auxiliary-history-only")||args.Contains("--attachments-only"),args.Contains("--attachments-only"));}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static unsafe int Run(string bin,bool auxiliaryHistoryOnly,bool attachmentsOnly){
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        Check(Marshal.SizeOf<NativePassPart>()==64&&Marshal.SizeOf<NativePassActor>()==216&&Marshal.SizeOf<NativePassFrame>()==1960&&Marshal.SizeOf<NativePassView>()==240&&Marshal.SizeOf<NativePassFallback>()==40&&Marshal.SizeOf<NativePassProfile>()==136&&Marshal.SizeOf<NativePassTransparent>()==24,"resident pass ABI layouts");
        string output=Path.GetFullPath("TestResults/NativePassGpuTests"),content=Path.GetFullPath(Path.Combine(bin,"../Content"));Directory.CreateDirectory(output);MyFileSystem.Init(content,output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(content,"Shaders"),Path.Combine(bin,"VRage.Render11.dll"));using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(content,"Shaders"));backend.ConfigureNativePasses(library);
        Check(backend.NativePassReady,"all twenty-eight authenticated stock simple-stream and cutout pipelines initialize");
        backend.GetPassProfile(true);
        var projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var shadow=MatrixD.CreateLookAt(new Vector3D(0,0,10),new Vector3D(0,0,-5),Vector3D.Up)*MatrixD.CreateOrthographic(20,20,1,100);backend.SetShadowQuality(256,0);backend.SetShadowCascades(NativeBackend.MatrixValues(shadow),new[]{100f});backend.SetShadowSchedule(true,1,1);
        NativeWorldVertex V(float x,float y){uint normal=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=0,Scale=1,NormalX=normal&65535,NormalY=normal>>16,TangentX=1,TangentW=1},WeightX=1};}backend.UploadWorldMesh(1,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2,2,1,0});
        backend.UploadTexture(10,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(11,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(12,1,1,0,new byte[]{0,1,0,0},false);
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;
        if(auxiliaryHistoryOnly){NativePassAuxiliaryChecks(backend,projection,light,attachmentsOnly,Check);Console.WriteLine(checks+(attachmentsOnly?" native-pass attachment checks passed.":" native-pass auxiliary checks passed."));return 0;}
        ulong model=backend.RegisterPassModel(new[]{new NativePassPart{Mesh=1,Color=10,Normal=11,Extensions=12,First=0,Count=3,Material=10},new NativePassPart{Mesh=1,Color=10,Normal=11,Extensions=12,First=3,Count=3,Material=11}}),family=backend.RegisterPassFamily(new[]{10f});backend.BindPassLevel(family,0,model);
        var actor=new NativePassActor{Owner=10,Family=family,Group=7,Flags=1|8|16|32,Scale=1,Lod=new(){Previous=-1}};actor.Translation[2]=-5;actor.Bounds[0]=actor.Bounds[1]=-1;actor.Bounds[2]=-5;actor.Bounds[3]=actor.Bounds[4]=1;actor.Bounds[5]=-5;actor.Basis[0]=actor.Basis[4]=actor.Basis[8]=1;actor.Handle=backend.RegisterPassActor(ref actor,Array.Empty<NativePassAppearance>());
        var frame=new NativePassFrame{Coefficient=1,Delta=.1f,GlobalFlags=3,SmoothDistance=100};frame.Global[4]=.25f;frame.Global[6]=1;
        var views=new[]{new NativePassView{Id=0,Kind=0,Flags=1},new NativePassView{Id=1,Kind=1,Flags=1}};
        byte[] Draw(NativePassFrame input,bool hidden=false){backend.BeginCriticalFrame(8);backend.BeginPacketCollection();backend.PreparePassFrame(ref input,views,views.Length);var prep=backend.GetPassPreparation();Check(prep.Queries.Length==1&&prep.Queries[0].Group==7&&prep.Missing.Length==0,"preparation returns one required query identity and no actor replay slices");backend.PublishPassFrame(hidden?new[]{new NativePassQueryResult{Group=7,HiddenViews=2}}:Array.Empty<NativePassQueryResult>(),hidden?1:0);backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"native group frame begins");backend.RequestReadback();backend.EndFrame();return backend.Readback(64,64);}
        var colour=Draw(frame);var depth=backend.ReadbackShadow(0,256);Check(depth.Any(x=>x<1),"native depth groups render a real silhouette");Check(backend.GetPerformanceStats().MainCommands==2&&backend.GetPerformanceStats().ShadowCommands==1,"native groups record directly: two color ranges and one merged depth segment");
        var streamProfile=backend.GetPassProfile(false);Check(streamProfile.EmittedRows==2&&streamProfile.StreamBytes==112&&streamProfile.Groups==3,"real GPU color ranges share one uploaded instance row and merged depth uses one transform row");
        Check(Draw(frame).SequenceEqual(colour)&&backend.ReadbackShadow(0,256).SequenceEqual(depth),"stock streams and dedicated segments remain identical in both frame slots");
        Check(Draw(frame,true).SequenceEqual(colour)&&backend.ReadbackShadow(0,256).All(x=>x==1),"shadow query result removes only depth and preserves main color pixels");
        var materialAppearance=new NativePassAppearance{Material=11,Visible=1};materialAppearance.Color[0]=0x3800;materialAppearance.Color[1]=materialAppearance.Color[2]=0x3c00;materialAppearance.Color[3]=0x4000;actor.AppearanceCount=1;backend.UpdatePassActors(new[]{actor},1,new[]{materialAppearance},1);var tinted=Draw(frame);Check(!tinted.SequenceEqual(colour),"full appearance commit changes the real GPU material stream");
        actor.Flags|=128;actor.AppearanceCount=0;actor.Translation[0]=1;backend.UpdatePassActors(new[]{actor},1,Array.Empty<NativePassAppearance>(),0);Draw(frame);actor.Translation[0]=0;backend.UpdatePassActors(new[]{actor},1,Array.Empty<NativePassAppearance>(),0);Check(Draw(frame).SequenceEqual(tinted),"pose-only commits retain tint and emissivity through both GPU frame slots");
        actor.Flags&=~128u;backend.UpdatePassActors(new[]{actor},1,Array.Empty<NativePassAppearance>(),0);Check(Draw(frame).SequenceEqual(colour),"explicit empty appearance replacement restores defaults rather than retaining old overrides");
        actor.AppearanceCount=2;
        backend.UpdatePassActors(new[]{actor},1,new[]{new NativePassAppearance{Material=10,Visible=0},new NativePassAppearance{Material=11,Visible=0}},2);
        Draw(frame);Check(backend.GetPerformanceStats().MainCommands==0&&backend.GetPerformanceStats().ShadowCommands==0&&backend.ReadbackShadow(0,256).All(x=>x==1),"hidden material pieces remove preprocessed colour/depth coverage and clear scheduled depth");
        Draw(frame);Check(backend.ReadbackShadow(0,256).All(x=>x==1),"hidden preprocessed coverage remains clear in the other frame slot");
        actor.AppearanceCount=0;backend.UpdatePassActors(new[]{actor},1,Array.Empty<NativePassAppearance>(),0);
        Check(Draw(frame).SequenceEqual(colour)&&backend.ReadbackShadow(0,256).SequenceEqual(depth),"restored material visibility recovers original preprocessed colour and depth pixels");
        actor.Translation[0]=3;actor.Bounds[0]=2;actor.Bounds[3]=4;backend.UpdatePassActors(new[]{actor},1,Array.Empty<NativePassAppearance>(),0);Check(!Draw(frame).SequenceEqual(colour),"committed transform reaches native instance stream without managed draw emission");
        frame.GlobalFlags=2;actor.Translation[2]=-20;actor.Bounds[2]=actor.Bounds[5]=-20;backend.UpdatePassActors(new[]{actor},1,Array.Empty<NativePassAppearance>(),0);backend.PreparePassFrame(ref frame,views,views.Length);var request=backend.GetPassPreparation();Check(request.Missing.Length==1&&request.Missing[0].Family==family&&request.Missing[0].Lod==1,"native selector requests the missing authored LOD once");backend.BindPassLevel(family,1,model);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);Check(backend.GetPassPreparation().Fallbacks.Length==0,"lazy resolution publishes prepared selection without managed representation replay");
        backend.RemovePassResource(2,actor.Handle);bool stale=false;try{backend.UpdatePassActors(new[]{actor},1,Array.Empty<NativePassAppearance>(),0);}catch(InvalidOperationException){stale=true;}Check(stale,"retired actor handle cannot update a replacement generation");
        backend.ClearPassFrame();Check(backend.GetPassGroupAudit(out var retiredOwners).Length==0&&retiredOwners.Length==0,"explicit frame clear cannot replay a retired actor's native groups");
        var infinite=(MatrixD)Matrix.CreatePerspectiveFovRhInfiniteComplementary((float)Math.PI/2,1,.01f);
        var viewFactory=typeof(VulkanScene).GetMethod("PassView",BindingFlags.NonPublic|BindingFlags.Static)!;
        var infiniteView=(NativePassView)viewFactory.Invoke(null,new object[]{9u,2u,new BoundingFrustumD(infinite),new MyPassLoddingSetting(),1u,0u,0d,100d})!;
        bool finite=true;for(int i=0;i<24;i++)finite&=double.IsFinite(infiniteView.Planes[i]);Check(finite,"infinite complementary probe projection preserves finite unnormalized clip inequalities");
        backend.PreparePassFrame(ref frame,new[]{infiniteView},1);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);Check(backend.GetPassPreparation().Fallbacks.Length==0,"native view validation accepts the stock infinite projection");
        ParallelTasks.Parallel.Scheduler=new ParallelTasks.FakeTaskScheduler();backend.SharedModelGeometryEnabled=true;
        // The game assigns these stock defaults before drawing. Avoid the full
        // settings initializer here: its streaming budgets require MyVRage.Platform.
        var sceneSettings=new MyRenderSettings{DrawTransparentModels=true,DrawTransparentModelsInstanced=true,TransparentColorMultiplier=Vector4.One,TransparentReflectivityMultiplier=1,TransparentFresnelMultiplier=1,TransparentGlossMultiplier=2};
        using(var scene=new VulkanScene(backend){NativePassSceneEnabled=true,NewActorViewsEnabled=true,GroupOcclusionEnabled=true,RenderSettings=sceneSettings}){
            scene.ContentFailed+=(path,error)=>throw new Exception(path,error);
            var shadowType=typeof(VulkanScene).Assembly.GetType("ClientPlugin.Rendering.VulkanShadows",true)!;var shadows=Activator.CreateInstance(shadowType,true)!;
            shadowType.GetProperty("StockSchedulingEnabled")!.SetValue(shadows,true);shadowType.GetProperty("StockFittingEnabled")!.SetValue(shadows,true);
            typeof(VulkanScene).GetField("ShadowSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(scene,shadows);
            scene.Process(new MyRenderMessageCreateManualCullObject{ID=100,DebugName="resident group",WorldMatrix=MatrixD.Identity});
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=200,Model=Path.Combine(content,"Models/Cubes/Large/ArmorCenter.mwm"),DebugName="resident game actor",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
            scene.Process(new MyRenderMessageSetParentCullObject{ID=200,CullObjectID=100,ChildToParent=Matrix.CreateTranslation(0,0,-5)});
            void SceneFrame(){backend.BeginPacketCollection();scene.SetCamera(Vector3D.Zero,MyTimeSpan.Zero,1f/60);scene.Draw(backend);scene.PublishNativePassScene();backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"game scene publication");backend.RequestReadback();backend.EndFrame();}
            SceneFrame();var gameGroups=backend.GetPassGroupAudit(out var gameOwners);
            Console.WriteLine(JsonSerializer.Serialize(scene.GetNativePassSceneStats()));
            Check(gameGroups.Length>0&&gameOwners.Contains(200u)&&((System.Collections.ICollection)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!).Count==0,"game actor reaches direct native groups and bypasses managed color packets");
            SceneFrame();Check(backend.GetPassGroupAudit(out gameOwners).Length==gameGroups.Length,"resident game scene survives the other frame slot");
            var actorMap=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
            var rootData=actorMap[100u]!;var rootActor=(VRage.Render.Scene.MyActor)rootData.GetType().GetField("Actor")!.GetValue(rootData)!;
            var gameData=actorMap[200u]!;var gameModel=gameData.GetType().GetField("Model")!.GetValue(gameData)!;
            var gameParts=(System.Collections.IList)gameModel.GetType().GetField("Parts")!.GetValue(gameModel)!;
            string usedMaterial=(string)gameParts[0]!.GetType().GetField("MaterialName")!.GetValue(gameParts[0])!;
            backend.GetPassProfile(true);scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=200,MaterialName=MyStringId.GetOrCompute(usedMaterial),DiffuseColor=Color.Red,Emissivity=1});SceneFrame();var colouredGame=backend.Readback(64,64);var beforePose=backend.GetPassProfile(true);Check(beforePose.AppearanceEntries>0,"game appearance message commits replacement resident material data");
            var rootPose=MatrixD.CreateTranslation(.5,0,0);rootActor.SetMatrix(ref rootPose);SceneFrame();rootPose=MatrixD.Identity;rootActor.SetMatrix(ref rootPose);SceneFrame();var afterPose=backend.GetPassProfile(false);
            Check(afterPose.CommittedActors>beforePose.CommittedActors&&afterPose.AppearanceEntries==beforePose.AppearanceEntries,"game hierarchy pose notifications commit transforms without rebuilding resident appearances");
            Check(backend.Readback(64,64).SequenceEqual(colouredGame),"game pose roundtrip preserves authored appearance pixels");
            rootActor.SetVisibility(false);SceneFrame();Check(backend.GetPassGroupAudit(out gameOwners).Length==0,"committed parent visibility removes resident descendant geometry");
            rootActor.SetVisibility(true);SceneFrame();Check(backend.GetPassGroupAudit(out gameOwners).Length>0,"parent visibility restoration republishes native groups");
            scene.Process(new MyRenderMessageUpdateModelHighlight{ID=200,Thickness=2,OutlineColor=Color.Yellow,InstanceIndex=-1});SceneFrame();Check(backend.GetPassGroupAudit(out gameOwners).Length==0&&((System.Collections.ICollection)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!).Count>0,"authored highlight identity takes explicit managed fallback");
            scene.Process(new MyRenderMessageUpdateModelHighlight{ID=200,Thickness=0,InstanceIndex=-1});SceneFrame();Check(backend.GetPassGroupAudit(out gameOwners).Length>0&&((System.Collections.ICollection)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!).Count==0,"highlight removal hands selector back to resident preparation");
            scene.NativePassSceneEnabled=false;SceneFrame();Check(backend.GetPassGroupAudit(out gameOwners).Length==0&&((System.Collections.ICollection)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!).Count>0,"benchmark mode handoff restores managed emission without stale native groups");
            scene.NativePassSceneEnabled=true;SceneFrame();Check(backend.GetPassGroupAudit(out gameOwners).Length>0&&((System.Collections.ICollection)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!).Count==0,"native mode reentry resumes the committed selector");
            scene.Process(new MyRenderMessageUpdateModelProperties{ID=200,MaterialName=MyStringId.GetOrCompute("__unused_material__"),FlagsChange=new RenderFlagsChange{Remove=RenderFlags.Visible}});SceneFrame();
            Check(backend.GetPassGroupAudit(out gameOwners).Length>0,"flags for an unused material preserve loaded stock new-pipeline ownership");
            // DX11 converts on presence, even when the new flag value is empty.
            scene.Process(new MyRenderMessageUpdateModelProperties{ID=200,MaterialName=MyStringId.GetOrCompute(usedMaterial),FlagsChange=new RenderFlagsChange()});SceneFrame();
            Check(backend.GetPassGroupAudit(out gameOwners).Length==0&&((System.Collections.ICollection)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!).Count>0,"used material flags transfer source ownership to explicit old-pipeline fallback");
            scene.Process(new MyRenderMessageUpdateModelProperties{ID=200,MaterialName=MyStringId.GetOrCompute(usedMaterial),FlagsChange=new RenderFlagsChange()});SceneFrame();
            Check(backend.GetPassGroupAudit(out gameOwners).Length==0,"resetting material flags does not revive source new-pipeline ownership in the other slot");
            scene.Process(new MyRenderMessageChangeModel{ID=200,Model=Path.Combine(content,"Models/Cubes/Large/ArmorCenter.mwm"),Scale=1});SceneFrame();
            Check(backend.GetPassGroupAudit(out gameOwners).Length==0,"model replacement preserves the actor's converted old-pipeline ownership");
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=200,Model=Path.Combine(content,"Models/Cubes/Large/Shelf_1.mwm"),DebugName="mixed cutout game actor",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
            shadowType.GetProperty("UpdateMask")!.SetValue(shadows,1u);((float[])shadowType.GetField("ProjectionFactors")!.GetValue(shadows)!)[0]=100;
            ((BoundingFrustumD[])shadowType.GetField("casterFrusta",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(shadows)!)[0]=new BoundingFrustumD(shadow);
            SceneFrame();gameGroups=backend.GetPassGroupAudit(out gameOwners);
            Check(gameGroups.Any(g=>g.View==0&&g.Shader>=4)&&((System.Collections.ICollection)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!).Count==0,"installed mixed model's opaque and stock cutout parts both bypass managed emission");
            Check(gameGroups.Where(g=>g.View==1).All(g=>g.Shader<4)&&backend.ReadbackShadow(0,256).Any(x=>x<1),"cutout geometry is excluded from dedicated shadow segments while real solids retain depth");
            SceneFrame();Check(backend.GetPassGroupAudit(out gameOwners).Count(g=>g.View==0&&g.Shader>=4)==gameGroups.Count(g=>g.View==0&&g.Shader>=4),"read-only cutout material state remains valid in the other frame slot");
            // Preserve both authored policies across managed/native ownership.
            foreach(string name in new[]{"GlassOutside","GlassInside"})MyTransparentMaterials.AddMaterial(new MyTransparentMaterial(MyStringId.GetOrCompute(name),MyTransparentMaterialTextureType.FileTexture,"Textures/GUI/Blank.dds","Textures/GUI/Blank.dds",1,false,false,new Vector4(.5f),Vector4.Zero,Vector4.One,Vector4.One,false,name=="GlassOutside"));
            // Normal game publication supplies stock pass constants. Select the
            // same biased cascade branch in both source and native recorders.
            var stockShadow=new byte[1200];BitConverter.GetBytes(1u).CopyTo(stockShadow,1192);backend.SetShadowPassConstants(stockShadow);
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=200,Model=Path.Combine(content,"Models/Cubes/Large/HalfWindow.mwm"),DebugName="mixed opaque glass actor",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
            scene.Process(new MyRenderMessageSetParentCullObject{ID=200,CullObjectID=100,ChildToParent=Matrix.CreateTranslation(0,0,-5)});
            string[] GlassPackets(){var packets=(System.Collections.IEnumerable)typeof(NativeBackend).GetField("glassPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;return packets.Cast<object>().Select(p=>{var geometry=p.GetType().GetField("Item1")!.GetValue(p)!;var t=geometry.GetType();string Bytes(string field)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData((byte[])t.GetField(field)!.GetValue(geometry)!));return string.Join(":",t.GetField("Mesh")!.GetValue(geometry),t.GetField("Color")!.GetValue(geometry),t.GetField("Normal")!.GetValue(geometry),t.GetField("Flags")!.GetValue(geometry),Bytes("Constants"),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData((byte[])p.GetType().GetField("Item2")!.GetValue(p)!)));}).ToArray();}
            SceneFrame();var referenceGlass=GlassPackets();var referenceDepth=backend.ReadbackShadow(0,256);var referenceMixedColour=backend.Readback(64,64);
            Check(referenceGlass.Length==2&&backend.GetPassGroupAudit(out gameOwners).Length==0,"mixed switch off retains installed glass and opaque managed path");
            scene.NativeMixedOpaqueEnabled=true;SceneFrame();var mixedGroups=backend.GetPassGroupAudit(out gameOwners);
            Check(mixedGroups.Length>0&&gameOwners.Contains(200u)&&((System.Collections.ICollection)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!).Count==0,"mixed switch bypasses managed opaque packets while keeping glass consumer separate");
            Check(backend.GetPassTransparentRequests() is [{Owner:200,Lod:0,Alpha:0}],"mixed game model returns only its main glass consumer ticket");
            Check(GlassPackets().SequenceEqual(referenceGlass),"mixed game glass geometry, culling, textures, object and material constants match existing managed consumer exactly");
            Check(backend.Readback(64,64).SequenceEqual(referenceMixedColour),"mixed game opaque and glass composite preserves the reference colour pixels");
            Check(backend.ReadbackShadow(0,256).SequenceEqual(referenceDepth),"mixed solids preserve installed depth silhouettes");
            SceneFrame();Check(GlassPackets().SequenceEqual(referenceGlass)&&backend.ReadbackShadow(0,256).SequenceEqual(referenceDepth),"mixed glass and depth remain identical in the other GPU frame slot");
            scene.Process(new MyRenderMessageUpdateRenderEntity{ID=200,DiffuseColor=Color.Red});SceneFrame();Check(GlassPackets().SequenceEqual(referenceGlass),"mixed glass ignores legacy entity diffuse color as stock DX11 does");
            foreach(string name in new[]{"GlassOutside","GlassInside"})scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=200,MaterialName=MyStringId.GetOrCompute(name),DiffuseColor=Color.Red,Emissivity=1});
            SceneFrame();var mixedTint=GlassPackets();Check(!mixedTint.SequenceEqual(referenceGlass),"mixed glass consumes actual material appearance changes");
            scene.NativeMixedOpaqueEnabled=false;SceneFrame();Check(GlassPackets().SequenceEqual(mixedTint)&&backend.GetPassGroupAudit(out gameOwners).Length==0,"mixed switch handoff preserves glass appearance and clears native opaque groups");
            scene.NativeMixedOpaqueEnabled=true;SceneFrame();rootActor.SetVisibility(false);SceneFrame();Check(backend.GetPassTransparentRequests().Length==0&&GlassPackets().Length==0,"hidden parent suppresses both native opaque and managed glass consumers");rootActor.SetVisibility(true);SceneFrame();
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=201,Model=Path.Combine(content,"Models/Cubes/Large/RailingStraight.mwm"),DebugName="LOD bounds parity actor",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});SceneFrame();
            var railingData=actorMap[201u]!;var railingActor=(VRage.Render.Scene.MyActor)railingData.GetType().GetField("Actor")!.GetValue(railingData)!;var rootBounds=railingActor.LocalAabb;
            // Numeric oracle captured from installed DX11 MyModel union bounds,
            // independent of the Vulkan union implementation.
            var sourceBounds=new BoundingBox(new Vector3(-1.2490224838256836f,-1.250396966934204f,-1.24602472782135f),new Vector3(4.999999873689376e-05f,2.185569216925365e-12f,1.24602472782135f));
            scene.StockLodBoundsEnabled=true;SceneFrame();Console.WriteLine(JsonSerializer.Serialize(new{RootMin=rootBounds.Min.ToString(),RootMax=rootBounds.Max.ToString(),UnionMin=railingActor.LocalAabb.Min.ToString(),UnionMax=railingActor.LocalAabb.Max.ToString(),ExpectedMin=sourceBounds.Min.ToString(),ExpectedMax=sourceBounds.Max.ToString(),Owned=railingData.GetType().GetField("ModelBoundsOwned")!.GetValue(railingData)}));Check(railingActor.LocalAabb==sourceBounds&&rootBounds!=sourceBounds,"installed railing LOD union replaces the narrower root culling bound");
            SceneFrame();Check(railingActor.LocalAabb==sourceBounds,"LOD union bound survives the other frame slot");
            scene.StockLodBoundsEnabled=false;SceneFrame();Check(railingActor.LocalAabb==rootBounds,"bounds benchmark switch restores protected root-bound control");
            var authoredBounds=new BoundingBox(new Vector3(-3),new Vector3(3));railingActor.SetLocalAabb(authoredBounds);scene.StockLodBoundsEnabled=true;SceneFrame();Check(railingActor.LocalAabb==authoredBounds,"RenderDirect-authored local bounds retain ownership across the parity switch");
            scene.HasEntityDecals=owner=>owner==202;
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=202,Model=Path.Combine(content,"Models/Cubes/Large/HalfWindow.mwm"),DebugName="old glass decal owner",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline});
            float[] OldGlassDistances(){var packets=(System.Collections.IEnumerable)typeof(NativeBackend).GetField("glassPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;return packets.Cast<object>().Select(p=>p.GetType().GetField("Item3")!.GetValue(p)).OfType<float>().ToArray();}
            SceneFrame();Check(OldGlassDistances() is [25,25],"actual forced-old model carries decal-owner distance independently of its rigid vertex stream");
            scene.HasEntityDecals=_=>false;SceneFrame();Check(OldGlassDistances() is [float.PositiveInfinity,float.PositiveInfinity],"old model without decal membership remains visible but is excluded from decal depth");
            var enabledGlassPixels=backend.Readback(64,64);
            var visibleSettings=scene.RenderSettings;var glassSettings=visibleSettings;glassSettings.DrawTransparentModels=false;scene.RenderSettings=glassSettings;
            SceneFrame();Check(OldGlassDistances().Length==0&&GlassPackets().Length==2,"OLD transparent-model switch suppresses only the forced-old glass consumer");
            glassSettings.DrawTransparentModels=true;glassSettings.DrawTransparentModelsInstanced=false;scene.RenderSettings=glassSettings;
            SceneFrame();Check(OldGlassDistances().Length==2&&GlassPackets().Length==2,"NEW transparent-model switch suppresses the mixed native glass consumer independently");
            scene.NativeMixedOpaqueEnabled=false;SceneFrame();Check(OldGlassDistances().Length==2&&GlassPackets().Length==2,"NEW transparent-model switch also suppresses managed new-model glass fallback");scene.NativeMixedOpaqueEnabled=true;
            glassSettings.DrawTransparentModels=false;scene.RenderSettings=glassSettings;SceneFrame();Check(GlassPackets().Length==0&&backend.GetPassGroupAudit(out gameOwners).Length>0&&!backend.Readback(64,64).SequenceEqual(enabledGlassPixels),"disabling both glass switches changes visible pixels while preserving native opaque geometry");
            scene.RenderSettings=visibleSettings;SceneFrame();Check(OldGlassDistances().Length==2&&GlassPackets().Length==4&&backend.Readback(64,64).SequenceEqual(enabledGlassPixels),"restoring glass switches restores exact visible pixels and both source consumers without rebuilding ownership");
            scene.Clear();backend.BeginPacketCollection();Check(backend.GetPassGroupAudit(out gameOwners).Length==0&&backend.GetPassTransparentRequests().Length==0,"scene teardown retires native ownership and glass tickets before model resources");
        }
        var critical=backend.GetCriticalProfile();Check(Marshal.SizeOf<NativeCriticalProfile>()==928&&critical.Selected>0&&critical.Selected<=critical.Completed/8+1&&critical.Rows[13*5]>0&&critical.Rows[12*5]>0,"sampled native profile counts real command API calls and completed frame scopes");
        Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"zero core and synchronization validation errors");File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationEnabled=backend.GetStats().ValidationEnabled,SyncValidationRequested=Environment.GetEnvironmentVariable("VK_VALIDATION_VALIDATE_SYNC")=="1",ValidationErrors=backend.GetStats().ValidationErrors},new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
}


