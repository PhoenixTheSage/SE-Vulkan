using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.FileSystem;
using VRage.Library.Utils;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using VRageRender.Import;

partial class Program
{
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,name)=>{string path=Path.Combine(bin,name.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};return Run(bin,args.Contains("--lod-only"),args.Contains("--light-capacity-only"),args.Contains("--light-selection-only"),args.Contains("--mesh-decals-only"),args.Contains("--raster-only"),args.Contains("--distance-fade-only"),args.Contains("--entity-color-only"));}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string bin,bool lodOnly,bool lightCapacityOnly,bool lightSelectionOnly,bool meshDecalsOnly,bool rasterOnly,bool distanceFadeOnly,bool entityColorOnly)
    {
        int checks=0;void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        string content=Path.GetFullPath(Path.Combine(bin,"../Content")),output=Path.GetFullPath("TestResults/SceneSubmissionGpuTests");Directory.CreateDirectory(output);MyFileSystem.Init(content,output);
        ParallelTasks.Parallel.Scheduler=new ParallelTasks.FakeTaskScheduler();
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(content,"Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(content,"Shaders"));
        backend.ModelBatchingEnabled=true;Check(backend.WorldRecipesEnabled,"native recipes are enabled by default without an override");using var scene=new VulkanScene(backend);var failures=new List<string>();scene.ContentFailed+=(path,error)=>failures.Add(path+": "+error);
        string armor=Path.Combine(content,"Models/Cubes/large/ArmorCenter.mwm");
        Check(scene.ActorLodsEnabled&&scene.NewActorViewsEnabled,"production defaults select stock old and new actor LOD policies");
        scene.ActorLodsEnabled=false;scene.NewActorViewsEnabled=false;
        if(System.Environment.GetCommandLineArgs().Contains("--ray-scene-only")){int count=RaySceneRegression(scene,content,(ok,text)=>Check(ok,text));
            var rayStats=backend.GetStats();Check(rayStats.ValidationErrors==0,"ray scene capture raises zero validation errors");Console.WriteLine("PASS "+(count+1)+" RayScene v1 producer checks");return 0;}
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=10,Model=armor,DebugName="submission test",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        if(System.Environment.GetCommandLineArgs().Contains("--sert-scene-only")){int count=SertSceneRegression(backend,scene,content,output,(ok,text)=>Check(ok,text));
            Check(backend.GetStats().ValidationErrors==0,"SE-RT scene composition raises zero validation errors");Console.WriteLine("PASS "+(count+1)+" SE-RT SoftwareVulkan scene checks");return 0;}
        MatrixD projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,distanceFadeOnly?10000:100);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;light.SunDiscIntensity=0;
        Vector3D renderCamera=Vector3D.Zero;
        byte[] Render(bool retained,Action configure=null){backend.RetainedSubmissionEnabled=retained;backend.BeginPacketCollection();scene.SetCamera(renderCamera,MyTimeSpan.Zero,1f/60);scene.Draw(backend);configure?.Invoke();backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,renderSettings:scene.RenderSettings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));if(!backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black))throw new Exception("Frame missing");backend.EndFrame();return backend.Readback(64,64);}
        void Pair(string name){var reference=Render(false);Check(Render(true).SequenceEqual(reference),name+" on first slot");Check(Render(true).SequenceEqual(reference),name+" on second slot");}
        Pair("installed model prepared submission preserves legacy pixels");
        Check(failures.Count==0 && backend.GetSubmissionStats().Actors==1 && backend.GetSubmissionStats().Parts>0 && backend.GetSubmissionStats().RebuiltActors==0,"steady actor replays without reconstructing its descriptors: "+string.Join("; ",failures));
        if(entityColorOnly){EntityColorSceneChecks(backend,scene,output,()=>Render(false),()=>Render(true),Check);Console.WriteLine(checks+" entity color scene checks passed.");return 0;}
        if(distanceFadeOnly){DistanceFadeSceneChecks(backend,scene,output,()=>Render(false),()=>Render(true),(keep,setup)=>Render(keep,setup),camera=>renderCamera=camera,Check);Console.WriteLine(checks+" distance-fade scene checks passed.");return 0;}
        if(rasterOnly){RasterSceneChecks(backend,scene,output,()=>Render(false),()=>Render(true),Check);File.WriteAllText(Path.Combine(output,"raster-result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=backend.GetStats().ValidationErrors}));Console.WriteLine(checks+" raster scene checks passed.");return 0;}
        if(meshDecalsOnly){MeshDecalSceneChecks(backend,scene,library,output,()=>Render(false),()=>Render(true),Check);File.WriteAllText(Path.Combine(output,"mesh-decal-result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=backend.GetStats().ValidationErrors}));Console.WriteLine(checks+" mesh-decal scene checks passed.");return 0;}
        if(lightSelectionOnly){LightSelectionChecks(backend,scene,projection,()=>Render(false),Check);File.WriteAllText(Path.Combine(output,"light-selection-result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=backend.GetStats().ValidationErrors}));Console.WriteLine(checks+" managed light discovery checks passed.");return 0;}
        if(lightCapacityOnly){
            backend.SetLightTiles(true);
            var created=new List<VRage.Render.Scene.Components.MyLightComponent>();
            void Add(bool spot,int index){var value=scene.ActorFactory.CreateLight("stock local capacity");value.Owner.SetID((uint)(100000+index));
                var layout=new MyLightLayout{Color=spot?new Vector3(.001f):new Vector3(.01f),Range=20,Falloff=1,GlossFactor=1,DiffuseFactor=1};
                var data=new UpdateRenderLightData{Position=new Vector3D(0,0,-1),PointLightOn=!spot,SpotLightOn=spot,PointIntensity=1,SpotIntensity=1,PointLight=layout,SpotLight=new MySpotLightLayout{Direction=Vector3.Forward,Up=Vector3.Up,ApertureCos=.75f,Light=layout}};
                value.UpdateData(ref data);value.Owner.SetVisibility(true);created.Add(value);}
            for(int i=0;i<32;i++)Add(true,i);
            Render(false);var onlySpots=backend.ReadbackLightingStage(3,64,64);
            for(int i=0;i<4097;i++)Add(false,i+32);
            double previous=0;
            foreach(var pair in new[]{(MyRenderQualityEnum.LOW,256),(MyRenderQualityEnum.NORMAL,512),(MyRenderQualityEnum.HIGH,2048),(MyRenderQualityEnum.EXTREME,4096)}){
                var capacitySettings=scene.RenderSettings;capacitySettings.User.LightsQuality=pair.Item1;scene.RenderSettings=capacitySettings;
                Render(false);Render(false);Render(false);
                var stats=backend.GetLightTileStats();var spotOwners=(System.Collections.IDictionary)typeof(VulkanScene).GetField("frameSpotActors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
                Check(stats.Points==pair.Item2&&spotOwners.Count==32,"actual managed scene honors independent "+pair.Item2+" point and 32 spot budgets at "+pair.Item1);
                var hdr=backend.ReadbackLightingStage(3,64,64);double signal=hdr.Where((v,i)=>i%4<3).Zip(onlySpots.Where((v,i)=>i%4<3),(a,b)=>(double)(a-b)).Sum();
                Check(signal>previous,"increasing stock point quality retains additional visible radiance at "+pair.Item1);previous=signal;
            }
            foreach(var value in created)value.Owner.Destruct();Render(false);Render(false);Render(false);
            Check(backend.GetLightTileStats().Points==0&&scene.LightCount==0,"dense local-light removal clears source and native point state");
            Check(backend.GetStats().ValidationErrors==0,"actual managed capacity lane has zero core and synchronization validation errors");
            File.WriteAllText(Path.Combine(output,"light-capacity-result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=backend.GetStats().ValidationErrors}));Console.WriteLine(checks+" managed local-light capacity checks passed.");return 0;
        }
        Check(System.Runtime.InteropServices.Marshal.SizeOf<NativeModelInstance>()==120 && System.Runtime.InteropServices.Marshal.SizeOf<NativeModelGroupStats>()==48,"bulk model ABI matches native record and statistics sizes");
        backend.BulkModelsEnabled=true;Pair("bulk single-part model submission preserves legacy pixels");
        Check(backend.GetModelGroupStats().Instances==1 && backend.GetModelGroupStats().Groups==1 && backend.GetModelGroupStats().Packets>0,"single-part model enters native-owned grouping without managed part packets");
        backend.BulkModelsEnabled=false;
        scene.Process(new MyRenderMessageUpdateRenderEntity{ID=10,DiffuseColor=new Color(0,255,0),ColorMaskHSV=new Vector3(.2f,.3f,-.1f),Dithering=.2f});Pair("appearance/Hsv/dither messages invalidate prepared descriptors");
        // Resolve the stock part name from the authoritative model, then drive
        // production message processing (not a test-only cache invalidator).
        object cache=typeof(VulkanScene).GetField("models",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        object model=cache.GetType().GetMethod("Get")!.Invoke(cache,new object[]{armor,1f})!;
        var parts=(System.Collections.IList)model.GetType().GetField("Parts")!.GetValue(model)!;object part=parts[0]!;
        bool NewModel(object root)=>(bool)cache.GetType().GetMethod("IsStockNewModel")!.Invoke(cache,new[]{root})!;
        var shelfModel=cache.GetType().GetMethod("Get")!.Invoke(cache,new object[]{Path.Combine(content,"Models/Cubes/Large/Shelf_1.mwm"),1f})!;
        Check(NewModel(shelfModel),"CPU new-pipeline classifier accepts installed stock shelf and authored LOD streams");
        var characterModel=cache.GetType().GetMethod("Get")!.Invoke(cache,new object[]{Path.Combine(content,"Models/Characters/Astronaut/Female/SE_astronaut.mwm"),1f})!;
        Check(!NewModel(characterModel),"CPU new-pipeline classifier rejects stock skinned character geometry");
        // Clone an installed mesh into test-owned storage and alter only its
        // indexed PatternScale payload. Stock assets use binary scales, which
        // hide the half-precision rounding required for mod-authored scales.
        string scaledUvModel=Path.Combine(output,"PatternScale3.mwm");
        string scaledUvSource=Path.Combine(content,"Models/Cubes/Large/Armor/SquarePlate.mwm");
        var scaleSourceImporter=new VRageRender.Import.MyModelImporter();scaleSourceImporter.ImportData(scaledUvSource,new[]{"GeometryDataAsset"});
        if(scaleSourceImporter.GetTagData().TryGetValue("GeometryDataAsset",out var scaleGeometry))scaledUvSource=new VRageRender.Import.MyLODDescriptor{Model=(string)scaleGeometry}.GetModelAbsoluteFilePath(scaledUvSource);
        File.Copy(scaledUvSource,scaledUvModel,true);
        using(var stream=File.Open(scaledUvModel,FileMode.Open,FileAccess.ReadWrite))using(var reader=new BinaryReader(stream)){
            reader.ReadString();int debugCount=reader.ReadInt32();for(int i=0;i<debugCount;i++)reader.ReadString();
            int tagCount=reader.ReadInt32(),patternOffset=-1;for(int i=0;i<tagCount;i++){string tag=reader.ReadString();int at=reader.ReadInt32();if(tag=="PatternScale")patternOffset=at;}
            if(patternOffset<0)throw new Exception("PatternScale fixture requires an indexed authored scale");
            stream.Position=patternOffset;if(reader.ReadString()!="PatternScale")throw new Exception("PatternScale fixture index is invalid");
            using var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true);writer.Write(3f);
        }
        foreach(string relative in new[]{"Models/Cubes/Large/Shelf_1.mwm","Models/Cubes/Large/InteriorWallSide.mwm","Models/Cubes/Large/CylindricalColumn.mwm","Models/Cubes/Large/Armor/SquarePlate.mwm",scaledUvModel}){
            string file=Path.Combine(content,relative);object basisModel=cache.GetType().GetMethod("Get")!.Invoke(cache,new object[]{file,1f})!;
            var basisImporter=new VRageRender.Import.MyModelImporter();basisImporter.ImportData(file,new[]{"Vertices","Normals","Tangents","Binormals","GeometryDataAsset","TexCoords0","PatternScale"});var basisTags=basisImporter.GetTagData();
        if(basisTags.TryGetValue("GeometryDataAsset",out var basisGeometry)){string geometryFile=new VRageRender.Import.MyLODDescriptor{Model=(string)basisGeometry}.GetModelAbsoluteFilePath(file);basisImporter.ImportData(geometryFile,new[]{"Vertices","Normals","Tangents","Binormals","TexCoords0","PatternScale"});basisTags=basisImporter.GetTagData();}
        var basisNormals=(VRageMath.PackedVector.Byte4[])basisTags["Normals"];var basisTangents=(VRageMath.PackedVector.Byte4[])basisTags["Tangents"];var basisBinormals=(VRageMath.PackedVector.Byte4[])basisTags["Binormals"];var basisVertices=(NativeWorldVertex[])basisModel.GetType().GetField("WorldVertices")!.GetValue(basisModel)!;bool exactBasis=basisVertices.Length==basisNormals.Length;
        for(int vertex=0;vertex<basisVertices.Length;vertex++){Vector3 n=VRage.Import.VF_Packer.UnpackNormal(basisNormals[vertex].PackedValue),t=VRage.Import.VF_Packer.UnpackNormal(basisTangents[vertex].PackedValue),b=VRage.Import.VF_Packer.UnpackNormal(basisBinormals[vertex].PackedValue);Vector4 tangentSign=new(t,Vector3.Dot(Vector3.Cross(t,n),b)<0?-1:1);uint expected=VRage.Import.VF_Packer.PackTangentSign(ref tangentSign);var packed=basisVertices[vertex].Material;uint actual=(uint)Math.Round(packed.TangentX*255)|((uint)Math.Round(packed.TangentY*255)<<8)|((uint)Math.Round(packed.TangentZ*255)<<16)|((uint)Math.Round(packed.TangentW*255)<<24);exactBasis&=actual==expected;}
        Check(exactBasis,"authored tangent/sign bytes match stock packing for "+Path.GetFileName(file));
        int normalMismatch=0;
        for(int vertex=0;vertex<basisVertices.Length;vertex++){var packed=basisVertices[vertex].Material;uint actual=(uint)packed.NormalX|((uint)packed.NormalY<<16);if(actual!=basisNormals[vertex].PackedValue)normalMismatch++;}
        Console.WriteLine($"NORMAL {Path.GetFileName(file)} mismatches={normalMismatch}/{basisVertices.Length}");
        Check(normalMismatch==0,"authored normal bytes survive import unchanged for "+Path.GetFileName(file));
        var authoredUv=(VRageMath.PackedVector.HalfVector2[])basisTags["TexCoords0"];float authoredPattern=basisTags.TryGetValue("PatternScale",out var pattern)?(float)pattern:1f;int uvMismatch=0;
        if(file==scaledUvModel)Check(authoredPattern==3 && authoredUv.Any(uv=>new VRageMath.PackedVector.HalfVector2(uv.ToVector2()/3f).ToVector2()!=uv.ToVector2()/3f),"private nonbinary scale fixture requires observable stock half rounding");
        for(int vertex=0;vertex<basisVertices.Length;vertex++){Vector2 scaled=authoredUv[vertex].ToVector2()/authoredPattern;Vector2 expectedUv=authoredPattern==1?scaled:new VRageMath.PackedVector.HalfVector2(scaled).ToVector2();var actualUv=basisVertices[vertex].Material;if(actualUv.U!=expectedUv.X||actualUv.V!=expectedUv.Y)uvMismatch++;}
        Console.WriteLine($"UV {Path.GetFileName(file)} PatternScale={authoredPattern} mismatches={uvMismatch}/{basisVertices.Length}");
        Check(uvMismatch==0,"pattern-scaled UVs preserve stock half precision for "+Path.GetFileName(file));
        }
        var flareLight=scene.ActorFactory.CreateLight("rotated flare parity");
        var flareOrigin=new Vector3D(1,.5,-5);
        var flareData=new UpdateRenderLightData{Position=flareOrigin,PointLightOn=true,PointIntensity=1,PointLight=new MyLightLayout{Color=Vector3.One,Range=10},Glare=new MyFlareDesc{Enabled=true,Type=VRageRender.Lights.MyGlareTypeEnum.Normal,Intensity=1,MaxDistance=100,QuerySize=.2f,QueryFreqMinMs=150,QueryFreqRndMs=100,SizeMultiplier=Vector2.One,Glares=new[]{new MySubGlare{Material=MyTransparentMaterials.ErrorMaterial.Id,Type=SubGlareType.Rotated,OcclusionToIntensityCurve=new[]{new MySubGlare.KeyPoint{Occlusion=0,Intensity=1},new MySubGlare.KeyPoint{Occlusion=1,Intensity=1}},FixedSize=true,Size=new Vector2(.1f,.2f),Color=Vector4.One,ScreenIntensityMultiplierCenter=1,ScreenIntensityMultiplierEdge=1}}}};
        flareLight.UpdateData(ref flareData);flareLight.Owner.SetVisibility(true);
        ulong flareId=(ulong)flareLight.GetType().GetField("FlareId")!.GetValue(flareLight)!;
        var flareOcclusion=(Dictionary<ulong,float>)typeof(VulkanScene).GetField("flareOcclusion",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;flareOcclusion[flareId]=0;
        object flareHelper=typeof(VulkanScene).GetField("billboards",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        var generatedFlares=(List<MyBillboard>)flareHelper.GetType().GetField("generated",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(flareHelper)!;generatedFlares.Clear();
        scene.SetCamera(Vector3D.Zero,MyTimeSpan.Zero,1f/60);scene.DrawFlares(MatrixD.CreateRotationX(.2)*MatrixD.CreateRotationZ(.6),projection,64,64,1);
        Check(generatedFlares.Count==1,"production flare path emits the isolated rotated descriptor");
        VRage.Utils.MyUtils.GetBillboardQuadAdvancedRotated(out var stockFlareQuad,flareOrigin,1,2,0,Vector3D.Zero);var actualFlare=generatedFlares[0];
        Check(Vector3D.Distance(actualFlare.Position0,stockFlareQuad.Point0)<1e-11 && Vector3D.Distance(actualFlare.Position1,stockFlareQuad.Point1)<1e-11 && Vector3D.Distance(actualFlare.Position2,stockFlareQuad.Point2)<1e-11 && Vector3D.Distance(actualFlare.Position3,stockFlareQuad.Point3)<1e-11,"rotated flare corners match stock world-up orientation under a rolled camera");
        var flareStates=(Dictionary<ulong,StockFlareOcclusionState>)typeof(VulkanScene).GetField("flareStates",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        var queryState=flareStates[flareId];ulong firstFlareRequest=queryState.PendingId;long flareRequests=scene.FlareQueries;
        Check(firstFlareRequest!=0,"production flare query has a distinct pending identity");
        Render(false);for(int f=0;f<3;f++){scene.DrawFlares(MatrixD.Identity,projection,64,64,1);Render(false);}
        Check(queryState.PendingId==0&&queryState.QueryTime>0&&scene.FlareQueries==flareRequests,"completed native flare query observes the stock retry interval without reissuing");
        scene.SetCamera(Vector3D.Zero,MyTimeSpan.FromMilliseconds(1000),1f/60);scene.DrawFlares(MatrixD.Identity,projection,64,64,1);ulong nextFlareRequest=queryState.PendingId;
        Check(nextFlareRequest!=0&&nextFlareRequest!=firstFlareRequest&&scene.FlareQueries==flareRequests+1,"elapsed flare interval submits a fresh native request identity");
        Render(false);scene.SetCamera(Vector3D.Zero,MyTimeSpan.FromMilliseconds(1000),1f/60);scene.DrawFlares(MatrixD.Identity,projection,64,64,1);
        Check(queryState.PendingId==nextFlareRequest,"repeated completed-slot data cannot resolve the newer in-flight flare request");
        flareLight.Owner.Destruct();scene.DrawFlares(MatrixD.Identity,projection,64,64,1);
        Check(!flareStates.ContainsKey(flareId)&&!flareOcclusion.ContainsKey(flareId),"deleted flare owner releases its temporal and completed visibility state");
        generatedFlares.Clear();backend.QueueFlareRaster(null);
        var sunIdSetter=typeof(ClientPlugin.Rendering.GuestBridge).GetMethod("SetSunLightRenderId",BindingFlags.NonPublic|BindingFlags.Static)!;
        sunIdSetter.Invoke(null,new object[]{60000u});ClientPlugin.Rendering.GuestBridge.SetNativeSunGlare(false);
        VRage.Render.Scene.Components.MyLightComponent MakeGlare(uint id,Vector3D origin,VRageRender.Lights.MyGlareTypeEnum type){var value=scene.ActorFactory.CreateLight("glare identity parity");value.Owner.SetID(id);var data=flareData;data.Position=origin;data.Glare.Type=type;data.SpotLight.Direction=Vector3.Normalize(-(Vector3)origin);value.UpdateData(ref data);value.Owner.SetVisibility(true);return value;}
        var solarGlare=MakeGlare(60000,new Vector3D(1,.5,-5),VRageRender.Lights.MyGlareTypeEnum.Distant);
        var directionalGlare=MakeGlare(60001,new Vector3D(-1,.5,-5),VRageRender.Lights.MyGlareTypeEnum.Directional);
        var otherDistantGlare=MakeGlare(60002,new Vector3D(0,-.5,-5),VRageRender.Lights.MyGlareTypeEnum.Distant);
        generatedFlares.Clear();scene.DrawFlares(MatrixD.Identity,projection,64,64,1);
        bool HasFlare(Vector3D origin)=>generatedFlares.Any(b=>Vector3D.Distance((b.Position0+b.Position1+b.Position2+b.Position3)/4,origin)<1e-11);
        Check(!HasFlare(new Vector3D(1,.5,-5)),"celestial glare ownership suppresses the exact solar actor");
        Check(HasFlare(new Vector3D(-1,.5,-5)),"celestial solar ownership preserves directional local-light glare");
        Check(HasFlare(new Vector3D(0,-.5,-5)),"celestial solar ownership preserves unrelated distant glare");
        solarGlare.Owner.Destruct();directionalGlare.Owner.Destruct();otherDistantGlare.Owner.Destruct();ClientPlugin.Rendering.GuestBridge.SetNativeSunGlare(true);sunIdSetter.Invoke(null,new object[]{uint.MaxValue});
        var edgeOrigin=new Vector3D(5.2,0,-5);var edgeGlare=MakeGlare(60003,edgeOrigin,VRageRender.Lights.MyGlareTypeEnum.Normal);generatedFlares.Clear();scene.DrawFlares(MatrixD.Identity,projection,64,64,1);
        Check(HasFlare(edgeOrigin),"stock flare producer retains a partially visible quad whose center is outside the viewport");
        edgeGlare.Owner.Destruct();scene.DrawFlares(MatrixD.Identity,projection,64,64,1);

        generatedFlares.Clear();backend.QueueFlareRaster(null);
        string cookiePath=Path.Combine(output,"spot-cookie-role.dds");File.Copy(Path.Combine(content,"Textures/Lights/reflector_large.dds"),cookiePath,true);
        var cookieLight=scene.ActorFactory.CreateLight("spot cookie role parity");var cookieData=new UpdateRenderLightData{Position=Vector3D.Zero,SpotLightOn=true,SpotIntensity=1,ReflectorTexture=cookiePath,SpotLight=new MySpotLightLayout{Direction=Vector3.Forward,Up=Vector3.Up,ApertureCos=.75f,Light=new MyLightLayout{Color=Vector3.One,Range=10,Falloff=1,GlossFactor=1,DiffuseFactor=1}}};cookieLight.UpdateData(ref cookieData);cookieLight.Owner.SetVisibility(true);Render(false);
        var textureEntries=(System.Collections.IDictionary)scene.TextureCache.GetType().GetField("entries",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(scene.TextureCache)!;
        var cookieKeys=textureEntries.Keys.Cast<string>().Where(k=>k.EndsWith("spot-cookie-role.dds",StringComparison.OrdinalIgnoreCase)).ToArray();
        Check(cookieKeys.Length==1&&cookieKeys[0].StartsWith("s:",StringComparison.Ordinal),"production spotlight cookie requests the stock CUSTOM sRGB view");
        cookieLight.Owner.Destruct();Render(false);
        string material=(string)part.GetType().GetField("MaterialName")!.GetValue(part)!;
        scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=10,MaterialName=MyStringId.GetOrCompute(material),DiffuseColor=Color.Red,Emissivity=2});Pair("material color/emission messages invalidate prepared descriptors");
        scene.Process(new MyRenderMessageUpdateModelProperties{ID=10,MaterialName=MyStringId.GetOrCompute(material),FlagsChange=new RenderFlagsChange{Remove=RenderFlags.Visible}});Pair("material visibility removes the prepared part");
        scene.Process(new MyRenderMessageUpdateModelProperties{ID=10,MaterialName=MyStringId.GetOrCompute(material),FlagsChange=new RenderFlagsChange()});Pair("material visibility restoration restores the prepared part");
        scene.Process(new MyRenderMessageChangeModel{ID=10,Model=armor,Scale=1});Render(true);Check(backend.GetSubmissionStats().RebuiltActors==1,"model replacement invalidates actor submission even when the cache returns the same model");Pair("model replacement retains reference pixels");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=10,Visible=false});Render(true);Check(backend.GetSubmissionStats().Actors==0,"hidden actor leaves the selection");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=10,Visible=true});Pair("restored actor selection retains reference pixels");
        var diagnosticReference=Render(true);scene.OcclusionDiagnosticsEnabled=true;scene.ResetOcclusionDiagnostics();
        Check(Render(true).SequenceEqual(diagnosticReference),"occlusion attribution preserves renderer pixels and policy");
        Check(scene.CaptureOcclusionDiagnostics()!=null,"occlusion attribution snapshot is available on the owning render thread");
        var state=new StockActorLodState();var thresholds=new[]{8f,15f,35f};var global=MyGlobalLoddingSettings.Default;global.MaxTransitionInSeconds=1;var pass=MyPassLoddingSetting.Default;
        state.Update(16,thresholds,global,pass,.25f);Check(state.Current==2&&state.Transition==0,"first visible actor selects authored distance level without a startup fade");
        state.Update(40,thresholds,global,pass,.25f);Check(state.Current==2&&state.Next==3&&state.Transition==.001f,"distance crossing retains both stock transition representations");
        state.Update(40,thresholds,global,pass,.25f);Check(Math.Abs(state.Transition-.251f)<.00001f,"transition follows stock time progression and maximum step");
        for(int i=0;i<3;i++)state.Update(40,thresholds,global,pass,.25f);Check(state.Current==3&&state.Transition==0,"completed transition retires the former level");
        global.EnableLodSelection=true;global.LodSelection=0;state.Update(100,thresholds,global,pass,.25f);Check(state.Current==0&&state.Transition==0,"explicit stock level overrides distance selection");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=10,Visible=false});
        string wall=Path.Combine(content,"Models/Cubes/Large/SciFiWall.mwm"),wallLod=Path.Combine(content,"Models/Cubes/large/SciFiWall_LOD3.mwm");
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=20,Model=wall,DebugName="stock LOD test",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible});Render(true);var baseWall=Render(true);
        var settings=new MyNewLoddingSettings();settings.Global=MyGlobalLoddingSettings.Default;settings.Global.EnableLodSelection=true;settings.Global.LodSelection=3;
        var lodMessage=new MyRenderMessageUpdateNewLoddingSettings();lodMessage.Settings.CopyFrom(settings);scene.Process(lodMessage);scene.ActorLodsEnabled=true;Render(true);var selectedWall=Render(true);
        int objectLodOffset=new ReflectedConstantBuffer(backend.WorldLayouts[0],"Object").OffsetOf("object_.LOD");
        var lodPackets=(List<WorldPacket>)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;
        bool MainLod(uint lod)=>lodPackets.Any(p=>(p.Flags&4096)==0&&p.Constants!=null&&BitConverter.ToUInt32(p.Constants,objectLodOffset)==lod);
        Check(MainLod(3),"selected authored LOD reaches retained main object constants");
        var lodSource=lodPackets.First(p=>(p.Flags&4096)==0&&BitConverter.ToUInt32(p.Constants,objectLodOffset)==3);
        Render(false);Check(MainLod(3),"selected authored LOD reaches uncached main object constants");Render(true);Check(MainLod(3),"returning to retained submission preserves authored LOD");
        Check(scene.GetActorLodStats().ReducedActors==1,"production setting message selects a lower authored model level");
        Check(selectedWall.Where((value,index)=>index%4!=3).Any(value=>value>10),"authored LOD fixture produces visible world pixels");
        settings.Global.EnableLodSelection=false;lodMessage.Settings.CopyFrom(settings);scene.Process(lodMessage);scene.ActorLodsEnabled=false;Render(true);scene.ActorLodsEnabled=true;
        // New model ownership resets the selector, matching first visibility.
        scene.Process(new MyRenderMessageChangeModel{ID=20,Model=wall,Scale=1});Render(true);
        Check(scene.GetActorLodStats().ReducedActors==0,"near unparented actor uses authoritative world bounds instead of an empty stock proxy cache");
        settings.Global.EnableLodSelection=true;settings.Global.LodSelection=3;lodMessage.Settings.CopyFrom(settings);scene.Process(lodMessage);Render(true);Render(true);Render(true);Render(true);Render(true);selectedWall=Render(true);
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=20,Visible=false});scene.Process(new MyRenderMessageCreateRenderEntity{ID=21,Model=wallLod,DebugName="authored level reference",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible});Render(true);
        Check(Render(true).SequenceEqual(selectedWall),"selected LOD pixels match direct rendering of the stock LOD asset");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=21,Visible=false});scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=20,Visible=true});scene.ActorLodsEnabled=false;
        Check(Render(true).SequenceEqual(baseWall),"switching LOD selection off restores the cached base representation");
        Check(MainLod(0),"restoring the base representation restores its LOD constant");
        // Same geometry/material can be reused at distinct authored levels.
        // Exercise both batching paths rather than relying on different meshes
        // to accidentally keep their immutable object constants separate.
        uint lodPipeline=lodSource.Pipeline%5==2?lodSource.Pipeline-2:lodSource.Pipeline;
        NativeBackend.PreparedActor LodActor(uint lod)=>new(Enumerable.Range(0,2).Select(_=>new NativeBackend.PreparedPart(lodPipeline,lodSource.Mesh,lodSource.Color,lodSource.Normal,lodSource.Extensions,lodSource.Alpha,Vector3.One,Vector3.Zero,1,0,backend.WorldLayouts[lodPipeline],lod:lod)).ToArray());
        var firstLodActor=LodActor(1);var secondLodActor=LodActor(2);
        void DrawLodPair(bool modelBatch){
            backend.ModelBatchingEnabled=modelBatch;backend.BeginPacketCollection();
            backend.SubmitPreparedActor(firstLodActor,MatrixD.CreateTranslation(-1,0,-5),null,1,512,0,new Vector3(-1,0,-5),2);
            backend.SubmitPreparedActor(secondLodActor,MatrixD.CreateTranslation(1,0,-5),null,1,512,0,new Vector3(1,0,-5),2);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"mixed-LOD batch frame is available");backend.EndFrame();
        }
        DrawLodPair(false);Check(MainLod(1)&&MainLod(2),"rigid batches retain distinct LOD constants for identical geometry and materials");
        DrawLodPair(true);Check(MainLod(1)&&MainLod(2)&&backend.GetModelBatchStats().Batches==2,"model templates retain distinct LOD constants for identical geometry and materials");
        Render(true);
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=20,Visible=false});
        var identityCube=new VRage.MyCubeInstanceData{LocalMatrix=Matrix.Identity};
        scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=2900});scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=2900,InstanceData=new List<VRage.MyCubeInstanceData>{identityCube},Capacity=1});
        var instanceRegistry=(System.Collections.IDictionary)typeof(VulkanScene).GetField("instances",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;object instanceRecord=instanceRegistry[2900u]!;
        bool HasSkinning()=>(bool)instanceRecord.GetType().GetField("EnabledSkinning")!.GetValue(instanceRecord)!;
        Check(!HasSkinning(),"ordinary cube update retains stock non-skinning LOD policy");
        var skinnedCube=identityCube;skinnedCube.EnableSkinning=true;scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=2900,InstanceData=new List<VRage.MyCubeInstanceData>{identityCube,skinnedCube},Capacity=2});
        Check(HasSkinning(),"one enabled cube sets whole-buffer stock skinning LOD policy");
        scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=2900,InstanceData=new List<VRage.MyCubeInstanceData>{identityCube},Capacity=1});Check(!HasSkinning(),"cube replacement clears obsolete skinning LOD policy");
        settings.Global.EnableLodSelection=false;lodMessage.Settings.CopyFrom(settings);scene.Process(lodMessage);scene.ActorLodsEnabled=true;
        var wallModel=cache.GetType().GetMethod("Get")!.Invoke(cache,new object[]{wall,1f})!;var wallDistances=(float[])wallModel.GetType().GetField("LodDistances")!.GetValue(wallModel)!;var wallBounds=(BoundingBox)wallModel.GetType().GetField("Bounds")!.GetValue(wallModel)!;
        double cubeDistance=wallDistances[0]*2+wallBounds.HalfExtents.Length()+1;
        scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=2901});scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=2901,InstanceData=new List<VRage.MyCubeInstanceData>{skinnedCube},Capacity=1});
        foreach(uint id in new[]{48u,49u}){scene.Process(new MyRenderMessageCreateRenderEntity{ID=id,Model=wall,DebugName="stock skinning LOD policy",WorldMatrix=MatrixD.CreateTranslation(0,0,-cubeDistance),Flags=RenderFlags.Visible});scene.Process(new MyRenderMessageSetInstanceBuffer{ID=id,InstanceBufferId=id==48?2900u:2901u,InstanceStart=0,InstanceCount=1,LocalAabb=wallBounds});}
        Render(true);var lodActors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        bool BaseRepresentation(uint id){var actor=lodActors[id]!;return ReferenceEquals(actor.GetType().GetField("Model")!.GetValue(actor),actor.GetType().GetField("RenderModel")!.GetValue(actor));}
        Check(!BaseRepresentation(48)&&BaseRepresentation(49),"actual cube actors use full distance when rigid and quarter distance only when skinning is enabled");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=48,Visible=false});scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=49,Visible=false});scene.ActorLodsEnabled=false;
        // Stock character LODs share the live skeleton, while each authored
        // mesh owns its own 60-bone draw palettes.
        string astronautPath=Path.Combine(content,"Models/Characters/Astronaut/SE_astronaut.mwm");
        object astronautRoot=cache.GetType().GetMethod("Get")!.Invoke(cache,new object[]{astronautPath,1f})!;
        object astronautLod=cache.GetType().GetMethod("GetLod")!.Invoke(cache,new object[]{astronautRoot,2})!;
        string astronautLodPath=(string)astronautLod.GetType().GetField("Name")!.GetValue(astronautLod)!;
        int MaxBone(object model){var indices=(Vector4I[])model.GetType().GetField("BlendIndices")!.GetValue(model)!;return indices.Max(v=>Math.Max(Math.Max(v.X,v.Y),Math.Max(v.Z,v.W)));}
        int boneCount=Math.Max(MaxBone(astronautRoot),MaxBone(astronautLod))+1;
        Check(boneCount>60&&!ReferenceEquals(astronautRoot,astronautLod),"installed astronaut LOD fixture exercises real geometry and multiple draw palettes");
        var skeletonBones=Enumerable.Range(0,boneCount).Select(_=>new MySkeletonBoneDescription{SkinTransform=Matrix.Identity,Parent=-1}).ToArray();
        Matrix[] Pose(float amount)=>Enumerable.Range(0,boneCount).Select(i=>Matrix.CreateTranslation((i%3-1)*amount,(i%5-2)*amount*.5f,0)).ToArray();
        void SetPose(uint id,float amount)=>scene.Process(new MyRenderMessageSetCharacterTransforms{CharacterID=id,BoneAbsoluteTransforms=Pose(amount)});
        foreach(uint id in new[]{5300u,5301u}){
            scene.Process(new MyRenderMessageCreateRenderCharacter{ID=id,Model=id==5300?astronautPath:astronautLodPath,DebugName="skinned authored LOD reference",WorldMatrix=MatrixD.CreateTranslation(0,-1,-5),Flags=RenderFlags.Visible});
            scene.Process(new MyRenderMessageSetCharacterSkeleton{CharacterID=id,SkeletonBones=skeletonBones,SkeletonIndices=Enumerable.Range(0,boneCount).ToArray()});SetPose(id,.05f);
        }
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=5301,Visible=false});
        var savedSkinnedGlobal=settings.Global;settings.Global.EnableLodSelection=true;settings.Global.LodSelection=2;lodMessage.Settings.CopyFrom(settings);scene.Process(lodMessage);scene.ActorLodsEnabled=true;Render(true);var selectedCharacter=Render(true);
        var skinnedData=lodActors[5300u]!;
        Check(ReferenceEquals(skinnedData.GetType().GetField("RenderModel")!.GetValue(skinnedData),astronautLod)&&MainLod(2),"live skinned actor selects authored LOD2 and its shader LOD");
        var astronautParts=(System.Collections.IList)astronautLod.GetType().GetField("Parts")!.GetValue(astronautLod)!;
        int astronautPalettes=astronautParts.Cast<object>().Sum(p=>((System.Collections.ICollection)p.GetType().GetField("SkinParts")!.GetValue(p)!).Count);
        Check(astronautPalettes>1,"selected installed character LOD renders through multiple independently remapped bone palettes");
        Check(selectedCharacter.Where((value,index)=>index%4!=3).Count(value=>value>10)>15,"posed skinned LOD fixture produces actual visible pixels");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=5300,Visible=false});scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=5301,Visible=true});
        var directCharacter=Render(true);Check(selectedCharacter.SequenceEqual(directCharacter),"selected character LOD matches direct authored geometry with the same nonuniform live bone pose");
        SetPose(5300,.2f);SetPose(5301,.2f);var animatedCharacter=Render(true);Check(!animatedCharacter.SequenceEqual(directCharacter),"skinned LOD reference responds to a changed nonuniform bone pose");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=5301,Visible=false});scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=5300,Visible=true});
        Check(Render(true).SequenceEqual(animatedCharacter)&&Render(true).SequenceEqual(animatedCharacter),"selected character LOD follows the current pose in both frame slots");
        settings.Global.EnableLodSelection=false;settings.Global.MaxTransitionInSeconds=.1f;lodMessage.Settings.CopyFrom(settings);scene.Process(lodMessage);Render(true);
        Check(MainLod(0)&&MainLod(2),"skinned distance transition retains both authored meshes and independent object LODs");
        for(int i=0;i<10;i++)Render(true);Check(MainLod(0)&&BaseRepresentation(5300),"skinned transition completes on the near base geometry");
        foreach(uint id in new[]{5300u,5301u})scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=id,Visible=false});
        settings.Global=savedSkinnedGlobal;lodMessage.Settings.CopyFrom(settings);scene.Process(lodMessage);scene.ActorLodsEnabled=false;
        var visibilityState=new StockGroupVisibility();var groupBox=new BoundingBoxD(new Vector3D(-1,-1,-9),new Vector3D(1,1,-7));
        Check(!visibilityState.Observe(1,groupBox,Vector3D.Zero,.1f),"group reentry starts visible");visibilityState.Observe(2,groupBox,Vector3D.Zero,.1f);visibilityState.Observe(3,groupBox,Vector3D.Zero,.1f);ulong epoch=visibilityState.Epoch;
        Check(visibilityState.Accept(epoch,2,4,groupBox.GetInflated(groupBox.Size*.5),groupBox,false)&&visibilityState.Observe(4,groupBox,new Vector3D(.0001,0,0),.1f),"inflated group query survives submillimetre camera movement");
        Check(!visibilityState.Observe(5,groupBox,groupBox.Center,.1f)&&!visibilityState.Accept(epoch,2,5,groupBox,groupBox,false),"near-camera reset rejects pending old-epoch hidden results");
        visibilityState.Observe(6,groupBox,Vector3D.Zero,.1f);epoch=visibilityState.Epoch;visibilityState.Accept(epoch,6,6,groupBox.GetInflated(groupBox.Size*.5),groupBox,false);
        Check(!visibilityState.Observe(7,new BoundingBoxD(groupBox.Min+new Vector3D(10,0,0),groupBox.Max+new Vector3D(10,0,0)),Vector3D.Zero,.1f),"group leaving its queried inflated volume becomes visible immediately");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=20,Visible=false});backend.RigidBatchingEnabled=false;
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=30,Model=armor,DebugName="occluder",WorldMatrix=MatrixD.CreateTranslation(0,0,-2),Flags=RenderFlags.Visible});
        scene.Process(new MyRenderMessageCreateManualCullObject{ID=100,DebugName="query group",WorldMatrix=MatrixD.Identity});
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=31,Model=armor,DebugName="hidden grouped actor",WorldMatrix=MatrixD.CreateTranslation(0,0,-10),Flags=RenderFlags.Visible});
        scene.Process(new MyRenderMessageSetParentCullObject{ID=31,CullObjectID=100,ChildToParent=Matrix.CreateTranslation(0,0,-10)});scene.RenderSettings.DisableOcclusionQueries=true;Render(true);var unculledGroup=Render(true);uint fullCommands=backend.GetPerformanceStats().MainCommands;Check(fullCommands>=2,"occlusion fixture begins with both opaque actors submitted");
        scene.RenderSettings.DisableOcclusionQueries=false;scene.GroupOcclusionEnabled=true;byte[] culledGroup=null;for(int i=0;i<6;i++)culledGroup=Render(true);
        Console.WriteLine("GROUP "+JsonSerializer.Serialize(scene.GetGroupOcclusionStats())+" main="+backend.GetPerformanceStats().MainCommands+" reference="+fullCommands);
        Check(scene.OcclusionCulls>0&&backend.GetPerformanceStats().MainCommands*2==fullCommands,"completed owned group query removes hidden main geometry");
        Check(culledGroup.SequenceEqual(unculledGroup),"group culling preserves visible pixels behind an opaque occluder");
        scene.ViewEligibilityEnabled=true;Check(Render(true).SequenceEqual(unculledGroup),"no-view removal of query-hidden actor preserves exact visible pixels");
        Check(scene.GetViewEligibilityStats().SkippedActors==1 && backend.GetSubmissionStats().Actors==1,"query-hidden actor with no other consumer bypasses preparation");
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=30});for(int i=0;i<6;i++)Render(true);Check(backend.GetPerformanceStats().MainCommands*2==fullCommands,"deleted occluder reveals group geometry after asynchronous query completion");
        Check(scene.GetViewEligibilityStats().SkippedActors==0,"query reentry resumes actor preparation after occluder removal");scene.ViewEligibilityEnabled=false;
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=100});scene.GroupOcclusionEnabled=false;Render(true);backend.RigidBatchingEnabled=true;
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=32,Model=armor,DebugName="notification owner test",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible});
        var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;var actorRecord=actors[32u]!;object actorIdentity=actorRecord.GetType().GetField("Actor")!.GetValue(actorRecord)!;
        var notify=typeof(VulkanScene).GetMethod("MarkTransforms",BindingFlags.Instance|BindingFlags.NonPublic)!;
        System.Threading.Tasks.Parallel.For(0,20000,_=>notify.Invoke(scene,new[]{actorIdentity}));
        var notificationReference=Render(true);Check(Render(true).SequenceEqual(notificationReference),"concurrent transform notifications drain on the render owner without collection corruption or pixel changes");
        Matrix areaProjection=Matrix.Identity;areaProjection.M11=4;areaProjection.M22=5;
        Check(!StockActorSizeCulling.Evaluate(16,4,1,ref areaProjection,2,2,out var exactArea)&&exactArea==20,"stock size test keeps the exact20-pixel boundary");
        areaProjection.M11=3;areaProjection.M22=6;
        Check(StockActorSizeCulling.Evaluate(16,4,1,ref areaProjection,2,2,out var smallArea)&&smallArea==18,"stock size test culls a strictly smaller projected area");
        Check(!StockActorSizeCulling.Evaluate(4,4,1,ref areaProjection,2,2,out var guardedArea)&&guardedArea==null,"final authored distance boundary bypasses projection");
        Check(!StockActorSizeCulling.Evaluate(0,0,1,ref areaProjection,2,2,out var nearArea)&&nearArea==null,"near-camera zero distance never divides by projection W");
        Check(!StockActorSizeCulling.Evaluate(16,4,2,ref areaProjection,2,2,out _)&&!StockActorSizeCulling.Evaluate(16,4,1,ref areaProjection,4,2,out _),"stock size test responds to extent and viewport without a new quality threshold");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=31,Visible=false});scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=32,Visible=false});
        Matrix normalSizeProjection=Matrix.CreatePerspectiveFieldOfView((float)Math.PI/2,1,.1f,10000);
        scene.Process(new MyRenderMessageSetCameraViewMatrix{ViewMatrix=MatrixD.Identity,ProjectionMatrix=normalSizeProjection,CameraPosition=Vector3D.Zero,FOV=(float)Math.PI/2,NearPlane=.1f,FarPlane=10000});
        projection=normalSizeProjection;projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=41,Model=armor,DebugName="stock size far actor",WorldMatrix=MatrixD.CreateTranslation(0,0,-500),Flags=RenderFlags.ForceOldPipeline|RenderFlags.Visible|RenderFlags.CastShadows});Render(true);
        uint sizeReference=backend.GetPerformanceStats().MainCommands;Check(sizeReference>0,"unculled size fixture retains far actor commands");
        scene.ActorSizeCullingEnabled=true;Render(true);Check(scene.GetActorSizeCullStats().MainCulledActors==1&&backend.GetPerformanceStats().MainCommands==0,"stock size rule removes far unparented normal geometry before packet replay");
        Render(true);Check(scene.GetActorSizeCullStats().Transitions==0&&backend.GetPerformanceStats().MainCommands==0,"steady size-cull state remains stable on the second fenced slot");
        scene.ActorSizeCullingEnabled=false;Render(true);Check(backend.GetPerformanceStats().MainCommands==sizeReference&&scene.GetActorSizeCullStats().Transitions==1,"policy disable restores culled actor ownership and commands");
        var sizePost=new VulkanPostprocess();sizePost.Hbao.Enabled=false;sizePost.Settings.BloomEnabled=false;sizePost.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
        scene.ActorSizeCullingEnabled=true;scene.Process(new MyRenderMessageUpdateModelHighlight{ID=41,Thickness=2,OutlineColor=Color.Yellow,InstanceIndex=-1});var highlightBefore=backend.GetHighlightStats();Render(true);
        Check(scene.GetActorSizeCullStats().HighlightOnlyActors==1&&backend.GetPerformanceStats().MainCommands==0&&backend.GetHighlightStats().Draws>highlightBefore.Draws,"size-culled actor retains its separate stock highlight-only draw identity");
        scene.Process(new MyRenderMessageUpdateModelHighlight{ID=41,Thickness=0});scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=41,Visible=false});
        scene.Process(new MyRenderMessageCreateManualCullObject{ID=201,DebugName="size parent",WorldMatrix=MatrixD.Identity});scene.Process(new MyRenderMessageCreateRenderEntity{ID=42,Model=armor,DebugName="stock size parented actor",WorldMatrix=MatrixD.CreateTranslation(0,0,-500),Flags=RenderFlags.ForceOldPipeline|RenderFlags.Visible});
        scene.Process(new MyRenderMessageSetParentCullObject{ID=42,CullObjectID=201,ChildToParent=Matrix.CreateTranslation(0,0,-500)});Render(true);Check(scene.GetActorSizeCullStats().MainCulledActors==1,"parented actor uses authoritative local extent and bounds distance instead of stale volume cache");
        scene.Process(new MyRenderMessageSetParentCullObject{ID=42,CullObjectID=201,ChildToParent=Matrix.CreateTranslation(0,0,-5)});Render(true);Check(scene.GetActorSizeCullStats().MainCulledActors==0&&backend.GetPerformanceStats().MainCommands>0,"parented actor reentry resumes normal model preparation");
        scene.Process(new MyRenderMessageSetParentCullObject{ID=42,CullObjectID=201,ChildToParent=Matrix.CreateTranslation(0,0,-500)});Render(true);
        scene.Process(new MyRenderMessageChangeModel{ID=42,Model=armor,Scale=100});Render(true);Check(scene.GetActorSizeCullStats().MainCulledActors==0&&backend.GetPerformanceStats().MainCommands>0,"model scale replacement recomputes size visibility from authoritative local bounds");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=42,Visible=false});scene.Process(new MyRenderMessageCreateRenderEntity{ID=420,Model=armor,DebugName="new pipeline size candidate",WorldMatrix=MatrixD.CreateTranslation(0,0,-500),Flags=RenderFlags.Visible});Render(true);
        Check(scene.GetActorSizeCullStats().MainCulledActors==0&&backend.GetPerformanceStats().MainCommands>0,"old20-pixel size rule cannot drop unclassified/new-pipeline geometry");scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=420,Visible=false});
        scene.ActorSizeCullingEnabled=false;
        var shadowType=typeof(VulkanScene).Assembly.GetType("ClientPlugin.Rendering.VulkanShadows",true)!;
        object shadowSystem=Activator.CreateInstance(shadowType,true)!;var shadowSettings=(MyShadowsSettings)shadowType.GetField("Settings")!.GetValue(shadowSystem)!;
        // Keep the fallback clip-volume tests independent of production defaults.
        shadowType.GetProperty("StockFittingEnabled")!.SetValue(shadowSystem,false);
        shadowType.GetProperty("StockSchedulingEnabled")!.SetValue(shadowSystem,false);
        shadowSettings.Data.CascadesCount=2;shadowSettings.Data.ShadowCascadeMaxDistance=100;
        MyRenderSettings shadowRender=default;shadowRender.User.ShadowQuality=MyShadowsQuality.HIGH;
        var shadowUpdate=shadowType.GetMethod("Update")!;var shadowMask=shadowType.GetMethod("CascadeMask")!;
        MatrixD shadowCamera=MatrixD.Identity;MatrixD shadowProjection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,1000);
        void FitShadows(Vector3D eye,MatrixD view){shadowUpdate.Invoke(shadowSystem,new object[]{backend,view,Vector3.Forward,shadowRender,shadowProjection,eye});}
        uint Mask(BoundingBoxD box,Vector3D eye)=>(uint)shadowMask.Invoke(shadowSystem,new object[]{box,eye})!;
        uint ExactShadowMask(BoundingBoxD box){var matrices=(MatrixD[])shadowType.GetField("worldMatrices",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(shadowSystem)!;uint mask=0;for(int c=0;c<2;c++)if(new BoundingFrustumD(matrices[c]).Contains(box)!=ContainmentType.Disjoint)mask|=1u<<c;return mask;}
        FitShadows(Vector3D.Zero,shadowCamera);var rearCaster=new BoundingBoxD(new Vector3D(-.5,-.5,4),new Vector3D(.5,.5,5));
        Check(ExactShadowMask(rearCaster)!=0&&Mask(rearCaster,Vector3D.Zero)==ExactShadowMask(rearCaster),"fitted light-space frusta retain a valid shadow caster behind the camera");
        foreach(var box in new[]{new BoundingBoxD(new Vector3D(-1,-1,-10),new Vector3D(1,1,-8)),new BoundingBoxD(new Vector3D(150,0,-20),new Vector3D(151,1,-19)),new BoundingBoxD(new Vector3D(-50,-50,-100),new Vector3D(50,50,20))})Check(Mask(box,Vector3D.Zero)==ExactShadowMask(box),"cascade membership equals actual fitted clip volumes for edge/outside/spanning bounds");
        var movedEye=new Vector3D(20,3,-5);FitShadows(movedEye,MatrixD.CreateRotationY(.5));Check(Mask(rearCaster,movedEye)==ExactShadowMask(rearCaster),"current camera fit replaces previous frame cascade membership");
        shadowSettings.ShadowCascadeFrozen[1]=true;FitShadows(movedEye+new Vector3D(1,0,0),MatrixD.Identity);Check(Mask(rearCaster,movedEye)==ExactShadowMask(rearCaster),"frozen cascade retains world-space caster volume as camera-relative uniforms change");
        shadowSettings.ShadowCascadeFrozen[1]=false;shadowSettings.Data.CascadesCount=6;shadowRender.EnableShadows=true;
        shadowType.GetProperty("StockSchedulingEnabled")!.SetValue(shadowSystem,true);
        for(int f=1;f<=32;f++){
            FitShadows(Vector3D.Zero,MatrixD.Identity);Render(true);var feedback=backend.GetShadowFeedback();
            uint expectedUpdates=f==1?63u:15u|(f%4==3?16u:0u)|(f%8==5?32u:0u);
            Check(feedback.UpdateMask==expectedUpdates,"native stock scheduled cascade mask across both frame slots frame "+f);
        }
        shadowSettings.Data.UpdateCascadesEveryFrame=true;FitShadows(Vector3D.Zero,MatrixD.Identity);Render(true);Check(backend.GetShadowFeedback().UpdateMask==63,"actual every-frame shadow setting updates all retained images");
        for(int c=0;c<6;c++)shadowSettings.ShadowCascadeFrozen[c]=true;
        FitShadows(new Vector3D(2000,0,0),MatrixD.Identity);Render(true);Check(backend.GetShadowFeedback().UpdateMask==0 && backend.GetPerformanceStats().ShadowCommands==0,"all explicitly frozen cascades skip native recording and execution");
        for(int c=0;c<6;c++)shadowSettings.ShadowCascadeFrozen[c]=false;
        shadowRender.ShadowCascadeUsageBasedSkip=true;shadowRender.EnableShadows=true;
        for(int f=0;f<25;f++){FitShadows(Vector3D.Zero,MatrixD.Identity);Render(true);}
        Check(backend.GetShadowFeedback().Completion>0,"stock GPU shadow usage feedback is read after completed submission without a wait");
        shadowRender.ShadowCascadeUsageBasedSkip=false;shadowType.GetProperty("StockFittingEnabled")!.SetValue(shadowSystem,true);
        FitShadows(Vector3D.Zero,MatrixD.Identity);Render(true);var fitMatrices=(MatrixD[])shadowType.GetField("worldMatrices",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(shadowSystem)!;
        var inside=Vector3D.Transform(new Vector3D(0,0,.5),MatrixD.Invert(fitMatrices[0]));var smallBox=new BoundingBoxD(inside-new Vector3D(.01),inside+new Vector3D(.01));
        shadowSettings.Cascades[0].SkippingSmallObjectThreshold=0;Check((Mask(smallBox,Vector3D.Zero)&1)!=0,"stock depth orientation retains geometry inside actual caster frustum");
        shadowSettings.Cascades[0].SkippingSmallObjectThreshold=100000000;Check((Mask(smallBox,Vector3D.Zero)&1)==0,"actual per-cascade projected-size setting removes small casters without main-view dependence");shadowSettings.Cascades[0].SkippingSmallObjectThreshold=0;
        Check(((float[])shadowType.GetField("ProjectionFactors")!.GetValue(shadowSystem)!)[0]>0,"stock cascade fit publishes finite positive culling projection factor");
        shadowProjection=(MatrixD)Matrix.CreatePerspectiveFovRhInfiniteComplementary((float)Math.PI/2,1,.1f);shadowProjection.M13=shadowProjection.M14-shadowProjection.M13;shadowProjection.M23=shadowProjection.M24-shadowProjection.M23;shadowProjection.M33=shadowProjection.M34-shadowProjection.M33;shadowProjection.M43=shadowProjection.M44-shadowProjection.M43;
        FitShadows(Vector3D.Zero,MatrixD.Identity);Render(true);var stockConstants=(byte[])shadowType.GetProperty("StockConstants")!.GetValue(shadowSystem)!;
        Check(Enumerable.Range(0,298).All(i=>float.IsFinite(BitConverter.ToSingle(stockConstants,i*4))),"actual infinite complementary camera produces finite stock CSM matrices/scales with stock affine corner transform");
        shadowType.GetMethod("CommitPreparedFrame")!.Invoke(shadowSystem,null);var acceptedMatrices=(MatrixD[])fitMatrices.Clone();
        FitShadows(new Vector3D(50,0,0),MatrixD.CreateRotationY(.4));shadowType.GetMethod("CancelPreparedFrame")!.Invoke(shadowSystem,null);
        Check(acceptedMatrices.SequenceEqual(fitMatrices),"failed frame acquisition restores matrices paired with retained depth images");
        FitShadows(Vector3D.Zero,MatrixD.Identity);Render(true);shadowType.GetMethod("CommitPreparedFrame")!.Invoke(shadowSystem,null);
        Check(backend.GetShadowFeedback().UpdateMask==63,"canceled updates are retried through successful publication");
        shadowType.GetProperty("StockFittingEnabled")!.SetValue(shadowSystem,false);
        shadowType.GetProperty("StockSchedulingEnabled")!.SetValue(shadowSystem,false);
        shadowRender.User.ShadowQuality=MyShadowsQuality.DISABLED;FitShadows(Vector3D.Zero,MatrixD.Identity);Check(Mask(rearCaster,Vector3D.Zero)==0,"disabled shadows do not publish stale fitted cascade masks");
        Check(VulkanScene.StockOldActorCascadeMask(63,RenderFlags.Visible|RenderFlags.CastShadows)==15,"stock old-pipeline ordinary actor retains four near cascades only");
        Check(VulkanScene.StockOldActorCascadeMask(63,RenderFlags.CastShadows|RenderFlags.DrawOutsideViewDistance)==63,"stock outside-distance actor retains fitted far-cascade memberships");
        Check(VulkanScene.StockOldActorCascadeMask(5,RenderFlags.CastShadows)==5,"stock far filter preserves existing near-frustum rejection");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=42,Visible=false});scene.Process(new MyRenderMessageCreateRenderEntity{ID=44,Model=armor,DebugName="far cascade flags",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.ForceOldPipeline|RenderFlags.Visible|RenderFlags.CastShadows});
        var cascadeField=typeof(VulkanScene).GetField("CascadeMaskFor",BindingFlags.Instance|BindingFlags.NonPublic)!;cascadeField.SetValue(scene,new Func<BoundingBoxD,Vector3D,uint>((_,_)=>63));
        scene.StockFarCascadesEnabled=true;Render(true);Check(scene.GetFarCascadePolicyStats().RemovedMemberships>0,"actual prepared actor reports removed far memberships");
        var queued=(System.Collections.IEnumerable)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;
        Check(queued.Cast<WorldPacket>().Where(p=>p.Pipeline<20).All(p=>p.CascadeMask==15),"prepared batch key and queued packet carry the stock filtered cascade mask");
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=440,Model=armor,DebugName="new pipeline candidate",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});Render(true);
        Check(queued.Cast<WorldPacket>().Any(p=>p.CascadeMask==63),"old far-cascade rule preserves unclassified/new-pipeline actor coverage");scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=440,Visible=false});
        scene.StockFarCascadesEnabled=false;Render(true);Check(queued.Cast<WorldPacket>().Where(p=>p.Pipeline<20).All(p=>p.CascadeMask==63),"disabling far policy invalidates batch hints and restores fitted mask without a pose change");
        scene.ProbeDrawDistance=0;scene.ShadowDrawDistance=100;
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=45,Model=armor,DebugName="unused rear actor",WorldMatrix=MatrixD.CreateTranslation(0,0,5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        cascadeField.SetValue(scene,new Func<BoundingBoxD,Vector3D,uint>((box,_)=>box.Center.Z>0?0u:1u));
        scene.ViewEligibilityEnabled=false;var unusedReference=Render(true);var unusedActors=backend.GetSubmissionStats().Actors;
        scene.ViewEligibilityEnabled=true;Check(Render(true).SequenceEqual(unusedReference),"no-view rejection preserves exact main pixels");
        Check(scene.GetViewEligibilityStats().SkippedActors==1 && backend.GetSubmissionStats().Actors==unusedActors-1,"no-view actor is removed before prepared descriptor replay");
        cascadeField.SetValue(scene,new Func<BoundingBoxD,Vector3D,uint>((_,_)=>1));Render(true);
        Check(scene.GetViewEligibilityStats().CascadeActors==1&&scene.GetViewEligibilityStats().SkippedActors==0,"behind-camera cascade-only actor remains submitted after membership change");
        cascadeField.SetValue(scene,new Func<BoundingBoxD,Vector3D,uint>((box,_)=>box.Center.Z>0?0u:1u));scene.ProbeDrawDistance=20;Render(true);
        Check(scene.GetViewEligibilityStats().ProbeActors==1,"probe-only actor remains submitted outside main and cascade views");scene.ProbeDrawDistance=0;
        scene.Process(new MyRenderMessageUpdateModelHighlight{ID=45,Thickness=2,OutlineColor=Color.Yellow,InstanceIndex=-1});Render(true);
        Check(scene.GetViewEligibilityStats().ConservativeActors==1&&scene.GetViewEligibilityStats().SkippedActors==0,"independent highlight preserves offscreen actor ownership");scene.Process(new MyRenderMessageUpdateModelHighlight{ID=45,Thickness=0});
        scene.RenderSettings.User.ShadowQuality=MyShadowsQuality.HIGH;
        // Production copies MyRenderSettings.Default (DrawNonMergeInstanced=true) into the scene every frame; stock
        // light culling queries dynamic renderables only under that flag, so the zeroed fixture selected no lights.
        scene.RenderSettings.DrawNonMergeInstanced=true;
        scene.Process(new MyRenderMessageCreateRenderLight{ID=2000,DebugName="view eligibility spotlight"});
        var spotData=new UpdateRenderLightData{ID=2000,Position=new Vector3D(0,0,10),CastShadows=true,SpotLightOn=true,SpotIntensity=1,SpotLight=new MySpotLightLayout{Direction=Vector3.Forward,Up=Vector3.Up,ApertureCos=.8f,Light=new MyLightLayout{Range=20,Color=Vector3.One,DiffuseFactor=1}}};
        scene.Process(new MyRenderMessageUpdateRenderLight{Data=spotData});Render(true);
        Check(scene.GetViewEligibilityStats().SpotActors==1&&scene.GetViewEligibilityStats().SkippedActors==0,"live shadowed spotlight retains the rear caster through production light messages");
        spotData.SpotLight.Direction=Vector3.Backward;scene.Process(new MyRenderMessageUpdateRenderLight{Data=spotData});Render(true);
        Check(scene.GetViewEligibilityStats().SkippedActors==1,"rotating live spotlight removes its last view consumer immediately");
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=2000});
        scene.ViewEligibilityEnabled=false;Render(true);Check(backend.GetSubmissionStats().Actors==unusedActors,"disabling view eligibility restores actor replay without pose changes");
        Check(VulkanScene.SpotSphereVisible(new Vector3(0,0,-5),Vector3.Forward,10,.8f,1),"spot-only caster within cone is retained");
        Check(!VulkanScene.SpotSphereVisible(new Vector3(0,0,5),Vector3.Forward,10,.8f,1),"spot test rejects sphere fully behind light");
        Check(VulkanScene.SpotSphereVisible(new Vector3(0,0,-11),Vector3.Forward,10,.8f,1),"spot range tangent remains conservative");
        Check(!VulkanScene.SpotSphereVisible(new Vector3(0,0,-11.01f),Vector3.Forward,10,.8f,1),"spot test rejects sphere beyond expanded range");
        Check(VulkanScene.SpotSphereVisible(new Vector3(4,0,-4),Vector3.Forward,10,.8f,1),"spot cone side retains intersecting sphere margin");
        Check(VulkanScene.SpotSphereVisible(new Vector3(100),Vector3.Forward,10,.8f,float.NaN),"unknown spotlight bound cannot remove a caster");
        Render(true);Render(true);Render(true);Check(backend.GetRecipeStats().MaterialsReused>0&&backend.GetRecipeStats().Revalidated==0,"actual scene mutation replay reaches stable native recipe references");
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=46,Model=armor,DebugName="logical batch owner",WorldMatrix=MatrixD.CreateTranslation(.1,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        var logicalReference=Render(true);backend.BeginLogicalSelectionCapture();var logicalPixels=Render(true);var logicalRows=backend.EndLogicalSelectionCapture();
        Check(logicalPixels.SequenceEqual(logicalReference),"logical selection capture preserves exact rendered pixels");
        Check(logicalRows.Length>0&&logicalRows.All(row=>row.Owner==44||row.Owner==45||row.Owner==46)&&logicalRows.Sum(row=>(long)row.Count)==queued.Cast<WorldPacket>().Sum(row=>(long)row.Count),"logical capture preserves owners and instance totals without phantom first-batch packets");
        Render(true);Check(backend.PeekLogicalSelectionCapture().Length==logicalRows.Length,"disabled logical capture leaves completed snapshot stable");
        var newState=new StockNewActorLodState();var newSettings=MyGlobalLoddingSettings.Default;newSettings.IsUpdateEnabled=true;newSettings.EnableLodSelection=false;newSettings.HisteresisRatio=.1f;newSettings.MaxTransitionInSeconds=1;newSettings.MinTransitionInSeconds=.25f;
        newState.Update(15,new[]{10f,20f},newSettings,.1f,true);Check(newState.Current==1&&newState.Previous==-1,"new pipeline starts from translation distance without an AABB shortcut");
        newState.Update(21,new[]{10f,20f},newSettings,.1f,false);Check(newState.Current==1&&newState.Previous==-1,"new pipeline hysteresis retains current level before its high boundary");
        newState.Update(23,new[]{10f,20f},newSettings,.1f,false);Check(newState.Current==2&&newState.Previous==1&&newState.Transition>0,"new pipeline transition owns the new current and old main representation");
        newState.Update(23,new[]{10f,20f},newSettings,1,false);Check(newState.Previous==-1&&newState.Transition==0,"new pipeline transition completion retires only the old main representation");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=44,Visible=false});scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=45,Visible=false});scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=46,Visible=false});
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=61,Model=wall,DebugName="new per-view LOD",WorldMatrix=MatrixD.CreateTranslation(0,0,-30),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        settings.Global.EnableLodSelection=true;settings.Global.LodSelection=0;settings.GBuffer=new MyPassLoddingSetting{LodShift=0,LodShiftVisible=0,MinLod=0};settings.Forward=new MyPassLoddingSetting{LodShift=3,LodShiftVisible=3,MinLod=3};settings.SingleDepth=new MyPassLoddingSetting{LodShift=2,LodShiftVisible=2,MinLod=0};settings.CascadeDepths=new[]{new MyPassLoddingSetting{LodShift=1,LodShiftVisible=1,MinLod=0},new MyPassLoddingSetting{LodShift=1,LodShiftVisible=1,MinLod=0}};lodMessage.Settings.CopyFrom(settings);scene.Process(lodMessage);scene.ProbeDrawDistance=100;cascadeField.SetValue(scene,new Func<BoundingBoxD,Vector3D,uint>((_,_)=>3));
        scene.NewActorViewsEnabled=false;Render(true);var allBase=Render(true);scene.NewActorViewsEnabled=true;var splitPixels=Render(true);Render(true);
        Check(splitPixels.SequenceEqual(allBase),"new per-view routing preserves exact main pixels with an explicit base main level");
        Check(scene.GetNewActorViewStats().Actors==1&&scene.GetNewActorViewStats().Representations==3,"new actor owns distinct main, depth and forward representations");
        Check(queued.Cast<WorldPacket>().Any(p=>(p.Flags&4096)==0&&BitConverter.ToUInt32(p.Constants,objectLodOffset)==255),"new actor main representation carries the stock pipeline marker");
        Check(queued.Cast<WorldPacket>().Any(p=>p.CascadeMask!=0&&(p.Flags&4096)!=0&&BitConverter.ToUInt32(p.Constants,objectLodOffset)==255),"new actor cascade representation preserves the marker with independently selected geometry");
        Check(queued.Cast<WorldPacket>().Any(p=>(p.Flags&16384)==0&&(p.Flags&4096)!=0&&p.CascadeMask==0&&BitConverter.ToUInt32(p.Constants,objectLodOffset)==255),"new actor forward representation preserves the marker with independently selected geometry");
        Check(queued.Cast<WorldPacket>().Any(p=>(p.Flags&4096)==0&&p.CascadeMask==0&&(p.Flags&512)!=0),"main-only representation cannot enter either depth consumer");
        Check(queued.Cast<WorldPacket>().Any(p=>(p.Flags&4096)!=0&&p.CascadeMask==3&&(p.Flags&0x2000000)!=0),"cascade-only representation excludes main and spot views");
        Check(queued.Cast<WorldPacket>().Any(p=>(p.Flags&16384)==0&&(p.Flags&4096)!=0&&p.CascadeMask==0&&(p.Flags&512)!=0),"forward-only representation excludes main and depth");
        scene.Process(new MyRenderMessageCreateRenderLight{ID=2001,DebugName="new per-view spotlight"});var newSpotData=new UpdateRenderLightData{ID=2001,Position=new Vector3D(0,0,-20),CastShadows=true,SpotLightOn=true,SpotIntensity=1,SpotLight=new MySpotLightLayout{Direction=Vector3.Forward,Up=Vector3.Up,ApertureCos=.8f,Light=new MyLightLayout{Range=30,Color=Vector3.One,DiffuseFactor=1}}};scene.Process(new MyRenderMessageUpdateRenderLight{Data=newSpotData});Render(true);uint newSpotCommands=backend.GetPerformanceStats().SpotCommands;Render(true);newSpotCommands+=backend.GetPerformanceStats().SpotCommands;Console.WriteLine("NEWSPOT "+JsonSerializer.Serialize(scene.GetNewActorViewStats(),new JsonSerializerOptions{IncludeFields=true})+" commands="+newSpotCommands);
        Check(scene.GetNewActorViewStats().SpotActors==1&&scene.GetNewActorViewStats().Representations==4&&newSpotCommands>0,"actual shadowed spotlight receives its independent current-depth representation");
        Check(queued.Cast<WorldPacket>().Any(p=>p.CascadeMask==0&&(p.Flags&(512|0x2000000))==0&&(p.Flags&4096)!=0),"spot-only packet remains excluded from cascades and main while spot depth draws");
        scene.RenderSettings.DrawNonMergeInstanced=false; // restore the fixture's later-section settings
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=2001});
        scene.Process(new MyRenderMessageUpdateModelHighlight{ID=61,Thickness=2,OutlineColor=Color.Yellow,InstanceIndex=-1});Render(true);
        Check(scene.GetNewActorViewStats().HighlightActors==1&&queued.Cast<WorldPacket>().Any(p=>(p.Flags&8192)!=0),"new per-view actor retains an independent authored base highlight identity");
        scene.Process(new MyRenderMessageUpdateModelHighlight{ID=61,Thickness=0});scene.Process(new MyRenderMessageChangeModel{ID=61,Model=wall,Scale=1});Render(true);Check(scene.GetNewActorViewStats().Actors==1&&scene.GetNewActorViewStats().MainActors==1,"model replacement resets new LOD ownership and restores current views without a pose change");scene.NewActorViewsEnabled=false;Check(Render(true).SequenceEqual(allBase),"disabling new per-view ownership restores legacy main pixels without a pose change");
        if(lodOnly){
            var stats=backend.GetStats();Check(stats.ValidationEnabled!=0&&stats.ValidationErrors==0,"actor LOD path has zero core/synchronization validation errors");
            File.WriteAllText(Path.Combine(output,"result-lod.json"),JsonSerializer.Serialize(new{Checks=checks,stats.ValidationErrors,Scope="Actor selection/transitions and old/new per-view main, probe, cascade, spotlight and highlight routing; excludes later bulk-model cases."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        backend.BulkModelsEnabled=true;Pair("bulk current scene preserves main pixels after LOD and model changes");backend.BulkModelsEnabled=false;
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=61,Visible=false});
        // Bulk models accept only plain/masked mesh families; Shelf_1 carries a DECAL_CUTOUT part (pipelines 22/27),
        // so the per-view bulk fixture uses a stock model whose parts are all bulk-eligible.
        // The per-material visibility cases also need two distinct materials, so take the first sorted stock large
        // cube with at least two materials whose parts are all plain or masked mesh.
        object LoadModel(string file)=>cache.GetType().GetMethod("Get")!.Invoke(cache,new object[]{file,1f})!;
        bool BulkEligible(object candidate){var parts=((System.Collections.IList)candidate.GetType().GetField("Parts")!.GetValue(candidate)!).Cast<object>().ToList();
            var techniques=parts.Select(q=>(MyMeshDrawTechnique)q.GetType().GetField("Technique")!.GetValue(q)!);var names=parts.Select(q=>(string)q.GetType().GetField("MaterialName")!.GetValue(q)!).Distinct().Count();
            return names>=2&&techniques.All(x=>x==MyMeshDrawTechnique.MESH||x==MyMeshDrawTechnique.ALPHA_MASKED);}
        string bulkModelPath=null;object bulkModel=null;
        foreach(var file in Directory.GetFiles(Path.Combine(content,"Models/Cubes/Large"),"*.mwm").OrderBy(f=>f,StringComparer.Ordinal)){var candidate=LoadModel(file);if(BulkEligible(candidate)){bulkModelPath=file;bulkModel=candidate;break;}}
        Check(bulkModel!=null,"a stock bulk-eligible multi-material fixture model exists: "+bulkModelPath);

        scene.Process(new MyRenderMessageCreateRenderEntity{ID=74,Model=bulkModelPath,DebugName="bulk per-view scene fixture",WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows|RenderFlags.MetalnessColorable});
        scene.NewActorViewsEnabled=true;scene.ProbeDrawDistance=100;cascadeField.SetValue(scene,new Func<BoundingBoxD,Vector3D,uint>((_,_)=>3));
        void BulkScenePair(string label,bool requireModels=true){
            backend.BulkModelsEnabled=false;Render(true);Render(true);backend.BeginLogicalSelectionCapture();var expected=Render(true);var logical=backend.EndLogicalSelectionCapture();
            backend.BulkModelsEnabled=true;
            for(int slot=0;slot<3;slot++){backend.BeginLogicalSelectionCapture();var actual=Render(true);var selected=backend.EndLogicalSelectionCapture();Check(expected.SequenceEqual(actual),label+" bulk scene pixels slot "+slot);if(!logical.SequenceEqual(selected))Console.WriteLine("BULKSELECTION "+JsonSerializer.Serialize(new{Label=label,Reference=logical,Actual=selected},new JsonSerializerOptions{IncludeFields=true}));Check(logical.SequenceEqual(selected),label+" ordered scene owner/LOD/part selection slot "+slot);if(requireModels)Check(backend.GetModelGroupStats().Instances>0,label+" actually uses bulk model representations slot "+slot);}
            backend.BulkModelsEnabled=false;
        }
        BulkScenePair("stock new-pipeline independent main/cascade/probe levels");
        scene.Process(new MyRenderMessageUpdateRenderEntity{ID=74,DiffuseColor=new Color(40,220,80),ColorMaskHSV=new Vector3(.2f,.15f,0),Dithering=.15f});BulkScenePair("appearance/Hsv/dither revision");
        var shelfParts=(System.Collections.IList)bulkModel.GetType().GetField("Parts")!.GetValue(bulkModel)!;string shelfMaterial=(string)shelfParts[0]!.GetType().GetField("MaterialName")!.GetValue(shelfParts[0])!;
        scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=74,MaterialName=MyStringId.GetOrCompute(shelfMaterial),DiffuseColor=Color.Red,Emissivity=2});BulkScenePair("per-material tint/emission revision");
        scene.Process(new MyRenderMessageUpdateModelProperties{ID=74,MaterialName=MyStringId.GetOrCompute(shelfMaterial),FlagsChange=new RenderFlagsChange{Remove=RenderFlags.Visible}});BulkScenePair("per-material visibility revision");
        scene.Process(new MyRenderMessageUpdateModelProperties{ID=74,MaterialName=MyStringId.GetOrCompute(shelfMaterial),FlagsChange=new RenderFlagsChange()});BulkScenePair("per-material visibility restoration");
        scene.Process(new MyRenderMessageChangeModel{ID=74,Model=bulkModelPath,Scale=1});BulkScenePair("production model replacement");
        scene.Process(new MyRenderMessageUpdateModelHighlight{ID=74,Thickness=2,OutlineColor=Color.Yellow,InstanceIndex=-1});BulkScenePair("highlight identity fallback",false);scene.Process(new MyRenderMessageUpdateModelHighlight{ID=74,Thickness=0});
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=74,Visible=false});backend.BulkModelsEnabled=true;Render(true);Check(backend.GetModelGroupStats().Instances==0,"hidden bulk actor leaves current native frame");backend.BulkModelsEnabled=false;
        Check(backend.GetStats().ValidationEnabled!=0 && backend.GetStats().ValidationErrors==0,"scene message replay has zero core/synchronization validation errors");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=backend.GetStats().ValidationErrors},new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
}
