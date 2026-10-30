using System;
using System.Diagnostics;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    // Diagnostic only: timings add hot-path clock reads. Keep ordinary benchmark
    // results separate from this opt-in lane.
    public bool PacketProfilingEnabled {get;}=Environment.GetEnvironmentVariable("SE_VULKAN_CPU_DETAIL")=="1";
    private long rigidLookupTicks,rigidAppendTicks,managedPackTicks,nativeQueueTicks,auxiliaryQueueTicks,uploadEndTicks;
    private uint partCacheHits,partCacheRefreshes,skinnedParts,createdBatches,batchHintHits;
    private long packetObjectBytes,packetInstanceBytes;
    private void ResetPacketPreparation()
    {
        rigidLookupTicks=rigidAppendTicks=managedPackTicks=nativeQueueTicks=auxiliaryQueueTicks=uploadEndTicks=0;
        partCacheHits=partCacheRefreshes=skinnedParts=createdBatches=batchHintHits=0;packetObjectBytes=packetInstanceBytes=0;
    }
    internal void NotePreparedPart(bool cached,bool skinned=false)
    {
        if(!PacketProfilingEnabled)return;
        if(skinned)skinnedParts++;else if(cached)partCacheHits++;else partCacheRefreshes++;
    }
    public PacketPreparationStats GetPacketPreparationStats()
    {
        double scale=1000.0/Stopwatch.Frequency;
        return new PacketPreparationStats{Enabled=PacketProfilingEnabled,RigidLookupMs=rigidLookupTicks*scale,RigidAppendMs=rigidAppendTicks*scale,
            ManagedPackMs=managedPackTicks*scale,NativeQueueMs=nativeQueueTicks*scale,AuxiliaryQueuesMs=auxiliaryQueueTicks*scale,UploadEndMs=uploadEndTicks*scale,
            PartCacheHits=partCacheHits,PartCacheRefreshes=partCacheRefreshes,SkinnedParts=skinnedParts,CreatedBatches=createdBatches,BatchHintHits=batchHintHits,ResidentBatches=(uint)E.RigidBatches.Count,
            ObjectBytes=packetObjectBytes,InstanceBytes=packetInstanceBytes};
    }
}
public struct PacketPreparationStats
{
    public bool Enabled;
    public double RigidLookupMs,RigidAppendMs,ManagedPackMs,NativeQueueMs,AuxiliaryQueuesMs,UploadEndMs;
    public uint PartCacheHits,PartCacheRefreshes,SkinnedParts,CreatedBatches,BatchHintHits,ResidentBatches;
    public long ObjectBytes,InstanceBytes;
}
