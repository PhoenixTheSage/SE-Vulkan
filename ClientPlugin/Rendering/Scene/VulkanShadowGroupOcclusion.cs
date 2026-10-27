using System;
using System.Collections.Generic;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender;
namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    internal VulkanShadows ShadowSystem;
    public bool StockShadowOcclusionEnabled{get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_STOCK_SHADOW_OCCLUSION")=="1";
    private sealed class ShadowGroupView
    {
        public readonly NativePackedOcclusionDraw[] Rows=new NativePackedOcclusionDraw[8192];
        public readonly Dictionary<ulong,GroupQueryOwner>[] Ring=new Dictionary<ulong,GroupQueryOwner>[8];
        public readonly Dictionary<ulong,Dictionary<ulong,GroupQueryOwner>> Owners=new();
        public readonly Dictionary<GroupVisibility,GroupQueryOwner> Pending=new();
        public Dictionary<ulong,GroupQueryOwner> Current;public int Count;public ulong Token,Observed;
    }
    private readonly ShadowGroupView[] shadowGroupViews=new ShadowGroupView[8];
    private bool previousShadowOcclusion;private uint shadowOcclusionMask;private int shadowOcclusionResolution;
    private long shadowQueries,shadowHidden,shadowAccepted,shadowRejected;
    private void ClearShadowGroupOcclusion()
    {
        for(uint c=0;c<8;c++)if(shadowGroupViews[c]!=null){
            var view=shadowGroupViews[c];view.Owners.Clear();view.Pending.Clear();
            foreach(var ring in view.Ring)ring?.Clear();view.Current=null;view.Count=0;
            // Keep monotonically increasing tokens: completed old GPU slots cannot alias a new scene.
            if(previousShadowOcclusion)backend.QueueShadowOcclusion(c,++view.Token,view.Rows,0);
        }
        previousShadowOcclusion=false;shadowOcclusionMask=0;shadowOcclusionResolution=0;
    }
    public object GetShadowOcclusionStats()=>new{Enabled=StockShadowOcclusionEnabled,Queries=shadowQueries,HiddenMemberships=shadowHidden,Accepted=shadowAccepted,Rejected=shadowRejected,Updates=shadowOcclusionMask};
    private void BeginShadowGroupOcclusion()
    {
        bool enabled=StockShadowOcclusionEnabled && ShadowSystem!=null && ShadowSystem.StockSchedulingEnabled && ShadowSystem.StockFittingEnabled && !RenderSettings.DisableOcclusionQueries && !RenderSettings.IgnoreOcclusionQueries && !RenderSettings.DisableShadowCascadeOcclusionQueries;
        int resolution=RenderSettings.User.ShadowMemoryQuality.ShadowCascadeResolution();
        if(enabled!=previousShadowOcclusion || resolution!=shadowOcclusionResolution){
            foreach(var group in occlusionGroups.Values)foreach(var state in group.ShadowStates)state.Reset();
            for(uint c=0;c<8;c++)if(shadowGroupViews[c]!=null){var view=shadowGroupViews[c];view.Owners.Clear();view.Pending.Clear();backend.QueueShadowOcclusion(c,++view.Token,view.Rows,0);}
            previousShadowOcclusion=enabled;shadowOcclusionResolution=resolution;
        }
        shadowOcclusionMask=enabled?ShadowSystem.UpdateMask:0;
        if(!enabled)return;RefreshGroupBounds();
        for(int c=0;c<8;c++){
            var view=shadowGroupViews[c]??(shadowGroupViews[c]=new ShadowGroupView());view.Count=0;
            if((shadowOcclusionMask&(1u<<c))!=0){
                var rows=backend.GetShadowOcclusionBorrowed((uint)c,out int count,out ulong token);
                view.Token=Math.Max(view.Token,token);
                if(token!=view.Observed && view.Owners.TryGetValue(token,out var owners)){
                    view.Observed=token;
                    for(int i=0;i<count;i++)if(owners.TryGetValue(rows[i].Id,out var owner)){
                        var group=owner.Group;var state=group.ShadowStates[c];
                        if(view.Pending.TryGetValue(group,out var pending) && pending.Frame==owner.Frame)view.Pending.Remove(group);
                        if(group.Root.IsDestroyed || !occlusionGroups.TryGetValue(group.Root,out var active) || !ReferenceEquals(active,group) || !state.Accept(owner.Epoch,owner.Frame,BuildFrameCounter,owner.Bounds,group.Bounds,rows[i].Visible!=0))shadowRejected++;else shadowAccepted++;
                    }
                }
            }
            ulong next=view.Token+1;var current=view.Ring[next%8]??(view.Ring[next%8]=new Dictionary<ulong,GroupQueryOwner>());if(next>8)view.Owners.Remove(next-8);current.Clear();view.Current=current;
        }
    }
    private uint FilterShadowGroupCascades(ActorData actor,uint mask)
    {
        if(shadowOcclusionMask==0 || actor.OcclusionGroup==null)return mask;
        var group=actor.OcclusionGroup;
        for(int c=0;c<8;c++)if((mask&shadowOcclusionMask&(1u<<c))!=0){
            if(group.ShadowSeen[c]!=BuildFrameCounter){
                group.ShadowSeen[c]=BuildFrameCounter;group.ShadowHiddenMask&=~(1u<<c);
                if(group.Members.Count==0 || !group.Bounds.Min.IsValid() || !group.Bounds.Max.IsValid())continue;
                var state=group.ShadowStates[c];var view=shadowGroupViews[c];var origin=ShadowSystem.ViewOrigins[c];
                bool entered=state.LastInView!=BuildFrameCounter-1;bool hidden=state.Observe(BuildFrameCounter,group.Bounds,origin,groupNear);
                if(hidden)group.ShadowHiddenMask|=1u<<c;
                var inflated=group.Bounds.GetInflated(group.Bounds.Size*.5);bool close=inflated.DistanceSquared(ref origin)<groupNear;
                bool pending=view.Pending.TryGetValue(group,out var previous) && previous.Epoch==state.Epoch && BuildFrameCounter-previous.Frame<=100;
                if(!entered && !close && !pending && view.Count<view.Rows.Length){
                    var min=inflated.Min-FrameView.CameraPosition;var size=inflated.Size;
                    view.Rows[view.Count++]=new NativePackedOcclusionDraw{Id=group.QueryId,MinimumX=(float)min.X,MinimumY=(float)min.Y,MinimumZ=(float)min.Z,ExtentX=(float)size.X,ExtentY=(float)size.Y,ExtentZ=(float)size.Z};
                    var owner=new GroupQueryOwner{Group=group,Epoch=state.Epoch,Frame=BuildFrameCounter,Bounds=inflated};view.Current[group.QueryId]=owner;view.Pending[group]=owner;shadowQueries++;
                }
            }
            if((group.ShadowHiddenMask&(1u<<c))!=0){mask&=~(1u<<c);shadowHidden++;}
        }
        return mask;
    }
    private void EndShadowGroupOcclusion(){if(!previousShadowOcclusion)return;for(uint c=0;c<8;c++){var view=shadowGroupViews[c];ulong token=++view.Token;view.Owners[token]=view.Current;backend.QueueShadowOcclusion(c,token,view.Rows,view.Count);}}
}
