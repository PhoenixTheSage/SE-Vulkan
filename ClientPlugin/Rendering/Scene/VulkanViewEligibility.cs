using System;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    public bool ViewEligibilityEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_VIEW_ELIGIBILITY")=="1";
    ViewEligibilityStats viewEligibilityStats;
    public ViewEligibilityStats GetViewEligibilityStats()=>viewEligibilityStats;

    // Mirrors Native/src/spot_bounds.h using the same camera-relative float inputs.
    public static bool SpotSphereVisible(Vector3 delta,Vector3 direction,float range,float cosine,float radius)
    {
        if(!(radius>=0))return true;
        float distance2=delta.X*delta.X+delta.Y*delta.Y+delta.Z*delta.Z;
        float reach=range+radius;if(distance2>reach*reach)return false;
        // Stock spotlight shadow near plane (MyLightComponent.UpdateViewProjection).
        float along=delta.X*direction.X+delta.Y*direction.Y+delta.Z*direction.Z;
        if(along < .5f-radius)return false;
        if(cosine>.05f && cosine<.999f){
            float side2=Math.Max(0,distance2-along*along);
            float sine=(float)Math.Sqrt(Math.Max(0,1-cosine*cosine));
            float limit=(along*sine+radius)/cosine;
            if(side2>limit*limit)return false;
        }
        return true;
    }
    bool RetainForAnyView(ActorData actor,BoundingBoxD bounds,bool probeOnly,bool occluded,bool outsideProbe,out uint? selectedCascades)
    {
        selectedCascades=null;viewEligibilityStats.TestedActors++;
        // Keep unknown aggregate/skinned ownership and the independent highlight consumer.
        if(actor.StaticInstances!=null || actor.SkinMatrices!=null || !actor.SubmissionOpaque || HasHighlight(actor.Actor.ID)){
            viewEligibilityStats.ConservativeActors++;return true;
        }
        if(!probeOnly && !occluded){viewEligibilityStats.MainActors++;return true;}
        if(!outsideProbe){viewEligibilityStats.ProbeActors++;return true;}
        bool casts=(actor.Flags&(RenderFlags.CastShadows|RenderFlags.SkipInDepth))==RenderFlags.CastShadows;
        uint mask=casts?(CascadeMaskFor?.Invoke(bounds,FrameView.CameraPosition)??0xFFu):0u;
        if(casts)mask=FilterFarActorCascades(mask,actor);
        selectedCascades=mask;
        if(mask!=0){viewEligibilityStats.CascadeActors++;return true;}
        if(casts){
            Vector3 origin=(Vector3)(bounds.Center-FrameView.CameraPosition);float radius=(float)bounds.HalfExtents.Length();
            foreach(var spot in buildLights.Spots){
                Vector3 delta=origin-(Vector3)(spot.Position-FrameView.CameraPosition);
                if(SpotSphereVisible(delta,spot.Direction,spot.Range,spot.Cone,radius)){viewEligibilityStats.SpotActors++;return true;}
            }
        }
        viewEligibilityStats.SkippedActors++;return false;
    }
}
public struct ViewEligibilityStats {public uint Enabled,TestedActors,SkippedActors,MainActors,ProbeActors,CascadeActors,SpotActors,ConservativeActors;}
