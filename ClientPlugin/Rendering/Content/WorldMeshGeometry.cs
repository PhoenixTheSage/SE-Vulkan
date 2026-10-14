using System;
using System.Collections.Generic;
using System.IO;
using ClientPlugin.Rendering.Interop;

namespace ClientPlugin.Rendering.Content;

// Active MWM material/depth/probe/highlight shaders consume vertex attributes;
// the SV_VertexID lookup path is guarded by USE_MERGE_INSTANCING, which this
// renderer does not use. Preserve every attribute and the complete index order.
public static class WorldMeshGeometry
{
    public static void Compact(NativeWorldVertex[] source,uint[] original,out NativeWorldVertex[] vertices,out uint[] indices)
    {
        if(source==null || original==null)throw new ArgumentNullException("World geometry is required.");
        var remap=new Dictionary<uint,uint>();var used=new List<NativeWorldVertex>();indices=new uint[original.Length];
        for(int i=0;i<original.Length;i++)
        {
            uint old=original[i];if(old>=source.Length)throw new InvalidDataException("World part index outside source vertices.");
            if(!remap.TryGetValue(old,out uint next)){next=(uint)used.Count;remap.Add(old,next);used.Add(source[old]);}
            indices[i]=next;
        }
        vertices=used.ToArray();
    }
}
public struct GeometryStorageStats
{
    public bool Compacted;
    public long ExpandedBaseVertexBytes,UploadedBaseVertexBytes,BaseIndexBytes,SkinVertexBytes,SkinIndexBytes;
    public string Scope=>"Logical current MWM/runtime geometry buffer bytes; excludes allocation overhead, terrain, textures and retired GPU resources.";
}
