using System;
using System.Collections.Generic;
using System.Text;
using ClientPlugin.Bootstrap;
using ClientPlugin.Rendering.Interop;
using VRage.Utils;
using VRageRender.Messages;

namespace ClientPlugin.Diagnostics;

public enum SubsystemState { NotImplemented, Unavailable, Ready, Active, Verified, Failed, Disabled }
public enum MessageDisposition { Handler, OutboundResponse, BaselineNoOp, Pending }

/// <summary>CPU-only evidence. A submitted frame does not prove visual parity.</summary>
public static class VulkanDiagnostics
{
    private sealed class Entry { public SubsystemState State; public string Detail; public ulong Frame; }
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> systems = new(StringComparer.Ordinal);
    private static readonly long[] messages = new long[(int)MyRenderMessageEnum.MaxMessageIndex];
    private static readonly MessageDisposition[] dispositions = new MessageDisposition[messages.Length];
    private static readonly Queue<string> failures = new();
    private static NativeStats native;
    private static NativeProbe probe;
    private static int actors, instances, models, fonts;
    private static string cached = "Vulkan status has not been sampled.";
    private static DateTime lastSnapshot;
    static VulkanDiagnostics()
    {
        for (int i = 0; i < dispositions.Length; i++) dispositions[i] = i < MessageCatalog.Dispositions.Length ? MessageCatalog.Dispositions[i] : MessageDisposition.Pending;
        foreach (string name in new[] { "Bootstrap", "Presentation", "Resources", "Shaders", "Sprites and clipping", "Fonts", "Models and materials",
            "Actors and instancing", "Character skinning", "RenderDirect animations", "Skybox", "Sun and ambient lighting", "Local lighting", "Cascaded shadows",
            "Fixed exposure tone mapping", "Construction dithering", "Billboards", "LCD targets", "Screenshot readback", "Voxels", "Planets and atmosphere",
            "Clouds, fog, foliage and weather", "Glass, decals and OIT", "GPU particles", "Environment probes", "Occlusion and SSAO/HBAO",
            "Exposure, bloom and highlights", "Postprocessing and upscale", "Stereo", "Native media", "Advanced capture", "Linux live acceptance", "Creative fixture acceptance" })
            systems.Add(name, new Entry { State = SubsystemState.NotImplemented, Detail = "Pending implementation or acceptance evidence." });
    }
    public static void Set(string name, SubsystemState state, string detail, ulong frame = 0)
    { lock (Sync) systems[name] = new Entry { State = state, Detail = detail, Frame = frame }; }
    public static void Probe(NativeProbe value) { lock (Sync) probe = value; }
    public static void NativeFrame(NativeStats value, int actorCount, int instanceCount, int modelCount, int fontCount)
    { lock (Sync) { native = value; actors = actorCount; instances = instanceCount; models = modelCount; fonts = fontCount; } }
    public static void Message(MyRenderMessageEnum type, MessageDisposition disposition)
    {
        int index = (int)type;
        // Called for every render message; readers copy under Sync and tolerate a torn pair.
        if (index >= 0 && index < messages.Length) { System.Threading.Interlocked.Increment(ref messages[index]); dispositions[index] = disposition; }
    }
    public static void Failure(string reason)
    {
        BackendRuntime.RecordFailure(reason);
        RenderSessionLog.Current?.Write(reason);
        lock (Sync) { while (failures.Count >= 16) failures.Dequeue(); failures.Enqueue(DateTime.UtcNow.ToString("u") + " " + reason); }
    }
    public static void Refresh()
    {
        lock (Sync)
        {
            if ((DateTime.UtcNow - lastSnapshot).TotalSeconds < 1) return;
            lastSnapshot = DateTime.UtcNow;
            var text = new StringBuilder(8192);
            text.AppendLine("Vulkan 1.4 renderer status — " + lastSnapshot.ToString("u"));
            text.AppendLine("Requested: " + BackendRuntime.RequestedBackend + "; active: " + BackendRuntime.ActiveBackend + "; restart: " + BackendRuntime.RestartRequired + "; recovery: " + BackendRuntime.RecoveryRequired);
            text.AppendLine(BackendRuntime.SelectionSummary);
            text.AppendLine("Early hook: " + BackendRuntime.HookReady + "; lazy cache disabled: " + BackendRuntime.LazyCacheDisabled);
            text.AppendLine("IL fingerprint: " + BackendRuntime.HookFingerprint);
            text.AppendLine("Runtime: " + Environment.Version + "; process: " + (IntPtr.Size * 8) + " bit; ABI: " + NativeBackend.AbiVersion);
            text.AppendLine("Platform: " + BackendRuntime.PlatformSummary);
            text.AppendLine("Render log: " + (RenderSessionLog.Current?.FilePath ?? "not open"));
            text.AppendLine("Native asset: " + (NativeBackend.LibraryPath ?? "not loaded"));
            text.AppendLine("GPU: " + (native.DeviceName ?? probe.DeviceName ?? "not queried"));
            text.AppendLine("Loader API: " + Version(probe.LoaderApi) + "; selected device: " + Version(native.ApiVersion != 0 ? native.ApiVersion : probe.DeviceApi));
            text.AppendLine("Required feature bits: 0x" + (probe.Features & 15423).ToString("X") + "; validation available: " + probe.ValidationAvailable + "; enabled: " + native.ValidationEnabled + "; errors: " + native.ValidationErrors);
            text.AppendLine("Optional support (queried, unused): host image copy=" + ((probe.Features & 64) != 0)
                + "; attachment local read=" + ((probe.Features & 128) != 0) + "; depth/stencil local read=" + ((probe.Features & 256) != 0)
                + "; multisampled local read=" + ((probe.Features & 512) != 0));
            text.AppendLine(probe.Detail ?? "GPU preflight not run.");
            text.AppendLine("Presentation: " + native.Width + "x" + native.Height + "; ready: " + native.SurfaceReady + "; device lost: " + native.DeviceLost);
            text.AppendLine("Frames submitted/presented: " + native.SubmittedFrames + "/" + native.PresentedFrames + "; mesh/sprite draws: " + native.MeshDraws + "/" + native.SpriteDraws + "; completed timeline: " + native.CompletedTimeline);
            text.AppendLine("Resources: textures=" + native.Textures + ", meshes=" + native.Meshes + ", uploaded bytes=" + native.UploadedBytes);
            text.AppendLine("Scene: actors=" + actors + ", instances=" + instances + ", models=" + models + ", fonts=" + fonts);
            text.AppendLine("Compatibility: " + BackendRuntime.CompatibilitySummary);
            text.AppendLine();
            foreach (var pair in systems)
                text.AppendLine(pair.Key + ": " + StateName(pair.Value.State) + " [observed frame " + pair.Value.Frame + "] — " + pair.Value.Detail);
            text.AppendLine();
            text.AppendLine("Render message coverage (" + messages.Length + " kinds; observation is separate from visual verification):");
            for (int i = 0; i < messages.Length; i++) text.AppendLine(((MyRenderMessageEnum)i) + ": " + dispositions[i] + "; observed=" + messages[i]);
            text.AppendLine(); text.AppendLine("Recent bounded failures:");
            foreach (string reason in BackendRuntime.RecentFailures) text.AppendLine(reason);
            cached = text.ToString();
        }
    }
    public static string Report() { Refresh(); lock (Sync) return cached; }
    internal sealed class ScreenRow
    {
        internal string Text, Detail;
        internal SubsystemState? State;
        internal bool Heading;
    }
    internal sealed class SystemRow
    {
        internal string Name, Detail;
        internal SubsystemState State;
        internal ulong Frame;
    }
    internal sealed class ScreenSnapshot
    {
        internal readonly List<ScreenRow> Summary = new();
        internal readonly List<SystemRow> Systems = new();
    }
    internal static ScreenSnapshot CaptureScreen()
    {
        Refresh();
        lock (Sync)
        {
            var result = new ScreenSnapshot();
            void Heading(string text) => result.Summary.Add(new ScreenRow { Text = text, Heading = true });
            void Row(string text, string detail = null, SubsystemState? state = null) => result.Summary.Add(new ScreenRow { Text = text, Detail = detail ?? text, State = state });
            string Yes(bool value) => value ? "Yes" : "No";
            string Count(ulong value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            Heading("BACKEND & STARTUP");
            Row("Active: " + BackendRuntime.ActiveBackend + "   |   Requested: " + BackendRuntime.RequestedBackend);
            Row("Restart required: " + Yes(BackendRuntime.RestartRequired) + "   |   Recovery: " + Yes(BackendRuntime.RecoveryRequired));
            Row(BackendRuntime.SelectionSummary, state: BackendRuntime.RequestedBackend == RendererBackend.Vulkan && BackendRuntime.ActiveBackend != RendererBackend.Vulkan ? SubsystemState.Unavailable : (SubsystemState?)null);
            Row("Early hook: " + Yes(BackendRuntime.HookReady) + "   |   Lazy cache disabled: " + Yes(BackendRuntime.LazyCacheDisabled));
            Row("Runtime: " + Environment.Version + " / " + (IntPtr.Size * 8) + " bit   |   ABI: " + NativeBackend.AbiVersion);
            Row("Platform: " + BackendRuntime.PlatformSummary);
            string renderLog = RenderSessionLog.Current?.FilePath;
            Row("Render log: " + (renderLog == null ? "not open" : System.IO.Path.GetFileName(renderLog)), renderLog);
            string path = NativeBackend.LibraryPath;
            Row("Native: " + (path == null ? "not loaded" : System.IO.Path.GetFileName(path)), path);
            string fingerprint = BackendRuntime.HookFingerprint ?? "unavailable";
            Row("Hook fingerprint: " + (fingerprint.Length > 16 ? fingerprint.Substring(0, 16) + "…" : fingerprint), fingerprint);
            Row("Compatibility: " + BackendRuntime.CompatibilitySummary);
            Heading("GPU & VALIDATION");
            Row("GPU: " + (native.DeviceName ?? probe.DeviceName ?? "not queried"));
            Row("Loader: " + Version(probe.LoaderApi) + "   |   Device: " + Version(native.ApiVersion != 0 ? native.ApiVersion : probe.DeviceApi));
            Row("Required features: 0x" + (probe.Features & 15423).ToString("X"));
            Row("Validation: available " + probe.ValidationAvailable + " / enabled " + native.ValidationEnabled + " / errors " + Count(native.ValidationErrors), state: native.ValidationErrors != 0 ? SubsystemState.Failed : (SubsystemState?)null);
            Row("Optional: image copy " + Yes((probe.Features & 64) != 0) + " / local read " + Yes((probe.Features & 128) != 0), "Optional features are queried but unused. Host image copy: " + Yes((probe.Features & 64) != 0) + "; attachment local read: " + Yes((probe.Features & 128) != 0) + "; depth/stencil local read: " + Yes((probe.Features & 256) != 0) + "; multisampled local read: " + Yes((probe.Features & 512) != 0));
            Row("Local read: depth/stencil " + Yes((probe.Features & 256) != 0) + " / MSAA " + Yes((probe.Features & 512) != 0));
            Row(probe.Detail ?? "GPU preflight has not run.");
            Heading("FRAME ACTIVITY & RESOURCES");
            Row("Presentation: " + native.Width + " × " + native.Height + "   |   Ready: " + native.SurfaceReady + " / lost: " + native.DeviceLost);
            Row("Frames: submitted " + Count(native.SubmittedFrames) + " / presented " + Count(native.PresentedFrames));
            Row("Draws: mesh " + Count(native.MeshDraws) + " / sprite " + Count(native.SpriteDraws));
            Row("Completed timeline: " + Count(native.CompletedTimeline));
            Row("Resources: " + Count(native.Textures) + " textures / " + Count(native.Meshes) + " meshes");
            Row("Uploaded: " + (native.UploadedBytes / (1024d * 1024d)).ToString("N1", System.Globalization.CultureInfo.InvariantCulture) + " MiB");
            Row("Scene: " + actors + " actors / " + instances + " instances / " + models + " models / " + fonts + " fonts");
            Heading("RENDER MESSAGE COVERAGE");
            var totals = new int[4]; int observed = 0; long totalMessages = 0;
            for (int i = 0; i < messages.Length; i++) { totals[(int)dispositions[i]]++; if (messages[i] != 0) observed++; totalMessages += messages[i]; }
            Row(messages.Length + " kinds   |   Observed: " + observed + "   |   Messages: " + totalMessages.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
            Row("Handlers: " + totals[(int)MessageDisposition.Handler] + "   |   Pending: " + totals[(int)MessageDisposition.Pending]);
            Row("Outbound: " + totals[(int)MessageDisposition.OutboundResponse] + "   |   Baseline no-ops: " + totals[(int)MessageDisposition.BaselineNoOp]);
            var recent = BackendRuntime.RecentFailures;
            Heading("RECENT FAILURES (" + recent.Length + ")");
            if (recent.Length == 0) Row("None recorded.");
            else
            {
                int start = Math.Max(0, recent.Length - 3);
                for (int i = start; i < recent.Length; i++) Row(recent[i], state: SubsystemState.Failed);
                if (start > 0) Row("Earlier failures: " + start + " — available in the full log report.", string.Join("\n", recent));
            }
            foreach (var pair in systems) result.Systems.Add(new SystemRow { Name = pair.Key, State = pair.Value.State, Frame = pair.Value.Frame, Detail = pair.Value.Detail });
            return result;
        }
    }
    public static void WriteReportToLog() { MyLog.Default?.WriteLineAndConsole(Report()); }
    public static string Version(uint version) => version == 0 ? "unavailable" : ((version >> 22) & 127) + "." + ((version >> 12) & 1023) + "." + (version & 4095);
    internal static string StateName(SubsystemState state) => state == SubsystemState.NotImplemented ? "Not implemented" : state.ToString();
}
