using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    // Stock consumes all OLD geometry, including decals, before NEW work.
    public const uint OldGeometryOrder=0x04000000u;
    public const uint OldMeshDecalOrder=OldGeometryOrder;
    public const uint OldBlendedDecalOrder=OldMeshDecalOrder;
    public const uint StockBackCull=0x08000000u, StockNoCull=0x10000000u;
    public const uint OldDepthNoCull=0x20000000u;
    private WorldFunctions worldApi;
    private WorldFunctions WorldApi => worldApi ?? (worldApi = new WorldFunctions(GetSymbol));
    public bool WorldReady { get; private set; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int HdrQualityFn(IntPtr ctx,uint highQuality);
    private HdrQualityFn hdrQuality;
    public void SetHdrQuality(bool highQuality){hdrQuality??=(HdrQualityFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_hdr_quality"),typeof(HdrQualityFn));Check(hdrQuality(context,highQuality?1u:0u),context);}
    private readonly HashSet<uint> tintedGenericPipelines=new();
    public readonly Dictionary<uint, ShaderMetadata> WorldLayouts = new Dictionary<uint, ShaderMetadata>();
    private readonly List<WorldPacket> worldPackets = new List<WorldPacket>();
    // In-place packet access. WorldPacket is a large struct with references;
    // copying it out and back per appended instance dominated batch appends.
    // The reference is valid only until the next Add to worldPackets.
#if NETCOREAPP
    private ref WorldPacket PacketAt(int index)=>ref CollectionsMarshal.AsSpan(worldPackets)[index];
#else
    private static readonly Func<List<WorldPacket>,WorldPacket[]> packetItems=CreatePacketItems();
    private static Func<List<WorldPacket>,WorldPacket[]> CreatePacketItems(){var list=System.Linq.Expressions.Expression.Parameter(typeof(List<WorldPacket>));
        var items=typeof(List<WorldPacket>).GetField("_items",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)??throw new MissingFieldException("List<T>._items");
        return System.Linq.Expressions.Expression.Lambda<Func<List<WorldPacket>,WorldPacket[]>>(System.Linq.Expressions.Expression.Field(list,items),list).Compile();}
    private ref WorldPacket PacketAt(int index){if((uint)index>=(uint)worldPackets.Count)throw new ArgumentOutOfRangeException(nameof(index));return ref packetItems(worldPackets)[index];}
#endif
    private readonly Dictionary<byte[], int> instanceOffsets = new Dictionary<byte[], int>(ByteIdentity.Instance);
    private byte[] constantArena = Array.Empty<byte>(), instanceArena = Array.Empty<byte>();
    public bool ObjectRetentionEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_RETAINED_OBJECTS")=="1";
    sealed class ObjectIdentity { public ulong Id; }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[],ObjectIdentity> objectIdentities=new();
    private ulong nextObjectIdentity;
    private ulong[] objectIdentityScratch=Array.Empty<ulong>();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RetainedQueueFn(IntPtr ctx,byte[] frame,uint bytes,float[] projection,float[] inverse,[In]NativeWorldDraw[] draws,uint count,[In]ulong[] ids);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ObjectRecordStatsFn(IntPtr ctx,ref NativeObjectRecordStats stats);
    private RetainedQueueFn retainedQueue;
    private ObjectRecordStatsFn objectRecordStats;
    public NativeObjectRecordStats GetObjectRecordStats(){
        if(objectRecordStats==null)objectRecordStats=(ObjectRecordStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_object_record_stats"),typeof(ObjectRecordStatsFn));
        var result=new NativeObjectRecordStats{Size=48,AbiVersion=AbiVersion};Check(objectRecordStats(context,ref result),context);return result;
    }
    private NativeWorldDraw[] drawScratch = Array.Empty<NativeWorldDraw>();
    private static readonly uint WorldDrawSize = (uint)Marshal.SizeOf(typeof(NativeWorldDraw));
    sealed class ByteIdentity : IEqualityComparer<byte[]>
    {
        public static readonly ByteIdentity Instance = new ByteIdentity();
        public bool Equals(byte[] x, byte[] y) => ReferenceEquals(x, y);
        public int GetHashCode(byte[] obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
    static void Grow(ref byte[] arena, int used, int add)
    {
        int need = used + add;
        if (arena.Length >= need) return;
        int size = Math.Max(need, arena.Length == 0 ? 1 << 20 : Math.Max(need, arena.Length * 2));
        var next = new byte[size];
        if (used > 0) Buffer.BlockCopy(arena, 0, next, 0, used);
        arena = next;
    }
    private string resourceCacheDirectory;
    public ShaderMetadata WorldFrameLayout { get; private set; }
    public static void CheckWorldApi(){EnsureLoaded();new WorldFunctions(GetSymbol);new GBufferFunctions(GetSymbol);new VoxelFunctions(GetSymbol);new EnvironmentFunctions(GetSymbol);new TransparencyFunctions(GetSymbol);new ParticleFunctions(GetSymbol);new DecalFunctions(GetSymbol);new FlareFunctions(GetSymbol);new PostprocessFunctions(GetSymbol);new ProbeFunctions(GetSymbol);new ResolutionFunctions(GetSymbol);new HighlightFunctions(GetSymbol);new SpotFunctions(GetSymbol);new OcclusionFunctions(GetSymbol);}
    public void BeginUploads() => Check(WorldApi.BeginUploads(context),context);
    public void EndUploads()
    {
        long started=PacketProfilingEnabled?System.Diagnostics.Stopwatch.GetTimestamp():0;
        Check(WorldApi.EndUploads(context),context);
        if(PacketProfilingEnabled)uploadEndTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-started;
    }
    public NativeWorkStats GetWorkStats()
    {
        var result=new NativeWorkStats{Size=(uint)Marshal.SizeOf(typeof(NativeWorkStats)),AbiVersion=AbiVersion};
        Check(WorldApi.Stats(context,ref result),context);return result;
    }
    private WorldIndirectFn parallelCascades;
    private CascadeRecordStatsFn cascadeRecordStats;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CascadeRecordStatsFn(IntPtr ctx,ref NativeCascadeRecordStats stats);
    public void SetParallelCascades(bool enabled){
        if(parallelCascades==null)parallelCascades=(WorldIndirectFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_parallel_cascades"),typeof(WorldIndirectFn));
        Check(parallelCascades(context,enabled?1u:0u),context);
    }
    public NativeCascadeRecordStats GetCascadeRecordStats(){
        if(cascadeRecordStats==null)cascadeRecordStats=(CascadeRecordStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_cascade_record_stats"),typeof(CascadeRecordStatsFn));
        var result=new NativeCascadeRecordStats{Size=32,AbiVersion=AbiVersion};Check(cascadeRecordStats(context,ref result),context);return result;
    }
    private WorldIndirectFn lightTiles;
    private LightTileStatsFn lightTileStats;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LightTileInitFn(IntPtr ctx,byte[] spirv,ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LightTileStatsFn(IntPtr ctx,ref NativeLightTileStats stats);
    private void ConfigureLightTiles(VulkanShaderLibrary library){var artifact=library.LoadLightTiles();var bytes=SpirvRuntimeModule.FromArtifact(artifact);var init=(LightTileInitFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_init_light_tiles"),typeof(LightTileInitFn));Check(init(context,bytes,(ulong)bytes.Length),context);}
    public void SetLightTiles(bool enabled){if(lightTiles==null)lightTiles=(WorldIndirectFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_light_tiles"),typeof(WorldIndirectFn));Check(lightTiles(context,enabled?1u:0u),context);}
    public NativeLightTileStats GetLightTileStats(){if(lightTileStats==null)lightTileStats=(LightTileStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_light_tile_stats"),typeof(LightTileStatsFn));var stats=new NativeLightTileStats{Size=64,AbiVersion=AbiVersion};Check(lightTileStats(context,ref stats),context);return stats;}
    private PerformanceStatsFn performanceStats;
    private WorldIndirectFn worldIndirect;
    private WorldIndirectFn geometryArena;
    private GeometryArenaStatsFn geometryArenaStats;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GeometryArenaStatsFn(IntPtr context,ref NativeGeometryArenaStats stats);
    public void SetGeometryArena(bool enabled)
    {
        if(geometryArena==null)geometryArena=(WorldIndirectFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_geometry_arena"),typeof(WorldIndirectFn));
        Check(geometryArena(context,enabled?1u:0u),context);
    }
    public NativeGeometryArenaStats GetGeometryArenaStats()
    {
        if(geometryArenaStats==null)geometryArenaStats=(GeometryArenaStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_geometry_arena_stats"),typeof(GeometryArenaStatsFn));
        var result=new NativeGeometryArenaStats{Size=64,AbiVersion=AbiVersion};Check(geometryArenaStats(context,ref result),context);return result;
    }
    private IndirectStatsFn indirectStats;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WorldIndirectFn(IntPtr context,uint enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IndirectStatsFn(IntPtr context,ref NativeIndirectStats stats);
    public void SetWorldIndirect(bool enabled)
    {
        if(worldIndirect==null)worldIndirect=(WorldIndirectFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_world_indirect"),typeof(WorldIndirectFn));
        Check(worldIndirect(context,enabled?1u:0u),context);
    }
    public NativeIndirectStats GetIndirectStats()
    {
        if(indirectStats==null)indirectStats=(IndirectStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_indirect_stats"),typeof(IndirectStatsFn));
        var result=new NativeIndirectStats{Size=32,AbiVersion=AbiVersion};Check(indirectStats(context,ref result),context);return result;
    }
    private bool textureValidationCacheEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_TEXTURE_VALIDATION_CACHE")!="0";
    private bool textureValidationCacheApplied;
    private TextureValidationCacheFn textureValidationCache;
    private QueueValidationStatsFn queueValidationStats;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TextureValidationCacheFn(IntPtr context,uint enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueueValidationStatsFn(IntPtr context,ref NativeQueueValidationStats stats);
    public bool TextureValidationCacheEnabled
    {
        get=>textureValidationCacheEnabled;
        set{if(value==textureValidationCacheEnabled && textureValidationCacheApplied)return;if(textureValidationCache==null)textureValidationCache=(TextureValidationCacheFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_texture_validation_cache"),typeof(TextureValidationCacheFn));Check(textureValidationCache(context,value?1u:0u),context);textureValidationCacheEnabled=value;textureValidationCacheApplied=true;}
    }
    public NativeQueueValidationStats GetQueueValidationStats()
    {
        if(queueValidationStats==null)queueValidationStats=(QueueValidationStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_queue_validation_stats"),typeof(QueueValidationStatsFn));
        var result=new NativeQueueValidationStats{Size=32,AbiVersion=AbiVersion};Check(queueValidationStats(context,ref result),context);return result;
    }
    private PresentationStatsFn presentationStats;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PresentationStatsFn(IntPtr context,ref NativePresentationStats stats);
    public NativePresentationStats GetPresentationStats()
    {
        if(presentationStats==null)presentationStats=(PresentationStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_presentation_stats"),typeof(PresentationStatsFn));
        var result=new NativePresentationStats{Size=32,AbiVersion=AbiVersion};Check(presentationStats(context,ref result),context);return result;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PerformanceStatsFn(IntPtr context,ref NativePerformanceStats stats);
    // Read on the collection/render thread after completion: logical batching
    // counters are managed frame state and can be partial during collection.
    public NativePerformanceStats GetPerformanceStats()
    {
        if(performanceStats==null)performanceStats=(PerformanceStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_performance_stats"),typeof(PerformanceStatsFn));
        var result=new NativePerformanceStats{Size=84,AbiVersion=AbiVersion};Check(performanceStats(context,ref result),context);result.LogicalDraws+=LogicalRigidDraws-rigidBatchPackets-nativeModelPackets;return result;
    }
    public void ConfigureWorld(VulkanShaderLibrary library,string shaderContent)
    {
        if(WorldReady)return;
        void Material(uint id,VulkanShaderArtifact vs,VulkanShaderArtifact ps)
        {
            if(id<20 && id%5==2){var adjusted=library.LoadObjectTintPixel(id,ps);if(!ReferenceEquals(adjusted,ps))tintedGenericPipelines.Add(id);ps=adjusted;}
            byte[] vertex=SpirvRuntimeModule.FromArtifact(vs),pixel=SpirvRuntimeModule.FromArtifact(ps);
            Check(WorldApi.InitPipeline(context,id,vertex,(ulong)vertex.Length,pixel,(ulong)pixel.Length),context);
            WorldLayouts[id]=vs.Layout;
        }
        foreach(var spec in WorldShaderVariants.Materials)Material(spec.Id,library.Load(spec.Vertex),library.Load(spec.Pixel));
        foreach(var spec in WorldShaderVariants.Extra)
            Material(spec.Id,library.LoadExtra(spec.Id,spec.Material,"Vertex",spec.Defines),library.LoadExtra(spec.Id,spec.Material,"Pixel",spec.Defines));
        for(uint i=0;i<WorldShaderVariants.Lighting.Length;++i)
        {
            var artifact=library.Load(WorldShaderVariants.Lighting[i]);byte[] module=SpirvRuntimeModule.FromArtifact(artifact);
            Check(WorldApi.InitLighting(context,i,module,(ulong)module.Length),context);
            if(i==1)WorldFrameLayout=artifact.Layout;
        }
        ConfigureVoxels(library);ConfigureEnvironment(library);ConfigureTransparency(library);ConfigureParticles(library);ConfigureDecals(library);ConfigurePostprocess(library);ConfigureProbes(library);ConfigureHighlights(library);ConfigureSpots(library);ConfigureOcclusion(library);ConfigureLightTiles(library);ConfigureObjectMotion();WorldReady=true;
        if(System.Environment.GetEnvironmentVariable("SE_VULKAN_NATIVE_PASS_SCENE")=="1")ConfigureNativePasses(library);
        ConfigureGpuScene(library,shaderContent);
    }
    void ConfigureObjectMotion()
    {
        try
        {
            string assets = ClientPlugin.Bootstrap.BootstrapAssets.FindDirectory(Path.Combine("Shaders", "object_motion.hlsl"));
            if (assets == null) return;
            string source = Path.Combine(assets, "Shaders", "object_motion.hlsl");
            string cache = Path.Combine(Path.GetTempPath(), "sevk-object-motion");
            using (var compiler = VulkanShaderCompiler.CreateBundled(cache))
            {
                byte[] pixel = CompileMotion(compiler, source, "__pixel_shader", "ps_5_0", null);
                var kinds = new (uint Kind, string Define)[] { (0, "MOTION_RIGID"), (1, "MOTION_GENERIC"), (2, "MOTION_CUBE"), (3, "MOTION_DEFORMED") };
                foreach (var kind in kinds)
                {
                    byte[] vertex = CompileMotion(compiler, source, "__vertex_shader", "vs_5_0", kind.Define);
                    Check(WorldApi.InitMotion(context, kind.Kind, vertex, (ulong)vertex.Length, pixel, (ulong)pixel.Length), context);
                }
            }
            ClientPlugin.Diagnostics.VulkanDiagnostics.Set("Object motion", ClientPlugin.Diagnostics.SubsystemState.Ready, "Rigid, generic, cube, and deformed-cube motion pipelines. Catalog velocity keeps those pixels and fills the rest from depth.");
        }
        catch (Exception error)
        {
            ClientPlugin.Diagnostics.VulkanDiagnostics.Set("Object motion", ClientPlugin.Diagnostics.SubsystemState.Failed, error.Message);
        }
    }
    byte[][] attachmentVs,indexedAttachmentVs;
    void ConfigureIndexedMotion()
    {
        string assets=ClientPlugin.Bootstrap.BootstrapAssets.FindDirectory(Path.Combine("Shaders","indexed_attachment.hlsl"));
        if(assets==null)throw new FileNotFoundException("Indexed motion vertex shader is required for GPU-owned geometry.");
        var initialize=(WorldFunctions.MotionFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_init_indexed_motion"),typeof(WorldFunctions.MotionFn));
        using(var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(Path.GetTempPath(),"sevk-indexed-motion"))){
            byte[] pixel=CompileMotion(compiler,Path.Combine(assets,"Shaders","object_motion.hlsl"),"__pixel_shader","ps_5_0",null);
            for(int kind=0;kind<5;kind++){
                var compiled=compiler.Compile(new VulkanShaderRequest{SourcePath=Path.Combine(assets,"Shaders","indexed_attachment.hlsl"),ContentDirectory=Path.Combine(assets,"Shaders"),EntryPoint="__vertex_shader",Profile="vs_5_0",NativeAbiVersion=1,Defines=new Dictionary<string,string>{{"INDEXED_KIND",kind.ToString(System.Globalization.CultureInfo.InvariantCulture)},{"INDEXED_MOTION","1"}}});
                byte[] vertex=SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiled.SpirvPath));Check(initialize(context,(uint)kind,vertex,(ulong)vertex.Length,pixel,(ulong)pixel.Length),context);
            }
        }
    }
    public bool InitGBufferAttachment(uint slot, uint format, byte[] pixel)
    {
        try
        {
            if (pixel == null || pixel.Length == 0 || slot > 2)
                return false;
            var vertex = AttachmentVertex();
            for (uint kind = 0; kind < 4; kind++)
                Check(WorldApi.InitAttach(context, slot, format, kind, vertex[kind], (ulong)vertex[kind].Length, pixel, (ulong)pixel.Length), context);
            if(GpuSceneReady){
                var indexed=(WorldFunctions.AttachFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_init_indexed_attachment"),typeof(WorldFunctions.AttachFn));
                var gpuVertex=IndexedAttachmentVertex();
                for(uint kind=0;kind<5;kind++)Check(indexed(context,slot,format,kind,gpuVertex[kind],(ulong)gpuVertex[kind].Length,pixel,(ulong)pixel.Length),context);
            }
            return true;
        }
        catch (Exception error)
        {
            ClientPlugin.Diagnostics.VulkanDiagnostics.Set("GBuffer attachment", ClientPlugin.Diagnostics.SubsystemState.Failed, error.Message);
            return false;
        }
    }
    byte[][] IndexedAttachmentVertex()
    {
        if(indexedAttachmentVs!=null)return indexedAttachmentVs;
        string assets=ClientPlugin.Bootstrap.BootstrapAssets.FindDirectory(Path.Combine("Shaders","indexed_attachment.hlsl"));
        if(assets==null)throw new FileNotFoundException("Indexed attachment vertex shader is required for GPU-owned geometry.");
        using(var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(Path.GetTempPath(),"sevk-indexed-attachment"))){
            var result=new byte[5][];
            for(int kind=0;kind<result.Length;kind++){
                var compiled=compiler.Compile(new VulkanShaderRequest{SourcePath=Path.Combine(assets,"Shaders","indexed_attachment.hlsl"),ContentDirectory=Path.Combine(assets,"Shaders"),EntryPoint="__vertex_shader",Profile="vs_5_0",NativeAbiVersion=1,Defines=new Dictionary<string,string>{{"INDEXED_KIND",kind.ToString(System.Globalization.CultureInfo.InvariantCulture)}}});
                result[kind]=SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiled.SpirvPath));
            }
            return indexedAttachmentVs=result;
        }
    }
    byte[][] AttachmentVertex()
    {
        if (attachmentVs != null)
            return attachmentVs;
        string assets = ClientPlugin.Bootstrap.BootstrapAssets.FindDirectory(Path.Combine("Shaders", "object_motion.hlsl"));
        if (assets == null)
            throw new FileNotFoundException("Object motion shader is required for a GBuffer attachment.");
        string source = Path.Combine(assets, "Shaders", "object_motion.hlsl");
        string cache = Path.Combine(Path.GetTempPath(), "sevk-object-motion");
        using (var compiler = VulkanShaderCompiler.CreateBundled(cache))
        {
            var kinds = new[] { "MOTION_RIGID", "MOTION_GENERIC", "MOTION_CUBE", "MOTION_DEFORMED" };
            attachmentVs = new byte[4][];
            for (int i = 0; i < kinds.Length; i++)
                attachmentVs[i] = CompileMotion(compiler, source, "__vertex_shader", "vs_5_0", kinds[i], true);
        }
        return attachmentVs;
    }
    static byte[] CompileMotion(VulkanShaderCompiler compiler, string source, string entry, string profile, string kind, bool attach = false)
    {
        var defines = new Dictionary<string, string>(StringComparer.Ordinal);
        if (kind != null) { defines["MOTION_VERTEX"] = "1"; defines[kind] = "1"; }
        if (attach) defines["ATTACH_VERTEX"] = "1";
        var result = compiler.Compile(new VulkanShaderRequest { SourcePath = source, ContentDirectory = Path.GetDirectoryName(source), EntryPoint = entry, Profile = profile, Defines = defines, NativeAbiVersion = 1 });
        if (result == null || string.IsNullOrEmpty(result.SpirvPath) || !File.Exists(result.SpirvPath)) throw new InvalidDataException("Object motion shader failed to compile.");
        // DXC -fspv-reflect declares Google semantic extensions. The world pass rejects every OpExtension.
        return SpirvRuntimeModule.Prepare(File.ReadAllBytes(result.SpirvPath));
    }
    public void UploadWorldMesh(ulong id,NativeWorldVertex[] vertices,uint[] indices)
    {
        if(vertices==null || indices==null)throw new ArgumentNullException("World geometry is required.");
        Check(WorldApi.Upload(context,id,vertices,(uint)vertices.Length,indices,(uint)indices.Length),context);
    }
    public uint AddWorldPacket(uint pipeline,ulong mesh,ulong color,ulong normal,ulong extensions,ulong alpha,byte[] constants,byte[] instances=null,uint instanceCount=1,uint flags=0,uint cascadeMask=0xFF,Vector3 origin=default,float radius=-1,bool recycleConstants=true,WorldRecipe recipe=null,bool recordLogical=true)
    {
        if(WorldShaderVariants.IsMeshDecal(pipeline))flags|=512u|16384u; // Stock mesh decals do not enter depth/forward passes.
        if(!WorldReady || !WorldLayouts.ContainsKey(pipeline))throw new InvalidOperationException("The stock world pipeline is not ready.");
        if(worldPackets.Count>=65536)throw new InvalidOperationException("World packet batch exceeds 65536 draws.");
        if(recordLogical)NoteLogicalDraw(pipeline,mesh,instanceCount,flags,cascadeMask);
        uint order=++modelSubmissionOrder;NoteMainGroupOrder(flags,order);
#if NET8_0_OR_GREATER
        // Fill the new slot in place: a temporary plus List.Add copies the
        // whole reference-bearing struct through the write-barrier helper.
        int index=worldPackets.Count;CollectionsMarshal.SetCount(worldPackets,index+1);ref var packet=ref CollectionsMarshal.AsSpan(worldPackets)[index];
        packet.Order=order;packet.MainGroup=mainGroup;packet.MainPart=MainPart;packet.OldOrder=(flags&OldGeometryOrder)!=0?oldPartOrder:default;packet.Recipe=recipe;packet.Pipeline=pipeline;packet.Mesh=mesh;packet.Color=color;packet.Normal=normal;packet.Extensions=extensions;packet.Alpha=alpha;
        packet.Constants=constants;packet.Instances=instances;packet.Count=instanceCount;packet.Flags=flags;packet.CascadeMask=cascadeMask;packet.Origin=origin;packet.Radius=radius;packet.RecycleConstants=recycleConstants;packet.InstanceCapacity=false;
        return (uint)index;
#else
        worldPackets.Add(new WorldPacket{Order=order,MainGroup=mainGroup,MainPart=MainPart,OldOrder=(flags&OldGeometryOrder)!=0?oldPartOrder:default,Recipe=recipe,Pipeline=pipeline,Mesh=mesh,Color=color,Normal=normal,Extensions=extensions,Alpha=alpha,Constants=constants,Instances=instances,Count=instanceCount,Flags=flags,CascadeMask=cascadeMask,Origin=origin,Radius=radius,RecycleConstants=recycleConstants});return (uint)worldPackets.Count-1;
#endif
    }
    public void QueueWorld(byte[] frame,float[] projection,float[] inverse)
    {
        if(frame==null || frame.Length!=1280 || projection==null || projection.Length!=16 || inverse==null || inverse.Length!=16)throw new ArgumentException("Invalid stock frame matrices/constants.");
        FinalizeModelBatches();
        if(worldPackets.Count==0){Check(WorldApi.Queue(context,frame,(uint)frame.Length,projection,inverse,null,0),context);QueueMainOrder();QueueNativeModels();QueueTransparency();QueueDecals();QueueHighlights();QueueShadowExclusions();return;}
        if(RetainedSubmissionEnabled && !ObjectRetentionEnabled){QueueWorldBlob(frame,projection,inverse);return;}
        long packedAt=PacketProfilingEnabled?System.Diagnostics.Stopwatch.GetTimestamp():0;
        if(drawScratch.Length<worldPackets.Count)drawScratch=new NativeWorldDraw[worldPackets.Count];
        if(ObjectRetentionEnabled && objectIdentityScratch.Length<worldPackets.Count)objectIdentityScratch=new ulong[worldPackets.Count];
        instanceOffsets.Clear();int constantUsed=0,instanceUsed=0;
        for(int i=0;i<worldPackets.Count;++i)
        {
            var packet=worldPackets[i];long instanceBytes=packet.Count*64L;if(packet.Constants==null || packet.Instances!=null && (packet.InstanceCapacity?packet.Instances.LongLength<instanceBytes:packet.Instances.LongLength!=instanceBytes))throw new ArgumentException("World object/instance data mismatch.");
            if(ObjectRetentionEnabled){
                // Recyclable frame constants have no persistent ownership. Give
                // their layout/packet position a reusable storage identity; native
                // byte comparison remains authoritative on every frame.
                objectIdentityScratch[i]=packet.RecycleConstants
                    ? (1UL<<63)|((ulong)(uint)packet.Constants.Length<<32)|(uint)i
                    : objectIdentities.GetValue(packet.Constants,_=>new ObjectIdentity{Id=++nextObjectIdentity}).Id;
            }
            Grow(ref constantArena,constantUsed,packet.Constants.Length);Buffer.BlockCopy(packet.Constants,0,constantArena,constantUsed,packet.Constants.Length);
            int instanceAt=0;if(packet.Instances!=null && !instanceOffsets.TryGetValue(packet.Instances,out instanceAt)){instanceAt=instanceUsed;int bytes=checked((int)instanceBytes);Grow(ref instanceArena,instanceUsed,bytes);Buffer.BlockCopy(packet.Instances,0,instanceArena,instanceUsed,bytes);instanceUsed+=bytes;instanceOffsets.Add(packet.Instances,instanceAt);}
            drawScratch[i]=new NativeWorldDraw{Size=WorldDrawSize,Pipeline=packet.Pipeline,Count=packet.Count,Flags=packet.Flags,Mesh=packet.Mesh,Color=packet.Color,Normal=packet.Normal,Extensions=packet.Extensions,Alpha=packet.Alpha,ConstantBytes=(uint)packet.Constants.Length,Reserved=packet.CascadeMask,OriginX=packet.Origin.X,OriginY=packet.Origin.Y,OriginZ=packet.Origin.Z,Radius=packet.Radius,
                Constants=new IntPtr(constantUsed),Instances=new IntPtr(packet.Instances==null?-1:instanceAt)};
            constantUsed+=packet.Constants.Length;
            // Offsets are applied after the arenas are pinned.
        }
        var constantPin=GCHandle.Alloc(constantArena,GCHandleType.Pinned);var instancePin=instanceUsed>0?GCHandle.Alloc(instanceArena,GCHandleType.Pinned):default;
        try
        {
            IntPtr constants=constantPin.AddrOfPinnedObject(),instances=instanceUsed>0?instancePin.AddrOfPinnedObject():IntPtr.Zero;
            for(int i=0;i<worldPackets.Count;++i)
            {
                // The first pass stored bounded offsets. Resolve them in place
                // after pinning, without re-reading packets or hashing arrays.
                ref var draw=ref drawScratch[i];
                draw.Constants=constants+draw.Constants.ToInt32();
                int instanceAt=draw.Instances.ToInt32();draw.Instances=instanceAt<0?IntPtr.Zero:instances+instanceAt;
            }
            if(PacketProfilingEnabled){packetObjectBytes=constantUsed;packetInstanceBytes=instanceUsed;long now=System.Diagnostics.Stopwatch.GetTimestamp();managedPackTicks+=now-packedAt;packedAt=now;}
            if(ObjectRetentionEnabled){
                if(retainedQueue==null)retainedQueue=(RetainedQueueFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_queue_world_retained"),typeof(RetainedQueueFn));
                Check(retainedQueue(context,frame,(uint)frame.Length,projection,inverse,drawScratch,(uint)worldPackets.Count,objectIdentityScratch),context);
            }else Check(WorldApi.Queue(context,frame,(uint)frame.Length,projection,inverse,drawScratch,(uint)worldPackets.Count),context);
            if(PacketProfilingEnabled){long now=System.Diagnostics.Stopwatch.GetTimestamp();nativeQueueTicks+=now-packedAt;packedAt=now;}
            QueueMainOrder();QueueTransparency();QueueDecals();QueueHighlights();QueueShadowExclusions();
            if(PacketProfilingEnabled)auxiliaryQueueTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-packedAt;
        }
        finally{constantPin.Free();if(instancePin.IsAllocated)instancePin.Free();}
    }
    private void ClearWorldPackets(){ShadowExclusions=Array.Empty<NativeShadowExclusion>();if(WorldReady)QueueShadowExclusions();for(int i=0;i<worldPackets.Count;i++){ref readonly var packet=ref PacketAt(i);if(packet.RecycleConstants)ReflectedConstantBuffer.Recycle(packet.Constants);}worldPackets.Clear();if(WorldReady)QueueOcclusion(0,Array.Empty<NativeOcclusionDraw>());highlightPackets.Clear();if(WorldReady)Check(HighlightApi.Queue(context,null,0),context);if(WorldReady)Check(WorldApi.Queue(context,null,0,null,null,null,0),context);}
    public void UploadWorldCube(ulong id,int width,int height,int format,byte[] bytes,bool srgb)
        => Check(WorldApi.Cube(context,id,(uint)width,(uint)height,TextureFormat(format),srgb?1u:0u,bytes,(ulong)bytes.Length),context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int CubeMipsFn(IntPtr ctx,ulong id,uint width,uint height,uint format,uint srgb,byte[] data,ulong bytes,uint mips);
    private CubeMipsFn cubeMips;
    public void UploadWorldCubeMips(ulong id,int width,int height,int format,byte[][][] faces,bool srgb)
    {
        if(faces==null || faces.Length!=6 || faces[0]==null || faces[0].Length==0)throw new ArgumentException("Cubemaps require six complete face mip chains.");
        int mips=faces[0].Length,total=0;
        foreach(var face in faces){if(face==null || face.Length!=mips)throw new ArgumentException("Cubemap faces require matching mip counts.");foreach(var mip in face){if(mip==null)throw new ArgumentException("Cubemap mip bytes are missing.");total=checked(total+mip.Length);}}
        var bytes=new byte[total];int offset=0;foreach(var face in faces)foreach(var mip in face){Buffer.BlockCopy(mip,0,bytes,offset,mip.Length);offset+=mip.Length;}
        if(mips==1){UploadWorldCube(id,width,height,format,bytes,srgb);return;}
        if(cubeMips==null)cubeMips=(CubeMipsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_upload_world_cube_mips"),typeof(CubeMipsFn));
        Check(cubeMips(context,id,(uint)width,(uint)height,TextureFormat(format),srgb?1u:0u,bytes,(ulong)bytes.Length,(uint)mips),context);
    }
    public void SetWorldSkybox(ulong id)=>Check(WorldApi.Skybox(context,id),context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int SkyboxesFn(IntPtr context,ulong visible,ulong indirect);
    private SkyboxesFn skyboxes;
    public void SetWorldSkybox(ulong visible,ulong indirect){if(indirect==0 || indirect==visible){SetWorldSkybox(visible);return;}skyboxes??=(SkyboxesFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_world_skyboxes"),typeof(SkyboxesFn));Check(skyboxes(context,visible,indirect),context);}
}
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeIndirectStats
{
    public uint Size,AbiVersion,Supported,Enabled,MainGroups,ShadowGroups,GroupedDraws,PreparedCommands;
}
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeGeometryArenaStats
{
    public uint Size,AbiVersion,Enabled,VertexPages,IndexPages,Reserved0,Reserved1,Reserved2;
    public ulong VertexLive,IndexLive,VertexCapacity,IndexCapacity;
}
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativePresentationStats
{
    public uint Size,AbiVersion,Swapchain,SurfaceReady,VSync,Mode,Images,Generation;
}
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeQueueValidationStats
{
    public uint Size,AbiVersion,Enabled,Draws,Hits,TextureChecks,VoxelChecks,MeshChecks;
}
public struct WorldPacket
{
    internal NativeBackend.MainGroupState MainGroup;internal uint MainPart;
    internal NativeOldPartOrder OldOrder;
    public NativeBackend.WorldRecipe Recipe;
    public uint Pipeline,Count,Flags,CascadeMask,Order;
    public ulong Mesh,Color,Normal,Extensions,Alpha;
    public byte[] Constants,Instances;
    public Vector3 Origin;
    public float Radius;
    public bool RecycleConstants,InstanceCapacity;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
internal struct NativeWorldDraw
{
    public uint Size,Pipeline,Count,Flags;
    public ulong Mesh,Color,Normal,Extensions,Alpha;
    public IntPtr Constants;
    public uint ConstantBytes,Reserved;
    public IntPtr Instances;
    public float OriginX,OriginY,OriginZ,Radius;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeWorkStats
{
    public uint Size,AbiVersion;
    public ulong UploadBatches,UploadSlotWaits,WorldFrames,WorldDraws,WorldInstances,SkinnedDraws,CubeDraws,LightingPasses;
    public uint SpotUs,ShadowUs,MainUs,ProbeUs,PrepareUs,TailUs,WaitUs,AcquireUs,ResetUs;
}
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativePerformanceStats
{
    public uint Size,AbiVersion,ComposeUs,SpriteUs,FinishUs,SubmitUs,PresentUs;
    public uint MaterialHits,MaterialMisses,MaterialResets,MainCommands,ShadowCommands,SpotCommands,ProbeCommands,LogicalDraws,SubmittedDraws;
    public uint QueueUs,GeometryUs,UploadUs,DescriptorsUs,SortUs;
}
internal sealed class WorldFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int PipelineFn(IntPtr ctx,uint id,byte[] vs,ulong vb,byte[] ps,ulong pb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int MotionFn(IntPtr ctx,uint kind,byte[] vs,ulong vb,byte[] ps,ulong pb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int AttachFn(IntPtr ctx,uint slot,uint format,uint kind,byte[] vs,ulong vb,byte[] ps,ulong pb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int LightingFn(IntPtr ctx,uint kind,byte[] spv,ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int UploadFn(IntPtr ctx,ulong id,[In] NativeWorldVertex[] vertices,uint vc,uint[] indices,uint ic);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int QueueFn(IntPtr ctx,byte[] frame,uint bytes,float[] projection,float[] inverse,[In] NativeWorldDraw[] draws,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CubeFn(IntPtr ctx,ulong id,uint w,uint h,uint format,uint srgb,byte[] bytes,ulong count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SkyboxFn(IntPtr ctx,ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int StatsFn(IntPtr ctx,ref NativeWorkStats stats);
    internal readonly PipelineFn InitPipeline;internal readonly MotionFn InitMotion;internal readonly AttachFn InitAttach;internal readonly LightingFn InitLighting;internal readonly UploadFn Upload;internal readonly QueueFn Queue;internal readonly CubeFn Cube;internal readonly SkyboxFn Skybox;internal readonly StatsFn Stats;internal readonly NativeFunctions.ContextFn BeginUploads,EndUploads;
    internal WorldFunctions(Func<string,IntPtr> symbol)
    {
        T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));
        InitPipeline=Get<PipelineFn>("sevk_init_world_pipeline");InitMotion=Get<MotionFn>("sevk_init_object_motion");InitAttach=Get<AttachFn>("sevk_init_gbuffer_attachment");InitLighting=Get<LightingFn>("sevk_init_world_lighting");Upload=Get<UploadFn>("sevk_upload_world_mesh");Queue=Get<QueueFn>("sevk_queue_world");Cube=Get<CubeFn>("sevk_upload_world_cube");Skybox=Get<SkyboxFn>("sevk_set_world_skybox");Stats=Get<StatsFn>("sevk_get_work_stats");BeginUploads=Get<NativeFunctions.ContextFn>("sevk_begin_uploads");EndUploads=Get<NativeFunctions.ContextFn>("sevk_end_uploads");
    }
}

[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeObjectRecordStats {
    public uint Size,AbiVersion,Enabled,Records,Selected,Changed;
    public ulong LiveBytes,CapacityBytes,UploadedBytes;
}

[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeCascadeRecordStats { public uint Size,AbiVersion,Enabled,Tasks,Commands,Reserved0,Reserved1,Reserved2; }

[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeLightTileStats {public uint Size,AbiVersion,Enabled,Dispatches,Tiles,Points,HaveResult,ReadbackUs;public ulong Frame,CompletedFrame,Assignments,FullAssignments;}
