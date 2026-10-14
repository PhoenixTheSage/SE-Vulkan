using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using SharpDX.Direct3D;
using VRage.FileSystem;

// Calls only the stock CPU permutation builders. Never calls Generate/ExecuteDataList,
// shader Create, MyRender11.Initialize, or any device factory.
internal static class Program
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Dictionary<string, Variant> Variants = new(StringComparer.Ordinal);
    static Assembly Render;
    static string ShaderRoot;
    public static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("ShaderSuiteCapture <game-Bin64> <inventory.json>");
        string bin = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(bin, name.Name + ".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
        Run(bin, Path.GetFullPath(args[1]));
    }
    static void Run(string bin, string output)
    {
        ShaderRoot = Path.Combine(Path.GetDirectoryName(bin), "Content", "Shaders");
        MyFileSystem.Init(Path.GetDirectoryName(ShaderRoot), Path.Combine(Path.GetDirectoryName(output), "CaptureUserData"));
        Render = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "VRage.Render11.dll"));
        Type generator = Render.GetType("VRage.Render11.Shader.MyShaderCacheGenerator", true);
        Type configuration = Render.GetTypes().Single(t => t.Name == "CacheGenerator");
        using var xml = File.OpenRead(Path.Combine(ShaderRoot, "CacheGenerator.xml"));
        object descriptor = new XmlSerializer(configuration).Deserialize(xml);
        Invoke(generator, "GenerateInternal", null, descriptor, null);
        Invoke(generator, "GenerateMaterials", null, descriptor, null);
        foreach (object item in (IEnumerable)generator.GetField("m_dataList", All).GetValue(null))
        {
            string source = (string)Field(item, "Filepath");
            string profile = Field(item, "Profile").ToString();
            Add(source, profile, (IEnumerable)Field(item, "Macros"), "StockCacheGenerator", (string)Field(item, "Descriptor"), (string)Field(item, "VertexLayoutString"));
        }
        CaptureGeometry2();
        CaptureRuntimeMaterialFlags();
        CaptureMergeInstancing();
        // Files intentionally excluded by @skipCache still belong in the audit.
        var files = Directory.GetFiles(ShaderRoot, "*", SearchOption.AllDirectories).Where(p => new[] { ".hlsl", ".hlsli", ".inl", ".xml" }.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(p => p).Select(p => new
        {
            Path = Relative(p), Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))),
            Role = Variants.Values.Any(v => v.Source == Relative(p)) ? "Entrypoint" : LegacyReason(Relative(p)) != null ? "LegacyUnreferenced" : Path.GetExtension(p) == ".hlsl" ? "UnscheduledHlsl" : "IncludeOrConfiguration",
            AuditReason = LegacyReason(Relative(p)),
            SkipCache = File.ReadAllText(p).Contains("@skipCache", StringComparison.Ordinal)
        }).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonSerializer.Serialize(new { SchemaVersion = 1, GameAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(bin, "VRage.Render11.dll")))), Files = files, Variants = Variants.Values.OrderBy(v => v.Source).ThenBy(v => v.Id).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Captured {Variants.Count} unique variants across {files.Count(f => f.Role == "Entrypoint")} entrypoint files; {files.Count(f => f.Role == "LegacyUnreferenced")} legacy unreferenced files; {files.Count(f => f.Role == "UnscheduledHlsl")} unexplained HLSL files.");
    }
    static string LegacyReason(string path) => path switch
    {
        "EnvProbe/EnvProbe.hlsl" => "@skipCache; no current runtime shader creation references; obsolete d_ggx/hammersley/g_smithschlick calls. Active probe shaders are separate catalog entries.",
        "Geometry/Materials/AlphaMaskedArray/Vertex.hlsl" or "Geometry/Materials/AlphaMaskedArray/Pixel.hlsl" => "Absent from CacheGenerator materials and current MyMaterialShaders/GeometryStage2 technique mappings. Active impostor/texture-array variants use AlphaMasked.",
        "Geometry/Materials/Test/Vertex.hlsl" or "Geometry/Materials/Test/Pixel.hlsl" => "Absent from stock material maps; uses obsolete VertexInterface/VertexStageOutputInterface and smoothness/id members. Retained in the source audit, excluded from runtime coverage.",
        _ => null
    };
    static void CaptureRuntimeMaterialFlags()
    {
        Type layouts = Render.GetType("VRageRender.MyVertexLayouts", true);
        Type component = Render.GetType("VRageRender.MyVertexInputComponentType", true);
        MethodInfo getLayout = layouts.GetMethods(All).Single(m => m.Name == "GetLayout" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.GetElementType() == component);
        foreach (var variant in Variants.Values.ToArray())
        {
            bool alpha = variant.Source.StartsWith("Geometry/Materials/AlphaMasked/", StringComparison.Ordinal);
            bool standard = variant.Source.StartsWith("Geometry/Materials/Standard/", StringComparison.Ordinal);
            if ((!alpha && !standard) || string.IsNullOrEmpty(variant.VertexLayout) || variant.VertexLayout == "Stock runtime components") continue;
            var names = variant.VertexLayout.Split(';');
            Array components = Array.CreateInstance(component, names.Length + 1);
            for (int i = 0; i < names.Length; i++) components.SetValue(Enum.Parse(component, names[i]), i);
            components.SetValue(Enum.Parse(component, "TEXINDICES"), names.Length);
            object layout = getLayout.Invoke(null, new object[] { components });
            object info = layout.GetType().GetProperty("Info", All).GetValue(layout);
            var definitions = new SortedDictionary<string, string>(variant.Defines, StringComparer.Ordinal);
            foreach (ShaderMacro macro in (IEnumerable)Field(info, "Macros")) definitions[macro.Name] = macro.Definition ?? "";
            AddDefinitions(variant, definitions, "RuntimeTextureArrays", variant.VertexLayout + ";TEXINDICES");
            if (alpha)
            {
                definitions["ALPHA_MASK_ARRAY"] = "";
                AddDefinitions(variant, definitions, "RuntimeImpostors", variant.VertexLayout + ";TEXINDICES");
                definitions = new SortedDictionary<string, string>(variant.Defines, StringComparer.Ordinal) { ["ALPHA_MASK_ARRAY"] = "" };
                AddDefinitions(variant, definitions, "RuntimeImpostors", variant.VertexLayout);
            }
        }
    }
    static void CaptureMergeInstancing()
    {
        foreach (int pass in new[] { 0, 1, 2, 3 })
        foreach (bool single in new[] { false, true })
        foreach (bool metal in new[] { false, true })
        foreach (string quality in new[] { "", "MQ", "LQ" })
        {
            var macros = new List<ShaderMacro> { new("RENDERING_PASS", pass), new("USE_MERGE_INSTANCING", null), new("VERTEX_COMPONENTS_DECLARATIONS", ""), new("TRANSFER_VERTEX_COMPONENTS", "") };
            if (single) macros.Add(new("USE_SINGLE_INSTANCE", null));
            if (metal) macros.Add(new("METALNESS_COLORABLE", null));
            if (quality != "") macros.Add(new(quality, null));
            if (pass == 1) macros.Add(new("DEPTH_ONLY", null));
            foreach (string stage in new[] { "Vertex", "Pixel" }) Add(Path.Combine(ShaderRoot, "Geometry", "Materials", "Standard", stage + ".hlsl"), stage == "Vertex" ? "vs_5_0" : "ps_5_0", macros, "RuntimeMergeInstancing", "MyMaterialMergeGroup", "");
        }
    }
    static void AddDefinitions(Variant original, SortedDictionary<string, string> definitions, string origin, string layout)
        => Add(Path.Combine(ShaderRoot, original.Source), original.Profile, definitions.Select(d => new ShaderMacro(d.Key, d.Value)).ToArray(), origin, original.Description, layout);
    static void CaptureGeometry2()
    {
        Type manager = Render.GetType("VRage.Render11.GeometryStage2.Rendering.MyShaderBundleManager", true);
        object instance = Activator.CreateInstance(manager, true);
        Type passType = manager.GetMethod("GetVertexInputComponents", All).GetParameters()[0].ParameterType;
        Type techniqueType = manager.GetMethod("AddMacrosForTechnique", All).GetParameters()[0].ParameterType;
        Type shaderType = manager.GetNestedType("MyShaderType", All);
        foreach (string passName in new[] { "GBuffer", "Depth", "Forward", "Highlight", "Transparent", "TransparentForDecals" })
        foreach (string techniqueName in new[] { "MESH", "ALPHA_MASKED", "ALPHA_MASKED_SINGLE_SIDED", "DECAL", "DECAL_NOPREMULT", "DECAL_CUTOUT", "GLASS", "HOLO", "SHIELD", "SHIELD_LIT" })
        foreach (bool metal in new[] { false, true })
        for (int textures = 0; textures < (techniqueName.StartsWith("DECAL") ? 8 : 1); textures++)
        {
            object pass = Enum.Parse(passType, passName), technique = Enum.Parse(techniqueType, techniqueName);
            var macros = new List<ShaderMacro>();
            if (metal) macros.Add(new ShaderMacro("METALNESS_COLORABLE", null));
            Invoke(manager, "AddMacrosForRenderingPass", instance, pass, macros);
            Invoke(manager, "AddMacrosForTechnique", instance, technique, (textures & 1) != 0, (textures & 2) != 0, (textures & 4) != 0, macros);
            object components = Invoke(manager, "GetVertexInputComponents", instance, pass);
            Invoke(manager, "AddMacrosVertexInputComponents", instance, components, macros);
            foreach (string stage in new[] { "VERTEX", "PIXEL" })
            {
                string file = (string)Invoke(manager, "GetShaderFilepath", instance, technique, Enum.Parse(shaderType, "SHADER_TYPE_" + stage));
                Add(Path.Combine(ShaderRoot, file.Replace('\\', Path.DirectorySeparatorChar)), stage == "VERTEX" ? "vs_5_0" : "ps_5_0", macros, "GeometryStage2", techniqueName + "/" + passName, "Stock runtime components");
            }
        }
    }
    static void Add(string path, string profile, IEnumerable macros, string origin, string description, string layout, string entry = null)
    {
        var defines = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (object macro in macros) { var m = (ShaderMacro)macro; defines[m.Name] = (m.Definition ?? "").Replace("\\\r\n", " ").Replace("\\\n", " ").Replace('\r', ' ').Replace('\n', ' '); }
        string source = Relative(path);
        entry ??= profile[..2] switch { "vs" => "__vertex_shader", "ps" => "__pixel_shader", "gs" => "__geometry_shader", "cs" => "__compute_shader", _ => throw new ArgumentException(profile) };
        string key = source + "\n" + profile + "\n" + entry + "\n" + JsonSerializer.Serialize(defines);
        string id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        if (Variants.TryGetValue(id, out var found)) { if (!found.Origins.Contains(origin)) found.Origins.Add(origin); return; }
        Variants[id] = new Variant { Id = id, Source = source, Profile = profile, EntryPoint = entry, Defines = defines, Origins = new() { origin }, Description = description, VertexLayout = layout };
    }
    static string Relative(string path) => Path.GetRelativePath(ShaderRoot, Path.GetFullPath(path)).Replace('\\', '/');
    static object Field(object value, string name) => value.GetType().GetField(name, All).GetValue(value);
    static object Invoke(Type type, string name, object instance, params object[] arguments) => type.GetMethod(name, All).Invoke(instance, arguments);
    sealed class Variant
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string Profile { get; set; }
        public string EntryPoint { get; set; }
        public SortedDictionary<string, string> Defines { get; set; }
        public List<string> Origins { get; set; }
        public string Description { get; set; }
        public string VertexLayout { get; set; }
    }
}
