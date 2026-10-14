using System;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
using VRage;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private VoxelFunctions voxelApi;
    private VoxelFunctions VoxelApi=>voxelApi??(voxelApi=new VoxelFunctions(GetSymbol));
    public ShaderMetadata VoxelMaterialLayout { get; private set; }
    public NativeVoxelStats GetVoxelStats(){var result=new NativeVoxelStats{Size=48,AbiVersion=AbiVersion};Check(VoxelApi.Stats(context,ref result),context);return result;}
    public void ConfigureVoxels(VulkanShaderLibrary library)
    {
        foreach(var spec in VoxelShaderVariants.Materials)
        {
            var vs=library.Load(spec.Vertex);var ps=library.Load(spec.Pixel);
            byte[] vertex=SpirvRuntimeModule.FromArtifact(vs),pixel=SpirvRuntimeModule.FromArtifact(ps);
            Check(WorldApi.InitPipeline(context,spec.Id,vertex,(ulong)vertex.Length,pixel,(ulong)pixel.Length),context);
            WorldLayouts[spec.Id]=vs.Layout;if(spec.Id==20)VoxelMaterialLayout=ps.Layout;
        }
        _=VoxelApi;
    }
    public void UploadVoxelMesh(ulong id,MyVertexFormatVoxelSingleData[] vertices,uint[] indices)
    {Check(VoxelApi.Mesh(context,id,vertices,(uint)vertices.Length,indices,(uint)indices.Length),context);}
    public void UploadVoxelArray(ulong id,int width,int height,int format,bool srgb,int layers,int mips,byte[] bytes)
    {Check(VoxelApi.Array(context,id,(uint)width,(uint)height,TextureFormat(format),srgb?1u:0u,(uint)layers,(uint)mips,bytes,(ulong)bytes.Length),context);}
    public void BuildVoxelArray(ulong id,int size,bool srgb,ulong[] sources){Check(VoxelApi.Build(context,id,(uint)size,srgb?1u:0u,sources,(uint)sources.Length),context);}
    public void SetVoxelMaterial(ulong id,byte[] constants,ulong[] arrays,ulong random=0)
    {if(arrays==null||arrays.Length!=3)throw new ArgumentException("Three voxel texture arrays are required.");Check(VoxelApi.Material(context,id,constants,(uint)constants.Length,arrays,random),context);}
}
internal sealed class VoxelFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int MeshFn(IntPtr context,ulong id,[In] MyVertexFormatVoxelSingleData[] vertices,uint count,uint[] indices,uint indexCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ArrayFn(IntPtr context,ulong id,uint width,uint height,uint format,uint srgb,uint layers,uint mips,byte[] data,ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int MaterialFn(IntPtr context,ulong id,byte[] constants,uint count,ulong[] arrays,ulong random);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int StatsFn(IntPtr context,ref NativeVoxelStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BuildFn(IntPtr context,ulong id,uint size,uint srgb,ulong[] sources,uint layers);
    internal readonly BuildFn Build;
    internal readonly StatsFn Stats;
    internal readonly MeshFn Mesh;internal readonly ArrayFn Array;internal readonly MaterialFn Material;
    internal VoxelFunctions(Func<string,IntPtr> symbol)
    {
        T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));
        Build=Get<BuildFn>("sevk_build_voxel_array");Stats=Get<StatsFn>("sevk_get_voxel_stats");Mesh=Get<MeshFn>("sevk_upload_voxel_mesh");Array=Get<ArrayFn>("sevk_upload_voxel_array");Material=Get<MaterialFn>("sevk_set_voxel_material");
    }
}

[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeVoxelStats {public uint Size,AbiVersion;public ulong Frames,Draws,Meshes,Arrays,Materials;}
