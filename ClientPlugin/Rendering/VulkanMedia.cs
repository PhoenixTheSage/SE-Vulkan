using System;
using System.Collections.Concurrent;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRage;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering;

internal sealed class VulkanMedia : IDisposable
{
    private sealed class Entry
    {
        internal volatile uint State;
        internal readonly ulong Texture;
        internal ulong Sequence;
        internal long UploadUs;
        internal int Width, Height;
        internal bool Updating;
        internal Entry(ulong texture) { Texture = texture; }
    }
    private readonly ConcurrentDictionary<uint, Entry> players = new();
    private readonly IVulkanMediaDevice device;
    internal bool Available => device.Available;
    internal bool Failed(uint id) => players.TryGetValue(id, out var player) && player.State == 3;
    internal bool IsValid(uint id) => players.ContainsKey(id);
    internal VideoState State(uint id) => players.TryGetValue(id, out var player) && player.State < 2 ? VideoState.Playing : VideoState.Stopped;

    internal VulkanMedia(NativeBackend backend, VulkanTextureCache textures, VulkanSprites sprites) : this(new VulkanMediaDevice(backend, textures, sprites)) { }
    internal VulkanMedia(IVulkanMediaDevice device)
    {
        this.device = device;
        VulkanDiagnostics.Set("Native media", Available ? SubsystemState.Ready : SubsystemState.Unavailable,
            Available ? "Bundled libmpv ready; software frames, Vulkan sprite composition and independent audio." : "Bundled media runtime unavailable; intro/menu use failure fallback.");
    }
    internal bool Process(MyRenderMessageBase message)
    {
        switch (message)
        {
            case MyRenderMessagePlayVideo play:
                Close(play.ID, false);
                var entry = new Entry(device.ReserveTexture()); players[play.ID] = entry;
                if (!Available || !device.Play(play.ID, play.VideoFile, Volume(play.Volume))) Fail(play.ID, entry, "Unable to open bundled media player.");
                return true;
            case MyRenderMessageUpdateVideo update:
                if (players.TryGetValue(update.ID, out var active)) { active.Updating = true; Update(update.ID, active); } return true;
            case MyRenderMessageSetVideoVolume volume:
                if (Available && players.ContainsKey(volume.ID)) device.Volume(volume.ID, Volume(volume.Volume)); return true;
            case MyRenderMessageCloseVideo close: Close(close.ID, true); return true;
            case MyRenderMessageDrawVideo draw:
                if (players.TryGetValue(draw.ID, out var player) && player.Sequence > 0 && player.State != 3)
                    device.Draw(draw, player.Texture, player.Width, player.Height);
                return true;
            default: return false;
        }
    }
    private static float Volume(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value));
    // Native removals of closed videos, at the hand-off (VulkanMediaDevice.Remove).
    internal void FlushRemovals() => device.FlushRemovals();
    internal bool HasRemovals => device.HasRemovals;
    internal void Poll()
    {
        foreach (var pair in players) if (pair.Value.Updating) Update(pair.Key, pair.Value);
    }
    private void Update(uint id, Entry player)
    {
        if (!Available || player.State == 3) return;
        var frame = device.Update(id);player.State = frame.State;
        if (frame.State == 3) { Fail(id, player, frame.Failure); return; }
        if (frame.Sequence == 0 || frame.Sequence == player.Sequence) return;
        long uploadStart = System.Diagnostics.Stopwatch.GetTimestamp();
        device.Upload(player.Texture, frame);
        player.UploadUs += (long)((System.Diagnostics.Stopwatch.GetTimestamp() - uploadStart) * (1000000.0 / System.Diagnostics.Stopwatch.Frequency));
        player.Sequence = frame.Sequence; player.Width = (int)frame.Width; player.Height = (int)frame.Height;
        VulkanDiagnostics.Set("Native media", SubsystemState.Active, "Video " + id + ": " + frame.Width + "x" + frame.Height + "; frames=" + frame.Frames + "; dropped=" + frame.Dropped + "; output owner CPU us=" + frame.ConversionUs + "; upload CPU us=" + player.UploadUs + "; position us=" + frame.PositionUs + "; A/V offset us=" + ((frame.Reserved & 2) != 0 ? frame.AvOffsetUs.ToString() : "unavailable") + "; audio=" + ((frame.Reserved & 4) != 0 ? "device" : "unavailable") + ". Live acceptance is recorded separately.", frame.Frames);
    }
    private static void Fail(uint id, Entry player, string failure)
    {
        player.State = 3;
        VulkanDiagnostics.Failure("Video " + id + ": " + failure);
        VulkanDiagnostics.Set("Native media", SubsystemState.Failed, failure);
    }
    private void Close(uint id, bool releaseId)
    {
        if (players.TryRemove(id, out var player))
        {
            if (Available) device.Close(id);
            if (player.Sequence > 0) device.Remove(player.Texture);
        }
        if (releaseId) device.ReleaseProxy(id);
    }
    public void Dispose()
    {
        // Join conversion/control workers while their runtime and CPU buffers live.
        device.Dispose();
        foreach (var pair in players) Close(pair.Key, true);
        device.FlushRemovals();
    }
}
