using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    readonly ReaderWriterLockSlim reflexLife = new ReaderWriterLockSlim();
    [StructLayout(LayoutKind.Sequential)]
    struct ReflexStatus
    {
        internal uint Size, Version, ExtensionEnabled, ExtensionVersion, SwapchainBound, ConfiguredMode;
        internal int LastResult;
        internal uint Reserved;
        internal ulong Epoch, SleepCount, MarkerCount;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int ReflexStatusFn(IntPtr context, ref ReflexStatus status);
    ReflexStatusFn reflexStatus;
    bool reflexStatusMissing;
    [StructLayout(LayoutKind.Sequential)]
    struct ReflexLatency
    {
        internal uint Size, Version;
        internal ulong Epoch;
        internal uint ReportCount, Reserved;
        internal ulong FrameId, SimStart, SimEnd, RenderStart, RenderEnd, PresentStart, PresentEnd, GpuStart, GpuEnd;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int ReflexLatencyFn(IntPtr context, ulong epoch, ref ReflexLatency latency);
    ReflexLatencyFn reflexLatency;
    bool reflexLatencyMissing;
    readonly object reflexExportsGate = new object();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int ReflexConfigureFn(IntPtr context, ulong epoch, uint mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int ReflexSleepFn(IntPtr context, ulong epoch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int ReflexMarkerFn(IntPtr context, ulong epoch, ulong frameId, uint marker);
    sealed class ReflexControls
    {
        internal readonly ReflexConfigureFn Configure;
        internal readonly ReflexSleepFn Sleep;
        internal readonly ReflexMarkerFn Marker;
        internal ReflexControls(ReflexConfigureFn configure, ReflexSleepFn sleep, ReflexMarkerFn marker)
        { Configure = configure; Sleep = sleep; Marker = marker; }
    }
    ReflexControls reflexControls;
    bool reflexControlsMissing;

    ReflexControls LoadReflexControls()
    {
        lock (reflexExportsGate)
        {
            if (reflexControls != null || reflexControlsMissing) return reflexControls;
            try
            {
                // Publish only a complete immutable table. A missing optional
                // command never disables the separately available diagnostics.
                reflexControls = new ReflexControls(
                    Marshal.GetDelegateForFunctionPointer<ReflexConfigureFn>(GetSymbol("sevk_reflex_configure")),
                    Marshal.GetDelegateForFunctionPointer<ReflexSleepFn>(GetSymbol("sevk_reflex_sleep")),
                    Marshal.GetDelegateForFunctionPointer<ReflexMarkerFn>(GetSymbol("sevk_reflex_marker")));
            }
            catch (EntryPointNotFoundException) { reflexControlsMissing = true; }
            return reflexControls;
        }
    }

    // Preserve VkResult, including stale swapchain epochs. These calls may run
    // on the input and render threads concurrently. The read lease includes the
    // native timeline wait; Dispose's writer cannot destroy its context early.
    int ReflexCommand(int command, ulong epoch, ulong frameId, uint value)
    {
        if (epoch == 0) return -1000001004; // VK_ERROR_OUT_OF_DATE_KHR
        reflexLife.EnterReadLock();
        try
        {
            if (context == IntPtr.Zero) return -4; // VK_ERROR_DEVICE_LOST
            var commands = LoadReflexControls();
            if (commands == null) return -7; // VK_ERROR_EXTENSION_NOT_PRESENT
            switch (command)
            {
                case 0: return commands.Configure(context, epoch, value);
                case 1: return commands.Sleep(context, epoch);
                default: return commands.Marker(context, epoch, frameId, value);
            }
        }
        finally { reflexLife.ExitReadLock(); }
    }

    internal int ReflexConfigure(ulong epoch, int mode)
    {
        if (mode < 0 || mode > 2) throw new ArgumentOutOfRangeException(nameof(mode));
        return ReflexCommand(0, epoch, 0, (uint)mode);
    }
    internal int ReflexSleep(ulong epoch) => ReflexCommand(1, epoch, 0, 0);
    internal object[] ReflexLatencyStatus()
    {
        var binding = ReflexBindingState();
        if (binding == null) return null;
        ulong epoch = (ulong)binding[6];
        reflexLife.EnterReadLock();
        try
        {
            if (context == IntPtr.Zero) return null;
            lock (reflexExportsGate)
            {
                if (reflexLatency == null && !reflexLatencyMissing)
                {
                    try { reflexLatency = Marshal.GetDelegateForFunctionPointer<ReflexLatencyFn>(GetSymbol("sevk_reflex_latency")); }
                    catch (EntryPointNotFoundException) { reflexLatencyMissing = true; }
                }
            }
            if (reflexLatencyMissing) return null;
            // Epoch validation rejects replacement between snapshot and query;
            // the context read lease protects the complete synchronous query.
            var state = new ReflexLatency { Size = 96, Version = AbiVersion };
            int result = reflexLatency(context, epoch, ref state);
            if (state.Size != 96 || state.Version != AbiVersion) throw new NotSupportedException("Vulkan Reflex timing ABI mismatch");
            return new object[] { 1, epoch, result, (int)state.ReportCount, state.FrameId, state.SimStart, state.SimEnd,
                state.RenderStart, state.RenderEnd, state.PresentStart, state.PresentEnd, state.GpuStart, state.GpuEnd };
        }
        finally { reflexLife.ExitReadLock(); }
    }
    internal int ReflexMarker(ulong epoch, ulong frameId, uint marker)
    {
        if (frameId == 0) throw new ArgumentOutOfRangeException(nameof(frameId));
        if (marker > 11) throw new ArgumentOutOfRangeException(nameof(marker));
        return ReflexCommand(2, epoch, frameId, marker);
    }

    // Device-free snapshot for Anomaly's core. This does not apply requested
    // modes or equate native binding readiness with complete Reflex integration.
    internal object[] ReflexBindingState()
    {
        reflexLife.EnterReadLock();
        try
        {
            if (context == IntPtr.Zero || reflexStatusMissing) return null;
            lock (reflexExportsGate)
            {
                if (reflexStatus == null)
                {
                    try { reflexStatus = Marshal.GetDelegateForFunctionPointer<ReflexStatusFn>(GetSymbol("sevk_reflex_status")); }
                    catch (EntryPointNotFoundException) { reflexStatusMissing = true; return null; }
                }
            }
            var state = new ReflexStatus { Size = 56, Version = AbiVersion };
            int result = reflexStatus(context, ref state);
            if (result != 0) throw new InvalidOperationException("Vulkan Reflex diagnostic result " + result);
            if (state.Size != 56 || state.Version != AbiVersion) throw new NotSupportedException("Vulkan Reflex diagnostic ABI mismatch");
            return new object[] { 1, state.ExtensionEnabled != 0, state.ExtensionVersion, state.SwapchainBound != 0,
                unchecked((int)state.ConfiguredMode), state.LastResult, state.Epoch, state.SleepCount, state.MarkerCount };
        }
        finally { reflexLife.ExitReadLock(); }
    }
}
