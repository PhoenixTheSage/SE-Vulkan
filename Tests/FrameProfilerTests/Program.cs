using ClientPlugin.Diagnostics;

static void Require(bool value, string detail) { if (!value) throw new Exception(detail); }
static void Frame(ulong gpu = 0, float ms = -1, bool paused = false)
{
    FrameProfiler.BeginDraw();
    FrameProfiler.Commit(paused, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, gpu, ms);
}
Frame(1, 2); Frame(1, 50); Frame(2, 4);
var view = FrameProfiler.Capture(false);
Require(view.GpuSamples == 2 && view.GpuMs == 3, "Repeated asynchronous GPU samples biased the mean.");
Frame(3, float.NaN);
Require(FrameProfiler.Capture(true).GpuMs == -1, "Invalid GPU sample was displayed as valid.");
Frame(4, 100, true);
view = FrameProfiler.Capture(false);
Require(view.Frames == 4 && view.GpuSamples == 2 && view.GpuMs == 3, "Paused frame polluted gameplay average.");
FrameProfiler.Record = false; FrameProfiler.Record = true;
for (int i = 0; i < 2500; ++i) Frame();
Require(FrameProfiler.Capture(false).Frames == 2500, "History cannot hold ten seconds at 240 fps.");
for (int i = 0; i < 2000; ++i) Frame();
Require(FrameProfiler.Capture(false).Frames == 4096, "Ring capacity/reuse failed.");
FrameProfiler.Record = false; Frame(5, 8);
Require(FrameProfiler.Capture(false).Frames == 0 && FrameProfiler.Capture(true).GpuMs == 8, "Recording off lost live GPU timing or retained history.");
Console.WriteLine("PASS GPU sample deduplication, invalid samples, pause history, 240 fps capacity, ring reuse and live-only recording.");

// Frame split: frame N's laps finish on the build thread (Adopt) while the render thread opens frame N+1; neither
// frame's sections leak into the other, and the render thread's laps stop once it detaches.
static void Spin(double ms) { var w = System.Diagnostics.Stopwatch.StartNew(); while (w.Elapsed.TotalMilliseconds < ms) { } }
FrameProfiler.BeginDraw(); Spin(2); FrameProfiler.Lap(FrameProfiler.Section.Messages);
var handed = FrameProfiler.Current; FrameProfiler.Detach();
FrameProfiler.Lap(FrameProfiler.Section.Sprites); // detached: dropped
var build = new Thread(() => { FrameProfiler.Adopt(handed); Spin(4); FrameProfiler.Lap(FrameProfiler.Section.Queue); Spin(1); FrameProfiler.Commit(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0); });
FrameProfiler.BeginDraw(); build.Start(); Spin(3); FrameProfiler.Lap(FrameProfiler.Section.Particles); build.Join();
var split = FrameProfiler.Capture(true).Milliseconds;
Require(split[(int)FrameProfiler.Section.Messages] >= 1.5f, "Apply's lap was lost in the hand-over.");
Require(split[(int)FrameProfiler.Section.Queue] >= 3.5f && split[(int)FrameProfiler.Section.Queue] < 6f, "The build thread's first lap must count from its adoption of the frame.");
Require(split[(int)FrameProfiler.Section.Particles] == 0 && split[(int)FrameProfiler.Section.Sprites] == 0, "Frame N+1's or a detached lap leaked into frame N.");
FrameProfiler.Commit(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
Require(FrameProfiler.Capture(true).Milliseconds[(int)FrameProfiler.Section.Particles] >= 2.5f, "Frame N+1 lost its own lap.");
Console.WriteLine("PASS split frames: build-thread adoption, no cross-frame laps, detached laps dropped.");
