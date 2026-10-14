using System.Security.Cryptography;
using System.Xml.Linq;
using ClientPlugin.Rendering.Shaders;
string content=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]);
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
var lib=new VulkanShaderLibrary(root,content,Path.GetFullPath(args[2]));var original=lib.Load(LightTileShader.Variant);
string snapshot=Path.GetFullPath("TestResults/LightTileShaderPackage/Source");Directory.CreateDirectory(snapshot);
foreach(var file in Directory.EnumerateFiles(content,"*",SearchOption.AllDirectories))if(new[]{".hlsl",".hlsli",".inl",".h"}.Contains(Path.GetExtension(file).ToLowerInvariant())){string copy=Path.Combine(snapshot,Path.GetRelativePath(content,file));Directory.CreateDirectory(Path.GetDirectoryName(copy));File.Copy(file,copy,true);}
string path=Path.Combine(snapshot,"Lighting/PrepareLights.hlsl");File.WriteAllText(path,LightTileShader.DeriveSource(File.ReadAllText(path)));
using var compiler=VulkanShaderCompiler.CreateBundled(Path.GetFullPath("TestResults/LightTileShaderPackage/Cache"));using var batch=compiler.CreateBatch(snapshot);
var result=batch.Compile(new VulkanShaderRequest{ContentDirectory=snapshot,SourcePath=path,EntryPoint="__compute_shader",Profile="cs_5_0",Defines=new(){["NUMTHREADS"]="16"}});batch.VerifyBatchToolchain();
string output=Path.Combine(root,"LightTiles");Directory.CreateDirectory(output);string module=Hash(result.SpirvPath),metadata=Hash(result.MetadataPath);
File.Copy(result.SpirvPath,Path.Combine(output,module+".spv"),true);File.Copy(result.MetadataPath,Path.Combine(output,metadata+".xml"),true);
new XDocument(new XElement("LightTiles",new XAttribute("Schema",1),new XAttribute("Transform",LightTileShader.Contract),new XAttribute("BaseModule",original.Layout.SpirvSha256),new XAttribute("DerivedSource",Hash(path)),new XAttribute("Module",module),new XAttribute("Metadata",metadata))).Save(Path.Combine(output,"manifest.xml"));
var coverage=XDocument.Load(Path.Combine(root,"coverage.xml"));coverage.Root.SetAttributeValue("LightTilesSha256",Hash(Path.Combine(output,"manifest.xml")));coverage.Save(Path.Combine(root,"coverage.xml"));Console.WriteLine("Packaged deterministic stock tiled light compute "+module);
