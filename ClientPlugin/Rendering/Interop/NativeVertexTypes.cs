using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeGBufferVertex
{
    public float X,Y,Z,Scale;
    public uint NormalX,NormalY;
    public float TangentX,TangentY,TangentZ,TangentW,U,V;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeWorldVertex
{
    public NativeGBufferVertex Material;
    public float WeightX,WeightY,WeightZ,WeightW;
    public uint BoneX,BoneY,BoneZ,BoneW;
}
