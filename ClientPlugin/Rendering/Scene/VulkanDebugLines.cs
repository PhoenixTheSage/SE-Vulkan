using System.Collections.Generic;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;

/// <summary>
/// DebugDrawLine3D replay with MyRender11's debug-message queueing: frame messages (ring-capped at 100000),
/// persistent messages carried into later frames (capped at 1000), and DebugClearPersistentMessages applied
/// after the frame draws. Lines are made camera-relative in double precision, as MyLinesRenderer does.
/// </summary>
public sealed class VulkanDebugLines
{
    const int MaxFrameMessages=100000,MaxPersistent=1000;
    struct Line{public Vector3D From,To;public Color ColorFrom,ColorTo;public bool DepthRead;}
    List<(Line line,bool persistent)> frame=new(),latchedFrame=new();
    // Frame split (milestone 6): with the build thread, Latch hands Apply's lines to Build at the hand-off and Submit
    // draws the latched ones; without it, Submit draws the live list, as before.
    public bool Latched;bool latchedClear;
    public void Latch(){(frame,latchedFrame)=(latchedFrame,frame);frame.Clear();overflow=0;latchedClear|=clearPersistent;clearPersistent=false;}
    readonly List<Line> persistent=new();
    int overflow;bool clearPersistent;

    public int FrameCount=>frame.Count;
    public int PersistentCount=>persistent.Count;

    public void Add(MyRenderMessageDebugDrawLine3D message)
    {
        var entry=(new Line{From=message.PointFrom,To=message.PointTo,ColorFrom=message.ColorFrom,ColorTo=message.ColorTo,DepthRead=message.DepthRead},message.IsPersistent);
        if(frame.Count<MaxFrameMessages){frame.Add(entry);return;}
        if(overflow>=MaxFrameMessages-1)overflow=0;
        frame[overflow++]=entry;
    }

    public void ClearPersistent()=>clearPersistent=true;

    public void Clear(){frame.Clear();latchedFrame.Clear();persistent.Clear();overflow=0;clearPersistent=latchedClear=false;}

    /// <summary>One frame's camera-relative vertices (Plan 3: stage A prepares them, stage B draws them).</summary>
    public sealed class LineBatch {internal NativeLineVertex[] Depth=new NativeLineVertex[256],Overlay=new NativeLineVertex[256];internal int DepthCount,OverlayCount;}
    readonly LineBatch own=new();
    /// <summary>Submits this frame's and the persistent lines, then advances the queues as MyRender11.ProcessDebugMessages does.</summary>
    public void Submit(NativeBackend backend,Vector3D camera){Prepare(camera,own);Draw(backend,own);}
    /// <summary>Makes this frame's and the persistent lines camera-relative into the batch and advances the queues.</summary>
    public void Prepare(Vector3D camera,LineBatch batch)
    {
        int depth=0,overlay=0;
        void Emit(in Line line)
        {
            ref var vertices=ref line.DepthRead?ref batch.Depth:ref batch.Overlay;ref int count=ref line.DepthRead?ref depth:ref overlay;
            if(count+2>vertices.Length)System.Array.Resize(ref vertices,vertices.Length*2);
            Vector3 a=(Vector3)(line.From-camera),b=(Vector3)(line.To-camera);
            vertices[count++]=new NativeLineVertex{X=a.X,Y=a.Y,Z=a.Z,Color=line.ColorFrom.PackedValue};
            vertices[count++]=new NativeLineVertex{X=b.X,Y=b.Y,Z=b.Z,Color=line.ColorTo.PackedValue};
        }
        var lines=Latched?latchedFrame:frame;
        foreach(var entry in lines)Emit(entry.line);
        foreach(var line in persistent)Emit(line);
        batch.DepthCount=depth;batch.OverlayCount=overlay;
        foreach(var entry in lines)if(entry.persistent)persistent.Add(entry.line);
        lines.Clear();if(!Latched)overflow=0;
        bool clear=Latched?latchedClear:clearPersistent;
        if(clear){persistent.Clear();if(Latched)latchedClear=false;else clearPersistent=false;}
        else if(persistent.Count>MaxPersistent)persistent.RemoveRange(0,persistent.Count-MaxPersistent);
    }
    public static void Draw(NativeBackend backend,LineBatch batch){backend.DrawLines(batch.Depth,batch.DepthCount,true);backend.DrawLines(batch.Overlay,batch.OverlayCount,false);}
}
