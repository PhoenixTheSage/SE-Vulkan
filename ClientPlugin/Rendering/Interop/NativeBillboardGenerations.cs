using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    // Native owner extension flag32: stock Transparent/Billboards.hlsl owners. Variant k (stock id 20+k) is the
    // VS/PS pair 2560+2*k/2561+2*k; 2624 is the LDR vertex shader of the even additive LDR and PostPP programs.
    internal const ulong BillboardGenerationFeature=32;
    internal const string BillboardLdrVertex="a1b699b3ad82adec90ba1af8318067d3410d117c5c94b9b424560f40e2fdf7b4";
    static bool BillboardModule(uint id)=>id>=2560&&id<2625;
    static string BillboardVariant(uint id)
    {
        if(id==2624)return BillboardLdrVertex;
        var spec=TransparencyShaderVariants.Billboards.Single(s=>s.Id==20+(id-2560)/2);return (id&1)==0?spec.Vertex:spec.Pixel;
    }
    object[][] BillboardCatalog()
    {
        if((NativeOwnerFeatures()&BillboardGenerationFeature)==0)return Array.Empty<object[]>();
        return Enumerable.Range(2560,65).Select(v=>(uint)v).Select(id=>{
            var artifact=postprocessLibrary.Load(BillboardVariant(id));
            var dependencies=artifact.Layout.Dependencies.Select(d=>d.Path.Replace('\\','/')).Where(d=>d.StartsWith("root0/",StringComparison.OrdinalIgnoreCase)).Select(d=>d.Substring(6)).Append(artifact.Source).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d=>d,StringComparer.Ordinal).ToArray();
            return new object[]{id,artifact.Source,artifact.Profile,dependencies};
        }).ToArray();
    }
    VulkanShaderRequest BillboardRequest(uint id,ShaderSourceGeneration generation,Dictionary<string,string> defines)
    {
        if((NativeOwnerFeatures()&BillboardGenerationFeature)==0)throw new NotSupportedException("Update coordinated SE-Vulkan: billboard owner generations require native owner flag32.");
        return postprocessLibrary.DerivedRequest(BillboardVariant(id),generation,defines);
    }
}
