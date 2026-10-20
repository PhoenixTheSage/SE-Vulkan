using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using VRageMath;
using Sandbox.Game.World;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    const string ParityMedium="CreativeFixture.SharedVolume";
    bool anomalyParityMode,parityComplete;
    Assembly anomalyAssembly;
    float parityNear;
    int parityCommits,paritySamples,parityPhase;
    double parityStarted,parityReportAt;
    bool staticLightingTurbineHidden;

    object AnomalyCall(string type,string method,params object[] values)
    {
        var info=anomalyAssembly.GetType("ClientPlugin."+type,true).GetMethod(method,BindingFlags.Public|BindingFlags.Static);
        if(info==null)throw new MissingMethodException(type,method);
        return info.Invoke(null,values);
    }
    object AnomalyProperty(string type,string name)=>anomalyAssembly.GetType("ClientPlugin."+type,true).GetProperty(name,BindingFlags.Public|BindingFlags.Static)?.GetValue(null);
    static object ImageProperty(object image,string name)
    {
        if(image==null)return null;
        var property=image.GetType().GetProperty(name)??image.GetType().GetInterfaces().Select(t=>t.GetProperty(name)).FirstOrDefault(p=>p!=null);
        return property?.GetValue(image);
    }
    void BeginAnomalyParity()
    {
        anomalyParityMode=Environment.GetEnvironmentVariable("SE_VULKAN_ANOMALY_PARITY")=="1";
        if(!anomalyParityMode)return;
        // Give both renderer routes the same finite private-world environment.
        // EmptyWorld can retain an undefined disabled rotating-sun direction.
        var towardSun=Vector3.Normalize(new Vector3(.3f,.5f,1));
        MySector.SunProperties.BaseSunDirectionNormalized=towardSun;
        MySector.SunProperties.SunDirectionNormalized=towardSun;
        File.WriteAllText(Path.Combine(reports,"anomaly-environment.txt"),"Fixed private-fixture sun toward="+towardSun);
        anomalyAssembly=AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a=>a.GetName().Name=="Anomaly");
        if(anomalyAssembly==null)throw new InvalidOperationException("The parity profile did not load the actual Anomaly core.");
        string source=Path.Combine(reports,"CreativeMedium.hlsli");
        File.WriteAllText(source,"AnomalyMediumSample EvaluateMedium(float3 p){AnomalyMediumSample m=AnomalyEmptyMedium();m.extinction=.002;m.emission=float3(.001,.0015,.002);return m;}");
        if(!(AnomalyCall("Shaders.VolumetricMediumRegistry","Register",ParityMedium,source,Array.Empty<string>()) is true))throw new InvalidOperationException("Live parity medium registration failed.");
        AnomalyCall("Shaders.VolumetricMediumRegistry","SetParameters",ParityMedium,Array.Empty<float>(),new[]{-1e8,-1e8,-1e8,1e8,1e8,1e8},new float[3]);
        AnomalyCall("Shaders.VolumetricMediumRegistry","SetIntervalCallback",ParityMedium,(Action<float>)(distance=>{parityNear=distance;if(distance>0)Interlocked.Increment(ref parityCommits);}));
        AnomalyCall("Shaders.VolumetricMediumRegistry","Configure",0,1000f,0);
        AnomalyCall("Shaders.VolumetricMediumRegistry","SetEnabled",ParityMedium,true);
        AnomalyCall("Shaders.VolumetricMediumRegistry","RequestRenderer",ParityMedium,true);
        AnomalyCall("Buffers.BufferCatalog","RequestLitMips",4);
        AnomalyCall("Buffers.BufferCatalog","RequestOccupancy",true);
        AnomalyCall("Buffers.BufferCatalog","RequestPointShadows",2,64);
        AnomalyCall("Buffers.BufferCatalog","RequestWorldBoxes",true);
        BeginInteriorParity();
    }
    void ExerciseAnomalyParity(double seconds)
    {
        if(!anomalyParityMode)return;
        if(lightingOwner==this && Volatile.Read(ref lightingPending)<0)throw new InvalidOperationException("Lighting buffer capture failed; inspect lighting-capture-failed.txt.");
        ExerciseInteriorParity();
        if(!staticLightingTurbineHidden && seconds>=75 && Environment.GetEnvironmentVariable("SE_VULKAN_STATIC_LIGHTING_DIAGNOSTICS")=="1") {
            if(turbine==null)throw new InvalidOperationException("Static lighting diagnostic needs the fixture turbine.");
            // Earlier animation captures still exercise the actual moving turbine.
            // Only the held lighting comparison excludes this asynchronous caster.
            turbine.Render.Visible=false;staticLightingTurbineHidden=true;
            File.WriteAllText(Path.Combine(reports,"lighting-scene.xml"),"<LightingScene Schema=\"1\" TurbineVisible=\"false\" Scope=\"Static held-camera isolation; earlier animation evidence retained\" />");
        }
        bool ready=parityNear==1000 && interiorParityComplete;
        var state=new StringBuilder("backend="+expectedBackend+"; seconds="+seconds.ToString("F1",CultureInfo.InvariantCulture)+"; phase="+parityPhase+"; interiorPhase="+interiorParityPhase+"; interiorComplete="+interiorParityComplete+"; near="+parityNear+"; commits="+parityCommits);
        foreach(string name in new[]{"occupancy","pointShadowAtlas","playerDepth","litMips","volumeReactive"}) {
            object image=AnomalyCall("Buffers.BufferCatalog","Active",name);
            bool available=ImageProperty(image,"IsAvailable") is true;
            ready&=available;
            state.AppendLine().Append(name).Append(" available=").Append(available).Append(" size=").Append(ImageProperty(image,"Width")).Append('x').Append(ImageProperty(image,"Height"));
        }
        if(ready)paritySamples++;
        state.AppendLine().Append("readySamples=").Append(paritySamples).AppendLine().Append(AnomalyProperty("Shaders.VolumetricMediumRegistry","StatusLine"));
        state.AppendLine().Append("point shadows: ").Append(AnomalyProperty("Buffers.BufferCatalog","PointShadowStatus"));
        if(seconds>=parityReportAt){parityReportAt=seconds+1;File.WriteAllText(Path.Combine(reports,"anomaly-parity.txt"),state.ToString());File.AppendAllText(Path.Combine(reports,"anomaly-parity-history.txt"),state+"\n\n");}
        if(parityPhase==0 && seconds>=80 && ready) {parityStarted=seconds;parityPhase=1;Camera(new Vector3D(15,11,-19),Vector3D.Zero);}
        if(parityPhase==1 && seconds-parityStarted>=5) {
            AnomalyCall("Shaders.VolumetricMediumRegistry","RequestRenderer",ParityMedium,false);AnomalyCall("Shaders.VolumetricMediumRegistry","SetEnabled",ParityMedium,false);parityPhase=2;
        }
        if(parityPhase==2 && seconds-parityStarted>=10){Capture("anomaly-volume-off");parityPhase=3;}
        if(parityPhase==3 && seconds-parityStarted>=15) {
            AnomalyCall("Shaders.VolumetricMediumRegistry","SetEnabled",ParityMedium,true);AnomalyCall("Shaders.VolumetricMediumRegistry","RequestRenderer",ParityMedium,true);parityPhase=4;
        }
        if(parityPhase==4 && seconds-parityStarted>=20 && ready){Capture("anomaly-volume-on");parityPhase=5;}
        if(parityPhase==5 && seconds-parityStarted>=25 && ready && (lightingOwner!=this || (Volatile.Read(ref lightingPending)==2 && Volatile.Read(ref lightingPostprocessReady)==3)) && File.Exists(Path.Combine(reports,"anomaly-volume-on.png")) && File.Exists(Path.Combine(reports,"anomaly-volume-off.png"))) {
            parityComplete=true;parityPhase=6;File.WriteAllText(Path.Combine(reports,"anomaly-producers-passed.txt"),state.ToString());
            Report("Anomaly live producers/capture delivery passed; visual comparison remains under review.");
        }
    }
}
