using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRage.Library.Utils;
using VRage.Render.Scene;
using VRage.Render.Scene.Components;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene
{
    /// <summary>
    /// The game owns message pooling; this scene copies message arrays on receipt.
    /// CPU actors and RenderDirect animators are shared with the game, GPU ownership is Vulkan-only.
    /// </summary>
    public sealed partial class VulkanScene : MyScene, IDisposable
    {
        public Func<uint,bool> HasEntityDecals {get;set;}
        private sealed class ActorData
        {
            // The box this actor last contributed to its occlusion group (incremental group bounds).
            public BoundingBoxD GroupContribution;public bool GroupContributes;
            public MyActor Actor;
            // The game actor id, cached at creation: Build never reads the MyActor (frame split, milestone 2).
            public uint Id;
            public readonly ActorFrame[] Frames={new ActorFrame(),new ActorFrame()};
            public uint CandidateStamp;
            // Dirty marks made during Apply, folded into the Build flags at the latch (VulkanSceneHandoff).
            public byte ApplyDirtyBits;
            // Apply's copy of the fields messages and the scene update write; latched into the fields below (VulkanActorFields).
            public readonly ApplyFields A=new();
            public VulkanModel Model;
            public StockOldModelIdentity OldModel;
            public bool TrackOldIdentity=true;
            public StockOldModelIdentity OldVariantModel;
            public uint OldVariantRevision;
            public StockOldModelIdentity OldLayoutModel;
            public uint OldLayoutInstance;
            public int OldLayoutStream;
            public int[] OldLodLayouts;
            public GroupVisibility OcclusionGroup;
            public VulkanModel RenderModel;
            public uint RenderLod;
            public StockActorLodState Lod=new();
            public float LodAlpha;
            public VulkanModel LodShadowModel,LodShadowNext;
            public float LodShadowAlpha;
            public readonly Dictionary<(VulkanModel,uint),ActorRepresentation> Representations=new();
            public Color Color = Color.White;
            public Vector3 Hsv;
            public float Dithering;
            public RenderFlags Flags;
            public string ConvertedOldMaterial;
            public uint InstanceBuffer = uint.MaxValue;
            public int InstanceStart, InstanceCount;
            public int LastNewSpotLod=-1;public bool Character;public bool SuppressViewHighlight,HaveNewViewCamera;public uint ExtraViewFlags;public Vector3D NewViewCamera;public readonly StockNewActorLodState NewLod=new();
            public Matrix[] StaticInstances, SkinMatrices;
            public MySkeletonBoneDescription[] Skeleton;
            public int[] SkeletonIndices;
            public ulong[] SkinnedMeshes;
            public bool SkinDirty;
            public BoundingBox? SkinnedBounds;
            public Dictionary<string, MaterialOverride> Materials = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
            public readonly Dictionary<int, ulong[]> DeformedMeshes = new Dictionary<int, ulong[]>();
            public PartFrame[] PartFrames;
            internal MainGroupKeyCache MainGroupKeys;
            public uint MaterialRevision;
            public NativeBackend.PreparedActor Submission;
            internal PassGlassEntry[] PassGlass;
            internal SkinConstantEntry[] SkinConstants;
            public uint SubmissionRevision,SubmissionType;
            public Color SubmissionColor;
            public Vector3 SubmissionHsv;
            public float SubmissionDither;
            public bool SubmissionMetal,SubmissionOpaque,SubmissionDepthGeometry,SubmissionDistanceFade;public long SubmissionTextures;
            public PackedInstances Packed;
            public int Proxy=-1;
            public BoundingBoxD Bounds;
            public bool GeometryDirty;
            public bool ModelBoundsOwned;
            public BoundingBox InstalledModelBounds;
            public MatrixD ShadowPose;
            public Vector3D ShadowCenter;
            public float ShadowRadius;
            public ulong GpuHandle,GpuFamily,GpuSource;public bool GpuOwned,GpuOld,GpuBindingDirty=true,GpuAppearanceDirty=true;public byte[] GpuSourceBytes,GpuObjectDefaults;public object GpuObjectLayout;public uint GpuType,GpuAppearanceCount;public string GpuFallbackReason;
            public bool HaveShadowPose, DynamicShadow;
            public bool SizeCulled;
            public ulong PassHandle;public PassFamily PassFamily;public bool PassOwned,PassBindingDirty=true,PassAppearanceDirty=true;
            public ulong CellHandle;public bool CellOwned;public VulkanModel[] CellModels;public NativeBackend.WorldRecipe[] CellRecipes;
        }
        private sealed class PartFrame
        {
            public NativeBackend.RigidBatchHint BatchHint;
            public NativeBackend.WorldRecipe Recipe;
            public byte[] Constants;
            public int Row0, Row1, Row2;
            public ulong Color, Normal, Extensions, Alpha;
            public uint Pipeline;
            public string ColorName, NormalName, ExtensionName, AlphaName;
            public Vector3 Tint, Hsv;
            public float Emissive, Dither;
            public bool Live;
            public uint MaterialRevision;
        }
        private struct LiveSpot { public Vector3D Position; public float Range, Cone; public Vector3 Direction; public int Index;public MatrixD WorldProjection; }
        private struct SpotMemory { public Vector3D Position,Up; public Vector3 Direction; public float Range, Aperture; public bool Valid; }
        private readonly Dictionary<uint, SpotMemory> spotByOwner = new Dictionary<uint, SpotMemory>();
        private readonly ulong[] spotOwnerAt=new ulong[64];

        private sealed class MaterialOverride
        {
            public Color? Color;
            public float Emissivity = 1;
            public MyTextureChange Textures;
            public RenderFlagsChange Flags;
        }

        private sealed class InstanceBuffer
        {
            public MyInstanceData[] Generic;
            public MyCubeInstanceData[] Cubes;
            public bool EnabledSkinning;
            public uint Parent;
            public uint Revision;
            public int OldLayout;
            public bool OldCube;
        }
        private sealed class PackedInstances
        {
            public object Source;
            public uint Revision, Type, Count;
            public int Start;
            public Vector3 Hsv;
            public byte[] Bytes;
            public bool Dithered;
        }

        private readonly NativeBackend backend;
        private readonly VulkanModelCache models;
        internal readonly StockOldResourceIdentities OldResources=new();
        public void InitializeStockOldIdentities(bool geometryArrays)=>OldResources.Initialize(geometryArrays);
        private readonly Dictionary<uint, ActorData> actors = new Dictionary<uint, ActorData>();
        private readonly Dictionary<uint, InstanceBuffer> instances = new Dictionary<uint, InstanceBuffer>();
        private readonly HashSet<uint> visible = new HashSet<uint>();
        private readonly HashSet<IMyActor> transformUpdated = new HashSet<IMyActor>();
        private readonly List<VulkanLightComponent> lights = new List<VulkanLightComponent>();
        private readonly VulkanBillboards billboards;
        private readonly VulkanSkybox skybox;
        private BoundingFrustumD frustum;
        private Vector3 colorizeHsv = new Vector3(MyTextureDebugMultipliers.Defaults.ColorizeHSV);
        private float bloomEmissiveness = 1;
        public MyRenderSettings RenderSettings;
        public MyShadowsSettings.Struct ShadowSettings=MyShadowsSettings.Struct.Default;
        public float BloomEmissiveness => bloomEmissiveness;
        public float ProbeDrawDistance,ShadowDrawDistance;
        internal System.Func<BoundingBoxD, Vector3D, uint> CascadeMaskFor;
        public MatrixD BackgroundOrientation => skybox.Orientation;
        public VulkanTextureCache TextureCache { get; }
        public int ActorCount => actors.Count;
        public int InstanceCount
        {
            get { int count = 0; foreach (var buffer in applyInstances.Values) count += buffer.Cubes?.Length ?? buffer.Generic?.Length ?? 0; return count; }
        }
        public int ModelCount => models.Count;
        public GeometryStorageStats GeometryStorage=>models.StorageStats;
        internal VulkanModel GetModel(string path)=>models.Get(path);
        public int LightCount => lights.Count;
        public long DrawnActors { get; private set; }
        public long DrawnMeshes { get; private set; }
        public long DitheredDraws { get; private set; }
        public long SkinnedUploads { get; private set; }
        // Called only for the once-per-second CPU status snapshot on the render thread: Apply-side state (the
        // Build-owned skinned bounds are a diagnostic read).
        public string CharacterStatus()
        {
            var text = new StringBuilder();
            foreach (var data in actors.Values)
            {
                if (data.A.Skeleton == null || data.A.Model == null) continue;
                var bounds = data.Actor.CalculateAabb();
                text.Append(" id=").Append(data.Id).Append(" visible=").Append(IsVisible(data.Actor))
                    .Append(" frustum=").Append(Next.Frustum == null ? "none" : Next.Frustum.Contains(bounds).ToString())
                    .Append(" world=").Append(data.Actor.WorldMatrix.Translation).Append(" skin=").Append(data.SkinnedBounds)
                    .Append(" palette=").Append(data.A.SkinMatrices?.Length ?? 0).Append(" flags=").Append(data.Flags).Append(';');
            }
            return text.ToString();
        }
        private long renderDirectRotationUpdates;
        public long RenderDirectRotationUpdates => Interlocked.Read(ref renderDirectRotationUpdates);
        public long UnsupportedMaterialDraws { get; private set; }
        public event Action<string, Exception> ContentFailed;
        public bool SceneSystemsReady { get; private set; }
        public Func<Vector3D, Vector3> CalculateGravityInPoint { get; private set; } = position => Vector3.Zero;
        public override float FadeOutTime => 0;

        public VulkanScene(NativeBackend backend)
            : base(new SceneComponentFactory(), new SceneActorFactory(), new BillboardHelper(),
                new MyDynamicAABBTreeD(Vector3.Zero), new MyDynamicAABBTreeD(Vector3.Zero),
                new MyDynamicAABBTreeD(Vector3.Zero), new MyDynamicAABBTreeD(Vector3.Zero))
        {
            this.backend = backend;
            ((SceneActorFactory)base.ActorFactory).Scene = this;
            TextureCache = new VulkanTextureCache(backend);
            models = new VulkanModelCache(backend);
            billboards = new VulkanBillboards(this, backend);
            skybox = new VulkanSkybox(backend, TextureCache);
            ((BillboardHelper)base.BillboardsHelper).Implementation = billboards;
            models.LoadFailed += (path, error) => ContentFailed?.Invoke(path, error);
            TextureCache.LoadFailed += (path, error) => ContentFailed?.Invoke(path, error);
            SceneSystemsReady = true;
        }

        public void SetCamera(Vector3D cameraPosition, MyTimeSpan drawTime, float deltaSeconds)
        {
            Environment.CameraPosition = cameraPosition;
            Environment.FrameTime = drawTime;
            Environment.LastFrameDelta = Math.Max(0, Math.Min(deltaSeconds, 0.25f));
        }

        public bool Process(MyRenderMessageBase message)
        {
            if((message.MessageClass==MyRenderMessageType.StateChangeOnce || message.MessageClass==MyRenderMessageType.StateChangeEvery) && message.MessageType!=MyRenderMessageEnum.SetCameraViewMatrix && message.MessageType!=MyRenderMessageEnum.UpdateRenderEnvironment && message.MessageType!=MyRenderMessageEnum.UpdateRenderLight)InvalidateOcclusionMessage(message);
            if(ProcessShadowExclusion(message))return true;
            if(ProcessHighlight(message))return true;
            switch (message.MessageType)
            {
                case MyRenderMessageEnum.UpdatePostprocessSettings:
                    // Stock LightDir applies this factor to surface emission before
                    // tone mapping. Copy the scalar; never retain the pooled message.
                    float emission = ((MyRenderMessageUpdatePostprocessSettings)message).Settings.Data.BloomEmissiveness;
                    if (float.IsNaN(emission) || float.IsInfinity(emission)) throw new ArgumentException("Nonfinite material emission multiplier.");
                    applyBloomEmissiveness = Math.Max(0, emission);
                    return false; // The renderer consumes the complete postprocess settings next.
                case MyRenderMessageEnum.CreateRenderEntity:
                    var create = (MyRenderMessageCreateRenderEntity)message;
                    var entity = Create(create.ID, create.DebugName, create.WorldMatrix, create.Flags);
                    if(OldResources.Initialized&&create.ForceReload&&(create.Flags&RenderFlags.ForceOldPipeline)!=0)OldResources.Remove(create.Model);
                    SetModel(entity, models.Get(create.Model, create.Rescale));
                    return true;
                case MyRenderMessageEnum.CreateRenderCharacter:
                    var character = (MyRenderMessageCreateRenderCharacter)message;
                    var ch = Create(character.ID, character.DebugName, character.WorldMatrix, character.Flags);W(ch).Character=true;
                    SetModel(ch, models.Get(character.Model));
                    // Stock MyRender11 consumes character HSV, but ignores
                    // the legacy entity DiffuseColor field.
                    W(ch).Hsv = character.ColorMaskHSV ?? Vector3.Zero;
                    return true;
                case MyRenderMessageEnum.CreateManualCullObject:
                    var manual = (MyRenderMessageCreateManualCullObject)message;
                    Create(manual.ID, manual.DebugName, manual.WorldMatrix, RenderFlags.Visible).Actor.SetRoot(true);
                    MarkMembershipDirty();
                    return true;
                case MyRenderMessageEnum.CreateStaticGroup:
                    var group = (MyRenderMessageCreateStaticGroup)message;
                    var stat = Create(group.ID, group.Model, MatrixD.CreateTranslation(group.Translation), RenderFlags.Visible | RenderFlags.CastShadows);
                    SetModel(stat, models.Get(group.Model),false);
                    W(stat).StaticInstances = group.LocalMatrices == null ? new Matrix[0] : (Matrix[])group.LocalMatrices.Clone();
                    return true;
                case MyRenderMessageEnum.UpdateRenderObject:
                    var update = (MyRenderMessageUpdateRenderObject)message;
                    // The proxy only sets fields present in this update. Its pooled
                    // Data is cleaned by the stock postponed-update manager, not
                    // by message Init/Close. Apply values synchronously and leave
                    // a clean packet for its next owner, including missing actors.
                    try
                    {
                        if (update.Data.HasWorldMatrix) update.Data.HasLocalMatrix = false;
                        ActorData updated;
                        if (actors.TryGetValue(update.ID, out updated)){if(update.Data.HasLocalAABB){updated.ModelBoundsOwned=false;if(!updated.Actor.HasLocalAabb || updated.Actor.LocalAabb!=update.Data.LocalAABB)MarkGeometry(updated);}updated.Actor.SetTransforms(update.Data);}
                    }
                    finally { update.Data.Clean(); }
                    return true;
                case MyRenderMessageEnum.UpdateRenderEntity:
                    var appearance = (MyRenderMessageUpdateRenderEntity)message;
                    ActorData colored;
                    if (actors.TryGetValue(appearance.ID, out colored))
                    {
                        DirtyNativePass(colored);
                        // MyRenderProxy also sends this message during entity
                        // creation. Stock ignores DiffuseColor here; actual
                        // material tint comes from the material update messages.
                        if (appearance.ColorMaskHSV.HasValue) W(colored).Hsv = appearance.ColorMaskHSV.Value;
                        if (appearance.Dithering.HasValue) W(colored).Dithering = appearance.Dithering.Value;
                        if (appearance.Dithering.HasValue) MarkGeometry(colored);
                    }
                    return true;
                case MyRenderMessageEnum.UpdateRenderObjectVisibility:
                    var visibility = (MyRenderMessageUpdateRenderObjectVisibility)message;
                    ActorData visibilityActor;
                    if (actors.TryGetValue(visibility.ID, out visibilityActor)){visibilityActor.Actor.SetVisibility(visibility.Visible);MarkVisibility(visibilityActor.Actor);}
                    return true;
                case MyRenderMessageEnum.SetVisibilityUpdates:
                    var visibilityUpdate = (MyRenderMessageSetVisibilityUpdates)message;
                    ActorData visibilityTarget;
                    if (actors.TryGetValue(visibilityUpdate.ID, out visibilityTarget)) visibilityTarget.Actor.SetVisibilityUpdates(visibilityUpdate.State);
                    return true;
                case MyRenderMessageEnum.SetParentCullObject:
                    var parenting = (MyRenderMessageSetParentCullObject)message;
                    ActorData child, parent;
                    if (actors.TryGetValue(parenting.ID, out child) && actors.TryGetValue(parenting.CullObjectID, out parent))
                        SetActorParent(parent.Actor, child.Actor, parenting.ChildToParent);
                    return true;
                case MyRenderMessageEnum.UpdateRenderComponent:
                    var component = (MyRenderMessageUpdateComponent)message;
                    ActorData owner;
                    if (actors.TryGetValue(component.ID, out owner)) owner.Actor.UpdateComponent(component);
                    return true;
                case MyRenderMessageEnum.RemoveRenderObject:
                    Remove(((MyRenderMessageRemoveRenderObject)message).ID);
                    return true;
                case MyRenderMessageEnum.ChangeModel:
                    var change = (MyRenderMessageChangeModel)message;
                    ActorData changed;
                    if (actors.TryGetValue(change.ID, out changed)) SetModel(changed, models.Get(change.Model, change.Scale));
                    return true;
                case MyRenderMessageEnum.SetRenderEntityData:
                    var entityData = (MyRenderMessageSetRenderEntityData)message;
                    ActorData dynamicActor;
                    if (actors.TryGetValue(entityData.ID, out dynamicActor)) SetModel(dynamicActor, models.PutRuntime("vulkan-actor-" + entityData.ID, entityData.ModelData),false);
                    return true;
                case MyRenderMessageEnum.AddRuntimeModel:
                    var runtime = (MyRenderMessageAddRuntimeModel)message;
                    if(OldResources.Initialized&&OldResources.Exists(runtime.Name))return true;
                    if(OldResources.Initialized)lock(OldResources){var identity=OldResources.Transient.CreateRuntime(runtime.Name,runtime.ModelData.Sections.Count,false);OldResources.Transient.UpdateRuntime(identity,runtime.ModelData.Sections.ConvertAll(section=>section.MaterialName));}
                    models.PutRuntime(runtime.Name, runtime.ModelData);
                    return true;
                case MyRenderMessageEnum.CreateRenderInstanceBuffer:
                    var createBuffer = (MyRenderMessageCreateRenderInstanceBuffer)message;
                    MarkInstances(createBuffer.ID);applyInstances[createBuffer.ID] = new InstanceBuffer { Parent = createBuffer.ParentID,OldCube=createBuffer.Type==MyRenderInstanceBufferType.Cube,OldLayout=OldResources.Initialized?OldResources.InstanceLayout(createBuffer.Type==MyRenderInstanceBufferType.Cube):0 };
                    return true;
                case MyRenderMessageEnum.UpdateRenderCubeInstanceBuffer:
                    var cubeUpdate = (MyRenderMessageUpdateRenderCubeInstanceBuffer)message;
                    InstanceBuffer cubeBuffer;
                    if (!applyInstances.TryGetValue(cubeUpdate.ID, out cubeBuffer)) applyInstances.Add(cubeUpdate.ID, cubeBuffer = new InstanceBuffer());
                    MarkInstances(cubeUpdate.ID);
                    cubeBuffer.Cubes = cubeUpdate.InstanceData.ToArray(); cubeBuffer.Generic = null;cubeBuffer.EnabledSkinning=false;foreach(var cube in cubeBuffer.Cubes)cubeBuffer.EnabledSkinning|=cube.EnableSkinning;
                    cubeBuffer.Revision++;
                    MarkInstanceGeometry(cubeUpdate.ID);
                    InvalidateDeformations(cubeUpdate.ID);
                    return true;
                case MyRenderMessageEnum.UpdateRenderInstanceBufferRange:
                    var range = (MyRenderMessageUpdateRenderInstanceBufferRange)message;
                    InstanceBuffer genericBuffer;
                    if (!applyInstances.TryGetValue(range.ID, out genericBuffer)) applyInstances.Add(range.ID, genericBuffer = new InstanceBuffer());
                    MarkInstances(range.ID);
                    int length = checked(range.StartOffset + (range.InstanceData?.Length ?? 0));
                    if (range.StartOffset < 0) throw new ArgumentOutOfRangeException(nameof(range.StartOffset));
                    if (genericBuffer.Generic == null || genericBuffer.Generic.Length < length || range.Trim)
                        Array.Resize(ref genericBuffer.Generic, length);
                    if (range.InstanceData != null) Array.Copy(range.InstanceData, 0, genericBuffer.Generic, range.StartOffset, range.InstanceData.Length);
                    genericBuffer.Cubes = null;genericBuffer.EnabledSkinning=false;
                    genericBuffer.Revision++;
                    MarkInstanceGeometry(range.ID);
                    return true;
                case MyRenderMessageEnum.SetInstanceBuffer:
                    var set = (MyRenderMessageSetInstanceBuffer)message;
                    ActorData instanceActor;
                    if (actors.TryGetValue(set.ID, out instanceActor))
                    {
                        var instanceFields=W(instanceActor);instanceFields.InstanceBuffer = set.InstanceBufferId;
                        instanceFields.Events|=EventEnsureOld;
                        instanceFields.InstanceStart = set.InstanceStart;
                        instanceFields.InstanceCount = set.InstanceCount;
                        instanceActor.ModelBoundsOwned=false;
                        instanceActor.Actor.SetLocalAabb(set.LocalAabb);
                        MarkGeometry(instanceActor);
                        if (set.InstanceData != null)
                        {
                            InstanceBuffer b;
                            if (!applyInstances.TryGetValue(set.InstanceBufferId, out b)) applyInstances.Add(set.InstanceBufferId, b = new InstanceBuffer());
                            MarkInstances(set.InstanceBufferId);
                            b.Generic = (MyInstanceData[])set.InstanceData.Clone();
                            b.Cubes = null; b.Revision++;
                        }
                    }
                    return true;
                case MyRenderMessageEnum.SetCharacterSkeleton:
                    var skeleton = (MyRenderMessageSetCharacterSkeleton)message;
                    ActorData skeletonActor;
                    if (actors.TryGetValue(skeleton.CharacterID, out skeletonActor))
                    {
                        var skeletonFields=W(skeletonActor);
                        skeletonFields.Skeleton = skeleton.SkeletonBones == null ? null : (MySkeletonBoneDescription[])skeleton.SkeletonBones.Clone();
                        skeletonFields.SkeletonIndices = skeleton.SkeletonIndices == null ? null : (int[])skeleton.SkeletonIndices.Clone();
                    }
                    return true;
                case MyRenderMessageEnum.SetCharacterTransforms:
                    var transforms = (MyRenderMessageSetCharacterTransforms)message;
                    ActorData skin;
                    if (actors.TryGetValue(transforms.CharacterID, out skin) && skin.A.Skeleton != null && transforms.BoneAbsoluteTransforms != null)
                    {
                        var skinFields=W(skin);
                        bool wasSkinned=skinFields.SkinMatrices!=null;
                        int[] map = skinFields.SkeletonIndices;
                        int count = map?.Length ?? skinFields.Skeleton.Length;
                        // A fresh array each update: Build keeps reading the one it latched.
                        var matrices = new Matrix[count];
                        for (int i = 0; i < count; ++i)
                        {
                            int index = map == null ? i : map[i];
                            matrices[i] = index >= 0 && index < skinFields.Skeleton.Length && index < transforms.BoneAbsoluteTransforms.Length
                                ? skinFields.Skeleton[index].SkinTransform * transforms.BoneAbsoluteTransforms[index] : Matrix.Identity;
                        }
                        skinFields.SkinMatrices = matrices;
                        skinFields.SkinDirty = true;
                        skinnedActors.Add(skin);if(!wasSkinned)MarkGeometry(skin);
                    }
                    return true;
                case MyRenderMessageEnum.UpdateColorEmissivity:
                    var color = (MyRenderMessageUpdateColorEmissivity)message;
                    ActorData colorActor;
                    if (actors.TryGetValue(color.ID, out colorActor)) { var material = Material(colorActor, color.MaterialName.String); if(material.Color!=color.DiffuseColor || material.Emissivity!=color.Emissivity){DirtyNativePass(colorActor);colorActor.A.MaterialRevision++;material.Color = color.DiffuseColor; material.Emissivity = color.Emissivity;} }
                    return true;
                case MyRenderMessageEnum.UpdateModelProperties:
                    var properties = (MyRenderMessageUpdateModelProperties)message;
                    ActorData propertiesActor;
                    if (actors.TryGetValue(properties.ID, out propertiesActor))
                    {
                        // Installed DX11 converts a used-material flags consumer
                        // permanently to its old pipeline, even for an empty flag
                        // value. Keep that ownership independent of later resets.
                        if(properties.FlagsChange.HasValue&&!KnownStockOldPipelineApply(propertiesActor)&&models.IsStockMaterialUsed(propertiesActor.A.Model,properties.MaterialName.String)){
                            var propertiesFields=W(propertiesActor);
                            propertiesFields.ConvertedOldMaterial=properties.MaterialName.String??string.Empty;
                            propertiesFields.Events|=EventEnsureOld|EventResetLod;MarkGeometry(propertiesActor);
                        }
                        var material = Material(propertiesActor, properties.MaterialName.String);
                        if (properties.DiffuseColor.HasValue && material.Color!=properties.DiffuseColor){DirtyNativePass(propertiesActor);propertiesActor.A.MaterialRevision++;material.Color = properties.DiffuseColor;}
                        if (properties.Emissivity.HasValue && material.Emissivity!=properties.Emissivity.Value){DirtyNativePass(propertiesActor);propertiesActor.A.MaterialRevision++;material.Emissivity = properties.Emissivity.Value;}
                        if (properties.FlagsChange.HasValue && (material.Flags.Add!=properties.FlagsChange.Value.Add || material.Flags.Remove!=properties.FlagsChange.Value.Remove)){DirtyNativePass(propertiesActor);propertiesActor.A.MaterialRevision++;material.Flags = properties.FlagsChange.Value;MarkGeometry(propertiesActor);}
                    }
                    return true;
                case MyRenderMessageEnum.ChangeMaterialTexture:
                    var replacement = (MyRenderMessageChangeMaterialTexture)message;
                    ActorData replacementActor;
                    if (actors.TryGetValue(replacement.RenderObjectID, out replacementActor) && replacement.Changes != null)
                        foreach (var item in replacement.Changes){var material=Material(replacementActor,item.Key.String);var old=material.Textures;var value=item.Value;if(old.ColorMetalFileName==value.ColorMetalFileName && old.NormalGlossFileName==value.NormalGlossFileName && old.ExtensionsFileName==value.ExtensionsFileName && old.AlphamaskFileName==value.AlphamaskFileName)continue;DirtyNativePass(replacementActor,true);replacementActor.A.MaterialRevision++;material.Textures=value;Updater.AddToNextUpdate(replacementActor.Actor);if(old.AlphamaskFileName!=value.AlphamaskFileName)MarkGeometry(replacementActor);}
                    return true;
                case MyRenderMessageEnum.CreateRenderLight:
                    var light = (MyRenderMessageCreateRenderLight)message;
                    var lightActor = Create(light.ID, light.DebugName, MatrixD.Identity, RenderFlags.Visible);
                    AttachLight(lightActor.Actor);
                    return true;
                case MyRenderMessageEnum.UpdateRenderLight:
                    var lightUpdate = (MyRenderMessageUpdateRenderLight)message;
                    ActorData lightOwner;
                    if (actors.TryGetValue(lightUpdate.Data.ID, out lightOwner))
                    {
                        VulkanLightComponent componentLight = lightOwner.Actor.GetComponent<MyLightComponent>() as VulkanLightComponent;
                        componentLight?.UpdateData(ref lightUpdate.Data);
                    }
                    return true;
                case MyRenderMessageEnum.PreloadModel:
                    var preload = (MyRenderMessagePreloadModel)message;if(OldResources.Initialized&&preload.ForceOldPipeline)lock(OldResources)OldResources.Get(preload.Name);models.Get(preload.Name, preload.Rescale); return true;
                case MyRenderMessageEnum.CreateRenderVoxelDebris:
                    // This existing pending visual path still consumes OLD
                    // file identities in stock. Preserve its source clock even
                    // while the outer dispatcher reports missing debris draw.
                    var debris=(MyRenderMessageCreateRenderVoxelDebris)message;if(OldResources.Initialized&&debris.Model!=null)lock(OldResources)OldResources.Get(debris.Model);return false;
                case MyRenderMessageEnum.PreloadModels:
                    var preloadMany=(MyRenderMessagePreloadModels)message;foreach (string name in preloadMany.Models){if(OldResources.Initialized&&!preloadMany.ForInstancedComponent)lock(OldResources)OldResources.Get(name);models.Get(name);} return true;
                case MyRenderMessageEnum.PreloadMaterials:
                    string materialModel=((MyRenderMessagePreloadMaterials)message).Name;if(OldResources.Initialized)lock(OldResources)OldResources.Get(materialModel);models.Get(materialModel); return true;
                case MyRenderMessageEnum.PreloadTextures:
                    var files = ((MyRenderMessagePreloadTextures)message).Files;
                    if (files != null) foreach (string file in files) TextureCache.GetTexture(file); return true;
                case MyRenderMessageEnum.UnloadTexture:
                    TextureCache.Unload(((MyRenderMessageUnloadTexture)message).Texture); return true;
                case MyRenderMessageEnum.UpdateNewLoddingSettings:
                    applyLodSettings.CopyFrom(((MyRenderMessageUpdateNewLoddingSettings)message).Settings);lodSettingsChanged=true;return false;
                case MyRenderMessageEnum.SetCameraViewMatrix:
                    var camera = (MyRenderMessageSetCameraViewMatrix)message;
                    Next.Frustum = new BoundingFrustumD(camera.ViewMatrix * camera.ProjectionMatrix);Next.LightFarFrustum=new BoundingFrustumD(camera.ViewMatrix*camera.ProjectionFarMatrix);
                    Environment.CameraPosition = camera.CameraPosition;
                    Next.LodFov=camera.FOV;
                    Next.SizeFallbackProjection=camera.ProjectionMatrix;Next.SizeFov=camera.FOV;Next.SizeNear=camera.NearPlane;Next.SizeOffsetX=camera.ProjectionOffsetX;Next.SizeOffsetY=camera.ProjectionOffsetY;Next.HaveSizeProjection=true;
                    Next.GroupNear=camera.NearPlane;
                    return true;
                case MyRenderMessageEnum.UpdateRenderEnvironment:
                    var environment = (MyRenderMessageUpdateRenderEnvironment)message;
                    pendingSkybox = (environment.Data.Skybox, environment.Data.SkyboxOrientation);
                    var updatedHsv=new Vector3(environment.Data.TextureMultipliers.ColorizeHSV);if(updatedHsv!=applyColorizeHsv){applyColorizeHsv=updatedHsv;foreach(var actor in actors.Values)DirtyNativePass(actor);}
                    return true;
                case MyRenderMessageEnum.InitSubsystemConsume:
                    SceneSystemsReady = true; // Resources are created directly; no DX11 background manager batch exists.
                    return true;
                case MyRenderMessageEnum.SetGravityProvider:
                    CalculateGravityInPoint = ((MyRenderMessageSetGravityProvider)message).CalculateGravityInPoint ?? (position => Vector3.Zero);
                    return true;
                default: return false;
            }
        }

        private ActorData Create(uint id, string name, MatrixD world, RenderFlags flags)
        {
            Remove(id);
            var actor = new MyActor(this);
            actor.Construct(name ?? "Vulkan actor " + id); actor.SetID(id); actor.SetMatrix(ref world);
            actor.OnRotationChanged += angle => Interlocked.Increment(ref renderDirectRotationUpdates);
            actor.SetVisibility((flags & RenderFlags.Visible) != 0);
            var data = new ActorData { Actor = actor, Id = id, Flags = flags };NoteActorFrame(data);
            actors.Add(id, data);LogActor(id,data);
            var observer=new ResidentActorObserver(this);observer.Construct();actor.AddComponent<ResidentActorObserver>(observer);
            MarkMembershipDirty();
            TrackTransforms(actor);
            return data;
        }

        private void SetModel(ActorData actor, VulkanModel model,bool trackOldIdentity=true)
        {
            // Native ownership is Build state: queued for Build's prologue (VulkanSceneHandoff).
            if(building){RemoveNativePassActor(actor,false,markSpots:false);ReleaseCell(actor,false);ReleaseDynamicMeshes(actor);}else buildOps.Add(new BuildOp{Kind=BuildOpKind.ReleaseOwnership,Data=actor});
            var fields=W(actor);
            fields.Model = model;
            // The LOD, submission and old-identity state is Build's: reset (and the identity resolved) at the latch.
            fields.TrackOldIdentity=trackOldIdentity;fields.Events|=EventResetModel;if(trackOldIdentity)fields.Events|=EventEnsureOld;
            MarkMembershipDirty();
            if (model != null){actor.ModelBoundsOwned=true;actor.InstalledModelBounds=StockLodBoundsEnabled&&!KnownStockOldPipelineApply(actor)&&fields.StaticInstances==null&&fields.SkinMatrices==null?models.StockNewCullingBounds(model):model.Bounds;actor.Actor.SetLocalAabb(actor.InstalledModelBounds);}
            fields.SkinDirty = true;
            MarkGeometry(actor);
        }

        // Stock instanced streams pass key color/dither through vertices, but
        // leave pixel color_mul at one. Rigid batching selects a generic shader
        // only after packing its original rigid tint, so keep that tint here.
        private static Vector3 MaterialTint(uint sourceType, Color color) => sourceType >= 2 ? Vector3.One : color.ToVector3();

        private MaterialOverride Material(ActorData actor, string name)
        {
            // Callers invalidate material constants only for changed values.
            // Color/emission do not change caster geometry or its spatial bound.
            name = name ?? string.Empty;
            MaterialOverride value;
            var materials=W(actor).Materials;
            if (!materials.TryGetValue(name, out value)) materials.Add(name, value = new MaterialOverride());
            return value;
        }

        long profileCullTicks, profilePackTicks, profileMaterialTicks;
        [ThreadStatic] static float[] boneScratch,boneRowScratch;
        // One frame on one thread (tests and callers without the Apply/Build split). Settings written straight into the
        // Build fields carry into Next first, so the latch keeps them.
        public void Draw(NativeBackend target)
        {
            Next.RenderSettings=RenderSettings;Next.ShadowSettings=ShadowSettings;Next.ProbeDrawDistance=ProbeDrawDistance;Next.ShadowDrawDistance=ShadowDrawDistance;Next.HighlightSepia=HighlightSepia;Next.ExternalDepthRevision=ExternalDepthRevision;
            UpdateAndBuild(target);
        }
        public void UpdateAndBuild(NativeBackend target){UpdateScene();LatchFrame();BeginBuild();BuildScene(target);}
        // Apply: the scene update. Writes actor state and the Apply light frame; no native scene calls.
        public void UpdateScene()
        {
            building=false;
            using var sceneProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Scene);
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.TextureLoads))TextureCache.DrainPendingLoads();
            FrameCounter++;
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.TransformsBefore))UpdateWorldTransforms();
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ActorUpdater))Updater.Update();
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.TransformsAfter))UpdateWorldTransforms();
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.SpatialIndex))RefreshSpatialIndex();
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.LightsAndExclusions)){SelectFrameLights();PrepareExcludedActors();}
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ShadowPoses))RefreshShadowPoses();
            SnapshotActors();SnapshotLights();
            FrameProfiler.Lap(FrameProfiler.Section.SceneUpdate);
        }
        public void BeginBuild()=>building=true;
        // Build: native light submission, skybox, occlusion, native commits and the actor traversal of the latched frame.
        public void BuildScene(NativeBackend target)
        {
            using var sceneProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Scene);
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.BuildOps))FlushBuildOps();
            sizeCullStats=new ActorSizeCullStats{Enabled=ActorSizeCullingEnabled?1u:0u};
            newViewStats=new NewActorViewStats{Enabled=NewActorViewsEnabled?1u:0u};
            viewEligibilityStats=new ViewEligibilityStats{Enabled=ViewEligibilityEnabled?1u:0u};
            farCascadeStats=new FarCascadePolicyStats{Enabled=StockFarCascadesEnabled?1u:0u};
            if(ActorSizeCullingEnabled)PrepareSizeProjection(target);
            actorLodStats=new ActorLodStats{Enabled=ActorLodsEnabled?1u:0u};
            if(FramePacketVerifyEnabled){VerifyActorFrames();VerifyBuildActors();}
            RunOldDecalRequests();
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.LightsAndExclusions)){frameShadowExclusions.Clear();CommitLights(target);}
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Skybox))skybox.Draw(FrameView.CameraPosition);
            profileCullTicks = profilePackTicks = profileMaterialTicks = 0;
            long cullStarted = Stopwatch.GetTimestamp();
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Occlusion)){visible.Clear();if(target.WorldReady)BeginOcclusion();}
            if(target.WorldReady)BeginShadowGroupOcclusion();
            CommitNativePassActors();CommitCells();
            if(target.WorldReady)BeginEarlyPassPrepare(); // overlap mode: the preparation runs during the traversal
            profileCullTicks += Stopwatch.GetTimestamp() - cullStarted;
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ActorTraversal)){
            IEnumerable<ActorData> drawCandidates;using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.CandidateQuery))drawCandidates=DrawCandidates(target.WorldReady);
            var candidateList=drawCandidates as List<ActorData>;bool planned=candidateList!=null&&ParallelTraversalEnabled&&candidateList.Count>=256;
            if(planned)using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.CandidatePlan,false))PlanCandidates(candidateList);
            int candidateIndex=0;bool stableOrder=target.StableMainOrderEnabled;
            foreach (var data in drawCandidates)
            {
                // Stable main order: pass-owned actors' orders are resident in native, and cell-owned (OLD)
                // actors take no main order, so neither needs the per-frame declaration.
                if(stableOrder&&(data.PassOwned||data.CellOwned)){candidateIndex++;continue;}
                bool ancestorsVisible;object ownerKey;
                if(planned){var plan=candidatePlans[candidateIndex++];ancestorsVisible=plan.Visible;ownerKey=plan.Key;}
                else{ancestorsVisible=data.Model!=null&&BuildFrameOf(data).Visible;ownerKey=ancestorsVisible&&(data.Flags&RenderFlags.SkipInMainView)==0?MainGroupKey(data,true):null;}
                using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.MainOwnerDeclare,false))if(ownerKey!=null)target.DeclareMainOwner(data.Id,ownerKey);
                if(data.GpuOwned){if((data.Flags&RenderFlags.SkipInMainView)==0&&(frustum==null||frustum.Contains(BuildFrameOf(data).Bounds)!=ContainmentType.Disjoint))visible.Add(data.Id);continue;}if(data.PassOwned||data.CellOwned)continue;
                long actorStarted = Stopwatch.GetTimestamp();
                bool actorVisible;using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ActorAncestorVisibility,false))actorVisible=ancestorsVisible&&((data.Flags&RenderFlags.SkipInMainView)==0 || NewActorViewsEnabled);
                if (!actorVisible) { profileCullTicks += Stopwatch.GetTimestamp() - actorStarted; continue; }
                var viewProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ActorViewGate,false);
                MatrixD world = BuildFrameOf(data).World;
                BoundingBoxD bounds = BuildFrameOf(data).Bounds;
                bool probeOnly=(data.Flags&RenderFlags.SkipInMainView)!=0 || frustum != null && data.StaticInstances == null && frustum.Contains(bounds) == ContainmentType.Disjoint;
                double objectDistance=System.Math.Sqrt(Vector3D.DistanceSquared(bounds.Center,FrameView.CameraPosition));
                bool outsideProbe=ProbeDrawDistance<=0 || objectDistance>ProbeDrawDistance+bounds.HalfExtents.Length();
                viewProfile.Dispose();
                if(probeOnly && (!target.WorldReady || outsideProbe && ((data.Flags&RenderFlags.CastShadows)==0 || objectDistance>ShadowDrawDistance+bounds.HalfExtents.Length()))){ profileCullTicks += Stopwatch.GetTimestamp() - actorStarted; continue; }
                if(target.WorldReady&&CullActorByStockSize(target,data,bounds,probeOnly)){
                    if(!HasHighlight(data.Id)){profileCullTicks+=Stopwatch.GetTimestamp()-actorStarted;continue;}
                    sizeCullStats.HighlightOnlyActors++;
                }
                if(!probeOnly)visible.Add(data.Id);
                if (target.WorldReady) {
                    bool occluded;using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ActorOcclusionTest,false))occluded=!probeOnly && QueryActorVisibility(data.Id,bounds);
                    uint? selectedCascades=null;
                    if(ViewEligibilityEnabled && !RetainForAnyView(data,bounds,probeOnly,occluded,outsideProbe,out selectedCascades)){profileCullTicks+=Stopwatch.GetTimestamp()-actorStarted;continue;}
                    DrawnActors++;profileCullTicks += Stopwatch.GetTimestamp() - actorStarted;
                    DrawActorLods(target,data,world,probeOnly,occluded,outsideProbe,bounds,selectedCascades);continue;
                }
                DrawnActors++;
                profileCullTicks += Stopwatch.GetTimestamp() - actorStarted;
                if (data.StaticInstances != null)
                {
                    foreach (Matrix local in data.StaticInstances) DrawModel(target, data, local * world, null, null);
                    continue;
                }
                InstanceBuffer buffer;
                if (data.InstanceBuffer != uint.MaxValue && instances.TryGetValue(data.InstanceBuffer, out buffer))
                {
                    int available = buffer.Cubes?.Length ?? buffer.Generic?.Length ?? 0;
                    int start = Math.Max(0, data.InstanceStart), end = Math.Min(available, start + Math.Max(0, data.InstanceCount));
                    for (int i = start; i < end; ++i)
                    {
                        if (buffer.Cubes != null)
                        {
                            MyCubeInstanceData cube = buffer.Cubes[i];
                            bool customGeometry = cube.EnableSkinning || cube.GetPackedBone(5).W != 0 || cube.GetPackedBone(6).W != 0;
                            ulong[] meshes = customGeometry ? DeformCube(data, i, cube) : null;
                            float dither = cube.ColorMaskHSV.W;
                            if ((cube.GetPackedBone(3).W & 2) != 0) dither = -dither;
                            DrawModel(target, data, cube.LocalMatrix * world, new Vector3(cube.ColorMaskHSV), meshes, dither);
                        }
                        else
                        {
                            MyInstanceData instance = buffer.Generic[i];
                            Vector4 mask = instance.ColorMaskHSV.ToVector4();
                            DrawModel(target, data, instance.LocalMatrix * world, new Vector3(mask), null, mask.W);
                        }
                    }
                }
                else
                {
                    if (data.SkinMatrices != null && data.Model.BlendIndices != null && data.SkinDirty) UpdateCharacterMesh(data);
                    DrawModel(target, data, world, null, data.SkinnedMeshes);
                }
            }
            cullStarted = Stopwatch.GetTimestamp();
            if(target.WorldReady&&!nativePassActive&&!gpuActive){EndOcclusion();EndShadowGroupOcclusion();target.ShadowExclusions=frameShadowExclusions.ToArray();}
            profileCullTicks += Stopwatch.GetTimestamp() - cullStarted;
            if (target.WorldReady&&!nativePassActive&&!gpuActive) target.SetSpotRedraw(buildLights.SpotStatic, buildLights.SpotDynamic);
            }
            LogPlan2Counters();
            FrameProfiler.Deposit(FrameProfiler.Section.ActorCull, profileCullTicks);
            FrameProfiler.Deposit(FrameProfiler.Section.InstancePack, profilePackTicks);
            FrameProfiler.Deposit(FrameProfiler.Section.Materials, profileMaterialTicks);
            FrameProfiler.Advance();
        }

        private void DrawWorld(NativeBackend target, ActorData actor, MatrixD world,bool probeOnly,bool occluded,bool outsideProbe,BoundingBoxD bounds,uint? selectedCascades=null)
        {
            var setupProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.DrawWorldSetup,false);
            using var logicalOwner=target.WithLogicalOwner(actor.Id);using var packetOwner=target.WithPacketOwner(actor.Id);
            using var mainGroup=target.WithMainGroup(MainGroupKey(actor));
            long packedAt = Stopwatch.GetTimestamp();
            bool casts=(actor.Flags&(RenderFlags.CastShadows|RenderFlags.SkipInDepth))==RenderFlags.CastShadows;
            Vector3 origin=(Vector3)(bounds.Center-FrameView.CameraPosition);float radius=(float)bounds.HalfExtents.Length();
            uint cascades;using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.CascadeMembership,false))cascades=selectedCascades??(casts?(CascadeMaskFor?.Invoke(bounds,FrameView.CameraPosition)??0xFFu):0u);
            if(casts && !selectedCascades.HasValue)cascades=FilterFarActorCascades(cascades,actor);
            if(casts)cascades=FilterShadowGroupCascades(actor,cascades);
            PrepareInstances(actor,out uint type,out uint count,out byte[] stream,out bool instanceDither);
            bool dithered=actor.Dithering!=0 || instanceDither;
            profilePackTicks += Stopwatch.GetTimestamp() - packedAt;
            setupProfile.Dispose();
            if(count==0)return;
            long materialsAt = Stopwatch.GetTimestamp();
            world.Translation-=FrameView.CameraPosition;
            float? oldDecalDistance=null;bool glassDepthPolicyReady=false,oldGlassSource=false;
            bool glassOnly=false;
            if(target.RetainedSubmissionEnabled && type!=1 && (actor.SubmissionOpaque || PreparedMixedEnabled) && !HasHighlight(actor.Id) && !buildLights.Excluded.Contains(actor.Id))
            {
                using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.PreparedActors,false))
                    SubmitPreparedActor(target,actor,world,type,stream,count,casts,probeOnly,occluded,outsideProbe,cascades,origin,radius,dithered);
                if(actor.SubmissionOpaque){profileMaterialTicks+=Stopwatch.GetTimestamp()-materialsAt;return;}
                glassOnly=true; // Mixed actor: the prepared submission holds the opaque parts; glass stays per part below.
            }
            using var legacyProfile=RendererCpuProfiler.Measure(type==1?RendererCpuProfiler.Phase.SkinnedActors:type==0?RendererCpuProfiler.Phase.LegacyRigidActors:RendererCpuProfiler.Phase.LegacyInstancedActors,false);
            if(LegacyCensusEnabled&&!glassOnly)NoteLegacyActor(target,actor,type);
            int partIndex=0,skinOrdinal=0;
            foreach(var part in MaterialParts(actor))
            {
                int cachedIndex=partIndex++;target.MainPart=(uint)cachedIndex;
                using var oldOrderScope=target.WithOldOrder(OldPartOrder(actor,actor.RenderModel,part,(int)actor.RenderLod));
                bool glass=(part.Flags&4)!=0;uint transparentType=type+((part.Flags&8)!=0?5u:0u);
                if(glassOnly&&!glass)continue;
                using var partProfile=RendererCpuProfiler.Measure(glass?RendererCpuProfiler.Phase.GlassParts:RendererCpuProfiler.Phase.LegacyOpaqueParts,false);
                if(probeOnly && glass)continue; // Installed RenderForward captures atmosphere, not glass.
                MyTransparentMaterial glassMaterial=null;if(glass && !MyTransparentMaterials.TryGetMaterial(VRage.Utils.MyStringId.GetOrCompute(part.MaterialName??string.Empty),out glassMaterial)){UnsupportedMaterialDraws++;continue;}
                if(glass&&!glassDepthPolicyReady){oldGlassSource=KnownStockOldPipeline(actor)||actor.StaticInstances!=null||actor.SkinMatrices!=null||!models.IsStockNewModel(actor.Model);oldDecalDistance=oldGlassSource?(HasEntityDecals?.Invoke(actor.Id)==true?((Vector3)world.Translation).LengthSquared():float.PositiveInfinity):(float?)null;glassDepthPolicyReady=true;}
                if(glass&&!(oldGlassSource?RenderSettings.DrawTransparentModels:RenderSettings.DrawTransparentModelsInstanced))continue;
                MaterialOverride material=null;if(actor.Materials.Count!=0)actor.Materials.TryGetValue(part.MaterialName??string.Empty,out material);
                if(material!=null && ((material.Flags.Add&RenderFlags.SkipInMainView)!=0 || (material.Flags.Remove&RenderFlags.Visible)!=0))continue;
                uint pipeline=MaterialPipeline(actor,part,type);
                uint flags=((actor.Flags&(RenderFlags.CastShadows|RenderFlags.SkipInDepth))!=RenderFlags.CastShadows?512u:0u)|actor.ExtraViewFlags|MaterialRasterFlags(actor,part,material);
                if(UsesOldDecalShaders(actor))flags|=NativeBackend.OldGeometryOrder;
                if(target.SharedModelGeometryEnabled && !DepthGeometrySegments.DrawsDepth(part.Technique))flags|=512u;
                if(actor.SizeCulled)flags|=8192u; // Preserve separate stock highlight rendering, exclude normal views.
                if(probeOnly || occluded)flags|=4096u;
                if(outsideProbe)flags|=16384u;
                if(actor.DynamicShadow || actor.LodAlpha!=0)flags|=0x1000000u;
                if(actor.LodAlpha==0 && type!=1 && !glass && SubmitCachedPart(target,actor,cachedIndex,part,pipeline,flags,cascades,origin,radius,stream,count,world,material)){if(dithered)DitheredDraws++;continue;}
                ulong color=TextureCache.GetTexture(material?.Textures.ColorMetalFileName??part.ColorTexture),normal=TextureCache.GetWorldNormal(material?.Textures.NormalGlossFileName??part.NormalTexture),
                    extensions=TextureCache.GetWorldExtensions(material?.Textures.ExtensionsFileName??part.ExtensionTexture),alpha=TextureCache.GetWorldAlpha(material?.Textures.AlphamaskFileName??part.AlphaTexture);
                Vector3 tint=glass?(material?.Color??actor.Color).ToVector3():MaterialTint(type,material?.Color??actor.Color);Vector3 hsv=actor.Hsv+colorizeHsv;
                byte[] Constants(float[] bones=null)=>WorldConstants.Object(glass?target.GlassLayouts[transparentType]:target.WorldLayouts[pipeline],world,tint,hsv,material?.Emissivity??1,actor.LodAlpha,bones,actor.Dithering,!glass,actor.RenderLod);
                if(type==1)
                {
                    target.NotePreparedPart(false,true);
                    models.EnsureSkinParts(actor.RenderModel,part);
                    foreach(var skin in part.SkinParts)
                    {
                        // Every row is rewritten; Object copies the palette into the packet constants.
                        int ordinal=skinOrdinal++;
                        if(SkinConstantsCacheEnabled && !glass){
                            uint cachedDraw=target.AddWorldPacket(pipeline,skin.Mesh,color,normal,extensions,alpha,SkinConstants(actor,ordinal,part,skin,target.WorldLayouts[pipeline],world,tint,hsv,material?.Emissivity??1),null,1,flags,cascades,origin,radius);SubmitHighlight(cachedDraw,actor,part,skin.StartIndex,skin.IndexCount,1);DrawnMeshes++;continue;
                        }
                        var bones=boneScratch??=new float[60*16];var row=boneRowScratch??=new float[16];for(int i=0;i<60;++i){Matrix bone=i<skin.Bones.Length && skin.Bones[i]<actor.SkinMatrices.Length?actor.SkinMatrices[skin.Bones[i]]:Matrix.Identity;NativeBackend.MatrixValues(bone,row);Array.Copy(row,0,bones,i*16,16);}
                        if(glass)target.AddGlassPacket(transparentType,skin.Mesh,TextureCache.GetTexture(material?.Textures.ColorMetalFileName??glassMaterial.Texture),TextureCache.GetTexture(material?.Textures.NormalGlossFileName??glassMaterial.GlossTexture,false),Constants(bones),NativeBackend.GlassMaterial(glassMaterial,actor.RenderModel.Bounds.HalfExtents.X,RenderSettings),triangleFaceCulling:glassMaterial.TriangleFaceCulling,oldDecalDistanceSquared:oldDecalDistance);else {uint draw=target.AddWorldPacket(pipeline,skin.Mesh,color,normal,extensions,alpha,Constants(bones),null,1,flags,cascades,origin,radius);SubmitHighlight(draw,actor,part,skin.StartIndex,skin.IndexCount,1);}if(glass && !actor.SuppressViewHighlight && HasHighlight(actor.Id)){uint draw=target.AddWorldPacket(type,skin.Mesh,color,normal,extensions,alpha,Constants(bones),null,1,8192);SubmitHighlight(draw,actor,part,skin.StartIndex,skin.IndexCount,1);}DrawnMeshes++;
                    }
                    actor.SkinDirty=false;
                }
                else {if(glass)target.AddGlassPacket(transparentType,part.Mesh,TextureCache.GetTexture(material?.Textures.ColorMetalFileName??glassMaterial.Texture),TextureCache.GetTexture(material?.Textures.NormalGlossFileName??glassMaterial.GlossTexture,false),Constants(),NativeBackend.GlassMaterial(glassMaterial,actor.RenderModel.Bounds.HalfExtents.X,RenderSettings),stream,count,glassMaterial.TriangleFaceCulling,oldDecalDistance);else {uint draw=target.AddWorldPacket(pipeline,part.Mesh,color,normal,extensions,alpha,Constants(),stream,count,flags,cascades,origin,radius);SubmitHighlight(draw,actor,part,0,(uint)part.Indices.Length,count);}if(glass && !actor.SuppressViewHighlight && HasHighlight(actor.Id)){uint draw=target.AddWorldPacket(type,part.Mesh,color,normal,extensions,alpha,Constants(),stream,count,8192);SubmitHighlight(draw,actor,part,0,(uint)part.Indices.Length,count);}DrawnMeshes++;}
                if(dithered)DitheredDraws++;
            }
            profileMaterialTicks += Stopwatch.GetTimestamp() - materialsAt;
        }
        // Actors with glass parts (stock old-pipeline LCDs, cockpits) submit their opaque parts through the
        // prepared path; only glass keeps the per-part path. SE_VULKAN_PREPARED_MIXED=1, or the lane toggle.
        public bool PreparedMixedEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_PREPARED_MIXED")=="1";
        // Diagnostic: why actors still take the legacy per-part path (logged every 600 frames).
        static readonly bool LegacyCensusEnabled=System.Environment.GetEnvironmentVariable("SE_VULKAN_LEGACY_CENSUS")=="1";
        readonly Dictionary<string,int> legacyCensus=new();long legacyCensusFrame=-1;
        private void NoteLegacyActor(NativeBackend target,ActorData actor,uint type)
        {
            if(legacyCensusFrame<0)legacyCensusFrame=FrameCounter;
            string reason=(target.RetainedSubmissionEnabled?"":"retained-off,")+(actor.SubmissionOpaque?"":"glass,")+(HasHighlight(actor.Id)?"highlight,":"")+(buildLights.Excluded.Contains(actor.Id)?"excluded,":"")
                +(actor.Dithering!=0?"dither,":"")+(KnownStockOldPipeline(actor)?"old,":"")+(models.IsStockNewModel(actor.Model)?"":"not-new,");
            string key="type="+type+" "+reason+" parts="+actor.RenderModel.Parts.Count+" "+(actor.RenderModel.Name??"?");
            legacyCensus.TryGetValue(key,out int n);legacyCensus[key]=n+1;
            if(FrameCounter-legacyCensusFrame<600)return;
            var text=new System.Text.StringBuilder("Vulkan legacy census over 600 frames:");
            var rows=new List<KeyValuePair<string,int>>(legacyCensus);rows.Sort((a,b)=>b.Value.CompareTo(a.Value));
            foreach(var pair in rows)text.Append("\n  ").Append(pair.Value).Append("  ").Append(pair.Key);
            VRage.Utils.MyLog.Default?.WriteLine(text.ToString());legacyCensus.Clear();legacyCensusFrame=FrameCounter;
        }
        private void SubmitPreparedActor(NativeBackend target,ActorData actor,MatrixD world,uint type,byte[] stream,uint count,bool casts,bool probeOnly,bool occluded,bool outsideProbe,uint cascades,Vector3 origin,float radius,bool dithered)
        {
            Vector3 hsv=actor.Hsv+colorizeHsv;bool metal=(actor.Flags&RenderFlags.MetalnessColorable)!=0;bool distanceFade=type>=2 && UsesOldDecalShaders(actor) && (actor.Flags&RenderFlags.DistanceFade)!=0;
            bool depthGeometry=target.SharedModelGeometryEnabled && casts && type==0 && actor.RenderModel.SharedMesh!=0 && models.IsStockNewModel(actor.RenderModel);
            if(actor.Submission==null || actor.SubmissionTextures!=TextureCache.Generation || actor.SubmissionDepthGeometry!=depthGeometry || actor.SubmissionRevision!=actor.MaterialRevision || actor.SubmissionType!=type || actor.SubmissionColor!=actor.Color || actor.SubmissionHsv!=hsv || actor.SubmissionDither!=actor.Dithering || actor.SubmissionMetal!=metal || actor.SubmissionDistanceFade!=distanceFade)
            {
                using var rebuildProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.PreparedRebuild);
                var parts=new List<NativeBackend.PreparedPart>(actor.RenderModel.Parts.Count);
                foreach(var part in MaterialParts(actor))
                {
                    if((part.Flags&4)!=0)continue; // Glass is emitted per part by DrawWorld (PreparedMixedEnabled).
                    MaterialOverride material=null;if(actor.Materials.Count!=0)actor.Materials.TryGetValue(part.MaterialName??string.Empty,out material);
                    if(material!=null && ((material.Flags.Add&RenderFlags.SkipInMainView)!=0 || (material.Flags.Remove&RenderFlags.Visible)!=0))continue;
                    uint pipeline=MaterialPipeline(actor,part,type);
                    parts.Add(new NativeBackend.PreparedPart(pipeline,part.Mesh,
                        TextureCache.GetTexture(material?.Textures.ColorMetalFileName??part.ColorTexture),
                        TextureCache.GetWorldNormal(material?.Textures.NormalGlossFileName??part.NormalTexture),
                        TextureCache.GetWorldExtensions(material?.Textures.ExtensionsFileName??part.ExtensionTexture),
                        TextureCache.GetWorldAlpha(material?.Textures.AlphamaskFileName??part.AlphaTexture),
                        MaterialTint(type,material?.Color??actor.Color),hsv,material?.Emissivity??1,actor.Dithering,target.WorldLayouts[pipeline],MaterialRasterFlags(actor,part,material)|(depthGeometry?512u:0u)|(UsesOldDecalShaders(actor)?NativeBackend.OldGeometryOrder:0u),actor.RenderLod,OldPartOrder(actor,actor.RenderModel,part,(int)actor.RenderLod)));
                }
                if(depthGeometry){
                    var sourceParts=new DepthGeometryPart[actor.RenderModel.Parts.Count];
                    for(int i=0;i<sourceParts.Length;i++){var p=actor.RenderModel.Parts[i];actor.Materials.TryGetValue(p.MaterialName??string.Empty,out var m);bool visible=m==null || ((m.Flags.Add&RenderFlags.SkipInMainView)==0 && (m.Flags.Remove&RenderFlags.Visible)==0);sourceParts[i]=new DepthGeometryPart(p.IndexStart,(uint)p.Indices.Length,p.Technique,visible);}
                    foreach(var segment in DepthGeometrySegments.Build(sourceParts)){
                        var p=actor.RenderModel.Parts[segment.MaterialPart];actor.Materials.TryGetValue(p.MaterialName??string.Empty,out var m);uint pipeline=segment.Alpha?5u:0u;
                        parts.Add(new NativeBackend.PreparedPart(pipeline,models.GetDepthRange(actor.RenderModel,segment.First,segment.Count),0,TextureCache.GetWorldNormal(null),TextureCache.GetWorldExtensions(null),segment.Alpha?TextureCache.GetWorldAlpha(m?.Textures.AlphamaskFileName??p.AlphaTexture):0,Vector3.One,hsv,1,0,target.WorldLayouts[pipeline],4096u|16384u|MaterialRasterFlags(actor,p,m),actor.RenderLod));
                    }
                }
                actor.Submission=new NativeBackend.PreparedActor(parts.ToArray());actor.SubmissionRevision=actor.MaterialRevision;actor.SubmissionType=type;actor.SubmissionColor=actor.Color;actor.SubmissionHsv=hsv;actor.SubmissionDither=actor.Dithering;actor.SubmissionMetal=metal;actor.SubmissionDistanceFade=distanceFade;
                actor.SubmissionDepthGeometry=depthGeometry;actor.SubmissionTextures=TextureCache.Generation;
                target.NoteSubmissionRebuild();
            }
            if(!actor.SubmissionOpaque && actor.Submission.Parts.Length==0)return; // all-glass mixed actor
            uint flags=(casts?0u:512u)|actor.ExtraViewFlags;if(probeOnly||occluded)flags|=4096u;if(outsideProbe)flags|=16384u;if(actor.DynamicShadow || actor.LodAlpha!=0)flags|=0x1000000u;
            if(actor.SizeCulled)flags|=8192u;
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.PreparedReplay,false))target.SubmitPreparedActor(actor.Submission,world,stream,count,flags,cascades,origin,radius,actor.LodAlpha);
            DrawnMeshes+=actor.Submission.Parts.Length;if(dithered)DitheredDraws+=actor.Submission.Parts.Length;
        }
        private void PrepareInstances(ActorData actor,out uint type,out uint count,out byte[] stream,out bool dithered)
        {
            InstanceBuffer buffer=null;object source=actor.StaticInstances;int start=0;
            uint revision=0;type=0;count=1;stream=null;dithered=false;
            if(actor.StaticInstances!=null){type=2;count=(uint)actor.StaticInstances.Length;}
            else if(actor.InstanceBuffer!=uint.MaxValue && instances.TryGetValue(actor.InstanceBuffer,out buffer))
            {
                source=buffer;revision=buffer.Revision;
                int available=buffer.Cubes?.Length??buffer.Generic?.Length??0;
                start=Math.Max(0,actor.InstanceStart);count=(uint)Math.Max(0,Math.Min((long)available-start,Math.Max(0,actor.InstanceCount)));
                type=buffer.Cubes!=null?(actor.RenderModel.BlendWeights!=null && actor.RenderModel.BlendWeights.Length!=0?4u:3u):2u;
            }
            else {if(actor.SkinMatrices!=null && actor.RenderModel.BlendWeights!=null && actor.RenderModel.BlendWeights.Length!=0)type=1;return;}
            Vector3 hsv=actor.StaticInstances!=null?actor.Hsv+colorizeHsv:colorizeHsv;
            var cached=actor.Packed;
            if(cached!=null && ReferenceEquals(cached.Source,source) && cached.Revision==revision && cached.Start==start && cached.Count==count && cached.Type==type && cached.Hsv==hsv)
            {stream=cached.Bytes;dithered=cached.Dithered;return;}
            stream=new byte[checked((int)count*64)];
            for(int i=0;i<count;i++)
            {
                if(actor.StaticInstances!=null){WriteInstance(stream,i,actor.StaticInstances[i],new Vector4(hsv,0));continue;}
                if(buffer.Cubes!=null)dithered|=WriteCubeInstance(stream,i,buffer.Cubes[start+i],hsv);
                else {var instance=buffer.Generic[start+i];Vector4 mask=instance.ColorMaskHSV.ToVector4();mask.X+=hsv.X;mask.Y+=hsv.Y;mask.Z+=hsv.Z;WriteInstance(stream,i,instance.LocalMatrix,mask);dithered|=mask.W!=0;}
            }
            actor.Packed=new PackedInstances{Source=source,Revision=revision,Start=start,Count=count,Type=type,Hsv=hsv,Bytes=stream,Dithered=dithered};
        }
        bool SubmitCachedPart(NativeBackend target, ActorData actor, int partIndex, VulkanModelPart part, uint pipeline, uint flags, uint cascades, Vector3 origin, float radius, byte[] stream, uint count, MatrixD world, MaterialOverride material)
        {
            if (actor.PartFrames == null || actor.PartFrames.Length != actor.RenderModel.Parts.Count) actor.PartFrames = new PartFrame[actor.RenderModel.Parts.Count];
            PartFrame frame = actor.PartFrames[partIndex];
            if (frame == null) actor.PartFrames[partIndex] = frame = new PartFrame();
            Vector3 tint = MaterialTint(WorldShaderVariants.BasePipeline(pipeline)%5,material?.Color ?? actor.Color);
            Vector3 hsv = actor.Hsv + colorizeHsv;
            float emissive = material?.Emissivity ?? 1;
            bool identityDraw=HasHighlight(actor.Id) || buildLights.Excluded.Contains(actor.Id);
            bool batchRigid=target.RigidBatchingEnabled && pipeline<20 && pipeline%5==0 && stream==null && count==1 && !identityDraw;
            if(batchRigid && frame.BatchHint==null)frame.BatchHint=new NativeBackend.RigidBatchHint();
            if (frame.Live && frame.Constants != null && frame.MaterialRevision == actor.MaterialRevision && frame.Pipeline == pipeline && frame.Tint == tint && frame.Hsv == hsv && frame.Emissive == emissive && frame.Dither == actor.Dithering)
            {
                target.NotePreparedPart(true);
                if(!batchRigid)WorldConstants.WriteMatrix(frame.Constants, new WorldConstants.MatrixRows(frame.Row0, frame.Row1, frame.Row2), world);
                if(!batchRigid && target.WorldRecipesEnabled)target.EnsureWorldRecipe(ref frame.Recipe,pipeline,part.Mesh,frame.Color,frame.Normal,frame.Extensions,frame.Alpha,frame.Constants.Length);
                uint kept = batchRigid?target.AddRigidWorldPacket(pipeline,part.Mesh,frame.Color,frame.Normal,frame.Extensions,frame.Alpha,world,tint,hsv,emissive,actor.Dithering,flags,cascades,origin,radius,frame.BatchHint,actor.RenderLod):target.AddWorldPacket(pipeline, part.Mesh, frame.Color, frame.Normal, frame.Extensions, frame.Alpha, frame.Constants, stream, count, flags, cascades, origin, radius, false,frame.Recipe);
                if (identityDraw) SubmitHighlight(kept, actor, part, 0, (uint)part.Indices.Length, count);
                DrawnMeshes++;
                return true;
            }
            string colorName = material?.Textures.ColorMetalFileName ?? part.ColorTexture;
            target.NotePreparedPart(false);
            string normalName = material?.Textures.NormalGlossFileName ?? part.NormalTexture;
            string extensionName = material?.Textures.ExtensionsFileName ?? part.ExtensionTexture;
            string alphaName = material?.Textures.AlphamaskFileName ?? part.AlphaTexture;
            bool same = frame.Live && frame.Constants != null && frame.Pipeline == pipeline && frame.ColorName == colorName && frame.NormalName == normalName && frame.ExtensionName == extensionName && frame.AlphaName == alphaName && frame.Tint == tint && frame.Hsv == hsv && frame.Emissive == emissive && frame.Dither == actor.Dithering;
            if (!same)
            {
                ShaderMetadata layout = target.WorldLayouts[pipeline];
                frame.Constants = WorldConstants.Object(layout, world, tint, hsv, emissive, 0, null, actor.Dithering, false,actor.RenderLod);
                WorldConstants.MatrixRows rows = WorldConstants.ObjectRows(layout);
                frame.Row0 = rows.Row0; frame.Row1 = rows.Row1; frame.Row2 = rows.Row2;
                frame.Color = TextureCache.GetTexture(colorName);
                frame.Normal = TextureCache.GetWorldNormal(normalName);
                frame.Extensions = TextureCache.GetWorldExtensions(extensionName);
                frame.Alpha = TextureCache.GetWorldAlpha(alphaName);
                frame.Pipeline = pipeline; frame.ColorName = colorName; frame.NormalName = normalName; frame.ExtensionName = extensionName; frame.AlphaName = alphaName;
                frame.Tint = tint; frame.Hsv = hsv; frame.Emissive = emissive; frame.Dither = actor.Dithering; frame.Live = true;
            }
            else WorldConstants.WriteMatrix(frame.Constants, new WorldConstants.MatrixRows(frame.Row0, frame.Row1, frame.Row2), world);
            frame.MaterialRevision = actor.MaterialRevision;
            if(!batchRigid && target.WorldRecipesEnabled)target.EnsureWorldRecipe(ref frame.Recipe,pipeline,part.Mesh,frame.Color,frame.Normal,frame.Extensions,frame.Alpha,frame.Constants.Length);
            uint draw = batchRigid?target.AddRigidWorldPacket(pipeline,part.Mesh,frame.Color,frame.Normal,frame.Extensions,frame.Alpha,world,tint,hsv,emissive,actor.Dithering,flags,cascades,origin,radius,frame.BatchHint,actor.RenderLod):target.AddWorldPacket(pipeline, part.Mesh, frame.Color, frame.Normal, frame.Extensions, frame.Alpha, frame.Constants, stream, count, flags, cascades, origin, radius, false,frame.Recipe);
            if (identityDraw) SubmitHighlight(draw, actor, part, 0, (uint)part.Indices.Length, count);
            DrawnMeshes++;
            return true;
        }
        void NoteShadowPose(ActorData data)
        {
            MatrixD pose = data.Actor.WorldMatrix;
            var a = data.A;
            bool skinned = a.SkinMatrices != null;
            bool rootMoved = !a.HaveShadowPose || !NearlySame(data.ShadowPose, pose);
            bool wasDynamic=a.DynamicShadow,geometryDirty=data.GeometryDirty;
            bool dynamic=skinned || rootMoved || geometryDirty;
            data.GeometryDirty=false;if(dynamic!=wasDynamic)W(data).DynamicShadow=dynamic;
            if(wasDynamic!=dynamic)DirtyNativePass(data,appearance:false);
            if (!skinned && !rootMoved && !geometryDirty && !wasDynamic) return;
            if (a.HaveShadowPose && (rootMoved || geometryDirty))
            {
                // Remove a former static pose from the frozen map. A moving or
                // animated caster already in the dynamic overlay cannot dirty it.
                if(!wasDynamic)MarkSpots(a.ShadowCenter,a.ShadowRadius,false);
                MarkSpots(a.ShadowCenter,a.ShadowRadius,true);
            }
            if (!a.HaveShadowPose || rootMoved || geometryDirty)
            {
                BoundingBoxD bounds = data.Actor.CalculateAabb();
                data.ShadowPose = pose; W(data); a.ShadowCenter = bounds.Center; a.ShadowRadius = (float)bounds.HalfExtents.Length(); a.HaveShadowPose = true;
            }
            if(!dynamic && (rootMoved || geometryDirty || wasDynamic))MarkSpots(a.ShadowCenter, a.ShadowRadius, false);
            if(dynamic)MarkSpots(a.ShadowCenter, a.ShadowRadius, true);
        }
        void MarkSpots(Vector3D center, float radius, bool dynamic)
        {
            var frame = SideLights;
            for (int i = 0; i < frame.Spots.Count; i++)
            {
                LiveSpot spot = frame.Spots[i];
                double reach = spot.Range + radius;
                if (spot.Index >= 64 || Vector3D.DistanceSquared(center, spot.Position) > reach * reach) continue;
                if (dynamic) frame.SpotDynamic |= 1UL << spot.Index;
                else frame.SpotStatic |= 1UL << spot.Index;
            }
        }
        bool SpotChanged(uint ownerId,int index,Vector3D position, Vector3 direction,Vector3D up,float range, float aperture)
        {
            SpotMemory previous;
            bool known = spotByOwner.TryGetValue(ownerId, out previous);
            Vector3D shift = previous.Position - position;
            bool changed = dirtyShadowOwners.Remove(ownerId) || spotOwnerAt[index]!=(ulong)ownerId+1 || !known || !previous.Valid || shift.LengthSquared() > 0.0001 || Vector3.Dot(previous.Direction, direction) < 0.9999f || Vector3D.DistanceSquared(previous.Up,up)>0.0001 || previous.Range != range || Math.Abs(previous.Aperture - aperture) > 0.0001f;
            spotOwnerAt[index]=(ulong)ownerId+1;
            spotByOwner[ownerId] = new SpotMemory { Position = position, Direction = direction, Up=up,Range = range, Aperture = aperture, Valid = true };
            return changed;
        }
        static bool NearlySame(MatrixD a, MatrixD b)
        {
            Vector3D shift = a.Translation - b.Translation;
            return shift.LengthSquared() <= 0.0001 && Vector3D.Dot(a.Forward, b.Forward) > 0.9999 && Vector3D.Dot(a.Up, b.Up) > 0.9999;
        }
        private static unsafe void WriteFloat4(byte[] bytes,int offset,float x,float y,float z,float w)
        {
            fixed(byte* raw=bytes){float* f=(float*)(raw+offset);f[0]=x;f[1]=y;f[2]=z;f[3]=w;}
        }
        private static bool WriteCubeInstance(byte[] stream,int i,MyCubeInstanceData cube,Vector3 hsv)
        {
            int offset=i*64;
            for(int b=0;b<8;b++){var packed=cube.GetPackedBone(b);stream[offset+b*4]=packed.X;stream[offset+b*4+1]=packed.Y;stream[offset+b*4+2]=packed.Z;stream[offset+b*4+3]=packed.W;}
            Vector4 p=cube.PackedOrthoMatrix,mask=cube.ColorMaskHSV;mask.X+=hsv.X;mask.Y+=hsv.Y;mask.Z+=hsv.Z;
            WriteFloat4(stream,offset+32,p.X,p.Y,p.Z,p.W);WriteFloat4(stream,offset+48,mask.X,mask.Y,mask.Z,mask.W);return mask.W!=0;
        }
        private static unsafe void WriteInstance(byte[] stream,int i,Matrix m,Vector4 hsv)
        {
            fixed(byte* raw=stream)
            {
                float* f=(float*)(raw+i*64);
                f[0]=m.M11;f[1]=m.M21;f[2]=m.M31;f[3]=m.M41;f[4]=m.M12;f[5]=m.M22;f[6]=m.M32;f[7]=m.M42;f[8]=m.M13;f[9]=m.M23;f[10]=m.M33;f[11]=m.M43;f[12]=hsv.X;f[13]=hsv.Y;f[14]=hsv.Z;f[15]=hsv.W;
            }
        }

        private void DrawModel(NativeBackend target, ActorData actor, MatrixD world, Vector3? hsv, ulong[] meshOverrides, float instanceDither = 0)
        {
            for (int p = 0; p < actor.Model.Parts.Count; ++p)
            {
                VulkanModelPart part = actor.Model.Parts[p];
                if ((part.Flags & 4) != 0) { UnsupportedMaterialDraws++; continue; }
                MaterialOverride material;
                actor.Materials.TryGetValue(part.MaterialName ?? string.Empty, out material);
                if (material != null && ((material.Flags.Add & RenderFlags.SkipInMainView) != 0 || (material.Flags.Remove & RenderFlags.Visible) != 0)) continue;
                string color = material?.Textures.ColorMetalFileName ?? part.ColorTexture;
                Color tint = material?.Color ?? actor.Color;
                ulong texture = TextureCache.GetTexture(color);
                ulong normal = TextureCache.GetTexture(material?.Textures.NormalGlossFileName ?? part.NormalTexture, false);
                ulong extensions = TextureCache.GetTexture(material?.Textures.ExtensionsFileName ?? part.ExtensionTexture, false);
                ulong alpha = TextureCache.GetWorldAlpha(material?.Textures.AlphamaskFileName ?? part.AlphaTexture);
                uint flags = part.Flags & 3;
                if ((actor.Flags & RenderFlags.MetalnessColorable) != 0) flags |= 16;
                if ((actor.Flags & (RenderFlags.CastShadows | RenderFlags.SkipInDepth)) != RenderFlags.CastShadows) flags |= 512;
                target.DrawMesh(meshOverrides != null && p < meshOverrides.Length ? meshOverrides[p] : part.Mesh, world, tint, texture, flags,
                    normal, extensions, alpha, (hsv ?? actor.Hsv) + colorizeHsv, (material?.Emissivity ?? 1) * bloomEmissiveness, actor.Dithering + instanceDither);
                DrawnMeshes++;
                if (Math.Abs(actor.Dithering + instanceDither) > 0) DitheredDraws++;
            }
        }

        private ulong[] DeformCube(ActorData actor, int instance, MyCubeInstanceData cube)
        {
            ulong[] handles;
            if (actor.DeformedMeshes.TryGetValue(instance, out handles)) return handles;
            var model = actor.Model;
            float[] vertices = (float[])model.Vertices.Clone();
            if (cube.EnableSkinning && model.BlendIndices != null && model.BlendWeights != null)
            {
                for (int i = 0; i < Math.Min(model.BlendIndices.Length, model.BlendWeights.Length); ++i)
                {
                    Vector4I source = model.BlendIndices[i];
                    var indices = new Vector4UByte((byte)source.X, (byte)source.Y, (byte)source.Z, (byte)source.W);
                    Vector4 weights = model.BlendWeights[i];
                    if (source.X < 0 || source.X > 8 || source.Y < 0 || source.Y > 8 || source.Z < 0 || source.Z > 8 || source.W < 0 || source.W > 8)
                        throw new InvalidOperationException("Cube deformation bone index outside packed nine-bone palette.");
                    Vector3 offset = cube.ComputeBoneOffset(ref indices, ref weights);
                    offset = Vector3.TransformNormal(offset, Matrix.Transpose(cube.LocalMatrix));
                    int o = i * 8;
                    if (o + 2 >= vertices.Length) break;
                    // Packed offsets are in cube/grid space. Rotate back before the instance transform.
                    vertices[o] += offset.X; vertices[o + 1] += offset.Y; vertices[o + 2] += offset.Z;
                }
            }
            byte packedPattern = cube.GetPackedBone(7).W;
            float offsetU = cube.GetPackedBone(5).W / (float)(1 + ((packedPattern >> 4) & 15));
            float offsetV = cube.GetPackedBone(6).W / (float)(1 + (packedPattern & 15));
            for (int i = 0; i < vertices.Length / 8; ++i) { vertices[i * 8 + 6] += offsetU; vertices[i * 8 + 7] += offsetV; }
            handles = UploadDerived(model, vertices);
            actor.DeformedMeshes.Add(instance, handles);
            return handles;
        }

        private void UpdateCharacterMesh(ActorData actor)
        {
            var model = actor.Model;
            float[] vertices = (float[])model.Vertices.Clone();
            if (model.BlendWeights == null) return;
            BoundingBox skinBounds = BoundingBox.CreateInvalid();
            for (int i = 0; i < Math.Min(model.BlendIndices.Length, model.BlendWeights.Length); ++i)
            {
                int o = i * 8;
                if (o + 7 >= vertices.Length) break;
                Vector4I indices = model.BlendIndices[i]; Vector4 weights = model.BlendWeights[i];
                Vector3 position = new Vector3(model.Vertices[o], model.Vertices[o + 1], model.Vertices[o + 2]);
                Vector3 normal = new Vector3(model.Vertices[o + 3], model.Vertices[o + 4], model.Vertices[o + 5]);
                Vector3 skinnedPosition = Vector3.Zero, skinnedNormal = Vector3.Zero;
                float weightSum = 0;
                for (int b = 0; b < 4; ++b)
                {
                    int index = indices[b]; float weight = weights[b];
                    if (weight <= 0 || index < 0 || index >= actor.SkinMatrices.Length) continue;
                    Matrix bone = actor.SkinMatrices[index];
                    skinnedPosition += Vector3.Transform(position, bone) * weight;
                    skinnedNormal += Vector3.TransformNormal(normal, bone) * weight;
                    weightSum += weight;
                }
                if (weightSum <= 0) continue;
                if (skinnedNormal.LengthSquared() > 0.00001f) skinnedNormal.Normalize();
                VulkanModelCache.WriteVertex(vertices, i, skinnedPosition / weightSum, skinnedNormal, new Vector2(model.Vertices[o + 6], model.Vertices[o + 7]));
                skinBounds = skinBounds.Include(skinnedPosition / weightSum);
            }
            actor.SkinnedBounds = skinBounds;
            if (actor.SkinnedMeshes == null) actor.SkinnedMeshes = UploadDerived(model, vertices);
            else for (int p = 0; p < model.Parts.Count; ++p) backend.UploadMesh(actor.SkinnedMeshes[p], vertices, model.Parts[p].Indices);
            actor.SkinDirty = false;
            SkinnedUploads++;
        }

        private ulong[] UploadDerived(VulkanModel model, float[] vertices)
        {
            ulong[] result = new ulong[model.Parts.Count];
            for (int p = 0; p < result.Length; ++p) { result[p] = models.AllocateMesh(); backend.UploadMesh(result[p], vertices, model.Parts[p].Indices); }
            return result;
        }

        private void InvalidateDeformations(uint buffer)
        {
            foreach (var actor in actors.Values) if (actor.A.InstanceBuffer == buffer){if(building)ReleaseDeformations(actor);else buildOps.Add(new BuildOp{Kind=BuildOpKind.ReleaseDeformations,Data=actor});}
        }

        private void ReleaseDeformations(ActorData actor)
        {
            foreach (ulong[] meshes in actor.DeformedMeshes.Values) foreach (ulong mesh in meshes) backend.RemoveResource(1, mesh);
            actor.DeformedMeshes.Clear();
        }

        private void ReleaseDynamicMeshes(ActorData actor)
        {
            if (actor.SkinnedMeshes != null) foreach (ulong mesh in actor.SkinnedMeshes) backend.RemoveResource(1, mesh);
            actor.SkinnedMeshes = null;
            ReleaseDeformations(actor);
        }

        public void AddVisible(HashSet<uint> destination) { foreach (uint id in visible) destination.Add(id);if(nativePassActive){var owners=backend.GetPassVisibleOwners(out int count);for(int i=0;i<count;i++)destination.Add(owners[i]);} }

        private void Remove(uint id)
        {
            ActorData actor;
            if (actors.TryGetValue(id, out actor))
            {
                NoteActorFrame(actor);
                // Native ownership, dirty entries, occlusion group and candidate proxy are Build state.
                if(building)RemoveBuildState(actor);else buildOps.Add(new BuildOp{Kind=BuildOpKind.RemoveActor,Data=actor});
                InvalidateOcclusion("Removal");
                MarkMembershipDirty();
                if(actor.A.HaveShadowPose)removedShadowRegions.Add((actor.A.ShadowCenter,actor.A.ShadowRadius));
                spatialDirty.Remove(actor);shadowDirty.Remove(actor);skinnedActors.Remove(actor);dirtyTransforms.Remove(actor.Actor);
                foreach(var child in actor.Actor.Children)MarkTransforms(child);
                actors.Remove(id); LogActor(id,null); actor.Actor.Destruct();
            }
            shadowIgnores.Remove(id);dirtyShadowOwners.Remove(id);spotByOwner.Remove(id);foreach(var values in shadowIgnores.Values)values.Remove(id);
            if(applyHighlights.Remove(id)|applyOverlappingHighlights.Remove(id))highlightsChanged=true;if(applyInstances.Remove(id))MarkInstances(id); visible.Remove(id);
        }

        public override void Clear()
        {
            // Unload runs with Build joined: flush the queued Build work and act on Build state directly.
            LatchBuildOps();FlushBuildOps();bool wasBuilding=building;building=true;
            try{
            foreach (uint id in new List<uint>(actors.Keys)) Remove(id);
            SyncBuildActors();
            applyPassDirty.Clear();applyCellDirty.Clear();applyCellPoseDirty.Clear();applyGpuDirty.Clear();applyGroupDirty.Clear();applyMembershipDirty=false;
            foreach (var light in new List<VulkanLightComponent>(lights)) light.Owner?.Destruct();
            instances.Clear();applyInstances.Clear();instancesChanged.Clear(); lights.Clear(); visible.Clear();
            highlights.Clear();overlappingHighlights.Clear();applyHighlights.Clear();applyOverlappingHighlights.Clear();highlightsChanged=false;pendingSkybox=null;
            billboards.Dispose();
            skybox.Dispose();
            ClearNativePassScene();
            models.Clear(); TextureCache.Clear();
            OldResources.SessionEnd();
            base.Clear(); ManualCullTree.Clear();
            ClearShadowGroupOcclusion();
            occlusionGroups.Clear();dirtyOcclusionGroups.Clear();groupQueryOwners.Clear();groupMembershipDirty=true;
            actorIndex.Clear();dirtyTransforms.Clear();spatialDirty.Clear();shadowDirty.Clear();skinnedActors.Clear();staticGroups.Clear();removedShadowRegions.Clear();candidates.Clear();shadowWork.Clear();
            while(transformNotifications.TryDequeue(out _)){}
            while(actorStateNotifications.TryDequeue(out _)){}
            Next.Frustum=Next.LightFarFrustum=null;frustum=null;lightQuery.Clear();ambientLightQuery.Clear();lightPointGroups.Clear();lightQueryGroups.Clear();lightGroupContained.Clear();
            Next.HaveSizeProjection=haveSizeProjection=false;
            colorizeHsv = applyColorizeHsv = new Vector3(MyTextureDebugMultipliers.Defaults.ColorizeHSV);
            CalculateGravityInPoint = position => Vector3.Zero;
            }finally{building=wasBuilding;}
        }

        public void Dispose() => Clear();
        public override void SetActorParent(IMyActor parent, IMyActor child, Matrix? childToParent)
        {
            for (IMyActor ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
                if (ReferenceEquals(ancestor, child)) throw new InvalidOperationException("Render actor parenting would create a cycle.");
            if(!ReferenceEquals(child.Parent,parent))MarkMembershipDirty();
            child.SetParent(parent);
            MarkTransforms(child);
            if (childToParent.HasValue)
            {
                Matrix local = childToParent.Value;
                if (parent != null) child.SetRelativeTransform(ref local);
                else { MatrixD world = local; child.SetMatrix(ref world); }
            }
        }

        public void DrawBillboards(MySharedData shared) => billboards.Draw(shared);
        public void DrawCollectedBillboards() => billboards.DrawCollected();
        // Flares and billboards forked over lighting setup and pass publication (plan 2.2,
        // SE_VULKAN_EFFECTS_AHEAD=0 turns it off): collect on the render thread at the serial point,
        // run flares and billboard emission on a worker, finish on the render thread before QueueWorld.
        public static bool EffectsAheadEnabled = System.Environment.GetEnvironmentVariable("SE_VULKAN_EFFECTS_AHEAD") != "0";
        public bool EffectsAhead { get => EffectsAheadEnabled; set => EffectsAheadEnabled = value; }
        // Apply collects and LatchFrame hands the collection to Build; the old-order lane collects inside Build, so it latches at once.
        public void CollectBillboards(MySharedData shared) { billboards.Collect(shared); if (building) billboards.Latch(); }
        public void DrawFlaresAndBillboardsAhead(MatrixD view, MatrixD projection, int width, int height, float multiplier) => billboards.EmitAhead(() => DrawFlares(view, projection, width, height, multiplier));
        public void FinishBillboards() => billboards.FinishAhead();
        public long BillboardAheadFrames => billboards.AheadFrames;
        public long BillboardAheadFallbacks => billboards.AheadFallbacks;
        public long BillboardVerifyMatches => billboards.VerifyMatches;
        public long BillboardVerifyMismatches => billboards.VerifyMismatches;
        public long BillboardCount => billboards.Submitted;
        public long UnsupportedBillboards => billboards.Unsupported;
        public long SkyboxDraws => skybox.Draws;
        public bool SkyboxReady => skybox.Ready;

        private void UpdateWorldTransforms()
        {
            AcceptActorStateNotifications();
            while(transformNotifications.TryDequeue(out var notification))AcceptTransformNotification(notification);
            transformUpdated.Clear();
            foreach (var actor in dirtyTransforms) UpdateTransform(actor, transformUpdated);
            TransformUpdates+=transformUpdated.Count;dirtyTransforms.Clear();
        }

        private static void UpdateTransform(IMyActor actor, HashSet<IMyActor> updated)
        {
            if (!updated.Add(actor)) return;
            if (actor.Parent != null) UpdateTransform(actor.Parent, updated);
            actor.UpdateWorldMatrix();
        }

        internal static bool IsVisible(IMyActor actor)
        {
            for (IMyActor current = actor; current != null; current = current.Parent)
                if (current.IsDestroyed || !current.IsVisible) return false;
            return true;
        }

        internal Matrix GetDecalBone(uint id,int index,Matrix[] transforms){if(actors.TryGetValue(id,out var actor)&&actor.A.Skeleton!=null&&index>=0&&index<actor.A.Skeleton.Length&&transforms!=null&&index<transforms.Length)return actor.A.Skeleton[index].SkinTransform*transforms[index];return Matrix.Identity;}
        internal MyActor CreateParticleLight(string name){var actor=new MyActor(this);actor.Construct(name);TrackTransforms(actor);AttachLight(actor);return actor;}
        private VulkanLightComponent AttachLight(MyActor actor)
        {
            var component = new VulkanLightComponent(this);
            component.Construct(); actor.AddComponent<MyLightComponent>(component); lights.Add(component);
            return component;
        }

        private void SelectFrameLights()
        {
            var frame = applyLights;
            frame.SpotActors.Clear();
            frame.Spots.Clear();
            frame.SpotStatic = frame.SpotDynamic = 0;
            var selected=SelectLights();
            Vector3D camera = Environment.CameraPosition;
            var packets = new List<NativeLight>();var spotPackets=new List<NativeSpot>();int shadowed=0;
            foreach (var light in selected.Spots)
            {
                UpdateRenderLightData data = light.Data;
                Vector3D position = light.Position - camera;
                if (data.SpotLightOn && spotPackets.Count < 32 && data.SpotLight.Light.Range > 0)
                {
                    Vector3 color = data.SpotLight.Light.Color;
                    Vector3 direction = light.Owner.WorldMatrix.Forward;
                    if (direction.LengthSquared() > .00001f) direction.Normalize();
                    var packet = new NativeLight { PositionRange = new[] { (float)position.X, (float)position.Y, (float)position.Z, data.SpotLight.Light.Range }, ColorIntensity = new[] { color.X, color.Y, color.Z, 1f }, DirectionCone = new[] { direction.X, direction.Y, direction.Z, data.SpotLight.ApertureCos }, Flags = 1 };
                    SetLightMaterial(ref packet, data.SpotLight.Light);
                    float shadowScale=Next.RenderSettings.User.ShadowQuality switch{
                        MyShadowsQuality.LOW=>Next.ShadowSettings.ReflectorShadowDistanceLow,
                        MyShadowsQuality.MEDIUM=>Next.ShadowSettings.ReflectorShadowDistanceMedium,
                        MyShadowsQuality.HIGH=>Next.ShadowSettings.ReflectorShadowDistanceHigh,
                        MyShadowsQuality.EXTREME=>Next.ShadowSettings.ReflectorShadowDistanceExtreme,
                        _=>0};
                    float shadowRange=shadowScale*data.SpotLight.Light.Range;
                    float far=Math.Max(shadowRange,1.5f);float angle=(float)(2*Math.Acos(MathHelper.Clamp(data.SpotLight.ApertureCos,.001f,.99999f)));
                    Vector3D up=light.Owner.WorldMatrix.Up;if(Math.Abs(Vector3D.Dot(up,direction))>.99)up=Math.Abs(direction.Y)>.99?Vector3D.Right:Vector3D.Up;
                    MatrixD projection=MatrixD.CreateLookAt(position,position+direction,up)*(MatrixD)Matrix.CreatePerspectiveFieldOfView(angle,1,.5f,far);
                    bool casts=data.CastShadows && shadowRange>0 && shadowed<16;if(casts)shadowed++;
                    int lightIndex=packets.Count;
                    if(casts){frame.Spots.Add(new LiveSpot{Position=light.Position,Range=far,Cone=data.SpotLight.ApertureCos,Direction=direction,Index=lightIndex,WorldProjection=MatrixD.CreateTranslation(-camera)*projection});if(SpotChanged(light.Owner.ID,lightIndex,light.Position,direction,up,far,data.SpotLight.ApertureCos))frame.SpotStatic|=1UL<<(lightIndex);}
                    frame.SpotActors[(uint)packets.Count]=light.Owner.ID;
                    spotPackets.Add(new NativeSpot{Light=(uint)packets.Count,Flags=casts?1u:0u,Cookie=TextureCache.GetTexture(data.ReflectorTexture,true),Projection=NativeBackend.MatrixValues(projection)});
                    packets.Add(packet);
                }
                if (spotPackets.Count >= 32) break;
            }
            // Independent stock budgets. Spots precede points so their identities
            // remain in the existing 64-slot shadow redraw/exclusion mask.
            uint pointBudget=WorldConstants.PointLightCapacity(Next.RenderSettings.User.LightsQuality);int pointCount=0;
            foreach(var light in selected.Points){
                var data=light.Data;Vector3D position=light.Position-camera;
                if (data.PointLightOn && pointCount < pointBudget && data.PointLight.Range > 0)
                {
                    Vector3 color = data.PointLight.Color;
                    Vector3D pointPosition = position + data.PointOffset * light.Owner.WorldMatrix.Forward;
                    // MyLight already multiplies the linear layout color by PointIntensity.
                    var packet = new NativeLight { PositionRange = new[] { (float)pointPosition.X, (float)pointPosition.Y, (float)pointPosition.Z, data.PointLight.Range }, ColorIntensity = new[] { color.X, color.Y, color.Z, 1f }, DirectionCone = new float[4] };
                    SetLightMaterial(ref packet, data.PointLight);
                    packets.Add(packet);pointCount++;
                }
                if(pointCount>=pointBudget)break;
            }
            ulong activeShadowSlots=0;foreach(var spot in frame.Spots)activeShadowSlots|=1UL<<spot.Index;
            for(int i=0;i<spotOwnerAt.Length;i++)if((activeShadowSlots&(1UL<<i))==0)spotOwnerAt[i]=0;
            frame.Lights=packets.ToArray();frame.SpotPackets=spotPackets.ToArray();
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatEncoding
        {
            [FieldOffset(0)] public float Value;
            [FieldOffset(0)] public uint Bits;
        }

        private static void SetLightMaterial(ref NativeLight packet, MyLightLayout layout)
        {
            float curve = layout.Falloff / 1.5f;
            packet.Flags |= 2; // Native light metadata uses the existing reserved float-bit fields.
            packet.Reserved0 = new FloatEncoding { Value = 1f / (.001f + curve * curve * curve) }.Bits;
            packet.Reserved1 = new FloatEncoding { Value = layout.DiffuseFactor }.Bits;
            packet.Reserved2 = new FloatEncoding { Value = layout.GlossFactor }.Bits;
        }

        private sealed partial class VulkanLightComponent : MyLightComponent
        {
            private readonly VulkanScene scene;
            public VulkanLightComponent(VulkanScene scene) { this.scene = scene;FlareId=++scene.nextFlareId; }
            public readonly ulong FlareId;
            // Bumped by UpdateData, the only writer of Data and EffectiveData (the light snapshot copies on change).
            public uint DataRevision;
            public Vector3D Position => Owner.WorldMatrix.Translation;
            public override void UpdateData(ref UpdateRenderLightData data)
            {
                m_originalData = data; m_data = data; DataRevision++;
                if (data.Glare.Glares != null)
                {
                    m_originalData.Glare.Glares = (MySubGlare[])data.Glare.Glares.Clone();
                    for(int i=0;i<m_originalData.Glare.Glares.Length;++i)if(m_originalData.Glare.Glares[i].OcclusionToIntensityCurve!=null)m_originalData.Glare.Glares[i].OcclusionToIntensityCurve=(MySubGlare.KeyPoint[])m_originalData.Glare.Glares[i].OcclusionToIntensityCurve.Clone();
                    m_data.Glare.Glares = m_originalData.Glare.Glares;
                }
                UpdateLightBounds();
                // Light positions are parent-local when attached to a grid or RenderDirect actor.
                Vector3 forward = data.SpotLight.Direction, up = data.SpotLight.Up;
                if (forward.LengthSquared() < .00001f) forward = Vector3.Forward;
                else forward.Normalize();
                if (up.LengthSquared() < .00001f || Math.Abs(Vector3.Dot(Vector3.Normalize(up), forward)) > .999f)
                    up = Math.Abs(forward.Y) > .99f ? Vector3.Right : Vector3.Up;
                if (Owner.Parent != null)
                {
                    Matrix local = Matrix.CreateWorld((Vector3)data.Position, forward, up);
                    Owner.SetRelativeTransform(ref local);
                }
                else
                {
                    MatrixD world = MatrixD.CreateWorld(data.Position, forward, up);
                    Owner.SetMatrix(ref world);
                }
                UpdateLightCullData();Owner.SetVisibility(PointOn||SpotOn||m_data.Glare.Enabled);
            }
            public override void OnRemove(MyActor owner) { scene.lights.Remove(this); base.OnRemove(owner); }
        }

        private sealed class CullResults : MyCullResultsBase
        {
            public readonly List<VulkanLightComponent> Points=new List<VulkanLightComponent>(),Spots=new List<VulkanLightComponent>();
            public void Clear(){Points.Clear();Spots.Clear();}
            public void Append(CullResults other){Points.AddRange(other.Points);Spots.AddRange(other.Spots);}
        }
        public override MyManualCullTreeData AllocateGroupData()
        {
            var data = new MyManualCullTreeData { All = new CullResults(), RenderCullData = new CullData[19] };
            for (int i = 0; i < data.RenderCullData.Length; ++i) { data.RenderCullData[i] = CullData.Create(); data.RenderCullData[i].ActiveResults = new CullResults(); }
            return data;
        }
        public override void FreeGroupData(MyManualCullTreeData data) { data.Actor = null; data.Children.Clear(); data.BruteCull.Clear(); }
        public override MyChildCullTreeData CompileCullData(MyChildCullTreeData data) => data;

        private sealed class SceneComponentFactory : IMyComponentFactory
        {
            public MyActorComponent Create(Type type)
            {
                if (!typeof(MyRenderDirectComponent).IsAssignableFrom(type)) throw new NotSupportedException("Vulkan component factory accepts RenderDirect components only: " + type.FullName);
                var component = (MyActorComponent)Activator.CreateInstance(type, true);
                component.Construct();
                return component;
            }
            public void Deallocate(MyActorComponent item) { }
        }

        private sealed class SceneActorFactory : IMyActorFactory
        {
            public VulkanScene Scene;
            public MyLightComponent CreateLight(string debugName)
            {
                var actor = new MyActor(Scene); actor.Construct(debugName);
                return Scene.AttachLight(actor);
            }
            public void Destroy(IMyActor actor, bool fadeOut)
            {
                if (Scene.actors.ContainsKey(actor.ID)) Scene.Remove(actor.ID);
                else actor.Destruct();
            }
        }

        private sealed class BillboardHelper : IMyBillboardsHelper
        {
            public VulkanBillboards Implementation;
            public void AddPointBillboard(MyStringId material, Vector4 color, Vector3D origin, float radius, float angle, MyBillboard.BlendTypeEnum blendType = MyBillboard.BlendTypeEnum.Standard, int customViewProjection = -1, float intensity = 1) => Implementation.AddPointBillboard(material, color, origin, radius, angle, blendType, customViewProjection, intensity);
            public void AddLineBillboard(MyStringId material, Vector4 color, Vector3D origin, Vector3 directionNormalized, float length, float thickness, MyBillboard.BlendTypeEnum blendType = MyBillboard.BlendTypeEnum.Standard, int customViewProjection = -1, float intensity = 1) => Implementation.AddLineBillboard(material, color, origin, directionNormalized, length, thickness, blendType, customViewProjection, intensity);
            public void AddBillboardOriented(MyStringId material, Vector4 color, Vector3D origin, Vector3 leftVector, Vector3 upVector, float radiusX, float radiusY, MyBillboard.BlendTypeEnum blendType = MyBillboard.BlendTypeEnum.Standard, float softParticleDistanceScale = 1, int customViewProjection = -1) => Implementation.AddBillboardOriented(material, color, origin, leftVector, upVector, radiusX, radiusY, blendType, softParticleDistanceScale, customViewProjection);
        }
    }
}

