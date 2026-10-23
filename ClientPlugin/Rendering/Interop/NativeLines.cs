using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;
/// <summary>DebugDrawLine3D vertex: camera-relative position, sRGB-encoded RGBA8 color (stock MyLinesRenderer).</summary>
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeLineVertex{public float X,Y,Z;public uint Color;}
public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int DrawLinesFn(IntPtr ctx,[In]NativeLineVertex[] vertices,uint count,uint flags);
    DrawLinesFn drawLines;
    /// <summary>Line list drawn after the game scene and before sprites; depthRead tests against scene depth.</summary>
    public void DrawLines(NativeLineVertex[] vertices,int count,bool depthRead)
    {
        if(vertices==null||count<=0)return;
        drawLines??=PostFunction<DrawLinesFn>("sevk_draw_lines");Check(drawLines(context,vertices,(uint)count,depthRead?1u:0u),context);
    }
}
