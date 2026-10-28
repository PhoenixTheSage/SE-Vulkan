using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender.Messages;
using VRage;
using VRageRender;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); ++checks; Console.WriteLine("PASS " + name); }
var assembly = typeof(MyVulkanRender).Assembly;
var mediaFrameType = assembly.GetType("ClientPlugin.Rendering.Interop.NativeMediaFrame");
Check(System.Runtime.InteropServices.Marshal.SizeOf(mediaFrameType) == 592 && System.Runtime.InteropServices.Marshal.OffsetOf(mediaFrameType, "Pixels").ToInt32() == 72, "managed media ABI matches x64 native layout");
var geometry = assembly.GetType("ClientPlugin.Rendering.VideoGeometry").GetMethod("Destination", BindingFlags.Static | BindingFlags.NonPublic);
RectangleF Rect(Rectangle rect, int w, int h, MyVideoRectangleFitMode mode) => (RectangleF)geometry.Invoke(null, new object[] { rect, w, h, mode });
var target = new Rectangle(10, 20, 400, 300);
Check(Rect(target, 1920, 1080, MyVideoRectangleFitMode.None) == new RectangleF(10, 20, 400, 300), "None retains destination");
Check(Rect(target, 1920, 1080, MyVideoRectangleFitMode.FitWidth) == new RectangleF(10, 57, 400, 225), "FitWidth uses stock integer rounding");
Check(Rect(target, 1920, 1080, MyVideoRectangleFitMode.FitHeight) == new RectangleF(10, 20, 400, 300), "FitHeight matches submitted stock geometry");
Check(Rect(target, 1920, 1080, MyVideoRectangleFitMode.AutoFit) == Rect(target, 1920, 1080, MyVideoRectangleFitMode.FitWidth), "wide AutoFit");
Check(Rect(target, 1920, 1080, MyVideoRectangleFitMode.Fill) == new RectangleF(-56, 20, 533, 300), "Fill extends beyond rectangle without invented crop");
Check(Rect(target, 300, 600, MyVideoRectangleFitMode.AutoFit) == new RectangleF(135, 20, 150, 300), "tall AutoFit");
Check(Rect(target, 300, 600, MyVideoRectangleFitMode.Fill) == new RectangleF(10, -230, 400, 800), "tall Fill");
Check(Rect(target, 300, 600, MyVideoRectangleFitMode.None) == new RectangleF(10, 20, 400, 300), "tall None");
Check(Rect(target, 300, 600, MyVideoRectangleFitMode.FitWidth) == new RectangleF(10, 20, 400, 300), "tall FitWidth uses stock clamp");
Check(Rect(target, 300, 600, MyVideoRectangleFitMode.FitHeight) == new RectangleF(135, 20, 150, 300), "tall FitHeight");
Check(Rect(target, 0, 10, MyVideoRectangleFitMode.Fill).Width == 0, "invalid dimensions never divide by zero");

var cache = new VulkanTextureCache(null);
var spritesType = assembly.GetType("ClientPlugin.Rendering.VulkanSprites");
var sprites = Activator.CreateInstance(spritesType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { cache }, null);
object Call(string name, params object[] args) => spritesType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(sprites, args);
Call("BeginFrame");
Call("Process", new MyRenderMessageSpriteScissorPush { ScreenRectangle = new Rectangle(20, 30, 100, 100) });
var video = new MyRenderMessageDrawVideo { ID = 7, Rectangle = target, FitMode = MyVideoRectangleFitMode.FitWidth, Color = new Color(128, 64, 32, 128) };
Call("AddVideo", video, 91UL, 1920, 1080);
Call("Process", new MyRenderMessageSpriteScissorPop());
Call("Process", new MyRenderMessageDrawSprite { Texture = null, DestinationRectangle = new RectangleF(5, 6, 7, 8), Color = Color.White });
video.IgnoreBounds = true; Call("AddVideo", video, 92UL, 1920, 1080);
spritesType.GetField("ViewportScale").SetValue(sprites, .5f);
var packets = (NativeSprite[])Call("MainPackets", 800, 600);
Check(packets.Length == 3 && packets[0].Texture == 91 && packets[1].Texture == 0 && packets[2].Texture == 92, "video batch preserves ordering around ordinary sprites");
Check(packets[0].Flags == 2 && packets[2].Flags == 2, "opaque media uses premultiplied sprite blending");
Check(packets[0].Destination[0] == 205 && packets[0].Destination[2] == 200, "video inherits centered viewport scale");
Check(packets[0].Clip[0] == 210 && packets[0].Clip[1] == 165 && packets[0].Clip[2] == 50, "video inherits explicit scaled scissor");
Check(packets[2].Destination[0] == 10 && packets[2].Destination[2] == 400, "IgnoreBounds retains full viewport");
Check(packets[0].Color[3] > .49f && packets[0].Color[3] < .51f, "video fade alpha preserved");
Check(packets[0].Color[0] > packets[0].Color[1] && packets[0].Color[1] > packets[0].Color[2], "video preserves channel tint");
video.TargetTexture = "media-lcd"; Call("AddVideo", video, 93UL, 1920, 1080);
var targetPackets = (NativeSprite[])Call("ConsumeTarget", "media-lcd");
Check(targetPackets.Length == 1 && targetPackets[0].Texture == 93, "video reaches offscreen sprite target");
Check(((NativeSprite[])Call("ConsumeTarget", "media-lcd")).Length == 0, "offscreen target consumes once");
video.TargetTexture = "media-lcd";Call("AddVideo", video, 91UL, 1920, 1080);
Call("RemoveVideoTexture", 91UL);
Check(((NativeSprite[])Call("MainPackets", 800, 600)).All(p => p.Texture != 91) && ((NativeSprite[])Call("ConsumeTarget", "media-lcd")).Length == 0, "close-after-draw removes queued main/offscreen video packets");
var fake = new FakeMedia();
using (var media = new VulkanMedia(fake))
{
    Check(!media.IsValid(99) && media.State(99) == VideoState.Stopped, "unknown video is invalid/stopped");
    var frame = new MyUpdateFrame();
    var play = new PooledPlay { ID = 7, VideoFile = "first.wmv", Volume = .5f };
    frame.Enqueue(play); frame.Enqueue(new PooledUpdate { ID = 7 });
    frame.Enqueue(new PooledDraw { ID = 7 });
    RenderFrameProtocol.Process(frame, true, message => media.Process(message));
    Check(fake.Opens == 1 && media.IsValid(7) && media.State(7) == VideoState.Playing && fake.Draws == 0, "asynchronous opening is valid/playing without an invented frame");
    media.Process(new MyRenderMessageSetVideoVolume { ID = 7, Volume = .2f });
    Check(fake.LastVolume == .2f, "volume updates during opening");
    fake.Frame = new NativeMediaFrame { State = 1, Width = 160, Height = 96, Sequence = 1, Frames = 1, Pixels = (IntPtr)1 };
    RenderFrameProtocol.Process(frame, true, message => media.Process(message));
    Check(fake.Opens == 1 && fake.Uploads == 1 && fake.Draws == 1, "retained frame updates/draws without replaying PlayVideo");
    RenderFrameProtocol.Process(frame, true, message => media.Process(message));
    Check(fake.Uploads == 1 && fake.Draws == 2, "unchanged decoded frame is drawn without another upload");
    fake.Frame.State = 2; media.Poll();
    Check(media.IsValid(7) && media.State(7) == VideoState.Stopped, "EOF preserves registered ID until CloseVideo");
    RenderFrameProtocol.Retire(frame, message => media.Process(message), null);
    Check(fake.Path == "first.wmv", "player values survive pooled-message retirement");
    media.Process(new MyRenderMessagePlayVideo { ID = 7, VideoFile = "replacement.wmv", Volume = 1 });
    Check(fake.Opens == 2 && fake.Closes == 1 && fake.Removes == 1, "duplicate ID retires old player/texture before replacement");
    fake.Frame = default; media.Process(new MyRenderMessageCloseVideo { ID = 7 });
    Check(!media.IsValid(7) && fake.Releases == 1, "close while opening invalidates state and releases video ID");
    var skipped = new MyUpdateFrame(); skipped.Enqueue(new PooledPlay { ID = 8, VideoFile = "skipped.wmv", Volume = 0 });
    skipped.Enqueue(new PooledUpdate { ID = 8 });
    int beforeUpdates = fake.Updates;
    RenderFrameProtocol.Retire(skipped, message => media.Process(message), null);
    Check(media.IsValid(8) && fake.Updates == beforeUpdates, "skipped frame retains Play state without starting draw-only update");
    media.Process(new MyRenderMessageCloseVideo { ID = 8 });
    media.Process(new MyRenderMessagePlayVideo { ID = 9, VideoFile = "failed.wmv", Volume = 1 });
    fake.Frame = new NativeMediaFrame { State = 3, Error = System.Text.Encoding.UTF8.GetBytes("invalid media") };
    media.Process(new MyRenderMessageUpdateVideo { ID = 9 });beforeUpdates = fake.Updates;
    media.Poll();media.Poll();
    Check(media.Failed(9) && media.State(9) == VideoState.Stopped && fake.Updates == beforeUpdates, "decode failure is terminal without per-frame retries");
}
Check(fake.Log.Last() == "release" && fake.Log.IndexOf("dispose") < fake.Log.LastIndexOf("release"), "shutdown joins media owner before releasing remaining registry state");
Console.WriteLine($"{checks} production media protocol/geometry/sprite checks passed.");

// Supply the pool boundary without booting the game singleton. The real
// dispatcher still receives production message classes and retires them.
sealed class PooledPlay : MyRenderMessagePlayVideo { public override void Dispose() { VideoFile = null; } }
sealed class PooledUpdate : MyRenderMessageUpdateVideo { public override void Dispose() { } }
sealed class PooledDraw : MyRenderMessageDrawVideo { public override void Dispose() { } }

sealed class FakeMedia : IVulkanMediaDevice
{
    public bool Available { get; private set; } = true;
    public int Opens, Closes, Removes, Releases, Updates, Uploads, Draws;
    public string Path;
    public float LastVolume;
    public NativeMediaFrame Frame;
    private ulong next;
    public readonly List<string> Log = new();
    public ulong ReserveTexture() => ++next;
    public bool Play(uint id, string path, float volume) { ++Opens; Path = path; LastVolume = volume; return true; }
    public NativeMediaFrame Update(uint id) { ++Updates; return Frame; }
    public void Volume(uint id, float value) => LastVolume = value;
    public void Close(uint id) { ++Closes; Log.Add("close"); }
    public void Upload(ulong texture, NativeMediaFrame frame) { ++Uploads; Log.Add("upload"); }
    public void Remove(ulong texture) { ++Removes; Log.Add("remove"); }
    public void FlushRemovals() { }
    public void ReleaseProxy(uint id) { ++Releases; Log.Add("release"); }
    public void Draw(MyRenderMessageDrawVideo draw, ulong texture, int width, int height) { ++Draws; }
    public void Dispose() { Available = false; Log.Add("dispose"); }
}
