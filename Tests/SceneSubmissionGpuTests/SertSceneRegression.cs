using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.Library.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

partial class Program
{
    // Audit P3 step 4: SE-RT's SoftwareVulkan composes a real GBuffer scene through the tenant session.
    static int SertSceneRegression(NativeBackend backend,VulkanScene scene,string content,string output,Action<bool,string> check)
    {
        int checks=0;void Check(bool ok,string text){check(ok,text);checks++;}
        var sert=Assembly.LoadFrom(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../SE-RT/ClientPlugin/bin/Release/net10.0/SERT.dll")));
        string assets=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../SE-RT/Assets")),shaders=Path.Combine(content,"Shaders");
        // The compile Anomaly's RayScene.CompileShader performs on Vulkan: game shaders as the first include root.
        var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"SertSpirv"));
        byte[] Compile(string path,int profile)=>SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiler.Compile(new VulkanShaderRequest{SourcePath=path,
            EntryPoint=profile==0?"__vertex_shader":profile==1?"__pixel_shader":"__compute_shader",Profile=profile==0?"vs_5_0":profile==1?"ps_5_0":"cs_5_0",
            ContentDirectory=shaders,IncludeDirectories=new List<string>{Path.GetDirectoryName(path)!,shaders}}).SpirvPath));
        var hostType=sert.GetType("SERT.Integration.AnomalyHost",true)!;var host=Activator.CreateInstance(hostType,new object[]{(Func<string,int,byte[]>)Compile})!;
        var softwareType=sert.GetType("SERT.Rendering.SoftwareVulkan",true)!;var software=Activator.CreateInstance(softwareType,host,(Func<string>)(()=>assets),false)!;
        var settingsType=sert.GetType("SERT.Core.ShadowSettings",true)!;var debugType=sert.GetType("SERT.Core.ShadowDebugView",true)!;
        void View(string name){var s=settingsType.GetProperty("Default")!.GetValue(null)!;settingsType.GetField("DebugView")!.SetValue(s,Enum.Parse(debugType,name));softwareType.GetMethod("SetShadowSettings")!.Invoke(software,new[]{s});}
        var assembler=sert.GetType("SERT.Core.SceneAssembler",true)!;var capture=typeof(VulkanScene).GetMethod("CaptureRayScene",BindingFlags.Instance|BindingFlags.NonPublic)!;
        MatrixD projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;light.SunDiscIntensity=0;
        var session=backend.TenantSession();uint index=0;object geometry=null!;bool rendered=false;
        var devices=new object[]{"vulkan",session,0xFFFF000000000001UL,0xFFFF000000000002UL,0xFFFF000000000003UL,0xFFFF000000000004UL,0xFFFF000000000005UL,0UL,
            0xFFFF000000000024UL,0xFFFF000000000020UL,0xFFFF000000000021UL,0xFFFF000000000022UL,0xFFFF000000000023UL};
        float[] Row(MatrixD m)=>new[]{(float)m.M11,(float)m.M12,(float)m.M13,(float)m.M14,(float)m.M21,(float)m.M22,(float)m.M23,(float)m.M24,(float)m.M31,(float)m.M32,(float)m.M33,(float)m.M34,(float)m.M41,(float)m.M42,(float)m.M43,(float)m.M44};
        byte[] Frame(bool trace)
        {
            backend.BeginPacketCollection();scene.SetCamera(Vector3D.Zero,MyTimeSpan.Zero,1f/60);scene.Draw(backend);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,renderSettings:scene.RenderSettings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            if(!backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black))throw new Exception("SE-RT scene frame missing");
            try{
                if(trace){
                    // The v1 frame Anomaly's DeliverVulkan builds from the same capture.
                    var c=(object[])capture.Invoke(scene,null)!;index++;
                    var frame=new object[]{1,1,index,c[5],c[0],c[1],c[2],devices,new[]{64,64},new float[]{0,0,1,1,2,2,2,index>1?1:0,.1f,.1f,.1f,Convert.ToSingle(c[6])},
                        Row(projection),Row(projection),Array.Empty<object[]>(),c[3],c[4],null!};
                    geometry??=assembler.GetMethod("Build")!.Invoke(null,new object[]{frame})!;
                    rendered=(bool)softwareType.GetMethod("Render")!.Invoke(software,new object[]{frame,geometry,1f,1f})!;
                }
            }finally{backend.EndFrame();}
            return backend.Readback(64,64);
        }
        try{
            var baseline=Frame(false);int armor=(32*64+32)*4;
            Check(baseline[armor]+baseline[armor+1]+baseline[armor+2]>0,"the armor block is lit natively at the image center");
            View("RayCoverage");var watch=System.Diagnostics.Stopwatch.StartNew();byte[] coverage=baseline;
            while(!rendered&&watch.Elapsed.TotalSeconds<300){coverage=Frame(true);if(!rendered)Thread.Sleep(50);}
            Console.WriteLine("SERT status: "+softwareType.GetProperty("Status")!.GetValue(software));
            Check(rendered&&backend.TenantStats().Rejected==0,"SoftwareVulkan renders the captured scene with no rejected commands");
            for(int i=0;i<2;i++)coverage=Frame(true);
            bool Green(byte[] p,int at)=>p[at+1]>200&&p[at]<60&&p[at+2]<60;bool Magenta(byte[] p,int at)=>p[at]>200&&p[at+2]>200&&p[at+1]<60;
            int covered=0,green=0,changed=0;for(int at=0;at<coverage.Length;at+=4){if(Green(coverage,at)||Magenta(coverage,at))covered++;if(Green(coverage,at))green++;if(coverage[at]!=baseline[at]||coverage[at+1]!=baseline[at+1]||coverage[at+2]!=baseline[at+2])changed++;}
            Console.WriteLine("SERT coverage: covered="+covered+" green="+green+" changed="+changed+" center="+coverage[armor]+","+coverage[armor+1]+","+coverage[armor+2]);
            // A pixel centred on a shared triangle edge can miss the BVH (DX11 traces the same way), so a few surface pixels stay untraced.
            Check(covered>=300&&green>=covered*9/10,"the ray-coverage view marks the armor surface as traced (green) on at least 90% of its pixels");
            View("Lighting");byte[] lit=coverage;for(int i=0;i<4;i++)lit=Frame(true);
            int differs=0;for(int at=0;at<lit.Length;at+=4)if(lit[at]!=baseline[at]||lit[at+1]!=baseline[at+1]||lit[at+2]!=baseline[at+2])differs++;
            Console.WriteLine("SERT lighting: differs="+differs+" center="+lit[armor]+","+lit[armor+1]+","+lit[armor+2]+" baseline="+baseline[armor]+","+baseline[armor+1]+","+baseline[armor+2]);
            Check(rendered&&differs>0&&lit[armor]+lit[armor+1]+lit[armor+2]>0,"traced lighting replaces native lighting on the armor and stays lit");
            var stats=backend.TenantStats();Check(stats.Rejected==0&&stats.Frames>=7,"every traced frame runs its whole tenant list");
        }finally{softwareType.GetMethod("Dispose")!.Invoke(software,null);compiler.Dispose();}
        return checks;
    }
}
