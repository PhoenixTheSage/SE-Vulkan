using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientPlugin.Rendering.Shaders;

public sealed class VulkanShaderRequest
{
    public string SourcePath { get; set; }
    public string ContentDirectory { get; set; }
    public string EntryPoint { get; set; } = "__pixel_shader";
    public string Profile { get; set; } = "ps_5_0";
    public Dictionary<string, string> Defines { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public List<string> IncludeDirectories { get; set; } = new List<string>();
    public bool Debug { get; set; }
    public uint NativeAbiVersion { get; set; } = 1;
    public bool GpuOpaque { get; set; }
    public string GpuOpaqueBaseModule { get; set; }
    public List<ShaderDependency> GpuOpaqueBaseDependencies { get; set; }
    public bool HbaoNegativeBorder { get; set; }
    public string HbaoBaseModule { get; set; }
    public List<ShaderDependency> HbaoBaseDependencies { get; set; }
    public ShaderSourceGeneration SourceGeneration { get; set; }
    internal VulkanShaderRequest Snapshot()
    {
        var copy=(VulkanShaderRequest)MemberwiseClone();
        copy.Defines=Defines==null?null:new Dictionary<string,string>(Defines,StringComparer.Ordinal);
        copy.IncludeDirectories=IncludeDirectories==null?new List<string>():new List<string>(IncludeDirectories);
        List<ShaderDependency> Dependencies(List<ShaderDependency> values)=>values?.Select(d=>new ShaderDependency{Path=d.Path,Sha256=d.Sha256,CompatibilityFixup=d.CompatibilityFixup}).ToList();
        copy.GpuOpaqueBaseDependencies=Dependencies(GpuOpaqueBaseDependencies);copy.HbaoBaseDependencies=Dependencies(HbaoBaseDependencies);return copy;
    }
}

/// <summary>A copied pack generation applied inside the compiler's immutable include mirror.</summary>
public sealed class ShaderSourceGeneration
{
    public const string Contract="AnomalySourceGeneration-1";
    readonly SortedDictionary<string,byte[]> files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
    readonly Dictionary<string,string> stock=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    public string Fingerprint { get; }
    public ShaderSourceGeneration(string packIdentity,IDictionary<string,byte[]> sources,IDictionary<string,string> stockHashes)
    {
        if(string.IsNullOrWhiteSpace(packIdentity)||sources==null||stockHashes==null||stockHashes.Count==0)
            throw new ArgumentException("A source generation requires pack identity, copied sources and authenticated stock hashes.");
        foreach(var entry in stockHashes)
        {
            string key=Key(entry.Key);
            if(!System.Text.RegularExpressions.Regex.IsMatch(entry.Value??"","^[0-9a-fA-F]{64}$")||stock.ContainsKey(key))throw new ArgumentException("Invalid or duplicate stock source hash: "+key);
            stock.Add(key,entry.Value.ToLowerInvariant());
        }
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in sources)
        {
            string key=Key(entry.Key);string extension=System.IO.Path.GetExtension(key);
            if(entry.Value==null||!names.Add(key)||!new[]{".hlsl",".hlsli",".inl",".inc"}.Contains(extension.ToLowerInvariant()))
                throw new ArgumentException("Invalid or duplicate generated shader source: "+key);
            files.Add(key,(byte[])entry.Value.Clone());
        }
        using(var stream=new System.IO.MemoryStream())
        using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))
        {
            writer.Write(Contract);writer.Write(packIdentity);
            foreach(var entry in stock.OrderBy(p=>p.Key,StringComparer.Ordinal)){writer.Write(entry.Key);writer.Write(entry.Value);}
            foreach(var entry in files){writer.Write(entry.Key);writer.Write(entry.Value.Length);writer.Write(entry.Value);}
            writer.Flush();using(var sha=System.Security.Cryptography.SHA256.Create())Fingerprint=BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
        }
    }
    internal IEnumerable<KeyValuePair<string,byte[]>> Files=>files;
    internal void Authenticate(string key,string hash)
    {
        if(!stock.TryGetValue(key,out string expected)||expected!=hash)
            throw new System.IO.InvalidDataException("Stock shader source differs from the authenticated generation: "+key+". Rebuild for the current game content.");
    }
    static string Key(string path)
    {
        string key=(path??"").Replace('\\','/');
        if(key.Length==0||key.StartsWith("/")||key.IndexOf(':')>=0||key.Split('/').Any(p=>p==""||p=="."||p==".."))throw new ArgumentException("Generation shader path must be relative and contained: "+path);
        return key;
    }
}

public sealed class VulkanShaderResult
{
    public string SpirvPath { get; internal set; }
    public string MetadataPath { get; internal set; }
    public bool CacheHit { get; internal set; }
    public ShaderMetadata Metadata { get; internal set; }
    public string Diagnostics { get; internal set; }
}

// XML is also consumed by offline content audits. Sizes are declared SPIR-V layout
// sizes; they do not include Vulkan's dynamic uniform-buffer offset alignment.
public sealed class ShaderMetadata
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public uint NativeAbiVersion { get; set; }
    public string CacheKey { get; set; }
    public string SpirvSha256 { get; set; }
    public string ToolchainSha256 { get; set; }
    public string SourceTransform { get; set; }
    public string BaseSpirvSha256 { get; set; }
    public string SourcePath { get; set; }
    public string Profile { get; set; }
    public string EntryPoint { get; set; }
    public string CompilerTarget { get; set; } = "vulkan1.3/SPIR-V1.6";
    public string ValidationTarget { get; set; } = "vulkan1.4/scalar-block-layout";
    public List<ShaderDependency> Dependencies { get; set; } = new List<ShaderDependency>();
    public List<ShaderDescriptor> Descriptors { get; set; } = new List<ShaderDescriptor>();
    public List<ShaderInterfaceVariable> Interface { get; set; } = new List<ShaderInterfaceVariable>();
    public List<ShaderPushConstant> PushConstants { get; set; } = new List<ShaderPushConstant>();
    public uint ExecutionModel { get; set; }
    public List<uint> Capabilities { get; set; } = new List<uint>();
    public List<string> Extensions { get; set; } = new List<string>();
    public uint WorkgroupSizeX { get; set; }
    public uint WorkgroupSizeY { get; set; }
    public uint WorkgroupSizeZ { get; set; }
}

public sealed class ShaderDependency
{
    public string CompatibilityFixup { get; set; }
    public string Path { get; set; }
    public string Sha256 { get; set; }
}

public sealed class ShaderDescriptor
{
    public string CounterFor { get; set; }
    public bool HasCounter { get; set; }
    public uint CounterBinding { get; set; }
    public string Name { get; set; }
    public uint Set { get; set; }
    public uint Binding { get; set; }
    public string DescriptorType { get; set; }
    public uint DescriptorCount { get; set; } = 1;
    public bool RuntimeArray { get; set; }
    public char RegisterClass { get; set; }
    public uint RegisterIndex { get; set; }
    public uint BlockSize { get; set; }
    public List<ShaderBlockMember> Members { get; set; } = new List<ShaderBlockMember>();
}

public sealed class ShaderBlockMember
{
    public string Name { get; set; }
    public string Type { get; set; }
    public uint Offset { get; set; }
    public uint Size { get; set; }
    public uint ArrayStride { get; set; }
    public uint MatrixStride { get; set; }
    public bool RowMajor { get; set; }
    public bool RuntimeArray { get; set; }
    public List<ShaderBlockMember> Members { get; set; } = new List<ShaderBlockMember>();
}

public sealed class ShaderInterfaceVariable
{
    public string Name { get; set; }
    public string Direction { get; set; }
    public uint Location { get; set; }
    public string Type { get; set; }
    public bool Flat { get; set; }
    public bool NoPerspective { get; set; }
    public bool Centroid { get; set; }
    public bool Sample { get; set; }
}

public sealed class ShaderPushConstant
{
    public string Name { get; set; }
    public uint Size { get; set; }
    public List<ShaderBlockMember> Members { get; set; } = new List<ShaderBlockMember>();
}
