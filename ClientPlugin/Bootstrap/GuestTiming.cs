using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ClientPlugin.Bootstrap;

// Per-listener cost of the guest callbacks (frame, preparation, scene), for CpuDetail runs only
// (SE_VULKAN_CPU_DETAIL=1 with a harness data directory): wall time and managed allocation per frame,
// written to Reports/guest-listeners.json every 600 frames. Contains no game types.
internal static class GuestTiming
{
    static readonly string report = Init();
    static readonly Dictionary<string, long[]> totals = new();
    static int frames;
    static string Init()
    {
        if (Environment.GetEnvironmentVariable("SE_VULKAN_CPU_DETAIL") != "1") return null;
        var data = Environment.GetEnvironmentVariable("SE_VULKAN_PERF_DATA");
        return string.IsNullOrEmpty(data) ? null : Path.Combine(data, "Reports", "guest-listeners.json");
    }
    static long Allocated()
    {
#if NETCOREAPP
        return GC.GetAllocatedBytesForCurrentThread();
#else
        return 0;
#endif
    }
    [ThreadStatic] static long startBytes;
    public static long Start() { if (report == null) return 0; startBytes = Allocated(); return Stopwatch.GetTimestamp(); }
    public static void Stop(string kind, Delegate listener, long start)
    {
        if (report == null) return;
        long ticks = Stopwatch.GetTimestamp() - start, bytes = Allocated() - startBytes;
        var method = listener.Method;
        string key = kind + ": " + (method.DeclaringType?.Assembly.GetName().Name ?? "?") + " " + method.DeclaringType?.FullName + "." + method.Name;
        if (!totals.TryGetValue(key, out var row)) totals[key] = row = new long[3];
        row[0] += ticks; row[1]++; row[2] += bytes;
    }
    public static void FrameDone()
    {
        if (report == null || ++frames % 600 != 0) return;
        var text = new StringBuilder("{\"Frames\":").Append(frames).Append(",\"Rows\":[");
        bool first = true;
        foreach (var pair in totals)
        {
            if (!first) text.Append(',');
            first = false;
            double ms = pair.Value[0] * 1000.0 / Stopwatch.Frequency / frames;
            text.Append("{\"Listener\":\"").Append(pair.Key.Replace("\\", "/").Replace("\"", "'")).Append("\",\"MsPerFrame\":").Append(ms.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append(",\"KbPerFrame\":").Append((pair.Value[2] / 1024.0 / frames).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(",\"Calls\":").Append(pair.Value[1]).Append('}');
        }
        text.Append("]}");
        try { Directory.CreateDirectory(Path.GetDirectoryName(report)); File.WriteAllText(report, text.ToString()); } catch { }
    }
}
