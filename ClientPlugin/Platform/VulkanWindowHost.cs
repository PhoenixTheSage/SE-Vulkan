using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Bootstrap;
using HarmonyLib;
using VRage;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Platform;

public sealed class VulkanWindowHost
{
    private const string Sdl = "libSDL3.so";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong SurfaceFactory(IntPtr instance, IntPtr window, IntPtr user);
    private readonly object sdlWindow;
    private readonly MethodInfo sdlInvoke;
    private MyRenderDeviceSettings settings;
    private bool displayModeChanged;
    private string displayDevice;
    private static VulkanWindowHost active;
    private static SubclassProc paintProc;
    private static IntPtr previousPaintProc;
    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static bool IsLinuxPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public bool IsLinux => IsLinuxPlatform;
    public IntPtr WindowHandle { get; }
    public Rectangle DesktopBounds { get; private set; }
    public MyDisplayMode[] SupportedDisplayModes { get; private set; }
    public uint Width => (uint)Math.Max(0, PixelSize.X);
    public uint Height => (uint)Math.Max(0, PixelSize.Y);
    public IntPtr OutputMonitor => IsLinux ? IntPtr.Zero : MonitorFromWindow(WindowHandle, 2);
    public bool CanPresent => WindowHandle != IntPtr.Zero && MyVRage.Platform.Windows.Window.DrawEnabled && Width != 0 && Height != 0;
    public string[] RequiredInstanceExtensions { get; }
    public IntPtr GetInstanceProcAddr { get; }
    public SurfaceFactory CreateSurface { get; }
    public VulkanWindowHost(IntPtr windowHandle, MyRenderDeviceSettings initialSettings)
    {
        WindowHandle = windowHandle; settings = initialSettings;
        if (windowHandle == IntPtr.Zero) throw new ArgumentException("A game window is required.", nameof(windowHandle));
        if (!CheckPlatform(out string reason)) throw new PlatformNotSupportedException(reason);
        if (IsLinux)
        {
            var assembly = PulsarCompatibility.FindLinuxCompatAssembly();
            var thread = assembly.GetType("ClientPlugin.Compatibility.SdlRenderThread", true);
            sdlInvoke = RuntimeMethodContract.FindVoidMethod(thread, "Invoke", BindingFlags.NonPublic | BindingFlags.Static, typeof(Action));
            var provider = assembly.GetType("ClientPlugin.Patches.PlatformGuards.SdlInput2Provider", true);
            sdlWindow = provider.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            if (sdlWindow == null || (IntPtr)Property(sdlWindow, "Handle") != windowHandle)
                throw new InvalidOperationException("LinuxCompat SDL_Window does not match the game window handle.");
            if (!(bool)thread.GetProperty("IsInitialized", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))
                throw new InvalidOperationException("LinuxCompat SDL video thread is not initialized.");
            string[] extensions = null; IntPtr loader = IntPtr.Zero;
            InvokeSdl(() =>
            {
                var names = SDL_Vulkan_GetInstanceExtensions(out uint count);
                if (names == IntPtr.Zero || count == 0) throw new InvalidOperationException("SDL Vulkan instance extensions are unavailable: " + Error());
                extensions = new string[count];
                for (int i = 0; i < count; i++) extensions[i] = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(names, i * IntPtr.Size));
                loader = SDL_Vulkan_GetVkGetInstanceProcAddr();
                if (loader == IntPtr.Zero) throw new InvalidOperationException("SDL Vulkan loader is unavailable: " + Error());
            });
            RequiredInstanceExtensions = extensions; GetInstanceProcAddr = loader; CreateSurface = CreateSdlSurface;
        }
        else { RequiredInstanceExtensions = new[] { "VK_KHR_surface", "VK_KHR_win32_surface" }; InstallPaintGuard(); }
        active = this;
        ApplySettings(initialSettings);
        if (IsLinux) InvokeSdl(RefreshDisplayMetadata); else RefreshDisplayMetadata();
    }
    private Vector2I PixelSize
    {
        get
        {
            if (IsLinux) return (Vector2I)Property(sdlWindow, "ClientSizePixels");
            if (GetClientRect(WindowHandle, out WinRect rectangle)) return new Vector2I(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
            return new Vector2I(settings.BackBufferWidth, settings.BackBufferHeight);
        }
    }
    public void ApplySettings(MyRenderDeviceSettings updated)
    {
        settings = updated; LastDisplayChange = null;
        if (!IsLinux && displayModeChanged && updated.WindowMode != MyWindowModeEnum.Fullscreen) RestoreDisplayMode();
        var window = IsLinux ? sdlWindow : (object)MyVRage.Platform.Windows.Window;
        var method = RuntimeMethodContract.FindWindowModeMethod(window.GetType(), typeof(MyWindowModeEnum), typeof(Rectangle));
        if (method == null) throw new MissingMethodException(window.GetType().FullName, "OnModeChanged");
        Action apply = () =>
        {
            var arguments = new List<object> { updated.WindowMode, updated.BackBufferWidth, updated.BackBufferHeight, DisplayBounds() };
            // LinuxCompat's newer SDL contract accepts Hz; renderer settings use millihertz.
            if (method.GetParameters().Length == 5) arguments.Add(updated.RefreshRate > 0 ? updated.RefreshRate / 1000f : 0f);
            method.Invoke(window, arguments.ToArray());
        };
        if (IsLinux) InvokeSdl(apply); else { apply(); PlaceWindows(updated); }
    }
    public void RestoreDisplayMode()
    {
        if (!displayModeChanged || IsLinux) return;
        ChangeDisplaySettingsEx(displayDevice, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        displayModeChanged = false;
    }
    public string LastDisplayChange { get; private set; }
    public bool EnsureDisplayMode(MyRenderDeviceSettings updated)
    {
        if (IsLinux || updated.WindowMode != MyWindowModeEnum.Fullscreen || !TryChangeDisplayMode(updated)) return false;
        PlaceWindows(updated);
        return true;
    }
    private void PlaceWindows(MyRenderDeviceSettings updated)
    {
        var bounds = DisplayBounds();
        var placement = DisplayPlacement.For((byte)updated.WindowMode, updated.BackBufferWidth, updated.BackBufferHeight, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        if (placement.Width <= 0 || placement.Height <= 0) return;
        if (placement.SizeClient) PlaceClient(placement);
        else if (placement.CoverMonitor) SetWindowPos(WindowHandle, new IntPtr(-1), placement.X, placement.Y, placement.Width, placement.Height, ShowWindow | FrameChanged);
    }
    private void InstallPaintGuard()
    {
        if (paintProc != null) return;
        paintProc = PaintWindow;
        previousPaintProc = SetWindowLongPtr(WindowHandle, -4, Marshal.GetFunctionPointerForDelegate(paintProc));
    }
    private static IntPtr PaintWindow(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        // WinForms fills the client while the window is dragged or loses focus.
        // The swapchain already owns those pixels; leave the last presented frame in place.
        if (message == 0x14) return (IntPtr)1;
        if (message == 0x0F) { ValidateRect(window, IntPtr.Zero); return IntPtr.Zero; }
        return CallWindowProc(previousPaintProc, window, message, wParam, lParam);
    }
    private void PlaceClient(DisplayPlacement placement)
    {
        int style = GetWindowLong(WindowHandle, -16);
        var rect = new WinRect { Right = placement.Width, Bottom = placement.Height };
        AdjustWindowRect(ref rect, style, false);
        SetWindowPos(WindowHandle, new IntPtr(-2), placement.X + rect.Left, placement.Y + rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, ShowWindow | FrameChanged);
    }
    private bool TryChangeDisplayMode(MyRenderDeviceSettings updated)
    {
        if (!TryMonitor(out var info)) { LastDisplayChange = "display device unavailable"; return false; }
        displayDevice = info.DeviceName;
        int hertz = updated.RefreshRate > 0 ? updated.RefreshRate / 1000 : 0;
        ReadMode(displayDevice, -1, out int currentW, out int currentH, out int currentHz);
        if (currentW == updated.BackBufferWidth && currentH == updated.BackBufferHeight && (hertz <= 0 || currentHz == hertz))
        {
            LastDisplayChange = "display already " + currentW + "x" + currentH + "@" + currentHz;
            return true;
        }
        // A hand-built DEVMODE is rejected. Apply a mode the driver enumerated.
        int status = ApplyEnumeratedMode(updated.BackBufferWidth, updated.BackBufferHeight, hertz);
        if (status != 0) status = ApplyEnumeratedMode(updated.BackBufferWidth, updated.BackBufferHeight, 0);
        ReadMode(displayDevice, -1, out int nowW, out int nowH, out int nowHz);
        LastDisplayChange = "display " + currentW + "x" + currentH + " -> " + nowW + "x" + nowH + "@" + nowHz + " (" + status + ")";
        if (status != 0) return false;
        displayModeChanged = true;
        return true;
    }
    private int ApplyEnumeratedMode(int width, int height, int hertz)
    {
        IntPtr buffer = Marshal.AllocHGlobal(220);
        try
        {
            for (int i = 0; ; i++)
            {
                Zero(buffer, 220);
                Marshal.WriteInt16(buffer, 68, 220);
                if (!EnumDisplaySettingsRaw(displayDevice, i, buffer)) return -2;
                if (Marshal.ReadInt32(buffer, 172) != width || Marshal.ReadInt32(buffer, 176) != height) continue;
                if (hertz > 0 && Marshal.ReadInt32(buffer, 184) != hertz) continue;
                return ChangeDisplaySettingsEx(displayDevice, buffer, IntPtr.Zero, FullscreenMode, IntPtr.Zero);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static void ReadMode(string device, int index, out int width, out int height, out int hertz)
    {
        width = height = hertz = 0;
        IntPtr buffer = Marshal.AllocHGlobal(220);
        try
        {
            Zero(buffer, 220);
            Marshal.WriteInt16(buffer, 68, 220);
            if (!EnumDisplaySettingsRaw(device, index, buffer)) return;
            width = Marshal.ReadInt32(buffer, 172); height = Marshal.ReadInt32(buffer, 176); hertz = Marshal.ReadInt32(buffer, 184);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static void Zero(IntPtr buffer, int bytes)
    {
        var empty = new byte[bytes];
        Marshal.Copy(empty, 0, buffer, bytes);
    }
    private bool TryMonitor(out MonitorInfoEx info)
    {
        info = new MonitorInfoEx { Size = Marshal.SizeOf(typeof(MonitorInfoEx)) };
        return GetMonitorInfoEx(MonitorFromWindow(WindowHandle, 2), ref info);
    }
    private Rectangle DisplayBounds()
    {
        if (IsLinux)
        {
            uint display = SDL_GetDisplayForWindow(WindowHandle);
            if (SDL_GetDisplayBounds(display, out SdlRect bounds)) return new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            return new Rectangle(0, 0, settings.BackBufferWidth, settings.BackBufferHeight);
        }
        var monitor = MonitorFromWindow(WindowHandle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
        if (GetMonitorInfo(monitor, ref info)) return new Rectangle(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
        return new Rectangle(0, 0, settings.BackBufferWidth, settings.BackBufferHeight);
    }
    private void RefreshDisplayMetadata()
    {
        DesktopBounds = DisplayBounds();
        var modes = new List<MyDisplayMode>();
        var unique = new HashSet<string>();
        Action<int, int, int> add = (width, height, refresh) =>
        {
            if (width <= 0 || height <= 0) return;
            if (refresh <= 0) refresh = 60000;
            if (unique.Add(width + "x" + height + "@" + refresh)) modes.Add(new MyDisplayMode(width, height, refresh));
        };
        if (IsLinux)
        {
            IntPtr pointers = SDL_GetFullscreenDisplayModes(SDL_GetDisplayForWindow(WindowHandle), out int count);
            if (pointers != IntPtr.Zero)
            {
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        var mode = Marshal.PtrToStructure<SdlDisplayMode>(Marshal.ReadIntPtr(pointers, i * IntPtr.Size));
                        add(mode.Width, mode.Height, (int)Math.Round(mode.RefreshRate * 1000));
                    }
                }
                finally { SDL_free(pointers); }
            }
        }
        else
        {
            if (TryMonitor(out var info))
            {
                var mode = new DeviceMode { Size = 220 };
                for (int i = 0; EnumDisplaySettings(info.DeviceName, i, ref mode); i++) add(mode.Width, mode.Height, checked(mode.RefreshRate * 1000));
            }
        }
        add(DesktopBounds.Width, DesktopBounds.Height, settings.RefreshRate);
        add(settings.BackBufferWidth, settings.BackBufferHeight, settings.RefreshRate);
        SupportedDisplayModes = modes.OrderBy(m => m.Width).ThenBy(m => m.Height).ThenBy(m => m.RefreshRate).ToArray();
    }
    private ulong CreateSdlSurface(IntPtr instance, IntPtr window, IntPtr user)
    {
        try
        {
            ulong surface = 0;
            InvokeSdl(() =>
            {
                if (!SDL_Vulkan_CreateSurface(window, instance, IntPtr.Zero, out surface))
                    throw new InvalidOperationException("SDL Vulkan surface creation failed: " + Error());
            });
            return surface;
        }
        catch (Exception error) { BackendRuntime.RecordFailure("Linux surface callback failed: " + error.Message); return 0; }
    }
    private void InvokeSdl(Action action) => sdlInvoke.Invoke(null, new object[] { action });
    private static object Property(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);
    public static bool CheckPlatform(out string reason)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { reason = "Windows HWND presentation available."; BackendRuntime.PlatformSummary = reason; return true; }
        if (!IsLinuxPlatform) { reason = "Unsupported platform; Vulkan supports Windows and native Linux."; return false; }
        if (typeof(object).Assembly.GetName().Name != "System.Private.CoreLib")
        { reason = "Native Linux requires Pulsar Interim/CoreCLR."; return false; }
        var assembly = PulsarCompatibility.FindLinuxCompatAssembly();
        var thread = assembly?.GetType("ClientPlugin.Compatibility.SdlRenderThread", false);
        var window = assembly?.GetType("ClientPlugin.Compatibility.SdlGameWindow", false);
        var provider = assembly?.GetType("ClientPlugin.Patches.PlatformGuards.SdlInput2Provider", false);
        var forwarder = assembly?.GetType("ClientPlugin.Patches.WindowManagement.ModeChangeForwarder", false);
        bool valid = RuntimeMethodContract.FindVoidMethod(thread, "Invoke", BindingFlags.NonPublic | BindingFlags.Static, typeof(Action)) != null &&
            thread.GetProperty("IsInitialized", BindingFlags.NonPublic | BindingFlags.Static)?.PropertyType == typeof(bool) &&
            window?.GetProperty("Handle", BindingFlags.NonPublic | BindingFlags.Instance)?.PropertyType == typeof(IntPtr) &&
            window.GetProperty("ClientSizePixels", BindingFlags.NonPublic | BindingFlags.Instance)?.PropertyType == typeof(Vector2I) &&
            provider?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.PropertyType == window &&
            RuntimeMethodContract.FindWindowModeMethod(window, typeof(MyWindowModeEnum), typeof(Rectangle)) != null &&
            RuntimeMethodContract.FindVoidMethod(forwarder, "Forward", BindingFlags.Public | BindingFlags.Static, typeof(MyRenderDeviceSettings?)) != null &&
            RuntimeMethodContract.FindVoidMethod(forwarder, "DriveDirect", BindingFlags.Public | BindingFlags.Static, typeof(MyRenderDeviceSettings)) != null;
        reason = valid ? "LinuxCompat SDL3 integration signatures verified." : "LinuxCompat SDL3 integration unavailable or changed; Vulkan Linux startup disabled.";
        BackendRuntime.PlatformSummary = reason; return valid;
    }
    public static void InstallLinuxForwardingGuard(Harmony harmony)
    {
        if (!CheckPlatform(out string reason)) { BackendRuntime.RecordFailure(reason); return; }
        var type = PulsarCompatibility.FindLinuxCompatAssembly().GetType("ClientPlugin.Patches.WindowManagement.ModeChangeForwarder", true);
        var prefix = new HarmonyMethod(typeof(VulkanWindowHost).GetMethod(nameof(GuardModeForwarding), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.First };
        harmony.Patch(RuntimeMethodContract.FindVoidMethod(type, "Forward", BindingFlags.Public | BindingFlags.Static, typeof(MyRenderDeviceSettings?)), prefix);
        harmony.Patch(RuntimeMethodContract.FindVoidMethod(type, "DriveDirect", BindingFlags.Public | BindingFlags.Static, typeof(MyRenderDeviceSettings)), prefix);
    }
    private static bool GuardModeForwarding(object[] __args)
    {
        if (BackendRuntime.ActiveBackend != RendererBackend.Vulkan) return true;
        if (__args.Length > 0 && __args[0] is MyRenderDeviceSettings updated) active?.ApplySettings(updated);
        return false;
    }
    private static string Error() => Marshal.PtrToStringAnsi(SDL_GetError()) ?? "Unknown SDL error";
    [StructLayout(LayoutKind.Sequential)] private struct SdlRect { public int X, Y, Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct SdlDisplayMode { public uint Display, Format; public int Width, Height; public float PixelDensity, RefreshRate; public int RefreshNumerator, RefreshDenominator; public IntPtr Internal; }
    [StructLayout(LayoutKind.Sequential)] private struct WinRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public WinRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfoEx { public int Size; public WinRect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; }
    [StructLayout(LayoutKind.Explicit, Size = 220)] private struct DeviceMode { [FieldOffset(68)] public ushort Size; [FieldOffset(72)] public uint Fields; [FieldOffset(172)] public int Width; [FieldOffset(176)] public int Height; [FieldOffset(184)] public int RefreshRate; }
    private const uint FullscreenMode = 4, ShowWindow = 0x40, FrameChanged = 0x20;
    [DllImport(Sdl, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_Vulkan_GetInstanceExtensions(out uint count);
    [DllImport(Sdl, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_Vulkan_GetVkGetInstanceProcAddr();
    [DllImport(Sdl, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_Vulkan_CreateSurface(IntPtr window, IntPtr instance, IntPtr allocator, out ulong surface);
    [DllImport(Sdl, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_GetError();
    [DllImport(Sdl, CallingConvention = CallingConvention.Cdecl)] private static extern uint SDL_GetDisplayForWindow(IntPtr window);
    [DllImport(Sdl, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_GetFullscreenDisplayModes(uint display, out int count);
    [DllImport(Sdl, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_free(IntPtr memory);
    [DllImport(Sdl, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_GetDisplayBounds(uint display, out SdlRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out WinRect rectangle);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoEx(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettings(string deviceName, int index, ref DeviceMode mode);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettingsRaw(string deviceName, int index, IntPtr mode);
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string deviceName, IntPtr mode, IntPtr window, uint flags, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustWindowRect(ref WinRect rectangle, int style, [MarshalAs(UnmanagedType.Bool)] bool menu);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr procedure);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ValidateRect(IntPtr window, IntPtr rectangle);
}
