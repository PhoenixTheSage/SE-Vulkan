using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.FileSystem;
using VRage.Library.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using VRageRender.Import;

internal static class Program
{
    private const int Width = 256, Height = 256;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static int Main(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Pass the installed Space Engineers Bin64 path and optionally an output directory.");
        string bin = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            string path = Path.Combine(bin, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Run(bin, Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine("artifacts", "world-gpu-smoke")));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string bin, string output)
    {
        Directory.CreateDirectory(output);
        string content = Path.Combine(Path.GetDirectoryName(bin), "Content");
        MyFileSystem.Init(content, Path.Combine(output, "userdata"));
        ParallelTasks.Parallel.Scheduler = new ParallelTasks.FakeTaskScheduler();
        NativeProbe probe = NativeBackend.Probe();
        Require(probe.LoaderApi >= 0x00404000 && probe.CompatibleDeviceCount > 0, "The GPU/loader must support Vulkan 1.4.");
        Require(probe.ValidationAvailable != 0, "Install the pinned Khronos validation layer before running this GPU acceptance test.");
        using var backend = new NativeBackend(null);
        backend.Create(Width, Height, 0, true, Path.Combine(output, "pipelines"));
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(content,"Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(content,"Shaders"));
        NativeStats builtins = backend.GetStats();
        using var scene = new VulkanScene(backend);
        var contentFailures = new List<string>();
        scene.ContentFailed += (path, error) => contentFailures.Add(path + ": " + error.Message);
        var camera = new Vector3D(0, .1, 2);
        MatrixD view = MatrixD.CreateLookAt(camera, new Vector3D(0, .1, -4), Vector3D.Up);
        MatrixD projection = MatrixD.CreatePerspectiveFieldOfView(Math.PI / 3, 1, .1, 100);
        scene.Process(new MyRenderMessageSetCameraViewMatrix { CameraPosition = camera, ViewMatrix = view, ProjectionMatrix = projection });
        scene.SetCamera(camera, MyTimeSpan.Zero, 1f / 60f);
        backend.CameraPosition = camera;
        MatrixD viewAtZero = view; viewAtZero.Translation = Vector3D.Zero;
        MatrixD reversed = projection;
        // Match production renderer's source-camera conversion to Vulkan's reversed Z.
        reversed.M13 = reversed.M14 - reversed.M13; reversed.M23 = reversed.M24 - reversed.M23;
        reversed.M33 = reversed.M34 - reversed.M33; reversed.M43 = reversed.M44 - reversed.M43;
        Vector3 sun = Vector3.Normalize(new Vector3(.4f, -.7f, -.4f));
        var shadows = new VulkanShadows();
        var renderSettings = new MyRenderSettings();
        renderSettings.User.ShadowQuality = MyShadowsQuality.HIGH;
        byte[] Render(string name)
        {
            backend.BeginUploads();
            backend.BeginPacketCollection();
            scene.Draw(backend); scene.DrawBillboards(null);
            shadows.Update(backend, viewAtZero, sun, renderSettings, projection, camera);
            var light=MyEnvironmentLightData.Default;light.SunLightDirection=sun;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,viewAtZero,reversed,Width,Height,light,scene.BloomEmissiveness,scene.BackgroundOrientation),NativeBackend.MatrixValues(viewAtZero*reversed),NativeBackend.MatrixValues(MatrixD.Invert(viewAtZero*reversed)));
            backend.EndUploads();
            Require(backend.BeginFrame(viewAtZero * reversed, sun, new Vector3(2), new Vector3(.2f), Color.Black), "Headless native frame did not begin.");
            backend.RequestReadback(); backend.EndFrame();
            byte[] pixels = backend.Readback(Width, Height);
            PngWriter.Save(Path.Combine(output, name + ".png"), Width, Height, pixels);
            Require(backend.GetStats().ValidationErrors == 0, "Vulkan validation failed: " + backend.GetStats().Detail);
            return pixels;
        }
        byte[] empty = Render("empty");
        scene.Process(new MyRenderMessageCreateRenderEntity { ID = 20, DebugName = "Installed armor GPU smoke", Model = Path.Combine(content, "Models", "Cubes", "large", "ArmorCenter.mwm"),
            WorldMatrix = MatrixD.CreateTranslation(-1.6, 0, -4), Flags = RenderFlags.Visible | RenderFlags.CastShadows });
        byte[] armor = Render("armor");
        var importer = new VRageRender.Import.MyModelImporter();
        string armorPath = Path.Combine(content, "Models", "Cubes", "large", "ArmorCenter.mwm");
        importer.ImportData(armorPath, new[] { "MeshParts", "GeometryDataAsset" });
        if (importer.GetTagData().TryGetValue("GeometryDataAsset", out var geometry))
            importer.ImportData(new MyLODDescriptor { Model = (string)geometry }.GetModelAbsoluteFilePath(armorPath), new[] { "MeshParts" });
        var importedParts = (List<VRageRender.Import.MyMeshPartInfo>)importer.GetTagData()["MeshParts"];
        var changes = new Dictionary<VRage.Utils.MyStringId, MyTextureChange>();
        foreach (var part in importedParts) changes[VRage.Utils.MyStringId.GetOrCompute(part.GetMaterialName())] = new MyTextureChange { ColorMetalFileName = "Textures/GUI/Blank.dds" };
        scene.Process(new MyRenderMessageChangeMaterialTexture { RenderObjectID = 20, Changes = changes });
        byte[] changedMaterial = Render("cached-material-replacement");
        Require(ChangedPixels(armor, changedMaterial) > 100, "Texture-only replacement was ignored by a warm material cache.");
        foreach (var name in changes.Keys.ToArray()) changes[name] = new MyTextureChange();
        scene.Process(new MyRenderMessageChangeMaterialTexture { RenderObjectID = 20, Changes = changes });
        byte[] restoredMaterial = Render("cached-material-restored");
        Require(ChangedPixels(armor, restoredMaterial) == 0, "Restoring material textures did not recover the original GPU pixels.");
        Render("settled-rigid");
        long stationaryUpdates=scene.TransformUpdates;
        Require(ChangedPixels(armor,Render("stationary-rigid"))==0 && scene.TransformUpdates==stationaryUpdates,"Stationary actors changed pixels or repeated world-transform updates.");
        scene.Process(new MyRenderMessageCreateManualCullObject{ID=24,DebugName="Dirty parent regression",WorldMatrix=MatrixD.Identity});
        scene.Process(new MyRenderMessageSetParentCullObject{ID=20,CullObjectID=24,ChildToParent=Matrix.CreateTranslation(-1.6f,0,-4)});
        Require(ChangedPixels(armor,Render("parented-rigid"))==0,"Parenting changed an unchanged rigid world transform.");
        scene.Process(new MyRenderMessageUpdateRenderObject{ID=24,Data=new MyRenderObjectUpdateData{HasWorldMatrix=true,WorldMatrix=MatrixD.CreateTranslation(3.2,0,0)}});
        Require(ChangedPixels(armor,Render("moved-parent"))>100,"Moving a cull parent failed to update its child's GPU transform and spatial bound.");
        scene.Process(new MyRenderMessageUpdateRenderObject{ID=24,Data=new MyRenderObjectUpdateData{HasWorldMatrix=true,WorldMatrix=MatrixD.Identity}});
        Require(ChangedPixels(armor,Render("restored-parent"))==0,"Restoring the parent failed to recover the original pixels.");
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=24});
        Require(ChangedPixels(armor,Render("removed-parent"))==0,"Removing a parent failed to preserve the child world transform.");
        var instanceData=new[]{new VRage.MyInstanceData{LocalMatrix=Matrix.Identity}};
        scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=25});
        scene.Process(new MyRenderMessageSetInstanceBuffer{ID=20,InstanceBufferId=25,InstanceCount=1,InstanceData=instanceData,LocalAabb=new BoundingBox(new Vector3(-1.25f),new Vector3(1.25f))});
        Require(ChangedPixels(armor,Render("cached-instance"))<4,"Generic instance encoding changed an identity rigid draw.");
        scene.Process(new MyRenderMessageUpdateRenderInstanceBufferRange{ID=25,InstanceData=new[]{new VRage.MyInstanceData{LocalMatrix=Matrix.CreateTranslation(3.2f,0,0)}}});
        Require(ChangedPixels(armor,Render("instance-range-change"))>100,"An in-place instance range update reused stale encoded bytes.");
        scene.Process(new MyRenderMessageUpdateRenderInstanceBufferRange{ID=25,InstanceData=instanceData});
        Require(ChangedPixels(armor,Render("instance-range-restored"))<4,"Restored instance bytes did not recover the original pixels.");
        scene.Process(new MyRenderMessageSetInstanceBuffer{ID=20,InstanceBufferId=uint.MaxValue,LocalAabb=new BoundingBox(new Vector3(-1.25f),new Vector3(1.25f))});
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=25});
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=26,Model=armorPath,WorldMatrix=MatrixD.CreateTranslation(-1.6,0,-4),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        scene.Process(new MyRenderMessageCreateRenderLight{ID=27});
        scene.Process(new MyRenderMessageUpdateRenderLight{Data=new UpdateRenderLightData{ID=27,SpotLightOn=true,SpotIntensity=1,CastShadows=true,SpotLight=new MySpotLightLayout{ApertureCos=.7f,Light=new MyLightLayout{Range=20,Color=Vector3.One,Falloff=1,DiffuseFactor=1,GlossFactor=1}}}});
        scene.Process(new MyRenderMessageSetLightShadowIgnore{ID=27,ID2=999});
        Render("settle-compatible-actors");Render("unrelated-shadow-exclusion");
        var merged=backend.GetPerformanceStats();
        Require(merged.LogicalDraws==2 && merged.SubmittedDraws==1,"An unrelated active light exclusion disabled compatible rigid batching.");
        Render("frozen-compatible-casters");
        scene.Process(new MyRenderMessageSetLightShadowIgnore{ID=27,ID2=26});Render("changed-static-shadow-ignore");
        Require(backend.GetPerformanceStats().SpotCommands==1,"Changing a light ignore list did not rebuild its frozen static map.");
        scene.Process(new MyRenderMessageClearLightShadowIgnore{ID=27});Render("cleared-static-shadow-ignore");
        Require(backend.GetPerformanceStats().SpotCommands==1,"Clearing a light ignore list did not restore its frozen static caster.");
        void ConfigureTestSpot(uint id,bool on,Vector3D position)
        {
            scene.Process(new MyRenderMessageUpdateRenderLight{Data=new UpdateRenderLightData{ID=id,Position=position,SpotLightOn=on,SpotIntensity=1,CastShadows=true,SpotLight=new MySpotLightLayout{ApertureCos=.7f,Light=new MyLightLayout{Range=20,Color=Vector3.One,Falloff=1,DiffuseFactor=1,GlossFactor=1}}}});
        }
        scene.Process(new MyRenderMessageCreateRenderLight{ID=28});ConfigureTestSpot(27,false,Vector3D.Zero);ConfigureTestSpot(28,true,new Vector3D(2,0,0));Render("reassigned-frozen-light-slot");
        ConfigureTestSpot(28,false,new Vector3D(2,0,0));ConfigureTestSpot(27,true,Vector3D.Zero);Render("restored-light-in-reused-slot");
        Require(backend.GetPerformanceStats().SpotCommands==1,"A previously known light reused another owner's frozen spotlight map.");
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=28});Render("unchanged-frozen-light-slot");
        Require(backend.GetPerformanceStats().SpotCommands==0,"An unchanged static light rebuilt its frozen map.");
        scene.Process(new MyRenderMessageSetLightShadowIgnore{ID=27,ID2=26});Render("excluded-caster");
        Require(backend.GetPerformanceStats().SubmittedDraws==2,"An excluded caster lost its individual draw identity through batching.");
        scene.Process(new MyRenderMessageClearLightShadowIgnore{ID=27});
        scene.Process(new MyRenderMessageUpdateModelHighlight{ID=20,InstanceIndex=-1,Thickness=2,OutlineColor=Color.Green});Render("highlighted-caster");
        Require(backend.GetPerformanceStats().LogicalDraws==2 && backend.GetPerformanceStats().SubmittedDraws==2,"A highlighted actor lost its individual draw identity through batching.");
        scene.Process(new MyRenderMessageUpdateModelHighlight{ID=20,Thickness=0});
        scene.Process(new MyRenderMessageSetCharacterSkeleton{CharacterID=26,SkeletonBones=new[]{new MySkeletonBoneDescription{SkinTransform=Matrix.Identity}}});
        scene.Process(new MyRenderMessageSetCharacterTransforms{CharacterID=26,BoneAbsoluteTransforms=new[]{Matrix.Identity}});Render("static-to-animated-caster");
        scene.Process(new MyRenderMessageSetCharacterTransforms{CharacterID=26,BoneAbsoluteTransforms=new[]{Matrix.CreateRotationY(.1f)}});Render("animated-overlay-only");
        Require(backend.GetPerformanceStats().SpotCommands==1,"A bone update rebuilt frozen static spotlight geometry instead of the dynamic overlay.");
        scene.Process(new MyRenderMessageUpdateRenderObject{ID=26,Data=new MyRenderObjectUpdateData{HasWorldMatrix=true,WorldMatrix=MatrixD.CreateTranslation(-1.5,0,-4)}});Render("moving-animated-overlay");
        Require(backend.GetPerformanceStats().SpotCommands==1,"Moving an already dynamic caster rebuilt frozen static spotlight geometry.");
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=26});scene.Process(new MyRenderMessageRemoveRenderObject{ID=27});
        scene.Process(new MyRenderMessageCreateRenderCharacter { ID = 21, DebugName = "Installed astronaut GPU smoke", Model = Path.Combine(content, "Models", "Characters", "Astronaut", "SE_astronaut.mwm"),
            WorldMatrix = MatrixD.CreateTranslation(1.6, -1, -4), Flags = RenderFlags.Visible | RenderFlags.CastShadows });
        byte[] both = Render("armor-astronaut");
        int Changed(byte[] a, byte[] b)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i += 4) if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 10) count++;
            return count;
        }
        int ChangedPixels(byte[] a, byte[] b) => Changed(a, b);
        int armorPixels = Changed(empty, armor), astronautPixels = Changed(armor, both);
        Require(contentFailures.Count == 0, "Installed content failed to import: " + string.Join("; ", contentFailures));
        Require(armorPixels > 100, "Installed MWM armor geometry did not change GPU pixels.");
        Require(astronautPixels > 100, "Installed MWM astronaut geometry did not change GPU pixels.");
        // Creation alone draws the bind-pose base mesh. Supply a complete pose
        // to exercise real palette buffers before checking their lifetime.
        var skinImporter = new MyModelImporter();
        string astronautPath = Path.Combine(content, "Models", "Characters", "Astronaut", "SE_astronaut.mwm");
        skinImporter.ImportData(astronautPath, new[] { "BlendIndices", "GeometryDataAsset" });
        if (skinImporter.GetTagData().TryGetValue("GeometryDataAsset", out var skinGeometry))
            skinImporter.ImportData(new MyLODDescriptor { Model = (string)skinGeometry }.GetModelAbsoluteFilePath(astronautPath), new[] { "BlendIndices" });
        var blendIndices = (Vector4I[])skinImporter.GetTagData()["BlendIndices"];
        int boneCount = blendIndices.Max(b => Math.Max(Math.Max(b.X, b.Y), Math.Max(b.Z, b.W))) + 1;
        Require(boneCount > 0, "Installed astronaut has no skin palette indices.");
        scene.Process(new MyRenderMessageSetCharacterSkeleton { CharacterID = 21,
            SkeletonBones = Enumerable.Range(0, boneCount).Select(_ => new MySkeletonBoneDescription { SkinTransform = Matrix.Identity }).ToArray() });
        scene.Process(new MyRenderMessageSetCharacterTransforms { CharacterID = 21,
            BoneAbsoluteTransforms = Enumerable.Repeat(Matrix.Identity, boneCount).ToArray() });
        byte[] posed = Render("astronaut-identity-palette");
        Require(Changed(armor, posed) > 100 && scene.GeometryStorage.SkinVertexBytes > 0 && scene.GeometryStorage.SkinIndexBytes > 0,
            "The posed astronaut did not draw using allocated skin palette geometry.");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 20, Visible = false });
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 21, Visible = false });
        byte[] hidden = Render("hidden");
        Require(Changed(empty, hidden) == 0, "Hidden scene geometry still changed GPU pixels.");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 20, Visible = true });
        scene.Process(new MyRenderMessageCreateRenderLight { ID = 22, DebugName = "Point light GPU smoke" });
        scene.Process(new MyRenderMessageUpdateRenderLight { Data = new UpdateRenderLightData {
            ID = 22, Position = new Vector3D(-1.6, 0, 0), PositionChanged = true, PointLightOn = true, PointIntensity = 5,
            PointLight = new MyLightLayout { Range = 20, Color = new Vector3(5, 0, 0), Falloff = 1, DiffuseFactor = 3.14f, GlossFactor = 1 } } });
        byte[] lit = Render("point-light");
        int lightPixels = Changed(armor, lit);
        Require(lightPixels > 100, "Scene point-light packet did not affect native GPU lighting.");
        scene.Process(new MyRenderMessageRemoveRenderObject { ID = 22 });
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 20, Visible = false });
        scene.Process(new MyRenderMessageUpdateRenderEnvironment { Data = new MyEnvironmentData {
            Skybox = "Textures/BackgroundCube/Final/BackgroundCube.dds", SkyboxOrientation = Quaternion.Identity } });
        byte[] sky = Render("skybox");
        int skyPixels = Changed(empty, sky);
        Require(scene.SkyboxReady && skyPixels > 1000, "Installed cubemap did not render stock lighting skybox pixels.");
        Require(contentFailures.Count == 0, "Scene lighting/skybox content failed: " + string.Join("; ", contentFailures));
        NativeStats rendered = backend.GetStats();
        Require(rendered.ValidationEnabled != 0 && rendered.ValidationErrors == 0 && rendered.SubmittedFrames >= 4 && rendered.MeshDraws > 0 && rendered.UploadedBytes > 0,
            "Native counters did not show validated geometry uploads and submissions.");
        var geometryBefore=scene.GeometryStorage;
        Require(geometryBefore.UploadedBaseVertexBytes>0 && geometryBefore.ExpandedBaseVertexBytes>=geometryBefore.UploadedBaseVertexBytes && geometryBefore.SkinVertexBytes>0,"Geometry storage counters did not cover base and skin buffers.");
        scene.Clear();
        var geometryAfter=scene.GeometryStorage;
        Require(geometryAfter.ExpandedBaseVertexBytes==0 && geometryAfter.UploadedBaseVertexBytes==0 && geometryAfter.BaseIndexBytes==0 && geometryAfter.SkinVertexBytes==0 && geometryAfter.SkinIndexBytes==0,"Unloaded geometry retained logical storage counters.");
        NativeStats cleared = backend.GetStats();
        Require(cleared.Meshes == builtins.Meshes && cleared.Textures == builtins.Textures && scene.ActorCount == 0 && scene.TextureCache.Count == 0,
            $"World unload retained native scene resources beyond the built-in fallback: {cleared.Meshes - builtins.Meshes} meshes, {cleared.Textures - builtins.Textures} textures, {scene.ActorCount} actors.");
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            Device = probe.DeviceName, Api = rendered.ApiVersion, Validation = rendered.ValidationEnabled, rendered.ValidationErrors,
            Work=backend.GetWorkStats(),
            GeometryBeforeUnload=geometryBefore,GeometryAfterUnload=geometryAfter,
            rendered.SubmittedFrames, rendered.MeshDraws, rendered.UploadedBytes, ArmorChangedPixels = armorPixels, AstronautChangedPixels = astronautPixels,
            PointLightChangedPixels = lightPixels, SkyboxChangedPixels = skyPixels,
            AfterUnloadSceneMeshes = cleared.Meshes - builtins.Meshes, AfterUnloadSceneTextures = cleared.Textures - builtins.Textures,
            BuiltinTextures = builtins.Textures, Scope = "Headless production scene/native bridge; live game Creative fixture remains pending."
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields=true }));
        Console.WriteLine($"Passed real Vulkan scene bridge on {probe.DeviceName}: armor {armorPixels} pixels, astronaut {astronautPixels} pixels, point light {lightPixels} pixels, skybox {skyPixels} pixels; {rendered.MeshDraws} mesh draws, zero validation errors, zero scene resources after unload. Captures: {output}");
        return 0;
    }
}
