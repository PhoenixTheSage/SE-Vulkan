using System.Reflection;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender;

partial class Program
{
    // Audit P3 step 4: SE-RT's software stack on the Vulkan tenant session, through Anomaly's actual core.
    static int SertVulkanRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        var scene=core.GetType("ClientPlugin.Shaders.RayScene",true)!;
        string shaders=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../SE-RT/Assets/Shaders"));
        var failures=new List<string>();var code=new Dictionary<string,byte[]>();
        foreach(var (name,profile) in new[]{("Fullscreen",0),("Guides",1),("Atlas",1),("Composite",1),("Skin",3),("Refit",3),("Identify",3),("LightWeights",3),("Candidates",3),("Reuse",3),("Trace",3),("Temporal",3),("Atrous",3),("Classify",3),("Refine",3)})
            try{var bytes=(byte[])scene.GetMethod("CompileShader",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,new object[]{Path.Combine(shaders,name+".hlsl"),profile})!;if(bytes.Length<20)failures.Add(name+": empty");else code[name]=bytes;}
            catch(TargetInvocationException e){failures.Add(name+": "+e.InnerException?.Message);}
        foreach(var f in failures)Console.WriteLine("COMPILE "+f);
        Check(failures.Count==0,"RayScene.CompileShader builds every SE-RT shader as Vulkan SPIR-V");
        var pipelines=new List<ulong>();
        foreach(var name in code.Keys.Where(n=>n!="Fullscreen"))
            try{pipelines.Add(name=="Guides"?backend.TenantCreateRaster(code["Fullscreen"],"__vertex_shader",code[name],"__pixel_shader",new[]{109,97,97}):name=="Atlas"?backend.TenantCreateRaster(code["Fullscreen"],"__vertex_shader",code[name],"__pixel_shader",new[]{97,97})
                :name=="Composite"?backend.TenantCreateRaster(code["Fullscreen"],"__vertex_shader",code[name],"__pixel_shader",new[]{0}):backend.TenantCreateCompute(code[name],"__compute_shader"));}
            catch(Exception e){failures.Add(name+": "+e.Message);Console.WriteLine("PIPELINE "+name+": "+e.Message);}
        Check(failures.Count==0,"the tenant session builds every SE-RT pipeline");
        foreach(var id in pipelines)backend.TenantDestroy(id);
        if(backend.TenantStats().RayQuery){
            // Option C: profile 4 compiles the SERT_RAY_QUERY variants (cs_6_5, SPV_KHR_ray_query) for the tenant session.
            var query=new List<ulong>();
            foreach(var name in new[]{"IdentifyRayQuery","TraceRayQuery","RefineRayQuery"})
                try{var bytes=(byte[])scene.GetMethod("CompileShader",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,new object[]{Path.Combine(shaders,name+".hlsl"),4})!;query.Add(backend.TenantCreateCompute(bytes,"__compute_shader"));}
                catch(Exception e){failures.Add(name+": "+(e.InnerException??e).Message);Console.WriteLine("RAYQUERY "+name+": "+(e.InnerException??e).Message);}
            Check(failures.Count==0,"RayScene.CompileShader profile 4 builds SE-RT's ray-query Identify, Trace and Refine pipelines");
            foreach(var id in query)backend.TenantDestroy(id);
        }

        // SoftwareVulkan end to end: Anomaly delivers v1 frames, SE-RT builds the scene and records its whole stack.
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true)!.GetMethod(method,stat)!.Invoke(null,args)!;
        var sert=Assembly.LoadFrom(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../SE-RT/ClientPlugin/bin/Release/net10.0/SERT.dll")));
        var hostType=sert.GetType("SERT.Integration.AnomalyHost",true)!;var host=Activator.CreateInstance(hostType,new object[]{null!})!;
        var software=Activator.CreateInstance(sert.GetType("SERT.Rendering.SoftwareVulkan",true)!,host,(Func<string>)(()=>Path.GetDirectoryName(shaders)!),false)!;
        var assembler=sert.GetType("SERT.Core.SceneAssembler",true)!;var settingsType=sert.GetType("SERT.Core.ShadowSettings",true)!;
        software.GetType().GetMethod("SetShadowSettings")!.Invoke(software,new[]{settingsType.GetProperty("Default")!.GetValue(null)!});
        string SoftwareStatus()=>(string)software.GetType().GetProperty("Status")!.GetValue(software)!;
        var quad=new float[64];float[][] corners={new[]{-2f,-2,0},new[]{2f,-2,0},new[]{2f,2,0},new[]{-2f,2,0}};for(int v=0;v<4;v++){quad[v*16]=corners[v][0];quad[v*16+1]=corners[v][1];quad[v*16+5]=1;}
        var part=new object[]{0,6,0,"fixture",new string[4],"MESH"};var row=new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,-5,1};
        var synthetic=new object[]{new[]{new object[]{1,quad,new[]{0,1,2,0,2,3},new[]{part},false}},new[]{new object[]{4242u,0,row,new object[]{new object[]{part,new[]{1f,1,1,0},new object[4],Array.Empty<float>(),null!}},new[]{-2f,-2,-5,2,2,-5}}},
            new float[]{0,1,-3,10,1,1,1,1,0,0,-1,-1,0,0,1,1},new[]{new object[]{null!,new[]{0f,1,0}}},new float[]{0,1,-3},new[]{0d,0,0},1f};
        var overrideField=typeof(ClientPlugin.Rendering.GuestBridge).GetField("RayCaptureOverride",stat)!;
        object geometry=null!;bool rendered=false;string error=null!;int callbacks=0;
        Action<object[]> frame=f=>{callbacks++;try{
            if(geometry==null)geometry=assembler.GetMethod("Build")!.Invoke(null,new object[]{f})!;
            if(!(bool)hostType.GetMethod("Claim")!.Invoke(host,null)!)throw new Exception("claim refused");
            rendered=(bool)software.GetType().GetMethod("Render")!.Invoke(software,new object[]{f,geometry,1f,1f})!;}catch(Exception e){error=e.GetBaseException().Message;}};
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true)!;
        var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true)!.GetMethod("Submit",stat)!);
        var post=new ClientPlugin.Rendering.Scene.VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        void Frame(){
            Call("Backend.RenderHost","PrepareShaderGenerations");
            backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(ClientPlugin.Rendering.Shaders.WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            if(!backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black))throw new Exception("SE-RT frame did not begin");Call("Backend.GuestFrame","Capture");
            Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);backend.EndFrame();
        }
        try{
            overrideField.SetValue(null,(Func<object[]>)(()=>synthetic));
            // Plugin.Completed's path: Anomaly polls with the tenant session; SE-RT reads its GPU timing.
            int polls=0;double polled=double.NaN;var poll=software.GetType().GetMethod("Poll",new[]{typeof(object),typeof(Action<double>)})!;
            Action<object> completed=context=>{polls++;poll.Invoke(software,new object[]{context,(Action<double>)(ms=>polled=ms)});};
            Check((bool)hostType.GetMethod("TryBind")!.Invoke(host,new object[]{frame,completed})!,"SE-RT's AnomalyHost binds RayScene v1 on Vulkan");
            Frame();var baseline=backend.Readback(64,64);
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(!rendered&&error==null&&watch.Elapsed.TotalSeconds<300){Frame();if(!rendered)Thread.Sleep(50);}
            Console.WriteLine("SERT status: "+SoftwareStatus()+(error!=null?" error: "+error:""));
            Check(rendered&&error==null,"SoftwareVulkan renders a frame from the delivered RayScene");
            var stats=backend.TenantStats();Console.WriteLine("SERT tenant: dispatches="+stats.Dispatches+" draws="+stats.Draws+" commands="+stats.Commands+" rejected="+stats.Rejected+" images="+stats.Images+" buffers="+stats.Buffers);
            Check(stats.Rejected==0&&stats.Dispatches>=10&&stats.Draws>=3,"the whole stack (atlas, guides, BVH refit, ReSTIR, trace, denoise, composite) records without a rejected command");
            ulong before=stats.Dispatches;for(int i=0;i<3;i++)Frame();
            Check(rendered&&error==null&&backend.TenantStats().Dispatches>before&&backend.TenantStats().Rejected==0,"later frames keep rendering with history");
            double gpu=(double)software.GetType().GetProperty("GpuMilliseconds")!.GetValue(software)!;long measured=(long)software.GetType().GetProperty("Measurements")!.GetValue(software)!;
            Console.WriteLine("SERT timing: polls="+polls+" gpu="+gpu.ToString("F4")+" ms measurements="+measured);
            Check(polls>0&&measured>0&&double.IsFinite(gpu)&&gpu>0&&polled==gpu,"Anomaly's Vulkan completion polls give SE-RT its GPU time for the adaptive budget");
            // This lane has no GBuffer geometry, so Composite discards every pixel; SceneSubmissionGpuTests --sert-scene-only checks its output.
            Check(backend.Readback(64,64).SequenceEqual(baseline),"with no GBuffer surface the composite leaves the image untouched, as on DX11");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"SE-RT on the tenant session raises zero validation errors");
        }finally{
            overrideField.SetValue(null,null);software.GetType().GetMethod("Dispose")!.Invoke(software,null);hostType.GetMethod("Dispose")!.Invoke(host,null);
            core.GetType("ClientPlugin.Shaders.OpaqueLightingOwnership",true)!.GetMethod("BeginFrame")!.Invoke(null,null);
        }
        return checks;
    }
}
