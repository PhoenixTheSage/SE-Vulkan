using System;
using System.Runtime.InteropServices;
using ClientPlugin.Diagnostics;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BlobQueueFn(IntPtr ctx,byte[] frame,uint bytes,float[] projection,float[] inverse,IntPtr draws,uint count,IntPtr objects,uint objectBytes,IntPtr instances,uint instanceBytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AlignmentFn(IntPtr ctx,out uint alignment);
    private BlobQueueFn blobQueue;
    private int objectAlignment;
    private unsafe void QueueWorldBlob(byte[] frame,float[] projection,float[] inverse)
    {
        long started=PacketProfilingEnabled?System.Diagnostics.Stopwatch.GetTimestamp():0;
        if(blobQueue==null){blobQueue=(BlobQueueFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_queue_world_blob"),typeof(BlobQueueFn));var get=(AlignmentFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_object_alignment"),typeof(AlignmentFn));Check(get(context,out uint alignment),context);if(alignment==0 || alignment>65536)throw new InvalidOperationException("Invalid world object alignment.");objectAlignment=(int)alignment;}
        bool recipes=WorldRecipesEnabled;
        if(recipes){if(recipeScratch.Length<worldPackets.Count)recipeScratch=new NativeRecipeDraw[worldPackets.Count];if(queueRecipes==null)queueRecipes=(QueueRecipesFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_queue_world_recipes"),typeof(QueueRecipesFn));}
        else if(drawScratch.Length<worldPackets.Count)drawScratch=new NativeWorldDraw[worldPackets.Count];
        instanceOffsets.Clear();int objectUsed=0,instanceUsed=0;
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.BlobPack)){
        for(int i=0;i<worldPackets.Count;++i)
        {
            var packet=worldPackets[i];int bytes=checked((int)(packet.Count*64L));
            if(packet.Constants==null || packet.Instances!=null && (packet.InstanceCapacity?packet.Instances.Length<bytes:packet.Instances.Length!=bytes))throw new ArgumentException("World object/instance data mismatch.");
            int aligned=checked((packet.Constants.Length+objectAlignment-1)/objectAlignment*objectAlignment);
            if((long)objectUsed+aligned>256*1024*1024 || (long)instanceUsed+bytes>256*1024*1024)throw new ArgumentException("World blob exceeds bounded upload budget.");
            Grow(ref constantArena,objectUsed,aligned);Buffer.BlockCopy(packet.Constants,0,constantArena,objectUsed,packet.Constants.Length);
            int instanceAt=0;if(packet.Instances!=null && !instanceOffsets.TryGetValue(packet.Instances,out instanceAt)){instanceAt=instanceUsed;Grow(ref instanceArena,instanceUsed,bytes);Buffer.BlockCopy(packet.Instances,0,instanceArena,instanceUsed,bytes);instanceUsed=checked(instanceUsed+bytes);instanceOffsets.Add(packet.Instances,instanceAt);}
            if(recipes){var recipe=PacketRecipe(packet);recipeScratch[i]=new NativeRecipeDraw{Recipe=recipe.Handle,Count=packet.Count,Flags=packet.Flags,Cascades=packet.CascadeMask,ObjectOffset=(uint)objectUsed,InstanceOffset=(packet.Pipeline==20 || packet.Pipeline==21) || ClientPlugin.Rendering.Shaders.WorldShaderVariants.BasePipeline(packet.Pipeline)%5<2 || packet.Instances==null?uint.MaxValue:(uint)instanceAt,OriginX=packet.Origin.X,OriginY=packet.Origin.Y,OriginZ=packet.Origin.Z,Radius=packet.Radius};}
            else drawScratch[i]=new NativeWorldDraw{Size=WorldDrawSize,Pipeline=packet.Pipeline,Count=packet.Count,Flags=packet.Flags,Mesh=packet.Mesh,Color=packet.Color,Normal=packet.Normal,Extensions=packet.Extensions,Alpha=packet.Alpha,ConstantBytes=(uint)packet.Constants.Length,Reserved=packet.CascadeMask,OriginX=packet.Origin.X,OriginY=packet.Origin.Y,OriginZ=packet.Origin.Z,Radius=packet.Radius,Constants=new IntPtr(objectUsed+1),Instances=packet.Instances==null?IntPtr.Zero:new IntPtr(instanceAt+1)};
            objectUsed=checked(objectUsed+aligned);
            if(objectUsed>256*1024*1024 || instanceUsed>256*1024*1024)throw new ArgumentException("World blob exceeds bounded upload budget.");
        }
        submissionStats.ObjectBlobBytes=(ulong)objectUsed;submissionStats.InstanceBlobBytes=(ulong)instanceUsed;
        }
        if(PacketProfilingEnabled){packetObjectBytes=objectUsed;packetInstanceBytes=instanceUsed;long now=System.Diagnostics.Stopwatch.GetTimestamp();managedPackTicks+=now-started;started=now;}
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativeWorldQueue)){
        if(recipes){fixed(NativeRecipeDraw* draws=recipeScratch)fixed(byte* objects=constantArena)fixed(byte* instances=instanceArena)
            Check(queueRecipes(context,frame,(uint)frame.Length,projection,inverse,(IntPtr)draws,(uint)worldPackets.Count,(IntPtr)objects,(uint)objectUsed,(IntPtr)instances,(uint)instanceUsed),context);}
        else {fixed(NativeWorldDraw* draws=drawScratch)fixed(byte* objects=constantArena)fixed(byte* instances=instanceArena)
            Check(blobQueue(context,frame,(uint)frame.Length,projection,inverse,(IntPtr)draws,(uint)worldPackets.Count,(IntPtr)objects,(uint)objectUsed,(IntPtr)instances,(uint)instanceUsed),context);}
        }
        if(PacketProfilingEnabled){long now=System.Diagnostics.Stopwatch.GetTimestamp();nativeQueueTicks+=now-started;started=now;}
        QueueMainOrder();QueueNativeModels();
        QueueTransparency();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.DecalQueue))QueueDecals();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.HighlightQueue))QueueHighlights();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ExclusionQueue))QueueShadowExclusions();
        if(PacketProfilingEnabled)auxiliaryQueueTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-started;
    }
}
