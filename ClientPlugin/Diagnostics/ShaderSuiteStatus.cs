using System;
using System.Globalization;
using System.IO;
using System.Xml;
using ClientPlugin.Bootstrap;
using ClientPlugin.Rendering.Shaders;
using ClientPlugin.Rendering.Interop;

namespace ClientPlugin.Diagnostics;

internal static class ShaderSuiteStatus
{
    public static string LastFailure {get;private set;}
    public static void Preflight(string contentDirectory)
    {
        string root=BootstrapAssets.FindDirectory(Path.Combine("Shaders","Vanilla","coverage.xml"));
        if(root==null)throw new FileNotFoundException("The stock Vulkan shader catalog is missing.");
        string directory=Path.Combine(root,"Shaders","Vanilla");
        using(var reader=XmlReader.Create(Path.Combine(directory,"coverage.xml"),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4096}))
        {
            reader.MoveToContent();if(reader.Name!="ShaderSuiteCoverage" || reader.GetAttribute("Schema")!="1" || reader.GetAttribute("Api")!="1.4" || reader.GetAttribute("Total")!=reader.GetAttribute("Validated") || reader.GetAttribute("Failed")!="0")throw new InvalidDataException("The stock shader catalog coverage is incompatible or incomplete.");
            var library=new VulkanShaderLibrary(directory,Path.Combine(contentDirectory,"Shaders"),Path.Combine(Path.GetDirectoryName(Path.GetFullPath(contentDirectory)),"Bin64","VRage.Render11.dll"),reader.GetAttribute("CatalogSha256"));
            if(library.VariantCount!=int.Parse(reader.GetAttribute("Total"),CultureInfo.InvariantCulture))throw new InvalidDataException("Stock shader coverage count mismatch.");
            void Check(VulkanShaderArtifact artifact){SpirvRuntimeModule.FromArtifact(artifact);foreach(uint capability in artifact.Layout.Capabilities)if(capability!=1 && capability!=50 && capability!=5379 && !((capability==49 || capability==47 || capability==46) && artifact.Layout.ExecutionModel==5) && !(capability==2 && artifact.Layout.ExecutionModel==3))throw new InvalidDataException("Unsupported world shader capability: "+capability);}
            foreach(var spec in WorldShaderVariants.Materials){Check(library.Load(spec.Vertex));var pixel=library.Load(spec.Pixel);Check(spec.Id<20 && spec.Id%5==2?library.LoadObjectTintPixel(spec.Id,pixel):pixel);}
            foreach(var spec in WorldShaderVariants.Extra){Check(library.LoadExtra(spec.Id,spec.Material,"Vertex",spec.Defines));var pixel=library.LoadExtra(spec.Id,spec.Material,"Pixel",spec.Defines);Check(spec.Id<20 && spec.Id%5==2?library.LoadObjectTintPixel(spec.Id,pixel):pixel);}
            foreach(var spec in VoxelShaderVariants.Materials){Check(library.Load(spec.Vertex));Check(library.Load(spec.Pixel));}
            foreach(string id in WorldShaderVariants.Lighting)Check(library.Load(id));Check(library.LoadLightTiles());
            foreach(var spec in TransparencyShaderVariants.Glass){Check(library.Load(spec.Vertex));Check(library.Load(spec.Pixel));}foreach(var spec in TransparencyShaderVariants.Decals){Check(library.Load(spec.Vertex));Check(library.Load(spec.Pixel));}foreach(var id in TransparencyShaderVariants.Particles)Check(library.Load(id));Check(library.Load(TransparencyShaderVariants.Resolve));foreach(var spec in TransparencyShaderVariants.Billboards){Check(library.Load(spec.Vertex));Check(library.Load(spec.Pixel));}foreach(var spec in TransparencyShaderVariants.Extra){Check(library.LoadExtra(spec.Id,spec.Material,"Vertex",spec.Defines));Check(library.LoadExtra(spec.Id,spec.Material,"Pixel",spec.Defines));}
            for(int i=0;i<EnvironmentShaderVariants.Stages.Length;++i)Check(library.Load(EnvironmentShaderVariants.Stages[i]));
            Check(library.Load("a1b699b3ad82adec90ba1af8318067d3410d117c5c94b9b424560f40e2fdf7b4"));foreach(string id in PostprocessShaderVariants.ShadowBlurs)Check(library.Load(id));foreach(string id in PostprocessShaderVariants.Stages)Check(library.Load(id));Check(library.Load("1bb6f40fef889661d4a74d17f3dc6c2cdfbc083270382103ff395321ada20f05"));Check(library.Load("babad18b0fbc76b619cc99914bbd22bc234ae57da0c44e005acc27b3996f2d93"));Check(library.Load("1e124ff187f7d6022692a237791b715152091c89eec94f9cac364c048ea2c7fb"));Check(library.Load("7e7f8a00e6cfc94f5e7f149a9a4df82ce433557cf56c91d2a08dfaa598952493"));Check(library.Load("76fcba14c7221e345e678f03aa4620250f69f7c39d6ea83ed764123721fcbf89"));
            foreach(string id in HighlightShaderVariants.Blurs)Check(library.Load(id));foreach(var spec in HighlightShaderVariants.Extra){Check(library.LoadExtra(spec.Id,spec.Material,"Vertex",spec.Defines));Check(library.LoadExtra(spec.Id,spec.Material,"Pixel",spec.Defines));}
            foreach(string id in ProbeShaderVariants.Stages)Check(library.Load(id));foreach(var spec in ProbeShaderVariants.Extra){Check(library.LoadExtra(spec.Id,spec.Material,"Vertex",spec.Defines));var pixel=library.LoadExtra(spec.Id,spec.Material,"Pixel",spec.Defines);Check((spec.Id-256)%5==2?library.LoadObjectTintPixel(spec.Id-192,pixel):pixel);}
        }
        NativeBackend.CheckWorldApi();
    }
    public static void Initialize(string contentDirectory, NativeBackend backend = null)
    {
        LastFailure=null;
        const string subsystem = "Vanilla shader conversion";
        VulkanDiagnostics.Set("Vanilla shader pass integration", SubsystemState.Unavailable, "Awaiting the authenticated stock material, depth and lighting pipeline initialization.");
        VulkanDiagnostics.Set("Vanilla Standard GBuffer", SubsystemState.Unavailable, "Requires a matching shader catalog and native offscreen pass extension.");
        try
        {
            string root = BootstrapAssets.FindDirectory(Path.Combine("Shaders", "Vanilla", "coverage.xml"));
            if (root == null) { VulkanDiagnostics.Set(subsystem, SubsystemState.Unavailable, "Build the installed-content shader suite to generate its local catalog."); return; }
            string directory = Path.Combine(root, "Shaders", "Vanilla");
            using (var reader = XmlReader.Create(Path.Combine(directory, "coverage.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4096 }))
            {
                reader.MoveToContent();
                if (reader.Name != "ShaderSuiteCoverage" || reader.GetAttribute("Schema") != "1" || reader.GetAttribute("Api") != "1.4") throw new InvalidDataException("Incompatible shader coverage report.");
                int total = int.Parse(reader.GetAttribute("Total"), CultureInfo.InvariantCulture), validated = int.Parse(reader.GetAttribute("Validated"), CultureInfo.InvariantCulture);
                if (total <= 0 || total != validated || reader.GetAttribute("Failed") != "0") throw new InvalidDataException("Shader suite has incomplete validation coverage.");
                // Interim's compatibility core rewrites its loaded assemblies.
                // Inventory identity belongs to the original installed renderer,
                // beside the Content directory, rather than that runtime copy.
                string gameAssembly = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(contentDirectory)), "Bin64", "VRage.Render11.dll");
                var library = new VulkanShaderLibrary(directory, Path.Combine(contentDirectory, "Shaders"), gameAssembly, reader.GetAttribute("CatalogSha256"));
                if (library.VariantCount != total) throw new InvalidDataException("Shader coverage count differs from its catalog.");
                VulkanDiagnostics.Set(subsystem, SubsystemState.Ready, validated + "/" + total + " source variants validated for Vulkan 1.4; " + library.ModuleCount + " unique modules; " + reader.GetAttribute("LegacyExcluded") + " obsolete unreferenced files excluded. Individual artifacts are hash-checked when loaded; native integration and visual acceptance pending.");
                if (backend != null)
                {
                    try
                    {
                        backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId), library.Load(VanillaGBufferShaders.PixelId));
                        VulkanDiagnostics.Set("Vanilla Standard GBuffer", SubsystemState.Ready, "Stock vertex/pixel modules reflected and bound to a native three-attachment pipeline.");
                    }
                    catch (Exception error) { LastFailure=error.Message;VulkanDiagnostics.Set("Vanilla Standard GBuffer", SubsystemState.Failed, error.Message); return; }
                    try
                    {
                        backend.ConfigureWorld(library,Path.Combine(contentDirectory,"Shaders"));
                        VulkanDiagnostics.Set("Vanilla shader pass integration",SubsystemState.Ready,"44 stock material/voxel/depth pipelines and stock directional/point/spot lighting initialized; game-world observations remain separate. Specular probes and advanced lighting parity remain pending.");
                    }
                    catch (Exception error) { LastFailure=error.Message;VulkanDiagnostics.Set("Vanilla shader pass integration", SubsystemState.Failed, error.Message); }
                }
            }
        }
        catch (Exception error) { LastFailure=error.Message;VulkanDiagnostics.Set(subsystem, SubsystemState.Failed, error.Message); }
    }
}
