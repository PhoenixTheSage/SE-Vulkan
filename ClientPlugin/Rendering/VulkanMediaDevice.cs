using System;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering;

internal interface IVulkanMediaDevice : IDisposable
{
    bool Available { get; }
    ulong ReserveTexture();
    bool Play(uint id, string path, float volume);
    NativeMediaFrame Update(uint id);
    void Volume(uint id, float volume);
    void Close(uint id);
    void Upload(ulong texture, NativeMediaFrame frame);
    void Remove(ulong texture);
    void FlushRemovals();
    bool HasRemovals { get; }
    void ReleaseProxy(uint id);
    void Draw(MyRenderMessageDrawVideo draw, ulong texture, int width, int height);
}

internal sealed class VulkanMediaDevice : IVulkanMediaDevice
{
    private readonly NativeBackend backend;
    private readonly VulkanTextureCache textures;
    private readonly VulkanSprites sprites;
    private IntPtr native;
    public bool Available => native != IntPtr.Zero;
    internal VulkanMediaDevice(NativeBackend backend, VulkanTextureCache textures, VulkanSprites sprites)
    {
        this.backend = backend; this.textures = textures; this.sprites = sprites;
        try { if (!backend.TryCreateMedia(out native)) VulkanDiagnostics.Failure("Bundled libmpv runtime or one of its dependencies could not be loaded."); }
        catch (Exception error) { VulkanDiagnostics.Failure("Native media runtime: " + error.Message); }
    }
    public ulong ReserveTexture() => textures.ReserveMediaHandle();
    public bool Play(uint id, string path, float volume) => backend.PlayMedia(native, id, path, volume);
    public NativeMediaFrame Update(uint id) => backend.UpdateMedia(native, id);
    public void Volume(uint id, float volume) => backend.SetMediaVolume(native, id, volume);
    public void Close(uint id) => backend.CloseMedia(native, id);
    public void Upload(ulong texture, NativeMediaFrame frame) => backend.UploadMediaTexture(texture, frame);
    // Frame split (milestone 4, slice 4): the closed video leaves the Apply sprite batch now; its native texture is
    // removed at the hand-off (FlushRemovals), once the batch Build may still draw no longer names it.
    readonly System.Collections.Generic.List<ulong> pendingRemovals = new();
    public void Remove(ulong texture) { sprites.RemoveVideoTexture(texture); pendingRemovals.Add(texture); }
    public bool HasRemovals => pendingRemovals.Count > 0;
    public void FlushRemovals() { foreach (ulong texture in pendingRemovals) backend.RemoveResource(0, texture); pendingRemovals.Clear(); }
    public void ReleaseProxy(uint id)
    {
        if (MyRenderProxy.GetObjectType(id) == MyRenderProxy.ObjectType.Video) MyRenderProxy.RemoveMessageId(id, MyRenderProxy.ObjectType.Video);
    }
    public void Draw(MyRenderMessageDrawVideo draw, ulong texture, int width, int height) => sprites.AddVideo(draw, texture, width, height);
    public void Dispose() { backend.DestroyMedia(native); native = IntPtr.Zero; }
}
