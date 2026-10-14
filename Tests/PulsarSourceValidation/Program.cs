using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("Usage: dotnet run --project Tests/PulsarSourceValidation -- <repo> <game Bin64> <Pulsar>"); return 2; }
        try
        {
            string repository = Path.GetFullPath(args[0]), game = Path.GetFullPath(args[1]), pulsar = Path.GetFullPath(args[2]);
            string libraries = Path.Combine(pulsar, "Libraries", "Interim");
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string candidate = Path.Combine(libraries, name.Name + ".dll");
                return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
            };
            var shared = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(libraries, "Pulsar.Shared.dll"));
            object manifest;
            using (var input = File.OpenRead(Path.Combine(repository, "Vulkan.xml")))
                manifest = new XmlSerializer(shared.GetType("Pulsar.Shared.Data.PluginData", true)).Deserialize(input);
            var metadata = manifest.GetType();
            string runtimes = (string)metadata.GetProperty("Runtimes").GetValue(manifest);
            string platforms = (string)metadata.GetProperty("Platforms").GetValue(manifest);
            if ((string)metadata.GetProperty("Id").GetValue(manifest) != "AE72EBB8-F4A8-4ADB-A400-19CC46FDAB79") throw new Exception("Manifest permanent ID mismatch.");
            var tools = shared.GetType("Pulsar.Shared.Tools", true);
            bool supported = (bool)tools.GetMethod("IsSupportedEnvironment").Invoke(null, new object[] { runtimes, platforms });
            if (!supported) throw new Exception("Installed Pulsar rejects manifest runtime/platform: " + runtimes + "/" + platforms);
            Console.WriteLine("PASS installed Pulsar XML parser and runtime/platform gate: " + metadata.Name + " " + runtimes + " " + platforms);
            string[] sourceDirectories = (string[])metadata.GetProperty("SourceDirectories").GetValue(manifest);
            var sources = sourceDirectories.SelectMany(d => Directory.GetFiles(Path.Combine(repository, d), "*.cs", SearchOption.AllDirectories))
                .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s == "bin" || s == "obj"))
                .Select(p => new { Name = Path.GetRelativePath(repository, p).Replace('\\', '/'), Data = Convert.ToBase64String(File.ReadAllBytes(p)), EmbedFile = p }).ToArray();
            if (sources.Length == 0) throw new Exception("Manifest selected no source files.");
            string framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "microsoft.netframework.referenceassemblies.net48", "1.0.3", "build", ".NETFramework", "v4.8");
            string core = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (!Directory.Exists(framework)) throw new DirectoryNotFoundException("Restore the net48 project first to provision reference assemblies: " + framework);
            string compilerDirectory = Path.Combine(pulsar, "Libraries", "Compiler");
            foreach (bool interim in new[] { false, true })
            {
                var start = new ProcessStartInfo(Path.Combine(compilerDirectory, "Compiler.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = compilerDirectory };
                using (var process = Process.Start(start))
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                    string targetFramework = interim ? core : framework;
                    string editionLibraries = Path.Combine(pulsar, "Libraries", interim ? "Interim" : "Legacy");
                    var probeDirectories = new[] { targetFramework, Path.Combine(targetFramework, "Facades"), editionLibraries, compilerDirectory, game }.Where(Directory.Exists).ToArray();
                    var references = new[] { "mscorlib", "System", "System.Core", "System.Xml", "System.Runtime.Serialization", "System.Runtime.InteropServices.RuntimeInformation", "netstandard", "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Console", "System.Linq", "System.IO.FileSystem", "System.Reflection", "System.Runtime.InteropServices", "System.Threading", "System.Xml.XmlSerializer", "System.Security.Cryptography" }
                        .Concat(Directory.GetFiles(game, "*.dll").Where(IsManaged).Select(Path.GetFileNameWithoutExtension))
                        .Concat(new[] { "0Harmony", "Mono.Cecil" }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    string work = Path.Combine(repository, "Tests", "PulsarSourceValidation", "obj", interim ? "interim" : "legacy"); Directory.CreateDirectory(work);
                    await Write(process.StandardInput.BaseStream, new { References = references, ProbeDirectories = probeDirectories, LogFile = Path.Combine(work, "compiler.log") }, timeout.Token);
                    var initialized = await Read(process.StandardOutput.BaseStream, timeout.Token);
                    if (initialized.TryGetProperty("Error", out var initError) && initError.ValueKind != JsonValueKind.Null) throw new Exception(initError.ToString());
                    await Write(process.StandardInput.BaseStream, new { AssemblyName = "Vulkan_PulsarSourceValidation_" + (interim ? "Interim" : "Legacy"), DebugBuild = true, Flags = interim ? new[] { "NETCOREAPP", "PULSAR", "DEBUG" } : new[] { "PULSAR", "DEBUG" }, Sources = sources, References = new[] { Path.Combine(editionLibraries, "0Harmony.dll"), Path.Combine(editionLibraries, "Mono.Cecil.dll") } }, timeout.Token);
                    var result = await Read(process.StandardOutput.BaseStream, timeout.Token);
                    process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
                    if (!result.GetProperty("Success").GetBoolean())
                    {
                        string diagnostics = result.TryGetProperty("Diagnostics", out var data) ? data.ToString() : result.ToString();
                        Console.Error.WriteLine(diagnostics); Console.Error.WriteLine(await stderr); return 1;
                    }
                    byte[] image = Convert.FromBase64String(result.GetProperty("Assembly").GetString()); File.WriteAllBytes(Path.Combine(work, "plugin.dll"), image);
                    Console.WriteLine("PASS installed Pulsar Compiler.exe " + (interim ? "Interim/CoreCLR" : "Legacy/CLR") + ": " + sources.Length + " source files, " + image.Length + " assembly bytes");
                }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static bool IsManaged(string file) { try { AssemblyName.GetAssemblyName(file); return true; } catch (BadImageFormatException) { return false; } }
    private static async Task Write(Stream stream, object request, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(request); await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    private static async Task<JsonElement> Read(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token); int count = BitConverter.ToInt32(header, 0);
        if (count <= 0 || count > 256 * 1024 * 1024) throw new InvalidDataException("Invalid Pulsar IPC response length.");
        byte[] bytes = new byte[count]; await stream.ReadExactlyAsync(bytes, token); return JsonDocument.Parse(bytes).RootElement.Clone();
    }
}
