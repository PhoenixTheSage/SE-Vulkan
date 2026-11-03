using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;

namespace ScenePerformanceHarness;
public sealed partial class Plugin {
    static bool nativeCriticalEnabled,haveCritical;
    static NativeCriticalProfile criticalFirst,criticalLast;
    static NativeRayProfileState criticalRayFirst,criticalRayLast;
    static long criticalObservations;
    static void SampleNativeCritical(NativeBackend backend){
        var value=backend.GetCriticalProfile();criticalObservations++;
        var ray=backend.GetCriticalRayState();
        if(!haveCritical){haveCritical=true;criticalFirst=value;criticalRayFirst=ray;}
        criticalLast=value;criticalRayLast=ray;
    }
    void WriteCriticalEvidence(int accepted){
        if(!nativeCriticalEnabled)return;
        // Write observations before any acceptance gate can throw. Keep the
        // frame census and raw profile available even if no sample qualified.
        var evidence=new {
            Accepted=accepted, SampleCalls=sampleCalls, Observations=criticalObservations, HaveSample=haveCritical,
            BuildTickets=NativeBackend.CriticalBuildTickets, LaunchTickets=NativeBackend.CriticalLaunchTickets,
            ManagedCompleted=CpuPhases.Capture().Frames,
            First=criticalFirst.RawSnapshot(),Last=criticalLast.RawSnapshot()
        };
        File.WriteAllText(Path.Combine(reports,"critical-ownership-evidence.json"),
            JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
    }
    void WriteNativeCritical(){
        if(!nativeCriticalEnabled)return;
        if(!haveCritical||criticalLast.Selected<=criticalFirst.Selected)
            throw new Exception($"Native critical samples unavailable: observations={criticalObservations}, begun={criticalFirst.Begun}..{criticalLast.Begun}, completed={criticalFirst.Completed}..{criticalLast.Completed}, selected={criticalFirst.Selected}..{criticalLast.Selected}. See critical-ownership-evidence.json.");
        ulong selected=criticalLast.Selected-criticalFirst.Selected;
        const int worldPrepare=4,recordOwner=12,commandApi=13;
        if(criticalLast.CallsSince(criticalFirst,recordOwner)!=selected||
           criticalLast.CallsSince(criticalFirst,worldPrepare)==0||
           criticalLast.CallsSince(criticalFirst,commandApi)==0)
            throw new Exception("Native critical profile lacks complete recording-owner, world preparation or command coverage.");
        if(criticalRayLast.Builds!=0||criticalRayLast.Dispatches!=0)
            throw new Exception("Ray-query work contaminated the raster profile.");
        var report=new{Instrumented=true,Data=criticalLast.Describe(criticalFirst),RayStart=criticalRayFirst,RayEnd=criticalRayLast};
        File.WriteAllText(Path.Combine(reports,"native-critical-path.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
    }
}
