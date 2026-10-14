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
