using System;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender;
namespace ClientPlugin.Rendering.Scene;
public sealed class VulkanResolution
{
    private readonly VulkanDrsHeuristic heuristic=new();
    private MyDRSSettings previousSettings;
    private ulong previousSample;
    private bool wasEnabled;
    public float Fraction{get;private set;}=1;
    public Vector2I Update(NativeBackend backend,MyRenderDeviceSettings device,MyRenderSettings render,bool world,float cpuMs)
    {
        var stats=backend.GetResolutionStats();int preset=device.BackBufferHeight==2160?2:device.BackBufferHeight==1440?1:0;
        var settings=device.DRSSettingsPresets;var selected=settings!=null && preset<settings.Length?settings[preset]:null;
        bool enabled=world && render.User.DRScaling && selected!=null && stats.TimingSupported!=0;
        if(!ReferenceEquals(previousSettings,selected) || wasEnabled!=enabled){heuristic.Reset_RenderThread();previousSettings=selected;previousSample=stats.Samples;}
        wasEnabled=enabled;Fraction=1;
        if(enabled)
        {
            heuristic.Settings=selected;
            if(stats.Samples!=previousSample && stats.GpuMs>0 && !float.IsNaN(stats.GpuMs) && !float.IsInfinity(stats.GpuMs))
            {
                // GPU work is sampled from the completed slot, never from CPU frame rate.
                ulong history=heuristic.CreateNewPreviousFrameTimings(Math.Max(0,cpuMs),0);
                heuristic.CommitPreviousFrameGPUTimings(history,stats.GpuMs,stats.GpuMs,false);heuristic.RefreshCurentFrameResolutionFraction();previousSample=stats.Samples;
            }
            Fraction=MathHelper.Clamp(heuristic.QueryCurentFrameResolutionFraction(),.25f,1);
        }
        backend.SetRenderFraction(Fraction);
        // NGX/FSR can override DRS. Resolve before frame, light-tile and AO constants
        // are built, rather than discovering the smaller target at EndFrame.
        var size=backend.ResolveRenderResolution(device.BackBufferWidth,device.BackBufferHeight);
        Fraction=backend.RenderFraction;
        return size;
    }
}
