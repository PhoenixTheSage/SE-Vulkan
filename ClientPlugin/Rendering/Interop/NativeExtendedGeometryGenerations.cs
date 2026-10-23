using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    [StructLayout(LayoutKind.Sequential,Pack=8)]struct OwnerCapabilities{public uint Size,Abi;public ulong Flags;public uint MaxModules,MaxBindings;}
    [StructLayout(LayoutKind.Sequential,Pack=8)]struct NativeGeneration{public uint Size,Abi;public ulong Revision;public uint Flags,Count;public IntPtr Modules;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int OwnerFeaturesFn(IntPtr ctx,ref OwnerCapabilities value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PrepareNativeFn(IntPtr ctx,ref NativeGeneration value,out ulong token);
    OwnerFeaturesFn ownerFeatures;PrepareNativeFn prepareNative;bool ownerFeaturesMissing;
    public ulong NativeOwnerFeatures(){
        if(context==IntPtr.Zero||ownerFeaturesMissing)return 0;
        try{ownerFeatures??=PostFunction<OwnerFeaturesFn>("sevk_native_owner_capabilities");}catch(EntryPointNotFoundException){ownerFeaturesMissing=true;return 0;}
        var value=new OwnerCapabilities{Size=24,Abi=AbiVersion};Check(ownerFeatures(context,ref value),context);if(value.Size!=24||value.Abi!=AbiVersion||value.MaxModules<8192)throw new NotSupportedException("Update SE-Vulkan: native owner capability transport is incompatible.");return value.Flags;
    }
    static bool CellModule(uint id)=>id>=16384&&id<16440;
    static bool GpuNewModule(uint id)=>id>=32768&&id<32824;
    static bool GpuOldModule(uint id)=>id>=49152&&id<54464;
    static bool ExtendedGeometryModule(uint id)=>CellModule(id)||GpuNewModule(id)||GpuOldModule(id);
    static ulong ExtendedGeometryFeature(uint id)=>CellModule(id)?1UL:GpuNewModule(id)?2UL:4UL;
    VulkanShaderArtifact ExtendedGeometryArtifact(uint id){
        uint pipeline=(id-(CellModule(id)?16384u:GpuNewModule(id)?32768u:49152u))/2;
        if(GpuOldModule(id))return GeometryArtifact(4096+2*pipeline+(id&1));
        var spec=NativePassShaderVariants.Materials.Single(v=>v.Id==pipeline);return postprocessLibrary.Load((id&1)==0?spec.Vertex:spec.Pixel);
    }
    object[][] ExtendedGeometryCatalog(){
        ulong features=NativeOwnerFeatures();var ids=new List<uint>();
        if((features&1)!=0&&NativePassReady)foreach(var s in NativePassShaderVariants.Materials)ids.AddRange(new[]{16384+2*s.Id,16385+2*s.Id});
        if((features&2)!=0)foreach(var s in NativePassShaderVariants.Materials)if(HasGpuShader(false,s.Id))ids.AddRange(new[]{32768+2*s.Id,32769+2*s.Id});
        if((features&4)!=0)foreach(var s in GeometryOwners.Values)if(HasGpuShader(true,s.Pipeline))ids.AddRange(new[]{49152+2*s.Pipeline,49153+2*s.Pipeline});
        return ids.Select(id=>{var artifact=ExtendedGeometryArtifact(id);uint pipeline=(id-(CellModule(id)?16384u:GpuNewModule(id)?32768u:49152u))/2;string pass=GpuOldModule(id)?GeometryOwners[pipeline].Pass:pipeline<12?(pipeline<4?"GBuffer":pipeline<8?"Depth":"Forward"):"GBuffer";string key="Geometry/Passes/"+pass+((id&1)==0?"/VertexStage.hlsli":"/PixelStage.hlsli");var dependencies=artifact.Layout.Dependencies.Select(d=>d.Path.Replace('\\','/')).Where(d=>d.StartsWith("root0/",StringComparison.OrdinalIgnoreCase)).Select(d=>d.Substring(6)).Append(key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d=>d,StringComparer.Ordinal).ToArray();return new object[]{id,key,(id&1)==0?"vs_5_0":"ps_5_0",dependencies};}).ToArray();
    }
    VulkanShaderRequest ExtendedGeometryRequest(uint id,ShaderSourceGeneration generation,Dictionary<string,string> defines,bool velocity=false){
        if((NativeOwnerFeatures()&ExtendedGeometryFeature(id))==0)throw new NotSupportedException("Update coordinated SE-Vulkan: requested native-cell/GPU owner generation capability unavailable.");
        uint pipeline=(id-(CellModule(id)?16384u:GpuNewModule(id)?32768u:49152u))/2;bool pixel=(id&1)!=0;VulkanShaderRequest request;
        // Native-cell and GPU NEW GBuffer owners take DX11's USE_SIMPLE_INSTANCING velocity contract (t15).
        if(velocity&&(CellModule(id)||GpuNewModule(id))&&(pipeline<4||pipeline>=12)&&postprocessLibrary.DerivedRequest(NativePassShaderVariants.Materials.Single(v=>v.Id==pipeline).Vertex,generation,defines).Defines.ContainsKey("USE_SIMPLE_INSTANCING"))defines=new Dictionary<string,string>(defines,StringComparer.Ordinal){["ANOMALY_VELOCITY"]="1"};
        // GPU OLD owners keep the classic per-draw currToPrev contract; the adapter derives it per indirect draw.
        if(velocity&&GpuOldModule(id)&&GeometryOwners[pipeline].Vertex!=null&&GBufferVelocityOwner(pipeline))defines=new Dictionary<string,string>(defines,StringComparer.Ordinal){["ANOMALY_VELOCITY"]="1"};
        if(GpuOldModule(id)){var spec=GeometryOwners[pipeline];request=spec.Vertex!=null?postprocessLibrary.DerivedRequest(pixel?spec.Pixel:spec.Vertex,generation,defines):postprocessLibrary.DerivedExtraRequest(spec.Extra,spec.Material,pixel?"Pixel":"Vertex",spec.Defines,generation,defines);}
        else{var spec=NativePassShaderVariants.Materials.Single(v=>v.Id==pipeline);request=postprocessLibrary.DerivedRequest(pixel?spec.Pixel:spec.Vertex,generation,defines);}
        if(!CellModule(id))postprocessLibrary.EnableGpuOpaqueDerivation(request,ExtendedGeometryArtifact(id));return request;
    }
}
