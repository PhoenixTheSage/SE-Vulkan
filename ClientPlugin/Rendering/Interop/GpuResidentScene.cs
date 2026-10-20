using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using ClientPlugin.Rendering.Shaders;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuModelFn(IntPtr ctx,IntPtr parts,uint count,uint old,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuFamilyFn(IntPtr ctx,IntPtr thresholds,uint count,IntPtr levels,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuSourceFn(IntPtr ctx,byte[] bytes,uint count,uint cube,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuActorFn(IntPtr ctx,ref NativeGpuSceneActor actor,IntPtr appearances,uint count,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuUpdatesFn(IntPtr ctx,IntPtr actors,uint count,IntPtr appearances,uint total);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuPublishFn(IntPtr ctx,uint views,uint references,uint draws);
    GpuModelFn gpuModel;GpuFamilyFn gpuFamily;GpuSourceFn gpuSource;GpuActorFn gpuActor;GpuUpdatesFn gpuUpdates;GpuPublishFn gpuPublish;
    GpuRefFrameFn gpuResidentFrame;
    public bool GpuSceneEnabled {get;}=Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_SCENE")=="1";
    public bool GpuSceneReady{get;private set;}
    readonly HashSet<(bool,uint)> gpuShaders=new();
    public bool HasGpuShader(bool old,uint id)=>gpuShaders.Contains((old,id));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuSupportedFn(IntPtr ctx,out uint supported);
    public bool GpuSceneSupported{get;private set;}
    public void ConfigureGpuScene(VulkanShaderLibrary library,string shaderDirectory){
        if(!GpuSceneEnabled||GpuSceneReady)return;Check(PassFunction<GpuSupportedFn>("sevk_gpu_scene_supported")(context,out uint supported),context);GpuSceneSupported=supported!=0;if(!GpuSceneSupported)return;using var compiler=VulkanShaderCompiler.CreateBundled(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"se-vulkan-gpu-stock"));using var batch=compiler.CreateBatch(shaderDirectory);
        foreach(var variant in NativePassShaderVariants.Materials){InitializeGpuStockPipeline(variant.Id,false,VulkanShaderArtifact.FromCompilation(batch.Compile(library.GpuOpaqueRequest(variant.Vertex)),"gpu-new-vs"),VulkanShaderArtifact.FromCompilation(batch.Compile(library.GpuOpaqueRequest(variant.Pixel)),"gpu-new-ps"));gpuShaders.Add((false,variant.Id));}
        foreach(var variant in WorldShaderVariants.Materials){try{var vs=batch.Compile(library.GpuOpaqueRequest(variant.Vertex));var ps=batch.Compile(library.GpuOpaqueRequest(variant.Pixel));InitializeGpuStockPipeline(variant.Id,true,VulkanShaderArtifact.FromCompilation(vs,"gpu-old-vs"),VulkanShaderArtifact.FromCompilation(ps,"gpu-old-ps"));gpuShaders.Add((true,variant.Id));}catch(System.IO.InvalidDataException){}}
        foreach(var spec in System.Linq.Enumerable.Concat(WorldShaderVariants.Extra,ProbeShaderVariants.Extra)) {
            uint id=spec.Id>=256 && spec.Id<278?spec.Id-192:spec.Id;
            if(WorldShaderVariants.WithoutDistanceFade(id)>=96)continue;
            try{var vs=batch.Compile(library.GpuOpaqueExtraRequest(spec.Id,spec.Material,"Vertex",spec.Defines));var ps=batch.Compile(library.GpuOpaqueExtraRequest(spec.Id,spec.Material,"Pixel",spec.Defines));InitializeGpuStockPipeline(id,true,VulkanShaderArtifact.FromCompilation(vs,"gpu-old-extra-vs"),VulkanShaderArtifact.FromCompilation(ps,"gpu-old-extra-ps"));gpuShaders.Add((true,id));}catch(System.IO.InvalidDataException){}
        }
        ConfigureIndexedMotion();GpuSceneReady=true;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuQueriesFn(IntPtr ctx,IntPtr views,uint count,IntPtr queries,uint capacity,out uint total);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuVisibilityFn(IntPtr ctx,IntPtr results,uint count);
    GpuQueriesFn gpuQueries;GpuVisibilityFn gpuVisibility;NativeFunctions.ContextFn gpuClear;
    public unsafe NativePassQuery[] GetGpuQueries(NativeGpuOpaqueView[] views){gpuQueries??=PassFunction<GpuQueriesFn>("sevk_gpu_scene_queries");fixed(NativeGpuOpaqueView* v=views){Check(gpuQueries(context,(IntPtr)v,(uint)views.Length,IntPtr.Zero,0,out uint total),context);var output=new NativePassQuery[total];fixed(NativePassQuery* q=output)Check(gpuQueries(context,(IntPtr)v,(uint)views.Length,(IntPtr)q,total,out total),context);return output;}}
    public unsafe void PublishGpuVisibility(NativePassQueryResult[] results){gpuVisibility??=PassFunction<GpuVisibilityFn>("sevk_gpu_scene_visibility");fixed(NativePassQueryResult* r=results)Check(gpuVisibility(context,(IntPtr)r,(uint)results.Length),context);}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuTopologyFn(IntPtr ctx,[Out]ulong[] counts,uint count);
    GpuTopologyFn gpuTopology;
    GpuTopologyFn gpuComputeTimes;
    GpuTopologyFn gpuCpuCosts;bool gpuCpuCostsMissing;
    public ulong[] GetGpuCpuCosts(){
        if(gpuCpuCostsMissing)return null;
        if(gpuCpuCosts==null){try{gpuCpuCosts=PassFunction<GpuTopologyFn>("sevk_gpu_scene_cpu_costs");}catch(EntryPointNotFoundException){gpuCpuCostsMissing=true;return null;}}
        var values=new ulong[28];Check(gpuCpuCosts(context,values,28),context);return values;
    }
    public ulong[] GetGpuComputeTimes(){gpuComputeTimes??=PassFunction<GpuTopologyFn>("sevk_gpu_scene_compute_times");var values=new ulong[11];Check(gpuComputeTimes(context,values,11),context);return values;}
    public ulong[] GetGpuTopologyCounts(){gpuTopology??=PassFunction<GpuTopologyFn>("sevk_gpu_scene_topology_counts");var counts=new ulong[7];Check(gpuTopology(context,counts,7),context);return counts;}
    public void ClearGpuScene(){gpuClear??=PassFunction<NativeFunctions.ContextFn>("sevk_gpu_scene_clear");Check(gpuClear(context),context);}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuSelectorFn(IntPtr ctx,ulong actor,out NativeGpuOpaqueSelector selector);
    GpuSelectorFn gpuSelector;PassRemoveFn gpuRemove;
    public NativeGpuOpaqueSelector GetGpuSelector(ulong actor){gpuSelector??=PassFunction<GpuSelectorFn>("sevk_gpu_scene_selector");Check(gpuSelector(context,actor,out var selector),context);return selector;}
    public void RemoveGpuResource(uint kind,ulong handle){gpuRemove??=PassFunction<PassRemoveFn>("sevk_gpu_scene_remove");Check(gpuRemove(context,kind,handle),context);}
    public unsafe void QueueGpuSceneFrame(ref NativeGpuOpaqueFrame frame,NativeGpuOpaqueView[] views){gpuResidentFrame??=PassFunction<GpuRefFrameFn>("sevk_gpu_scene_frame");fixed(NativeGpuOpaqueView* v=views)Check(gpuResidentFrame(context,ref frame,(IntPtr)v,(uint)views.Length),context);}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GpuModelOrderedFn(IntPtr context,IntPtr parts,IntPtr orders,uint count,uint old,out ulong handle);
    GpuModelOrderedFn gpuModelOrdered;
    public ulong RegisterGpuModel(NativePassPart[] parts,bool old)=>RegisterGpuModel(parts,old,null);
    public unsafe ulong RegisterGpuModel(NativePassPart[] parts,bool old,NativeOldPartOrder[] orders){
        if(orders!=null&&orders.Length!=parts.Length)throw new ArgumentException("GPU OLD identity count must match model parts");
        bool ordered=false;if(orders!=null)foreach(var row in orders)ordered|=row.Valid!=0;
        fixed(NativePassPart* p=parts){if(ordered){gpuModelOrdered??=PassFunction<GpuModelOrderedFn>("sevk_gpu_scene_model_ordered");fixed(NativeOldPartOrder* rows=orders){Check(gpuModelOrdered(context,(IntPtr)p,(IntPtr)rows,(uint)parts.Length,old?1u:0u,out ulong handle),context);return handle;}}
            gpuModel??=PassFunction<GpuModelFn>("sevk_gpu_scene_model");Check(gpuModel(context,(IntPtr)p,(uint)parts.Length,old?1u:0u,out ulong result),context);return result;}}
    public unsafe ulong RegisterGpuFamily(float[] thresholds,ulong[] levels){if(levels.Length!=thresholds.Length+1)throw new ArgumentException("GPU family requires every authored LOD");gpuFamily??=PassFunction<GpuFamilyFn>("sevk_gpu_scene_family");fixed(float* t=thresholds)fixed(ulong* l=levels){Check(gpuFamily(context,(IntPtr)t,(uint)thresholds.Length,(IntPtr)l,out ulong handle),context);return handle;}}
    public ulong RegisterGpuSource(byte[] bytes,bool cube){gpuSource??=PassFunction<GpuSourceFn>("sevk_gpu_scene_source");Check(gpuSource(context,bytes,(uint)bytes.Length,cube?1u:0u,out ulong handle),context);return handle;}
    public unsafe ulong RegisterGpuActor(ref NativeGpuSceneActor actor,NativeGpuOpaqueAppearance[] appearances){actor.Size=(uint)Marshal.SizeOf<NativeGpuSceneActor>();actor.AbiVersion=AbiVersion;gpuActor??=PassFunction<GpuActorFn>("sevk_gpu_scene_actor");fixed(NativeGpuOpaqueAppearance* a=appearances){Check(gpuActor(context,ref actor,(IntPtr)a,(uint)appearances.Length,out ulong handle),context);return handle;}}
    public unsafe void UpdateGpuActors(NativeGpuSceneActor[] actors,NativeGpuOpaqueAppearance[] appearances){gpuUpdates??=PassFunction<GpuUpdatesFn>("sevk_gpu_scene_updates");for(int i=0;i<actors.Length;i++){actors[i].Size=(uint)Marshal.SizeOf<NativeGpuSceneActor>();actors[i].AbiVersion=AbiVersion;}fixed(NativeGpuSceneActor* a=actors)fixed(NativeGpuOpaqueAppearance* p=appearances)Check(gpuUpdates(context,(IntPtr)a,(uint)actors.Length,(IntPtr)p,(uint)appearances.Length),context);}
    public void PublishGpuScene(uint viewCapacity,uint referenceCapacity,uint drawCapacity){gpuPublish??=PassFunction<GpuPublishFn>("sevk_gpu_scene_publish");Check(gpuPublish(context,viewCapacity,referenceCapacity,drawCapacity),context);}
}
