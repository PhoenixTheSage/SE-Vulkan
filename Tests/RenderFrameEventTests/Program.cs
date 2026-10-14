using System.Diagnostics.Tracing;
using ScenePerformanceHarness;
if(args.Contains("--emit")){for(long i=0;i<200;i++){RenderFrameEvents.Log.Begin(i);Thread.Sleep(10);RenderFrameEvents.Log.End(i,10);}return;}
using var listener=new FrameListener();
RenderFrameEvents.Log.Begin(123L);RenderFrameEvents.Log.End(123L,17.25);
if(listener.Events.Count!=2)throw new Exception("Expected Begin and End; received "+listener.Events.Count);
var begin=listener.Events[0];var end=listener.Events[1];
if(begin.Id!=1||end.Id!=2||begin.Payload[0] is not long||end.Payload[0] is not long||end.Payload[1] is not long||Convert.ToInt64(end.Payload[0])!=123||Convert.ToInt64(end.Payload[1])!=17250)throw new Exception("Frame event payload contract failed.");
Console.WriteLine("Begin/End event IDs and typed payloads verified.");
sealed class FrameListener:EventListener{
 public readonly List<(int Id,object?[] Payload)> Events=new();
 protected override void OnEventSourceCreated(EventSource source){if(source.Name=="SEVulkan-RenderFrames")EnableEvents(source,EventLevel.Informational);}
 protected override void OnEventWritten(EventWrittenEventArgs e){Events.Add((e.EventId,e.Payload?.ToArray()??Array.Empty<object>()));}
}
