using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ClientPlugin.Diagnostics;

// Thread placement (Plan 2 D1; default under the split since Plan 3 batch 121).
// SE_VULKAN_BUILD_CPU on the frame build thread, like SE_VULKAN_STAGE_CPU on the native recording stage:
// "<cpu>" sets the ideal processor, "<cpu>!" pins the thread to that logical CPU (processor group 0).
// Unset, the thread's default slot applies (DefaultCpu). The first placement, or SE_VULKAN_TOPOLOGY_LOG=1, logs which
// logical CPUs share a core (SMT) and an L3.
internal static class ThreadPlacement
{
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern uint SetThreadIdealProcessor(IntPtr thread, uint processor);
    [DllImport("kernel32.dll")] static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
    static bool logged;
    static int[] coreFirsts;

    // Applies the placement named by the variable to the calling thread; unset, the default for its slot (DefaultCpu).
    public static void PlaceCurrent(string variable, int slot = 0)
    {
        string value = Environment.GetEnvironmentVariable(variable);
        if (Environment.GetEnvironmentVariable("SE_VULKAN_TOPOLOGY_LOG") == "1" || !string.IsNullOrEmpty(value)) LogTopology();
        if (string.IsNullOrEmpty(value))
        {
            int fallback = DefaultCpu(slot);
            if (fallback >= 0) { SetThreadIdealProcessor(GetCurrentThread(), (uint)fallback); Log("Default placement: ideal CPU " + fallback + " for " + System.Threading.Thread.CurrentThread.Name + "."); }
            return;
        }
        bool pin = value.EndsWith("!", StringComparison.Ordinal);
        if (!uint.TryParse(pin ? value.Substring(0, value.Length - 1) : value, out uint cpu) || cpu >= 64) return;
        if (pin) SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << (int)cpu));
        else SetThreadIdealProcessor(GetCurrentThread(), cpu);
        Log(variable + "=" + value + " applied to " + System.Threading.Thread.CurrentThread.Name + ".");
    }
    // The same rule as native default_thread_cpu (renderer.cpp): under SE_VULKAN_FRAME_SPLIT=1, slot k (1 build, 2 launch,
    // 3 recording stage, 4 present worker) takes the first logical CPU of physical core k, skipping core 0, as its ideal
    // processor. Only with six or more physical cores; SE_VULKAN_THREAD_PLACEMENT=0 turns it off. -1 means no default.
    public static int DefaultCpu(int slot)
    {
        if (slot <= 0 || Environment.GetEnvironmentVariable("SE_VULKAN_FRAME_SPLIT") != "1" || Environment.GetEnvironmentVariable("SE_VULKAN_THREAD_PLACEMENT") == "0") return -1;
        if (coreFirsts == null)
        {
            var firsts = new List<int>();
            try { foreach (var (kind, mask, _) in Relations()) if (kind == "core" && mask != 0) { int first = 0; while ((mask >> first & 1) == 0) first++; firsts.Add(first); } }
            catch { firsts.Clear(); }
            firsts.Sort();
            coreFirsts = firsts.Count >= 6 ? firsts.ToArray() : Array.Empty<int>();
        }
        return slot < coreFirsts.Length ? coreFirsts[slot] : -1;
    }
    static void LogTopology()
    {
        if (logged) return; logged = true;
        try
        {
            var text = new StringBuilder("Vulkan CPU topology (group 0 masks):");
            foreach (var (kind, mask, detail) in Relations()) text.Append("\n  ").Append(kind).Append(' ').Append(mask.ToString("X")).Append(detail);
            Log(text.ToString());
        }
        catch (Exception error) { Log("Vulkan CPU topology unavailable: " + error.Message); }
    }
    // RelationProcessorCore (0) and RelationCache (2, level 3 only) records of GetLogicalProcessorInformationEx.
    static IEnumerable<(string Kind, ulong Mask, string Detail)> Relations()
    {
        var result = new List<(string, ulong, string)>();
        uint length = 0; GetLogicalProcessorInformationEx(0xFFFF, IntPtr.Zero, ref length);
        IntPtr buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(0xFFFF, buffer, ref length)) throw new InvalidOperationException("GetLogicalProcessorInformationEx failed: " + Marshal.GetLastWin32Error());
            for (long offset = 0; offset < length;)
            {
                IntPtr record = buffer + (int)offset;
                int relationship = Marshal.ReadInt32(record), size = Marshal.ReadInt32(record, 4);
                if (relationship == 0)
                {
                    // PROCESSOR_RELATIONSHIP: Flags, EfficiencyClass, Reserved[20], GroupCount, GroupMask[] (KAFFINITY, WORD group).
                    byte flags = Marshal.ReadByte(record, 8), efficiency = Marshal.ReadByte(record, 9);
                    ulong mask = (ulong)Marshal.ReadInt64(record, 8 + 24);
                    result.Add(("core", mask, (flags & 1) != 0 ? " smt efficiency=" + efficiency : " efficiency=" + efficiency));
                }
                else if (relationship == 2)
                {
                    // CACHE_RELATIONSHIP: Level, Associativity, LineSize(WORD), CacheSize(DWORD), Type, Reserved[18], GroupCount(WORD), GroupMask.
                    byte level = Marshal.ReadByte(record, 8); int bytes = Marshal.ReadInt32(record, 12);
                    if (level == 3) result.Add(("L3", (ulong)Marshal.ReadInt64(record, 8 + 32), " " + (bytes >> 20) + " MB"));
                }
                offset += size;
                if (size <= 0) break;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }
    static void Log(string text) { try { VRage.Utils.MyLog.Default?.WriteLine(text); } catch { } }
}
