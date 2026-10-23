using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    internal const int GeometryGenerationFeature=unchecked((int)0x80000000);
    sealed class GeometryOwnerSpec
    {
        internal uint Pipeline,Extra;internal string Vertex,Pixel,Material;internal Dictionary<string,string> Defines;
        internal string Pass {get{uint id=WorldShaderVariants.WithoutDistanceFade(Pipeline);return id<32||WorldShaderVariants.IsBlendedDecal(id)?"GBuffer":id<64?"Depth":id<96?"Forward":"Highlight";}}
    }
    static readonly Dictionary<uint,GeometryOwnerSpec> GeometryOwners=BuildGeometryOwners();
    static Dictionary<uint,GeometryOwnerSpec> BuildGeometryOwners()
    {
        var result=new Dictionary<uint,GeometryOwnerSpec>();
        foreach(var s in WorldShaderVariants.Materials)result.Add(s.Id,new GeometryOwnerSpec{Pipeline=s.Id,Vertex=s.Vertex,Pixel=s.Pixel});
        // Voxel GBuffer (20, 21) and depth (52, 53) come from the voxel library but build as world pipelines.
        foreach(var s in VoxelShaderVariants.Materials)if(!result.ContainsKey(s.Id))result.Add(s.Id,new GeometryOwnerSpec{Pipeline=s.Id,Vertex=s.Vertex,Pixel=s.Pixel});
        void Extras(IEnumerable<(uint Id,string Material,Dictionary<string,string> Defines)> specs,uint offset){foreach(var s in specs){uint id=s.Id-offset;result.Add(id,new GeometryOwnerSpec{Pipeline=id,Extra=s.Id,Material=s.Material,Defines=s.Defines});}}
        Extras(WorldShaderVariants.Extra,0);Extras(ProbeShaderVariants.Extra,192);Extras(HighlightShaderVariants.Extra,416);return result;
    }
    internal static bool StockGeometryObjectTint(uint pipeline)=>pipeline<20&&pipeline%5==2||pipeline>=64&&pipeline<84&&(pipeline-64)%5==2;
    static bool GeometryModule(uint id)=>id>=4096&&id<10912;
    VulkanShaderArtifact GeometryArtifact(uint id)
    {
        var spec=GeometryOwners[(id-4096)/2];bool pixel=(id&1)!=0;
        return spec.Vertex!=null?postprocessLibrary.Load(pixel?spec.Pixel:spec.Vertex):postprocessLibrary.LoadExtra(spec.Extra,spec.Material,pixel?"Pixel":"Vertex",spec.Defines);
    }
    object[][] GeometryCatalog()
    {
        if((GuestFeatures()&GeometryGenerationFeature)==0)return Array.Empty<object[]>();
        return GeometryOwners.Values.OrderBy(s=>s.Pipeline).SelectMany(spec=>new uint[]{4096+2*spec.Pipeline,4097+2*spec.Pipeline}).Select(id=>{
            var artifact=GeometryArtifact(id);var spec=GeometryOwners[(id-4096)/2];string key="Geometry/Passes/"+spec.Pass+((id&1)==0?"/VertexStage.hlsli":"/PixelStage.hlsli");
            var dependencies=artifact.Layout.Dependencies.Select(d=>d.Path.Replace('\\','/')).Where(d=>d.StartsWith("root0/",StringComparison.OrdinalIgnoreCase)).Select(d=>d.Substring(6)).Append(key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d=>d,StringComparer.Ordinal).ToArray();
            return new object[]{id,key,artifact.Layout.EntryPoint=="__vertex_shader"?"vs_5_0":"ps_5_0",dependencies};
        }).ToArray();
    }
    // Native owner extension flag8: classic GBuffer owners emit DX11's velocity contract in set 2,
    // including fade-bank variants, blended static decals and voxels (20, 21; static, so their rows give camera-only motion).
    internal const ulong GBufferVelocityFeature=8;
    internal static bool GBufferVelocityOwner(uint pipeline)=>WorldShaderVariants.WithoutDistanceFade(pipeline)<22||WorldShaderVariants.IsBlendedDecal(pipeline);
    VulkanShaderRequest GeometryRequest(uint id,string identity,Dictionary<string,byte[]> sources,Dictionary<string,string> defines,bool velocity=false)
    {
        if((GuestFeatures()&GeometryGenerationFeature)==0)throw new NotSupportedException("Update SE-Vulkan native library: stock world geometry generations require bit31.");
        var spec=GeometryOwners[(id-4096)/2];bool pixel=(id&1)!=0;var original=GeometryArtifact(id);
        // Match the installed owner selection exactly: fade-bank variants do not use this adaptation.
        uint pipeline=spec.Pipeline;
        bool tint=pixel&&StockGeometryObjectTint(pipeline);
        var inputs=sources;
        if(tint){
            postprocessLibrary.LoadObjectTintPixel(spec.Pipeline,original); // Authenticate the installed native stock adaptation.
            inputs=new Dictionary<string,byte[]>(sources,StringComparer.OrdinalIgnoreCase);
            foreach(string key in new[]{"Geometry/PixelTemplateBase.hlsli","_anomaly/stock/Geometry/PixelTemplateBase.hlsli"})
                if(inputs.TryGetValue(key,out var bytes))inputs[key]=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("pix.color_mul = 1;","pix.color_mul = object_.color_mul;"));
        }
        var generation=postprocessLibrary.CaptureSourceGeneration(identity+(tint?"/ObjectTint-1":""),inputs);
        // ANOMALY_VELOCITY is GBuffer-only on DX11; Depth/Forward/Highlight never see it.
        if(velocity&&GBufferVelocityOwner(pipeline))defines=new Dictionary<string,string>(defines,StringComparer.Ordinal){["ANOMALY_VELOCITY"]="1"};
        if(spec.Vertex!=null)return postprocessLibrary.DerivedRequest(pixel?spec.Pixel:spec.Vertex,generation,defines);
        return postprocessLibrary.DerivedExtraRequest(spec.Extra,spec.Material,pixel?"Pixel":"Vertex",spec.Defines,generation,defines);
    }
}
