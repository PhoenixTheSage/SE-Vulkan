using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using ClientPlugin.Rendering.Shaders;
var lib=new VulkanShaderLibrary(args[0],args[1],args[2]);int checks=0;
void Check(bool c,string text){if(!c)throw new Exception(text);++checks;Console.WriteLine("PASS "+text);}
var core=WorldShaderVariants.Materials.Single(s=>s.Id==2);var original=lib.Load(core.Pixel);
var adjusted=lib.LoadObjectTintPixel(2,original);Check(adjusted.Id=="object-tint-2" && adjusted.Layout.SpirvSha256!=original.Layout.SpirvSha256,"derived tint pixels have distinct authenticated module identity");
foreach(uint id in new uint[]{2,7,12,17}){
 var stock=WorldShaderVariants.Materials.FirstOrDefault(v=>v.Id==id);VulkanShaderArtifact artifact;
 if(stock.Pixel!=null)artifact=lib.Load(stock.Pixel);else{var extra=WorldShaderVariants.Extra.Single(v=>v.Id==id);artifact=lib.LoadExtra(id,extra.Material,"Pixel",extra.Defines);}
 Check(lib.LoadObjectTintPixel(id,artifact).Layout.EntryPoint=="__pixel_shader","tint generic variant "+id+" authenticates");
 var probe=ProbeShaderVariants.Extra.Single(v=>v.Id==256+id);Check(lib.LoadObjectTintPixel(64+id,lib.LoadExtra(probe.Id,probe.Material,"Pixel",probe.Defines)).Layout.EntryPoint=="__pixel_shader","tint forward variant "+(64+id)+" authenticates");
}
string scratch=Path.GetFullPath("TestResults/TintShaderIntegrityTests/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
using(var file=File.Create(Path.Combine(scratch,"catalog.xml.gz")))using(var gzip=new GZipStream(file,CompressionMode.Compress))new XDocument(new XElement("ShaderSuite",new XAttribute("Schema",1),new XAttribute("Api","1.4"),new XAttribute("Total",0))).Save(gzip);
File.Copy(Path.Combine(args[0],"coverage.xml"),Path.Combine(scratch,"coverage.xml"));string addon=Path.Combine(scratch,"ObjectTint");Directory.CreateDirectory(addon);
foreach(string path in Directory.GetFiles(Path.Combine(args[0],"ObjectTint")))File.Copy(path,Path.Combine(addon,Path.GetFileName(path)));
var copy=new VulkanShaderLibrary(scratch,args[1]);
void Reject(Action a,string text){try{a();}catch(InvalidDataException){Check(true,text);return;}throw new Exception("accepted "+text);}
Reject(()=>copy.LoadObjectTintPixel(2,lib.Load(WorldShaderVariants.Materials.Single(s=>s.Id==0).Pixel)),"wrong base shader identity rejected");
string module=Path.Combine(addon,adjusted.Layout.SpirvSha256+".spv");byte[] bytes=File.ReadAllBytes(module);File.WriteAllBytes(module,new byte[20]);Reject(()=>copy.LoadObjectTintPixel(2,original),"corrupt derived module rejected");File.WriteAllBytes(module,bytes);
string manifest=Path.Combine(addon,"manifest.xml");var doc=XDocument.Load(manifest);doc.Root.SetAttributeValue("DerivedSource",new string('0',64));doc.Save(manifest);
var coverage=XDocument.Load(Path.Combine(scratch,"coverage.xml"));coverage.Root.SetAttributeValue("ObjectTintSha256",Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifest))));coverage.Save(Path.Combine(scratch,"coverage.xml"));
Reject(()=>copy.LoadObjectTintPixel(2,original),"different transformation source rejected even with matching manifest hash");
Console.WriteLine(checks+" tint shader integrity checks passed; scratch retained under "+scratch);
