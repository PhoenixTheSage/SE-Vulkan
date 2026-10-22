using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClientPlugin.Rendering.Shaders;

// Changes only stock input/resource addressing on the compiler's immutable
// preprocessed snapshot. The material, depth and lighting programs stay stock.
public static class GpuOpaqueShaderAdapter
{
    public const string Contract="GpuOpaqueIndexedStock-4;captured-stage-replacements";
    private const string Varying="nointerpolation uint sevk_draw : SEVK_DRAW;";
    private static string Once(string source,string pattern,Func<Match,string> replacement)
    {
        var regex=new Regex(pattern,RegexOptions.Singleline|RegexOptions.CultureInvariant);
        if(regex.Matches(source).Count!=1)throw new InvalidDataException("GPU opaque stock anchor changed: "+pattern);
        return regex.Replace(source,m=>replacement(m),1);
    }
    public static string Apply(string source,VulkanShaderRequest request,List<ShaderDependency> dependencies)
    {
        if(!request.GpuOpaque)return source;
        if(request.GpuOpaqueBaseModule==null||!Regex.IsMatch(request.GpuOpaqueBaseModule,"^[0-9a-f]{64}$")||request.GpuOpaqueBaseDependencies==null)
            throw new InvalidDataException("GPU opaque derivation needs an authenticated stock module and source dependency set.");
        var expected=request.GpuOpaqueBaseDependencies.ToDictionary(d=>d.Path,d=>d.Sha256,StringComparer.Ordinal);
        // The stock catalog deduplicates identical SPIR-V across material source
        // paths. Module metadata can name AlphaMasked for a Standard variant.
        // Authenticate the actual snapshot against the catalog's SOURCE table,
        // not that unrelated deduplicated module's original dependency paths.
        if(dependencies.Any(d=>!expected.TryGetValue(d.Path,out string hash)||hash!=d.Sha256))
            throw new InvalidDataException("GPU opaque stock source differs from its authenticated catalog snapshot. Expected "+expected.Count+", actual "+dependencies.Count+". "+string.Join("; ",dependencies.Where(d=>!expected.TryGetValue(d.Path,out string h)||h!=d.Sha256).Select(d=>d.Path+"="+d.Sha256))+" Missing: "+string.Join(",",expected.Keys.Except(dependencies.Select(d=>d.Path))));
        var defines=request.Defines;bool Has(string name)=>defines.ContainsKey(name);
        if(Has("USE_SKINNING")||Has("USE_VOXEL_DATA")||Has("USE_MERGE_INSTANCING")||Has("USE_TEXTURE_INDICES"))
            throw new InvalidDataException("Unsupported GPU opaque source contract; retain its explicit fallback.");
        bool simple=Has("USE_SIMPLE_INSTANCING"),generic=Has("USE_GENERIC_INSTANCING"),cube=Has("USE_CUBE_INSTANCING")||Has("USE_DEFORMED_CUBE_INSTANCING");
        if((simple?1:0)+(generic?1:0)+(cube?1:0)>1)throw new InvalidDataException("Ambiguous GPU opaque instance contract.");
        bool vertex=request.EntryPoint=="__vertex_shader",pixel=request.EntryPoint=="__pixel_shader";
        if(!vertex&&!pixel)throw new InvalidDataException("GPU opaque adaptation only accepts stock geometry vertex/pixel stages.");
        string header=@"
struct SevkGpuDraw {uint object_byte_offset,material,source,reserved;uint4 textures;};
[[vk::binding(128,1)]] ByteAddressBuffer sevk_instances;
[[vk::binding(129,1)]] ByteAddressBuffer sevk_objects;
[[vk::binding(130,1)]] ByteAddressBuffer sevk_sources;
[[vk::binding(131,1)]] StructuredBuffer<SevkGpuDraw> sevk_draws;
[[vk::binding(132,1)]] Texture2D<float4> sevk_textures[];
[[vk::binding(133,1)]] Texture2D<float> sevk_masks[];
struct SevkGpuPush {uint draw_base;};
[[vk::push_constant]] ConstantBuffer<SevkGpuPush> sevk_push;
static uint4 sevk_texture_indices;
static ObjectConstants object_;
float4 sevk_half4(uint2 p){return float4(f16tof32(p.x&65535),f16tof32(p.x>>16),f16tof32(p.y&65535),f16tof32(p.y>>16));}
float4 sevk_unorm4(uint p){return float4(p&255,(p>>8)&255,(p>>16)&255,p>>24)/255.0;}
void sevk_object(uint draw_index){
    SevkGpuDraw d=sevk_draws[draw_index];sevk_texture_indices=d.textures;uint at=d.object_byte_offset;
    object_.facing=asfloat(sevk_objects.Load(at));object_.windScaleAndFreq=asfloat(sevk_objects.Load2(at+4));
    object_.foliage_multiplier=asfloat(sevk_objects.Load(at+12));object_.CenterOffset=asfloat(sevk_objects.Load3(at+16));object_.__paddingNonVoxelData2=asfloat(sevk_objects.Load(at+28));
    object_.matrix_row0=asfloat(sevk_objects.Load4(at+32));object_.matrix_row1=asfloat(sevk_objects.Load4(at+48));object_.matrix_row2=asfloat(sevk_objects.Load4(at+64));
    object_.key_color=asfloat(sevk_objects.Load3(at+80));object_.LOD=sevk_objects.Load(at+92);
    object_.color_mul=asfloat(sevk_objects.Load3(at+96));object_.emissive=asfloat(sevk_objects.Load(at+108));
    object_.custom_alpha=asfloat(sevk_objects.Load2(at+112));object_.depth_bias=sevk_objects.Load(at+120);object_.material_flags=sevk_objects.Load(at+124);
}
";
        source=Once(source,@"cbuffer\s+Object\s*:\s*register\s*\(\s*b2\s*\)\s*\{\s*ObjectConstants\s+object_\s*;\s*\}\s*;",_=>header);
        // Keep scalar alpha sampling scalar. Array texture consumers remain on
        // fallback; they are not quietly flattened into two-dimensional images.
        source=Regex.Replace(source,@"Texture2D\s*<\s*(float4|float)\s*>\s+(ColorMetalTexture|NormalGlossTexture|ExtensionsTexture|AlphamaskTexture)\s*:\s*register\s*\(\s*t([0-3])\s*\)\s*;",m=>{
            bool mask=m.Groups[2].Value=="AlphamaskTexture";
            if((mask&&m.Groups[1].Value!="float")||(!mask&&m.Groups[1].Value!="float4"))throw new InvalidDataException("Stock material resource type changed.");
            return "#define "+m.Groups[2].Value+" "+(mask?"sevk_masks":"sevk_textures")+"[NonUniformResourceIndex(sevk_texture_indices["+m.Groups[3].Value+"])]\n";
        });
        // Supply the same leading flat semantic to both stages. No stock payload
        // field, interpolation rule or material math is altered.
        string structure=vertex?"VertexStageOutput":Regex.IsMatch(source,@"struct\s+PixelStageInput\b")?"PixelStageInput":Regex.IsMatch(source,@"struct\s+VertexStageOutput\b")?"VertexStageOutput":null;
        if(structure!=null)source=Once(source,@"struct\s+"+structure+@"\s*\{",m=>m.Value+"\n"+Varying+"\n");
        if(vertex){
            string inputs="";
            if(simple){int stride=Has("USE_SIMPLE_INSTANCING_COLORING")?64:48;
                inputs="uint sevk_at=sevk_instance*"+stride+"; input.matrix_row0=asfloat(sevk_instances.Load4(sevk_at)); input.matrix_row1=asfloat(sevk_instances.Load4(sevk_at+16)); input.matrix_row2=asfloat(sevk_instances.Load4(sevk_at+32));";
                if(stride==64)inputs+="input.instance_keyColorDithering=sevk_half4(sevk_instances.Load2(sevk_at+48));input.instance_colorMultEmissivity=sevk_half4(sevk_instances.Load2(sevk_at+56));";
            }else if(generic)inputs="uint sevk_at=sevk_instance*64;input.matrix_row0=asfloat(sevk_sources.Load4(sevk_at));input.matrix_row1=asfloat(sevk_sources.Load4(sevk_at+16));input.matrix_row2=asfloat(sevk_sources.Load4(sevk_at+32));input.colormask=asfloat(sevk_sources.Load4(sevk_at+48));";
            else if(cube){inputs="uint sevk_at=sevk_instance*64; input.cube_transformation=asfloat(sevk_sources.Load4(sevk_at+32));input.colormask=asfloat(sevk_sources.Load4(sevk_at+48));";
                for(int i=0;i<8;i++)inputs+="input.packed_bone"+i+"=sevk_unorm4(sevk_sources.Load(sevk_at+"+(i*4)+"));";
            }
            source=Once(source,@"void\s+__vertex_shader\s*\(([^)]*)\)\s*\{",m=>"void __vertex_shader("+m.Groups[1].Value+",uint sevk_instance:SV_InstanceID,[[vk::builtin(\"DrawIndex\")]] uint sevk_draw_index:SEVK_DRAW_INDEX){\nuint sevk_index=sevk_push.draw_base+sevk_draw_index;sevk_object(sevk_index);output.sevk_draw=sevk_index;"+inputs+"\n");
        }else{
            if(structure!=null)source=Once(source,@"void\s+__pixel_shader\s*\(([^)]*)\)\s*\{",m=>m.Value+"\nsevk_object(input.sevk_draw);\n");
            else source=Once(source,@"([A-Za-z_]\w*\s+__pixel_shader\s*\()([^)]*)(\)\s*(?::[^{}]+)?\s*\{)",m=>m.Groups[1].Value+"nointerpolation uint sevk_direct_draw:SEVK_DRAW"+(string.IsNullOrWhiteSpace(m.Groups[2].Value)?"":","+m.Groups[2].Value)+m.Groups[3].Value+"\nsevk_object(sevk_direct_draw);\n");
            // GPU generic variants represent genuine source instances. Keep
            // stock color_mul initialization; rigid batching's derived tint
            // shader is a separate fallback optimization.
        }
        return source;
    }
}
