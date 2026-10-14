using System;
using System.Text;
using VRage.Utils;
using VRage.Utils.Keen;
using VRageRender;

namespace ClientPlugin.Diagnostics;

/// <summary>
/// Session file <c>VRageRender-Vulkan_*.log</c> in the game profile, using Keen's
/// <see cref="MyLog"/> line format. <see cref="MyRenderProxy.Log"/> reads
/// <see cref="IMyRender.Log"/>, so font construction and the video-adapter dump
/// land here when this renderer is active.
/// </summary>
public sealed class RenderSessionLog
{
    public const string BaseName = "VRageRender-Vulkan";
    public static RenderSessionLog Current { get; private set; }
    public MyLog Log { get; }
    public string FilePath => Log.GetFilePath();
    public string OpenError { get; private set; }
    private bool closed;

    private RenderSessionLog(MyLog log) { Log = log; }

    public static RenderSessionLog Open()
    {
        var log = new MyLogKeen(alwaysFlush: true);
        string openError = null;
        try
        {
            log.InitWithDateNoCheck(BaseName, new StringBuilder("Version unknown"), 3);
            log.WriteLine("VRage renderer started");
        }
        catch (Exception error) { openError = error.Message; }
        var session = new RenderSessionLog(log) { OpenError = openError };
        Current = session;
        return session;
    }

    public void Write(string message) { try { Log.WriteLine(message); } catch { } }
    public void IncreaseIndent() { try { Log.IncreaseIndent(); } catch { } }
    public void DecreaseIndent() { try { Log.DecreaseIndent(); } catch { } }

    public void RenderSettings(MyRenderSettings settings)
    {
        var user = settings.User;
        Write("MyRenderSettings1 = {");
        IncreaseIndent();
        Write("AntialiasingMode = " + user.AntialiasingMode);
        Write("ShadowQuality = " + user.ShadowQuality);
        Write("ShadowGPUQuality = " + user.ShadowGPUQuality);
        Write("ShadowMemoryQuality = " + user.ShadowMemoryQuality);
        Write("TextureQuality = " + user.TextureQuality);
        Write("VoxelTextureQuality = " + user.VoxelTextureQuality);
        Write("AnisotropicFiltering = " + user.AnisotropicFiltering);
        Write("HqDepth = " + user.HqDepth);
        Write("GrassDrawDistance = " + user.GrassDrawDistance);
        Write("GrassDensityFactor = " + user.GrassDensityFactor);
        Write("AmbientOcclusionEnabled = " + user.AmbientOcclusionEnabled);
        Write("ModelQuality = " + user.ModelQuality);
        Write("VoxelQuality = " + user.VoxelQuality);
        Write("VoxelShaderQuality = " + user.VoxelShaderQuality);
        Write("AlphaMaskedShaderQuality = " + user.AlphaMaskedShaderQuality);
        Write("AtmosphereShaderQuality = " + user.AtmosphereShaderQuality);
        Write("DistanceFade = " + user.DistanceFade);
        Write("ParticleQuality = " + user.ParticleQuality);
        Write("LightsQuality = " + user.LightsQuality);
        DecreaseIndent();
        Write("}");
    }

    /// <summary>
    /// Same block as Keen's device-settings dump. Physical device and display
    /// replace the DXGI adapter and output ids.
    /// </summary>
    public void DeviceSettings(MyRenderDeviceSettings settings)
    {
        Write("MyRenderDeviceSettings = {");
        IncreaseIndent();
        Write("Adapter id = " + settings.AdapterOrdinal);
        if (settings.AdapterOrdinal >= 0)
        {
            Write("PhysicalDevice id = " + settings.AdapterOrdinal);
            Write("Display id = 0");
        }
        else
        {
            Write("PhysicalDevice id = <autodetect>");
            Write("Display id = <autodetect>");
        }
        Write($"Resolution = {settings.BackBufferWidth} x {settings.BackBufferHeight}");
        Write("Window mode = " + settings.WindowMode);
        DecreaseIndent();
        Write("}");
    }

    public void Apply(MyRenderDeviceSettings settings)
    {
        Write("ApplySettings");
        IncreaseIndent();
        DeviceSettings(settings);
        Write($"Screen resolution: {settings.BackBufferWidth}x{settings.BackBufferHeight}");
        DecreaseIndent();
    }

    public void Screen(int width, int height) => Write($"CreateScreenResources: {width}x{height}");

    public void DeviceReady(MyRenderDeviceSettings settings, string device)
    {
        Write("CreateDeviceInternal m_initializedOnce (False)");
        Write("CreateDeviceInternal m_initialized (False)");
        Write("CreateDeviceInternal Apply Settings");
        Apply(settings);
        Screen(settings.BackBufferWidth, settings.BackBufferHeight);
        Write("CreateDeviceInternal done (" + settings + ")");
        Write("CreateDevice: deviceCreated = True");
        if (!string.IsNullOrEmpty(device)) Write(device);
    }

    public void Fail(string heading, Exception error)
    {
        Write(heading);
        IncreaseIndent();
        try { Log.WriteLine(error); } catch { Write(error?.ToString() ?? heading); }
        DecreaseIndent();
    }

    public void UnloadSession() => Write("Unloading session data");

    public void Close()
    {
        if (closed) return;
        closed = true;
        if (Current == this) Current = null;
        try { Log.Close(); } catch { }
    }
}
