using System;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering;

// Mirrors SE 1.210.014 MyRender11.ProcessRenderFrame/ProcessPreFrame ownership.
// Kept independent of scene/GPU work so skipped and retained frames can be tested.
internal static class RenderFrameProtocol
{
    internal static void Process(MyUpdateFrame frame, bool draw, Action<MyRenderMessageBase> dispatch)
    {
        foreach (var message in frame.RenderInput)
            if ((draw || (message.MessageClass != MyRenderMessageType.Draw && message.MessageClass != MyRenderMessageType.DebugDraw))
                && (!frame.Processed || message.MessageClass != MyRenderMessageType.StateChangeOnce)) dispatch(message);
        frame.Processed = true;
    }

    internal static void Retire(MyUpdateFrame frame, Action<MyRenderMessageBase> dispatch, Action<MyUpdateFrame> returnShared)
    {
        if (!frame.Processed) foreach (var message in frame.RenderInput)
            if (message.MessageClass != MyRenderMessageType.Draw && message.MessageClass != MyRenderMessageType.DebugDraw) dispatch(message);
        lock (frame)
        {
            frame.Cleared = true;
            foreach (var message in frame.RenderInput) if (!message.IsPersistent) message.Dispose();
            frame.RenderInput.Clear();
            if (returnShared != null) returnShared(frame);
            else { frame.Cleared = false; frame.Processed = false; }
        }
    }
}
