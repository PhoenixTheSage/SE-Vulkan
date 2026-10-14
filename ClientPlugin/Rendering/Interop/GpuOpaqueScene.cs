using System;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
namespace ClientPlugin.Rendering.Interop;
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeGpuOpaqueActor {
    public fixed double Translation[3],Minimum[3],Maximum[3],LocalMinimum[3],LocalMaximum[3],Inverse[12];
    public fixed float Basis[9];public float Scale;public fixed float Colour[4];
    public uint Owner,Generation,Family,Flags,SourceFirst,SourceCount;public fixed uint KeyColour[2];
    public uint AppearanceCount,HiddenLow,HiddenHigh,DepthRevision;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeGpuOpaqueFamily {public uint Generation,FirstPart,PartCount,ThresholdCount;public fixed float Thresholds[16];}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeGpuOpaquePart {public uint Family,Lod,FirstIndex,IndexCount,Material,Flags,Generation,Reserved;public fixed uint Textures[4];public int VertexOffset;public uint Padding,DrawFirst,DrawCapacity,CoverageCount;public fixed uint CoverageMaterials[32];}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeGpuOpaqueAppearance {public uint Material,Visible;public fixed uint Colour[2];public fixed float OldColour[4];}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeGpuOpaqueAppearanceUpdate {public uint Actor,First,Count,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeGpuSceneActor {
    public uint Size,AbiVersion;public ulong Handle,Family,Source,Group;
    public uint SourceFirst,SourceCount,AppearanceFirst,AppearanceCount;
    public NativeGpuOpaqueActor Input;public NativeGpuOpaqueSelector Selector;public fixed byte Object[128];
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeGpuOpaqueSelector {public int Current,Other;public float Transition,Start;public uint Initialized,Border,Generation,Epoch;public fixed double LastCamera[3];}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeGpuOpaqueBatch {public ulong Mesh;public uint Old,MainPipeline,DepthPipeline,ForwardPipeline,Flags,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeGpuOpaqueView {public fixed double Planes[24];public double MinimumSizeSquared,MaximumDistance;public fixed float Projection[16];public int MinimumLod,Shift,VisibleShift;public uint Flags,Id,Light;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeGpuOpaqueFrame {public fixed double Camera[3];public double SmoothCameraDistance;public float Delta,Coefficient,DistanceAdd,DistanceMult,Hysteresis,DeadZone,DeadZoneMult,MaximumSeconds,MinimumSeconds;public uint Flags,Selection,Epoch;}
[StructLayout(LayoutKind.Sequential,Pack=8)]struct NativeGpuOpaqueConfig {
    public uint Size,AbiVersion,ActorCount,FamilyCount,PartCount,ViewCount,VertexCount,IndexCount,SourceCount,ReferenceCapacity,DrawCapacity,Reserved;
    public IntPtr Actors,Families,Parts,Vertices,Indices,Sources,Textures;public uint MaterialCount,MaterialReserved;
}
public sealed partial class NativeBackend {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuRefInitFn(IntPtr ctx,ref NativeGpuOpaqueConfig config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuRefFrameFn(IntPtr ctx,ref NativeGpuOpaqueFrame frame,IntPtr views,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuStockPipelineFn(IntPtr ctx,uint id,uint old,byte[] vs,ulong vb,byte[] ps,ulong pb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuStockInputsFn(IntPtr ctx,ulong mesh,byte[] objects,uint bytes,uint old,uint main,uint depth);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuStockBatchesFn(IntPtr ctx,IntPtr batches,uint count,IntPtr meshes,uint parts,byte[] objects,uint bytes);
    GpuStockBatchesFn gpuStockBatches;
    GpuRefInitFn gpuRefInit;GpuRefFrameFn gpuRefFrame;GpuStockPipelineFn gpuStockPipeline;GpuStockInputsFn gpuStockInputs;
    public unsafe void InitializeGpuOpaqueReference(NativeGpuOpaqueActor[] actors,NativeGpuOpaqueFamily[] families,NativeGpuOpaquePart[] parts,int viewCount,float[] vertices,uint[] indices,byte[] sources,ulong[] textures,uint referenceCapacity,uint drawCapacity,uint batchCount=1){
        gpuRefInit??=PassFunction<GpuRefInitFn>("sevk_opaque_reference_init");
        fixed(NativeGpuOpaqueActor* a=actors)fixed(NativeGpuOpaqueFamily* f=families)fixed(NativeGpuOpaquePart* p=parts)fixed(float* v=vertices)fixed(uint* i=indices)fixed(byte* s=sources)fixed(ulong* t=textures){
            var config=new NativeGpuOpaqueConfig{Size=(uint)Marshal.SizeOf<NativeGpuOpaqueConfig>(),AbiVersion=AbiVersion,ActorCount=(uint)actors.Length,FamilyCount=(uint)families.Length,PartCount=(uint)parts.Length,ViewCount=(uint)viewCount,VertexCount=(uint)(vertices.Length/3),IndexCount=(uint)indices.Length,SourceCount=(uint)(sources.Length/64),ReferenceCapacity=referenceCapacity,DrawCapacity=drawCapacity,Reserved=batchCount,Actors=(IntPtr)a,Families=(IntPtr)f,Parts=(IntPtr)p,Vertices=(IntPtr)v,Indices=(IntPtr)i,Sources=(IntPtr)s,Textures=(IntPtr)t,MaterialCount=(uint)textures.Length};Check(gpuRefInit(context,ref config),context);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuActorUpdatesFn(IntPtr ctx,IntPtr indices,IntPtr actors,uint count,IntPtr objects,uint bytes);
    GpuActorUpdatesFn gpuActorUpdates;
    public unsafe void UpdateGpuOpaqueActors(uint[] indices,NativeGpuOpaqueActor[] actors,byte[] objects=null){
        if(indices.Length!=actors.Length||(objects!=null&&objects.Length!=actors.Length*128))throw new ArgumentException("GPU actor commit sizes differ");
        gpuActorUpdates??=PassFunction<GpuActorUpdatesFn>("sevk_opaque_reference_actors");fixed(uint* i=indices)fixed(NativeGpuOpaqueActor* a=actors)fixed(byte* o=objects)Check(gpuActorUpdates(context,(IntPtr)i,(IntPtr)a,(uint)actors.Length,(IntPtr)o,(uint)(objects?.Length??0)),context);
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuAppearancesFn(IntPtr ctx,IntPtr updates,uint count,IntPtr entries,uint total);
    GpuAppearancesFn gpuAppearances;
    public unsafe void UpdateGpuOpaqueAppearances(NativeGpuOpaqueAppearanceUpdate[] updates,NativeGpuOpaqueAppearance[] appearances){gpuAppearances??=PassFunction<GpuAppearancesFn>("sevk_opaque_reference_appearances");fixed(NativeGpuOpaqueAppearanceUpdate* u=updates)fixed(NativeGpuOpaqueAppearance* e=appearances)Check(gpuAppearances(context,(IntPtr)u,(uint)updates.Length,(IntPtr)e,(uint)appearances.Length),context);}
    public unsafe void QueueGpuOpaqueFrame(ref NativeGpuOpaqueFrame frame,NativeGpuOpaqueView[] views){gpuRefFrame??=PassFunction<GpuRefFrameFn>("sevk_opaque_reference_frame");fixed(NativeGpuOpaqueView* input=views)Check(gpuRefFrame(context,ref frame,(IntPtr)input,(uint)views.Length),context);}
    public void InitializeGpuStockPipeline(uint id,bool old,VulkanShaderArtifact vertex,VulkanShaderArtifact pixel){
        if(vertex.Layout.SourceTransform!=GpuOpaqueShaderAdapter.Contract||pixel.Layout.SourceTransform!=GpuOpaqueShaderAdapter.Contract||vertex.Layout.BaseSpirvSha256==null||pixel.Layout.BaseSpirvSha256==null)throw new InvalidOperationException("GPU stock pipeline requires authenticated derived shader stages.");
        gpuStockPipeline??=PassFunction<GpuStockPipelineFn>("sevk_opaque_stock_pipeline");byte[] vs=SpirvRuntimeModule.FromArtifact(vertex),ps=SpirvRuntimeModule.FromArtifact(pixel);Check(gpuStockPipeline(context,id,old?1u:0u,vs,(ulong)vs.Length,ps,(ulong)ps.Length),context);
    }
    public unsafe void SetGpuStockBatches(NativeGpuOpaqueBatch[] batches,ulong[] partMeshes,byte[] objects){
        gpuStockBatches??=PassFunction<GpuStockBatchesFn>("sevk_opaque_stock_batches");fixed(NativeGpuOpaqueBatch* b=batches)fixed(ulong* m=partMeshes)Check(gpuStockBatches(context,(IntPtr)b,(uint)batches.Length,(IntPtr)m,(uint)partMeshes.Length,objects,(uint)objects.Length),context);
    }
    public void SetGpuStockInputs(ulong mesh,byte[] objects,bool old,uint main,uint depth){gpuStockInputs??=PassFunction<GpuStockInputsFn>("sevk_opaque_stock_inputs");Check(gpuStockInputs(context,mesh,objects,(uint)objects.Length,old?1u:0u,main,depth),context);}
}
