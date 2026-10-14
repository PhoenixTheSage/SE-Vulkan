using System;
using System.Collections.Generic;

namespace ClientPlugin.Bootstrap;

// Contains no game types; Preloader.Initialize/Patch can safely update this state.
public static class BackendRuntime
{
    private static readonly object Sync = new object();
    private static BackendConfiguration configuration = new BackendConfiguration();
    private static BootstrapConfigStore store;
    private static bool startupVerified;
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
    public static RendererBackend RequestedBackend { get { lock (Sync) return configuration.RequestedBackend; } }
    public static bool RestartRequired => RequestedBackend != ActiveBackend;
    public static bool RecoveryRequired { get { lock (Sync) return configuration.RecoveryRequired; } }
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
    public static void BeginVulkanAttempt()
    {
        lock (Sync) { if (store == null) throw new InvalidOperationException("Configuration not initialized."); startupVerified = false; store.BeginAttempt(configuration.RetryGeneration); }
    }
    public static void MarkStartupSuccessful()
    {
        lock (Sync)
        {
            if (startupVerified) return;
            // Retain the sentinel until healthy renderer disposal: a later native crash must also recover.
            startupVerified = true;
            SelectionSummary = "Vulkan startup healthy; presented UI frame evidence observed; Creative acceptance remains separate";
        }
    }
    public static void EndSession()
    {
        lock (Sync)
        {
            if (startupVerified && !configuration.RecoveryRequired) store?.MarkStartupSuccessful();
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
    public static void AddFrameListener(Action listener)
    {
        if (listener == null) return;
        lock (Sync) frameListeners.Add(listener);
    }
    public static void RaiseFrame()
    {
        Action[] copy;
        lock (Sync) copy = frameListeners.ToArray();
        foreach (var listener in copy)
        {
            try { listener(); }
            catch (Exception error) { RecordFailure("guest frame: " + error.Message); }
        }
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
            configuration.RecoveryRequired = true; configuration.LastFailure = reason;
            store?.Save(configuration); RecordFailure(reason);
        }
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
