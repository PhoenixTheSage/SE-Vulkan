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

partial class Program
{
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(bin,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};try{return Run(bin,args.Contains("--distance-fade-only"));}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    [MethodImpl(MethodImplOptions.NoInlining)]static unsafe int Run(string bin,bool distanceFadeOnly)
    {
        int checks=0;void Check(bool condition,string text){if(!condition)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        string output=Path.GetFullPath("TestResults/ProbeGpuTests");Directory.CreateDirectory(output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));
        backend.SetTextureAnisotropy(16);
        Check(Marshal.SizeOf<NativeProbeConfig>()==8096 && Marshal.SizeOf<NativeProbeStats>()==64,"probe ABI packing");
        using(var file=File.OpenRead(Path.Combine(bin,"../Content/Textures/Miscellaneous/ambient_brdf.dds"))){var lut=DdsImage.Read(file,true);Check(lut.Format==9 && !lut.Srgb && lut.Data.Length==lut.Width*lut.Height*4,"installed stock RG16F BRDF texture retains half-float channels");backend.UploadTextureMips(13,lut.Width,lut.Height,lut.Format,lut.Mips,false);}
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.BeginUploads();backend.UploadWorldMesh(1,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});backend.UploadTexture(10,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(11,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(12,1,1,0,new byte[]{255,0,0,0},false);backend.UploadWorldCube(14,1,1,0,Enumerable.Range(0,6).SelectMany(_=>new byte[]{0,0,100,255}).ToArray(),true);backend.EndUploads();backend.SetWorldSkybox(14);
        MatrixD projection=(MatrixD)Matrix.CreatePerspectiveFovRhInfiniteComplementary((float)Math.PI/2,1,.1f);
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;light.SunDiscIntensity=0;light.EnvSkyboxBrightness=1;
        var probes=new VulkanProbes{Resolution=32};probes.Settings.TimeOut=10;
        MyRenderSettings captureSettings=new MyRenderSettings();
        List<NativeEnvironmentDraw> environment=null;float captureTime=0,probeDelta=.1f;bool nativeForward=false;Vector3D probeCamera=Vector3D.Zero;
        ulong activeProbeMesh=1;Action disableIndexedProbe=()=>{};
        NativePassFrame passFrame=default;NativePassView[] passViews=Array.Empty<NativePassView>();
        byte[] Render(bool opaque=true,uint flags=512,bool batch=false,Vector3? tint=null,bool reflectionOnly=false,Action extra=null){backend.BeginPacketCollection();if(environment!=null)backend.QueueEnvironment(environment);if(opaque){if(batch)backend.AddRigidWorldPacket(0,activeProbeMesh,10,11,12,0,MatrixD.Identity,tint??Vector3.One,Vector3.Zero,1,0,flags,0,Vector3.Zero,3);else backend.AddWorldPacket(0,activeProbeMesh,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(-probeCamera),tint??Vector3.One,Vector3.Zero,1,0),flags:flags);}extra?.Invoke();probes.Configure(backend,probeCamera,light,MyPostprocessSettings.Default,MatrixD.Identity,13,probeDelta,captureTime,renderSettings:captureSettings);if(nativeForward){backend.PreparePassFrame(ref passFrame,passViews,passViews.Length);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);}var renderingLight=light;if(reflectionOnly){renderingLight.SunColorRaw=renderingLight.SunSpecularColorRaw=Vector3.Zero;renderingLight.AmbientDiffuseFactor=0;renderingLight.AmbientSpecularFactor=1;renderingLight.SkyboxBrightness=1;}backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,renderingLight,specularProbes:backend.ProbesAvailableForFrame,renderSettings:captureSettings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"probe frame begins");backend.RequestReadback();backend.EndFrame();return backend.Readback(64,64);}
        // Timeout is measured from capture start, without an extra minimum hold.
        void AdvanceCapture(){captureTime+=Math.Max(0,probes.Settings.TimeOut)+1;}
        float[] Floats(byte[] data)=>Enumerable.Range(0,data.Length/2).Select(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data,i*2))).ToArray();
        if(distanceFadeOnly){
            probes.Settings.TimeOut=0;captureSettings.User.DistanceFade=10000;
            // Use stock-facing geometry and explicit stock raster state in both controls.
            backend.UploadWorldMesh(2,new[]{V(-1,-1),V(1,-1),V(1,1),V(-1,1)},new uint[]{0,2,1,0,3,2});
            var source=new byte[64];foreach(int offset in new[]{0,20,40})BitConverter.GetBytes(1f).CopyTo(source,offset);
            byte[] CubeSource(bool skin){var cube=new VRage.MyCubeInstanceData{LocalMatrix=Matrix.Identity};if(skin){cube.EnableSkinning=true;cube.BoneRange=1;for(int i=0;i<9;i++)cube[i]=new Vector3UByte(127,127,127);}var bytes=new byte[64];for(int i=0;i<8;i++){var bone=cube.GetPackedBone(i);bytes[i*4]=bone.X;bytes[i*4+1]=bone.Y;bytes[i*4+2]=bone.Z;bytes[i*4+3]=bone.W;}var packed=cube.PackedOrthoMatrix;float[] values={packed.X,packed.Y,packed.Z,packed.W,0,0,0,0};for(int i=0;i<8;i++)BitConverter.GetBytes(values[i]).CopyTo(bytes,32+i*4);return bytes;}
            var cubeSource=CubeSource(false);var deformedSource=CubeSource(true);
            byte[] Capture(uint pipeline,float alpha=0,float scale=1){Render(false,extra:()=>backend.AddWorldPacket(pipeline,2,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[pipeline],MatrixD.CreateScale(scale),Vector3.One,Vector3.Zero,1,0,alphaY:alpha),WorldShaderVariants.BasePipeline(pipeline)%5==3?cubeSource:WorldShaderVariants.BasePipeline(pipeline)%5==4?deformedSource:source,1,512|0x08000000));return backend.ReadbackProbe(0,5,0,32);}
            Render(false);var emptyForward=backend.ReadbackProbe(0,5,0,32);var visibleForward=Capture(2);
            Check(visibleForward.Zip(emptyForward,(a,b)=>a!=b).Count(v=>v)>20,"forward distance-fade control contains real captured geometry");
            var inactiveFade=Capture(2562);File.WriteAllBytes(Path.Combine(output,"fade-before-or-after.rgba16f"),inactiveFade);
            Check(inactiveFade.SequenceEqual(visibleForward),"inactive DF forward shader preserves the visible generic probe capture");
            captureSettings.User.DistanceFade=1;Check(Capture(2562).SequenceEqual(emptyForward),"full distance fade removes actual forward probe geometry");
            foreach(float alpha in new[]{.23f,.53f,.83f}){
                captureSettings.User.DistanceFade=(float)Math.Sqrt(11)/(1+alpha/10);var implicitFade=Capture(2562);var explicitFade=Capture(2,alpha);
                Check(implicitFade.SequenceEqual(explicitFade),"forward probe distance fade matches analytic stock dither: "+alpha);
                Check(!implicitFade.SequenceEqual(emptyForward)&&!implicitFade.SequenceEqual(visibleForward),"intermediate forward fade has partial real geometry: "+alpha);
            }
            captureSettings.User.DistanceFade=10000;Check(Capture(2562).SequenceEqual(visibleForward),"forward fade threshold roundtrip restores exact probe pixels");
            foreach(uint type in new uint[]{3,4}){
                captureSettings.User.DistanceFade=1;var cubeNear=Capture(type);
                Check(!cubeNear.SequenceEqual(emptyForward),"forward cube control captures real geometry: "+type);
                Check(Capture(2560+type).SequenceEqual(cubeNear),"stock near cube preserves its overwritten dither alpha in forward: "+type);
                captureSettings.User.DistanceFade=900;var cubeFar=Capture(type,scale:350);
                Check(!cubeFar.SequenceEqual(emptyForward),"forward far cube control captures real geometry: "+type);
                Check(Capture(2560+type,scale:350).SequenceEqual(emptyForward),"far cube position scaling removes actual forward geometry: "+type);
                captureSettings.User.DistanceFade=10000;Check(Capture(2560+type,scale:350).SequenceEqual(cubeFar),"forward far cube fade roundtrip restores exact radiance: "+type);
            }
            if(Environment.GetEnvironmentVariable("SE_VULKAN_TEST_GPU_OPAQUE")=="1")IndexedProbeFadeChecks(backend,library,Path.GetFullPath(Path.Combine(bin,"../Content/Shaders")),output,(pipeline,alpha,scale)=>Capture(pipeline,alpha,scale),()=>Render(false),value=>captureSettings.User.DistanceFade=value,source,cubeSource,deformedSource,emptyForward,Check);
            Check(backend.GetStats().ValidationErrors==0,"forward fade has zero Vulkan validation errors");Console.WriteLine(checks+" forward distance-fade checks passed.");return 0;
        }
        Check(Marshal.SizeOf<NativeProbeScheduleStats>()==32,"probe stage statistics ABI packing");var firstReflection=Render(reflectionOnly:true);var s=backend.GetProbeStats();
        // Stock MyEnvironmentProbe explicitly allocates log2(resolution)
        // levels, ending at 2x2. PBR uses GetDimensions' level count for LOD.
        Check(backend.ReadbackProbe(4,0,4,32).Length==2*2*8,"stock generated probe chain retains its final 2x2 level");
        bool extraMipRejected=false;try{backend.ReadbackProbe(4,0,5,32);}catch(InvalidOperationException){extraMipRejected=true;}
        Check(extraMipRejected,"stock generated probe chain exposes no extra 1x1 roughness level");
        Check(s.Ready==1&&s.Faces==6&&s.GeometryDraws==6&&s.PrefilterPasses==48,"startup completes all capture and filtering faces before publication");
        // Installed MyEnvironmentProbe always allocates R11G11B10, even when
        // the main HDR target is high precision. Observe real original pixels.
        var storedSky=Floats(backend.ReadbackProbe(1,5,0,32));
        Check(storedSky[2]>.1f,"probe precision fixture contains nontrivial blue sky radiance");
        bool PackedFloatGrid(float value,int mantissa){if(value==0)return true;if(!float.IsFinite(value)||value<0)return false;double step=Math.Pow(2,Math.Max(-14,(int)Math.Floor(Math.Log2(value)))-mantissa);return Math.Abs(value/step-Math.Round(value/step))<1e-5;}
        Check(Enumerable.Range(0,storedSky.Length/4).All(i=>PackedFloatGrid(storedSky[i*4],6)&&PackedFloatGrid(storedSky[i*4+1],6)&&PackedFloatGrid(storedSky[i*4+2],5)&&storedSky[i*4+3]==1),"stock probe storage has R11G11B10 radiance precision and implicit alpha one");
        bool startupPublished=true;for(uint kind=0;kind<2;kind++)for(uint face=0;face<6;face++)for(uint mip=0;mip<5;mip++)startupPublished&=backend.ReadbackProbe(kind+2,face,mip,32).SequenceEqual(backend.ReadbackProbe(kind+4,face,mip,32));
        Check(startupPublished,"first complete stock probe publishes all filtered radiance directly without a fade from black");
        // Main lighting runs after that complete startup capture. With direct,
        // diffuse ambient and emission disabled, this surface must reflect blue.
        File.WriteAllBytes(Path.Combine(output,"startup-reflection.rgba8"),firstReflection);int firstCenter=(32*64+32)*4;
        Check(firstReflection[firstCenter+2]>firstReflection[firstCenter] && firstReflection[firstCenter+2]<firstReflection[2],"first scene frame receives the just-published specular probe (center "+firstReflection[firstCenter]+"/"+firstReflection[firstCenter+1]+"/"+firstReflection[firstCenter+2]+", sky blue "+firstReflection[2]+")");
        var beforeSteady=backend.GetProbeStats();Render();
        Check(backend.GetProbeStats().BlendPasses-beforeSteady.BlendPasses==5,"stock timed probes blend one alternating cube per frame, not both");
        var publishedBefore=backend.ReadbackProbe(4,5,3,32);
        backend.UploadWorldCube(19,1,1,0,Enumerable.Range(0,6).SelectMany(_=>new byte[]{0,255,0,255}).ToArray(),true);backend.SetWorldSkybox(19);
        var priorClose=backend.ReadbackProbe(4,5,0,32);var priorFar=backend.ReadbackProbe(5,5,0,32);
        AdvanceCapture();for(int stage=0;stage<18;stage++){Render();uint expectedStage=(uint)(stage+1+(stage>=5?1:0));Check(backend.GetProbeScheduleStats().Stage==expectedStage&&backend.GetProbeStats().Cycles==1,"timed refresh follows stock capture/finalization stage "+expectedStage);if(stage==5||stage==13||stage==17)Check(backend.ReadbackProbe(4,5,3,32).SequenceEqual(publishedBefore),"unprocessed final face retains its previous radiance during refresh frame "+stage);}
        Render();Check(backend.GetProbeScheduleStats().Stage==0&&backend.GetProbeStats().Cycles==2,"nineteenth frame completes the stock timed probe cycle");
        int changedCubes=(backend.ReadbackProbe(4,5,0,32).SequenceEqual(priorClose)?0:1)+(backend.ReadbackProbe(5,5,0,32).SequenceEqual(priorFar)?0:1);
        Check(changedCubes==1,"newly processed face publishes through only the selected alternating cube");backend.SetWorldSkybox(14);
        float[] close=Floats(backend.ReadbackProbe(0,5,0,32)),far=Floats(backend.ReadbackProbe(1,5,0,32));Check(close.All(float.IsFinite) && far.All(float.IsFinite),"original probe pixels are finite");Check(close.Where((v,i)=>i%4<3 && v>far[i]+.03f).Count()>10,"near probe contains actual scene geometry absent from far probe");
        var faceGeometry=new Dictionary<uint,int>();for(uint face=0;face<6;face++){var nearFace=Floats(backend.ReadbackProbe(0,face,0,32));var farFace=Floats(backend.ReadbackProbe(1,face,0,32));faceGeometry[face]=nearFace.Where((v,i)=>i%4<3&&Math.Abs(v-farFace[i])>.03f).Count();}Console.WriteLine("FACEDELTA "+JsonSerializer.Serialize(faceGeometry));
        Check(faceGeometry[5]>10&&Enumerable.Range(0,5).All(face=>faceGeometry[(uint)face]==0),"face view-projection captures forward geometry only in its correct cubemap face");
        foreach(uint kind in Enumerable.Range(2,4).Select(i=>(uint)i))foreach(uint mip in Enumerable.Range(0,5).Select(i=>(uint)i)){float[] pixels=Floats(backend.ReadbackProbe(kind,5,mip,32));Check(pixels.All(float.IsFinite) && pixels.Any(v=>v>0),"filtered/blended probe mip contains finite light kind="+kind+" mip="+mip);}
        float[] blurred=Floats(backend.ReadbackProbe(2,5,3,32));Check(blurred.Where((v,i)=>i%4<3&&v>.01f).Count()>10,"GGX rough mip retains scene illumination");
        var retainedClose=backend.ReadbackProbe(2,5,0,32);var retainedFar=backend.ReadbackProbe(3,5,0,32);bool retainedUntilProcessed=true;
        AdvanceCapture();for(int i=0;i<18;i++){Render(tint:new Vector3(0,1,0));retainedUntilProcessed&=backend.ReadbackProbe(2,5,0,32).SequenceEqual(retainedClose)&&backend.ReadbackProbe(3,5,0,32).SequenceEqual(retainedFar);}
        Check(retainedUntilProcessed,"refresh retains completed filtered faces and replaces leading mips only when their face is processed");
        Render(tint:new Vector3(0,1,0));Render(tint:new Vector3(0,1,0));var tintedProbe=backend.ReadbackProbe(0,5,0,32);
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
        // A refresh must follow a camera move smaller than DrawDistance. The
        // fixed world triangle is now left of the camera, not straight ahead.
        probeCamera=new Vector3D(10,0,0);Render();
        int GeometryPixels(uint face){var a=Floats(backend.ReadbackProbe(0,face,0,32));var b=Floats(backend.ReadbackProbe(1,face,0,32));return a.Where((v,i)=>i%4<3&&Math.Abs(v-b[i])>.03f).Count();}
        Check(GeometryPixels(0)>0 && GeometryPixels(5)==0,"camera movement refreshes the probe around the current camera within draw distance");
        probeCamera=Vector3D.Zero;Render();
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
            var retiredProbeActors=new[]{ga};disableIndexedProbe=()=>{retiredProbeActors[0].Flags=0;backend.UpdateGpuOpaqueActors(new uint[]{0},retiredProbeActors);};
            var gv=new NativeGpuOpaqueView[15];for(int f=0;f<6;f++){gv[9+f].Flags=1|4|32;for(int i=0;i<24;i++)gv[9+f].Planes[i]=passViews[f].Planes[i];}
            for(uint epoch=600;epoch<602;epoch++){var gf=new NativeGpuOpaqueFrame{Epoch=epoch,Coefficient=1,DistanceMult=1,Flags=3,SmoothCameraDistance=100,MaximumSeconds=1,MinimumSeconds=.25f};backend.QueueGpuOpaqueFrame(ref gf,gv);Render(false);foreach(var item in referenceFaces)Check(backend.ReadbackProbe(item.Key.Item1,item.Key.Item2,0,32).SequenceEqual(item.Value),"GPU-selected indexed stock forward matches captured radiance slot/kind/face="+epoch+"/"+item.Key.Item1+"/"+item.Key.Item2);}
            probes.Settings.TimeOut=10;AdvanceCapture();
            for(uint frame=0;frame<19;frame++){var gf=new NativeGpuOpaqueFrame{Epoch=602+frame,Coefficient=1,DistanceMult=1,Flags=3,SmoothCameraDistance=100,MaximumSeconds=1,MinimumSeconds=.25f};backend.QueueGpuOpaqueFrame(ref gf,gv);Render(false);}
            Check(backend.GetProbeScheduleStats().Stage==0,"indexed forward supports the stock nineteen-frame refresh and combined capture/finalization frame");
            foreach(var item in referenceFaces)Check(backend.ReadbackProbe(item.Key.Item1,item.Key.Item2,0,32).SequenceEqual(item.Value),"timed indexed forward retains reference radiance kind/face="+item.Key.Item1+"/"+item.Key.Item2);probes.Settings.TimeOut=0;
        }
        // Deliberately different authored levels make missing cube mips visible
        // through the actual stock probe sky shader, not a CPU upload mock.
        var cubeLevels=Enumerable.Range(0,6).Select(face=>Enumerable.Range(0,9).Select(mip=>Enumerable.Range(0,Math.Max(1,256>>mip)*Math.Max(1,256>>mip)).SelectMany(_=>mip==0?new byte[]{255,0,0,255}:new byte[]{0,255,0,255}).ToArray()).ToArray()).ToArray();
        backend.UploadWorldCube(18,256,256,0,cubeLevels.SelectMany(f=>f[0]).ToArray(),true);backend.SetWorldSkybox(18);Render(false);
        int Dominant(float[] values,int channel,int other)=>Enumerable.Range(0,values.Length/4).Count(i=>values[i*4+channel]>values[i*4+other]+.1f);
        Check(Dominant(Floats(backend.ReadbackProbe(1,5,0,32)),0,1)>900,"single-level cube control exposes red mip zero in the stock probe sky");
        backend.BeginUploads();backend.UploadWorldCubeMips(18,256,256,0,cubeLevels,true);backend.EndUploads();var visibleSky=Render(false);
        Check(Dominant(Floats(backend.ReadbackProbe(1,5,0,32)),1,0)>900,"authored cube mips supply green radiance through the actual stock probe sky shader");
        probes.Settings.TimeOut=0;backend.SetWorldSkybox(18,14);var indirectSky=Render(false);
        Check(Dominant(Floats(backend.ReadbackProbe(1,5,0,32)),2,0)>900,"stock probe sky uses its separate blue indirect cube instead of the visible red/green cube");
        Check(visibleSky.Zip(indirectSky,(a,b)=>Math.Abs(a-b)).Max()<=1,"indirect cube selection preserves the actual visible sky pixels");
        backend.SetWorldSkybox(18);Render(false);
        Check(Dominant(Floats(backend.ReadbackProbe(1,5,0,32)),1,0)>900,"legacy skybox setter restores visible-cube probe fallback");
        backend.SetWorldSkybox(18,14);backend.RemoveResource(4,14);Render(false);
        Check(Dominant(Floats(backend.ReadbackProbe(1,5,0,32)),1,0)>900,"removing the indirect cube clears its probe binding before the next capture");
        // At gloss zero, stock DefaultSampler clamps the BRDF LUT's left
        // texel. Altering only the right column must not alter PBR radiance.
        backend.UploadWorldCube(18,1,1,0,Enumerable.Repeat(new byte[]{255,255,255,255},6).SelectMany(x=>x).ToArray(),true);
        light.SunColorRaw=Vector3.Zero;light.SunSpecularColorRaw=Vector3.Zero;light.AmbientDiffuseFactor=0;light.AmbientSpecularFactor=1;
        probes.Settings.TimeOut=0; // Publish identical probe radiance immediately in both comparisons.
        backend.UploadTexture(11,1,1,0,new byte[]{128,128,255,0},false);
        byte[] Lut(bool edge){var data=new byte[16];for(int i=0;i<4;i++){ushort value=BitConverter.HalfToUInt16Bits((Half)(edge && i%2==1?.8f:.2f));data[i*4]=(byte)value;data[i*4+1]=(byte)(value>>8);}return data;}
        backend.UploadTexture(13,2,2,9,Lut(false),false);var clampedReference=Render();
        backend.UploadTexture(13,2,2,9,Lut(true),false);var clampedEdge=Render();
        Check(clampedReference.Zip(clampedEdge,(a,b)=>Math.Abs(a-b)).Max()<=1,"stock PBR BRDF lookup clamps its edge instead of wrapping to the opposite LUT column");
        light.SkipIBLevels=2;var beforeSkipped=backend.GetProbeStats();Render();var afterSkipped=backend.GetProbeStats();
        Check(afterSkipped.PrefilterPasses-beforeSkipped.PrefilterPasses==24,"SkipIBLevels=2 filters only the two remaining rough levels of each cube face");
        bool skippedEqual=true;for(uint kind=0;kind<2;kind++)for(uint face=0;face<6;face++)for(uint mip=0;mip<=2;mip++)skippedEqual&=backend.ReadbackProbe(kind,face,mip,32).SequenceEqual(backend.ReadbackProbe(kind+2,face,mip,32));
        Check(skippedEqual,"stock skipped leading levels are exact original-mip copies in both cubes and every face");
        bool roughFinite=true;for(uint kind=2;kind<4;kind++)for(uint mip=3;mip<5;mip++)roughFinite&=Floats(backend.ReadbackProbe(kind,5,mip,32)).All(float.IsFinite);
        Check(roughFinite,"nonzero-skip GGX rough levels retain finite radiance");
        light.SkipIBLevels=4;var beforeAllSkipped=backend.GetProbeStats();Render();
        Check(backend.GetProbeStats().PrefilterPasses==beforeAllSkipped.PrefilterPasses,"skipping through the final valid mip emits no GGX passes or zero-denominator gloss ramp");
        bool allSkippedEqual=true;for(uint kind=0;kind<2;kind++)for(uint face=0;face<6;face++)for(uint mip=0;mip<5;mip++)allSkippedEqual&=backend.ReadbackProbe(kind,face,mip,32).SequenceEqual(backend.ReadbackProbe(kind+2,face,mip,32));
        Check(allSkippedEqual,"maximum valid skip retains exact copies of every original cube mip");
        bool invalidSkipRejected=false;try{backend.SetProbes(backend.CurrentProbeConfig,5);}catch(InvalidOperationException){invalidSkipRejected=true;}
        Check(invalidSkipRejected,"out-of-range probe skip is rejected before mutating the valid capture configuration");
        light.SkipIBLevels=0;var beforeUnskipped=backend.GetProbeStats();Render();
        Check(backend.GetProbeStats().PrefilterPasses-beforeUnskipped.PrefilterPasses==48,"legacy zero-skip configuration restores the full stock prefilter chain");
        // Stock blending uses the measured delta, including a stopped clock.
        var stoppedClose=backend.ReadbackProbe(4,5,0,32);var stoppedFar=backend.ReadbackProbe(5,5,0,32);
        backend.UploadWorldCube(18,1,1,0,Enumerable.Repeat(new byte[]{0,0,255,255},6).SelectMany(x=>x).ToArray(),true);
        probes.Settings.TimeOut=10;probeDelta=0;AdvanceCapture();
        for(int i=0;i<19;i++)Render(false);
        Check(backend.CurrentProbeConfig.Blend==0,"zero frame delta produces zero stock probe blend strength");
        Check(backend.ReadbackProbe(4,5,0,32).SequenceEqual(stoppedClose)&&backend.ReadbackProbe(5,5,0,32).SequenceEqual(stoppedFar),"zero-delta refresh preserves published radiance in both cubes");
        Check(!backend.ReadbackProbe(3,5,0,32).SequenceEqual(stoppedFar),"zero-delta control completes distinct new radiance instead of skipping capture");
        var completedFiltered=backend.ReadbackProbe(3,5,0,32);captureTime+=11;Render(false);
        Check(backend.ReadbackProbe(3,5,0,32).SequenceEqual(completedFiltered),"next timed refresh retains completed filtered radiance on unprocessed faces instead of reseeding from lagging publication");
        probeDelta=.00025f;Render(false);
        Check(Math.Abs(backend.CurrentProbeConfig.Blend-.000025f)<1e-10f,"sub-millisecond frame delta retains stock probe blending rate");
        probeDelta=.1f;Render(false);Render(false);
        Check(!backend.ReadbackProbe(4,5,0,32).SequenceEqual(stoppedClose)&&!backend.ReadbackProbe(5,5,0,32).SequenceEqual(stoppedFar),"resuming the clock blends the new radiance into both alternating cubes");
        // Stock UpdateCullQuery advances its existing refresh after a camera
        // teleport; only resource initialization or TimeOut=0 captures all
        // faces immediately. A teleport must not republish a full-strength
        // cube while the render clock is stopped.
        var beforeTeleport=backend.GetProbeStats();
        var teleportClose=backend.ReadbackProbe(4,5,0,32);var teleportFar=backend.ReadbackProbe(5,5,0,32);
        var savedProbeCamera=probeCamera;probeCamera+=Vector3D.Right*(Math.Max(1,probes.Settings.DrawDistance)+100);
        probeDelta=0;Render(false);
        var afterTeleport=backend.GetProbeStats();
        Check(afterTeleport.Faces==beforeTeleport.Faces+1&&afterTeleport.Cycles==beforeTeleport.Cycles,"large camera move retains the timed probe refresh instead of forcing an immediate six-face restart");
        Check(backend.ReadbackProbe(4,5,0,32).SequenceEqual(teleportClose)&&backend.ReadbackProbe(5,5,0,32).SequenceEqual(teleportFar),"large camera move at zero delta preserves both published probe histories");
        probeCamera=savedProbeCamera;probeDelta=.1f;
        nativeForward=false;backend.ClearPassFrame();disableIndexedProbe();activeProbeMesh=2;
        backend.UploadWorldMesh(2,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});
        light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;
        captureTime+=11;probes.Settings.TimeOut=0;probeCamera=new Vector3D(.7,0,0);Render();var currentCameraFace=backend.ReadbackProbe(0,5,0,32);
        probeCamera=Vector3D.Zero;Render();var priorCameraFace=backend.ReadbackProbe(0,5,0,32);
        Check(!currentCameraFace.SequenceEqual(priorCameraFace),"moving-camera probe reference changes actual geometry coverage");
        probes.Settings.TimeOut=10;AdvanceCapture();for(int face=0;face<5;face++)Render();
        probeCamera=new Vector3D(.7,0,0);Render();
        // Face five gets its sky in the later postprocess step; compare only
        // after that step so cleared background is not a false mismatch.
        for(int frame=0;frame<5;frame++)Render(false);
        Check(currentCameraFace.SequenceEqual(backend.ReadbackProbe(0,5,0,32)),"timed probe face uses the current camera origin as stock AddProbe does");
        for(int frame=0;frame<8;frame++)Render(false);Render(false);
        var heldFaces=backend.GetProbeStats().Faces;Render(false);
        Check(backend.GetProbeStats().Faces==heldFaces,"timeout-transition fixture is actually holding a completed cycle");
        probes.Settings.TimeOut=0;Render(false);
        Check(backend.GetProbeStats().Faces==heldFaces+6,"changing timeout to zero immediately releases the previous positive-timeout hold");
        // Distinct axis colors expose view/inverse-view confusion that a
        // uniform cubemap cannot. Stock SkyboxColorReflected flips world Z;
        // the six capture directions therefore sample faces 1,0,2,3,5,4.
        byte[][] axisColors={new byte[]{255,0,0,255},new byte[]{0,255,0,255},new byte[]{0,0,255,255},new byte[]{255,255,0,255},new byte[]{255,0,255,255},new byte[]{0,255,255,255}};
        backend.UploadWorldCube(21,4,4,0,axisColors.SelectMany(c=>Enumerable.Range(0,16).SelectMany(_=>c)).ToArray(),true);backend.SetWorldSkybox(21);probes.Settings.TimeOut=0;probeCamera=Vector3D.Zero;light.EnvSkyboxBrightness=1;Render(false);
        int[] expectedSkyFace={1,0,2,3,5,4};
        for(uint face=0;face<6;face++){var pixels=Floats(backend.ReadbackProbe(1,face,0,32));int center=(16*32+16)*4;var expected=axisColors[expectedSkyFace[face]];Console.WriteLine("AXIS SKY face="+face+" rgb="+pixels[center]+","+pixels[center+1]+","+pixels[center+2]+" expectedSource="+expectedSkyFace[face]);Check(Enumerable.Range(0,3).All(c=>Math.Abs(pixels[center+c]-expected[c]/255f)<.05f),"stock reflected sky orientation matches capture face "+face);}
        // Exercise the actual main-light descriptor writer with distinct cubes.
        // Close captures the white triangle; far retains only its sky input.
        // A split-screen shader reads stock t11/t17 at that same direction.
        backend.UploadWorldCube(22,1,1,0,Enumerable.Range(0,6).SelectMany(_=>new byte[]{0,0,100,255}).ToArray(),true);backend.SetWorldSkybox(22);
        light.SunColorRaw=new Vector3(2);probes.Settings.TimeOut=0;Render();
        var closeBindingControl=Floats(backend.ReadbackProbe(4,5,0,32));var farBindingControl=Floats(backend.ReadbackProbe(5,5,0,32));
        int bindingCenter=(16*32+16)*4;
        Check(closeBindingControl[bindingCenter]>farBindingControl[bindingCenter]+.1f,"close/far descriptor control contains distinct published geometry and sky radiance");
        probes.Settings.TimeOut=10;
        string bindingSource=Path.Combine(output,"ProbeBinding.hlsl");File.WriteAllText(bindingSource,"TextureCube<float4> Close : register(t11); TextureCube<float4> Far : register(t17); SamplerState Linear : register(s2); float3 __pixel_shader(float4 p:SV_Position):SV_Target0 { return p.x<32?Close.SampleLevel(Linear,float3(0,0,-1),0).rgb:Far.SampleLevel(Linear,float3(0,0,-1),0).rgb; }");
        using(var bindingCompiler=new VulkanShaderCompiler(Path.GetFullPath("Assets/Tools/win-x64"),Path.Combine(output,"BindingCache"))){
            var shader=bindingCompiler.Compile(new VulkanShaderRequest{SourcePath=bindingSource,ContentDirectory=Path.Combine(bin,"../Content/Shaders"),IncludeDirectories=new(){output},Profile="ps_5_0",EntryPoint="__pixel_shader"});
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            object api=typeof(NativeBackend).GetProperty("WorldApi",flags).GetValue(backend);
            var init=(Delegate)api.GetType().GetField("InitLighting",flags).GetValue(api);
            var ctx=typeof(NativeBackend).GetField("context",flags).GetValue(backend);
            byte[] module=SpirvRuntimeModule.FromArtifact(VulkanShaderArtifact.FromCompilation(shader,"probe-binding"));
            Check((int)init.DynamicInvoke(ctx,1u,module,(ulong)module.Length)==0,"main-light descriptor control pipeline initializes");
            try{
                var sampled=Render(false);int left=(32*64+16)*4,right=(32*64+48)*4;
                Console.WriteLine("PROBE BINDING close="+sampled[left]+","+sampled[left+1]+","+sampled[left+2]+" far="+sampled[right]+","+sampled[right+1]+","+sampled[right+2]);
                Check(sampled[left]>sampled[right]+30,"main lighting binds stock t11 to close geometry and t17 to far sky");
            }finally{byte[] stock=SpirvRuntimeModule.FromArtifact(library.Load(WorldShaderVariants.Lighting[1]));init.DynamicInvoke(ctx,1u,stock,(ulong)stock.Length);}
        }
        // Exercise the actual transparent-decal descriptor set. A diagnostic
        // pixel shader writes the published cube colors directly into OIT;
        // zero-color glass supplies the normal two depth-layer transactions.
        string decalSource=Path.Combine(output,"DecalProbeBinding.hlsl");File.WriteAllText(decalSource,"TextureCube<float4> Close:register(t11); TextureCube<float4> Far:register(t17); SamplerState Linear:register(s2); struct Result{float4 color:SV_Target0;float coverage:SV_Target1;}; Result __pixel_shader(float4 p:SV_Position){Result r;r.color=float4(p.x<32?Close.SampleLevel(Linear,float3(0,0,-1),0).rgb:Far.SampleLevel(Linear,float3(0,0,-1),0).rgb,1);r.coverage=1;return r;}");
        using(var compiler=new VulkanShaderCompiler(Path.GetFullPath("Assets/Tools/win-x64"),Path.Combine(output,"DecalBindingCache"))){
            var compiled=compiler.Compile(new VulkanShaderRequest{SourcePath=decalSource,ContentDirectory=output,Profile="ps_5_0",EntryPoint="__pixel_shader"});
            var spec=TransparencyShaderVariants.Decals.Single(s=>s.Id==8);byte[] vertex=SpirvRuntimeModule.FromArtifact(library.Load(spec.Vertex)),pixel=SpirvRuntimeModule.FromArtifact(VulkanShaderArtifact.FromCompilation(compiled,"decal-probe-binding"));
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;object api=typeof(NativeBackend).GetProperty("DecalApi",flags).GetValue(backend);var init=(Delegate)api.GetType().GetField("Init",flags).GetValue(api);object ctx=typeof(NativeBackend).GetField("context",flags).GetValue(backend);
            Check((int)init.DynamicInvoke(ctx,8u,vertex,(ulong)vertex.Length,pixel,(ulong)pixel.Length)==0,"transparent-decal descriptor control pipeline initializes");
            try{
                MatrixD box=MatrixD.CreateScale(4,4,1)*MatrixD.CreateTranslation(0,0,-3);var constants=new byte[128];Buffer.BlockCopy(new[]{(float)box.M11,(float)box.M21,(float)box.M31,(float)box.M41,(float)box.M12,(float)box.M22,(float)box.M32,(float)box.M42,(float)box.M13,(float)box.M23,(float)box.M33,(float)box.M43},0,constants,0,48);Buffer.BlockCopy(new[]{1f},0,constants,48,4);Buffer.BlockCopy(NativeBackend.MatrixValues(MatrixD.Invert(box)),0,constants,64,64);
                Render(false,extra:()=>{backend.AddGlassPacket(0,1,10,11,WorldConstants.Object(backend.GlassLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),new byte[96]);backend.DecalPackets.Add(new NativeDecalDraw{Size=176,Role=8,Alpha=10,Color=10,Normal=11,Extensions=12,Constants=constants});});
                float[] hdr=backend.ReadbackLightingStage(3,64,64);int left=(32*64+16)*4,right=(32*64+48)*4;
                Console.WriteLine("DECAL PROBE BINDING close="+string.Join(",",hdr.Skip(left).Take(3))+" far="+string.Join(",",hdr.Skip(right).Take(3)));
                var decalCloseCube=Floats(backend.ReadbackProbe(4,5,0,32));var decalFarCube=Floats(backend.ReadbackProbe(5,5,0,32));
                float Center(float[] cube,int channel)=>(cube[(15*32+15)*4+channel]+cube[(15*32+16)*4+channel]+cube[(16*32+15)*4+channel]+cube[(16*32+16)*4+channel])*.25f;
                bool Matches(int at,float[] cube)=>Enumerable.Range(0,3).All(c=>Math.Abs(hdr[at+c]-Center(cube,c))<=1e-4+.02*Math.Abs(Center(cube,c)));
                Check(hdr[left]>hdr[right]+.1f && Matches(left,decalCloseCube) && Matches(right,decalFarCube),"transparent decals bind stock t11 close geometry and t17 far sky");
            }finally{byte[] stock=SpirvRuntimeModule.FromArtifact(library.Load(spec.Pixel));init.DynamicInvoke(ctx,8u,vertex,(ulong)vertex.Length,stock,(ulong)stock.Length);}
        }
        var stats=backend.GetStats();if(stats.ValidationErrors!=0){Console.WriteLine("VALIDATION ERRORS "+stats.ValidationErrors+" "+stats.Detail);}Check(stats.ValidationEnabled!=0 && stats.ValidationErrors==0,"zero Vulkan core and synchronization validation errors");File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=stats.ValidationErrors,Probes=backend.GetProbeStats()},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));return 0;
    }
}
