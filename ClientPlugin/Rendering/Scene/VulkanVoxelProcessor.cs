using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VRage;
using VRage.Library;
using VRage.Library.Collections;
using VRageMath;
using VRageRender.Voxels;

namespace ClientPlugin.Rendering.Scene;
// Meshing workers own this processor. It never accesses scene state or Vulkan.
// GetData transfers native-array ownership to the cell callback, including failure paths.
public sealed class VulkanVoxelProcessorProvider : IMyVoxelRenderDataProcessorProvider
{
    public IMyVoxelRenderDataProcessor GetRenderDataProcessor(int vertexCount,int indexCount,int partsCount)
        =>new VulkanVoxelProcessor(vertexCount,indexCount,partsCount);
}
internal sealed class VulkanVoxelArray : NativeArray { public VulkanVoxelArray(int bytes):base(bytes){} }
internal sealed class VulkanVoxelProcessor : IMyVoxelRenderDataProcessor
{
    private readonly MyVertexFormatVoxelSingleData[] vertices;
    private readonly uint[] indices;
    private readonly List<MyVoxelMeshPartIndex> parts=new List<MyVoxelMeshPartIndex>();
    private int vertexOffset,indexOffset;
    private bool disposed;
    public VulkanVoxelProcessor(int vc,int ic,int pc)
    {
        if(vc<0||ic<0||pc<0||vc>10000000||ic>30000000)throw new ArgumentOutOfRangeException();
        vertices=new MyVertexFormatVoxelSingleData[vc];indices=new uint[ic];
    }
    public unsafe void AddPart(MyList<MyVertexFormatVoxelSingleData> source,ushort* input,int count,MyVoxelMaterialTriple material)
    {
        if(disposed||source==null||count<0||count%3!=0||(count>0&&input==null)||source.Count>vertices.Length-vertexOffset||count>indices.Length-indexOffset)throw new InvalidOperationException("Invalid voxel processor part.");
        Array.Copy(source.GetInternalArray(),0,vertices,vertexOffset,source.Count);
        for(int i=0;i<count;++i){if(input[i]>=source.Count)throw new ArgumentOutOfRangeException("voxel index");indices[indexOffset+i]=(uint)(vertexOffset+input[i]);}
        parts.Add(new MyVoxelMeshPartIndex{Materials=material,StartIndex=indexOffset,IndexCount=count});vertexOffset+=source.Count;indexOffset+=count;
    }
    public unsafe void GetDataAndDispose(ref MyVoxelRenderCellData data)
    {
        if(disposed)throw new ObjectDisposedException(nameof(VulkanVoxelProcessor));disposed=true;
        NativeArray va=null,ia=null;
        try
        {
            va=new VulkanVoxelArray(checked(vertexOffset*24));ia=new VulkanVoxelArray(checked(indexOffset*4));
            fixed(MyVertexFormatVoxelSingleData* p=vertices)Buffer.MemoryCopy(p,(void*)va.Ptr,va.Size,va.Size);
            fixed(uint* p=indices)Buffer.MemoryCopy(p,(void*)ia.Ptr,ia.Size,ia.Size);
            var bounds=BoundingBox.CreateInvalid();for(int i=0;i<vertexOffset;++i)bounds.Include(vertices[i].Position);
            data.Vertices=va;data.Indices=ia;data.Normals=null;data.Parts=parts.ToArray();data.ShortIndices=false;data.VertexCount=vertexOffset;data.IndexCount=indexOffset;data.CellBounds=bounds;
        }
        catch{va?.Dispose();ia?.Dispose();throw;}
    }
}
