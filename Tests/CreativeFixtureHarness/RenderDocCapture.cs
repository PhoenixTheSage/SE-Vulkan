using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CreativeFixtureHarness;

// Test-only binding to the installed renderdoc_app.h API 1.4 function table.
// The launcher enables its Vulkan layer only in the isolated child process.
internal static class RenderDocCapture
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetApi(int version, out IntPtr api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetPath([MarshalAs(UnmanagedType.LPStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Trigger();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Count();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Overlay(uint andMask, uint orMask);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    private static Trigger trigger;
    private static Count count;
    private static T Entry<T>(IntPtr table, int index) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, index * IntPtr.Size));

    public static void Initialize(string reports)
    {
        if (Environment.GetEnvironmentVariable("SE_VULKAN_CREATIVE_RENDERDOC") != "1") return;
        IntPtr module = GetModuleHandle("renderdoc.dll");
        IntPtr address = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "RENDERDOC_GetAPI");
        if (address == IntPtr.Zero || Marshal.GetDelegateForFunctionPointer<GetApi>(address)(10400, out IntPtr table) != 1)
            throw new InvalidOperationException("Requested isolated RenderDoc Vulkan layer/API 1.4 is unavailable.");
        Entry<SetPath>(table, 11)(Path.Combine(reports, "fixture-native"));
        Entry<Overlay>(table, 8)(0, 0);
        trigger = Entry<Trigger>(table, 15);
        count = Entry<Count>(table, 13);
    }

    public static void Request(string name)
    {
        if (trigger != null && (name == "fixture-overview" || name == "fixture-lcd-sprites" || name == "transparency-glass-particles" || name == "transparency-rain")) trigger();
    }
    public static void VerifyDelivery()
    {
        if (count != null && count() < 2) throw new InvalidOperationException("The isolated RenderDoc run did not deliver both world and LCD frame captures.");
    }
}
