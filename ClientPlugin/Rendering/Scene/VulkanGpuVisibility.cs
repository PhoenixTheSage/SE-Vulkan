using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    static readonly string GpuVisibleProfilePath=System.Environment.GetEnvironmentVariable("SE_VULKAN_GPU_VISIBLE_PROFILE");
    static readonly bool GpuVisiblePacked=System.Environment.GetEnvironmentVariable("SE_VULKAN_GPU_VISIBLE_PACKED")!="0";
    static readonly bool GpuVisiblePackedVerify=System.Environment.GetEnvironmentVariable("SE_VULKAN_GPU_VISIBLE_PACKED_VERIFY")=="1";
    static readonly bool GpuVisibleDense=System.Environment.GetEnvironmentVariable("SE_VULKAN_GPU_VISIBLE_DENSE")!="0";
    readonly DenseGpuVisibility denseGpuVisibility=new();
    readonly HashSet<uint> gpuVisibleReference=new();
    long gpuVisibleFrames,gpuVisibleSamples,gpuVisibleProfileWritten,gpuVisibleGatherTicks,gpuVisiblePredicateTicks,gpuVisibleAddTicks;
    long gpuVisibleGathered,gpuVisibleAdmitted;
    struct GpuVisibleRow {public BoundingBoxD Bounds;public uint Id;public RenderFlags Flags;public bool Destroyed,Owned;public ActorData Actor;}
    GpuVisibleRow[] gpuVisibleRows=Array.Empty<GpuVisibleRow>();
    uint[] gpuVisibleIds=Array.Empty<uint>();
    int gpuVisibleRowCount,gpuVisibleIdCount;
    double[] gpuVisibleMatrix;
    [DataContract]public sealed class GpuVisibleProfileReceipt
    {
        [DataMember]public bool Instrumented,Fast,Verify,Packed,PackedVerify,Dense;
        [DataMember]public long Frames,Samples;
        [DataMember]public int Stride;
        [DataMember]public double GatherMs,PredicateMs,AddMs,Actors,Admitted;
        [DataMember]public string Scope;
        [DataMember]public GpuVisibleLastReceipt Last;
    }
    [DataContract]public sealed class GpuVisibleLastReceipt
    {
        [DataMember]public double[] Matrix;
        [DataMember]public uint SkipMainMask;
        [DataMember]public GpuVisibleRowReceipt[] Rows;
        [DataMember]public uint[] Visible;
    }
    [DataContract]public sealed class GpuVisibleRowReceipt
    {
        [DataMember]public uint Id,Flags;
        [DataMember]public bool Destroyed,Owned;
        [DataMember]public double[] Bounds;
    }
    void PublishGpuOwnedVisible(in FrustumVisibility prepared)
    {
        while(denseGpuVisibility.TryPruneUnowned(out object stale))gpuOwnedActors.Remove((ActorData)stale);
        if(GpuVisiblePackedVerify){
            gpuVisibleReference.Clear();gpuVisibleReference.UnionWith(visible);
            // Use the original registration set, independently of the compact
            // iteration, so a missing dense actor cannot validate itself.
            foreach(var data in gpuOwnedActors){var frame=BuildFrameOf(data);bool expected=!frame.Destroyed&&data.GpuOwned&&(data.Flags&RenderFlags.SkipInMainView)==0&&(frustum==null||frustum.Contains(frame.Bounds)!=ContainmentType.Disjoint);VerifyGpuVisibleActor(data,expected);}}
        // Opt-in only: sample whole loops, with four clock reads per 64th frame.
        // The sampled frame gathers the same immutable Build inputs once, then
        // evaluates and publishes them. Ordinary frames use one admission loop.
        if(!string.IsNullOrEmpty(GpuVisibleProfilePath)&&++gpuVisibleFrames%64==0)ProfileGpuOwnedVisible(in prepared);
        else if(GpuVisiblePacked&&GpuVisibleDense)
        {
            var bounds=packedActorBounds.Frame(buildParity);var metadata=denseGpuVisibility.Active;int count=denseGpuVisibility.Count;
            for(int i=0;i<count;i++){
                ref readonly var entry=ref metadata[i];ref readonly var row=ref PackedActorBounds.Read(bounds,entry.BoundsIndex,entry.BoundsToken);
                bool admitted=!row.Destroyed&&(entry.Flags&(uint)RenderFlags.SkipInMainView)==0&&GpuVisibleBounds(in prepared,in row.Bounds);
                if(GpuVisiblePackedVerify)VerifyDenseGpuVisibleActor(in entry,admitted);
                if(row.Destroyed)gpuOwnedStale.Add((ActorData)entry.Handle.Owner);else if(admitted)visible.Add(entry.Id);
            }
        }
        else if(GpuVisiblePacked)
        {
            var rows=packedActorBounds.Frame(buildParity);
            foreach(var data in gpuOwnedActors)
            {
                ref readonly var row=ref PackedActorBounds.Read(rows,data.BoundsIndex,data.BoundsToken);
                bool stale=row.Destroyed||!data.GpuOwned;
                bool admitted=!stale&&(data.Flags&RenderFlags.SkipInMainView)==0&&GpuVisibleBounds(in prepared,in row.Bounds);
                if(GpuVisiblePackedVerify)VerifyGpuVisibleActor(data,admitted);
                if(stale)gpuOwnedStale.Add(data);else if(admitted)visible.Add(data.Id);
            }
        }
        else foreach(var data in gpuOwnedActors)
        {
            var frame=BuildFrameOf(data);
            bool stale=frame.Destroyed||!data.GpuOwned;
            bool admitted=!stale&&(data.Flags&RenderFlags.SkipInMainView)==0&&GpuVisibleBounds(in prepared,in frame.Bounds);
            if(GpuVisiblePackedVerify)VerifyGpuVisibleActor(data,admitted);
            if(stale)gpuOwnedStale.Add(data);else if(admitted)visible.Add(data.Id);
        }
        foreach(var data in gpuOwnedStale)UntrackGpuVisibleActor(data);gpuOwnedStale.Clear();
        if(GpuVisiblePackedVerify&&!gpuVisibleReference.SetEquals(visible))throw new InvalidOperationException("Packed GPU game-visible publication differs from the current Build-frame oracle");
    }
    void TrackGpuVisibleActor(ActorData data){gpuOwnedActors.Add(data);data.VisibilityRegistration?.SetRegistered(true);}
    void UntrackGpuVisibleActor(ActorData data){gpuOwnedActors.Remove(data);data.VisibilityRegistration?.SetRegistered(false);}
    void VerifyDenseGpuVisibleActor(in DenseGpuVisibility.Row entry,bool admitted)
    {
        var data=(ActorData)entry.Handle.Owner;
        if(!gpuOwnedActors.Contains(data)||!data.GpuOwned||entry.Id!=data.Id||entry.Flags!=(uint)data.Flags||entry.BoundsIndex!=data.BoundsIndex||entry.BoundsToken!=data.BoundsToken)
            throw new InvalidOperationException("Dense GPU eligibility differs from current registered actor state: "+entry.Id);
        VerifyGpuVisibleActor(data,admitted);
    }
    void VerifyGpuVisibleActor(ActorData data,bool admitted)
    {
        var frame=BuildFrameOf(data);ref readonly var packed=ref PackedActorBounds.Read(packedActorBounds.Frame(buildParity),data.BoundsIndex,data.BoundsToken);
        var a=packed.Bounds;var b=frame.Bounds;
        if(packed.Destroyed!=frame.Destroyed||!a.Min.X.Equals(b.Min.X)||!a.Min.Y.Equals(b.Min.Y)||!a.Min.Z.Equals(b.Min.Z)||!a.Max.X.Equals(b.Max.X)||!a.Max.Y.Equals(b.Max.Y)||!a.Max.Z.Equals(b.Max.Z))
            throw new InvalidOperationException("Packed GPU actor bounds/destruction differ from the current Build snapshot: "+data.Id);
        bool expected=!frame.Destroyed&&data.GpuOwned&&(data.Flags&RenderFlags.SkipInMainView)==0&&(frustum==null||frustum.Contains(frame.Bounds)!=ContainmentType.Disjoint);
        if(expected!=admitted)throw new InvalidOperationException("Packed GPU game-visible admission differs from the game oracle: "+data.Id);
        if(expected)gpuVisibleReference.Add(data.Id);
    }
    void ProfileGpuOwnedVisible(in FrustumVisibility prepared)
    {
        bool dense=GpuVisiblePacked&&GpuVisibleDense;int count=dense?denseGpuVisibility.Count:gpuOwnedActors.Count;
        if(gpuVisibleRows.Length<count){gpuVisibleRows=new GpuVisibleRow[count*2];gpuVisibleIds=new uint[count*2];}
        long begin=Stopwatch.GetTimestamp();int row=0;
        var packedRows=packedActorBounds.Frame(buildParity);
        if(dense){var metadata=denseGpuVisibility.Active;
            for(int i=0;i<count;i++){ref readonly var entry=ref metadata[i];ref readonly var packed=ref PackedActorBounds.Read(packedRows,entry.BoundsIndex,entry.BoundsToken);
                var data=GpuVisiblePackedVerify?(ActorData)entry.Handle.Owner:null;
                if(GpuVisiblePackedVerify)VerifyDenseGpuVisibleActor(in entry,!packed.Destroyed&&(entry.Flags&(uint)RenderFlags.SkipInMainView)==0&&GpuVisibleBounds(in prepared,in packed.Bounds));
                gpuVisibleRows[row++]=new GpuVisibleRow{Bounds=packed.Bounds,Id=entry.Id,Flags=(RenderFlags)entry.Flags,Destroyed=packed.Destroyed,Owned=true,Actor=data};
                if(packed.Destroyed)gpuOwnedStale.Add((ActorData)entry.Handle.Owner);}}
        else foreach(var data in gpuOwnedActors)
        {
            BoundingBoxD bounds;bool destroyed;
            if(GpuVisiblePacked){ref readonly var packed=ref PackedActorBounds.Read(packedRows,data.BoundsIndex,data.BoundsToken);bounds=packed.Bounds;destroyed=packed.Destroyed;}
            else{var frame=BuildFrameOf(data);bounds=frame.Bounds;destroyed=frame.Destroyed;}
            gpuVisibleRows[row++]=new GpuVisibleRow{Bounds=bounds,Id=data.Id,Flags=data.Flags,Destroyed=destroyed,Owned=data.GpuOwned,Actor=data};
            if(destroyed||!data.GpuOwned)gpuOwnedStale.Add(data);
        }
        long gathered=Stopwatch.GetTimestamp();int admitted=0;
        for(int i=0;i<count;i++)
        {
            ref var item=ref gpuVisibleRows[i];
            bool selected=!item.Destroyed&&item.Owned&&(item.Flags&RenderFlags.SkipInMainView)==0&&GpuVisibleBounds(in prepared,in item.Bounds);
            if(GpuVisiblePackedVerify)VerifyGpuVisibleActor(item.Actor,selected);
            if(selected)gpuVisibleIds[admitted++]=item.Id;
        }
        long tested=Stopwatch.GetTimestamp();
        for(int i=0;i<admitted;i++)visible.Add(gpuVisibleIds[i]);
        long added=Stopwatch.GetTimestamp();
        gpuVisibleSamples++;gpuVisibleGatherTicks+=gathered-begin;gpuVisiblePredicateTicks+=tested-gathered;gpuVisibleAddTicks+=added-tested;
        gpuVisibleGathered+=count;gpuVisibleAdmitted+=admitted;gpuVisibleRowCount=count;gpuVisibleIdCount=admitted;
        if(frustum==null)gpuVisibleMatrix=null;
        else{var m=frustum.Matrix;gpuVisibleMatrix=new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};}
    }
    void WriteGpuVisibleProfile()
    {
        try{
        if(string.IsNullOrEmpty(GpuVisibleProfilePath)||gpuVisibleSamples==0||gpuVisibleProfileWritten==gpuVisibleSamples)return;
        double ms=1000.0/Stopwatch.Frequency/gpuVisibleSamples;
        var receipt=new GpuVisibleProfileReceipt{Instrumented=true,Frames=gpuVisibleFrames,Samples=gpuVisibleSamples,Stride=64,
            GatherMs=gpuVisibleGatherTicks*ms,PredicateMs=gpuVisiblePredicateTicks*ms,AddMs=gpuVisibleAddTicks*ms,
            Actors=(double)gpuVisibleGathered/gpuVisibleSamples,Admitted=(double)gpuVisibleAdmitted/gpuVisibleSamples,
            Fast=GpuVisibleFast,Verify=GpuVisibleVerify,Packed=GpuVisiblePacked,PackedVerify=GpuVisiblePackedVerify,Dense=GpuVisibleDense,
            Scope="Diagnostic bulk loops every 64th Build frame; same current Build snapshot and game-visible population, no GPU readback. Latest sampled inputs support independent CPU replay. Subscopes sum only for these sequential sampled loops.",
            Last=new GpuVisibleLastReceipt{Matrix=gpuVisibleMatrix,SkipMainMask=(uint)RenderFlags.SkipInMainView,Rows=gpuVisibleRows.Take(gpuVisibleRowCount).Select(r=>new GpuVisibleRowReceipt{Id=r.Id,Flags=(uint)r.Flags,Destroyed=r.Destroyed,Owned=r.Owned,Bounds=new[]{r.Bounds.Min.X,r.Bounds.Min.Y,r.Bounds.Min.Z,r.Bounds.Max.X,r.Bounds.Max.Y,r.Bounds.Max.Z}}).ToArray(),Visible=gpuVisibleIds.Take(gpuVisibleIdCount).ToArray()}};
        string path=Path.GetFullPath(GpuVisibleProfilePath);Directory.CreateDirectory(Path.GetDirectoryName(path));
        using(var file=File.Create(path))new DataContractJsonSerializer(typeof(GpuVisibleProfileReceipt)).WriteObject(file,receipt);
        gpuVisibleProfileWritten=gpuVisibleSamples;
        }finally{
            // Clear the entire capacity, including rows beyond the latest
            // sample count. Numeric replay inputs may remain; actor lifetimes
            // must not be extended by a diagnostic sample after unload.
            for(int i=0;i<gpuVisibleRows.Length;i++)gpuVisibleRows[i].Actor=null;
        }
    }
}
