using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace ClientPlugin.Rendering;

// Phase 4 frame split, milestone 6 (Docs/planning/PLAN-2026-10-25-MEET-DX11.md, "Phase 4"): the dedicated thread that
// runs Build(N) while the render thread runs Apply(N+1). One frame is in flight at a time: Launch hands it a frame,
// Join waits for it and rethrows its failure on the render thread. A long-lived thread, not the job pool, so Build
// never queues behind job work and keeps its thread-local state (the native sort reuse, the building flag).
internal sealed class FrameBuildThread : IDisposable
{
    readonly Thread thread;
    readonly SemaphoreSlim start = new(0, 1), done = new(0, 1);
    Action work;
    Exception failure;
    volatile bool stopping;
    bool inFlight;

    public FrameBuildThread()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Vulkan frame build", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }
    public bool InFlight => inFlight;
    public void Launch(Action build)
    {
        if (inFlight) throw new InvalidOperationException("A frame build is already in flight.");
        work = build; inFlight = true; start.Release();
    }
    public void Join()
    {
        if (!inFlight) return;
        done.Wait(); inFlight = false;
        var error = failure; failure = null;
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    void Run()
    {
        while (true)
        {
            start.Wait();
            if (stopping) return;
            try { work(); }
            catch (Exception error) { failure = error; }
            finally { work = null; done.Release(); }
        }
    }
    public void Dispose()
    {
        try { Join(); } catch { }
        stopping = true; start.Release(); thread.Join();
        start.Dispose(); done.Dispose();
    }
}
