using System;
using System.Collections.Generic;
using VRage.Render.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.Render11.Common;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    readonly CullResults lightQuery=new CullResults();
    readonly List<MyManualCullTreeData> lightQueryGroups=new List<MyManualCullTreeData>();
    readonly List<bool> lightGroupContained=new List<bool>();
    readonly List<StockLightSelection.Group> lightPointGroups=new List<StockLightSelection.Group>();
    BoundingFrustumD lightFarFrustum;
    readonly struct LightFrustum
    {
        readonly Plane[] planes;
        public LightFrustum(BoundingFrustum frustum){planes=frustum.Planes;}
        public bool Contains(MyCullAABB box){
            foreach(var plane in planes){var n=plane.Normal;int index=(n.X>=0?4:0)|(n.Y>=0?2:0)|(n.Z>=0?1:0);var p=box.Data[index];
                if((double)(n.X*p.X+n.Y*p.Y+n.Z*p.Z+plane.D)>0)return false;}
            return true;
        }
    }
    struct LightTreeOp:IAddOp<Action<MyCullResultsBase,bool>>
    {
        public int Count;public CullResults Results;
        public void Add(Action<MyCullResultsBase,bool> add,bool contained){Count++;add(Results,contained);}
    }
    static void RemoveLightAtFast<T>(List<T> list,int at){list[at]=list[list.Count-1];list.RemoveAt(list.Count-1);}
    void CullLightGroup(MyManualCullTreeData data,BoundingFrustum local)
    {
        int count=data.BruteCull.Count;bool brute=count<=RenderSettings.CullGroupsThreshold;
        ref var state=ref data.RenderCullData[0];
        var clip=new LightFrustum(local);
        if(RenderSettings.UseIncrementalCulling){
            if(brute||state.ActiveActorsLastFrame>=count*RenderSettings.IncrementalCullingTreeFallbackThreshold){
                var active=state.ActiveActors;var culled=state.CulledActors;var results=(CullResults)state.ActiveResults;int oldCount=active.Count;
                for(int i=0;i<culled.Count;i++)if(clip.Contains(culled[i].Aabb)){var item=culled[i];item.UserData.Add(results,true);RemoveLightAtFast(culled,i);active.Add(item);i--;}
                int check=oldCount>=RenderSettings.IncrementalCullFrames?oldCount/RenderSettings.IncrementalCullFrames:oldCount;
                int offset=state.IterationOffset;if(offset>=oldCount)offset=0;int end=Math.Min(oldCount,offset+check);
                for(int i=offset;i<end;i++)if(!clip.Contains(active[i].Aabb)){var item=active[i];item.UserData.Remove(results);culled.Add(item);RemoveLightAtFast(active,i);end--;i--;}
                state.IterationOffset=end;state.ActiveActorsLastFrame=active.Count;lightQuery.Append(results);
            }else{
                var op=new LightTreeOp{Results=lightQuery};data.Children.OverlapAllFrustum<Action<MyCullResultsBase,bool>,LightTreeOp>(ref local,ref op);state.ActiveActorsLastFrame=op.Count;
            }
        }else if(brute){
            foreach(var item in data.BruteCull.Values)if(clip.Contains(item.Aabb))item.UserData.Add(lightQuery,true);
        }else{
            var op=new LightTreeOp{Results=lightQuery};data.Children.OverlapAllFrustum<Action<MyCullResultsBase,bool>,LightTreeOp>(ref local,ref op);
        }
    }
    (List<VulkanLightComponent> Points,List<VulkanLightComponent> Spots) SelectLights()
    {
        lightQuery.Clear();lightPointGroups.Clear();lightQueryGroups.Clear();lightGroupContained.Clear();
        if(frustum==null){
            // Offscreen callers that supply no view retain an unbounded query.
            foreach(var light in lights)if(light.Owner!=null&&IsVisible(light.Owner)){if(light.PointOn)lightQuery.Points.Add(light);if(light.SpotOn)lightQuery.Spots.Add(light);}
        }else{
            if(RenderSettings.DrawNonMergeInstanced){
                var view=frustum;DynamicRenderablesDBVH.OverlapAllFrustum(ref view,(MyCullResultsBase)lightQuery);
                var far=lightFarFrustum??view;DynamicRenderablesFarDBVH.OverlapAllFrustum(ref far,(MyCullResultsBase)lightQuery);
                if(RenderSettings.DrawGroups){
                    ManualCullTree.OverlapAllFrustum(ref view,lightQueryGroups,lightGroupContained);
                    for(int i=0;i<lightQueryGroups.Count;i++){
                        var group=lightQueryGroups[i];int start=lightQuery.Points.Count;
                        // Shared actors retain the manual-group proxy when hidden.
                        // Preserve the scene's ancestor visibility contract before
                        // consuming either cached or freshly culled child results.
                        if(IsVisible(group.Actor)&&!group.Actor.IsOccluded(0)){
                            if(lightGroupContained[i])lightQuery.Append((CullResults)group.All);
                            else CullLightGroup(group,new BoundingFrustum((Matrix)(group.Actor.WorldMatrix*view.Matrix)));
                        }
                        int count=lightQuery.Points.Count-start;
                        if(count>0)lightPointGroups.Add(new StockLightSelection.Group(start,count,group.Actor.CalculateCameraDistanceSquared()));
                    }
                }
            }
            if(RenderSettings.DrawMergeInstanced){var view=frustum;MergeGroupsDBVH.OverlapAllFrustum(ref view,(MyCullResultsBase)lightQuery);}
        }
        return (StockLightSelection.Points(lightQuery.Points,lightPointGroups,(int)WorldConstants.PointLightCapacity(RenderSettings.User.LightsQuality),light=>light.Owner.CalculateCameraDistanceSquaredFast()),
            StockLightSelection.Spots(lightQuery.Spots,light=>light.Owner.CalculateCameraDistanceSquaredFast()));
    }
}
