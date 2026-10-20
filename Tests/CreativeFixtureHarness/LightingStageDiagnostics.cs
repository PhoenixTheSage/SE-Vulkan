using System;
using System.IO;
using System.Xml.Linq;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    static bool lightingTermsReady;
    static void EnsureLightingTerms(NativeBackend backend)
    {
        if(lightingTermsReady)return;
        string file=Path.Combine(lightingOwner.reports,"LightingTerms.hlsl");File.WriteAllText(file,LightingTermsSource.Source);
        string content=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(VRageRender.MyRenderProxy).Assembly.Location),"../Content/Shaders"));
        using(var compiler=new VulkanShaderCompiler(Path.Combine(NativeBackend.AssetsRoot,"Tools/win-x64"),Path.Combine(Path.GetDirectoryName(lightingOwner.reports),"LightingShaderCache"))){
            var result=compiler.Compile(new VulkanShaderRequest{SourcePath=file,ContentDirectory=content,IncludeDirectories=new System.Collections.Generic.List<string>{lightingOwner.reports},Profile="ps_5_0",EntryPoint="__pixel_shader"});
            backend.ConfigureLightingTerms(SpirvRuntimeModule.FromArtifact(VulkanShaderArtifact.FromCompilation(result,"lighting-terms")));
        }
        lightingTermsReady=true;
    }
    static void SaveLightingStages(NativeBackend backend,string folder)
    {
        string[] names={"after-directional","after-points","after-spots","end-frame-hdr","term-normal-depth","term-view-ao","term-ibl-lod","term-brdf-gloss-nv","term-diffuse-ao","term-direct-shadow","term-fogged-light-ao"};
        var root=new XElement("LightingStages",new XAttribute("Schema",2),new XAttribute("Backend","Vulkan"),new XAttribute("Width",640),new XAttribute("Height",480),new XAttribute("Format","RGBA32F"),new XAttribute("RenderOrder","points,spots,directional"),
            new XAttribute("Scope","Exact native HDR snapshots plus seven FP32 diagnostic shader outputs using the actual directional-light descriptor set; opt-in rendering is separately checked for unchanged HDR bits."));
        try{
            for(uint stage=0;stage<names.Length;stage++){
                float[] values=backend.ReadbackLightingStage(stage,640,480);byte[] bytes=new byte[checked(values.Length*4)];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);
                File.WriteAllBytes(Path.Combine(folder,names[stage]+".rgba32f"),bytes);root.Add(new XElement("Stage",new XAttribute("Index",stage),new XAttribute("Name",names[stage])));
            }
            root.Save(Path.Combine(folder,"lighting-stages.xml"));
        }finally{backend.SetLightingDiagnostics(false);}
    }
}
