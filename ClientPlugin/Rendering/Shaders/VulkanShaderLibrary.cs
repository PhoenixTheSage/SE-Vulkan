using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;
using System.Text;

namespace ClientPlugin.Rendering.Shaders;

// Layouts and capabilities are inspected before a pass creates its native
// pipeline. Loading an artifact does not imply that the pass is implemented.
public sealed class VulkanShaderLibrary
{
    private sealed class Module { public string MetadataSha256; }
    private sealed class Variant { public string Source, Profile, Entry, Module, CacheKey;public readonly Dictionary<string,string> Defines=new Dictionary<string,string>(StringComparer.Ordinal); }
    private readonly Dictionary<string, Module> modules = new Dictionary<string, Module>(StringComparer.Ordinal);
    private readonly Dictionary<string, Variant> variants = new Dictionary<string, Variant>(StringComparer.Ordinal);
    private readonly Dictionary<string,string> sourceHashes=new Dictionary<string,string>(StringComparer.Ordinal);
    private readonly string directory,contentDirectory;
    public int VariantCount => variants.Count;
    public int ModuleCount => modules.Count;
    public IEnumerable<string> VariantIds => variants.Keys;
    public ShaderSourceGeneration CaptureSourceGeneration(string packIdentity,IDictionary<string,byte[]> sources)
        =>new ShaderSourceGeneration(packIdentity,sources,sourceHashes.ToDictionary(p=>p.Key.Substring(6),p=>p.Value,StringComparer.OrdinalIgnoreCase));

    public VulkanShaderRequest DerivedRequest(string variantId,ShaderSourceGeneration generation,IDictionary<string,string> packDefines=null)
    {
        if(generation==null)throw new ArgumentNullException(nameof(generation));
        Load(variantId); // Authenticate the selected stock module before deriving it.
        foreach(var dependency in sourceHashes)generation.Authenticate(dependency.Key.Substring(6),dependency.Value);
        var variant=variants[variantId];var defines=new Dictionary<string,string>(variant.Defines,StringComparer.Ordinal);
        if(packDefines!=null)foreach(var define in packDefines)defines[define.Key]=define.Value;
        return new VulkanShaderRequest{SourcePath=ResolveInside(contentDirectory,variant.Source),ContentDirectory=contentDirectory,
            Profile=variant.Profile,EntryPoint=variant.Entry,Defines=defines,SourceGeneration=generation};
    }

    public VulkanShaderLibrary(string directory, string contentDirectory, string gameAssemblyPath = null, string catalogSha256 = null)
    {
        this.directory = Path.GetFullPath(directory);this.contentDirectory=Path.GetFullPath(contentDirectory);
        if (catalogSha256 != null && FileHash(Path.Combine(this.directory, "catalog.xml.gz")) != HashId(catalogSha256)) throw new InvalidDataException("Shader catalog hash mismatch.");
        var settings = SafeXml(64 * 1024 * 1024);
        using (var stream = File.OpenRead(Path.Combine(this.directory, "catalog.xml.gz")))
        using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
        using (var reader = XmlReader.Create(gzip, settings))
        {
            reader.MoveToContent();
            if (reader.Name != "ShaderSuite" || reader.GetAttribute("Schema") != "1" || reader.GetAttribute("Api") != "1.4") throw new InvalidDataException("Incompatible Vulkan shader catalog.");
            if (gameAssemblyPath != null && FileHash(gameAssemblyPath) != reader.GetAttribute("AssemblySha256")) throw new InvalidDataException("Shader catalog game assembly mismatch; rebuild for this game version.");
            int total = int.Parse(reader.GetAttribute("Total"), CultureInfo.InvariantCulture);
            Variant currentVariant=null;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.Name == "Source")
                {
                    string source = ResolveInside(contentDirectory, reader.GetAttribute("Path"));
                    if (FileHash(source) != reader.GetAttribute("Sha256")) throw new InvalidDataException("Shader catalog source mismatch: " + source);
                    sourceHashes.Add("root0/"+reader.GetAttribute("Path").Replace('\\','/'),reader.GetAttribute("Sha256"));
                }
                else if (reader.Name == "Module") modules.Add(HashId(reader.GetAttribute("Sha256")), new Module { MetadataSha256 = HashId(reader.GetAttribute("MetadataSha256")) });
                else if (reader.Name == "Variant") {currentVariant=new Variant
                {
                    Source = reader.GetAttribute("Source"), Profile = reader.GetAttribute("Profile"), Entry = reader.GetAttribute("Entry"),
                    Module = HashId(reader.GetAttribute("Module")), CacheKey = HashId(reader.GetAttribute("CacheKey"))
                };variants.Add(HashId(reader.GetAttribute("Id")),currentVariant);}
                else if(reader.Name=="Define"&&currentVariant!=null)currentVariant.Defines.Add(reader.GetAttribute("Name"),reader.GetAttribute("Value")??"");
            }
            if (total != variants.Count || variants.Values.Any(v => !modules.ContainsKey(v.Module))) throw new InvalidDataException("Incomplete Vulkan shader catalog.");
            foreach (string module in modules.Keys)
                if (!File.Exists(Path.Combine(this.directory, "Modules", module + ".spv")) || !File.Exists(Path.Combine(this.directory, "Modules", module + ".xml"))) throw new FileNotFoundException("Shader catalog module assets are missing: " + module);
        }
    }

    public VulkanShaderArtifact Load(string variantId)
    {
        Variant variant;
        if (!variants.TryGetValue(variantId, out variant)) throw new KeyNotFoundException("Unknown Vulkan shader variant: " + variantId);
        string spirv = Path.Combine(directory, "Modules", variant.Module + ".spv"), metadata = Path.Combine(directory, "Modules", variant.Module + ".xml");
        if (FileHash(spirv) != variant.Module || FileHash(metadata) != modules[variant.Module].MetadataSha256) throw new InvalidDataException("Vulkan shader artifact hash mismatch: " + variantId);
        ShaderMetadata layout;
        using (var reader = XmlReader.Create(metadata, SafeXml(8 * 1024 * 1024))) layout = (ShaderMetadata)new XmlSerializer(typeof(ShaderMetadata)).Deserialize(reader);
        if (layout.SchemaVersion != ShaderMetadata.CurrentSchemaVersion || layout.EntryPoint != variant.Entry || layout.SpirvSha256 != variant.Module) throw new InvalidDataException("Shader artifact metadata mismatch.");
        SpirvMetadataReader.Reflect(File.ReadAllBytes(spirv), layout);
        return new VulkanShaderArtifact { Id = variantId, Source = variant.Source, Profile = variant.Profile, CacheKey = variant.CacheKey, SpirvPath = spirv, Layout = layout };
    }
    public VulkanShaderRequest GpuOpaqueRequest(string variantId)
    {
        var original=Load(variantId);var variant=variants[variantId];
        return new VulkanShaderRequest{SourcePath=ResolveInside(contentDirectory,variant.Source),ContentDirectory=contentDirectory,Profile=variant.Profile,EntryPoint=variant.Entry,
            Defines=new Dictionary<string,string>(variant.Defines,StringComparer.Ordinal),GpuOpaque=true,GpuOpaqueBaseModule=original.Layout.SpirvSha256,
            GpuOpaqueBaseDependencies=sourceHashes.Select(p=>new ShaderDependency{Path=p.Key,Sha256=p.Value}).ToList()};
    }
    public VulkanShaderRequest HbaoBorderRequest(string variantId){
        var original=Load(variantId);var variant=variants[variantId];
        return new VulkanShaderRequest{SourcePath=ResolveInside(contentDirectory,variant.Source),ContentDirectory=contentDirectory,Profile=variant.Profile,EntryPoint=variant.Entry,
            Defines=new Dictionary<string,string>(variant.Defines,StringComparer.Ordinal),HbaoNegativeBorder=true,HbaoBaseModule=original.Layout.SpirvSha256,
            HbaoBaseDependencies=sourceHashes.Select(p=>new ShaderDependency{Path=p.Key,Sha256=p.Value}).ToList()};
    }
    public VulkanShaderRequest GpuOpaqueExtraRequest(uint id,string material,string stage,Dictionary<string,string> defines) {
        var original=LoadExtra(id,material,stage,defines);
        return new VulkanShaderRequest{SourcePath=Path.Combine(contentDirectory,"Geometry","Materials",material,stage+".hlsl"),ContentDirectory=contentDirectory,Profile=stage=="Vertex"?"vs_5_0":"ps_5_0",EntryPoint=stage=="Vertex"?"__vertex_shader":"__pixel_shader",Defines=new Dictionary<string,string>(defines,StringComparer.Ordinal),GpuOpaque=true,GpuOpaqueBaseModule=original.Layout.SpirvSha256,GpuOpaqueBaseDependencies=sourceHashes.Select(p=>new ShaderDependency{Path=p.Key,Sha256=p.Value}).ToList()};
    }
    public static string ExtraDefinitionsHash(string material,string stage,Dictionary<string,string> defines)
    {
        string identity=material+"\n"+stage+"\n"+string.Join("\n",defines.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value));
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").ToLowerInvariant();
    }
    public VulkanShaderArtifact LoadExtra(uint id,string material,string stage,Dictionary<string,string> defines)
    {
        string addon=Path.Combine(directory,"Extra"),module=null,metadataHash=null;
        using(var coverage=XmlReader.Create(Path.Combine(directory,"coverage.xml"),SafeXml(4096)))
        {
            coverage.MoveToContent();string expected=coverage.GetAttribute("WorldExtrasSha256");
            if(expected==null || FileHash(Path.Combine(addon,"manifest.xml"))!=HashId(expected))throw new InvalidDataException("World shader supplement is missing or changed.");
        }
        using(var reader=XmlReader.Create(Path.Combine(addon,"manifest.xml"),SafeXml(524288)))
        {
            int stages=2*(WorldShaderVariants.Extra.Length+TransparencyShaderVariants.Extra.Length+ProbeShaderVariants.Extra.Length+HighlightShaderVariants.Extra.Length);
            reader.MoveToContent();if(reader.Name!="WorldShaders" || reader.GetAttribute("Schema")!="1" || reader.GetAttribute("Count")!=stages.ToString(CultureInfo.InvariantCulture))throw new InvalidDataException("Incompatible world shader supplement.");
            while(reader.Read())if(reader.NodeType==XmlNodeType.Element && reader.Name=="Shader" && reader.GetAttribute("Id")==id.ToString(CultureInfo.InvariantCulture) && reader.GetAttribute("Stage")==stage)
            {
                if(module!=null || reader.GetAttribute("DefinitionsSha256")!=ExtraDefinitionsHash(material,stage,defines))throw new InvalidDataException("World shader supplement definitions mismatch.");
                module=HashId(reader.GetAttribute("Module"));metadataHash=HashId(reader.GetAttribute("Metadata"));
            }
        }
        if(module==null)throw new InvalidDataException("Missing world shader supplement variant: "+id+" "+stage);
        string spirv=Path.Combine(addon,module+".spv"),metadata=Path.Combine(addon,metadataHash+".xml");
        if(FileHash(spirv)!=module || FileHash(metadata)!=metadataHash)throw new InvalidDataException("World shader supplement module hash mismatch.");
        ShaderMetadata layout;using(var reader=XmlReader.Create(metadata,SafeXml(8*1024*1024)))layout=(ShaderMetadata)new XmlSerializer(typeof(ShaderMetadata)).Deserialize(reader);
        if(layout.SchemaVersion!=ShaderMetadata.CurrentSchemaVersion || layout.NativeAbiVersion!=1 || layout.SpirvSha256!=module || layout.EntryPoint!=(stage=="Vertex"?"__vertex_shader":"__pixel_shader"))throw new InvalidDataException("World shader supplement layout mismatch.");
        SpirvMetadataReader.Reflect(File.ReadAllBytes(spirv),layout);
        return new VulkanShaderArtifact{Id="world-"+id+"-"+stage,SpirvPath=spirv,Layout=layout};
    }
    public VulkanShaderArtifact LoadObjectTintPixel(uint pipeline,VulkanShaderArtifact original)
    {
        string addon=Path.Combine(directory,"ObjectTint"),module=null,metadataHash=null,expected,derivedSource=null;
        using(var coverage=XmlReader.Create(Path.Combine(directory,"coverage.xml"),SafeXml(4096))){coverage.MoveToContent();expected=coverage.GetAttribute("ObjectTintSha256");}
        if(expected==null)return original;
        string manifest=Path.Combine(addon,"manifest.xml");if(FileHash(manifest)!=HashId(expected))throw new InvalidDataException("Object tint manifest changed.");
        int count=0;
        using(var reader=XmlReader.Create(manifest,SafeXml(16384))){
            reader.MoveToContent();if(reader.Name!="ObjectTint" || reader.GetAttribute("Schema")!="1" || reader.GetAttribute("Transform")!="ObjectTint-1" || reader.GetAttribute("Count")!="8" || reader.GetAttribute("OriginalSource")!=FileHash(Path.Combine(contentDirectory,"Geometry/PixelTemplateBase.hlsli")))throw new InvalidDataException("Object tint source contract changed.");
            string source=File.ReadAllText(Path.Combine(contentDirectory,"Geometry/PixelTemplateBase.hlsli"));
            if(source.Split(new[]{"pix.color_mul = 1;"},StringSplitOptions.None).Length!=2)throw new InvalidDataException("Object tint initializer changed.");
            using(var sha=SHA256.Create())derivedSource=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source.Replace("pix.color_mul = 1;","pix.color_mul = object_.color_mul;")))).Replace("-","").ToLowerInvariant();
            if(reader.GetAttribute("DerivedSource")!=derivedSource)throw new InvalidDataException("Object tint transformation changed.");
            while(reader.Read())if(reader.NodeType==XmlNodeType.Element&&reader.Name=="Pixel"){
                ++count;if(reader.GetAttribute("Pipeline")!=pipeline.ToString(CultureInfo.InvariantCulture))continue;
                if(module!=null || reader.GetAttribute("BaseId")!=original.Id || reader.GetAttribute("BaseModule")!=original.Layout.SpirvSha256)throw new InvalidDataException("Object tint base variant mismatch.");
                module=HashId(reader.GetAttribute("Module"));metadataHash=HashId(reader.GetAttribute("Metadata"));
            }
        }
        if(count!=8 || module==null)throw new InvalidDataException("Object tint variant missing.");
        string spirv=Path.Combine(addon,module+".spv"),metadata=Path.Combine(addon,metadataHash+".xml");
        if(FileHash(spirv)!=module||FileHash(metadata)!=metadataHash)throw new InvalidDataException("Object tint artifacts changed.");
        ShaderMetadata layout;using(var reader=XmlReader.Create(metadata,SafeXml(8*1024*1024)))layout=(ShaderMetadata)new XmlSerializer(typeof(ShaderMetadata)).Deserialize(reader);
        if(layout.SchemaVersion!=ShaderMetadata.CurrentSchemaVersion||layout.NativeAbiVersion!=1||layout.SpirvSha256!=module||layout.EntryPoint!="__pixel_shader")throw new InvalidDataException("Object tint metadata contract changed.");
        var dependencies=layout.Dependencies.Where(d=>d.Path=="root0/Geometry/PixelTemplateBase.hlsli").ToArray();
        if(dependencies.Length!=1 || dependencies[0].Sha256!=derivedSource)throw new InvalidDataException("Object tint dependency hash mismatch.");
        SpirvMetadataReader.Reflect(File.ReadAllBytes(spirv),layout);
        return new VulkanShaderArtifact{Id="object-tint-"+pipeline,SpirvPath=spirv,Layout=layout};
    }
    public VulkanShaderArtifact LoadLightTiles()
    {
        var original=Load(LightTileShader.Variant);string addon=Path.Combine(directory,"LightTiles"),expected;
        using(var reader=XmlReader.Create(Path.Combine(directory,"coverage.xml"),SafeXml(4096))){reader.MoveToContent();expected=reader.GetAttribute("LightTilesSha256");}
        string manifest=Path.Combine(addon,"manifest.xml");if(expected==null||FileHash(manifest)!=HashId(expected))throw new InvalidDataException("Light tile manifest missing or changed.");
        string source=LightTileShader.DeriveSource(File.ReadAllText(Path.Combine(contentDirectory,"Lighting/PrepareLights.hlsl"))),derived;
        using(var sha=SHA256.Create())derived=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source))).Replace("-","").ToLowerInvariant();
        string module,metadataHash;
        using(var reader=XmlReader.Create(manifest,SafeXml(4096))){reader.MoveToContent();if(reader.Name!="LightTiles"||reader.GetAttribute("Schema")!="1"||reader.GetAttribute("Transform")!=LightTileShader.Contract||reader.GetAttribute("BaseModule")!=original.Layout.SpirvSha256||reader.GetAttribute("DerivedSource")!=derived)throw new InvalidDataException("Light tile source contract changed.");module=HashId(reader.GetAttribute("Module"));metadataHash=HashId(reader.GetAttribute("Metadata"));}
        string spirv=Path.Combine(addon,module+".spv"),metadata=Path.Combine(addon,metadataHash+".xml");if(FileHash(spirv)!=module||FileHash(metadata)!=metadataHash)throw new InvalidDataException("Light tile artifacts changed.");
        ShaderMetadata layout;using(var reader=XmlReader.Create(metadata,SafeXml(8*1024*1024)))layout=(ShaderMetadata)new XmlSerializer(typeof(ShaderMetadata)).Deserialize(reader);
        if(layout.SchemaVersion!=ShaderMetadata.CurrentSchemaVersion||layout.NativeAbiVersion!=1||layout.SpirvSha256!=module||layout.EntryPoint!="__compute_shader"||layout.WorkgroupSizeX!=16||layout.WorkgroupSizeY!=16||layout.WorkgroupSizeZ!=1||layout.Dependencies.Count(d=>d.Path=="root0/Lighting/PrepareLights.hlsl"&&d.Sha256==derived)!=1)throw new InvalidDataException("Light tile metadata contract changed.");
        SpirvMetadataReader.Reflect(File.ReadAllBytes(spirv),layout);return new VulkanShaderArtifact{Id="light-tiles",Layout=layout,SpirvPath=spirv};
    }
    private static XmlReaderSettings SafeXml(long limit) => new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limit };
    private static string HashId(string value)
    {
        if (value == null || value.Length != 64 || value.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))) throw new InvalidDataException("Invalid shader catalog hash.");
        return value;
    }
    private static string ResolveInside(string directory, string relative)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(Path.Combine(root, relative ?? ""));
        if (Path.IsPathRooted(relative) || !resolved.StartsWith(root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Shader source escapes its content directory.");
        return resolved;
    }
    private static string FileHash(string path)
    { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
}

public sealed class VulkanShaderArtifact
{
    public static VulkanShaderArtifact FromCompilation(VulkanShaderResult result,string id)=>new VulkanShaderArtifact{Id=id,Source=result.Metadata.SourcePath,Profile=result.Metadata.Profile,CacheKey=result.Metadata.CacheKey,SpirvPath=result.SpirvPath,Layout=result.Metadata};
    public string Id { get; internal set; }
    public string Source { get; internal set; }
    public string Profile { get; internal set; }
    public string CacheKey { get; internal set; }
    public string SpirvPath { get; internal set; }
    public ShaderMetadata Layout { get; internal set; }
}
