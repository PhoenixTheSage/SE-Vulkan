using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using Sandbox.RenderDirect.ActorComponents;
using VRage;
using VRage.FileSystem;
using VRage.Library.Utils;
using VRage.Utils;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

internal static class Program
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static int Main(string[] args)
    {
        string bin = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Pass the installed game's Bin64 directory.");
        AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(bin, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
        return Run(bin);
    }

    private static int Run(string bin)
    {
        string content = Path.Combine(Path.GetDirectoryName(bin), "Content");
        string temporary = Path.Combine(Path.GetTempPath(), "SeVulkanContentSmoke-" + Guid.NewGuid().ToString("N"));
        MyFileSystem.Init(content, temporary);
        ParallelTasks.Parallel.Scheduler = new ParallelTasks.FakeTaskScheduler();
        var backend = new NativeBackend();
        var scene = new VulkanScene(backend);
        var failures = new List<string>();
        scene.ContentFailed += (path, error) => failures.Add(path + ": " + error.Message);

        string armor = Path.Combine(content, "Models", "Cubes", "large", "ArmorCenter.mwm");
        scene.Process(new MyRenderMessageCreateRenderEntity { ID = 10, Model = armor, DebugName = "CPU test armor", WorldMatrix = MatrixD.CreateTranslation(4, 5, 6), Flags = RenderFlags.Visible | RenderFlags.CastShadows });
        Require(scene.ModelCount == 1 && backend.Meshes.Count > 0, "Installed armor MWM did not upload geometry: " + string.Join("; ", failures));
        foreach (var mesh in backend.Meshes.Values) Require(mesh.Vertices.Length % 8 == 0 && mesh.Indices.Length % 3 == 0 && mesh.Indices.Length > 0, "Imported mesh layout/index topology failed.");

        scene.SetCamera(Vector3D.Zero, MyTimeSpan.Zero, 1f / 60f);
        scene.Draw(backend);
        Require(backend.Draws.Count > 0 && backend.Draws[0].World.Translation == new Vector3D(4, 5, 6), "Actor world matrix was not preserved.");
        Require(backend.Textures.Count > 0, "Installed armor DDS material textures did not upload: " + string.Join("; ", failures));
        Require(new List<byte[][]>(backend.TextureMipLevels.Values).Exists(levels => levels.Length > 1), "Installed material DDS mip chains were not preserved for GPU upload.");

        scene.Process(new MyRenderMessageCreateManualCullObject { ID = 9, WorldMatrix = MatrixD.CreateTranslation(100, 0, 0) });
        scene.Process(new MyRenderMessageSetParentCullObject { ID = 10, CullObjectID = 9, ChildToParent = Matrix.CreateTranslation(1, 2, 3) });
        backend.Draws.Clear(); scene.Draw(backend);
        Require(backend.Draws[0].World.Translation == new Vector3D(101, 2, 3), "Actor parent/local transform propagation failed.");
        // Reuse the exact production packet: proxy producers set only present
        // flags, and message Init/Close do not clear MyRenderObjectUpdateData.
        var reusedTransform = new MyRenderMessageUpdateRenderObject { ID = 10 };
        reusedTransform.Data.LocalMatrix = Matrix.CreateTranslation(2, 3, 4);
        reusedTransform.Data.HasLocalMatrix = true;
        scene.Process(reusedTransform);
        Require(!reusedTransform.Data.HasLocalMatrix && !reusedTransform.Data.HasWorldMatrix && !reusedTransform.Data.HasLocalAABB,
            "Consumed pooled transform data retained presence flags.");
        reusedTransform.ID = 9;
        reusedTransform.Data.WorldMatrix = MatrixD.CreateTranslation(200, 10, 20);
        reusedTransform.Data.HasWorldMatrix = true;
        reusedTransform.Data.HasLocalMatrix = true; // World wins if both are supplied, as in the stock manager.
        scene.Process(reusedTransform);
        backend.Draws.Clear(); scene.Draw(backend);
        Require(backend.Draws[0].World.Translation == new Vector3D(202, 13, 24), "Reused world update failed to move a cull parent and its child.");
        reusedTransform.ID = uint.MaxValue;
        reusedTransform.Data.HasLocalAABB = true;
        scene.Process(reusedTransform);
        Require(!reusedTransform.Data.HasLocalAABB, "Unknown-actor updates polluted later pooled transform packets.");
        scene.Process(new MyRenderMessageUpdateRenderObject { ID = 9, Data = new MyRenderObjectUpdateData { WorldMatrix = MatrixD.CreateTranslation(100, 0, 0), HasWorldMatrix = true } });
        scene.Process(new MyRenderMessageUpdateRenderObject { ID = 10, Data = new MyRenderObjectUpdateData { LocalMatrix = Matrix.CreateTranslation(1, 2, 3), HasLocalMatrix = true } });
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 10, Visible = false });
        backend.Draws.Clear(); scene.Draw(backend); Require(backend.Draws.Count == 0, "Hidden actor was still submitted.");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 10, Visible = true });
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 9, Visible = false });
        backend.Draws.Clear(); scene.Draw(backend); Require(backend.Draws.Count == 0, "Hidden cull parent did not suppress its child.");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 9, Visible = true });

        scene.Process(new MyRenderMessageCreateRenderLight { ID = 15, DebugName = "Parented point/spot test" });
        scene.Process(new MyRenderMessageSetParentCullObject { ID = 15, CullObjectID = 9 });
        scene.Process(new MyRenderMessageUpdateRenderLight { Data = new UpdateRenderLightData {
            ID = 15, Position = new Vector3D(2, 3, 4), PositionChanged = true, PointOffset = 1,
            PointLightOn = true, PointIntensity = 2, PointLight = new MyLightLayout { Range = 10, Color = new Vector3(1, .5f, .25f), Falloff = 1, DiffuseFactor = 3.14f, GlossFactor = 1 },
            SpotLightOn = true, SpotIntensity = 3, SpotLight = new MySpotLightLayout { Direction = Vector3.Forward, Up = Vector3.Up, ApertureCos = .8f, Light = new MyLightLayout { Range = 20, Color = Vector3.One, Falloff = 1.5f, DiffuseFactor = 3.14f, GlossFactor = 1 } } } });
        backend.Draws.Clear(); scene.Draw(backend);
        Require(backend.Lights.Length == 2 && backend.Lights[0].PositionRange[0] == 102 && backend.Lights[0].PositionRange[1] == 3 && backend.Lights[0].PositionRange[2] == 3 && backend.Lights[1].PositionRange[2] == 4,
            "Parent-local light position or point forward offset was lost.");
        Require(backend.Lights[1].DirectionCone[2] == -1 && backend.Lights[1].Flags == 3, "Spotlight world direction or source metadata was lost.");
        Require(backend.Lights[0].ColorIntensity[0] == 1 && backend.Lights[0].ColorIntensity[3] == 1 && backend.Lights[1].ColorIntensity[3] == 1,
            "Already intensity-scaled game light colors were multiplied by PointIntensity/SpotIntensity again.");
        Require(Math.Abs(BitConverter.ToSingle(BitConverter.GetBytes(backend.Lights[0].Reserved0), 0) - 3.363648f) < .0001f &&
            Math.Abs(BitConverter.ToSingle(BitConverter.GetBytes(backend.Lights[1].Reserved0), 0) - .999001f) < .0001f &&
            BitConverter.ToSingle(BitConverter.GetBytes(backend.Lights[0].Reserved1), 0) == 3.14f,
            "Source falloff remapping or diffuse factors were not preserved in light packets.");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 9, Visible = false });
        scene.Draw(backend); Require(backend.Lights.Length == 0, "Hidden cull parent did not suppress its lights.");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 9, Visible = true });
        scene.Process(new MyRenderMessageRemoveRenderObject { ID = 15 });

        var billboardMaterial = new MyTransparentMaterial(MyStringId.GetOrCompute("VulkanTestBillboard"), MyTransparentMaterialTextureType.FileTexture,
            "Textures/GUI/Blank.dds", null, 1, false, false, new Vector4(.5f), Vector4.Zero, Vector4.One, Vector4.One, false, false);
        MyTransparentMaterials.AddMaterial(billboardMaterial);
        var shared = new MySharedData();
        shared.AddPersistentBillboard(new MyBillboard { Material = billboardMaterial.Id, ParentID = 9, CustomViewProjection = -1,
            Position0 = new Vector3D(-1, 1, -5), Position1 = new Vector3D(1, 1, -5), Position2 = new Vector3D(1, -1, -5), Position3 = new Vector3D(-1, -1, -5),
            Color = new Vector4(4, .5f, .25f, .5f), ColorIntensity = 3, UVSize = Vector2.One });
        shared.BeforeRender(null);
        backend.Draws.Clear(); scene.DrawBillboards(shared);
        Require(backend.Draws.Count == 1 && backend.Draws[0].World.Translation == new Vector3D(100, 0, -5), "Parent-local billboard quad transform was lost.");
        Require(backend.Draws[0].LinearTint == new Vector4(12, 1.5f, .75f, .5f), "Shared billboard HDR tint was clamped or material color applied twice.");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 9, Visible = false });
        backend.Draws.Clear(); scene.DrawBillboards(shared); Require(backend.Draws.Count == 0, "Hidden billboard parent still submitted.");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility { ID = 9, Visible = true });
        shared.ClearPersistentBillboards();
        scene.BillboardsHelper.AddPointBillboard(billboardMaterial.Id, Vector4.One, new Vector3D(0, 0, -20), 1, 0, MyBillboard.BlendTypeEnum.AdditiveTop);
        scene.BillboardsHelper.AddPointBillboard(billboardMaterial.Id, Vector4.One, new Vector3D(0, 0, -10), 1, 0, MyBillboard.BlendTypeEnum.Standard);
        scene.BillboardsHelper.AddPointBillboard(billboardMaterial.Id, Vector4.One, new Vector3D(0, 0, -5), 1, 0, MyBillboard.BlendTypeEnum.AdditiveBottom);
        backend.Draws.Clear(); scene.DrawBillboards(null);
        Require(backend.Draws.Count == 3 && (backend.Draws[0].Flags & 8) != 0 && (backend.Draws[1].Flags & 4) != 0 && (backend.Draws[2].Flags & 2048) != 0,
            "Billboard additive-bottom/standard/additive-top ordering was lost.");
        long unsupported = scene.UnsupportedBillboards;
        for (int i = 0; i < 8; ++i)
            scene.BillboardsHelper.AddPointBillboard(billboardMaterial.Id, Vector4.One, new Vector3D(0, 0, -100 - i), 1, 0, customViewProjection: 0);
        scene.BillboardsHelper.AddPointBillboard(billboardMaterial.Id, Vector4.One, new Vector3D(0, 0, -10), 1, 0);
        backend.Draws.Clear(); scene.DrawBillboards(null);
        Require(backend.Draws.Count == 1 && scene.UnsupportedBillboards == unsupported + 8,
            "Skipped billboard packets broke later valid mesh resource slots or lost unsupported diagnostics.");
        unsupported = scene.UnsupportedBillboards;
        scene.BillboardsHelper.AddPointBillboard(billboardMaterial.Id, Vector4.One, new Vector3D(0, 0, -10), 1, 0, MyBillboard.BlendTypeEnum.PostPP);
        scene.BillboardsHelper.AddPointBillboard(billboardMaterial.Id, Vector4.One, new Vector3D(0, 0, -10), 1, 0, MyBillboard.BlendTypeEnum.LDR);
        backend.Draws.Clear(); scene.DrawBillboards(null);
        Require(backend.Draws.Count == 0 && scene.UnsupportedBillboards == unsupported + 2,
            "Pending LDR/PostPP billboards were silently substituted with standard world composition.");
        System.Threading.Tasks.Parallel.For(0, 100, i => scene.BillboardsHelper.AddPointBillboard(billboardMaterial.Id,
            Vector4.One, new Vector3D(i, 0, -10), 1, 0));
        backend.Draws.Clear(); scene.DrawBillboards(null);
        Require(backend.Draws.Count == 100, "Parallel RenderDirect billboard additions lost copied packets.");

        var cube = new MyCubeInstanceData(); cube.ResetBones(); cube.LocalMatrix = Matrix.CreateWorld(new Vector3(3, 4, 5), Vector3.Left, Vector3.Up);
        Matrix unpacked = cube.LocalMatrix;
        Require(Vector3.Distance(unpacked.Translation, new Vector3(3, 4, 5)) < .0001 && Vector3.Distance(unpacked.Forward, Vector3.Left) < .0001, "Game packed orthogonal rotation/translation failed.");
        cube.ColorMaskHSV = new Vector4(.2f, .3f, .1f, 0);
        scene.Process(new MyRenderMessageCreateRenderInstanceBuffer { ID = 11 });
        var cubes = new MyRenderMessageUpdateRenderCubeInstanceBuffer { ID = 11 }; cubes.InstanceData.Add(cube); scene.Process(cubes); cubes.InstanceData.Clear();
        scene.Process(new MyRenderMessageSetInstanceBuffer { ID = 10, InstanceBufferId = 11, InstanceCount = 1, LocalAabb = new BoundingBox(new Vector3(-10), new Vector3(10)) });
        backend.Draws.Clear(); scene.Draw(backend);
        Require(backend.Draws.Count > 0 && Vector3.Distance(backend.Draws[0].Hsv, new Vector3(.2f, 1.1f, 0)) < .0001f, "Cube instances retained pooled message list or lost HSV/default colorization offset.");
        MatrixD expected = unpacked * MatrixD.CreateTranslation(101, 2, 3);
        Require(Vector3D.Distance(backend.Draws[0].World.Translation, expected.Translation) < .001, "Cube local/actor matrix composition failed.");
        scene.Process(new MyRenderMessageUpdateRenderEnvironment { Data = new MyEnvironmentData {
            TextureMultipliers = new MyTextureDebugMultipliers { ColorizeHSV = new Vector4(.05f, .1f, .2f, 0) } } });
        backend.Draws.Clear(); scene.Draw(backend);
        Require(Vector3.Distance(backend.Draws[0].Hsv, new Vector3(.25f, .4f, .3f)) < .0001f, "Updated environment HSV offset was not applied to the raw instance paint mask.");
        scene.Process(new MyRenderMessageUpdateRenderEnvironment { Data = new MyEnvironmentData { TextureMultipliers = MyTextureDebugMultipliers.Defaults } });

        var generic = new MyInstanceData(Matrix.CreateTranslation(7, 8, 9));
        generic.ColorMaskHSV = new HalfVector4(.1f, .2f, .3f, .25f);
        scene.Process(new MyRenderMessageUpdateRenderInstanceBufferRange { ID = 11, StartOffset = 2, Trim = true, InstanceData = new[] { generic } });
        scene.Process(new MyRenderMessageSetInstanceBuffer { ID = 10, InstanceBufferId = 11, InstanceStart = 2, InstanceCount = 1, LocalAabb = new BoundingBox(new Vector3(-10), new Vector3(10)) });
        backend.Draws.Clear(); scene.Draw(backend);
        Require(Vector3D.Distance(backend.Draws[0].World.Translation, new Vector3D(108, 10, 12)) < .001, "Generic instance range/half matrix failed.");
        Require(Math.Abs(backend.Draws[0].Dithering - .25f) < .001 && Vector3.Distance(backend.Draws[0].Hsv, new Vector3(.1f, 1, .2f)) < .001,
            "Generic packed HSV/dithering was lost.");

        // A synthetic weighted vertex gives an exact translated result through the production CPU skinning path.
        var data = new MyModelData { AABB = new BoundingBox(Vector3.Zero, Vector3.One) };
        data.Positions.Add(Vector3.Zero); data.Positions.Add(Vector3.UnitX); data.Positions.Add(Vector3.UnitY);
        data.Normals.Add(Vector3.Up); data.Normals.Add(Vector3.Up); data.Normals.Add(Vector3.Up);
        data.Indices.AddRange(new[] { 0, 1, 2 });
        var modelCache = (VulkanModelCache)typeof(VulkanScene).GetField("models", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(scene);
        var lcdModel = modelCache.Get(Path.Combine(content, "Models", "Cubes", "Large", "LCDPanel.mwm"));
        Require(lcdModel != null, "Installed external LCD MWM import failed.");
        int screenParts = 0;
        foreach (var part in lcdModel.Parts)
            if (part.MaterialName.StartsWith("ScreenArea", StringComparison.Ordinal))
            {
                Vector3 normal = Vector3.Zero;
                foreach (uint index in part.Indices) { int o = checked((int)index * 8); normal += new Vector3(lcdModel.Vertices[o + 3], lcdModel.Vertices[o + 4], lcdModel.Vertices[o + 5]); }
                normal.Normalize();
                Require((part.Flags & 4) == 0 && normal.Z > .9f, "External LCD screen should use an opaque +Z-facing material.");
                screenParts++;
            }
        Require(screenParts == 4, "Installed external LCD is missing its four rotated screen-area materials.");
        scene.Process(new MyRenderMessageCreateRenderEntity { ID = 16, Model = lcdModel.Name, WorldMatrix = MatrixD.Identity, Flags = RenderFlags.Visible });
        var screens = lcdModel.Parts.FindAll(part => part.MaterialName.StartsWith("ScreenArea", StringComparison.Ordinal));
        for (int i = 1; i < screens.Count; ++i)
            scene.Process(new MyRenderMessageUpdateModelProperties { ID = 16, MaterialName = MyStringId.GetOrCompute(screens[i].MaterialName), FlagsChange = new RenderFlagsChange { Remove = RenderFlags.Visible } });
        backend.Draws.Clear(); scene.Draw(backend);
        Require(backend.Draws.FindAll(draw => screens.Exists(part => part.Mesh == draw.Mesh)).Count == 1, "External LCD hidden rotated screen materials were submitted, causing overlapping surfaces.");
        scene.Process(new MyRenderMessageUpdateModelProperties { ID = 16, MaterialName = MyStringId.GetOrCompute(screens[1].MaterialName), FlagsChange = new RenderFlagsChange() });
        backend.Draws.Clear(); scene.Draw(backend);
        Require(backend.Draws.FindAll(draw => screens.Exists(part => part.Mesh == draw.Mesh)).Count == 2, "Resetting material visibility did not restore the external LCD screen material.");
        var emissionSettings = new MyRenderMessageUpdatePostprocessSettings { Settings = new MyPostprocessSettings { Data = new MyPostprocessSettings.Layout { BloomEmissiveness = 120 } } };
        Require(!scene.Process(emissionSettings), "Partial material emission support must not report complete postprocess settings support.");
        emissionSettings.Settings.Data.BloomEmissiveness = 0;
        scene.Process(new MyRenderMessageUpdateColorEmissivity { ID = 16, MaterialName = MyStringId.GetOrCompute(screens[0].MaterialName), DiffuseColor = Color.White, Emissivity = .5f });
        backend.Draws.Clear(); scene.Draw(backend);
        Require(backend.Draws.Find(draw => draw.Mesh == screens[0].Mesh).Emissivity == 60,
            "Source material emission factor was omitted, applied twice, or retained a pooled postprocess message.");
        scene.Process(new MyRenderMessageUpdatePostprocessSettings { Settings = MyPostprocessSettings.Default });
        scene.Process(new MyRenderMessageRemoveRenderObject { ID = 16 });
        scene.Process(new MyRenderMessageCreateRenderEntity { ID = 17, Model = armor, WorldMatrix = MatrixD.Identity, Flags = RenderFlags.Visible });
        scene.Process(new MyRenderMessageSetParentCullObject { ID = 17, CullObjectID = 9, ChildToParent = Matrix.CreateTranslation(3, 2, 3) });
        var rotation = new MyRotationAnimatorInitData { RotationAxis = MyRotationAnimator.RotationAxis.AxisY, Speed = 1.5f, SpinUpSpeed = float.NaN,
            SpinDownSpeed = float.NaN, DesiredState = float.NaN, OriginalRotation = Matrix.Identity, ReveseRotationDirection = false };
        scene.Process(new MyRenderMessageUpdateComponent { ID = 17, Data = rotation });
        rotation.RotationAxis = MyRotationAnimator.RotationAxis.AxisZ; rotation.Speed = 0;
        scene.SetCamera(Vector3D.Zero, MyTimeSpan.Zero, .2f);
        long rotationUpdates = scene.RenderDirectRotationUpdates;
        backend.Draws.Clear(); scene.Draw(backend);
        var animated = backend.Draws.Find(draw => Vector3D.Distance(draw.World.Translation, new Vector3D(103, 2, 3)) < .001);
        Require(animated != null && Math.Abs(animated.World.Forward.X) > .1 && Math.Abs(animated.World.Forward.Y) < .001,
            "The actual game RenderDirect Y animator did not rotate its parented actor or retained pooled init data.");
        Require(scene.RenderDirectRotationUpdates > rotationUpdates, "Actual RenderDirect transform changes did not produce CPU animation evidence.");
        scene.Process(new MyRenderMessageRemoveRenderObject { ID = 17 });
        scene.SetCamera(Vector3D.Zero, MyTimeSpan.Zero, 1f / 60f);
        var astronaut = modelCache.Get(Path.Combine(content, "Models", "Characters", "Astronaut", "SE_astronaut.mwm"));
        Require(astronaut != null && astronaut.BlendIndices != null && astronaut.BlendWeights != null && astronaut.BlendWeights.Length == astronaut.Vertices.Length / 8,
            "Installed astronaut MWM skinning streams were not imported: " + string.Join("; ", failures));
        var imported = modelCache.PutRuntime("weighted-test", data);
        Require(imported.Vertices[8] == 1 && imported.Parts[0].Indices[2] == 2, "Runtime model geometry import failed.");
        imported.BlendIndices = new[] { new Vector4I(0, 1, 0, 0), new Vector4I(0, 1, 0, 0), new Vector4I(0, 1, 0, 0) };
        imported.BlendWeights = new[] { new Vector4(.75f, .25f, 0, 0), new Vector4(.75f, .25f, 0, 0), new Vector4(.75f, .25f, 0, 0) };
        scene.Process(new MyRenderMessageCreateRenderCharacter { ID = 12, Model = "weighted-test", WorldMatrix = MatrixD.Identity, Flags = RenderFlags.Visible });
        var hierarchy = new[] { new MySkeletonBoneDescription { SkinTransform = Matrix.Identity }, new MySkeletonBoneDescription { SkinTransform = Matrix.Identity } };
        scene.Process(new MyRenderMessageSetCharacterSkeleton { CharacterID = 12, SkeletonBones = hierarchy, SkeletonIndices = new[] { 0, 1 } });
        hierarchy[0].SkinTransform = Matrix.CreateTranslation(100, 0, 0);
        var bones = new[] { Matrix.CreateTranslation(2, 0, 0), Matrix.CreateTranslation(6, 0, 0) };
        scene.Process(new MyRenderMessageSetCharacterTransforms { CharacterID = 12, BoneAbsoluteTransforms = bones });
        bones[0] = Matrix.Identity; bones[1] = Matrix.Identity;
        backend.Draws.Clear(); scene.Draw(backend);
        var skinned = backend.Draws.Find(draw => draw.World.Translation == Vector3D.Zero);
        Require(skinned != null && Math.Abs(backend.Meshes[skinned.Mesh].Vertices[0] - 3) < .0001, "Production CPU skinning lost weighted bones or retained pooled skeleton/transforms.");
        scene.Process(new MyRenderMessageRemoveRenderObject { ID = 12 });

        scene.Process(new MyRenderMessageCreateRenderEntity { ID = 13, Model = "weighted-test", WorldMatrix = MatrixD.Identity, Flags = RenderFlags.Visible });
        var deformed = cube; deformed.EnableSkinning = true; deformed.BoneRange = 1; deformed[0] = new Vector3UByte(255, 128, 128);
        var deformedMessage = new MyRenderMessageUpdateRenderCubeInstanceBuffer { ID = 14 }; deformedMessage.InstanceData.Add(deformed); scene.Process(deformedMessage);
        scene.Process(new MyRenderMessageSetInstanceBuffer { ID = 13, InstanceBufferId = 14, InstanceCount = 1, LocalAabb = new BoundingBox(new Vector3(-10), new Vector3(10)) });
        backend.Draws.Clear(); scene.Draw(backend);
        var cubeDraw = backend.Draws.Find(draw => draw.World.Translation == new Vector3D(3, 4, 5));
        Require(cubeDraw != null, "Deformed cube did not submit a draw.");
        var deformedVertex = backend.Meshes[cubeDraw.Mesh].Vertices;
        Vector3D actualPoint = Vector3D.Transform(new Vector3D(deformedVertex[0], deformedVertex[1], deformedVertex[2]), cubeDraw.World);
        var packedIndices = new Vector4UByte(0, 1, 0, 0); var weights = new Vector4(.75f, .25f, 0, 0);
        Vector3 expectedOffset = deformed.ComputeBoneOffset(ref packedIndices, ref weights);
        Require(Vector3D.Distance(actualPoint, new Vector3D(3, 4, 5) + expectedOffset) < .001, "Cube deformation rotated grid-space packed bone offset incorrectly.");
        scene.Process(new MyRenderMessageRemoveRenderObject { ID = 13 });

        // Texture size and readiness must remain CPU-only when called from the simulation thread.
        int texturesBefore = backend.Textures.Count;
        bool metadataRead = false;
        var thread = new Thread(() => { int w, h; metadataRead = scene.TextureCache.GetSize("Textures/GUI/WhiteFlag.dds", out w, out h); scene.TextureCache.GetTexture("Textures/GUI/WhiteFlag.dds"); });
        thread.Start(); thread.Join();
        Require(backend.Textures.Count == texturesBefore, "Simulation-thread texture query performed native upload.");
        Require(metadataRead, "CPU DDS metadata query failed.");
        scene.TextureCache.DrainPendingLoads();
        Require(backend.Textures.Count > texturesBefore, "Queued simulation-thread texture request did not upload on the owner thread.");

        scene.Process(new MyRenderMessageRemoveRenderObject { ID = 10 });
        backend.Draws.Clear(); scene.Draw(backend); Require(backend.Draws.Count == 0, "Removed actor remained visible.");
        scene.Process(new MyRenderMessageUpdateRenderEnvironment { Data = new MyEnvironmentData { Skybox = "Textures/BackgroundCube/Final/BackgroundCube.dds", SkyboxOrientation = Quaternion.Identity } });
        backend.Draws.Clear(); scene.Draw(backend);
        Require(scene.SkyboxReady && scene.SkyboxDraws == 6 && backend.Draws.Count == 6, "Installed skybox cube DDS did not produce six native face draws: " + string.Join("; ", failures));
        scene.Clear();
        Require(backend.Textures.Count == 0 && backend.TextureMipLevels.Count == 0 && backend.Meshes.Count == 0 && scene.ActorCount == 0, "World unload leaked scene textures/meshes/actors.");
        Console.WriteLine("Passed installed armor/astronaut/LCD MWM and DDS materials/skybox imports, LCD surface orientation/visibility/reset, actual game RenderDirect parented rotation, actor/light/billboard parent transforms/visibility, billboard HDR tint/bucket ordering, pooled cube ownership/rotations/HSV/deformation, generic instance ranges, weighted CPU skinning, runtime geometry, owner-thread texture checks and complete resource unload.");
        return 0;
    }
}
