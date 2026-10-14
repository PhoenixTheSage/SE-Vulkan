using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Rendering.Interop;
using VRage.Render.Scene;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    public bool GroupOcclusionEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_GROUP_OCCLUSION")=="1";
    private sealed class GroupVisibility
    {
        public MyActor Root;
        public ulong QueryId;
        public BoundingBoxD Bounds;
        public readonly HashSet<ActorData> Members=new();
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
    private void DirtyGroup(ActorData data){if(data.OcclusionGroup!=null)dirtyOcclusionGroups.Add(data.OcclusionGroup);}
    private void RefreshGroupBounds()
    {
        if(groupMembershipDirty){
            foreach(var group in occlusionGroups.Values){group.Members.Clear();group.State.Reset();foreach(var state in group.ShadowStates)state.Reset();}
            foreach(var data in actors.Values){DirtyNativePass(data,appearance:false);data.OcclusionGroup=null;if(data.Model==null)continue;for(IMyActor node=data.Actor.Parent;node!=null;node=node.Parent)if(node is MyActor root && root.IsRoot()){
                if(!occlusionGroups.TryGetValue(root,out var group)){group=new GroupVisibility{Root=root,QueryId=++nextGroupQueryId};occlusionGroups.Add(root,group);}group.Members.Add(data);data.OcclusionGroup=group;break;}}
            foreach(var group in occlusionGroups.Values)dirtyOcclusionGroups.Add(group);groupMembershipDirty=false;
        }
        foreach(var group in dirtyOcclusionGroups){var box=BoundingBoxD.CreateInvalid();foreach(var member in group.Members)if(!member.Actor.IsDestroyed && IsVisible(member.Actor))box.Include(member.Bounds);group.Bounds=box;}
        dirtyOcclusionGroups.Clear();
    }
    private void BeginGroupOcclusion()
    {
        RefreshGroupBounds();
        bool disabled=RenderSettings.DisableOcclusionQueries||RenderSettings.IgnoreOcclusionQueries;
        if(visibilityWidth!=backend.PresentationWidth||visibilityHeight!=backend.PresentationHeight||visibilityFraction!=backend.RenderFraction||groupFov!=lodFov||disabled!=groupQueriesDisabled){foreach(var group in occlusionGroups.Values)group.State.Reset();groupQueryOwners.Clear();visibilityWidth=backend.PresentationWidth;visibilityHeight=backend.PresentationHeight;visibilityFraction=backend.RenderFraction;groupFov=lodFov;groupQueriesDisabled=disabled;}
        var results=backend.GetOcclusionBorrowed(out int count,out ulong token);
        if(token!=groupObservedToken && groupQueryOwners.TryGetValue(token,out var owners)){
            groupObservedToken=token;for(int i=0;i<count;i++){var result=results[i];if(!owners.TryGetValue(result.Id,out var owner))continue;var group=owner.Group;
                if(group.Root.IsDestroyed || !occlusionGroups.TryGetValue(group.Root,out var active) || !ReferenceEquals(active,group) || !group.State.Accept(owner.Epoch,owner.Frame,FrameCounter,owner.Bounds,group.Bounds,result.Visible!=0))groupRejected++;else groupAccepted++;}}
        ulong next=visibilityToken+1;int slot=(int)(next%8);groupOwners=groupOwnerRing[slot]??(groupOwnerRing[slot]=new Dictionary<ulong,GroupQueryOwner>());if(next>8)groupQueryOwners.Remove(next-8);groupOwners.Clear();packedOcclusionCount=0;
    }
    private bool QueryGroupVisibility(uint id)
    {
        if(RenderSettings.DisableOcclusionQueries || RenderSettings.IgnoreOcclusionQueries || !actors.TryGetValue(id,out var data) || data.OcclusionGroup==null)return false;
        var group=data.OcclusionGroup;if(group.Seen!=FrameCounter){group.Seen=FrameCounter;group.Hidden=false;if(!group.Bounds.Min.IsValid()||!group.Bounds.Max.IsValid()||group.Members.Count==0)return false;
            bool entered=group.State.LastInView!=FrameCounter-1;group.Hidden=group.State.Observe(FrameCounter,group.Bounds,Environment.CameraPosition,groupNear);if(entered)groupReentries++;
            var inflated=group.Bounds.GetInflated(group.Bounds.Size*.5);var eye=Environment.CameraPosition;bool close=inflated.DistanceSquared(ref eye)<groupNear;
            if(!entered && !close && packedOcclusionCount<8192){var min=inflated.Min-eye;var size=inflated.Size;packedOcclusion[packedOcclusionCount++]=new NativePackedOcclusionDraw{Id=group.QueryId,MinimumX=(float)min.X,MinimumY=(float)min.Y,MinimumZ=(float)min.Z,ExtentX=(float)size.X,ExtentY=(float)size.Y,ExtentZ=(float)size.Z};groupOwners[group.QueryId]=new GroupQueryOwner{Group=group,Epoch=group.State.Epoch,Frame=FrameCounter,Bounds=inflated};groupQueries++;}
        }
        if(group.Hidden)OcclusionCulls++;return group.Hidden;
    }
    private void EndGroupOcclusion(){ulong token=++visibilityToken;groupQueryOwners[token]=groupOwners;backend.QueueOcclusion(token,packedOcclusion,packedOcclusionCount);}
    private void ResetGroupMode(){foreach(var group in occlusionGroups.Values)group.State.Reset();groupQueryOwners.Clear();visibilitySnapshots.Clear();occludedBounds.Clear();groupMembershipDirty=true;groupObservedToken=observedVisibilityToken=0;}
}
