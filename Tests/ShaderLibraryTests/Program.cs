using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using ClientPlugin.Rendering.Shaders;

if (args.Length < 3) throw new ArgumentException("ShaderLibraryTests <catalog-directory> <installed-shader-directory> <game-render11-assembly>");
var library = new VulkanShaderLibrary(args[0], args[1], args[2]);
int loaded = 0;
var modules = new HashSet<string>();
var capabilities = new HashSet<uint>();
foreach (string id in library.VariantIds)
{
    var artifact = library.Load(id);
    if (artifact.Layout.EntryPoint == null || artifact.Layout.Capabilities.Count == 0) throw new Exception("Missing execution requirements.");
    modules.Add(artifact.Layout.SpirvSha256);
    foreach (uint capability in artifact.Layout.Capabilities) capabilities.Add(capability);
    loaded++;
}
if (loaded != library.VariantCount || modules.Count != library.ModuleCount) throw new Exception("Incomplete catalog/artifact coverage.");
Console.WriteLine($"PASS: all {loaded} variant records / {modules.Count} unique module layouts load with checked hashes and reflection; capabilities {string.Join(',', capabilities.Order())}.");
int checks = 1;
void Reject(Action action, string label)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException || error is XmlException || error is FileNotFoundException || error is KeyNotFoundException)
    { Console.WriteLine("PASS: " + label); checks++; return; }
    throw new Exception("FAIL: " + label);
}
Reject(() => library.Load(new string('0', 64)), "unknown shader variant is rejected");
Reject(() => new VulkanShaderLibrary(args[0], args[1], args[2], new string('0', 64)), "catalog hash mismatch is rejected");
string temporary = Path.Combine(Path.GetTempPath(), "SeVulkanShaderLibrary-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    void WriteCatalog(string xml)
    {
        using var stream = File.Create(Path.Combine(temporary, "catalog.xml.gz"));
        using var gzip = new GZipStream(stream, CompressionLevel.Fastest);
        gzip.Write(Encoding.UTF8.GetBytes(xml));
    }
    WriteCatalog("<!DOCTYPE ShaderSuite [<!ENTITY escape SYSTEM 'file:///nonexistent'>]><ShaderSuite Schema='1' Api='1.4' Total='0'>&escape;</ShaderSuite>");
    Reject(() => new VulkanShaderLibrary(temporary, args[1]), "external entities and DTDs are rejected");
    WriteCatalog("<ShaderSuite Schema='1' Api='1.4' Total='0'><Source Path='../escape.hlsl' Sha256='bad'/></ShaderSuite>");
    Reject(() => new VulkanShaderLibrary(temporary, args[1]), "source path traversal is rejected");
    WriteCatalog("<ShaderSuite Schema='1' Api='1.4' Total='0'><Source Path='Common.hlsli' Sha256='bad'/></ShaderSuite>");
    Reject(() => new VulkanShaderLibrary(temporary, args[1]), "edited installed content is rejected");
    WriteCatalog("<ShaderSuite Schema='1' Api='1.4' AssemblySha256='bad' Total='0'/>");
    Reject(() => new VulkanShaderLibrary(temporary, args[1], args[2]), "different game assembly is rejected");
    // Use the actual registry with one copied module and corrupt it, leaving all
    // installed game content and deployment assets untouched.
    File.Copy(Path.Combine(args[0], "catalog.xml.gz"), Path.Combine(temporary, "catalog.xml.gz"), true);
    string moduleDirectory = Path.Combine(temporary, "Modules");
    Directory.CreateDirectory(moduleDirectory);
    foreach (string path in Directory.GetFiles(Path.Combine(args[0], "Modules"))) File.Copy(path, Path.Combine(moduleDirectory, Path.GetFileName(path)));
    var copied = new VulkanShaderLibrary(temporary, args[1]);
    string firstId = copied.VariantIds.First();
    var first = copied.Load(firstId);
    File.WriteAllBytes(first.SpirvPath, new byte[20]);
    Reject(() => copied.Load(firstId), "corrupt SPIR-V asset is rejected");
}
finally
{
    if (Path.GetDirectoryName(Path.GetFullPath(temporary)) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(temporary).StartsWith("SeVulkanShaderLibrary-", StringComparison.Ordinal)) Directory.Delete(temporary, true);
}
Console.WriteLine("Shader library checks passed: " + checks + ".");
