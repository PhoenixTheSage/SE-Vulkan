#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using ClientPlugin.Rendering.Shaders;
string content=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]);
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
var library=new VulkanShaderLibrary(root,content,Path.GetFullPath(args[2]));
string snapshot=Path.GetFullPath("TestResults/TintShaderPackage/Source");Directory.CreateDirectory(snapshot);
foreach(string file in Directory.EnumerateFiles(content,"*",SearchOption.AllDirectories)){
 if(!new[]{".hlsl",".hlsli",".inl",".h"}.Contains(Path.GetExtension(file).ToLowerInvariant()))continue;
 string copy=Path.Combine(snapshot,Path.GetRelativePath(content,file));Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(file,copy,true);
}
string relative="Geometry/PixelTemplateBase.hlsli",original=Path.Combine(content,relative),modified=Path.Combine(snapshot,relative);
string text=File.ReadAllText(modified),before="pix.color_mul = 1;",after="pix.color_mul = object_.color_mul;";
if(text.Split(before).Length!=2)throw new InvalidDataException("Unexpected stock pixel initializer.");
File.WriteAllText(modified,text.Replace(before,after));
using var input=File.OpenRead(Path.Combine(root,"catalog.xml.gz"));using var gzip=new GZipStream(input,CompressionMode.Decompress);var catalog=XDocument.Load(gzip);
var work=new List<(uint Id,VulkanShaderArtifact Base,string Source,Dictionary<string,string> Defines)>();
foreach(uint id in new uint[]{2,7,12,17}){
 var core=WorldShaderVariants.Materials.FirstOrDefault(s=>s.Id==id);
 if(core.Pixel!=null){var a=library.Load(core.Pixel);var variant=catalog.Descendants("Variant").Single(v=>(string?)v.Attribute("Id")==core.Pixel);work.Add((id,a,a.Source,variant.Elements("Define").ToDictionary(d=>(string)d.Attribute("Name")!,d=>(string?)d.Attribute("Value")??"")));}
 else{var e=WorldShaderVariants.Extra.Single(e=>e.Id==id);work.Add((id,library.LoadExtra(e.Id,e.Material,"Pixel",e.Defines),"Geometry/Materials/"+e.Material+"/Pixel.hlsl",e.Defines));}
 var probe=ProbeShaderVariants.Extra.Single(e=>e.Id==256+id);work.Add((64+id,library.LoadExtra(probe.Id,probe.Material,"Pixel",probe.Defines),"Geometry/Materials/"+probe.Material+"/Pixel.hlsl",probe.Defines));
}
string output=Path.Combine(root,"ObjectTint");Directory.CreateDirectory(output);
var manifest=new XElement("ObjectTint",new XAttribute("Schema","1"),new XAttribute("Transform","ObjectTint-1"),new XAttribute("Count",8),new XAttribute("OriginalSource",Hash(original)),new XAttribute("DerivedSource",Hash(modified)));
using var compiler=VulkanShaderCompiler.CreateBundled(Path.GetFullPath("TestResults/TintShaderPackage/Cache"));using var batch=compiler.CreateBatch(snapshot);
foreach(var w in work){
 if(!w.Defines.ContainsKey("USE_GENERIC_INSTANCING"))throw new InvalidDataException("Object tint bridge only supports generic instancing.");
 var result=batch.Compile(new VulkanShaderRequest{ContentDirectory=snapshot,SourcePath=Path.Combine(snapshot,w.Source),Profile="ps_5_0",EntryPoint="__pixel_shader",Defines=w.Defines});
 string module=Hash(result.SpirvPath),metadata=Hash(result.MetadataPath);File.Copy(result.SpirvPath,Path.Combine(output,module+".spv"),true);File.Copy(result.MetadataPath,Path.Combine(output,metadata+".xml"),true);
 manifest.Add(new XElement("Pixel",new XAttribute("Pipeline",w.Id),new XAttribute("BaseId",w.Base.Id),new XAttribute("BaseModule",w.Base.Layout.SpirvSha256),new XAttribute("Module",module),new XAttribute("Metadata",metadata)));Console.WriteLine("Packaged tinted generic pixel pipeline "+w.Id);
}
batch.VerifyBatchToolchain();new XDocument(manifest).Save(Path.Combine(output,"manifest.xml"));var coverage=XDocument.Load(Path.Combine(root,"coverage.xml"));coverage.Root!.SetAttributeValue("ObjectTintSha256",Hash(Path.Combine(output,"manifest.xml")));coverage.Save(Path.Combine(root,"coverage.xml"));
