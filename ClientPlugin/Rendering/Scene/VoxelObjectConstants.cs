using System;
using System.Runtime.CompilerServices;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

// Metadata owns the immutable layout; each packet still gets zeroed bytes (rented,
// recycled when its packet is cleared) and every dynamic field is written.
// No actor or camera state is cached.
public static class VoxelObjectConstants
{
    public static bool Enabled { get; set; }=Environment.GetEnvironmentVariable("SE_VULKAN_VOXEL_OBJECT_CACHE")!="0";
    private static readonly ConditionalWeakTable<ShaderMetadata,Layout> Layouts=new ConditionalWeakTable<ShaderMetadata,Layout>();
    private sealed class Layout
    {
        public readonly int Size,Lod,Offset,Scale,Radius,Start,Center,Row0,Row1,Row2,Color,Emissive,Alpha;
        public Layout(ShaderMetadata metadata)
        {
            var buffer=new ReflectedConstantBuffer(metadata,"Object");Size=buffer.Bytes.Length;
            Lod=buffer.FloatOffsetOf("object_.voxelLodSize",1);
            Offset=buffer.FloatOffsetOf("object_.voxel_offset",3);
            Scale=buffer.FloatOffsetOf("object_.voxel_scale",3);
            Radius=buffer.FloatOffsetOf("object_.spherizeTo",1);
            Start=buffer.FloatOffsetOf("object_.spherizeStart",1);
            Center=buffer.FloatOffsetOf("object_.spherizeCenter",3);
            Row0=buffer.FloatOffsetOf("object_.matrix_row0",4);
            Row1=buffer.FloatOffsetOf("object_.matrix_row1",4);
            Row2=buffer.FloatOffsetOf("object_.matrix_row2",4);
            Color=buffer.FloatOffsetOf("object_.color_mul",3);
            Emissive=buffer.FloatOffsetOf("object_.emissive",1);
            Alpha=buffer.FloatOffsetOf("object_.custom_alpha",2);
        }
    }
    // Builds the layout now; its first use fills shared reflection caches that are not thread-safe.
    public static void Warm(ShaderMetadata metadata)=>Layouts.GetValue(metadata,key=>new Layout(key));
    public static unsafe byte[] Write(ShaderMetadata metadata,MatrixD matrix,Vector3D offset,int lod,float dither,float? radius,Vector3D center)
    {
        Layout layout=Layouts.GetValue(metadata,key=>new Layout(key));
        var bytes=ReflectedConstantBuffer.Rent(layout.Size);float scale=(float)(1<<lod);
        fixed(byte* target=bytes)
        {
            *(float*)(target+layout.Lod)=lod;
            Put3(target,layout.Offset,(float)offset.X,(float)offset.Y,(float)offset.Z);
            Put3(target,layout.Scale,scale,scale,scale);
            *(float*)(target+layout.Radius)=radius??0;
            *(float*)(target+layout.Start)=radius.HasValue?radius.Value*1.1f:0;
            Put3(target,layout.Center,(float)center.X,(float)center.Y,(float)center.Z);
            Put4(target,layout.Row0,(float)matrix.M11,(float)matrix.M21,(float)matrix.M31,(float)matrix.M41);
            Put4(target,layout.Row1,(float)matrix.M12,(float)matrix.M22,(float)matrix.M32,(float)matrix.M42);
            Put4(target,layout.Row2,(float)matrix.M13,(float)matrix.M23,(float)matrix.M33,(float)matrix.M43);
            Put3(target,layout.Color,1,1,1);*(float*)(target+layout.Emissive)=1;
            float* alpha=(float*)(target+layout.Alpha);alpha[0]=dither;alpha[1]=0;
        }
        return bytes;
    }
    private static unsafe void Put3(byte* target,int offset,float x,float y,float z)
    {float* field=(float*)(target+offset);field[0]=x;field[1]=y;field[2]=z;}
    private static unsafe void Put4(byte* target,int offset,float x,float y,float z,float w)
    {float* field=(float*)(target+offset);field[0]=x;field[1]=y;field[2]=z;field[3]=w;}
}
