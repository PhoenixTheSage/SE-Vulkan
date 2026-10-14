using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using ClientPlugin.Rendering.Shaders;

string content=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]),output=Path.Combine(root,"Extra");Directory.CreateDirectory(output);
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
using var compiler=VulkanShaderCompiler.CreateBundled(Path.GetFullPath("TestResults/WorldShaderPackage/Cache"));using var batch=compiler.CreateBatch(content);
var specs=WorldShaderVariants.Extra.Concat(TransparencyShaderVariants.Extra).Concat(ProbeShaderVariants.Extra).Concat(HighlightShaderVariants.Extra).ToArray();
var manifest=new XElement("WorldShaders",new XAttribute("Schema","1"),new XAttribute("Count",specs.Length*2));
foreach(var spec in specs)foreach(string stage in new[]{"Vertex","Pixel"})
{
    var result=batch.Compile(new VulkanShaderRequest{SourcePath=Path.Combine(content,"Geometry","Materials",spec.Material,stage+".hlsl"),ContentDirectory=content,
        EntryPoint=stage=="Vertex"?"__vertex_shader":"__pixel_shader",Profile=stage=="Vertex"?"vs_5_0":"ps_5_0",Defines=spec.Defines});
    string module=Hash(result.SpirvPath);File.Copy(result.SpirvPath,Path.Combine(output,module+".spv"),true);File.Copy(result.MetadataPath,Path.Combine(output,Hash(result.MetadataPath)+".xml"),true);
    manifest.Add(new XElement("Shader",new XAttribute("Id",spec.Id),new XAttribute("Stage",stage),new XAttribute("Module",module),new XAttribute("Metadata",Hash(result.MetadataPath)),
        new XAttribute("DefinitionsSha256",VulkanShaderLibrary.ExtraDefinitionsHash(spec.Material,stage,spec.Defines))));
}
batch.VerifyBatchToolchain();new XDocument(manifest).Save(Path.Combine(output,"manifest.xml"));var coverage=XDocument.Load(Path.Combine(root,"coverage.xml"));coverage.Root.SetAttributeValue("WorldExtrasSha256",Hash(Path.Combine(output,"manifest.xml")));coverage.Save(Path.Combine(root,"coverage.xml"));
Console.WriteLine("Packaged "+specs.Length*2+" validated supplemental shader stages; no runtime compiler invocation is required.");
