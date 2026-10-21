using System;
using System.IO;
using System.Linq;
using ClientPlugin.Rendering.Shaders;

if (args.Length < 2) throw new ArgumentException("ShaderCompilerTests <bundled-tool-directory> <installed-game-shader-directory>");
string testDirectory = Path.Combine(Path.GetTempPath(), "se-vulkan-shader-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDirectory);
int assertions = 0;
void Check(bool condition, string description) { if (!condition) throw new Exception("FAIL: " + description); assertions++; Console.WriteLine("PASS: " + description); }
void Reject(Action action, string text, string description)
{
    try { action(); } catch (Exception error) { Check(error.Message.Contains(text, StringComparison.OrdinalIgnoreCase), description + " (" + error.GetType().Name + ")"); return; }
    throw new Exception("FAIL: " + description + " did not reject input.");
}
try
{
    string content = Path.Combine(testDirectory, "Content");
    Directory.CreateDirectory(Path.Combine(content, "Inc", "deep"));
    string includePath = Path.Combine(content, "Inc", "lower.hlsli");
    string deepIncludePath = Path.Combine(content, "Inc", "deep", "shared.hlsli");
    File.WriteAllText(includePath, "#include \"Deep/Shared.hlsli\"\n#define MERGE(a,b) a##b\n#define MY_SLOT 0\n");
    File.WriteAllText(deepIncludePath, "#define SCALE 1.0\n");
    string source = Path.Combine(content, "CaseAndRegisters.hlsl");
    File.WriteAllText(source, """
        #include <inc/Lower.hlsli>
        cbuffer Data : register(MERGE(b, MY_SLOT)) { float2 padding; float2x3 transform; float4 values[2]; float2 tail; };
        Texture2D<float4> Image : register(t0);
        SamplerState Linear : register(s0);
        RWTexture2D<float4> Output : register(u0);
        [numthreads(1,1,1)] void main(uint3 id : SV_DispatchThreadID) {
            float2 position = mul(transform, float3(values[0].xy, 1));
            Output[id.xy] = Image.SampleLevel(Linear, position + padding + tail, 0) * values[1] * SCALE;
        }
        """);
    string toolCopy = Path.Combine(testDirectory, "Tools");
    Directory.CreateDirectory(toolCopy);
    foreach (string path in Directory.GetFiles(args[0], "*", SearchOption.AllDirectories))
    {
        if (path.Contains(Path.DirectorySeparatorChar + "Legal" + Path.DirectorySeparatorChar)) continue;
        string destination = Path.Combine(toolCopy, Path.GetRelativePath(args[0], path));
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(path, destination);
    }
    var compiler = new VulkanShaderCompiler(toolCopy, Path.Combine(testDirectory, "Cache"));
    var request = new VulkanShaderRequest { SourcePath = source, ContentDirectory = content, Profile = "cs_5_0", EntryPoint = "main" };
    var first = compiler.Compile(request);
    Check(!first.CacheHit && first.Metadata.Profile == "cs_6_0", "legacy shader model upgrades to SPIR-V-capable shader model");
    Check(first.Metadata.Descriptors.Select(d => d.Binding).SequenceEqual(new uint[] { 0, 128, 256, 384 }), "b0/t0/s0/u0 map to distinct reflected descriptor ranges");
    Check(first.Metadata.Descriptors.Select(d => d.RegisterClass).SequenceEqual(new[] { 'b', 't', 's', 'u' }), "reflection maps descriptors back to their HLSL register classes");
    Check(first.Metadata.Dependencies.Count == 3, "case-insensitive and nested include resolution tracks three source dependencies (" + string.Join(",", first.Metadata.Dependencies.Select(d => d.Path)) + ")");
    var buffer = first.Metadata.Descriptors.Single(d => d.DescriptorType == "UniformBuffer");
    Check(buffer.Members.Single(m => m.Name == "transform").MatrixStride != 0 && buffer.Members.Single(m => m.Name == "values").ArrayStride == 16,
        "reflected constant buffer retains matrix and array strides");
    Check(buffer.BlockSize >= buffer.Members.Max(m => m.Offset + m.Size), "constant buffer range covers all reflected members");
    Check(first.Metadata.ExecutionModel == 5 && first.Metadata.WorkgroupSizeX == 1 && first.Metadata.WorkgroupSizeY == 1 && first.Metadata.WorkgroupSizeZ == 1 && first.Metadata.Capabilities.Contains(1),
        "reflection records compute execution model, workgroup dimensions and required capabilities");
    Check(compiler.Compile(request).CacheHit, "identical preprocessed content reuses a validated cache entry");
    string originalKey = first.Metadata.CacheKey;
    File.WriteAllBytes(Path.Combine(toolCopy, "dependency-hash-test.dll"), new byte[] { 1, 2, 3 });
    var changedTool = compiler.Compile(request);
    Check(!changedTool.CacheHit && changedTool.Metadata.CacheKey != originalKey && changedTool.Metadata.ToolchainSha256 != first.Metadata.ToolchainSha256,
        "tool/runtime binary hashes invalidate the shader cache");
    File.AppendAllText(deepIncludePath, "// comment-only nested dependency change\n");
    var editedInclude = compiler.Compile(request);
    Check(!editedInclude.CacheHit && editedInclude.Metadata.CacheKey != originalKey, "raw transitive include content invalidates the cache even when preprocessing removes comments");
    request.Defines["UNUSED_CONTENT_DEFINE"] = "1";
    var editedDefine = compiler.Compile(request);
    Check(!editedDefine.CacheHit && editedDefine.Metadata.CacheKey != editedInclude.Metadata.CacheKey, "definition changes invalidate the cache even when unused in this variant");
    request.NativeAbiVersion = 2;
    var editedAbi = compiler.Compile(request);
    Check(!editedAbi.CacheHit && editedAbi.Metadata.CacheKey != editedDefine.Metadata.CacheKey, "native ABI changes invalidate the shader cache");
    File.WriteAllBytes(editedAbi.SpirvPath, new byte[20]);
    Check(!compiler.Compile(request).CacheHit, "a corrupt SPIR-V cache entry is rebuilt");
    string originalSource = File.ReadAllText(source);
    File.WriteAllText(source, originalSource.Replace("register(t0)", "register(t0, space2)"));
    var otherSpace = compiler.Compile(request);
    Check(otherSpace.Metadata.Descriptors.Single(d => d.RegisterClass == 't').Set == 2 && otherSpace.Metadata.Descriptors.Single(d => d.RegisterClass == 't').Binding == 128,
        "register spaces retain descriptor sets with per-space register shifts");
    File.WriteAllText(source, originalSource.Replace("register(t0)", "register(t128)"));
    Reject(() => compiler.Compile(request), "128-register range", "overlapping HLSL register ranges are rejected before compilation");
    File.WriteAllText(source, originalSource + "\nfloat syntax_error = invalid_name;\n");
    Reject(() => compiler.Compile(request), "invalid_name", "DXC errors preserve actionable source diagnostics");
    Reject(() => SpirvMetadataReader.Reflect(new byte[20], new ShaderMetadata()), "header", "malformed SPIR-V is rejected by reflection");
    string installed = Path.GetFullPath(args[1]);
    File.WriteAllText(source, originalSource);
    request.NativeAbiVersion = 1;
    using (var batch = compiler.CreateBatch(content))
    {
        var snapshot = batch.Compile(request);
        File.AppendAllText(deepIncludePath, "// changed after the immutable batch snapshot\n");
        Check(batch.Compile(request).Metadata.CacheKey == snapshot.Metadata.CacheKey, "batch compilation keeps an immutable content snapshot across source edits");
        Check(compiler.Compile(request).Metadata.CacheKey != snapshot.Metadata.CacheKey, "ordinary compilation still detects edits outside the batch");
        batch.VerifyBatchToolchain();
        File.WriteAllBytes(Path.Combine(toolCopy, "batch-tool-change.dll"), new byte[] { 4, 5, 6 });
        Reject(batch.VerifyBatchToolchain, "tools changed", "batch tool mutations are detected before accepting its report");
        File.Delete(Path.Combine(toolCopy, "batch-tool-change.dll"));
    }
    Check(!Directory.EnumerateFileSystemEntries(Path.Combine(testDirectory, "Cache", "Snapshots")).Any(), "disposing a batch removes only its owned source snapshot");
    var stockHashes=Directory.GetFiles(content,"*",SearchOption.AllDirectories).ToDictionary(
        p=>Path.GetRelativePath(content,p).Replace('\\','/'),p=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant());
    byte[] replacement=System.Text.Encoding.UTF8.GetBytes("#include <Generated/pack.hlsli>\n#define SCALE PACK_SCALE\n");
    var replacements=new System.Collections.Generic.Dictionary<string,byte[]>{["Inc/deep/shared.hlsli"]=replacement,["Generated/pack.hlsli"]=System.Text.Encoding.UTF8.GetBytes("#define PACK_SCALE 8.0\n")};
    request.SourceGeneration=new ShaderSourceGeneration("pack-order-A;priority=1",replacements,stockHashes);
    string fingerprint=request.SourceGeneration.Fingerprint;replacement[0]=0;replacements.Clear();
    var generated=compiler.Compile(request);
    Check(generated.Metadata.Dependencies.Any(d=>d.Path=="root0/Generated/pack.hlsli"),"virtual pack includes resolve inside the same mirror as relative stock includes");
    Check(generated.Metadata.Dependencies.Single(d=>d.Path=="root0/Inc/deep/shared.hlsli").Sha256!=stockHashes["Inc/deep/shared.hlsli"],"relative stock includes use captured overlay bytes instead of disk source");
    Check(compiler.Compile(request).CacheHit && request.SourceGeneration.Fingerprint==fingerprint,"source generation copies caller bytes and keeps stable cache identity");
    request.GpuOpaque=true;request.GpuOpaqueBaseModule=new string('0',64);request.GpuOpaqueBaseDependencies=new System.Collections.Generic.List<ShaderDependency>();
    Reject(()=>compiler.Compile(request),"authenticated catalog snapshot","pack generations preserve GPU-owned stock authentication rather than bypassing the adapter");
    request.GpuOpaque=false;
    byte[] pristine=File.ReadAllBytes(deepIncludePath);File.AppendAllText(deepIncludePath,"// tampered pristine input\n");
    Reject(()=>compiler.Compile(request),"authenticated generation","overrides cannot hide mutated pristine stock inputs or bypass authentication on a cache hit");
    File.WriteAllBytes(deepIncludePath,pristine);
    using(var batch=compiler.CreateBatch(content))Reject(()=>batch.Compile(request),"Batch requests","a stock batch cannot silently discard a requested source generation");
    Reject(()=>new ShaderSourceGeneration("escape",new System.Collections.Generic.Dictionary<string,byte[]>{["../escape.hlsl"]=new byte[0]},stockHashes),"contained","virtual source paths cannot escape the captured content root");
    request.SourceGeneration=new ShaderSourceGeneration("pack-order-B;priority=2",new System.Collections.Generic.Dictionary<string,byte[]>{["Unused.hlsli"]=new byte[0]},stockHashes);
    var differentGeneration=compiler.Compile(request);
    request.SourceGeneration=new ShaderSourceGeneration("pack-order-C;priority=3",new System.Collections.Generic.Dictionary<string,byte[]>{["Unused.hlsli"]=new byte[0]},stockHashes);
    Check(compiler.Compile(request).Metadata.CacheKey!=differentGeneration.Metadata.CacheKey,"complete pack identity participates in the derived cache key even for unchanged active HLSL");
    request.SourceGeneration=null;
    string aliases = Path.Combine(content, "InactiveAliases.hlsl");
    File.WriteAllText(aliases, "cbuffer Dead : register(b1) { float4 unused; }; cbuffer Live : register(b1) { float4 color; }; Texture2D<float4> Image : register(t0); SamplerState Filter; float4 main(float2 uv : TEXCOORD0) : SV_Target { return color * Image.Sample(Filter, uv); }");
    var activeAliases = compiler.Compile(new VulkanShaderRequest { SourcePath = aliases, ContentDirectory = content, EntryPoint = "main", Profile = "ps_5_0" });
    Check(activeAliases.Metadata.Descriptors.Count(d => d.Binding == 1) == 1 && !activeAliases.Metadata.Descriptors.Any(d => d.Name == "Dead") && activeAliases.Metadata.Descriptors.Single(d => d.Name == "Filter").RegisterClass == 's',
        "inactive register aliases are eliminated and implicit sampler bindings use the sampler band");
    string pasted = Path.Combine(content, "RegisterPaste.hlsl");
    File.WriteAllText(pasted, "#define REGISTER(SLOT) : register(##SLOT##)\ncbuffer Value REGISTER(b0) { float4 color; }; float4 main() : SV_Target { return color; }");
    Check(compiler.Compile(new VulkanShaderRequest { SourcePath = pasted, ContentDirectory = content, EntryPoint = "main", Profile = "ps_5_0" }).Metadata.Descriptors.Single().Binding == 0,
        "legacy HBAO register punctuation paste preserves the intended binding");
    string counters = Path.Combine(content, "CounterBuffers.hlsl");
    File.WriteAllText(counters, "AppendStructuredBuffer<float4> Added : register(u1); ConsumeStructuredBuffer<float4> Removed : register(u2); [numthreads(1,1,1)] void main() { Added.Append(Removed.Consume()); }");
    var counterLayout = compiler.Compile(new VulkanShaderRequest { SourcePath = counters, ContentDirectory = content, EntryPoint = "main", Profile = "cs_5_0" });
    Check(counterLayout.Metadata.Descriptors.Count(d => d.RegisterClass == 'c') == 2 && counterLayout.Metadata.Descriptors.Single(d => d.Name == "Added").CounterBinding == 513 && counterLayout.Metadata.Descriptors.Single(d => d.Name == "Removed").CounterBinding == 514,
        "append/consume UAV counters get separate deterministic reflected descriptors");
    File.WriteAllText(counters, "RWStructuredBuffer<float4> Counted : register(u3); [numthreads(1,1,1)] void main() { Counted.IncrementCounter(); }");
    var counterOnly = compiler.Compile(new VulkanShaderRequest { SourcePath = counters, ContentDirectory = content, EntryPoint = "main", Profile = "cs_5_0" });
    Check(counterOnly.Metadata.Descriptors.Count == 1 && counterOnly.Metadata.Descriptors.Single().CounterFor == "Counted" && counterOnly.Metadata.Descriptors.Single().Binding == 515,
        "counter-only access retains its owner identity after the data descriptor is optimized out");
    var installedPixel = compiler.Compile(new VulkanShaderRequest
    {
        SourcePath = Path.Combine(installed, "Primitives", "Sprites.hlsl"), ContentDirectory = installed, Profile = "ps_5_0", EntryPoint = "__pixel_shader"
    });
    Check(installedPixel.Metadata.Descriptors.Any(d => d.Name.Contains("SpriteTexture")) && installedPixel.Metadata.Dependencies.Count > 4,
        "installed sprite pixel HLSL compiles and validates against Vulkan 1.4");
    var installedVertex = compiler.Compile(new VulkanShaderRequest
    {
        SourcePath = Path.Combine(installed, "Primitives", "Sprites.hlsl"), ContentDirectory = installed, Profile = "vs_5_0", EntryPoint = "__vertex_shader"
    });
    Check(installedVertex.Metadata.Descriptors.Any(d => d.RegisterClass == 'b') && installedVertex.Metadata.Interface.Any(d => d.Direction == "Input"),
        "installed sprite vertex HLSL preserves constant-buffer layout and vertex inputs");
    Check(!Directory.EnumerateFileSystemEntries(Path.Combine(testDirectory, "Cache", "Jobs")).Any(), "compile workspaces are removed after success and failure");
    Console.WriteLine("Shader compiler checks passed: " + assertions + ".");
}
finally
{
    string resolved = Path.GetFullPath(testDirectory);
    string expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
    if (Path.GetDirectoryName(resolved) == expectedParent && Path.GetFileName(resolved).StartsWith("se-vulkan-shader-test-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
}
