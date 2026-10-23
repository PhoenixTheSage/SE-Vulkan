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
    readonly List<(Line line,bool persistent)> frame=new();
    readonly List<Line> persistent=new();
    int overflow;bool clearPersistent;
    NativeLineVertex[] depthVertices=new NativeLineVertex[256],overlayVertices=new NativeLineVertex[256];

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

    public void Clear(){frame.Clear();persistent.Clear();overflow=0;clearPersistent=false;}

    /// <summary>Submits this frame's and the persistent lines, then advances the queues as MyRender11.ProcessDebugMessages does.</summary>
    public void Submit(NativeBackend backend,Vector3D camera)
    {
        int depth=0,overlay=0;
        void Emit(in Line line)
        {
            ref var vertices=ref line.DepthRead?ref depthVertices:ref overlayVertices;ref int count=ref line.DepthRead?ref depth:ref overlay;
            if(count+2>vertices.Length)System.Array.Resize(ref vertices,vertices.Length*2);
            Vector3 a=(Vector3)(line.From-camera),b=(Vector3)(line.To-camera);
            vertices[count++]=new NativeLineVertex{X=a.X,Y=a.Y,Z=a.Z,Color=line.ColorFrom.PackedValue};
            vertices[count++]=new NativeLineVertex{X=b.X,Y=b.Y,Z=b.Z,Color=line.ColorTo.PackedValue};
        }
        foreach(var entry in frame)Emit(entry.line);
        foreach(var line in persistent)Emit(line);
        backend.DrawLines(depthVertices,depth,true);
        backend.DrawLines(overlayVertices,overlay,false);
        foreach(var entry in frame)if(entry.persistent)persistent.Add(entry.line);
        frame.Clear();overflow=0;
        if(clearPersistent){persistent.Clear();clearPersistent=false;}
        else if(persistent.Count>MaxPersistent)persistent.RemoveRange(0,persistent.Count-MaxPersistent);
    }
}
