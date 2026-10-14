using ClientPlugin.Rendering;
using VRageRender;
using VRageRender.Messages;

var checks = 0;
void Check(bool condition, string name)
{ if (!condition) throw new Exception(name); ++checks; Console.WriteLine("PASS " + name); }
var observed = new List<string>();
void Dispatch(MyRenderMessageBase message) => observed.Add(((Tracked)message).Name);
var frame = new MyUpdateFrame();
var once = new Tracked("once", MyRenderMessageType.StateChangeOnce);
var each = new Tracked("each", MyRenderMessageType.StateChangeEvery);
var draw = new Tracked("draw", MyRenderMessageType.Draw);
var debug = new Tracked("debug", MyRenderMessageType.DebugDraw);
var persistent = new Tracked("persistent", MyRenderMessageType.Draw, true);
foreach (var message in new[] { once, draw, each, debug, persistent }) frame.Enqueue(message);
RenderFrameProtocol.Process(frame, true, Dispatch);
Check(string.Join(",", observed) == "once,draw,each,debug,persistent", "first frame preserves submission order");
observed.Clear(); RenderFrameProtocol.Process(frame, true, Dispatch);
Check(string.Join(",", observed) == "draw,each,debug,persistent", "retained frame replays draws and recurring state only");
observed.Clear(); RenderFrameProtocol.Process(frame, false, Dispatch);
Check(string.Join(",", observed) == "each", "draw-disabled frame keeps recurring state");
RenderFrameProtocol.Retire(frame, Dispatch, returned => Check(ReferenceEquals(returned, frame) && returned.Cleared && returned.RenderInput.Count == 0, "shared frame returns cleared"));
Check(once.Disposals == 1 && each.Disposals == 1 && draw.Disposals == 1 && debug.Disposals == 1, "nonpersistent messages disposed exactly once");
Check(persistent.Disposals == 0, "persistent message remains caller-owned");
Check(observed.Count == 1, "already processed retirement does not replay state");
RenderFrameProtocol.Retire(frame, Dispatch, _ => { });
Check(once.Disposals == 1 && each.Disposals == 1, "empty repeated retirement does not dispose again");

var skipped = new MyUpdateFrame();
var child = new Tracked("owned-child", MyRenderMessageType.Draw);
var owner = new Tracked("owner", MyRenderMessageType.Draw) { Child = child };
var skippedOnce = new Tracked("skipped-once", MyRenderMessageType.StateChangeOnce);
var skippedEach = new Tracked("skipped-each", MyRenderMessageType.StateChangeEvery);
skipped.Enqueue(owner); skipped.Enqueue(skippedOnce); skipped.Enqueue(skippedEach);
observed.Clear(); RenderFrameProtocol.Retire(skipped, Dispatch, _ => { });
Check(string.Join(",", observed) == "skipped-once,skipped-each", "skipped frame processes state once without drawing");
Check(owner.Disposals == 1 && child.Disposals == 1, "nested command disposal belongs to the outer owner");

var next = new MyUpdateFrame();
next.Enqueue(new Tracked("next1", MyRenderMessageType.StateChangeOnce));
observed.Clear(); RenderFrameProtocol.Process(next, true, Dispatch); RenderFrameProtocol.Retire(next, Dispatch, null);
Check(!next.Processed && !next.Cleared && next.RenderInput.Count == 0, "render-thread next-frame queue resets for reuse");
next.Enqueue(new Tracked("next2", MyRenderMessageType.StateChangeOnce));
RenderFrameProtocol.Process(next, true, Dispatch); RenderFrameProtocol.Retire(next, Dispatch, null);
Check(string.Join(",", observed) == "next1,next2", "new render-thread state survives successive frames");
var animationClock = new RenderFrameClock(1000, 1000);
Check(Math.Abs(animationClock.Sample(1016) - .016f) < .00001f, "RenderDirect default uses elapsed time");
Check(Math.Abs(animationClock.Sample(2016) - .066f) < .00001f, "slow animation frame clamps to stock 66 ms");
animationClock.StepOverride = .02f;
Check(Math.Abs(animationClock.Sample(2020) - .02f) < .00001f, "positive frame-time override advances fixed step");
animationClock.StepOverride = -1;
Check(animationClock.Sample(3020) == 0, "negative frame-time override pauses animation");
animationClock.StepOverride = 0;
Check(Math.Abs(animationClock.Sample(3030) - .01f) < .00001f, "return to measured animation excludes paused time");
Console.WriteLine(checks + " production render-protocol and animation checks passed.");

sealed class Tracked : MyRenderMessageBase
{
    internal readonly string Name;
    private readonly MyRenderMessageType messageClass;
    private readonly bool persistent;
    internal int Disposals;
    internal Tracked Child;
    internal Tracked(string name, MyRenderMessageType type, bool persistent = false) { Name = name; messageClass = type; this.persistent = persistent; }
    public override MyRenderMessageType MessageClass => messageClass;
    public override MyRenderMessageEnum MessageType => MyRenderMessageEnum.RenderProfiler;
    public override bool IsPersistent => persistent;
    public override void Dispose() { ++Disposals; Child?.Dispose(); }
}
