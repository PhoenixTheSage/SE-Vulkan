using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.Import;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

class Program
{
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(bin,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};return Run(bin);}
    [MethodImpl(MethodImplOptions.NoInlining)]static unsafe int Run(string bin)
    {
        int checks=0;void Check(bool condition,string text){if(!condition)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        string output=Path.GetFullPath("TestResults/ProbeGpuTests");Directory.CreateDirectory(output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));
        Check(Marshal.SizeOf<NativeProbeConfig>()==8096 && Marshal.SizeOf<NativeProbeStats>()==64,"probe ABI packing");
        using(var file=File.OpenRead(Path.Combine(bin,"../Content/Textures/Miscellaneous/ambient_brdf.dds"))){var lut=DdsImage.Read(file,true);Check(lut.Format==9 && !lut.Srgb && lut.Data.Length==lut.Width*lut.Height*4,"installed stock RG16F BRDF texture retains half-float channels");backend.UploadTextureMips(13,lut.Width,lut.Height,lut.Format,lut.Mips,false);}
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.BeginUploads();backend.UploadWorldMesh(1,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});backend.UploadTexture(10,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(11,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(12,1,1,0,new byte[]{255,1,0,0},false);backend.UploadWorldCube(14,1,1,0,Enumerable.Range(0,6).SelectMany(_=>new byte[]{0,0,100,255}).ToArray(),true);backend.EndUploads();backend.SetWorldSkybox(14);
        MatrixD projection=(MatrixD)Matrix.CreatePerspectiveFovRhInfiniteComplementary((float)Math.PI/2,1,.1f);
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;light.SunDiscIntensity=0;light.EnvSkyboxBrightness=1;
        var probes=new VulkanProbes{Resolution=32};probes.Settings.TimeOut=10;
        List<NativeEnvironmentDraw> environment=null;float captureTime=0;bool nativeForward=false;
        NativePassFrame passFrame=default;NativePassView[] passViews=Array.Empty<NativePassView>();
        byte[] Render(bool opaque=true,uint flags=512,bool batch=false,Vector3? tint=null){backend.BeginPacketCollection();if(environment!=null)backend.QueueEnvironment(environment);if(opaque){if(batch)backend.AddRigidWorldPacket(0,1,10,11,12,0,MatrixD.Identity,tint??Vector3.One,Vector3.Zero,1,0,flags,0,Vector3.Zero,3);else backend.AddWorldPacket(0,1,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,tint??Vector3.One,Vector3.Zero,1,0),flags:flags);}probes.Configure(backend,Vector3D.Zero,light,MyPostprocessSettings.Default,MatrixD.Identity,13,.1f,captureTime);if(nativeForward){backend.PreparePassFrame(ref passFrame,passViews,passViews.Length);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);}backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,specularProbes:backend.ProbesReady),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"probe frame begins");backend.RequestReadback();backend.EndFrame();return backend.Readback(64,64);}
        // Timeout is measured from capture start, without an extra minimum hold.
        void AdvanceCapture(){captureTime+=Math.Max(0,probes.Settings.TimeOut)+1;}
        float[] Floats(byte[] data)=>Enumerable.Range(0,data.Length/2).Select(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data,i*2))).ToArray();
        Check(Marshal.SizeOf<NativeProbeScheduleStats>()==32,"probe stage statistics ABI packing");Render();var s=backend.GetProbeStats();Check(s.Ready==1&&s.Faces==6&&s.GeometryDraws==6&&s.PrefilterPasses==60,"startup completes all capture and filtering faces before publication");
        var publishedBefore=backend.ReadbackProbe(4,5,3,32);
        AdvanceCapture();for(int stage=0;stage<19;stage++){Render();Check(backend.GetProbeScheduleStats().Stage==(uint)(stage+1)&&backend.GetProbeStats().Cycles==1,"timed refresh advances exact processing stage without publishing partial work "+stage);if(stage==5||stage==13||stage==18)Check(backend.ReadbackProbe(4,5,3,32).SequenceEqual(publishedBefore),"completed final probe remains unchanged during refresh stage "+stage);}Render();Check(backend.GetProbeScheduleStats().Stage==0&&backend.GetProbeStats().Cycles==2,"twentieth stage completes the timed probe cycle");
        float[] close=Floats(backend.ReadbackProbe(0,5,0,32)),far=Floats(backend.ReadbackProbe(1,5,0,32));Check(close.All(float.IsFinite) && far.All(float.IsFinite),"original probe pixels are finite");Check(close.Where((v,i)=>i%4<3 && v>far[i]+.03f).Count()>10,"near probe contains actual scene geometry absent from far probe");
        var faceGeometry=new Dictionary<uint,int>();for(uint face=0;face<6;face++){var nearFace=Floats(backend.ReadbackProbe(0,face,0,32));var farFace=Floats(backend.ReadbackProbe(1,face,0,32));faceGeometry[face]=nearFace.Where((v,i)=>i%4<3&&Math.Abs(v-farFace[i])>.03f).Count();}Console.WriteLine("FACEDELTA "+JsonSerializer.Serialize(faceGeometry));
        Check(faceGeometry[5]>10&&Enumerable.Range(0,5).All(face=>faceGeometry[(uint)face]==0),"face view-projection captures forward geometry only in its correct cubemap face");
        foreach(uint kind in Enumerable.Range(2,4).Select(i=>(uint)i))foreach(uint mip in Enumerable.Range(0,6).Select(i=>(uint)i)){float[] pixels=Floats(backend.ReadbackProbe(kind,5,mip,32));Check(pixels.All(float.IsFinite) && pixels.Any(v=>v>0),"filtered/blended probe mip contains finite light kind="+kind+" mip="+mip);}
        float[] blurred=Floats(backend.ReadbackProbe(2,5,3,32));Check(blurred.Where((v,i)=>i%4<3&&v>.01f).Count()>10,"GGX rough mip retains scene illumination");
        AdvanceCapture();for(int i=0;i<20;i++)Render(tint:new Vector3(0,1,0));var tintedProbe=backend.ReadbackProbe(0,5,0,32);
        AdvanceCapture();for(int i=0;i<20;i++)Render(batch:true,tint:new Vector3(0,1,0));var groupedProbe=backend.ReadbackProbe(0,5,0,32);
        Check(tintedProbe.SequenceEqual(groupedProbe),"derived generic forward tint preserves exact rigid probe pixels");
        Check(Floats(tintedProbe).Where((v,i)=>i%4<3 && Math.Abs(v-close[i])>.03f).Count()>10,"probe tint fixture visibly differs from white geometry");
        var original=Render();AdvanceCapture();for(int i=0;i<20;i++)Render(false);var empty=Floats(backend.ReadbackProbe(0,5,0,32));Check(empty.Where((v,i)=>i%4<3&&Math.Abs(v-close[i])>.03f).Count()>10,"removing geometry updates the captured probe");
        AdvanceCapture();for(int i=0;i<20;i++)Render(flags:512|4096);var only=Floats(backend.ReadbackProbe(0,5,0,32));Check(only.Where((v,i)=>i%4<3&&v>empty[i]+.03f).Count()>10,"probe-only geometry outside the main view is captured");
        probes.Resolution=16;Render();Check(backend.GetProbeStats().Ready==1&&backend.GetProbeStats().Faces>=6,"resolution change rebuilds all face-owned targets and publishes only complete startup work");
        probes.Resolution=32;for(int i=0;i<20;i++)Render(false);float[] clearSky=Floats(backend.ReadbackProbe(1,5,0,32));
        var atmosphere=new ReflectedConstantBuffer(backend.EnvironmentLayouts[1],"AtmosphereConstants");atmosphere.Set("PlanetCenter",0,0,-80);atmosphere.Set("RadiusAtmosphere",7632000);atmosphere.Set("RadiusGround",6423600);atmosphere.Set("BetaRayleighScattering",5.8e-6f/20,1.35e-5f/7.5f,3.31e-5f/10);atmosphere.Set("BetaMieScattering",4e-7f,4e-7f,4e-7f);atmosphere.Set("MieG",.9998f);atmosphere.Set("HeightScaleRayleighMie",80000,60000);atmosphere.Set("PlanetScaleFactor",50f/6360000);atmosphere.Set("AtmosphereScaleFactor",.4f);atmosphere.Set("Intensity",35);atmosphere.Set("WorldViewProj",NativeBackend.MatrixValues(projection));environment=new(){NativeEnvironmentDraw.Create(0,1,atmosphere.Bytes)};
        backend.SetProbeAtmosphere(1,35,0,Vector3.Forward);ulong atmoBefore=backend.GetProbeStats().AtmospherePasses;AdvanceCapture();for(int i=0;i<20;i++)Render(false);float[] atmosphereSky=Floats(backend.ReadbackProbe(1,5,0,32));Check(atmosphereSky.All(float.IsFinite) && atmosphereSky.Where((v,i)=>i%4<3 && Math.Abs(v-clearSky[i])>.001f).Count()>100,"stock probe atmosphere produces finite scattering pixels");Check(backend.GetProbeStats().AtmospherePasses==atmoBefore+6,"all six faces execute stock atmosphere capture");
        backend.SetProbeAtmosphere(1,0,0,Vector3.Forward);AdvanceCapture();for(int i=0;i<20;i++)Render(false);float[] disabledSky=Floats(backend.ReadbackProbe(1,5,0,32));Check(disabledSky.SequenceEqual(clearSky),"zero ambient atmosphere intensity restores sky capture");
        probes.Settings.TimeOut=10;ulong faces=backend.GetProbeStats().Faces;AdvanceCapture();for(int i=0;i<20;i++)Render(false);ulong refreshed=backend.GetProbeStats().Faces;for(int i=0;i<3;i++)Render(false);Check(refreshed==faces+6 && backend.GetProbeStats().Faces==refreshed,"probe timeout holds completed capture while blending continues");captureTime+=11;Render(false);Check(backend.GetProbeStats().Faces==refreshed+1,"probe refresh resumes after timeout");
        probes.Settings.TimeOut=.3f;captureTime+=11;Render(false);ulong shortStart=backend.GetProbeStats().Cycles;
        for(int i=0;i<19;i++)Render(false);Check(backend.GetProbeStats().Cycles==shortStart+1,"short positive timeout still completes all processing stages");
        captureTime+=.31f;ulong shortFaces=backend.GetProbeStats().Faces;Render(false);Check(backend.GetProbeStats().Faces==shortFaces+1,"point-three-second refresh resumes from cycle start without five-second hold");
        probes.Settings.TimeOut=0;ulong immediateFaces=backend.GetProbeStats().Faces;for(int i=0;i<20;i++)Render(false);Render(false);
        Check(backend.GetProbeStats().Faces>=immediateFaces+6,"zero timeout keeps complete all-face updates active");
        ulong zeroFaces=backend.GetProbeStats().Faces,zeroCycles=backend.GetProbeStats().Cycles;Render(false);
        Check(backend.GetProbeStats().Faces==zeroFaces+6&&backend.GetProbeStats().Cycles==zeroCycles+1&&backend.GetProbeScheduleStats().Stage==0,"each zero-timeout frame records and completes all six faces");
        // Isolate radiance from culling: the legacy reference is uncullled,
        // whereas stock forward defaults to back-face culling. First match
        // raster state, then test the stock-facing winding explicitly.
        environment=null;backend.SetProbeAtmosphere(0,0,0,Vector3.Forward);backend.ConfigureNativePasses(library);Render();
        var referenceFaces=new Dictionary<(uint,uint),byte[]>();for(uint kind=0;kind<2;kind++)for(uint face=0;face<6;face++)referenceFaces[(kind,face)]=backend.ReadbackProbe(kind,face,0,32);
        ulong pm=backend.RegisterPassModel(new[]{new NativePassPart{Mesh=1,Color=10,Normal=11,Extensions=12,Count=3,Flags=2}}),pf=backend.RegisterPassFamily(Array.Empty<float>());backend.BindPassLevel(pf,0,pm);
        var pa=new NativePassActor{Owner=900,Family=pf,Flags=1|32,Scale=1,Lod=new(){Previous=-1}};pa.Bounds[0]=pa.Bounds[1]=-1;pa.Bounds[2]=pa.Bounds[5]=-3;pa.Bounds[3]=pa.Bounds[4]=1;pa.Basis[0]=pa.Basis[4]=pa.Basis[8]=1;pa.Handle=backend.RegisterPassActor(ref pa,Array.Empty<NativePassAppearance>());
        var configuredPassFrame=new NativePassFrame{Coefficient=1,GlobalFlags=3,SmoothDistance=100};configuredPassFrame.Global[6]=1;passFrame=configuredPassFrame;
        var viewFactory=typeof(VulkanScene).GetMethod("PassView",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        passViews=Enumerable.Range(0,6).Select(face=>(NativePassView)viewFactory.Invoke(null,new object[]{(uint)face+9,2u,new BoundingFrustumD(VulkanProbes.FaceView(face)*projection),new MyPassLoddingSetting(),1u,0u,0d,130d})!).ToArray();nativeForward=true;
        Render(flags:512|16384);var forwardGroups=backend.GetPassGroupAudit(out var forwardOwners);Check(forwardGroups.Length==1&&forwardGroups[0].View==14&&forwardOwners.SequenceEqual(new[]{900u}),"resident forward frustum selects the same visible cubemap face as clipped reference coverage");
        File.WriteAllBytes(Path.Combine(output,"forward-reference.f16"),referenceFaces[(0,5)]);File.WriteAllBytes(Path.Combine(output,"forward-resident.f16"),backend.ReadbackProbe(0,5,0,32));
        foreach(var item in referenceFaces)Check(backend.ReadbackProbe(item.Key.Item1,item.Key.Item2,0,32).SequenceEqual(item.Value),"equal-cull resident MESH forward radiance matches reference values kind="+item.Key.Item1+" face="+item.Key.Item2);
        Render(flags:512|16384);Check(backend.ReadbackProbe(0,5,0,32).SequenceEqual(referenceFaces[(0,5)]),"resident forward radiance matches in the other GPU frame slot");
        backend.ClearPassFrame();backend.RemovePassResource(0,pm);backend.UploadWorldMesh(1,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{2,1,0});
        pm=backend.RegisterPassModel(new[]{new NativePassPart{Mesh=1,Color=10,Normal=11,Extensions=12,Count=3}});backend.BindPassLevel(pf,0,pm);Render(flags:512|16384);
        Check(backend.ReadbackProbe(0,5,0,32).SequenceEqual(referenceFaces[(0,5)]),"stock-facing triangle uses default forward back-cull state with identical radiance");
        Render(flags:512|16384);Check(backend.ReadbackProbe(0,5,0,32).SequenceEqual(referenceFaces[(0,5)]),"stock-facing forward geometry survives the other GPU frame slot");
        backend.ClearPassFrame();backend.RemovePassResource(2,pa.Handle);backend.RemovePassResource(0,pm);backend.RemovePassResource(1,pf);
        if(Environment.GetEnvironmentVariable("SE_VULKAN_TEST_GPU_OPAQUE")=="1"){
            using var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"GpuCompiler"));using var shaders=compiler.CreateBatch(Path.GetFullPath(Path.Combine(bin,"../Content/Shaders")));
            foreach(uint id in new uint[]{0,4,8}){var spec=NativePassShaderVariants.Materials.Single(x=>x.Id==id);backend.InitializeGpuStockPipeline(id,false,VulkanShaderArtifact.FromCompilation(shaders.Compile(library.GpuOpaqueRequest(spec.Vertex)),"gpu-probe-vs"),VulkanShaderArtifact.FromCompilation(shaders.Compile(library.GpuOpaqueRequest(spec.Pixel)),"gpu-probe-ps"));}
            var ga=new NativeGpuOpaqueActor{Owner=900,Generation=1,Flags=1,Scale=1,SourceCount=1};ga.Minimum[0]=ga.Minimum[1]=-1;ga.Minimum[2]=ga.Maximum[2]=-3;ga.Maximum[0]=ga.Maximum[1]=1;ga.Basis[0]=ga.Basis[4]=ga.Basis[8]=1;for(int i=0;i<4;i++)ga.Colour[i]=1;
            var gp=new NativeGpuOpaquePart{IndexCount=3,Flags=2,Generation=1,Material=10};gp.Textures[0]=10;gp.Textures[1]=11;gp.Textures[2]=12;var gt=new ulong[13];gt[10]=10;gt[11]=11;gt[12]=12;
            backend.InitializeGpuOpaqueReference(new[]{ga},new[]{new NativeGpuOpaqueFamily{Generation=1,PartCount=1}},new[]{gp},15,new float[]{-1,-1,-3,1,-1,-3,0,1,-3},new uint[]{2,1,0},Array.Empty<byte>(),gt,30,4);
            var placeholder=WorldConstants.Object(library.Load(NativePassShaderVariants.Materials[0].Vertex).Layout,MatrixD.Identity,Vector3.One,Vector3.Zero,1,0);
            backend.SetGpuStockBatches(new[]{new NativeGpuOpaqueBatch{Mesh=1,MainPipeline=0,DepthPipeline=4,ForwardPipeline=8}},new ulong[]{1},placeholder);nativeForward=false;
            var gv=new NativeGpuOpaqueView[15];for(int f=0;f<6;f++){gv[9+f].Flags=1|4|32;for(int i=0;i<24;i++)gv[9+f].Planes[i]=passViews[f].Planes[i];}
            for(uint epoch=600;epoch<602;epoch++){var gf=new NativeGpuOpaqueFrame{Epoch=epoch,Coefficient=1,DistanceMult=1,Flags=3,SmoothCameraDistance=100,MaximumSeconds=1,MinimumSeconds=.25f};backend.QueueGpuOpaqueFrame(ref gf,gv);Render(false);foreach(var item in referenceFaces)Check(backend.ReadbackProbe(item.Key.Item1,item.Key.Item2,0,32).SequenceEqual(item.Value),"GPU-selected indexed stock forward matches captured radiance slot/kind/face="+epoch+"/"+item.Key.Item1+"/"+item.Key.Item2);}
        }
        var stats=backend.GetStats();if(stats.ValidationErrors!=0){Console.WriteLine("VALIDATION ERRORS "+stats.ValidationErrors+" "+stats.Detail);}Check(stats.ValidationEnabled!=0 && stats.ValidationErrors==0,"zero Vulkan core and synchronization validation errors");File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=stats.ValidationErrors,Probes=backend.GetProbeStats()},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));return 0;
    }
}
