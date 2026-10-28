using System;
using System.Diagnostics;
using System.Threading;

namespace ClientPlugin.Diagnostics;

/// <summary>Render-thread timings. Unpaused frames stay in a 10 second window measured from the newest gameplay frame, so opening the pause menu does not erase it. Live shows the newest frame, including while that menu is open.</summary>
internal static class FrameProfiler
{
    public enum Section
    {
        Messages, Clipmaps, Particles, SceneUpdate, ActorCull, InstancePack, Materials,
        Voxels, Shadows, Setup, Queue, FrameSlot, Sprites, Host, NativeRecord
    }
    public const int SectionCount = 15;
    public static readonly string[] Names =
    {
        "Messages", "Clipmaps", "Particles and decals", "Scene update", "Actor cull", "Instance pack", "Materials",
        "Voxels and flares", "Shadow fit", "Lighting setup", "Queue world", "Frame slot", "Sprites", "Host gap", "Native record"
    };
    public static readonly string[] Details =
    {
        "Render messages drained this frame.",
        "Clipmap residency update. This is what lets the loading screen finish.",
        "Particle and decal packet build.",
        "Actor transforms, the scene updater, lights and the skybox.",
        "Visibility, occlusion and cascade tests.",
        "Per-actor instance streams copied for cubes and generic instances.",
        "Per-part object constants, texture lookup and packet submission.",
        "Voxel cells, foliage, flares and billboards.",
        "Cascade fit and spotlight list.",
        "Environment, post and probe setup.",
        "Pins world packets and copies them into the native queue.",
        "Waits for this frame slot's previous GPU work, acquires the swap image, and opens the command buffer.",
        "HUD and main-target sprites.",
        "Render-thread time after sprites and before native command recording.",
        "Native EndFrame. The rows below are pieces of this time."
    };
    public static bool Live { get; set; }
    static bool record = true;
    /// <summary>Keeps unpaused frames for the 10 second average. Off drops that history and stops allocating it.</summary>
    public static bool Record
    {
        get => record;
        set
        {
            if (record == value) return;
            record = value;
            if (!value) ClearHistory();
        }
    }
    // Ten seconds at 240 fps needs 2400 entries. Retain headroom through 409 fps.
    const int Capacity = 4096;
    static readonly Sample[] History = new Sample[Capacity];
    static readonly float[] Pending = new float[SectionCount];
    static readonly float[] LatestMilliseconds = new float[SectionCount];
    static int count, next;
    static long frameStart;
    static volatile bool drawing;
    static bool haveLatest, haveDrawBaseline;
    // Frame split (milestone 4, slice 5): laps may come from Apply (the render thread) and Build (later its own thread).
    // Each thread keeps its own lap mark; a thread's first lap in a frame starts its mark and adds nothing. Pending is
    // shared, so its updates take a lock (uncontended on one thread).
    static readonly object gate = new object();
    static int frameId;
    [ThreadStatic] static long threadMark;
    [ThreadStatic] static int threadFrame;
    static ref long Mark(long now)
    {
        if (threadFrame != Volatile.Read(ref frameId)) { threadFrame = frameId; threadMark = now; }
        return ref threadMark;
    }
    static void Add(Section section, long ticks)
    {
        float ms = (float)(ticks * 1000.0 / Stopwatch.Frequency);
        lock (gate) Pending[(int)section] += ms;
    }
    static ulong drawBaseline;
    static ulong gpuBaseline;
    static Sample latest;

    public static void BeginDraw()
    {
        long now = Stopwatch.GetTimestamp();
        lock (gate)
        {
            frameStart = now;
            Array.Clear(Pending, 0, Pending.Length);
            threadFrame = ++frameId;
            threadMark = now;
        }
        drawing = true;
    }
    public static void Lap(Section section)
    {
        if (!drawing) return;
        long now = Stopwatch.GetTimestamp();
        ref long mark = ref Mark(now);
        Add(section, now - mark);
        mark = now;
    }
    public static void Since(Section section, long started)
    {
        if (!drawing) return;
        long now = Stopwatch.GetTimestamp();
        Add(section, now - started);
        Mark(now) = now;
    }
    public static void Deposit(Section section, long ticks)
    {
        if (!drawing || ticks <= 0) return;
        Add(section, ticks);
    }
    public static void Advance()
    {
        if (!drawing) return;
        long now = Stopwatch.GetTimestamp();
        Mark(now) = now;
    }
    public static void Commit(bool paused, uint spotUs, uint shadowUs, uint mainUs, uint probeUs, uint prepareUs, uint tailUs, uint waitUs, uint acquireUs, uint resetUs, ulong worldDraws, ulong gpuSample = 0, float gpuMs = -1)
    {
        if (!drawing) return;
        long now = Stopwatch.GetTimestamp();
        ulong frameDraws = 0;
        if (worldDraws != 0)
        {
            if (haveDrawBaseline && worldDraws >= drawBaseline) frameDraws = worldDraws - drawBaseline;
            drawBaseline = worldDraws;
            haveDrawBaseline = true;
        }
        bool store = record && !paused;
        bool haveGpu = gpuSample != 0 && gpuMs >= 0 && !float.IsNaN(gpuMs) && !float.IsInfinity(gpuMs);
        bool newGpu = haveGpu && gpuSample != gpuBaseline;
        if (haveGpu) gpuBaseline = gpuSample;
        var sample = new Sample
        {
            Tick = now, Paused = paused, Total = (float)((now - frameStart) * 1000.0 / Stopwatch.Frequency),
            SpotUs = spotUs, ShadowUs = shadowUs, MainUs = mainUs, ProbeUs = probeUs, PrepareUs = prepareUs, TailUs = tailUs, WaitUs = waitUs, AcquireUs = acquireUs, ResetUs = resetUs,
            GpuMs = haveGpu ? gpuMs : -1, NewGpu = newGpu,
            WorldDraws = frameDraws, Milliseconds = store ? History[next].Milliseconds ?? new float[SectionCount] : LatestMilliseconds
        };
        lock (gate) Array.Copy(Pending, sample.Milliseconds, SectionCount);
        latest = sample;
        haveLatest = true;
        if (store)
        {
            History[next] = sample;
            next = (next + 1) % Capacity;
            if (count < Capacity) count++;
        }
        drawing = false;
    }
    static void ClearHistory()
    {
        Array.Clear(History, 0, History.Length);
        count = 0;
        next = 0;
    }
    public static View Capture(bool live)
    {
        var view = new View { Live = live, HaveFrame = haveLatest, GpuMs = -1, Milliseconds = new float[SectionCount] };
        if (!haveLatest) return view;
        if (live)
        {
            view.Paused = latest.Paused;
            view.Frames = 1;
            view.Total = latest.Total;
            view.WorldDraws = latest.WorldDraws;
            view.GpuMs = latest.GpuMs;
            view.GpuSamples = latest.GpuMs >= 0 ? 1 : 0;
            Array.Copy(latest.Milliseconds, view.Milliseconds, SectionCount);
            view.Spot = latest.SpotUs / 1000f; view.Shadow = latest.ShadowUs / 1000f; view.Main = latest.MainUs / 1000f;
            view.Probe = latest.ProbeUs / 1000f; view.Prepare = latest.PrepareUs / 1000f; view.Tail = latest.TailUs / 1000f;
            view.Wait = latest.WaitUs / 1000f; view.Acquire = latest.AcquireUs / 1000f; view.Reset = latest.ResetUs / 1000f;
            return view;
        }
        long newest = 0;
        for (int i = 0; i < count; i++) if (History[i].Tick > newest) newest = History[i].Tick;
        long cutoff = newest - (long)(10.0 * Stopwatch.Frequency);
        float total = 0, spot = 0, shadow = 0, main = 0, probe = 0, prepare = 0, tail = 0, wait = 0, acquire = 0, reset = 0;
        ulong draws = 0;
        int frames = 0;
        float gpuTotal = 0;
        for (int i = 0; i < count; i++)
        {
            var sample = History[i];
            if (sample.Tick < cutoff || sample.Milliseconds == null) continue;
            frames++;
            total += sample.Total;
            draws += sample.WorldDraws;
            if (sample.NewGpu) { gpuTotal += sample.GpuMs; view.GpuSamples++; }
            spot += sample.SpotUs; shadow += sample.ShadowUs; main += sample.MainUs; probe += sample.ProbeUs; prepare += sample.PrepareUs; tail += sample.TailUs; wait += sample.WaitUs; acquire += sample.AcquireUs; reset += sample.ResetUs;
            for (int s = 0; s < SectionCount; s++) view.Milliseconds[s] += sample.Milliseconds[s];
        }
        view.Frames = frames;
        if (frames == 0) return view;
        view.Total = total / frames;
        view.WorldDraws = draws / (ulong)frames;
        if (view.GpuSamples > 0) view.GpuMs = gpuTotal / view.GpuSamples;
        float scale = 1f / frames / 1000f;
        view.Spot = spot * scale; view.Shadow = shadow * scale; view.Main = main * scale; view.Probe = probe * scale; view.Prepare = prepare * scale; view.Tail = tail * scale; view.Wait = wait * scale; view.Acquire = acquire * scale; view.Reset = reset * scale;
        for (int s = 0; s < SectionCount; s++) view.Milliseconds[s] /= frames;
        return view;
    }
    public struct View
    {
        public bool Live, Paused, HaveFrame;
        public int Frames;
        public int GpuSamples;
        public float GpuMs;
        public float Total, Spot, Shadow, Main, Probe, Prepare, Tail, Wait, Acquire, Reset;
        public ulong WorldDraws;
        public float[] Milliseconds;
    }
    struct Sample
    {
        public long Tick;
        public bool Paused;
        public bool NewGpu;
        public float GpuMs;
        public float Total;
        public float[] Milliseconds;
        public uint SpotUs, ShadowUs, MainUs, ProbeUs, PrepareUs, TailUs, WaitUs, AcquireUs, ResetUs;
        public ulong WorldDraws;
    }
}
