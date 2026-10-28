using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Rendering.Interop;
using VRage.Render.Scene;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    public bool GroupOcclusionEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_GROUP_OCCLUSION")!="0";
    private sealed class GroupVisibility
    {
        public MyActor Root;
        public ulong QueryId;
        public BoundingBoxD Bounds;
        public readonly HashSet<ActorData> Members=new();
        // Incremental bounds (default; SE_VULKAN_INCREMENTAL_GROUP_BOUNDS=0 turns them off): members dirtied since the last refresh, and whether
        // only a full recompute is valid (new group, membership rebuild).
        public readonly HashSet<ActorData> DirtyMembers=new();public bool FullDirty=true;
        public readonly StockGroupVisibility State=new();
        public readonly StockGroupVisibility[] ShadowStates=System.Linq.Enumerable.Range(0,8).Select(_=>new StockGroupVisibility()).ToArray();
        public readonly long[] ShadowSeen={-1,-1,-1,-1,-1,-1,-1,-1};public uint ShadowHiddenMask;
        public long Seen=-1;
        public bool Hidden;
    }
    private struct GroupQueryOwner {public GroupVisibility Group;public ulong Epoch;public long Frame;public BoundingBoxD Bounds;}
    private readonly Dictionary<MyActor,GroupVisibility> occlusionGroups=new();
    private readonly HashSet<GroupVisibility> dirtyOcclusionGroups=new();
    private readonly Dictionary<ulong,Dictionary<ulong,GroupQueryOwner>> groupQueryOwners=new();
    private readonly Dictionary<ulong,GroupQueryOwner>[] groupOwnerRing=new Dictionary<ulong,GroupQueryOwner>[8];
    private Dictionary<ulong,GroupQueryOwner> groupOwners;
    private ulong nextGroupQueryId,groupObservedToken;
    private bool groupMembershipDirty=true,previousGroupMode;
    private float groupNear=.1f;
    private float groupFov;
    private bool groupQueriesDisabled;
    private long groupAccepted,groupRejected,groupQueries,groupReentries;
    public object GetGroupOcclusionStats()=>new{Enabled=GroupOcclusionEnabled,Groups=occlusionGroups.Count,Accepted=groupAccepted,Rejected=groupRejected,Queries=groupQueries,Reentries=groupReentries,SceneCulls=OcclusionCulls};
    private void DirtyGroup(ActorData data){if(!building){applyGroupDirty.Add(data);return;}DirtyGroupNow(data);}
    private void DirtyGroupNow(ActorData data){if(data.OcclusionGroup!=null){dirtyOcclusionGroups.Add(data.OcclusionGroup);if(IncrementalGroupBounds)data.OcclusionGroup.DirtyMembers.Add(data);}}
    internal static readonly bool IncrementalGroupBounds=System.Environment.GetEnvironmentVariable("SE_VULKAN_INCREMENTAL_GROUP_BOUNDS")!="0";
    private void RefreshGroupBounds()
    {
        if(groupMembershipDirty){
            foreach(var group in occlusionGroups.Values){group.Members.Clear();group.State.Reset();foreach(var state in group.ShadowStates)state.Reset();}
            foreach(var data in buildActors.Values){DirtyNativePass(data,appearance:false);data.OcclusionGroup=null;if(data.Model==null)continue;var root=BuildFrameOf(data).GroupRoot;if(root!=null){
                if(!occlusionGroups.TryGetValue(root,out var group)){group=new GroupVisibility{Root=root,QueryId=++nextGroupQueryId};occlusionGroups.Add(root,group);}group.Members.Add(data);data.OcclusionGroup=group;break;}}
            foreach(var group in occlusionGroups.Values){dirtyOcclusionGroups.Add(group);group.FullDirty=true;}groupMembershipDirty=false;
        }
        foreach(var group in dirtyOcclusionGroups){
            // A group box is the min/max over member boxes. Removing a member box strictly inside on every axis leaves
            // the extremes unchanged and adding one only grows them, so the incremental result equals the full one;
            // anything that touched an extreme falls back to the full recompute. Moves, geometry and visibility
            // changes (MarkVisibility recurses into children) all reach DirtyGroup, and membership changes go full.
            if(IncrementalGroupBounds && !group.FullDirty && group.Bounds.Min.IsValid() && group.Bounds.Max.IsValid() && group.Bounds.Min.X<=group.Bounds.Max.X && RefreshGroupIncremental(group)){group.DirtyMembers.Clear();if(VerifyGroupBounds)VerifyGroup(group);continue;}
            var box=BoundingBoxD.CreateInvalid();foreach(var member in group.Members){bool contributes=!BuildFrameOf(member).Destroyed && BuildFrameOf(member).Visible;if(contributes)box.Include(member.Bounds);member.GroupContributes=contributes;member.GroupContribution=member.Bounds;}
            group.Bounds=box;group.FullDirty=false;group.DirtyMembers.Clear();}
        dirtyOcclusionGroups.Clear();
    }
    // SE_VULKAN_INCREMENTAL_GROUP_BOUNDS_VERIFY=1: every incremental box is compared with a full recompute.
    static readonly bool VerifyGroupBounds=System.Environment.GetEnvironmentVariable("SE_VULKAN_INCREMENTAL_GROUP_BOUNDS_VERIFY")=="1";
    static long groupVerified,groupMismatches;
    private void VerifyGroup(GroupVisibility group)
    {
        var full=BoundingBoxD.CreateInvalid();foreach(var member in group.Members)if(!BuildFrameOf(member).Destroyed && BuildFrameOf(member).Visible)full.Include(member.Bounds);
        groupVerified++;if(full.Min!=group.Bounds.Min||full.Max!=group.Bounds.Max)groupMismatches++;
        if((groupVerified&255)==0)ClientPlugin.Diagnostics.VulkanDiagnostics.Set("Group bounds verify",groupMismatches==0?ClientPlugin.Diagnostics.SubsystemState.Active:ClientPlugin.Diagnostics.SubsystemState.Failed,"incremental="+groupVerified+"; mismatches="+groupMismatches);
    }
    // Applies the dirty members' changes to the group box; false when an old contribution touched an extreme
    // (the box could shrink), so the caller recomputes in full.
    private bool RefreshGroupIncremental(GroupVisibility group)
    {
        var box=group.Bounds;
        foreach(var member in group.DirtyMembers)
        {
            if(!group.Members.Contains(member))return false;
            if(member.GroupContributes){var old=member.GroupContribution;
                if(!(old.Min.X>box.Min.X&&old.Min.Y>box.Min.Y&&old.Min.Z>box.Min.Z&&old.Max.X<box.Max.X&&old.Max.Y<box.Max.Y&&old.Max.Z<box.Max.Z))return false;}
        }
        foreach(var member in group.DirtyMembers)
        {
            bool contributes=!BuildFrameOf(member).Destroyed && BuildFrameOf(member).Visible;
            var memberBounds=BuildFrameOf(member).Bounds;
            if(contributes)box.Include(memberBounds);
            member.GroupContributes=contributes;member.GroupContribution=memberBounds;
        }
        group.Bounds=box;return true;
    }
    private void BeginGroupOcclusion()
    {
        using(ClientPlugin.Diagnostics.RendererCpuProfiler.Measure(ClientPlugin.Diagnostics.RendererCpuProfiler.Phase.OcclusionBounds))RefreshGroupBounds();
        using var resultsScope=ClientPlugin.Diagnostics.RendererCpuProfiler.Measure(ClientPlugin.Diagnostics.RendererCpuProfiler.Phase.OcclusionResults);
        bool disabled=RenderSettings.DisableOcclusionQueries||RenderSettings.IgnoreOcclusionQueries;
        if(visibilityWidth!=backend.PresentationWidth||visibilityHeight!=backend.PresentationHeight||visibilityFraction!=backend.RenderFraction||groupFov!=lodFov||disabled!=groupQueriesDisabled){foreach(var group in occlusionGroups.Values)group.State.Reset();groupQueryOwners.Clear();visibilityWidth=backend.PresentationWidth;visibilityHeight=backend.PresentationHeight;visibilityFraction=backend.RenderFraction;groupFov=lodFov;groupQueriesDisabled=disabled;}
        var results=backend.GetOcclusionBorrowed(out int count,out ulong token);
        if(token!=groupObservedToken && groupQueryOwners.TryGetValue(token,out var owners)){
            groupObservedToken=token;for(int i=0;i<count;i++){var result=results[i];if(!owners.TryGetValue(result.Id,out var owner))continue;var group=owner.Group;
                if(group.Root.IsDestroyed || !occlusionGroups.TryGetValue(group.Root,out var active) || !ReferenceEquals(active,group) || !group.State.Accept(owner.Epoch,owner.Frame,BuildFrameCounter,owner.Bounds,group.Bounds,result.Visible!=0))groupRejected++;else groupAccepted++;}}
        ulong next=visibilityToken+1;int slot=(int)(next%8);groupOwners=groupOwnerRing[slot]??(groupOwnerRing[slot]=new Dictionary<ulong,GroupQueryOwner>());if(next>8)groupQueryOwners.Remove(next-8);groupOwners.Clear();packedOcclusionCount=0;
    }
    private bool QueryGroupVisibility(uint id)
    {
        if(RenderSettings.DisableOcclusionQueries || RenderSettings.IgnoreOcclusionQueries || !buildActors.TryGetValue(id,out var data) || data.OcclusionGroup==null)return false;
        var group=data.OcclusionGroup;if(group.Seen!=BuildFrameCounter){group.Seen=BuildFrameCounter;group.Hidden=false;if(!group.Bounds.Min.IsValid()||!group.Bounds.Max.IsValid()||group.Members.Count==0)return false;
            bool entered=group.State.LastInView!=BuildFrameCounter-1;group.Hidden=group.State.Observe(BuildFrameCounter,group.Bounds,FrameView.CameraPosition,groupNear);if(entered)groupReentries++;
            var inflated=group.Bounds.GetInflated(group.Bounds.Size*.5);var eye=FrameView.CameraPosition;bool close=inflated.DistanceSquared(ref eye)<groupNear;
            if(!entered && !close && packedOcclusionCount<8192){var min=inflated.Min-eye;var size=inflated.Size;packedOcclusion[packedOcclusionCount++]=new NativePackedOcclusionDraw{Id=group.QueryId,MinimumX=(float)min.X,MinimumY=(float)min.Y,MinimumZ=(float)min.Z,ExtentX=(float)size.X,ExtentY=(float)size.Y,ExtentZ=(float)size.Z};groupOwners[group.QueryId]=new GroupQueryOwner{Group=group,Epoch=group.State.Epoch,Frame=BuildFrameCounter,Bounds=inflated};groupQueries++;}
        }
        if(group.Hidden)OcclusionCulls++;return group.Hidden;
    }
    private void EndGroupOcclusion(){ulong token=++visibilityToken;groupQueryOwners[token]=groupOwners;backend.QueueOcclusion(token,packedOcclusion,packedOcclusionCount);}
    private void ResetGroupMode(){foreach(var group in occlusionGroups.Values)group.State.Reset();groupQueryOwners.Clear();visibilitySnapshots.Clear();occludedBounds.Clear();groupMembershipDirty=true;groupObservedToken=observedVisibilityToken=0;}
}
