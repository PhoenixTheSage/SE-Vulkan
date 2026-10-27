using System;
using System.Collections.Generic;
using System.Threading;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering;
using VRageMath;
using VRageRender;
using VRageRender.Lights;
using VRageRender.Messages;
using VRage.Library.Utils;
namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    private ulong nextFlareId;private readonly Dictionary<ulong,float> flareOcclusion=new Dictionary<ulong,float>();
    private static long flareQuerySequence;
    private readonly Dictionary<ulong,StockFlareOcclusionState> flareStates=new Dictionary<ulong,StockFlareOcclusionState>();
    private readonly Dictionary<ulong,ulong> pendingFlareOwners=new Dictionary<ulong,ulong>();
    private readonly MyRandom flareRandom=new MyRandom();
    public long FlareQueries {get;private set;}public long FlareBillboards {get;private set;}
    // Per-frame scratch, reused so flare scheduling allocates nothing in steady state.
    private readonly Dictionary<ulong,VulkanLightComponent> currentFlares=new Dictionary<ulong,VulkanLightComponent>();
    private readonly List<ulong> staleFlareKeys=new List<ulong>();private readonly List<NativeFlareRasterQuery> flareQueries=new List<NativeFlareRasterQuery>();
    private readonly NativeFlareResult[] flareResults=new NativeFlareResult[4096];private NativeFlareRasterQuery[] flareQueryArray=new NativeFlareRasterQuery[64];
    public void DrawFlares(MatrixD view,MatrixD projection,int width,int height,float multiplier)
    {
        float now=(float)FrameView.FrameTime.Milliseconds;
        var current=currentFlares;current.Clear();foreach(var l in lights)current[l.FlareId]=l;
        int resultCount=backend.ReadFlareResults(flareResults);
        for(int i=0;i<resultCount;i++){var r=flareResults[i];if(pendingFlareOwners.TryGetValue(r.Id,out ulong owner) && current.TryGetValue(owner,out var light) && flareStates.TryGetValue(owner,out var state)){
            var glare=light.EffectiveData.Glare;if(state.Complete(r.Id,r.Occlusion,now,glare.QueryFreqMinMs,glare.QueryFreqRndMs,flareRandom.NextFloat()))pendingFlareOwners.Remove(r.Id);
        }}
        staleFlareKeys.Clear();foreach(var key in flareOcclusion.Keys)if(!current.ContainsKey(key))staleFlareKeys.Add(key);foreach(var key in staleFlareKeys)flareOcclusion.Remove(key);
        staleFlareKeys.Clear();foreach(var key in flareStates.Keys)if(!current.ContainsKey(key))staleFlareKeys.Add(key);foreach(var key in staleFlareKeys)flareStates.Remove(key);
        staleFlareKeys.Clear();foreach(var entry in pendingFlareOwners)if(!current.ContainsKey(entry.Value))staleFlareKeys.Add(entry.Key);foreach(var key in staleFlareKeys)pendingFlareOwners.Remove(key);
        foreach(var entry in flareStates)flareOcclusion[entry.Key]=entry.Value.Sample(now);
        var queries=flareQueries;queries.Clear();MatrixD vp=view*projection,inv=MatrixD.Invert(vp),cameraRotation=MatrixD.Invert(view);
        Vector3D camera=FrameView.CameraPosition;
        foreach(var l in lights)
        {
            var data=l.EffectiveData;var desc=data.Glare;if(!desc.Enabled||desc.Glares==null||desc.Glares.Length==0||!IsVisible(l.Owner))continue;
            if(!GuestBridge.NativeSunGlare && GuestBridge.SunLightRenderId!=uint.MaxValue && l.Owner.ID==GuestBridge.SunLightRenderId)continue;
            Vector3D position=l.Position,relative=position-camera;double distance=relative.LengthSquared(),max=(double)desc.MaxDistance*desc.MaxDistance;
            if(max<=0||distance>max||distance<1e-12)continue;
            float intensity=desc.Intensity*multiplier;if(desc.Type==MyGlareTypeEnum.Directional){float dot=(float)Vector3D.Dot(-Vector3D.Normalize(relative),l.Owner.WorldMatrix.Forward)-.3f;intensity*=MathHelper.Saturate((float)Math.Pow(Math.Max(dot,0)*2,4));}
            float fade=distance>max*.64?(float)(1-(distance-max*.64)/(max*.36)):1;intensity*=fade*fade*fade*fade;if(intensity<1e-5f)continue;
            Vector4 clip=Vector4.Transform(new Vector4((Vector3)relative,1),vp);if(clip.W<=0)continue;Vector3 ndc=new Vector3(clip.X,clip.Y,clip.Z)/clip.W;
            Vector3 shifted=(Vector3)(relative-Vector3D.Normalize(relative)*desc.QueryShift);
            Vector4 queryClip=Vector4.Transform(new Vector4(shifted,1),vp);
            if(!flareStates.TryGetValue(l.FlareId,out var queryState)){queryState=new StockFlareOcclusionState();flareStates.Add(l.FlareId,queryState);flareOcclusion[l.FlareId]=queryState.Sample(now);}
            if(queryState.ShouldQuery(now) && queries.Count<4096 && queryClip.W>0 && desc.QuerySize>0){float area=(float)Math.Abs(desc.QuerySize*desc.QuerySize*projection.M11*projection.M22/distance);
                ulong request=unchecked((ulong)Interlocked.Increment(ref flareQuerySequence));queryState.Begin(request);pendingFlareOwners.Add(request,l.FlareId);
                queries.Add(new NativeFlareRasterQuery{Id=request,X=queryClip.X/queryClip.W,Y=queryClip.Y/queryClip.W,Z=queryClip.Z/queryClip.W,
                    HalfWidth=(float)Math.Abs(desc.QuerySize*projection.M11/(2*queryClip.W)),HalfHeight=(float)Math.Abs(desc.QuerySize*projection.M22/(2*queryClip.W)),Area=area});}
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
                if(glare.Type==SubGlareType.Rotated){billboards.AddBillboardRotated(glare.Material,color,origin,camera,size.X,size.Y,MyBillboard.BlendTypeEnum.AdditiveTop);FlareBillboards++;continue;}
                if(glare.Type==SubGlareType.Anamorphic)size.X=Math.Max(size.X*(1-Math.Abs(screen.X)),.0001f);if(glare.Type==SubGlareType.AnamorphicInverted)size.X=Math.Max(size.X*Math.Abs(screen.X),.0001f);
                billboards.AddBillboardOriented(glare.Material,color,origin,left,up,size.X,size.Y,MyBillboard.BlendTypeEnum.AdditiveTop);FlareBillboards++;
            }
        }
        if(flareQueryArray.Length<queries.Count)flareQueryArray=new NativeFlareRasterQuery[Math.Max(queries.Count,flareQueryArray.Length*2)];queries.CopyTo(flareQueryArray);
        backend.QueueFlareRaster(flareQueryArray,queries.Count);FlareQueries+=queries.Count;
    }
    public static float FlareCurve(MySubGlare.KeyPoint[] curve,float occlusion)
    {
        if(curve==null||curve.Length==0)return (1-occlusion*occlusion-.2f)*1.25f;
        if(occlusion<=curve[0].Occlusion)return curve[0].Intensity;
        for(int i=1;i<curve.Length;++i)if(occlusion<curve[i].Occlusion){float span=curve[i].Occlusion-curve[i-1].Occlusion;return span<=0?curve[i].Intensity:MathHelper.Lerp(curve[i-1].Intensity,curve[i].Intensity,(occlusion-curve[i-1].Occlusion)/span);}
        return curve[curve.Length-1].Intensity;
    }
}
