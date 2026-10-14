using System;
using System.Collections.Generic;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering;
using VRageMath;
using VRageRender;
using VRageRender.Lights;
using VRageRender.Messages;
namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    private ulong nextFlareId;private readonly Dictionary<ulong,float> flareOcclusion=new Dictionary<ulong,float>();
    public long FlareQueries {get;private set;}public long FlareBillboards {get;private set;}
    public void DrawFlares(MatrixD view,MatrixD projection,int width,int height,float multiplier)
    {
        var current=new HashSet<ulong>();foreach(var l in lights)current.Add(l.FlareId);
        foreach(var r in backend.GetFlareResults())if(current.Contains(r.Id))flareOcclusion[r.Id]=r.Occlusion;
        foreach(var key in new List<ulong>(flareOcclusion.Keys))if(!current.Contains(key))flareOcclusion.Remove(key);
        var queries=new List<NativeFlareQuery>();MatrixD vp=view*projection,inv=MatrixD.Invert(vp),cameraRotation=MatrixD.Invert(view);
        Vector3D camera=Environment.CameraPosition;
        foreach(var l in lights)
        {
            var data=l.Data;var desc=data.Glare;if(!desc.Enabled||desc.Glares==null||desc.Glares.Length==0||!IsVisible(l.Owner))continue;
            if(desc.Type==MyGlareTypeEnum.Directional && !GuestBridge.NativeSunGlare)continue;
            Vector3D position=l.Position,relative=position-camera;double distance=relative.LengthSquared(),max=(double)desc.MaxDistance*desc.MaxDistance;
            if(max<=0||distance>max||distance<1e-12)continue;
            float intensity=desc.Intensity*multiplier;if(desc.Type==MyGlareTypeEnum.Directional){float dot=(float)Vector3D.Dot(-Vector3D.Normalize(relative),l.Owner.WorldMatrix.Forward)-.3f;intensity*=MathHelper.Saturate((float)Math.Pow(Math.Max(dot,0)*2,4));}
            float fade=distance>max*.64?(float)(1-(distance-max*.64)/(max*.36)):1;intensity*=fade*fade*fade*fade;if(intensity<1e-5f)continue;
            Vector4 clip=Vector4.Transform(new Vector4((Vector3)relative,1),vp);if(clip.W<=0)continue;Vector3 ndc=new Vector3(clip.X,clip.Y,clip.Z)/clip.W;
            if(Math.Abs(ndc.X)>1||Math.Abs(ndc.Y)>1||ndc.Z<0||ndc.Z>1)continue;
            Vector3 shifted=(Vector3)(relative-Vector3D.Normalize(relative)*desc.QueryShift);float radius=Math.Abs(desc.QuerySize*(float)projection.M22*height/(2*clip.W));
            if(queries.Count>=4096)break;queries.Add(new NativeFlareQuery{Id=l.FlareId,X=shifted.X,Y=shifted.Y,Z=shifted.Z,Radius=radius*backend.RenderFraction});
            // Until its first completed query, a flare stays occluded.
            if(!flareOcclusion.TryGetValue(l.FlareId,out float occlusion))continue;
            float baseIntensity=data.SpotLightOn?data.SpotIntensity:data.PointIntensity;Vector3 baseColor=data.SpotLightOn?data.SpotLight.Light.Color:data.PointLight.Color;
            Vector4 colorFactor=new Vector4(baseIntensity>1e-8f?baseColor/baseIntensity:Vector3.One,1);colorFactor=Vector4.Lerp(colorFactor,colorFactor.ToGray(),.5f);
            var screen=new Vector2(ndc.X,ndc.Y);
            foreach(var glare in desc.Glares){float amount=FlareCurve(glare.OcclusionToIntensityCurve,occlusion);if(amount<=1e-5f)continue;
                bool anamorphic=glare.Type==SubGlareType.Anamorphic||glare.Type==SubGlareType.AnamorphicInverted;
                float screenIntensity=MathHelper.Lerp(glare.ScreenIntensityMultiplierCenter,glare.ScreenIntensityMultiplierEdge,anamorphic?Math.Abs(screen.X):MathHelper.Saturate(screen.Length()));
                Vector4 color=glare.Color*colorFactor*(intensity*screenIntensity*amount);if(color.W<=1e-5f)continue;color.X/=4;color.Y/=4;color.Z/=4;
                Vector3D origin=position;if(glare.ScreenCenterDistance!=Vector2.Zero){Vector2 ghost=screen*(Vector2.One-glare.ScreenCenterDistance);origin=camera+Vector3D.Transform(new Vector3D(ghost.X,ghost.Y,ndc.Z),inv);}
                Vector2 size=glare.Size*(glare.FixedSize?Vector2.One:desc.SizeMultiplier*fade)*10;
                Vector3 left=(Vector3)cameraRotation.Left,up=(Vector3)cameraRotation.Up;
                if(glare.Type==SubGlareType.Rotated){Vector3 forward=Vector3.Normalize(camera-origin);left=Vector3.Cross((Vector3)cameraRotation.Up,forward);if(left.LengthSquared()<1e-8f)continue;left.Normalize();up=Vector3.Cross(forward,left);}
                if(glare.Type==SubGlareType.Anamorphic)size.X=Math.Max(size.X*(1-Math.Abs(screen.X)),.0001f);if(glare.Type==SubGlareType.AnamorphicInverted)size.X=Math.Max(size.X*Math.Abs(screen.X),.0001f);
                billboards.AddBillboardOriented(glare.Material,color,origin,left,up,size.X,size.Y,MyBillboard.BlendTypeEnum.AdditiveTop);FlareBillboards++;
            }
        }
        backend.QueueFlares(vp,queries.ToArray());FlareQueries+=queries.Count;
    }
    public static float FlareCurve(MySubGlare.KeyPoint[] curve,float occlusion)
    {
        if(curve==null||curve.Length==0)return (1-occlusion*occlusion-.2f)*1.25f;
        if(occlusion<=curve[0].Occlusion)return curve[0].Intensity;
        for(int i=1;i<curve.Length;++i)if(occlusion<curve[i].Occlusion){float span=curve[i].Occlusion-curve[i-1].Occlusion;return span<=0?curve[i].Intensity:MathHelper.Lerp(curve[i-1].Intensity,curve[i].Intensity,(occlusion-curve[i-1].Occlusion)/span);}
        return curve[curve.Length-1].Intensity;
    }
}
