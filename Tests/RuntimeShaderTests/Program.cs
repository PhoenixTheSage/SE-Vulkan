using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Serialization;
using System.Xml.Linq;
using ClientPlugin.Rendering.Shaders;

if (args.Length != 3) throw new ArgumentException("RuntimeShaderTests <module-directory> <spirv-val> <output-directory>");
string output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
var metadataByModule=new Dictionary<string,string>();string supplement=Path.Combine(args[0],"manifest.xml");
if(File.Exists(supplement)){
    var manifest=XDocument.Load(supplement);var shaders=manifest.Root.Elements("Shader").ToArray();
    if(manifest.Root.Name!="WorldShaders"||(int)manifest.Root.Attribute("Count")!=shaders.Length)throw new InvalidDataException("Invalid world supplement manifest.");
    foreach(var shader in shaders){string module=(string)shader.Attribute("Module"),metadata=(string)shader.Attribute("Metadata");
        if(module.Length!=64||metadata.Length!=64||!module.All(Uri.IsHexDigit)||!metadata.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid supplement artifact hash.");
        string path=Path.Combine(args[0],metadata+".xml");if(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))!=metadata)throw new InvalidDataException("Supplement metadata hash mismatch.");
        metadataByModule.TryAdd(Path.GetFullPath(Path.Combine(args[0],module+".spv")),path);
    }
}
var modules=metadataByModule.Count>0?metadataByModule.Keys.ToArray():Directory.GetFiles(args[0],"*.spv");if(modules.Length==0)throw new InvalidOperationException("No catalog modules.");
var failures=new ConcurrentBag<string>();int done=0,cubeAdapted=0;
object Layout(ShaderMetadata m)=>new {m.ExecutionModel,m.WorkgroupSizeX,m.WorkgroupSizeY,m.WorkgroupSizeZ,m.Capabilities,m.Interface,m.PushConstants,Descriptors=m.Descriptors.Select(d=>new {d.Name,d.Set,d.Binding,d.DescriptorType,d.DescriptorCount,d.RuntimeArray,d.RegisterClass,d.RegisterIndex,d.BlockSize,d.Members})};
Parallel.ForEach(modules,new ParallelOptions{MaxDegreeOfParallelism=8},source=>{
    try {
        byte[] original=File.ReadAllBytes(source),runtime=SpirvRuntimeModule.Prepare(original);
        if(Convert.ToHexStringLower(SHA256.HashData(original))!=Path.GetFileNameWithoutExtension(source))throw new InvalidDataException("Catalog binary hash mismatch");
        if(!runtime.SequenceEqual(SpirvRuntimeModule.Prepare(runtime)))throw new InvalidDataException("Runtime preparation is not idempotent");
        ShaderMetadata recorded;using(var file=File.OpenRead(metadataByModule.TryGetValue(Path.GetFullPath(source),out var metadata)?metadata:Path.ChangeExtension(source,".xml")))recorded=(ShaderMetadata)new XmlSerializer(typeof(ShaderMetadata)).Deserialize(file);
        var before=new ShaderMetadata{EntryPoint=recorded.EntryPoint};var after=new ShaderMetadata{EntryPoint=recorded.EntryPoint};SpirvMetadataReader.Reflect(original,before);SpirvMetadataReader.Reflect(runtime,after);
        if(JsonSerializer.Serialize(Layout(before))!=JsonSerializer.Serialize(Layout(after)))throw new InvalidDataException("Runtime preparation changed executable interfaces/layouts/capabilities");
        if(after.Extensions.Any(e=>e=="SPV_GOOGLE_hlsl_functionality1" || e=="SPV_GOOGLE_user_type") || !before.Extensions.Where(e=>e!="SPV_GOOGLE_hlsl_functionality1" && e!="SPV_GOOGLE_user_type").SequenceEqual(after.Extensions))throw new InvalidDataException("Runtime extension filtering mismatch");
        byte[] cube=Dx11CubeLodAdapter.Apply(runtime);
        if(!cube.SequenceEqual(Dx11CubeLodAdapter.Apply(cube)))throw new InvalidDataException("Cube-LOD adaptation is not idempotent");
        if(!cube.SequenceEqual(runtime)){
            var adapted=new ShaderMetadata{EntryPoint=recorded.EntryPoint};SpirvMetadataReader.Reflect(cube,adapted);
            bool hadLinear=after.Descriptors.Any(d=>d.Set==0 && d.Binding==258);
            var kept=adapted.Descriptors.Where(d=>hadLinear || d.Set!=0 || d.Binding!=258).ToArray();
            if(JsonSerializer.Serialize(after.Descriptors)!=JsonSerializer.Serialize(kept) || !adapted.Descriptors.Any(d=>d.Set==0 && d.Binding==258 && d.DescriptorType=="Sampler"))throw new InvalidDataException("Cube-LOD adaptation changed the resource contract beyond its isotropic sampler");
            if(JsonSerializer.Serialize(after.Interface)!=JsonSerializer.Serialize(adapted.Interface) || !after.Capabilities.SequenceEqual(adapted.Capabilities))throw new InvalidDataException("Cube-LOD adaptation changed stage interfaces/capabilities");
            runtime=cube;after=adapted;Interlocked.Increment(ref cubeAdapted);
        }
        if (after.ExecutionModel == 0) // Vertex descriptors must have a disjoint namespace from fragment registers.
        {
            byte[] relocated = SpirvRuntimeModule.RebaseVertexBindings(runtime);
            // Reflection consumes catalog register ranges; normalize only binding words before comparing it.
            byte[] normalized = (byte[])relocated.Clone();
            for (int offset = 20; offset < normalized.Length;)
            {
                uint instruction = BitConverter.ToUInt32(normalized, offset);
                int length = checked((int)(instruction >> 16) * 4);
                if ((instruction & 65535) == 71 && length == 16 && BitConverter.ToUInt32(normalized, offset + 8) == 33)
                {
                    uint originalBinding = BitConverter.ToUInt32(runtime, offset + 12);
                    if (BitConverter.ToUInt32(normalized, offset + 12) != originalBinding + 1024)
                        throw new InvalidDataException("Vertex descriptor relocation omitted a binding");
                    Buffer.BlockCopy(BitConverter.GetBytes(originalBinding), 0, normalized, offset + 12, 4);
                }
                offset += length;
            }
            if (!runtime.SequenceEqual(normalized)) throw new InvalidDataException("Relocation changed nonbinding instructions");
            var reflected = new ShaderMetadata { EntryPoint = recorded.EntryPoint };
            SpirvMetadataReader.Reflect(normalized, reflected);
            if (JsonSerializer.Serialize(Layout(after)) != JsonSerializer.Serialize(Layout(reflected)))
                throw new InvalidDataException("Vertex descriptor relocation changed another executable contract");
            try { SpirvRuntimeModule.RebaseVertexBindings(relocated); if (after.Descriptors.Count > 0) throw new InvalidDataException("Repeated relocation was accepted"); }
            catch (InvalidDataException relocationError) when (relocationError.Message == "SPIR-V binding exceeds stage range.") { }
            runtime = relocated;
        }
        string target=Path.Combine(output,Path.GetFileName(source));File.WriteAllBytes(target,runtime);
        var start=new ProcessStartInfo(Path.GetFullPath(args[1])){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};foreach(string arg in new[]{"--target-env","vulkan1.4","--scalar-block-layout",target})start.ArgumentList.Add(arg);
        using var process=Process.Start(start);var error=process.StandardError.ReadToEndAsync();var stdout=process.StandardOutput.ReadToEndAsync();if(!process.WaitForExit(30000)){process.Kill();throw new TimeoutException("Runtime shader validation timeout");}Task.WaitAll(error,stdout);if(process.ExitCode!=0)throw new InvalidDataException(error.Result+stdout.Result);
        int count=Interlocked.Increment(ref done);if(count%200==0)Console.WriteLine($"{count}/{modules.Length} runtime modules validated");
    }catch(Exception e){failures.Add(Path.GetFileName(source)+": "+e.Message);}
});
File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Total=modules.Length,Validated=done,CubeLodAdapted=cubeAdapted,Failures=failures.ToArray(),Scope="Runtime annotation removal, isotropic explicit cube LOD and vertex relocation; only the optional s2 sampler addition is allowed; Vulkan 1.4 validated"},new JsonSerializerOptions{WriteIndented=true}));
if(!failures.IsEmpty)throw new InvalidOperationException(string.Join("\n",failures.Take(10)));
Console.WriteLine($"PASS all {done} runtime modules validate; {cubeAdapted} preserve DX11 cube-LOD semantics with only an optional isotropic sampler descriptor added.");

