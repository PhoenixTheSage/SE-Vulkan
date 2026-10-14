using System.Collections.Concurrent;
using System.Text.Json;
using System.Security.Cryptography;
using ClientPlugin.Rendering.Shaders;

if (args.Length < 4) throw new ArgumentException("ShaderSuiteCompile <inventory.json> <shader-directory> <tool-directory> <output-directory> [source-filter] [workers]");
int workers = args.Length > 5 ? int.Parse(args[5]) : 6;
if (workers < 1 || workers > 16) throw new ArgumentOutOfRangeException(nameof(workers), "Use between 1 and 16 compilation workers.");
string output = Path.GetFullPath(args[3]);
Directory.CreateDirectory(output);
using var inventory = JsonDocument.Parse(File.ReadAllText(args[0]));
var expectedFiles = inventory.RootElement.GetProperty("Files").EnumerateArray().ToDictionary(f => f.GetProperty("Path").GetString(), f => f.GetProperty("Sha256").GetString(), StringComparer.Ordinal);
void VerifySourceSnapshot()
{
    foreach (var expected in expectedFiles)
        if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetFullPath(args[1]), expected.Key)))) != expected.Value)
            throw new InvalidDataException("Installed shader content changed since capture: " + expected.Key);
}
VerifySourceSnapshot();
var variants = inventory.RootElement.GetProperty("Variants").EnumerateArray().Where(v => args.Length < 5 || args[4] == "*" || v.GetProperty("Source").GetString().Contains(args[4], StringComparison.OrdinalIgnoreCase)).ToArray();
if (variants.Length == 0) throw new InvalidDataException("The shader source filter selected no variants.");
using var compiler = new VulkanShaderCompiler(args[2], Path.Combine(output, "Cache")).CreateBatch(args[1]);
var results = new ConcurrentDictionary<string, object>();
int completed = 0, failed = 0;
object checkpointLock = new();
var started = DateTime.UtcNow;
Parallel.ForEach(variants, new ParallelOptions { MaxDegreeOfParallelism = workers }, variant =>
{
    string id = variant.GetProperty("Id").GetString(), source = variant.GetProperty("Source").GetString();
    try
    {
        var request = new VulkanShaderRequest
        {
            SourcePath = Path.Combine(Path.GetFullPath(args[1]), source), ContentDirectory = Path.GetFullPath(args[1]),
            Profile = variant.GetProperty("Profile").GetString(), EntryPoint = variant.GetProperty("EntryPoint").GetString(),
            Defines = variant.GetProperty("Defines").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString())
        };
        var result = compiler.Compile(request);
        foreach (var dependency in result.Metadata.Dependencies)
            if (!dependency.Path.StartsWith("root0/", StringComparison.Ordinal) || !expectedFiles.TryGetValue(dependency.Path[6..], out string expectedHash) || expectedHash != dependency.Sha256)
                throw new InvalidDataException("Compiler snapshot differs from the source inventory: " + dependency.Path);
        results[id] = new { Id = id, Source = source, Profile = request.Profile, Compilation = "Validated", Integration = "Pending", VisualVerification = "Pending", result.CacheHit, result.Metadata.CacheKey, result.Metadata.SpirvSha256, result.Metadata.ToolchainSha256, result.SpirvPath, result.MetadataPath, DescriptorCount = result.Metadata.Descriptors.Count, Diagnostics = result.Diagnostics };
    }
    catch (Exception error)
    {
        Interlocked.Increment(ref failed);
        results[id] = new { Id = id, Source = source, Profile = variant.GetProperty("Profile").GetString(), Compilation = "Failed", Integration = "Pending", VisualVerification = "Pending", Diagnostics = error.ToString() };
    }
    int done = Interlocked.Increment(ref completed);
    if (done % 100 == 0 || done == variants.Length)
    {
        Console.WriteLine($"{done}/{variants.Length}; failed {Volatile.Read(ref failed)}; {(DateTime.UtcNow - started).TotalSeconds:F0}s");
        lock (checkpointLock) File.WriteAllText(Path.Combine(output, "results.partial.json"), JsonSerializer.Serialize(results.Values.ToArray()));
    }
});
string report = Path.Combine(output, "results.json");
VerifySourceSnapshot();
compiler.VerifyBatchToolchain();
File.WriteAllText(report, JsonSerializer.Serialize(new { SchemaVersion = 1, StartedUtc = started, FinishedUtc = DateTime.UtcNow, Total = variants.Length, Validated = completed - failed, Failed = failed, Integrated = 0, VisuallyVerified = 0, Variants = results.OrderBy(p => p.Key).Select(p => p.Value).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(report);
return failed == 0 ? 0 : 1;
