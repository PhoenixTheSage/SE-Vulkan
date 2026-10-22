using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.Import;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using System.Reflection;
using ClientPlugin.Rendering;

class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int HbaoJitterCall(IntPtr context,float[] values,uint count);
    static bool generationMode,hbaoReplacementOnly,stockHighlightOnly;
    static int Main(string[] args){stockHighlightOnly=args.Contains("--stock-highlight-only");generationMode=args.Contains("--generations");hbaoReplacementOnly=args.Contains("--hbao-replacement-only");string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,n)=>{string path=Path.Combine(bin,n.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};try{return Run(bin);}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    [MethodImpl(MethodImplOptions.NoInlining)]static int Run(string bin)
    {
        int checks=0;void Check(bool condition,string text){if(!condition)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        var heuristic=new VulkanDrsHeuristic{Settings=new MyDRSSettings(historySize:4,minResolutionChangePeriod:0,increaseAmortizationBlendFactor:1,changePercentageThreshold:0)};
        heuristic.Reset_RenderThread();Check(heuristic.QueryCurentFrameResolutionFraction()==1,"DRS reset restores full resolution");
        Check(heuristic.CreateNewPreviousFrameTimings(1,1)==VulkanDrsHeuristic.kInvalidEntryId,"DRS ignores the first timing after reset");
        void Feed(float gpu,float cpu=1,bool bubbles=false){ulong id=heuristic.CreateNewPreviousFrameTimings(cpu,1);heuristic.CommitPreviousFrameGPUTimings(id,gpu,gpu,bubbles);heuristic.RefreshCurentFrameResolutionFraction();}
        for(int i=0;i<16;i++)Feed(60);Check(heuristic.QueryCurentFrameResolutionFraction()==.5f,"sustained GPU overload reaches the preset minimum");
        for(int i=0;i<16;i++)Feed(1);Check(heuristic.QueryCurentFrameResolutionFraction()==1,"completed low GPU timings recover full resolution across history wrap");
        heuristic.Settings=new MyDRSSettings(historySize:2,minResolutionChangePeriod:0,increaseAmortizationBlendFactor:1,changePercentageThreshold:0,cpuBoundScreenPercentage:75);heuristic.Reset_RenderThread();for(int i=0;i<8;i++)Feed(60,30,true);Check(heuristic.QueryCurentFrameResolutionFraction()==.75f,"CPU bubbles retain the configured CPU-bound fraction");
        string output=Path.GetFullPath("TestResults/PostprocessGpuTests");Directory.CreateDirectory(output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using(var authentication=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"HbaoAuthenticationCache"))){
            var invalid=library.HbaoBorderRequest(PostprocessShaderVariants.Stages[36]);
            invalid.HbaoBaseDependencies.Single(d=>d.Path.EndsWith("/Postprocess/HBAO/CoarseAO.hlsl",StringComparison.Ordinal)).Sha256=new string('0',64);
            bool rejectedSource=false;try{authentication.Compile(invalid);}catch(InvalidDataException error){rejectedSource=error.Message.Contains("catalog snapshot");}
            Check(rejectedSource,"HBAO border compiler rejects a changed authenticated source dependency before producing a pipeline");
            var valid=library.HbaoBorderRequest(PostprocessShaderVariants.Stages[36]);var derived=authentication.Compile(valid);var cached=authentication.Compile(valid);
            Check(cached.CacheHit && cached.Metadata.SourceTransform=="HbaoNegativeBorder-1" && cached.Metadata.BaseSpirvSha256==library.Load(PostprocessShaderVariants.Stages[36]).Layout.SpirvSha256 && cached.Metadata.SpirvSha256==derived.Metadata.SpirvSha256,"derived HBAO cache preserves the exact authenticated base module and transform identity");
        }
        using var backend=new NativeBackend(null);backend.Create(65,63,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));
        uint[] generationStages=Enumerable.Range(0,62).Concat(Enumerable.Range(128,16)).Concat(Enumerable.Range(256,4)).Select(i=>(uint)i).ToArray();
        ulong generationToken=0;
        if(generationMode)
        {
            var result=backend.CompilePostprocessGeneration("fixture-all-native-postprocess-families",new Dictionary<string,byte[]>(),new Dictionary<string,string>{{"ANOMALY_GENERATION_FIXTURE","1"}},generationStages);
            generationToken=(ulong)result[0];backend.QueuePostprocessGeneration(generationToken);
            Check(backend.GetPostprocessGeneration(13).Active==0,"complete detached postprocess generation leaves stock active until the host boundary");
        }
        Check(backend.PostprocessLayouts.Count==128,"128 authenticated stock postprocess/highlight blur stages initialize");Check(Marshal.SizeOf<NativePostprocessConfig>()==384 && Marshal.SizeOf<NativePostprocessStats>()==88 && Marshal.SizeOf<NativeHbaoConfig>()==224,"postprocess ABI packing");
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.BeginUploads();backend.UploadWorldMesh(1,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});backend.UploadTexture(10,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(11,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(12,1,1,0,new byte[]{255,1,0,0},false);backend.EndUploads();
        int width=65,height=63;MatrixD projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,width/(double)height,.1,100);projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;light.SunDiscIntensity=0;
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.Data.ChromaticFactor=0;post.Settings.Data.VignetteStart=0;
        NativeOcclusionDraw[] queries=Array.Empty<NativeOcclusionDraw>();ulong queryToken=0;bool packedQueries=false;bool casts=false;int highlight=0;float thickness=4;bool excludeSpot=false;int billboard=0;float fraction=1;byte[] Render(bool fxaa=false,bool opaque=true,bool ambientOcclusion=true){int rw=Math.Max(1,(int)Math.Round(width*fraction,MidpointRounding.AwayFromZero)),rh=Math.Max(1,(int)Math.Round(height*fraction,MidpointRounding.AwayFromZero));backend.BeginPacketCollection();if(opaque){uint draw=backend.AddWorldPacket(0,1,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),flags:casts?0u:512u);if(highlight>0)backend.AddHighlightPacket(draw,0,3,0,1,new Vector4(0,1,0,1),thickness);if(highlight==2)backend.AddHighlightPacket(draw,0,3,0,1,new Vector4(0,0,0,1),0,true);}if(billboard>0){var data=new byte[48];Buffer.BlockCopy(new float[]{1,0,0,.5f},0,data,0,16);Buffer.BlockCopy(BitConverter.GetBytes(billboard==5?0:-1),0,data,16,4);backend.BillboardPackets.Add(new NativeBillboardDraw{Texture=20,Flags=8,Bucket=(uint)(billboard==5?4:billboard),Vertices=new float[]{-.5f,-.5f,-2,0,0,.5f,-.5f,-2,1,0,.5f,.5f,-2,1,1,-.5f,.5f,-2,0,1},Data=data});}if(excludeSpot)backend.ShadowExclusions=new[]{new NativeShadowExclusion{Light=0,Draw=0}};if(queries.Length>0){++queryToken;if(packedQueries)backend.QueueOcclusion(queryToken,queries.Select(q=>new NativePackedOcclusionDraw{Id=q.Id,MinimumX=q.Minimum[0],MinimumY=q.Minimum[1],MinimumZ=q.Minimum[2],ExtentX=q.Extents[0],ExtentY=q.Extents[1],ExtentZ=q.Extents[2],Flags=q.Flags}).ToArray(),queries.Length);else backend.QueueOcclusion(queryToken,queries);}post.Configure(backend,rw,rh,fxaa,view:MatrixD.Identity,projection:projection,ambientOcclusion:ambientOcclusion);backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,rw,rh,light,delta:.25f,postprocess:post.Settings,renderFraction:fraction),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"postprocess frame begins");backend.RequestReadback();backend.EndFrame();return backend.Readback(width,height);}
        int Changes(byte[] a,byte[] b)=>a.Where((v,i)=>i%4!=3 && Math.Abs(v-b[i])>2).Count();
        void HbaoReplacement()
        {
            var saved=post.Hbao;post.Hbao=MyHBAOData.Default;post.Hbao.Enabled=true;post.Hbao.BlurEnable=false;post.Hbao.PowerExponent=1;post.Hbao.DepthClampToEdge=true;
            post.Ssao.Enabled=false;post.Settings.EnableEyeAdaptation=false;post.Settings.BloomEnabled=false;post.Settings.Data.ChromaticFactor=0;post.Settings.Data.VignetteStart=0;post.Settings.EnableTonemapping=true;
            Render();var stockAo=backend.ReadbackLightingInput(0,width,height);
            uint[] stages=Enumerable.Range(36,16).Concat(Enumerable.Range(128,16)).Select(i=>(uint)i).ToArray();
            var files=new Dictionary<string,byte[]>{{"Postprocess/HBAO/CoarseAO.hlsl",System.Text.Encoding.UTF8.GetBytes("float __pixel_shader():SV_Target0{return .25;}")}};
            var result=backend.CompilePostprocessGeneration("fixture-resource-free-hbao-replacement",files,new Dictionary<string,string>(),stages);
            ulong token=(ulong)result[0];backend.QueuePostprocessGeneration(token);
            foreach(bool clamp in new[]{true,false})foreach(int bits in Enumerable.Range(0,16))
            {
                post.Hbao.DepthClampToEdge=clamp;post.Hbao.BackgroundAOEnable=(bits&1)!=0;post.Hbao.ForegroundAOEnable=(bits&2)!=0;post.Hbao.DepthThresholdEnable=(bits&4)!=0;post.Hbao.UseGBufferNormals=(bits&8)!=0;
                Render();var actual=backend.ReadbackLightingInput(0,width,height);
                Check(actual.All(v=>Math.Abs(v-64)<=1),"selected HBAO replacement produces quarter AO in every pixel, clamp="+clamp+", variant="+bits);
                var state=backend.GetPostprocessGeneration((uint)(clamp?36:128)+(uint)bits);
                Check(state.Active==token&&state.Executions>0&&state.LastCompletion<=state.Completed,"selected HBAO permutation has completed native execution");
            }
            var restore=backend.CompilePostprocessGeneration("fixture-resource-free-hbao-stock-restore",new Dictionary<string,byte[]>(),new Dictionary<string,string>(),stages);
            backend.QueuePostprocessGeneration((ulong)restore[0]);post.Hbao=MyHBAOData.Default;post.Hbao.Enabled=true;post.Hbao.BlurEnable=false;post.Hbao.PowerExponent=1;post.Hbao.DepthClampToEdge=true;Render();
            Check(stockAo.SequenceEqual(backend.ReadbackLightingInput(0,width,height)),"HBAO replacement unregister restores exact stock AO pixels");
            post.Hbao=saved;Check(backend.GetStats().ValidationErrors==0,"HBAO replacement and restoration have zero validation errors");
        }
        if(hbaoReplacementOnly){HbaoReplacement();return 0;}
        var baseline=Render();Check(baseline.Where((v,i)=>i%4!=3&&v>20).Count()>100,"stock Hable tone map presents lit geometry");
        post.Settings.Data.LuminanceExposure+=2;var brighter=Render();Check(Changes(baseline,brighter)>50,"exposure settings affect actual pixels");post.Settings.Data.LuminanceExposure-=2;
        post.Settings.BloomEnabled=true;post.Settings.Data.BloomMult=2;var beforeBloom=backend.GetPostprocessStats();var bloom=Render();Check(Changes(baseline,bloom)>50,"stock bloom pyramid affects actual pixels");
        int bloomHeight=post.Settings.HighQualityBloom?height:height/2;
        int bloomLevels=Math.Clamp((int)Math.Log2(bloomHeight)-8+Math.Clamp(post.Settings.BloomSize,1,8),1,10);
        Check(backend.GetPostprocessStats().BloomPasses-beforeBloom.BloomPasses==(ulong)(2*bloomLevels),"stock bloom stops at the first downsample level without an extra prefilter-sized upsample");post.Settings.BloomEnabled=false;
        var aa=Render(true);Check(Changes(baseline,aa)>10,"stock FXAA filters triangle edges using luminance alpha");
        var preFxaa=backend.ReadbackFxaaInput(false,width,height);var postFxaa=backend.ReadbackFxaaInput(true,width,height);
        Check(preFxaa.Length==width*height*4 && Enumerable.Range(0,width*height).Any(i=>preFxaa[i*4+3]<255) && Enumerable.Range(0,width*height).All(i=>postFxaa[i*4+3]==255),"completed FXAA diagnostics read actual luminance-alpha input and opaque output images");
        Check(Changes(preFxaa,postFxaa)>10 && postFxaa.SequenceEqual(backend.ReadbackFxaaInput(true,width,height)),"same-frame FXAA diagnostics expose filtered pixels and restore sampling layouts");
        post.Settings.Data.ChromaticFactor=.5f;post.Settings.Data.VignetteStart=2;var chromatic=Render();Check(Changes(baseline,chromatic)>10,"stock chromatic and vignette pass affects pixels");
        foreach(int iterations in Enumerable.Range(1,16)){post.Settings.ChromaticIterations=iterations;Render();}Check(backend.GetPostprocessStats().ChromaticDispatches==17,"all sixteen chromatic permutations dispatch");post.Settings.Data.ChromaticFactor=0;post.Settings.Data.VignetteStart=0;
        post.Ssao.Enabled=true;post.Ssao.UseBlur=true;Render();Check(backend.GetPostprocessStats().SsaoPasses==3,"SSAO and both stock Gaussian blur passes execute");post.Ssao.Enabled=false;
        post.Settings.EnableEyeAdaptation=true;var adaptive=Render();for(int i=0;i<8;i++)adaptive=Render();Check(Changes(baseline,adaptive)>30,"histogram exposure history adapts pixels over multiple frames");Check(backend.GetPostprocessStats().HistogramDispatches==9,"one stock histogram dispatch per adaptive frame");
        post.Settings.EyeAdaptationPrioritizeScreenCenter=true;Render();post.Settings.EnableEyeAdaptation=false;post.Settings.EnableTonemapping=false;var linear=Render();Check(Changes(baseline,linear)>30,"stock tone-disable variant is honored");post.Settings.EnableTonemapping=true;
        post.Hbao.Enabled=true;var hbao=Render();Check(backend.GetPostprocessStats().HbaoPasses==23,"sixteen-slice stock HBAO and bilateral blur execute");
        foreach(int bits in Enumerable.Range(0,16)){post.Hbao.BackgroundAOEnable=(bits&1)!=0;post.Hbao.ForegroundAOEnable=(bits&2)!=0;post.Hbao.DepthThresholdEnable=(bits&4)!=0;post.Hbao.DepthThreshold=20;post.Hbao.UseGBufferNormals=(bits&8)!=0;Render();}Check(true,"all sixteen HBAO coarse permutations execute");
        foreach(int bits in Enumerable.Range(0,4)){post.Hbao.BlurRadius4=(bits&1)!=0;post.Hbao.BlurSharpnessFunctionEnable=(bits&2)!=0;Render();}post.Hbao.BlurEnable=false;post.Hbao.DepthClampToEdge=true;Render();Check(true,"all HBAO bilateral kernels, profiles and unblurred path execute");post.Hbao.Enabled=false;
        var empty=Render(opaque:false);Check(empty.Where((v,i)=>i%4!=3&&v!=0).Count()==0,"empty scene remains finite and black");
        backend.Resize(17,11,0);width=17;height=11;Render();backend.Resize(65,63,0);width=65;height=63;Render();Check(true,"odd-size resize and restored postprocess targets execute");
        foreach(float scale in new[]{.75f,.5f,1f,.25f,.85f,1f}){fraction=scale;backend.SetRenderFraction(scale);post.Hbao.Enabled=true;var scaled=Render();Check(scaled.Length==width*height*4 && scaled.Where((v,i)=>i%4!=3&&v>20).Count()>20,"DRS preserves full output and visible geometry at "+scale);var resolution=backend.GetResolutionStats();Check(resolution.Width==(uint)Math.Round(width*scale,MidpointRounding.AwayFromZero) && resolution.Height==(uint)Math.Round(height*scale,MidpointRounding.AwayFromZero) && resolution.OutputWidth==width,"DRS reports independent world dimensions at "+scale);}
        Render();var timing=backend.GetResolutionStats();Check(timing.Samples>0 && timing.GpuMs>0 && float.IsFinite(timing.GpuMs),"completed Vulkan timestamps supply finite GPU time");
        bool rejected=false;try{backend.SetRenderFraction(float.NaN);}catch{rejected=true;}Check(rejected,"nonfinite render fraction rejected");post.Hbao.Enabled=false;
        casts=true;var matrices=new float[128];for(int i=0;i<8;i++)Array.Copy(NativeBackend.MatrixValues(MatrixD.CreateLookAt(new Vector3D(0,0,10),new Vector3D(0,0,-3),Vector3D.Up)*MatrixD.CreateOrthographic(12,12,.1,100)),0,matrices,i*16,16);backend.SetShadowCascades(matrices,new float[]{5,10,20,40,80,160,320,640});foreach(uint size in new uint[]{512,2048,1024}){backend.SetShadowQuality(size,.001f);backend.SetShadowOptions(true);Render();}Check(backend.GetPostprocessStats().ShadowDispatches>0,"eight cascades and deferred shadow-quality changes execute stock CSM");backend.SetShadowCascades(Array.Empty<float>(),Array.Empty<float>());
        backend.SetLights(new[]{new NativeLight{PositionRange=new float[]{0,0,0,10},ColorIntensity=new float[]{10,10,10,1},DirectionCone=new float[]{0,0,-1,.5f},Flags=1}});
        var spotProjection=NativeBackend.MatrixValues(MatrixD.CreatePerspectiveFieldOfView(Math.PI*.65,1,.1,10));backend.SetSpots(256,new[]{new NativeSpot{Light=0,Projection=spotProjection}});var unmasked=Render();backend.UploadTexture(13,1,1,0,new byte[]{0,0,0,255},false);backend.SetSpots(256,new[]{new NativeSpot{Light=0,Cookie=13,Projection=spotProjection}});var masked=Render();Check(Changes(unmasked,masked)>50,"reflector cookie controls stock spotlight pixels");backend.SetSpots(256,new[]{new NativeSpot{Light=0,Flags=1,Projection=spotProjection}});Render();backend.SetSpots(1024,new[]{new NativeSpot{Light=0,Flags=1,Projection=spotProjection}});Render();Check(backend.GetSpotStats().Maps==2 && backend.GetSpotStats().Draws==2,"stock spotlight shadow PCF, depth replay and quality resize execute");ulong spotDraws=backend.GetSpotStats().Draws;excludeSpot=true;Render();Check(backend.GetSpotStats().Draws==spotDraws,"per-light shadow exclusion suppresses depth replay");excludeSpot=false;Render();Check(backend.GetSpotStats().Draws==spotDraws+1,"clearing exclusion restores shadow depth replay");backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(512,Array.Empty<NativeSpot>());casts=false;
        // Actual triangle reaches the cone at x=18,z=-11; its sphere centre
        // is x=20,z=-10,r=2.5, previously rejected by the horizontal margin.
        NativeWorldVertex EdgeVertex(float x,float y,float z){var v=V(x,y);v.Material.Z=z;return v;}
        backend.UploadWorldMesh(2,new[]{EdgeVertex(18,-.3f,-11),EdgeVertex(18,.3f,-11),EdgeVertex(21,0,-10)},new uint[]{0,1,2});
        backend.SetLights(new[]{new NativeLight{PositionRange=new float[]{0,0,0,40},ColorIntensity=new float[]{10,10,10,1},DirectionCone=new float[]{0,0,-1,.5f},Flags=1}});
        backend.SetSpots(256,new[]{new NativeSpot{Light=0,Flags=1,Projection=NativeBackend.MatrixValues(MatrixD.CreatePerspectiveFieldOfView(Math.PI*2/3,1,.1,40))}});
        backend.BeginPacketCollection();backend.SetSpotRedraw(1,1);
        backend.AddWorldPacket(0,2,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),flags:4096,origin:new Vector3(20,0,-10),radius:2.5f);
        backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
        ulong edgeBefore=backend.GetSpotStats().Draws;backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();backend.Readback(width,height);
        Check(backend.GetSpotStats().Draws==edgeBefore+1 && backend.GetPerformanceStats().SpotCommands==1,"cone-edge sphere retains intersecting spotlight shadow geometry in the production queue");
        backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(512,Array.Empty<NativeSpot>());backend.RemoveResource(3,2);
        var plain=Render();highlight=1;var outlined=Render();Check(Changes(plain,outlined)>30,"stock highlight geometry, blur and composition affect pixels");Check(Enumerable.Range(0,width*height).Count(i=>plain[i*4+1]<5 && outlined[i*4+1]>10)>5,"highlight blur expands beyond visible geometry");highlight=2;var overlap=Render();Check(Enumerable.Range(0,width*height).Count(i=>plain[i*4+1]>20 && Math.Abs(plain[i*4+1]-overlap[i*4+1])>2)==0,"overlap mask suppresses interior highlight composition");highlight=1;foreach(int radius in Enumerable.Range(1,32)){thickness=radius;Render();}Check(backend.GetHighlightStats().BlurPasses==68,"all 32 stock exponential blur radii execute");thickness=.5f;Render();Check(backend.GetHighlightStats().BlurPasses==68,"midpoint-to-even subpixel thickness skips the identity blur");thickness=1.5f;Render();Check(backend.GetHighlightStats().BlurPasses==70,"fractional thickness rounds to the stock even radius");highlight=0;Check(Changes(plain,Render())==0,"clearing highlight packets removes the outline");
        if(stockHighlightOnly){Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"stock highlight core/synchronization validation remains clean");Console.WriteLine("PASS "+checks+" focused stock highlight checks");return 0;}
        byte[] SharedHighlighted(bool share)
        {
            var bytes=new byte[64];Buffer.BlockCopy(new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,0},0,bytes,0,64);
            backend.BeginPacketCollection();
            for(int i=0;i<2;i++){
                uint draw=backend.AddWorldPacket(2,1,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[2],MatrixD.CreateTranslation(i==0?-.6:.6,0,0),Vector3.One,Vector3.Zero,1,0),share?bytes:(byte[])bytes.Clone(),1,512);
                if(i==1)backend.AddHighlightPacket(draw,0,3,0,1,new Vector4(0,1,0,1),4);
            }
            post.Configure(backend,width,height,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.RequestReadback();backend.EndFrame();return backend.Readback(width,height);
        }
        Check(Changes(SharedHighlighted(false),SharedHighlighted(true))==0,"highlighting a later draw preserves GPU pixels when its instance arena slice is shared");
        queries=new[]{new NativeOcclusionDraw{Id=1,Minimum=new float[]{-.1f,-.1f,-4.2f},Extents=new float[]{.2f,.2f,.2f}},new NativeOcclusionDraw{Id=2,Minimum=new float[]{-.1f,-.1f,-2.2f},Extents=new float[]{.2f,.2f,.2f}},new NativeOcclusionDraw{Id=3,Minimum=new float[]{-1,-1,-.01f},Extents=new float[]{2,2,1},Flags=1}};Render();var visibility=backend.GetOcclusion(out ulong token);Check(token==queryToken && visibility.Length==3 && visibility[0].Visible==0 && visibility[1].Visible!=0 && visibility[2].Visible!=0,"stock bounding-box queries distinguish hidden/front/near-plane cases");Render(opaque:false);visibility=backend.GetOcclusion(out token);Check(visibility.All(x=>x.Visible!=0),"removed occluder makes previously hidden bounds visible");queries=Array.Empty<NativeOcclusionDraw>();Render();Check(backend.GetOcclusionStats().Queries==6 && backend.GetOcclusionStats().Results==6,"completed occlusion results are returned once per frame slot");
        Check(Marshal.SizeOf<NativePackedOcclusionDraw>()==Marshal.SizeOf<NativeOcclusionDraw>(),"packed occlusion preserves native box ABI");packedQueries=true;queries=new[]{new NativeOcclusionDraw{Id=1,Minimum=new float[]{-.1f,-.1f,-4.2f},Extents=new float[]{.2f,.2f,.2f}},new NativeOcclusionDraw{Id=2,Minimum=new float[]{-.1f,-.1f,-2.2f},Extents=new float[]{.2f,.2f,.2f}},new NativeOcclusionDraw{Id=3,Minimum=new float[]{-1,-1,-.01f},Extents=new float[]{2,2,1},Flags=1}};Render();visibility=backend.GetOcclusion(out token);Check(visibility.Length==3 && token==queryToken && visibility[0].Visible==0 && visibility[1].Visible!=0 && visibility[2].Visible!=0,"packed occlusion retains hidden/front/near-plane selection and token ownership");queries=Array.Empty<NativeOcclusionDraw>();packedQueries=false;
        backend.UploadTexture(20,1,1,0,new byte[]{255,255,255,255},false);var noBillboards=Render();billboard=3;Check(Changes(noBillboards,Render())>50,"LDR billboard blends through an sRGB attachment before FXAA");billboard=4;Check(Changes(noBillboards,Render())>50,"PostPP billboard blends after tone and chromatic processing");var custom=new float[512];Array.Copy(NativeBackend.MatrixValues(MatrixD.CreateTranslation(.7,0,0)*projection),custom,16);backend.SetBillboardProjections(custom);billboard=5;Check(Changes(Render(),noBillboards)>50,"copied custom billboard projection produces pixels");billboard=0;
        post.Ssao.Enabled=true;post.Ssao.UseBlur=true;post.Hbao.Enabled=true;
        var beforeAo=backend.GetPostprocessStats();Render(ambientOcclusion:false);var afterAo=backend.GetPostprocessStats();
        Check(afterAo.SsaoPasses==beforeAo.SsaoPasses && afterAo.HbaoPasses==beforeAo.HbaoPasses,"user AO disable suppresses both algorithms despite enabled environment settings");
        var disabledAo=backend.ReadbackLightingInput(0,width,height);var resolvedSun=backend.ReadbackLightingInput(1,width,height);
        Check(disabledAo.Length==width*height && disabledAo.All(x=>x==255),"completed lighting input reads the actual neutral R8 AO image");
        Check(resolvedSun.Length==width*height && resolvedSun.SequenceEqual(backend.ReadbackLightingInput(1,width,height)) && disabledAo.SequenceEqual(backend.ReadbackLightingInput(0,width,height)),"completed sun/AO readback restores image layouts for repeated reads");
        Render();var preferredAo=backend.GetPostprocessStats();
        Check(preferredAo.SsaoPasses==afterAo.SsaoPasses+3 && preferredAo.HbaoPasses==afterAo.HbaoPasses,"stock SSAO priority wins when both environment algorithms are enabled");
        post.Process(new MyRenderMessageUpdateDebugOverrides{Overrides=new(){Postprocessing=true,SSAO=false}});Render();afterAo=backend.GetPostprocessStats();
        Check(afterAo.SsaoPasses==preferredAo.SsaoPasses && afterAo.HbaoPasses==preferredAo.HbaoPasses,"stock SSAO debug override gates both algorithms");
        post.Process(new MyRenderMessageUpdateDebugOverrides{Overrides=new(){Postprocessing=false,SSAO=true}});Render();preferredAo=backend.GetPostprocessStats();
        Check(preferredAo.SsaoPasses==afterAo.SsaoPasses && preferredAo.HbaoPasses==afterAo.HbaoPasses,"stock postprocessing debug override gates both AO algorithms");
        post.Process(new MyRenderMessageUpdateDebugOverrides{Overrides=new(){Postprocessing=true,SSAO=true}});post.Ssao.Enabled=false;Render();afterAo=backend.GetPostprocessStats();
        Check(afterAo.SsaoPasses==preferredAo.SsaoPasses && afterAo.HbaoPasses>preferredAo.HbaoPasses,"HBAO resumes when the shared gate opens and SSAO is disabled");
        // With zero depth sharpness and exponent one, stock HBAO blur reduces
        // to its documented separable Gaussian. Compare the actual AO image
        // against that independent clamp-edge reference, including edge texels.
        var savedHbao=post.Hbao;post.Hbao=MyHBAOData.Default;post.Hbao.Enabled=true;post.Hbao.UseGBufferNormals=true;post.Hbao.DepthClampToEdge=true;post.Hbao.ForegroundAOEnable=false;post.Hbao.PowerExponent=1;post.Hbao.BlurSharpness=0;post.Hbao.BlurSharpnessFunctionEnable=false;post.Hbao.BlurRadius4=true;post.Hbao.BlurEnable=false;
        backend.InitializeHbaoJitter(17);
        NativeWorldVertex AoVertex(float x,float y,float z){var v=V(x,y);v.Material.Z=z;return v;}
        backend.UploadWorldMesh(1,new[]{AoVertex(-4,-4,-3),AoVertex(4,-4,-3),AoVertex(4,4,-3),AoVertex(-4,4,-3),AoVertex(-2,-1.5f,-2.5f),AoVertex(-1.2f,-1.5f,-2.5f),AoVertex(-1.2f,1.5f,-2.5f),AoVertex(-2,1.5f,-2.5f)},new uint[]{0,1,2,0,2,3,4,5,6,4,6,7});
        Render();var aoRaw=backend.ReadbackLightingInput(0,width,height);
        backend.InitializeHbaoJitter(20261018);Render();var differentJitter=backend.ReadbackLightingInput(0,width,height);
        Check(!differentJitter.SequenceEqual(aoRaw),"stock MyRandom HBAO seed changes actual coarse AO samples");
        backend.InitializeHbaoJitter(17);Render();
        Check(aoRaw.SequenceEqual(backend.ReadbackLightingInput(0,width,height)),"same stock HBAO seed restores exact sixteen-slice GPU radiance");
        var jitterSymbol=(IntPtr)typeof(NativeBackend).GetMethod("GetSymbol",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{"sevk_set_hbao_jitter"})!;
        var jitterCall=Marshal.GetDelegateForFunctionPointer<HbaoJitterCall>(jitterSymbol);
        var jitterContext=(IntPtr)typeof(NativeBackend).GetField("context",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(backend)!;
        var invalidJitter=new float[64];for(int i=0;i<16;i++)invalidJitter[i*4]=1;invalidJitter[63]=float.NaN;
        Check(jitterCall(jitterContext,invalidJitter,64)!=0 && jitterCall(jitterContext,new float[63],63)!=0,"HBAO jitter rejects a nonfinite tail and incorrect array size");Render();
        Check(aoRaw.SequenceEqual(backend.ReadbackLightingInput(0,width,height)),"rejected HBAO jitter does not partially mutate the published sample pattern");
        File.WriteAllBytes(Path.Combine(output,"hbao-blur-raw.r8"),aoRaw);int aoEdgeContrast=Enumerable.Range(0,height).Max(y=>Math.Abs(aoRaw[y*width]-aoRaw[y*width+width-1]));
        Check(aoRaw.Min()<230 && aoEdgeContrast>10,"HBAO blur fixture has distinct occlusion at opposite viewport edges (minimum "+aoRaw.Min()+", edge contrast "+aoEdgeContrast+")");
        post.Hbao.BlurEnable=true;Render();var aoFiltered=backend.ReadbackLightingInput(0,width,height);
        float[] Gaussian(float[] input,bool horizontal){var output=new float[input.Length];for(int y=0;y<height;y++)for(int x=0;x<width;x++){
            float Sample(float offset){float at=(horizontal?x:y)+offset;int low=(int)Math.Floor(at);float fraction=at-low;int limit=horizontal?width:height;int a=Math.Clamp(low,0,limit-1),b=Math.Clamp(low+1,0,limit-1);return horizontal?input[y*width+a]*(1-fraction)+input[y*width+b]*fraction:input[a*width+x]*(1-fraction)+input[b*width+x]*fraction;}
            float sum=input[y*width+x],weight=1;foreach(int sign in new[]{-1,1})foreach(int r in new[]{1,2,3}){float w=(float)Math.Pow(2,-r*r/12.5);sum+=w*Sample(sign*(r==3?3.5f:r));weight+=w;}output[y*width+x]=sum/weight;
        }return output;}
        var aoHorizontal=Gaussian(aoRaw.Select(v=>(float)(Half)(v/255f)).ToArray(),true).Select(v=>(float)(Half)v).ToArray();
        var aoExpected=Gaussian(aoHorizontal,false).Select(v=>(byte)Math.Clamp((int)Math.Round(v*255),0,255)).ToArray();
        int aoMaximumDelta=aoExpected.Zip(aoFiltered,(a,b)=>Math.Abs(a-b)).Max();
        Check(aoMaximumDelta<=1,"stock HBAO radius-four blur matches the linear-clamp Gaussian at viewport edges (max delta "+aoMaximumDelta+")");
        post.Hbao.BlurEnable=false;post.Hbao.Radius=4;post.Hbao.BackgroundAOEnable=false;
        backend.UploadWorldMesh(1,new[]{AoVertex(-4,-4,-3),AoVertex(4,-4,-3),AoVertex(4,4,-3),AoVertex(-4,4,-3)},new uint[]{0,1,2,0,2,3});
        Render();var flatClamp=backend.ReadbackLightingInput(0,width,height);
        Check(flatClamp.All(v=>v>=254),"flat HBAO border fixture has no self-occlusion with clamp-to-edge");
        post.Hbao.DepthClampToEdge=false;Render();var flatBorder=backend.ReadbackLightingInput(0,width,height);
        Check(flatBorder.All(v=>v>=254),"stock negative-depth border adds no false viewport-edge occluders to a flat plane");
        post.Hbao=savedHbao;
        if(generationMode)
        {
            Check(Enumerable.Range(256,4).All(id=>{var state=backend.GetPostprocessGeneration((uint)id);return state.Active==generationToken&&state.Executions>0&&state.LastCompletion<=state.Completed;}),"all four deferred lighting generation variants execute with stock-equivalent pixels");
        }
        // Exercise the actual shared shadow sampler against real rendered D32
        // maps. Test-only fragment programs expose border and bilinear PCF state.
        string shadowRoot=Path.Combine(output,"ShadowSamplerSources");Directory.CreateDirectory(shadowRoot);
        var shadowExpressions=new Dictionary<string,string>{{"corner","Depth.SampleCmpLevelZero(ComparisonSampler,float2(0,0),.5).xxx"},{"half","Depth.SampleCmpLevelZero(ComparisonSampler,float2(.5,.5),.5).xxx"},{"constant","float3(.5,.5,.5)"}};
        foreach(var item in shadowExpressions)File.WriteAllText(Path.Combine(shadowRoot,item.Key+".hlsl"),"Texture2D<float> Depth:register(t14);SamplerComparisonState ComparisonSampler:register(s15);void __pixel_shader(out float3 output:SV_Target0){output="+item.Value+";}");
        using(var shadowCompiler=new VulkanShaderCompiler(Path.GetFullPath("Assets/Tools/win-x64"),Path.Combine(output,"ShadowSamplerCache"))) {
            var privateMembers=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var worldApi=typeof(NativeBackend).GetProperty("WorldApi",privateMembers)!.GetValue(backend)!;
            var initializeLighting=(Delegate)worldApi.GetType().GetField("InitLighting",privateMembers)!.GetValue(worldApi)!;
            var nativeContext=(IntPtr)typeof(NativeBackend).GetField("context",privateMembers)!.GetValue(backend)!;
            void ShadowProgram(string name){var compiled=shadowCompiler.Compile(new VulkanShaderRequest{SourcePath=Path.Combine(shadowRoot,name+".hlsl"),ContentDirectory=shadowRoot,Profile="ps_5_0",EntryPoint="__pixel_shader"});var code=SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiled.SpirvPath));Check((int)initializeLighting.DynamicInvoke(nativeContext,4u,code,(ulong)code.Length)! == 0,"shadow sampler diagnostic pipeline "+name);}
            NativeWorldVertex ShadowVertex(float x,float y){var v=V(x,y);v.Material.Z=.25f;return v;}
            void ShadowRectangle(float right)=>backend.UploadWorldMesh(1,new[]{ShadowVertex(-1,-1),ShadowVertex(right,-1),ShadowVertex(right,1),ShadowVertex(-1,1)},new uint[]{0,1,2,0,2,3});
            backend.SetLights(new[]{new NativeLight{PositionRange=new float[]{0,0,0,10},ColorIntensity=new float[]{1,1,1,1},DirectionCone=new float[]{0,0,1,0},Flags=1}});
            backend.SetSpots(256,new[]{new NativeSpot{Light=0,Flags=1,Projection=NativeBackend.MatrixValues(MatrixD.Identity)}});casts=true;post.Hbao.Enabled=false;
            ShadowRectangle(1);ShadowProgram("corner");var corner=Render();var shadowDepth=backend.ReadbackSpot(0,256);
            Check(shadowDepth[0]<.5f && shadowDepth[^1]<.5f,"shadow sampler fixture fills both outer D32 texels with an occluder");
            Check(corner.Where((v,i)=>i%4!=3).Max()<=1,"stock comparison sampler clamps the occluded map edge without white-border light leakage");
            ShadowRectangle(0);ShadowProgram("constant");var halfReference=Render();ShadowProgram("half");var halfPcf=Render();
            Check(halfReference.Zip(halfPcf,(a,b)=>Math.Abs(a-b)).Max()<=1,"stock comparison sampler linearly filters the half-occluded texel boundary");
        }
        if(generationMode)Check(backend.GetPostprocessGeneration(259).Active==0&&backend.PostprocessGenerationState(259)[6]==null,"legacy diagnostic replacement clears the lighting generation identity");
        backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(256,Array.Empty<NativeSpot>());casts=false;queries=Array.Empty<NativeOcclusionDraw>();
        backend.UploadWorldMesh(1,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});
        post.Hbao.Enabled=false;post.Ssao.Enabled=false;
        typeof(GuestBridge).GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{backend,null});
        ushort[] Hdr(){var bytes=new byte[width*height*8];GuestBridge.Readback(0xFFFF000000000001UL,bytes);return MemoryMarshal.Cast<byte,ushort>(bytes).ToArray();}
        backend.SetHdrQuality(true);Render();var highPrecision=Hdr();
        backend.SetHdrQuality(false);Render();var packedHdr=Hdr();Render();
        Check(packedHdr.SequenceEqual(Hdr()),"packed main HDR remains stable across both native frame slots");
        Check(packedHdr.Where((value,i)=>i%4<3).Any(value=>value!=0)&&packedHdr.Where((value,i)=>i%4<3).All((value)=>value<0x7c00),"packed main HDR readback contains actual finite radiance");
        Check(Enumerable.Range(0,packedHdr.Length).All(i=>i%4==3?packedHdr[i]==0x3c00:(packedHdr[i]&(i%4==2?31:15))==0),"normal-quality main lighting uses exact R11G11B10 precision and implicit alpha one");
        Check(Enumerable.Range(0,packedHdr.Length).Count(i=>i%4<3&&packedHdr[i]!=highPrecision[i])>20,"normal and high-quality targets expose different actual lighting precision");
        backend.SetHdrQuality(true);Render();Check(highPrecision.SequenceEqual(Hdr()),"switching HqTarget back restores exact RGBA16F lighting");
        post.Settings.BloomEnabled=true;post.Settings.HighQualityBloom=true;post.Settings.BloomSize=8;
        var highBloom=Render();backend.SetHdrQuality(false);var normalBloom=Render();
        Check(Changes(highBloom,normalBloom)>0,"packed bloom/exposure intermediates execute the actual normal-quality pipeline");
        backend.SetHdrQuality(true);Check(highBloom.SequenceEqual(Render()),"bloom targets recreate by format at unchanged dimensions when HqTarget returns");
        post.Settings.HighQualityBloom=false;post.Settings.BloomSize=1;
        ulong beforeSmallBloom=backend.GetPostprocessStats().BloomPasses;Render();
        Check(backend.GetPostprocessStats().BloomPasses-beforeSmallBloom==2,"single-level stock bloom returns the sole downsample without any upsample pass");post.Settings.BloomEnabled=false;
        backend.SetHdrQuality(false);billboard=2;Render();var packedBillboard=Hdr();billboard=0;
        Check(Enumerable.Range(0,packedHdr.Length/4).Count(i=>packedBillboard[i*4]>packedHdr[i*4])>20,"additive billboard pipeline writes the packed main HDR target");
        backend.SetHdrQuality(true);Render();
        if(generationMode)
        {
            post.Hbao.Enabled=true;post.Hbao.BlurEnable=false;
            foreach(bool clamp in new[]{true,false})foreach(int bits in Enumerable.Range(0,16)){post.Hbao.DepthClampToEdge=clamp;post.Hbao.BackgroundAOEnable=(bits&1)!=0;post.Hbao.ForegroundAOEnable=(bits&2)!=0;post.Hbao.DepthThresholdEnable=(bits&4)!=0;post.Hbao.UseGBufferNormals=(bits&8)!=0;Render();}
            post.Hbao.Enabled=false;post.Settings.BloomEnabled=true;post.Settings.HighQualityBloom=true;post.Settings.BloomSize=8;
            foreach(bool antiflicker in new[]{false,true}){post.Settings.BloomAntiFlickerFilter=antiflicker;Render();}
            post.Settings.BloomEnabled=false;
            foreach(bool tone in new[]{false,true})foreach(bool aaEnabled in new[]{false,true}){post.Settings.EnableTonemapping=tone;Render(aaEnabled);}
            var unexecuted=generationStages.Where(id=>id<256).Where(id=>backend.GetPostprocessGeneration(id).Active!=generationToken||backend.GetPostprocessGeneration(id).Executions==0).ToArray();
            Check(unexecuted.Length==0,"all 78 selected postprocess generation variants execute: missing="+string.Join(",",unexecuted));
            Check(backend.ActivePostprocessGeneration.Token==generationToken,"full postprocess family generation publishes coherent immutable metadata");
            HbaoReplacement();
        }
        var stats=backend.GetStats();Check(stats.ValidationEnabled!=0 && stats.ValidationErrors==0,"zero Vulkan core and synchronization validation errors");File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=stats.ValidationErrors,Postprocess=backend.GetPostprocessStats(),Highlights=backend.GetHighlightStats(),Spots=backend.GetSpotStats(),Resolution=backend.GetResolutionStats(),Occlusion=backend.GetOcclusionStats()},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));return 0;
    }
}
