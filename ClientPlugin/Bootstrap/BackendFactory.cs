using System;
using System.Runtime.CompilerServices;
using ClientPlugin.Platform;
using ClientPlugin.Rendering;
using VRage.FileSystem;
using VRageRender;

namespace ClientPlugin.Bootstrap;

public static class BackendFactory
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IMyRender Create(MyRenderSettings? settings)
    {
        string reason = "DX11 selected.";
        IMyRender candidate = null;
        try
        {
            BackendRuntime.InitializeConfiguration(MyFileSystem.UserDataPath);
            EarlyBootstrapHooks.Install();
            if (BackendRuntime.RequestedBackend == RendererBackend.Vulkan)
            {
                BackendRuntime.ApplyEnvironmentFingerprint(RecoveryFingerprint.Compute());
                if (!BackendRuntime.HookReady || !BackendRuntime.LazyCacheDisabled) reason = "Early renderer hook or preloader cache guard is unavailable.";
                else if (BackendRuntime.RecoveryRequired) reason = BackendRuntime.Configuration.LastFailure;
                else if (PulsarCompatibility.Check(out reason) && VulkanWindowHost.CheckPlatform(out reason) &&
                    MyVulkanRender.TryCreate(settings.GetValueOrDefault(), out candidate, out reason))
                {
                    BackendRuntime.BeginVulkanAttempt();
                    BackendRuntime.SetActive(RendererBackend.Vulkan, "Vulkan selected; startup validation pending.");
                    return candidate;
                }
            }
        }
        catch (Exception error)
        {
            reason = "Vulkan pre-activation failed: " + error.Message;
            BackendRuntime.RecordFailure(reason);
            try { candidate?.DisposeDevice(); } catch (Exception disposeError) { BackendRuntime.RecordFailure("Pre-activation cleanup failed: " + disposeError.Message); }
        }
        // Keep vanilla DX11 creation outside the Vulkan exception handler; its own failure must propagate once.
        return Dx11(settings, reason);
    }
    private static IMyRender Dx11(MyRenderSettings? settings, string reason)
    {
        BackendRuntime.SetActive(RendererBackend.Dx11, reason);
        return new MyDX11Render(settings);
    }
}
