using ClientPlugin.Diagnostics;
using System.Diagnostics;

Environment.SetEnvironmentVariable("SE_VULKAN_CPU_DETAIL",args.Contains("--disabled")?"0":"1");
var window=new RendererCpuProfiler.Window();
RendererCpuProfiler.BeginFrame();
if(args.Contains("--disabled")){
    using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Scene))Thread.Sleep(1);
    RendererCpuProfiler.EndFrame();window.AddCompleted();
    if(RendererCpuProfiler.Enabled||window.Capture().Frames!=0||window.CaptureTails().Frames!=0)throw new Exception("Disabled diagnostic published a frame.");
    Console.WriteLine("Disabled profiler: no published frame.");return;
}
using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ActorUpdater))Thread.Sleep(60);
byte[] allocation;
using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.Scene)){
    allocation=new byte[1024*1024];
    long end=Stopwatch.GetTimestamp()+Stopwatch.Frequency/15;
    double sum=0;while(Stopwatch.GetTimestamp()<end)sum+=Math.Sqrt(42);GC.KeepAlive(sum);
}
using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.PreparedActors,false))Thread.SpinWait(10000);
RendererCpuProfiler.EndFrame();window.AddCompleted(150);window.AddCompleted(150);
var first=window.Capture();
RendererCpuProfiler.Row Row(string name)=>first.Rows.Single(r=>r.Phase==name);
if(first.Frames!=1||first.RenderThreadId==0)throw new Exception("Window duplicated or lost frame ownership.");
var wait=Row("ActorUpdater");var busy=Row("Scene");var fine=Row("PreparedActors");
if(wait.CpuAvailable||wait.CpuMs!=null||wait.WallMs<40||!wait.CyclesAvailable||wait.CpuCycles>busy.CpuCycles/2)throw new Exception("Cycle counters did not distinguish a wait from render CPU, or fabricated phase CPU-ms coverage.");
if(busy.CpuAvailable||busy.CpuMs!=null||!busy.CyclesAvailable||busy.CpuCycles<=0)throw new Exception("Phase CPU coverage is invalid.");
var full=Row("Frame");if(!full.CpuAvailable||full.CpuMs<20||full.CpuMs>full.WallMs+16)throw new Exception("Whole-frame CPU aggregate was not measured.");
if(!busy.AllocationAvailable||busy.AllocatedBytes<allocation.Length)throw new Exception("Scope lost known managed allocation.");
if(fine.CpuAvailable||fine.AllocationAvailable||fine.CyclesAvailable||fine.Calls!=1)throw new Exception("Wall-only bucket fabricated CPU/allocation coverage.");
if(first.CounterReadWallMs<=0)throw new Exception("Diagnostic overhead was not reported.");
RendererCpuProfiler.BeginFrame();
using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassPublish))GC.Collect(2,GCCollectionMode.Forced,true,true);
RendererCpuProfiler.EndFrame();window.AddCompleted(10);
if(window.Capture().Frames!=2||window.Capture().Rows.Single(r=>r.Phase=="Frame").Calls!=1)throw new Exception("Window averages are not per accepted frame.");
var tails=window.CaptureTails();
if(tails.Frames!=2||tails.DroppedSamples!=0||tails.GcTaggedFrames<1||tails.Slowest[0].Frame.CompletedDrawMs!=150)throw new Exception("Tail window lost accepted-frame identity, sorting or GC correlation.");
if(tails.Rows.Single(r=>r.Phase=="ActorUpdater").MeanOnSlowest5PercentMs<40||tails.Rows.Single(r=>r.Phase=="Frame").P95Ms<100)throw new Exception("Slow-frame attribution did not select the complete frame tail.");
if(tails.Slowest[0].PhaseWallMs.Length!=tails.Rows.Length)throw new Exception("Tail phase array lost enum order.");
window.Reset();if(window.Capture().Frames!=0||window.CaptureTails().Frames!=0)throw new Exception("Window reset retained stale frames.");
GC.KeepAlive(allocation);
Console.WriteLine($"Profiler checks passed: wait/busy cycles {wait.CpuCycles}/{busy.CpuCycles}; frame wall/CPU {full.WallMs:F2}/{full.CpuMs:F2}ms; allocation {busy.AllocatedBytes}; counter overhead {first.CounterReadWallMs:F3}ms.");
