using System;
using System.Runtime.InteropServices;
using ClientPlugin.Diagnostics;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BlobQueueFn(IntPtr ctx,byte[] frame,uint bytes,float[] projection,float[] inverse,IntPtr draws,uint count,IntPtr objects,uint objectBytes,IntPtr instances,uint instanceBytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AlignmentFn(IntPtr ctx,out uint alignment);
    private BlobQueueFn blobQueue;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private unsafe delegate int ObjectArenaFn(IntPtr ctx,uint bytes,byte** objects);
    private ObjectArenaFn objectArena;private bool objectArenaMissing;
    // Plan 2 B4: pack the recipe path's object constants straight into native's queue blob (one copy instead of
    // two). SE_VULKAN_WORLD_ARENA=1; a native library without sevk_world_object_arena keeps the managed arena.
    public bool WorldArenaEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_WORLD_ARENA")=="1";
    private unsafe byte* AcquireObjectArena(int bytes)
    {
        if(bytes==0 || objectArenaMissing)return null;
        if(objectArena==null){try{objectArena=(ObjectArenaFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_object_arena"),typeof(ObjectArenaFn));}catch(Exception){objectArenaMissing=true;return null;}}
        byte* objects;Check(objectArena(context,(uint)bytes,&objects),context);return objects;
    }
    private int objectAlignment;
    private unsafe void QueueWorldBlob(byte[] frame,float[] projection,float[] inverse)
    {
        long started=PacketProfilingEnabled?System.Diagnostics.Stopwatch.GetTimestamp():0;
        if(blobQueue==null){blobQueue=(BlobQueueFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_queue_world_blob"),typeof(BlobQueueFn));var get=(AlignmentFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_object_alignment"),typeof(AlignmentFn));Check(get(context,out uint alignment),context);if(alignment==0 || alignment>65536)throw new InvalidOperationException("Invalid world object alignment.");objectAlignment=(int)alignment;}
        bool recipes=WorldRecipesEnabled;
        if(recipes){if(recipeScratch.Length<worldPackets.Count)recipeScratch=new NativeRecipeDraw[worldPackets.Count];if(queueRecipes==null)queueRecipes=(QueueRecipesFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_queue_world_recipes"),typeof(QueueRecipesFn));}
        else if(drawScratch.Length<worldPackets.Count)drawScratch=new NativeWorldDraw[worldPackets.Count];
        instanceOffsets.Clear();int objectUsed=0,instanceUsed=0;byte* arena=null;
        // Collect the blocks to recycle at the next prologue while the packets are in cache (see ClearWorldPackets).
        bool collect=AsyncPacketRecycle;int collected=0;if(collect&&recycleScratch.Length<worldPackets.Count)recycleScratch=new byte[worldPackets.Count][];
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.BlobPack)){
        if(recipes && WorldArenaEnabled){
            long total=0;for(int i=0;i<worldPackets.Count;++i){var constants=PacketAt(i).Constants;if(constants==null)throw new ArgumentException("World object/instance data mismatch.");total+=(constants.Length+objectAlignment-1)/objectAlignment*objectAlignment;}
            if(total>256*1024*1024)throw new ArgumentException("World blob exceeds bounded upload budget.");
            arena=AcquireObjectArena((int)total);
        }
        for(int i=0;i<worldPackets.Count;++i)
        {
            ref readonly var packet=ref PacketAt(i);int bytes=checked((int)(packet.Count*64L));if(collect&&packet.RecycleConstants)recycleScratch[collected++]=packet.Constants;
            if(packet.Constants==null || packet.Instances!=null && (packet.InstanceCapacity?packet.Instances.Length<bytes:packet.Instances.Length!=bytes))throw new ArgumentException("World object/instance data mismatch.");
            int aligned=checked((packet.Constants.Length+objectAlignment-1)/objectAlignment*objectAlignment);
            if((long)objectUsed+aligned>256*1024*1024 || (long)instanceUsed+bytes>256*1024*1024)throw new ArgumentException("World blob exceeds bounded upload budget.");
            if(arena!=null){fixed(byte* source=packet.Constants)Buffer.MemoryCopy(source,arena+objectUsed,aligned,packet.Constants.Length);}
            else{Grow(ref constantArena,objectUsed,aligned);Buffer.BlockCopy(packet.Constants,0,constantArena,objectUsed,packet.Constants.Length);}
            int instanceAt=0;if(packet.Instances!=null && !instanceOffsets.TryGetValue(packet.Instances,out instanceAt)){instanceAt=instanceUsed;Grow(ref instanceArena,instanceUsed,bytes);Buffer.BlockCopy(packet.Instances,0,instanceArena,instanceUsed,bytes);instanceUsed=checked(instanceUsed+bytes);instanceOffsets.Add(packet.Instances,instanceAt);}
            if(recipes){var recipe=PacketRecipe(in packet);recipeScratch[i]=new NativeRecipeDraw{Recipe=recipe.Handle,Count=packet.Count,Flags=packet.Flags,Cascades=packet.CascadeMask,ObjectOffset=(uint)objectUsed,InstanceOffset=(packet.Pipeline==20 || packet.Pipeline==21) || ClientPlugin.Rendering.Shaders.WorldShaderVariants.BasePipeline(packet.Pipeline)%5<2 || packet.Instances==null?uint.MaxValue:(uint)instanceAt,OriginX=packet.Origin.X,OriginY=packet.Origin.Y,OriginZ=packet.Origin.Z,Radius=packet.Radius};}
            else drawScratch[i]=new NativeWorldDraw{Size=WorldDrawSize,Pipeline=packet.Pipeline,Count=packet.Count,Flags=packet.Flags,Mesh=packet.Mesh,Color=packet.Color,Normal=packet.Normal,Extensions=packet.Extensions,Alpha=packet.Alpha,ConstantBytes=(uint)packet.Constants.Length,Reserved=packet.CascadeMask,OriginX=packet.Origin.X,OriginY=packet.Origin.Y,OriginZ=packet.Origin.Z,Radius=packet.Radius,Constants=new IntPtr(objectUsed+1),Instances=packet.Instances==null?IntPtr.Zero:new IntPtr(instanceAt+1)};
            objectUsed=checked(objectUsed+aligned);
            if(objectUsed>256*1024*1024 || instanceUsed>256*1024*1024)throw new ArgumentException("World blob exceeds bounded upload budget.");
        }
        if(collect){precollected=collected;precollectedFor=worldPackets.Count;}
        submissionStats.ObjectBlobBytes=(ulong)objectUsed;submissionStats.InstanceBlobBytes=(ulong)instanceUsed;
        }
        if(PacketProfilingEnabled){packetObjectBytes=objectUsed;packetInstanceBytes=instanceUsed;long now=System.Diagnostics.Stopwatch.GetTimestamp();managedPackTicks+=now-started;started=now;}
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativeWorldQueue)){
        if(recipes){fixed(NativeRecipeDraw* draws=recipeScratch)fixed(byte* objects=constantArena)fixed(byte* instances=instanceArena)
            Check(queueRecipes(context,frame,(uint)frame.Length,projection,inverse,(IntPtr)draws,(uint)worldPackets.Count,arena!=null?(IntPtr)arena:(IntPtr)objects,(uint)objectUsed,(IntPtr)instances,(uint)instanceUsed),context);}
        else {fixed(NativeWorldDraw* draws=drawScratch)fixed(byte* objects=constantArena)fixed(byte* instances=instanceArena)
            Check(blobQueue(context,frame,(uint)frame.Length,projection,inverse,(IntPtr)draws,(uint)worldPackets.Count,(IntPtr)objects,(uint)objectUsed,(IntPtr)instances,(uint)instanceUsed),context);}
        }
        if(PacketProfilingEnabled){long now=System.Diagnostics.Stopwatch.GetTimestamp();nativeQueueTicks+=now-started;started=now;}
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.MainOrderQueue))QueueMainOrder();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativeModelQueue))QueueNativeModels();
        QueueTransparency();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.DecalQueue))QueueDecals();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.HighlightQueue))QueueHighlights();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ExclusionQueue))QueueShadowExclusions();
        // Native OLD cells follow every managed packet, so physical managed indices are unchanged.
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.CellEmit))EmitCells();
        if(PacketProfilingEnabled)auxiliaryQueueTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-started;
    }
}
