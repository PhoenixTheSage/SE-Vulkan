using System;
using System.Diagnostics;

namespace ClientPlugin.Rendering;

/// <summary>Matches the game's measured, fixed-step and paused render animation modes.</summary>
internal sealed class RenderFrameClock
{
    private readonly double frequency;
    private long previous;
    internal float StepOverride { get; set; }
    internal RenderFrameClock() : this(Stopwatch.GetTimestamp(), Stopwatch.Frequency) { }
    internal RenderFrameClock(long initialTimestamp, long timestampFrequency)
    { previous = initialTimestamp; frequency = timestampFrequency; }
    internal float Sample() => Sample(Stopwatch.GetTimestamp());
    internal float Sample(long timestamp)
    {
        double elapsed = Math.Max(0, (timestamp - previous) / frequency);
        previous = timestamp;
        if (StepOverride < 0) return 0;
        if (StepOverride > 0) return StepOverride;
        return (float)Math.Min(elapsed, 0.066);
    }
}
