using System;
using System.Collections.Generic;

namespace ClientPlugin.Bootstrap;

// Contains no game types; Preloader.Initialize/Patch can safely update this state.
public static class BackendRuntime
{
    private static readonly object Sync = new object();
    private static BackendConfiguration configuration = new BackendConfiguration();
    private static BootstrapConfigStore store;
    private static AttemptJournal journal;
    private static string environmentFingerprint = "";
    private static bool crashHandlerInstalled;
    private static readonly Queue<string> failures = new Queue<string>();
    public static bool HookReady { get; private set; }
    public static string HookFingerprint { get; private set; } = "Not inspected";
    public static bool LazyCacheDisabled { get; internal set; }
    public static string CompatibilitySummary { get; internal set; } = "Not inspected";
    public static string PlatformSummary { get; internal set; } = "Not inspected";
    public static string SelectionSummary { get; private set; } = "DX11 default; early factory has not run";
    public static RendererBackend ActiveBackend { get; private set; } = RendererBackend.Dx11;
    public static bool DecisionMade { get; private set; }
    private static readonly List<Action<int>> guestListeners = new List<Action<int>>();
    private static readonly List<Action> frameListeners = new List<Action>();
    private static readonly List<Action<double[],float[]>> sceneListeners = new();
    public static RendererBackend RequestedBackend { get { lock (Sync) return configuration.RequestedBackend; } }
    public static bool RestartRequired => RequestedBackend != ActiveBackend;
    public static bool RecoveryRequired { get { lock (Sync) return configuration.RecoveryRequired; } }
    public static bool RecoveryPromptPending { get { lock (Sync) return configuration.RecoveryPromptPending; } }
    public static BackendConfiguration Configuration { get { lock (Sync) return configuration.Copy(); } }
    public static string[] RecentFailures { get { lock (Sync) return failures.ToArray(); } }
    public static void SetHook(bool ready, string fingerprint, string reason)
    {
        HookReady = ready; HookFingerprint = fingerprint;
        if (!string.IsNullOrEmpty(reason)) RecordFailure(reason);
    }
    public static void InitializeConfiguration(string userDataPath)
    {
        lock (Sync)
        {
            if (store != null) return;
            store = new BootstrapConfigStore(userDataPath);
            configuration = store.Load(out string warning);
            if (!string.IsNullOrEmpty(warning)) RecordFailure(warning);
            if (store.RecoverIncompleteAttempt(configuration)) RecordFailure(configuration.LastFailure);
        }
    }
    public static void ApplyEnvironmentFingerprint(string fingerprint)
    {
        lock (Sync)
        {
            if (store == null) return;
            environmentFingerprint = fingerprint ?? "";
            if (store.ReleaseChangedRecovery(configuration, environmentFingerprint)) RecordFailure(configuration.LastFailure);
        }
    }
    public static void SaveConfiguration(BackendConfiguration updated)
    {
        lock (Sync)
        {
            if (store == null) throw new InvalidOperationException("Vulkan configuration initialized before the game user-data path.");
            updated = updated.Copy();
            // A delayed user-preference write must not erase a render-thread failure or recovery generation.
            updated.RecoveryRequired = configuration.RecoveryRequired;
            updated.RetryGeneration = configuration.RetryGeneration;
            updated.LastFailure = configuration.LastFailure;
            updated.UncleanExits = configuration.UncleanExits;
            updated.RendererFaultSeen = configuration.RendererFaultSeen;
            updated.RecoveryPromptPending = configuration.RecoveryPromptPending;
            updated.FailureFingerprint = configuration.FailureFingerprint;
            store.Save(updated);
            configuration = updated;
        }
    }
    public static void RequestBackend(RendererBackend backend)
    {
        lock (Sync)
        {
            var updated = configuration.Copy(); updated.RequestedBackend = backend;
            if (backend != RendererBackend.Vulkan) updated.ExclusiveFullscreen = false;
            SaveConfiguration(updated);
        }
    }
    public static void RetryVulkan()
    {
        lock (Sync)
        {
            if (store == null || !configuration.RecoveryRequired || ActiveBackend == RendererBackend.Vulkan) return;
            var updated = configuration.Copy(); store.Retry(updated); configuration = updated;
            SelectionSummary = "Retry acknowledged; restart required";
        }
    }
    // The recovery prompt's answers: dismiss it (keeping the current renderer plan) or fall back to DX11 at the next launch.
    public static void DismissRecoveryPrompt(bool forgiveUncleanExits)
    {
        lock (Sync)
        {
            if (store == null || !configuration.RecoveryPromptPending) return;
            configuration.RecoveryPromptPending = false;
            if (forgiveUncleanExits) { configuration.UncleanExits = 0; configuration.RendererFaultSeen = false; }
            store.Save(configuration);
        }
    }
    public static void FallBackNextLaunch()
    {
        lock (Sync)
        {
            if (store == null) return;
            configuration.RecoveryPromptPending = false; configuration.RecoveryRequired = true;
            configuration.FailureFingerprint = environmentFingerprint;
            configuration.LastFailure = "DX11 chosen after repeated unclean Vulkan exits.";
            store.Save(configuration);
        }
    }
    public static void BeginVulkanAttempt()
    {
        lock (Sync)
        {
            if (store == null) throw new InvalidOperationException("Configuration not initialized.");
            journal = new AttemptJournal { Generation = configuration.RetryGeneration }; store.WriteAttempt(journal);
            if (!crashHandlerInstalled) { crashHandlerInstalled = true; AppDomain.CurrentDomain.UnhandledException += (_, args) => ReportUnhandled(args.ExceptionObject as Exception); }
        }
    }
    public static void MarkStartupSuccessful()
    {
        lock (Sync)
        {
            if (journal == null || journal.Verified) return;
            // Retain the sentinel until healthy renderer disposal: a later native crash must also recover.
            journal.Verified = true; store?.WriteAttempt(journal);
            SelectionSummary = "Vulkan startup healthy; presented UI frame evidence observed; Creative acceptance remains separate";
        }
    }
    public static void EndSession()
    {
        lock (Sync)
        {
            if (store == null || journal == null || !journal.Verified || journal.Fault == SessionFault.Renderer) return;
            store.MarkStartupSuccessful(); journal = null;
            if (configuration.UncleanExits == 0 && !configuration.RendererFaultSeen) return;
            configuration.UncleanExits = 0; configuration.RendererFaultSeen = false;
            store.Save(configuration);
        }
    }
    public static void AddGuestListener(Action<int> listener)
    {
        if (listener == null) return;
        bool decided;
        int backend;
        lock (Sync)
        {
            guestListeners.Add(listener);
            decided = DecisionMade;
            backend = (int)ActiveBackend;
        }
        if (decided) listener(backend);
    }
    static readonly List<Action> preparationListeners=new();
    public static void AddPreparationListener(Action listener){if(listener!=null)lock(Sync)preparationListeners.Add(listener);}
    public static void RaisePreparation()
    {
        Action[] copy;lock(Sync)copy=preparationListeners.ToArray();
        foreach(var listener in copy){long start=GuestTiming.Start();try{listener();}catch(Exception error){RecordFailure("guest preparation: "+error.Message);}GuestTiming.Stop("preparation",listener,start);}
    }
    public static void AddFrameListener(Action listener)
    {
        if (listener == null) return;
        lock (Sync) frameListeners.Add(listener);
    }
    public static void AddSceneListener(Action<double[],float[]> listener)
    { if(listener!=null)lock(Sync)sceneListeners.Add(listener); }
    public static void RaiseScene(double[] camera,float[] sunToward)
    {
        Action<double[],float[]>[] copy;lock(Sync)copy=sceneListeners.ToArray();
        foreach(var listener in copy)
        {
            long start=GuestTiming.Start();
            try { listener((double[])camera.Clone(),(float[])sunToward.Clone()); }
            catch(Exception error) { RecordFailure("guest scene: "+error.Message); }
            GuestTiming.Stop("scene",listener,start);
        }
    }
    public static void RaiseFrame()
    {
        Action[] copy;
        lock (Sync) copy = frameListeners.ToArray();
        foreach (var listener in copy)
        {
            long start = GuestTiming.Start();
            try { listener(); }
            catch (Exception error) { RecordFailure("guest frame: " + error.Message); }
            GuestTiming.Stop("frame", listener, start);
        }
        GuestTiming.FrameDone();
    }
    public static void SetActive(RendererBackend backend, string reason)
    {
        Action<int>[] copy;
        lock (Sync)
        {
            ActiveBackend = backend; SelectionSummary = reason; DecisionMade = true;
            if (backend == RendererBackend.Dx11 && RequestedBackend == RendererBackend.Vulkan) RecordFailure(reason);
            copy = guestListeners.ToArray();
        }
        foreach (var listener in copy)
        {
            try { listener((int)backend); }
            catch (Exception error) { RecordFailure("guest listener: " + error.Message); }
        }
    }
    public static void FailStartup(string reason)
    {
        lock (Sync)
        {
            // The next launch counts this as a renderer-attributed unclean exit.
            RecordFault(SessionFault.Renderer, reason);
            configuration.LastFailure = reason; store?.Save(configuration); RecordFailure(reason);
        }
    }
    internal static void ReportUnhandled(Exception error)
    {
        try
        {
            if (error == null) return;
            bool ours = IsRendererFault(error);
            lock (Sync) RecordFault(ours ? SessionFault.Renderer : SessionFault.Game, error.GetType().Name + ": " + error.Message);
        }
        catch { }
    }
    // Only frames from this assembly (including its P/Invoke calls into the native renderer and driver) blame Vulkan.
    public static bool IsRendererFault(Exception error)
    {
        var own = typeof(BackendRuntime).Assembly;
        for (var current = error; current != null; current = current.InnerException)
            foreach (var frame in new System.Diagnostics.StackTrace(current, false).GetFrames() ?? Array.Empty<System.Diagnostics.StackFrame>())
                if (frame.GetMethod()?.DeclaringType?.Assembly == own) return true;
        return false;
    }
    private static void RecordFault(SessionFault fault, string detail)
    {
        if (store == null) return;
        journal ??= new AttemptJournal { Generation = configuration.RetryGeneration };
        if (journal.Fault == SessionFault.Renderer) return;
        journal.Fault = fault; journal.Detail = detail ?? "";
        store.WriteAttempt(journal);
    }
    public static void RecordFailure(string reason)
    {
        lock (Sync)
        {
            if (failures.Count > 0 && Array.Exists(failures.ToArray(), s => s.EndsWith(reason, StringComparison.Ordinal))) return;
            while (failures.Count >= 16) failures.Dequeue();
            failures.Enqueue(DateTime.UtcNow.ToString("u") + " " + reason);
        }
    }
}
