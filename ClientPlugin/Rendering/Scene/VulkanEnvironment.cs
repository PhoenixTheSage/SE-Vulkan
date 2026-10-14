using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;
/// <summary>Copied planet messages and stock main-view atmosphere/cloud constants.</summary>
public sealed class VulkanEnvironment : IDisposable
{
    private sealed class Atmosphere {public uint Id;public Vector3D Center;public float PlanetRadius,AtmosphereRadius;public MyAtmosphereSettings Settings;public double Created,Removed=-1;public bool FadeIn;}
    private sealed class Cloud {public MyCloudLayerSettingsRender Settings;public double Created,Removed=-1,Rotation;public long GameplayFrame;}
    private readonly NativeBackend backend;
    private readonly VulkanScene scene;
    private readonly Dictionary<uint,Atmosphere> atmospheres=new Dictionary<uint,Atmosphere>();
    private readonly Dictionary<uint,Cloud> clouds=new Dictionary<uint,Cloud>();
    public MyRenderFogSettings Fog;
    public MyRenderPlanetSettings Planet=new MyRenderPlanetSettings{AtmosphereIntensityMultiplier=35,AtmosphereIntensityAmbientMultiplier=35,AtmosphereDesaturationFactorForward=.5f,CloudsIntensityMultiplier=40};
    public double Time;
    public long GameplayFrame;
    public float TransitionSeconds;
    private bool enabled=true;
    public int AtmosphereCount=>atmospheres.Count;
    public int CloudCount=>clouds.Count;
    public long FogUpdates {get;private set;}
    public VulkanEnvironment(NativeBackend backend,VulkanScene scene){this.backend=backend;this.scene=scene;}
    public bool Process(MyRenderMessageBase message)
    {
        switch(message.MessageType)
        {
            case MyRenderMessageEnum.CreateRenderEntityAtmosphere:
                var a=(MyRenderMessageCreateRenderEntityAtmosphere)message;
                if(!a.WorldMatrix.Translation.IsValid() || !(a.AtmosphereRadius>a.PlanetRadius && a.PlanetRadius>0))throw new ArgumentException("Invalid planet atmosphere geometry.");
                if(atmospheres.ContainsKey(a.ID))throw new InvalidOperationException("Duplicate atmosphere ID.");
                atmospheres.Add(a.ID,new Atmosphere{Id=a.ID,Center=a.WorldMatrix.Translation,PlanetRadius=a.PlanetRadius,AtmosphereRadius=a.AtmosphereRadius,Settings=MyAtmosphereSettings.Defaults(),Created=Time,FadeIn=a.FadeIn});return true;
            case MyRenderMessageEnum.UpdateAtmosphereSettings:
                var update=(MyRenderMessageUpdateAtmosphereSettings)message;if(atmospheres.TryGetValue(update.ID,out var existing))existing.Settings=Normalize(update.Settings);return true;
            case MyRenderMessageEnum.EnableAtmosphere:enabled=((MyRenderMessageEnableAtmosphere)message).Enabled;return true;
            case MyRenderMessageEnum.CreateRenderEntityClouds:
                var c=((MyRenderMessageCreateRenderEntityClouds)message).Settings;
                c.Textures=c.Textures==null?null:new List<string>(c.Textures);
                if(!c.CenterPoint.IsValid() || !c.RotationAxis.IsValid() || c.RotationAxis.LengthSquared()<1e-12 || !(c.Altitude>0 && c.MinScaledAltitude>0))throw new ArgumentException("Invalid cloud shell geometry.");
                if(clouds.ContainsKey(c.ID))throw new InvalidOperationException("Duplicate cloud ID.");
                clouds.Add(c.ID,new Cloud{Settings=c,Created=Time,Rotation=c.InitialRotation,GameplayFrame=GameplayFrame});return true;
            case MyRenderMessageEnum.UpdateNewLoddingSettings:TransitionSeconds=((MyRenderMessageUpdateNewLoddingSettings)message).Settings.Global.MaxTransitionInSeconds;return false;
            case MyRenderMessageEnum.UpdateFogSettings:Fog=((MyRenderMessageUpdateFogSettings)message).Settings;FogUpdates++;return true;
            case MyRenderMessageEnum.UpdatePlanetSettings:Planet=((MyRenderMessageUpdatePlanetSettings)message).Settings;return false;
            case MyRenderMessageEnum.RemoveRenderObject:
                var remove=(MyRenderMessageRemoveRenderObject)message;
                if(atmospheres.TryGetValue(remove.ID,out var atmo)){if(remove.FadeOut)atmo.Removed=Time;else RemoveAtmosphere(remove.ID);return true;}
                if(clouds.TryGetValue(remove.ID,out var cloud)){if(remove.FadeOut)cloud.Removed=Time;else clouds.Remove(remove.ID);return true;}return false;
        }return false;
    }
    private static MyAtmosphereSettings Normalize(MyAtmosphereSettings s)
    {
        if(s.MieColorScattering==Vector3.Zero)s.MieColorScattering=new Vector3(s.MieScattering);
        if(s.Intensity==0)s.Intensity=1;if(s.AtmosphereTopModifier==0)s.AtmosphereTopModifier=1;if(s.SeaLevelModifier==0)s.SeaLevelModifier=1;
        if(s.RayleighHeightSpace==0)s.RayleighHeightSpace=s.RayleighHeight;if(s.RayleighTransitionModifier==0)s.RayleighTransitionModifier=1;
        if(!s.RayleighScattering.IsValid() || !s.MieColorScattering.IsValid() || Math.Min(Math.Min(s.RayleighScattering.X,Math.Min(s.RayleighScattering.Y,s.RayleighScattering.Z)),Math.Min(s.MieColorScattering.X,Math.Min(s.MieColorScattering.Y,s.MieColorScattering.Z)))<=0 || s.MieHeight<=0 || s.RayleighHeight<=0 || s.RayleighHeightSpace<=0)throw new ArgumentException("Invalid atmosphere scattering/height settings.");return s;
    }
    private float Fade(double created,double removed,bool fadeIn)=>Math.Min(1,fadeIn?(float)((Time-created)/Math.Max(.001,TransitionSeconds)):1)*Math.Max(0,removed>=0?1-(float)((Time-removed)/Math.Max(.001,TransitionSeconds)):1);
    public void Draw(Vector3D camera,MatrixD viewProjection,Vector3 sunDirection=default)
    {
        foreach(var id in atmospheres.Where(p=>p.Value.Removed>=0 && Time-p.Value.Removed>=TransitionSeconds).Select(p=>p.Key).ToArray())RemoveAtmosphere(id);
        foreach(var id in clouds.Where(p=>p.Value.Removed>=0 && Time-p.Value.Removed>=TransitionSeconds).Select(p=>p.Key).ToArray())clouds.Remove(id);
        var nearest=atmospheres.Values.Where(a=>enabled).OrderBy(a=>(a.Center-camera).LengthSquared()).FirstOrDefault();backend.SetProbeAtmosphere(nearest?.Id??0,nearest==null?0:nearest.Settings.Intensity*Planet.AtmosphereIntensityAmbientMultiplier*Fade(nearest.Created,nearest.Removed,nearest.FadeIn),Planet.AtmosphereDesaturationFactorForward,sunDirection);
        var draws=new List<NativeEnvironmentDraw>();
        foreach(var a in atmospheres.Values.Where(a=>enabled).OrderByDescending(a=>(a.Center-camera).LengthSquared()))
        {
            var s=a.Settings;float scale=a.PlanetRadius/6360000f;float radiusAtmo=6360000f*(a.AtmosphereRadius/a.PlanetRadius);
            Vector3D center=a.Center-camera;double threshold=radiusAtmo*s.AtmosphereTopModifier*scale*s.RayleighTransitionModifier;
            float height=MathHelper.Lerp(s.RayleighHeight,s.RayleighHeightSpace,(float)MathHelper.Clamp((center.Length()-threshold)/threshold,0,1));
            var constants=new ReflectedConstantBuffer(backend.EnvironmentLayouts[1],"AtmosphereConstants");
            constants.Set("PlanetCenter",(float)center.X,(float)center.Y,(float)center.Z);constants.Set("RadiusAtmosphere",radiusAtmo*s.AtmosphereTopModifier);constants.Set("RadiusGround",6360000f*1.01f*s.SeaLevelModifier);
            Vector3 beta=new Vector3(5.8e-6f,1.35e-5f,3.31e-5f)/s.RayleighScattering;constants.Set("BetaRayleighScattering",beta.X,beta.Y,beta.Z);beta=new Vector3(2e-5f)/s.MieColorScattering;constants.Set("BetaMieScattering",beta.X,beta.Y,beta.Z);
            constants.Set("MieG",s.MieG);constants.Set("HeightScaleRayleighMie",8000*height,1200*s.MieHeight);constants.Set("PlanetScaleFactor",scale);constants.Set("AtmosphereScaleFactor",(a.AtmosphereRadius-a.PlanetRadius)/(a.PlanetRadius*.5f));
            constants.Set("Intensity",s.Intensity*Planet.AtmosphereIntensityMultiplier*Fade(a.Created,a.Removed,a.FadeIn));constants.Set("FogIntensity",s.FogIntensity);constants.Set("DesaturationFactor",0);constants.Set("FogMultiplier",0);constants.Set("WorldViewProj",NativeBackend.MatrixValues(viewProjection));
            draws.Add(NativeEnvironmentDraw.Create(0,a.Id,constants.Bytes));
            DrawClouds(a.Id,camera,viewProjection,draws);
        }
        // Stock unassociated clouds use the invalid atmosphere-ID sentinel.
        DrawClouds(uint.MaxValue,camera,viewProjection,draws);backend.QueueEnvironment(draws);
    }
    private void DrawClouds(uint atmosphere,Vector3D camera,MatrixD viewProjection,List<NativeEnvironmentDraw> draws)
    {
        foreach(var cloud in clouds.Values.Where(c=>c.Settings.AtmosphereID==atmosphere).OrderByDescending(c=>Math.Pow((c.Settings.CenterPoint-camera).Length()-c.Settings.Altitude,2)))
        {
            var s=cloud.Settings;double distance=(s.CenterPoint-camera).Length(),relative=(distance-s.MinScaledAltitude)/(s.MaxPlanetHillRadius-s.MinScaledAltitude);float alpha=1;
            if(s.FadeOutRelativeAltitudeStart!=s.FadeOutRelativeAltitudeEnd)alpha=(float)MathHelper.Clamp(1-(s.FadeOutRelativeAltitudeStart>s.FadeOutRelativeAltitudeEnd?(s.FadeOutRelativeAltitudeStart-relative)/(s.FadeOutRelativeAltitudeStart-s.FadeOutRelativeAltitudeEnd):(relative-s.FadeOutRelativeAltitudeStart)/(s.FadeOutRelativeAltitudeEnd-s.FadeOutRelativeAltitudeStart)),0,1);
            Vector4 color=s.Color*Fade(cloud.Created,cloud.Removed,s.FadeIn)*new Vector4(Planet.CloudsIntensityMultiplier,Planet.CloudsIntensityMultiplier,Planet.CloudsIntensityMultiplier,alpha);if(color.W==0)continue;
            cloud.Rotation=(cloud.Rotation+s.AngularVelocity*(GameplayFrame-cloud.GameplayFrame)/10)%(Math.PI*2);cloud.GameplayFrame=GameplayFrame;
            double altitude=s.Altitude;if(s.ScalingEnabled){double threshold=s.Altitude*.95;altitude=MathHelper.Clamp(altitude*(1-MathHelper.Clamp((distance-threshold)/(threshold*1.5),0,1)),s.MinScaledAltitude,s.Altitude);}
            MatrixD world=MatrixD.CreateScale(altitude)*MatrixD.CreateFromAxisAngle(Vector3D.Normalize(s.RotationAxis),(float)cloud.Rotation);world.Translation=s.CenterPoint-camera;
            var constants=new ReflectedConstantBuffer(backend.EnvironmentLayouts[2],"CloudsConstants");constants.Set("World",NativeBackend.MatrixValues(world));constants.Set("ViewProj",NativeBackend.MatrixValues(viewProjection));constants.Set("Color",color.X,color.Y,color.Z,color.W);
            var model=scene.GetModel(s.Model);if(model==null)throw new FileNotFoundException("Cloud MWM could not be loaded.",s.Model);
            foreach(var part in model.Parts){string cm=part.ColorTexture,am=part.AlphaTexture;if(s.Textures?.Count>0){string path=s.Textures[0],ext=Path.GetExtension(path),stem=path.Substring(0,path.Length-ext.Length);cm=stem+"_cm"+ext;am=stem+"_alphamask"+ext;}
                var draw=NativeEnvironmentDraw.Create(1,s.ID,constants.Bytes);draw.Mesh=part.Mesh;draw.Color=scene.TextureCache.GetTexture(cm,true);draw.Alpha=scene.TextureCache.GetTexture(am,false);draws.Add(draw);}
        }
    }
    private void RemoveAtmosphere(uint id){backend.RemoveResource(7,id);atmospheres.Remove(id);}
    public void Clear(){foreach(var id in atmospheres.Keys.ToArray())RemoveAtmosphere(id);clouds.Clear();Fog=default;}
    public void Dispose()=>Clear();
}
