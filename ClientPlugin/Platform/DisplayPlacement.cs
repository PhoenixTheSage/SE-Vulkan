using System;

namespace ClientPlugin.Platform;

// Exclusive fullscreen has to cover the selected mode. Borderless covers the
// monitor, and the HUD follows that client instead of a smaller requested mode.
public readonly struct DisplayPlacement
{
    public readonly bool Exclusive;
    public readonly bool CoverMonitor;
    public readonly int X;
    public readonly int Y;
    public readonly int Width;
    public readonly int Height;
    public readonly bool SizeClient;

    public DisplayPlacement(bool exclusive, bool coverMonitor, int x, int y, int width, int height, bool sizeClient)
    {
        Exclusive = exclusive; CoverMonitor = coverMonitor; X = x; Y = y; Width = width; Height = height; SizeClient = sizeClient;
    }

    // mode matches MyWindowModeEnum: 0 window, 1 borderless, 2 fullscreen.
    // Both regular and exclusive fullscreen cover the requested mode.
    public static DisplayPlacement For(byte mode, int requestedWidth, int requestedHeight, int monitorX, int monitorY, int monitorWidth, int monitorHeight)
    {
        int requestW = Math.Max(0, requestedWidth), requestH = Math.Max(0, requestedHeight);
        if (mode == 2)
            return new DisplayPlacement(true, true, monitorX, monitorY, requestW, requestH, false);
        if (mode == 1)
        {
            bool monitor = monitorWidth > 0 && monitorHeight > 0;
            return new DisplayPlacement(false, true, monitorX, monitorY, monitor ? monitorWidth : requestW, monitor ? monitorHeight : requestH, false);
        }
        int x = monitorWidth > 0 ? monitorX + Math.Max(0, (monitorWidth - requestW) / 2) : monitorX;
        int y = monitorHeight > 0 ? monitorY + Math.Max(0, (monitorHeight - requestH) / 2) : monitorY;
        return new DisplayPlacement(false, false, x, y, requestW, requestH, true);
    }

    public static DisplayPlacement PresentationSize(int clientWidth, int clientHeight, int requestedWidth, int requestedHeight)
    {
        if (clientWidth > 0 && clientHeight > 0) return new DisplayPlacement(false, false, 0, 0, clientWidth, clientHeight, false);
        if (requestedWidth > 0 && requestedHeight > 0) return new DisplayPlacement(false, false, 0, 0, requestedWidth, requestedHeight, false);
        return new DisplayPlacement(false, false, 0, 0, 1, 1, false);
    }
}

// Dropdown key 3 is Vulkan-only. The game stores Fullscreen (2) so DX11 still understands the config.
public static class WindowModeChoice
{
    public const long ExclusiveKey = 3;
    public static bool Offer(bool vulkanActive, bool linux, bool fullscreenListed) => vulkanActive && !linux && fullscreenListed;
    public static byte StoredMode(long key)
    {
        if (key == ExclusiveKey) return 2;
        if (key < 0 || key > 2) return 0;
        return (byte)key;
    }
    public static bool WantsExclusive(long key) => key == ExclusiveKey;
    public static long DropdownKey(byte storedMode, bool exclusive, bool offer) => offer && exclusive && storedMode == 2 ? ExclusiveKey : storedMode;
}
