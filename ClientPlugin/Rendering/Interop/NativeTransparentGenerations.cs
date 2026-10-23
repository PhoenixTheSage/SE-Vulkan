using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    // Native owner extension flag16: stock glass/holo owners. 2304+2*i is owner i's VS, 2305+2*i its PS;
    // i<10 is OIT glass pipeline i (Transparent), i>=10 the decal-depth glass pipeline 50+i (TransparentForDecals).
    internal const ulong TransparentGenerationFeature=16;
    static bool TransparentModule(uint id)=>id>=2304&&id<2344;
    static uint TransparentPipeline(uint id){uint i=(id-2304)/2;return i<10?i:50+i;}
    sealed class TransparentOwnerSpec{internal string Vertex,Pixel,Material;internal uint Extra;internal Dictionary<string,string> Defines;}
    static readonly Dictionary<uint,TransparentOwnerSpec> TransparentOwners=BuildTransparentOwners();
    static Dictionary<uint,TransparentOwnerSpec> BuildTransparentOwners()
    {
        var result=TransparencyShaderVariants.Glass.ToDictionary(s=>s.Id,s=>new TransparentOwnerSpec{Vertex=s.Vertex,Pixel=s.Pixel});
        // Same pipeline assignment as ConfigureTransparency.
        foreach(var s in TransparencyShaderVariants.Extra)result.Add(s.Id>=90?s.Id-90+65:s.Id>=80?s.Id-80+5:s.Id>=70?s.Id-70+60:s.Id-64,new TransparentOwnerSpec{Extra=s.Id,Material=s.Material,Defines=s.Defines});
        return result;
    }
    VulkanShaderArtifact TransparentArtifact(uint id)
    {
        var spec=TransparentOwners[TransparentPipeline(id)];bool pixel=(id&1)!=0;
        return spec.Vertex!=null?postprocessLibrary.Load(pixel?spec.Pixel:spec.Vertex):postprocessLibrary.LoadExtra(spec.Extra,spec.Material,pixel?"Pixel":"Vertex",spec.Defines);
    }
    object[][] TransparentCatalog()
    {
        if((NativeOwnerFeatures()&TransparentGenerationFeature)==0)return Array.Empty<object[]>();
        return Enumerable.Range(2304,40).Select(v=>(uint)v).Where(id=>TransparentOwners.ContainsKey(TransparentPipeline(id))).Select(id=>{
            var artifact=TransparentArtifact(id);string key="Geometry/Passes/"+(id<2324?"Transparent":"TransparentForDecals")+((id&1)==0?"/VertexStage.hlsli":"/PixelStage.hlsli");
            var dependencies=artifact.Layout.Dependencies.Select(d=>d.Path.Replace('\\','/')).Where(d=>d.StartsWith("root0/",StringComparison.OrdinalIgnoreCase)).Select(d=>d.Substring(6)).Append(key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d=>d,StringComparer.Ordinal).ToArray();
            return new object[]{id,key,(id&1)==0?"vs_5_0":"ps_5_0",dependencies};
        }).ToArray();
    }
    VulkanShaderRequest TransparentRequest(uint id,ShaderSourceGeneration generation,Dictionary<string,string> defines)
    {
        if((NativeOwnerFeatures()&TransparentGenerationFeature)==0)throw new NotSupportedException("Update coordinated SE-Vulkan: transparent glass owner generations require native owner flag16.");
        var spec=TransparentOwners[TransparentPipeline(id)];bool pixel=(id&1)!=0;
        return spec.Vertex!=null?postprocessLibrary.DerivedRequest(pixel?spec.Pixel:spec.Vertex,generation,defines):postprocessLibrary.DerivedExtraRequest(spec.Extra,spec.Material,pixel?"Pixel":"Vertex",spec.Defines,generation,defines);
    }
}
