using System.Text.Json;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ClientPlugin.Rendering.Shaders;
string content=Path.GetFullPath(args.Length>0?args[0]:"T:/SteamLibrary/steamapps/common/SpaceEngineers/Content/Shaders"),output=Path.GetFullPath("TestResults/GpuOpaqueStockShaders");Directory.CreateDirectory(output);
var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),content);
using var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"Cache"));using var batch=compiler.CreateBatch(content);
var results=new List<object>();
var work=NativePassShaderVariants.Materials.SelectMany(s=>new[]{(Kind:"NEW",s.Id,Stage:"Vertex",Variant:s.Vertex),(Kind:"NEW",s.Id,Stage:"Pixel",Variant:s.Pixel)})
    .Concat(WorldShaderVariants.Materials.Where(s=>new uint[]{0,2,3,4,5,7,8,32,34,35,36,37,39,40}.Contains(s.Id)).SelectMany(s=>new[]{(Kind:"OLD",s.Id,Stage:"Vertex",Variant:s.Vertex),(Kind:"OLD",s.Id,Stage:"Pixel",Variant:s.Pixel)})).ToArray();
if(args.Length>1&&args[1]=="first")work=work.Take(2).ToArray();
foreach(var item in work){
    var request=library.GpuOpaqueRequest(item.Variant);var result=batch.Compile(request);
    if(result.Metadata.SourceTransform!=GpuOpaqueShaderAdapter.Contract||result.Metadata.BaseSpirvSha256!=request.GpuOpaqueBaseModule)throw new Exception("Derived source provenance missing");
    var descriptors=result.Metadata.Descriptors;
    if(descriptors.Any(d=>d.Set>1))throw new Exception("Indexed stock descriptor set exceeded contract");
    if(item.Stage=="Vertex"&&result.Metadata.Interface.Any(i=>i.Direction=="Input"&&new[]{"TEXCOORD1","TEXCOORD2","TEXCOORD3","TEXCOORD4","TEXCOORD5","TEXCOORD6","TEXCOORD7","TEXCOORD8","TEXCOORD9","TEXCOORD10"}.Contains(i.Name.Split('.').Last())))throw new Exception("Indexed stock variant still consumes CPU instance attributes: "+item+" "+string.Join(",",result.Metadata.Interface.Where(i=>i.Direction=="Input").Select(i=>i.Name+":"+i.Type)));
    if(args.Contains("--generation"))
    {
        string relative=Path.GetRelativePath(content,request.SourcePath).Replace('\\','/');
        byte[] bytes=System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(request.SourcePath)+"\n// captured pack source generation fixture\n");
        var generation=library.CaptureSourceGeneration("gpu-owned-generation-fixture",new Dictionary<string,byte[]>{{relative,bytes}});
        var derivedRequest=library.DerivedRequest(item.Variant,generation);
        derivedRequest.GpuOpaque=true;derivedRequest.GpuOpaqueBaseModule=request.GpuOpaqueBaseModule;derivedRequest.GpuOpaqueBaseDependencies=request.GpuOpaqueBaseDependencies;
        var derived=compiler.Compile(derivedRequest);
        if(derived.Metadata.SourceTransform!=GpuOpaqueShaderAdapter.Contract || derived.Metadata.BaseSpirvSha256!=request.GpuOpaqueBaseModule || derived.Metadata.SourceGenerationSha256!=generation.Fingerprint ||
            JsonSerializer.Serialize(derived.Metadata.Descriptors)!=JsonSerializer.Serialize(result.Metadata.Descriptors) || JsonSerializer.Serialize(derived.Metadata.Interface)!=JsonSerializer.Serialize(result.Metadata.Interface))
            throw new Exception("Captured source generation changed the GPU-owned resource/interface contract or lost stock provenance");
        if(derived.Metadata.CacheKey==result.Metadata.CacheKey)throw new Exception("Captured source generation reused the stock generation cache identity");
        Console.WriteLine($"PASS {item.Kind} {item.Id} {item.Stage} captured active-source generation retains stock provenance and indexed interfaces; same SPIR-V={derived.Metadata.SpirvSha256==result.Metadata.SpirvSha256}");
    }
    results.Add(new{item.Kind,item.Id,item.Stage,item.Variant,result.SpirvPath,result.MetadataPath,result.Metadata.SpirvSha256,result.Metadata.BaseSpirvSha256,result.Metadata.SourceTransform,result.Metadata.Capabilities,Descriptors=descriptors,Interface=result.Metadata.Interface});
    Console.WriteLine($"PASS {item.Kind} {item.Id} {item.Stage} authenticated indexed stock inputs");
}
batch.VerifyBatchToolchain();File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Validated {results.Count} authenticated GPU opaque shader stages; raster parity pending");
