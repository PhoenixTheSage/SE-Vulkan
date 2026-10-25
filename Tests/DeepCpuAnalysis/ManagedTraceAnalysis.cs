using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using System.Text.Json;

static class ManagedTraceAnalysis
{
    public static void Run(string input)
    {
        input=Path.GetFullPath(input);string run=Path.GetDirectoryName(input)!;
        using var conversion=File.CreateText(Path.Combine(run,"managed-conversion.log"));
        string etlx=TraceLog.CreateFromEventPipeDataFile(input,null,new TraceLogOptions{ConversionLog=conversion});
        using var trace=new TraceLog(etlx);
        var markers=new List<Marker>();
        var eventIds=new Dictionary<string,int>();
        using(var source=new EventPipeEventSource(input)){
            source.Dynamic.All+=e=>{if(e.ProviderName!="SEVulkan-RenderFrames")return;string key=e.ID+"/"+e.EventName;eventIds[key]=eventIds.GetValueOrDefault(key)+1;if(e.ID==(TraceEventID)1||e.ID==(TraceEventID)2)markers.Add(new Marker(e.ID==(TraceEventID)1?"Begin":"End",e.ThreadID,e.TimeStampRelativeMSec,Convert.ToInt64(e.PayloadByName("sequence"))));};
            source.Process();
        }
        File.WriteAllText(Path.Combine(run,"managed-provider-counts.json"),JsonSerializer.Serialize(eventIds));
        var tids=markers.Select(e=>e.ThreadID).Distinct().ToArray();
        if(tids.Length!=1)throw new Exception("Expected one marked render thread; got "+tids.Length);
        var starts=markers.Where(e=>e.EventName=="Begin").ToDictionary(e=>e.Sequence);
        var spans=markers.Where(e=>e.EventName=="End"&&starts.ContainsKey(e.Sequence)).Select(e=>new{Start=starts[e.Sequence].TimeStampRelativeMSec,End=e.TimeStampRelativeMSec,e.Sequence}).OrderBy(e=>e.Start).ToArray();
        if(markers.Count<2)throw new Exception("Insufficient renderer markers.");
        double first=spans.Length>0?spans.First().Start:markers.First().TimeStampRelativeMSec,last=spans.Length>0?spans.Last().End:markers.Last().TimeStampRelativeMSec;
        var events=trace.Events.Where(e=>e.ProviderName=="Microsoft-Windows-DotNETRuntime"&&e.EventName.StartsWith("GC/")&&e.TimeStampRelativeMSec>=first&&e.TimeStampRelativeMSec<=last)
            .Select(e=>new{e.EventName,e.ThreadID,e.TimeStampRelativeMSec,Values=e.PayloadNames.ToDictionary(n=>n,n=>e.PayloadByName(n))}).ToArray();
        var suspended=new List<double>();var reasons=new Dictionary<int,(int Count,double Milliseconds)>();double? suspend=null;int reason=0;
        foreach(var e in events){if(e.EventName=="GC/SuspendEEStart"){suspend=e.TimeStampRelativeMSec;reason=Convert.ToInt32(e.Values["Reason"]);}
            if(e.EventName=="GC/RestartEEStop"&&suspend.HasValue){double elapsed=e.TimeStampRelativeMSec-suspend.Value;var old=reasons.GetValueOrDefault(reason);reasons[reason]=(old.Count+1,old.Milliseconds+elapsed);if(reason==1)suspended.Add(elapsed);suspend=null;}}
        var allocations=new Dictionary<string,double>();var callers=new Dictionary<string,double>();int withStack=0,count=0;
        foreach(var e in trace.Events.Where(e=>e.EventName=="GC/AllocationTick"&&e.ThreadID==tids[0]&&e.TimeStampRelativeMSec>=first&&e.TimeStampRelativeMSec<=last)){
            count++;var stack=e.CallStack();if(stack==null)continue;withStack++;
            string owner="[no plugin allocator]";while(stack!=null){string name=stack.CodeAddress.FullMethodName;if(name.Contains("ClientPlugin.")||name.Contains("ScenePerformanceHarness.")){owner=name;break;}stack=stack.Caller;}
            // The next plugin frame up names the caller of small allocating helpers.
            string parent="[none]";for(var up=stack?.Caller;up!=null;up=up.Caller){string name=up.CodeAddress.FullMethodName;if(name.Contains("ClientPlugin.")||name.Contains("ScenePerformanceHarness.")){parent=name;break;}}
            string key=Convert.ToString(e.PayloadByName("TypeName"))+" <- "+owner;
            double bytes=Convert.ToDouble(e.PayloadByName("AllocationAmount64"));
            allocations[key]=allocations.GetValueOrDefault(key)+bytes;callers[key+" <- "+parent]=callers.GetValueOrDefault(key+" <- "+parent)+bytes;
        }
        var options=new JsonSerializerOptions{WriteIndented=true};
        File.WriteAllText(Path.Combine(run,"managed-allocation-callers.json"),JsonSerializer.Serialize(callers.OrderByDescending(e=>e.Value).Select(e=>new{Name=e.Key,SampledBytes=e.Value}),options));
        File.WriteAllText(Path.Combine(run,"managed-suspension-reasons.json"),JsonSerializer.Serialize(reasons.OrderBy(e=>e.Key).Select(e=>new{Reason=e.Key,e.Value.Count,e.Value.Milliseconds,Scope="Reason 1 denotes garbage collection; other suspensions are not counted as GC pauses."}),options));
        File.WriteAllText(Path.Combine(run,"managed-frame-markers.json"),JsonSerializer.Serialize(markers,options));
        File.WriteAllText(Path.Combine(run,"managed-gc-events.json"),JsonSerializer.Serialize(events,options));
        File.WriteAllText(Path.Combine(run,"managed-events-summary.json"),JsonSerializer.Serialize(new{RenderThreadId=tids[0],LostEvents=trace.EventsLost,CompleteFrames=spans.Length,BeginMarkers=markers.Count(e=>e.EventName=="Begin"),EndMarkers=markers.Count(e=>e.EventName=="End"),WindowStartMs=first,WindowEndMs=last,FrameWallMeanMs=spans.Length>0?(double?)spans.Average(e=>e.End-e.Start):null,GcSuspensions=suspended.Count,GcSuspendedMs=suspended.Sum(),GcSuspensionMaxMs=suspended.DefaultIfEmpty().Max(),AllocationSamples=count,AllocationSamplesWithStack=withStack,AllocationTriggers=allocations.OrderByDescending(e=>e.Value).Take(30).Select(e=>new{Name=e.Key,SampledBytes=e.Value}),Scope="Instrumented runtime events. Without paired markers window is first-to-last recorded marker and complete frame mean is unavailable. AllocationTick bytes approximate trigger attribution. Sampled thread time does not establish active CPU or native functions."},options));
        Console.WriteLine("Managed event analysis complete: "+run);
    }
    record Marker(string EventName,int ThreadID,double TimeStampRelativeMSec,long Sequence);
}
