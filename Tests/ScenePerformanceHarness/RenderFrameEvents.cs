using System.Diagnostics.Tracing;
namespace ScenePerformanceHarness;
[EventSource(Name="SEVulkan-RenderFrames")]
sealed class RenderFrameEvents : EventSource {
    public static readonly RenderFrameEvents Log=new();
    [Event(1,Level=EventLevel.Informational)] public void Begin(long sequence){if(IsEnabled())WriteEvent(1,sequence);}
    [NonEvent] public void End(long sequence,double milliseconds){if(IsEnabled())Completed(sequence,(long)(milliseconds*1000));}
    [Event(2,Level=EventLevel.Informational)] public void Completed(long sequence,long microseconds){if(IsEnabled())WriteEvent(2,sequence,microseconds);}
}
