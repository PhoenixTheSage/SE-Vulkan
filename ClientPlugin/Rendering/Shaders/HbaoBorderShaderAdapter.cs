using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClientPlugin.Rendering.Shaders;

// Core Vulkan has no float.MinValue sampler border. Preserve the stock fetch
// result explicitly, without changing its in-range sample or AO computation.
internal static class HbaoBorderShaderAdapter
{
    internal const string Contract="HbaoNegativeBorder-1";
    internal static string Apply(string source,VulkanShaderRequest request,List<ShaderDependency> dependencies){
        if(!request.HbaoNegativeBorder)return source;
        if(request.GpuOpaque || request.Profile!="ps_5_0" || request.EntryPoint!="__pixel_shader" ||
            request.HbaoBaseModule==null || !Regex.IsMatch(request.HbaoBaseModule,"^[a-f0-9]{64}$") || request.HbaoBaseDependencies==null ||
            !request.SourcePath.Replace('\\','/').EndsWith("/Postprocess/HBAO/CoarseAO.hlsl",StringComparison.Ordinal))
            throw new InvalidDataException("HBAO border adaptation requires the authenticated coarse AO pixel stage.");
        var expected=request.HbaoBaseDependencies.ToDictionary(d=>d.Path,d=>d.Sha256,StringComparer.Ordinal);
        if(dependencies.Any(d=>!expected.TryGetValue(d.Path,out var hash)||hash!=d.Sha256))
            throw new InvalidDataException("HBAO border source differs from the stock catalog snapshot.");
        const string fetch=@"float\s+ViewDepth\s*=\s*QuarterResDepthTexture\.SampleLevel\(\s*PointClampSampler\s*,\s*float3\(\s*UV\s*,\s*fSliceIndex\s*\)\s*,\s*0\s*\)\s*;";
        if(Regex.Matches(source,fetch).Count!=1)throw new InvalidDataException("Stock HBAO depth fetch changed.");
        return Regex.Replace(source,fetch,"float ViewDepth = (any(UV < 0.0f) || any(UV >= 1.0f)) ? -3.402823466e+38f : QuarterResDepthTexture.SampleLevel(PointClampSampler, float3(UV,fSliceIndex), 0);");
    }
}
