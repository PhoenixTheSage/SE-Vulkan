using System;
using System.Collections.Generic;

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
