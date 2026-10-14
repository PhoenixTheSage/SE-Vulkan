using System.Collections;
using System.Reflection;
using ClientPlugin.Rendering.Scene;

namespace ScenePerformanceHarness;
public sealed partial class Plugin
{
    static object gpuAdmissionSnapshot;
    // One diagnostic snapshot only. Never run this reflection audit during an
    // ordinary comparison or use loaded actor counts as visible cost estimates.
    static object AuditGpuAdmission(VulkanScene scene)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var type=scene.GetType();var actors=(IDictionary)type.GetField("actors",flags).GetValue(scene);
        var dirty=type.GetField("gpuDirty",flags).GetValue(scene);var contains=dirty.GetType().GetMethod("Contains");
        var supported=type.GetMethod("GpuActorSupported",flags);var modelSupported=type.GetMethod("GpuModelSupported",flags);
        var reasons=new Dictionary<string,int>();int cpuEligible=0,managedEligible=0,cpuEligibleNoFailure=0,dirtyCpuEligible=0;
        foreach(DictionaryEntry entry in actors){
            var actor=entry.Value;var actorType=actor.GetType();object Read(string name)=>actorType.GetField(name,flags).GetValue(actor);
            var model=Read("Model");if(model==null||(bool)Read("GpuOwned"))continue;
            bool cpu=(bool)Read("PassOwned"),eligible=(bool)supported.Invoke(scene,new[]{actor});string rejection;
            if(eligible){
                if(cpu){cpuEligible++;if((bool)contains.Invoke(dirty,new[]{actor}))dirtyCpuEligible++;}else managedEligible++;
                rejection=(string)Read("GpuFallbackReason");
                if(rejection==null){if(cpu)cpuEligibleNoFailure++;rejection="predicate eligible; no recorded rejection";}
            }else if(Read("SkinMatrices")!=null)rejection="skeletal";
            else if(Convert.ToSingle(Read("Dithering"))!=0)rejection="dithered";
            else if(((IDictionary)Read("Materials")).Count>31)rejection="appearance count exceeds GPU admission";
            else if(((Array)model.GetType().GetField("LodDistances",flags).GetValue(model)).Length>16)rejection="authored LOD count exceeds GPU admission";
            else if(!(bool)modelSupported.Invoke(null,new[]{model}))rejection="geometry/technique admission";
            else rejection="highlight/exclusion admission";
            string key=(cpu?"native CPU: ":"managed: ")+rejection;reasons[key]=reasons.TryGetValue(key,out int n)?n+1:1;
        }
        return new{LoadedActorScope=true,EligibleNativeCpuActors=cpuEligible,EligibleNativeCpuWithoutRecordedFailure=cpuEligibleNoFailure,DirtyEligibleNativeCpuActors=dirtyCpuEligible,EligibleManagedActors=managedEligible,Reasons=reasons};
    }
}
