using System;
using VRage.Render.Scene;
using VRageMath;
using VRageRender;
namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    readonly CullResults ambientLightQuery=new CullResults();
    // MyEnvironmentProbe.GatherLightAmbient, with guards for degenerate distances.
    public float GatherProbeAmbient(float radius,MyEnvironmentProbeData settings)
    {
        if(radius<=0)return 0;float sum=0;Vector3D camera=Environment.CameraPosition;
        // MyLightsRendering.GatherLights uses root bounds and whole intersecting
        // groups, independently of the view frustum and visible-light budget.
        ambientLightQuery.Clear();var sphere=new BoundingSphereD(camera,radius);
        DynamicRenderablesDBVH.OverlapAllBoundingSphere<(Action<MyCullResultsBase,bool>,object)>(ref sphere,item=>item.Item1(ambientLightQuery,true));
        ManualCullTree.OverlapAllBoundingSphere<MyManualCullTreeData>(ref sphere,group=>{if(IsVisible(group.Actor))ambientLightQuery.Append((CullResults)group.All);});
        foreach(var light in ambientLightQuery.Points){if(light.Owner==null || !IsVisible(light.Owner))continue;var d=light.Data;
            if(d.PointLightOn && d.PointLight.Range>.5f){double distance=Vector3D.Distance(light.Owner.WorldMatrix.Translation,camera);if(distance<radius+d.PointLight.Range){float edge=distance>radius?1-(float)(distance-radius)/d.PointLight.Range:1;sum+=edge/(1+.25f*d.PointLight.Falloff)*(d.PointLight.Range*d.PointLight.Range/(radius*radius))*((d.PointLight.Color.X+d.PointLight.Color.Y+d.PointLight.Color.Z)/3);}}
        }
        foreach(var light in ambientLightQuery.Spots){if(light.Owner==null || !IsVisible(light.Owner))continue;var d=light.Data;
            if(d.SpotLightOn){double range=d.SpotLight.Light.Range/2+radius*2;Vector3D offset=light.Owner.WorldMatrix.Translation-light.Owner.WorldMatrix.Forward*radius-camera;double distance=offset.Length();if(distance<range && distance>1e-8 && MathHelper.Saturate(-Vector3D.Dot(offset/distance,light.Owner.WorldMatrix.Forward))>d.SpotLight.ApertureCos)sum+=MathHelper.Saturate(1-(float)((distance-radius)/range))/(1+.25f*d.SpotLight.Light.Falloff)*((d.SpotLight.Light.Color.X+d.SpotLight.Light.Color.Y+d.SpotLight.Light.Color.Z)/6);}}
        sum*=settings.AmbientScale;if(sum<settings.AmbientMinClamp)return 0;if(sum<settings.AmbientMinClamp*2 && settings.AmbientMinClamp>0)return MathHelper.Lerp(0,sum,sum/settings.AmbientMinClamp-1);return Math.Min(sum,settings.AmbientMaxClamp);
    }
}
