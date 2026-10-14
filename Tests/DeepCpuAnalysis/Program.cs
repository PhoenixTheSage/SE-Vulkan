using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Stacks;
using System.Text.Json;
if(args[0]=="--check-native-profile"){NativeProfileContract.Check(args[1]);return;}
if(args[0]=="--eventpipe"){ManagedTraceAnalysis.Run(args[1]);return;}
string run=Path.GetFullPath(args[0]);int pid=int.Parse(args[1]);
int outputIndex=Array.IndexOf(args,"--output-directory");string output=outputIndex>=0?Path.GetFullPath(args[outputIndex+1]):run;Directory.CreateDirectory(output);
using var log=File.CreateText(Path.Combine(output,"analysis.log"));
using var trace=TraceLog.OpenOrConvert(Path.Combine(run,"cpu.etl"),new TraceLogOptions{ConversionLog=log});
Console.WriteLine("Converted ETL: events="+trace.Events.Count()+" lost="+trace.EventsLost);
var proc=trace.Processes.Last(p=>p.ProcessID==pid);
string native=Path.Combine(run,"Pulsar/Interim/Local/Vulkan/Assets/Native/win-x64"),managed=Path.Combine(run,"Pulsar/Interim/Local/Vulkan"),cache=Path.Combine(run,"SymbolCache");Directory.CreateDirectory(cache);
bool nativeOnly=args.Contains("--native-only");
using var symbols=new SymbolReader(log,(nativeOnly?"":"srv*"+cache+"*https://msdl.microsoft.com/download/symbols;")+native+";"+managed);
symbols.SecurityCheck=p=>p.StartsWith(run,StringComparison.OrdinalIgnoreCase);
foreach(var module in trace.ModuleFiles.Where(m=>m.Name.Equals("se_vulkan_native",StringComparison.OrdinalIgnoreCase)||!nativeOnly&&(m.Name.Equals("coreclr",StringComparison.OrdinalIgnoreCase)||m.Name.Equals("ntdll",StringComparison.OrdinalIgnoreCase)||m.Name.Equals("kernelbase",StringComparison.OrdinalIgnoreCase)))){Console.WriteLine("Resolve "+module.Name+" "+module.FilePath);trace.CodeAddresses.LookupSymbolsForModule(symbols,module);log.Flush();}
var markers=proc.EventsInProcess.Where(e=>e.ProviderName=="SEVulkan-RenderFrames" && (e.ID==(TraceEventID)1||e.ID==(TraceEventID)2)).Select(e=>new{EventName=e.ID==(TraceEventID)1?"Begin":"End",e.ThreadID,e.TimeStampRelativeMSec,Values=e.PayloadNames.ToDictionary(n=>n,n=>e.PayloadByName(n))}).ToArray();
File.WriteAllText(Path.Combine(output,"frame-markers.json"),JsonSerializer.Serialize(markers,new JsonSerializerOptions{WriteIndented=true}));
var tids=markers.Select(m=>m.ThreadID).Distinct().ToArray();if(tids.Length!=1)throw new Exception("Expected one marked render thread; found "+tids.Length);int tid=tids[0];
var begins=markers.Where(m=>m.EventName=="Begin").ToDictionary(m=>Convert.ToInt64(m.Values["sequence"]));
var spans=markers.Where(m=>m.EventName=="End"&&begins.ContainsKey(Convert.ToInt64(m.Values["sequence"]))).Select(m=>(Start:begins[Convert.ToInt64(m.Values["sequence"])].TimeStampRelativeMSec,End:m.TimeStampRelativeMSec,Sequence:Convert.ToInt64(m.Values["sequence"]))).OrderBy(s=>s.Start).ToArray();
double windowStart=spans.First().Start,windowEnd=spans.Last().End;
Console.WriteLine("Render TID="+tid+" markers="+markers.Length);
var cpu=new TraceEventStackSource(proc.EventsInProcess.Filter(e=>e is SampledProfileTraceData&&e.ThreadID==tid));
var threadTime=new MutableTraceEventStackSource(trace);
#pragma warning disable CS0618 // Pinned TraceEvent3.2.8 API used by PerfView itself.
var computer=new ThreadTimeStackComputer(trace,symbols){ExcludeReadyThread=false,IncludeEventSourceEvents=false};computer.GenerateThreadTimeStacks(threadTime);
#pragma warning restore CS0618
object Rank(StackSource source,bool filterThread){
 var leaf=new Dictionary<string,double>();var inclusive=new Dictionary<string,double>();var categories=new Dictionary<string,double>();var buckets=new Dictionary<string,double>();var nativeBoundaries=new Dictionary<string,double>();var blockedLeaf=new Dictionary<string,double>();double total=0;int samples=0;
 source.ForEach(sample=>{
  var names=new List<string>();var stack=sample.StackIndex;
  while(stack!=StackSourceCallStackIndex.Invalid){names.Add(source.GetFrameName(source.GetFrameIndex(stack),false));stack=source.GetCallerIndex(stack);}
  if(filterThread && !names.Any(n=>n.StartsWith("Thread (")&&n.Contains("("+tid+")")))return;
  double weight;
  if(filterThread)weight=Math.Max(0,Math.Min(sample.TimeRelativeMSec+sample.Metric,windowEnd)-Math.Max(sample.TimeRelativeMSec,windowStart));
  else weight=sample.TimeRelativeMSec>=windowStart&&sample.TimeRelativeMSec<=windowEnd?sample.Metric:0;
  if(weight==0)return;
  total+=weight;samples++;string terminal=names.FirstOrDefault()??"[empty]";
  leaf[terminal]=leaf.GetValueOrDefault(terminal)+weight;
  string bucket=RendererSystemClassifier.Classify(names);
  buckets[bucket]=buckets.GetValueOrDefault(bucket)+weight;
  foreach(var name in names.Distinct())inclusive[name]=inclusive.GetValueOrDefault(name)+weight;
  string category=names.FirstOrDefault(n=>n=="CPU_TIME"||n=="BLOCKED_TIME"||n.StartsWith("READIED_TIME"))??"Other";categories[category]=categories.GetValueOrDefault(category)+weight;
  if(category=="BLOCKED_TIME"){
    string boundary=names.FirstOrDefault(n=>n.StartsWith("se_vulkan_native!")||n.StartsWith("plugin!")||n.StartsWith("vrage.render11!")||n.Contains("MyVulkanRender."))??"[no renderer boundary]";
    nativeBoundaries[boundary]=nativeBoundaries.GetValueOrDefault(boundary)+weight;
    string real=names.FirstOrDefault(n=>n.Contains('!')&&!n.StartsWith("ntoskrnl!"))??"[unknown]";
    blockedLeaf[real]=blockedLeaf.GetValueOrDefault(real)+weight;
  }
 });
 object Sorted(Dictionary<string,double> values)=>values.OrderByDescending(v=>v.Value).Take(40).Select(v=>new{Name=v.Key,Milliseconds=v.Value}).ToArray();
 return new{Samples=samples,TotalMetricMs=total,Categories=categories,NonOverlappingBuckets=buckets,Leaf=Sorted(leaf),Inclusive=Sorted(inclusive),BlockedRendererBoundary=Sorted(nativeBoundaries),BlockedLeaf=Sorted(blockedLeaf)};
}
var allCpu=new TraceEventStackSource(proc.EventsInProcess.Filter(e=>e is SampledProfileTraceData));
var threadSystems=new Dictionary<(string Thread,string System),double>();
allCpu.ForEach(sample=>{if(sample.TimeRelativeMSec<windowStart||sample.TimeRelativeMSec>windowEnd)return;var stack=sample.StackIndex;var names=new List<string>();while(stack!=StackSourceCallStackIndex.Invalid){names.Add(allCpu.GetFrameName(allCpu.GetFrameIndex(stack),false));stack=allCpu.GetCallerIndex(stack);}string thread=names.FirstOrDefault(n=>n.StartsWith("Thread ("))??"[unknown]";var key=(thread,RendererSystemClassifier.Classify(names));threadSystems[key]=threadSystems.GetValueOrDefault(key)+sample.Metric;});
File.WriteAllText(Path.Combine(output,"renderer-system-workers.json"),JsonSerializer.Serialize(new{RenderTid=tid,CompleteFrames=spans.Length,LostEvents=trace.EventsLost,Rows=threadSystems.OrderByDescending(x=>x.Value).Select(x=>new{Thread=x.Key.Thread,System=x.Key.System,SampleMs=x.Value,SampleMsPerMarkedFrame=x.Value/spans.Length}),Scope="Disjoint CPU sample attribution by process thread and renderer system during marked window. Worker sample sums are CPU work, not frame critical-path duration; dependencies/waits require scheduling stacks."},new JsonSerializerOptions{WriteIndented=true}));
var allocations=new Dictionary<string,double>();int allocWithStack=0,allocTotal=0;
foreach(var data in proc.EventsInProcess.Where(e=>e.EventName=="GC/AllocationTick"&&e.ThreadID==tid&&e.TimeStampRelativeMSec>=windowStart&&e.TimeStampRelativeMSec<=windowEnd)){
    ++allocTotal;var stack=data.CallStack();if(stack==null)continue;++allocWithStack;
    var names=new List<string>();while(stack!=null){names.Add(stack.CodeAddress.ModuleName+"!"+stack.CodeAddress.FullMethodName);stack=stack.Caller;}
    string owner=names.FirstOrDefault(n=>n.Contains("ClientPlugin.")||n.Contains("ScenePerformanceHarness."))??"[no plugin allocator]";
    string type=Convert.ToString(data.PayloadByName("TypeName"))??"[unknown]";string key=type+" <- "+owner;
    allocations[key]=allocations.GetValueOrDefault(key)+Convert.ToDouble(data.PayloadByName("AllocationAmount64"));
}
File.WriteAllText(Path.Combine(output,"allocation-stacks.json"),JsonSerializer.Serialize(new{Samples=allocTotal,SamplesWithStack=allocWithStack,Scope="AllocationTick sampled bytes since prior allocation tick, grouped by triggering type/stack; approximate attribution, not exact type byte totals",Ranked=allocations.OrderByDescending(v=>v.Value).Take(35).Select(v=>new{Name=v.Key,SampledBytes=v.Value})},new JsonSerializerOptions{WriteIndented=true}));
var gcEvents=proc.EventsInProcess.Where(e=>e.ProviderName=="Microsoft-Windows-DotNETRuntime" && e.EventName.StartsWith("GC/") && e.TimeStampRelativeMSec>=windowStart && e.TimeStampRelativeMSec<=windowEnd).Select(e=>new{e.EventName,e.ThreadID,e.TimeStampRelativeMSec,Values=e.PayloadNames.ToDictionary(n=>n,n=>e.PayloadByName(n))}).ToArray();
File.WriteAllText(Path.Combine(output,"gc-events.json"),JsonSerializer.Serialize(gcEvents,new JsonSerializerOptions{WriteIndented=true}));
File.WriteAllText(Path.Combine(output,"deep-cpu-summary.json"),JsonSerializer.Serialize(new{ProcessId=pid,RenderTid=tid,LostEvents=trace.EventsLost,MarkerCount=markers.Length,CompleteFrames=spans.Length,WindowStartMs=windowStart,WindowEndMs=windowEnd,FrameWallMeanMs=spans.Average(s=>s.End-s.Start),Cpu=Rank(cpu,false),ThreadTime=Rank(threadTime,true),Scope="CPU sample weights and ETW scheduling-derived thread time; inclusive rows overlap; profile overhead not FPS proof."},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Analysis complete");
