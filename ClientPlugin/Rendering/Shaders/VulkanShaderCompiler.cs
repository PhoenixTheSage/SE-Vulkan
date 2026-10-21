using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using ClientPlugin.Bootstrap;

namespace ClientPlugin.Rendering.Shaders;

// Deliberately independent of the render thread and game assemblies. Content
// translation can run offline or on the renderer's resource worker. Callers must
// create pipelines from the reflected bindings, never from DX11 register numbers.
public sealed class VulkanShaderCompiler : IDisposable
{
    public const uint RegisterBandSize = 128;
    private const string CompileContract = "SEVK-HLSL-7;SE1.210.014-array-repairs;HV2016;legacy-macros;legacy-register-paste;legacy-return-warning;legacy-resources;dx-layout;dx-position-w;row-major;active-bindings;auto-shift;SPIRV1.6;Vulkan1.4;bands=0,128,256,384;counter=512";
    private static readonly Regex Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Include = new Regex(@"^(\s*#\s*include\s*)[<"" ]*([^>""\r\n]+)[>""]", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex SourceLine = new Regex(@"^\s*#(?:line)?\s*\d+\s+""([^""]+)""", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Register = new Regex(@"\bregister\s*\(\s*([btsu])\s*(\d+)\s*(?:,\s*space\s*(\d+)\s*)?\)", RegexOptions.CultureInvariant);
    private static readonly object[] CacheLocks = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
    private static readonly XmlSerializer MetadataSerializer = new XmlSerializer(typeof(ShaderMetadata));
    private readonly string toolsDirectory;
    private readonly string cacheDirectory;
    private readonly string dxc;
    private readonly string validator;
    private readonly int timeoutMilliseconds;
    private IncludeMirror batchMirror;
    private string batchDirectory, batchContentDirectory, batchToolchain;
    private bool disposed;

    // A bulk audit shares one immutable source snapshot. Ordinary Compile calls
    // continue to snapshot fresh content/tool hashes so hot reload stays correct.
    public VulkanShaderCompiler CreateBatch(string contentDirectory)
    {
        if (disposed) throw new ObjectDisposedException(nameof(VulkanShaderCompiler));
        var batch = new VulkanShaderCompiler(toolsDirectory, cacheDirectory, timeoutMilliseconds);
        batch.batchContentDirectory = Path.GetFullPath(contentDirectory);
        batch.batchDirectory = Path.Combine(cacheDirectory, "Snapshots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(batch.batchDirectory);
        try
        {
            string source = Directory.GetFiles(batch.batchContentDirectory, "*.hlsl", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).First();
            batch.batchMirror = new IncludeMirror(new VulkanShaderRequest { SourcePath = source, ContentDirectory = batch.batchContentDirectory }, batch.batchDirectory);
            batch.batchToolchain = batch.ToolchainHash();
            return batch;
        }
        catch { batch.Dispose(); throw; }
    }

    public void Dispose()
    {
        // The owner waits for all batch workers before disposing this snapshot.
        disposed = true;
        if (batchDirectory != null && Path.GetDirectoryName(batchDirectory) == Path.Combine(cacheDirectory, "Snapshots") && Directory.Exists(batchDirectory)) Directory.Delete(batchDirectory, true);
    }

    public void VerifyBatchToolchain()
    {
        if (disposed) throw new ObjectDisposedException(nameof(VulkanShaderCompiler));
        if (batchMirror == null) throw new InvalidOperationException("An immutable compilation batch is required.");
        if (ToolchainHash() != batchToolchain) throw new InvalidDataException("Compiler tools changed during the shader compilation batch.");
    }

    public VulkanShaderCompiler(string toolsDirectory, string cacheDirectory, int processTimeoutMilliseconds = 30000)
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("The bundled Vulkan shader tools require a 64-bit process.");
        this.toolsDirectory = Path.GetFullPath(toolsDirectory ?? throw new ArgumentNullException(nameof(toolsDirectory)));
        this.cacheDirectory = Path.GetFullPath(cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory)));
        if (processTimeoutMilliseconds < 1000 || processTimeoutMilliseconds > 60000) throw new ArgumentOutOfRangeException(nameof(processTimeoutMilliseconds));
        timeoutMilliseconds = processTimeoutMilliseconds;
        bool windows = Path.DirectorySeparatorChar == '\\';
        dxc = Path.Combine(this.toolsDirectory, windows ? "dxc.exe" : Path.Combine("bin", "dxc"));
        validator = Path.Combine(this.toolsDirectory, windows ? "spirv-val.exe" : Path.Combine("bin", "spirv-val"));
        if (!File.Exists(dxc) || !File.Exists(validator)) throw new FileNotFoundException("Bundled DXC and SPIRV-Tools are required in " + this.toolsDirectory + ".");
        if (windows && (!File.Exists(Path.Combine(this.toolsDirectory, "dxcompiler.dll")) || !File.Exists(Path.Combine(this.toolsDirectory, "dxil.dll"))))
            throw new FileNotFoundException("The pinned DXC runtime libraries are missing.");
        if (!windows && !File.Exists(Path.Combine(this.toolsDirectory, "lib", "libdxcompiler.so")))
            throw new FileNotFoundException("The pinned Linux DXC runtime library is missing.");
#if NETCOREAPP
        // Windows cross-packaging cannot retain POSIX executable bits. These
        // two bundled tools belong to the plugin's own asset directory.
        if (OperatingSystem.IsLinux())
            foreach (string executable in new[] { dxc, validator })
                File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
#endif
    }

    public static VulkanShaderCompiler CreateBundled(string cacheDirectory)
    {
        string rid = Path.DirectorySeparatorChar == '\\' ? "win-x64" : "linux-x64";
        string relative = Path.Combine("Tools", rid, Path.DirectorySeparatorChar == '\\' ? "dxc.exe" : Path.Combine("bin", "dxc"));
        string assets = BootstrapAssets.FindDirectory(relative);
        if (assets == null) throw new FileNotFoundException("Vulkan shader compiler assets are unavailable: " + relative + ".");
        return new VulkanShaderCompiler(Path.Combine(assets, "Tools", rid), cacheDirectory);
    }

    public VulkanShaderResult Compile(VulkanShaderRequest request)
    {
        if (disposed) throw new ObjectDisposedException(nameof(VulkanShaderCompiler));
        if (request == null) throw new ArgumentNullException(nameof(request));
        request=request.Snapshot();
        string profile = NormalizeProfile(request.Profile);
        if (!Identifier.IsMatch(request.EntryPoint ?? "")) throw new ArgumentException("A valid HLSL entry point is required.");
        if (request.NativeAbiVersion == 0) throw new ArgumentException("A native ABI version is required.");
        // The shipped D3DX format include uses FXC's uppercase FLOAT alias.
        // HLSL_VERSION selects its HLSL branch rather than the C++ implementation.
        var mergedDefinitions = new Dictionary<string, string>(StringComparer.Ordinal) { ["HLSL_VERSION"] = "5", ["FLOAT"] = "float" };
        foreach (var define in request.Defines ?? new Dictionary<string, string>()) mergedDefinitions[define.Key] = define.Value;
        var definitions = mergedDefinitions.OrderBy(d => d.Key, StringComparer.Ordinal).ToArray();
        foreach (var define in definitions)
            if (!Identifier.IsMatch(define.Key) || (define.Value ?? "").IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("Invalid HLSL definition: " + define.Key + ".");
        Directory.CreateDirectory(cacheDirectory);
        string jobs = Path.Combine(cacheDirectory, "Jobs");
        Directory.CreateDirectory(jobs);
        string job = Path.Combine(jobs, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        try
        {
            if (batchMirror != null && (Path.GetFullPath(request.ContentDirectory) != batchContentDirectory || request.IncludeDirectories.Count != 0 || request.SourceGeneration!=null))
                throw new ArgumentException("Batch requests must use the snapshotted content directory without extra include roots.");
            var mirror = batchMirror == null ? new IncludeMirror(request, job) : batchMirror.ForSource(request.SourcePath);
            string workingDirectory = batchDirectory ?? job;
            var common = new List<string> { "-HV", "2016", "-flegacy-macro-expansion", "-flegacy-resource-reservation", "-Zpr", "-T", profile, "-E", request.EntryPoint };
            foreach (var define in definitions) { common.Add("-D"); common.Add(define.Key + "=" + (define.Value ?? "1")); }
            foreach (string root in mirror.Roots) { common.Add("-I"); common.Add(root); }
            string preprocessedPath = Path.Combine(job, "preprocessed.hlsl");
            var preprocessArgs = new List<string>(common) { "-P", "-Fi", preprocessedPath, mirror.Source };
            string diagnostics = Run(dxc, preprocessArgs, workingDirectory);
            if (!File.Exists(preprocessedPath)) throw new InvalidDataException("DXC produced no preprocessed HLSL.\n" + diagnostics);
            string preprocessed = File.ReadAllText(preprocessedPath);
            string dependencyFile = Path.Combine(job, "dependencies.d");
            var dependencyArgs = new List<string>(common) { "-M", "-MF", dependencyFile, mirror.Source };
            string dependencyOutput = Run(dxc, dependencyArgs, workingDirectory);
            List<ShaderDependency> dependencies = mirror.Dependencies(preprocessed, File.Exists(dependencyFile) ? File.ReadAllText(dependencyFile) : dependencyOutput);
            // Preserve relative #line paths. Preprocess and compile use the same
            // immutable mirror, so an edited include cannot race the cache key.
            File.WriteAllText(preprocessedPath, preprocessed, new UTF8Encoding(false));
            var spaces = new SortedSet<uint> { 0 };
            if (preprocessed.Contains("vk::binding")) throw new InvalidDataException("Explicit vk::binding attributes conflict with the renderer's HLSL register mapping.");
            foreach (Match match in Register.Matches(preprocessed))
            {
                uint index = uint.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                if (index >= RegisterBandSize) throw new InvalidDataException("HLSL register " + match.Groups[1].Value + index + " exceeds the reserved 128-register range.");
                if (match.Groups[3].Success) spaces.Add(uint.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            }
            if (preprocessed.Contains("vk::counter_binding")) throw new InvalidDataException("Explicit counter bindings conflict with the renderer's counter range.");
            var authenticatedDependencies=request.SourceGeneration==null?dependencies:mirror.StockDependencies(dependencies);
            preprocessed=GpuOpaqueShaderAdapter.Apply(preprocessed,request,authenticatedDependencies);
            preprocessed=HbaoBorderShaderAdapter.Apply(preprocessed,request,authenticatedDependencies);
            // DXC gives append/consume UAV counters separate descriptors. Their
            // default bindings do not follow -fvk-u-shift. Reserve one explicit
            // counter slot per source UAV, retaining the source descriptor set.
            preprocessed = Regex.Replace(preprocessed, @"\b(?:RW|Append|Consume)StructuredBuffer\s*<[^;]+?>\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*register\s*\(\s*u(\d+)\s*(?:,\s*space\s*\d+\s*)?\)", match =>
                "[[vk::counter_binding(" + (4 * RegisterBandSize + uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture) + ")]] " + match.Value);
            File.WriteAllText(preprocessedPath, preprocessed, new UTF8Encoding(false));
            string toolchain = batchToolchain ?? ToolchainHash();
            var keyContent = new StringBuilder(CompileContract).Append('\n').Append(ShaderMetadata.CurrentSchemaVersion).Append('\n')
                .Append(request.NativeAbiVersion).Append('\n').Append(toolchain).Append('\n').Append(profile).Append('\n')
                .Append(request.EntryPoint).Append('\n').Append(request.Debug).Append('\n').Append(preprocessed);
            if(request.GpuOpaque)keyContent.Append('\n').Append(GpuOpaqueShaderAdapter.Contract).Append('\n').Append(request.GpuOpaqueBaseModule);
            if(request.HbaoNegativeBorder)keyContent.Append('\n').Append(HbaoBorderShaderAdapter.Contract).Append('\n').Append(request.HbaoBaseModule);
            if(request.SourceGeneration!=null)keyContent.Append('\n').Append(ShaderSourceGeneration.Contract).Append('\n').Append(request.SourceGeneration.Fingerprint);
            foreach (var define in definitions) keyContent.Append('\n').Append(define.Key).Append('=').Append(define.Value ?? "1");
            foreach (ShaderDependency dependency in dependencies) keyContent.Append('\n').Append(dependency.Path).Append('=').Append(dependency.Sha256);
            string key = Hash(Encoding.UTF8.GetBytes(keyContent.ToString()));
            string spirvPath = Path.Combine(cacheDirectory, key + ".spv"), metadataPath = Path.Combine(cacheDirectory, key + ".xml");
            // Bounded lock stripes avoid retaining every historical variant key.
            lock (CacheLocks[(int)(Convert.ToUInt32(key.Substring(0, 8), 16) % (uint)CacheLocks.Length)])
            {
                VulkanShaderResult cached = ReadCached(spirvPath, metadataPath, key, request, toolchain, profile, diagnostics, job);
                if (cached != null) return cached;
                // Vanilla includes reuse registers across mutually exclusive
                // resources. Preserve active bindings, not dead declarations.
                var compileArgs = new List<string> { "-spirv", "-fspv-target-env=vulkan1.3", "-fvk-use-dx-layout", "-fvk-use-dx-position-w", "-fvk-auto-shift-bindings", "-fspv-reflect", "-HV", "2016", "-Zpr", "-T", profile, "-E", request.EntryPoint, "-Fo", Path.Combine(job, "shader.spv") };
                compileArgs.Add(request.Debug ? "-Od" : "-O3");
                // The stock unused TemperatureFilter function has no return.
                // DXC checks it even when unreachable; retain the warning while
                // compiling the unchanged, live tone-mapping code.
                compileArgs.Add("-Wno-error=return-type");
                foreach (uint space in spaces) for (int band = 0; band < 4; band++)
                {
                    compileArgs.Add("-fvk-" + "btsu"[band] + "-shift");
                    compileArgs.Add(((uint)band * RegisterBandSize).ToString(CultureInfo.InvariantCulture));
                    compileArgs.Add(space.ToString(CultureInfo.InvariantCulture));
                }
                compileArgs.Add(preprocessedPath);
                diagnostics += Run(dxc, compileArgs, workingDirectory);
                string output = Path.Combine(job, "shader.spv");
                diagnostics += Validate(output, job);
                byte[] spirv = ReadSpirv(output);
                var metadata = new ShaderMetadata
                {
                    NativeAbiVersion = request.NativeAbiVersion, CacheKey = key, SpirvSha256 = Hash(spirv), ToolchainSha256 = toolchain,
                    SourcePath = Path.GetFullPath(request.SourcePath), EntryPoint = request.EntryPoint, Profile = profile, Dependencies = dependencies,
                    SourceTransform=request.GpuOpaque?GpuOpaqueShaderAdapter.Contract:request.HbaoNegativeBorder?HbaoBorderShaderAdapter.Contract:null,
                    BaseSpirvSha256=request.GpuOpaque?request.GpuOpaqueBaseModule:request.HbaoBaseModule
                };
                SpirvMetadataReader.Reflect(spirv, metadata);
                WriteAtomic(spirvPath, stream => stream.Write(spirv, 0, spirv.Length));
                WriteAtomic(metadataPath, stream => { lock (MetadataSerializer) MetadataSerializer.Serialize(stream, metadata); });
                return new VulkanShaderResult { SpirvPath = spirvPath, MetadataPath = metadataPath, Metadata = metadata, Diagnostics = diagnostics, CacheHit = false };
            }
        }
        finally
        {
            // Only this freshly created GUID workspace may be removed.
            if (Path.GetDirectoryName(job) == jobs && Directory.Exists(job)) try { Directory.Delete(job, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private VulkanShaderResult ReadCached(string spirvPath, string metadataPath, string key, VulkanShaderRequest request, string toolchain, string profile, string diagnostics, string job)
    {
        if (!File.Exists(spirvPath) || !File.Exists(metadataPath)) return null;
        try
        {
            ShaderMetadata metadata;
            using (var reader = XmlReader.Create(metadataPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 }))
                lock (MetadataSerializer) metadata = (ShaderMetadata)MetadataSerializer.Deserialize(reader);
            byte[] bytes = ReadSpirv(spirvPath);
            if (metadata.SchemaVersion != ShaderMetadata.CurrentSchemaVersion || metadata.NativeAbiVersion != request.NativeAbiVersion || metadata.CacheKey != key ||
                metadata.ToolchainSha256 != toolchain || metadata.Profile != profile || metadata.EntryPoint != request.EntryPoint || metadata.SpirvSha256 != Hash(bytes)) return null;
            if(request.GpuOpaque&&(metadata.SourceTransform!=GpuOpaqueShaderAdapter.Contract||metadata.BaseSpirvSha256!=request.GpuOpaqueBaseModule))return null;
            if(request.HbaoNegativeBorder&&(metadata.SourceTransform!=HbaoBorderShaderAdapter.Contract||metadata.BaseSpirvSha256!=request.HbaoBaseModule))return null;
            diagnostics += Validate(spirvPath, job);
            SpirvMetadataReader.Reflect(bytes, metadata);
            return new VulkanShaderResult { SpirvPath = spirvPath, MetadataPath = metadataPath, Metadata = metadata, Diagnostics = diagnostics, CacheHit = true };
        }
        catch (Exception error) when (error is IOException || error is InvalidDataException || error is InvalidOperationException || error is XmlException)
        {
            return null;
        }
    }

    private string Validate(string path, string job)
    {
        ReadSpirv(path);
        return Run(validator, new[] { "--target-env", "vulkan1.4", "--scalar-block-layout", path }, job);
    }

    private string Run(string executable, IEnumerable<string> arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(executable, string.Join(" ", arguments.Select(Quote)))
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.DirectorySeparatorChar != '\\')
        {
            string existing = start.EnvironmentVariables["LD_LIBRARY_PATH"];
            start.EnvironmentVariables["LD_LIBRARY_PATH"] = toolsDirectory + ":" + Path.Combine(toolsDirectory, "lib") + (string.IsNullOrEmpty(existing) ? "" : ":" + existing);
        }
        using (var process = new Process { StartInfo = start })
        {
            if (!process.Start()) throw new InvalidOperationException("Could not start " + executable + ".");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                throw new InvalidOperationException(Path.GetFileName(executable) + " exceeded the shader compilation timeout.");
            }
            string text = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
            if (text.Length > 65536) text = text.Substring(0, 65536) + "\n[diagnostics truncated]";
            if (process.ExitCode != 0) throw new InvalidOperationException(Path.GetFileName(executable) + " failed (" + process.ExitCode + "):\n" + text);
            return text;
        }
    }

    private string ToolchainHash()
    {
        var digest = new StringBuilder();
        foreach (string path in Directory.GetFiles(toolsDirectory, "*", SearchOption.AllDirectories).Where(p =>
            p == dxc || p == validator || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).Contains(".so")).OrderBy(p => p, StringComparer.Ordinal))
            digest.Append(path.Substring(toolsDirectory.Length).Replace('\\', '/')).Append('=').Append(FileHash(path)).Append('\n');
        return Hash(Encoding.UTF8.GetBytes(digest.ToString()));
    }

    private static string NormalizeProfile(string profile)
    {
        Match match = Regex.Match(profile ?? "", @"^(vs|ps|cs|gs|hs|ds)_([56])_([0-8])$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new ArgumentException("Unsupported shader profile: " + profile + ".");
        return match.Groups[1].Value + (match.Groups[2].Value == "5" ? "_6_0" : "_6_" + match.Groups[3].Value);
    }

    private static string Quote(string value)
    {
        // ProcessStartInfo.Arguments uses argv quoting on both CLR and CoreCLR.
        // No shell is involved, including for macro values and content paths.
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("An argument contains NUL.");
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1).Append(c); slashes = 0; continue; }
            result.Append('\\', slashes).Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static byte[] ReadSpirv(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 20 || file.Length > 64 * 1024 * 1024 || file.Length % 4 != 0) throw new InvalidDataException("Missing or invalid SPIR-V output: " + path + ".");
        return File.ReadAllBytes(path);
    }
    private static string Hash(byte[] value) { using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(value)); }
    private static string FileHash(string path) { using (SHA256 sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return Hex(sha.ComputeHash(stream)); }
    private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }

    private static void WriteAtomic(string path, Action<Stream> write)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { write(stream); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else try { File.Move(temporary, path); } catch (IOException) { if (!File.Exists(path)) throw; File.Replace(temporary, path, null); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class IncludeMirror
    {
        private sealed class FileCopy
        {
            public string Original;
            public string Staged;
            public string Content;
            public string Hash;
            public string StockHash;
            public string CompatibilityFixup;
        }
        private readonly Dictionary<string, FileCopy> originals = new Dictionary<string, FileCopy>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileCopy> staged = new Dictionary<string, FileCopy>(StringComparer.OrdinalIgnoreCase);
        private readonly string job;
        private readonly List<string> originalRoots = new List<string>();
        public readonly List<string> Roots = new List<string>();
        public string Source { get; }

        private IncludeMirror(IncludeMirror shared, string source)
        {
            job = shared.job; originals = shared.originals; staged = shared.staged;
            originalRoots = shared.originalRoots; Roots = shared.Roots;
            FileCopy main;
            if (!originals.TryGetValue(Path.GetFullPath(source), out main)) throw new FileNotFoundException("Source is absent from the shader snapshot: " + source);
            Source = main.Staged;
        }
        public IncludeMirror ForSource(string source) { return new IncludeMirror(this, source); }

        public IncludeMirror(VulkanShaderRequest request, string job)
        {
            this.job = job;
            string source = Path.GetFullPath(request.SourcePath ?? throw new ArgumentException("A shader source is required."));
            originalRoots.Add(Path.GetFullPath(request.ContentDirectory ?? Path.GetDirectoryName(source)));
            foreach (string include in request.IncludeDirectories ?? new List<string>())
            {
                string root = Path.GetFullPath(include);
                if (!originalRoots.Contains(root, StringComparer.OrdinalIgnoreCase)) originalRoots.Add(root);
            }
            for (int i = 0; i < originalRoots.Count; i++)
            {
                string root = originalRoots[i];
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Shader include directory is missing: " + root + ".");
                string relativeRoot = "root" + i;
                Roots.Add(relativeRoot);
                foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(path);
                    bool hlsl = extension.Equals(".hlsl", StringComparison.OrdinalIgnoreCase) || extension.Equals(".hlsli", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".inl", StringComparison.OrdinalIgnoreCase) || extension.Equals(".inc", StringComparison.OrdinalIgnoreCase);
                    if (!hlsl && !path.Equals(source, StringComparison.OrdinalIgnoreCase)) continue;
                    if (originals.ContainsKey(path))
                    {
                        if (originals[path].Original != path) throw new InvalidDataException("Case-ambiguous HLSL include path: " + path + ".");
                        continue;
                    }
                    byte[] bytes = File.ReadAllBytes(path);
                    var copy = new FileCopy { Original = path, Staged = relativeRoot + "/" + path.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1).Replace('\\', '/'), Content = Decode(bytes), Hash = Hash(bytes), StockHash=Hash(bytes) };
                    originals.Add(path, copy); staged.Add(copy.Staged, copy);
                }
            }
            if(request.SourceGeneration!=null)
            {
                if(originalRoots.Count!=1)throw new ArgumentException("Source generations must capture extra includes as virtual source bytes instead of mutable include roots.");
                // Authenticate pristine inputs before any pack replaces them. The
                // adapters still receive those original dependency hashes.
                foreach(var copy in originals.Values)request.SourceGeneration.Authenticate(copy.Staged.Substring(6),copy.StockHash);
                foreach(var entry in request.SourceGeneration.Files)
                {
                    string path=Path.GetFullPath(Path.Combine(originalRoots[0],entry.Key.Replace('/',Path.DirectorySeparatorChar)));
                    FileCopy copy;
                    if(!originals.TryGetValue(path,out copy))
                    {
                        copy=new FileCopy{Original=path,Staged="root0/"+entry.Key};originals.Add(path,copy);staged.Add(copy.Staged,copy);
                    }
                    copy.Content=Decode(entry.Value);copy.Hash=Hash(entry.Value);
                }
            }
            FileCopy main;
            if (!originals.TryGetValue(source, out main)) throw new FileNotFoundException("Shader source must be inside a supplied content/include directory: " + source + ".");
            Source = main.Staged;
            foreach (FileCopy copy in originals.Values)
            {
                // NVIDIA's shipped HBAO header pastes punctuation around SLOT.
                // FXC permits that extension; standard preprocessing requires
                // only ordinary substitution inside register(SLOT).
                string compatible = VanillaHlslCompatibility.Apply(copy.Hash, copy.Content, out copy.CompatibilityFixup);
                compatible = Regex.Replace(compatible, @"register\s*\(\s*##\s*([A-Za-z_][A-Za-z0-9_]*)\s*##\s*\)", "register($1)");
                string text = Include.Replace(compatible, match =>
                {
                    string name = match.Groups[2].Value.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    FileCopy target = Resolve(name, Path.GetDirectoryName(copy.Original));
                    // Leave inactive platform includes unresolved; DXC diagnoses
                    // an unresolved include only when preprocessing reaches it.
                    return target == null ? match.Value : match.Groups[1].Value + "\"" + target.Staged + "\"";
                });
                string path = Path.Combine(job, copy.Staged.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            // Includes rewritten to rootN paths are relative to the job root.
            Roots.Insert(0, ".");
        }

        public List<ShaderDependency> Dependencies(string preprocessed, string dependencyText)
        {
            var dependencies = new SortedDictionary<string, FileCopy>(StringComparer.Ordinal);
            dependencies[Source] = staged[Source];
            var paths = SourceLine.Matches(preprocessed).Cast<Match>().Select(m => m.Groups[1].Value.Replace("\\\\", "\\")).ToList();
            // -M records macro-only includes that disappear from #line output.
            // DXC writes makefile escaping for spaces and line continuation.
            string make = dependencyText.Replace("\\\r\n", " ").Replace("\\\n", " ");
            int colon = make.IndexOf(':');
            if (colon >= 0) make = make.Substring(colon + 1);
            var token = new StringBuilder();
            for (int i = 0; i <= make.Length; i++)
            {
                if (i < make.Length && make[i] == '\\' && i + 1 < make.Length && (char.IsWhiteSpace(make[i + 1]) || make[i + 1] == '#')) { token.Append(make[++i]); continue; }
                if (i < make.Length && !char.IsWhiteSpace(make[i])) { token.Append(make[i]); continue; }
                if (token.Length > 0) { paths.Add(token.ToString()); token.Clear(); }
            }
            foreach (string rawPath in paths)
            {
                string path = rawPath.Replace('\\', '/');
                while (path.Contains("//")) path = path.Replace("//", "/");
                if (Path.IsPathRooted(path))
                {
                    string prefix = job.Replace('\\', '/') + "/";
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) path = path.Substring(prefix.Length);
                }
                while (path.StartsWith("./", StringComparison.Ordinal)) path = path.Substring(2);
                FileCopy copy;
                if (staged.TryGetValue(path, out copy)) dependencies[copy.Staged] = copy;
            }
            return dependencies.Select(d => new ShaderDependency { Path = d.Key, Sha256 = d.Value.Hash, CompatibilityFixup = d.Value.CompatibilityFixup }).ToList();
        }

        public List<ShaderDependency> StockDependencies(List<ShaderDependency> dependencies)
            =>dependencies.Where(d=>staged[d.Path].StockHash!=null).Select(d=>new ShaderDependency{Path=d.Path,Sha256=staged[d.Path].StockHash,CompatibilityFixup=d.CompatibilityFixup}).ToList();

        private FileCopy Resolve(string include, string directory)
        {
            FileCopy copy;
            if (originals.TryGetValue(Path.GetFullPath(Path.Combine(directory, include)), out copy)) return copy;
            foreach (string root in originalRoots) if (originals.TryGetValue(Path.GetFullPath(Path.Combine(root, include)), out copy)) return copy;
            return null;
        }
        private static string Decode(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes)) using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
        }
    }
}
