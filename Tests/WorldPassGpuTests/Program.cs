using System.Runtime.InteropServices;
using System.Runtime.Loader;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Scene;
using VRage.Import;
using VRage;
using VRageMath;
using VRageRender;

partial class Program
{
    [StructLayout(LayoutKind.Sequential,Pack=8)] struct BlobDraw{public uint Size,Pipeline,Count,Flags;public ulong Mesh,Color,Normal,Extensions,Alpha;public IntPtr Constants;public uint ConstantBytes,Reserved;public IntPtr Instances;public float X,Y,Z,Radius;}
    [StructLayout(LayoutKind.Sequential,Pack=8)] struct RecipeDraw{public ulong Recipe;public uint Count,Flags,Cascades,ObjectOffset,InstanceOffset,Reserved;public float X,Y,Z,Radius;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int RecipeRawFn(IntPtr ctx,byte[] frame,uint bytes,float[] projection,float[] inverse,[In]RecipeDraw[] draws,uint count,byte[] objects,uint objectBytes,byte[] instances,uint instanceBytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int AlignmentFn(IntPtr ctx,out uint alignment);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int BlobFn(IntPtr ctx,byte[] frame,uint bytes,float[] projection,float[] inverse,[In]BlobDraw[] draws,uint count,byte[] objects,uint objectBytes,byte[] instances,uint instanceBytes);
    [StructLayout(LayoutKind.Sequential,Pack=8)]struct RawModelPart{public ulong Recipe;public uint ObjectOffset,Reserved;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int RegisterModelRawFn(IntPtr context,[In]RawModelPart[] parts,uint count,byte[] objects,uint bytes,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ClearModelsRawFn(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int AppendModelsRawFn(IntPtr context,[In]NativeModelInstance[] instances,uint count,[In]uint[] order,uint legacyCount,[Out]uint[] remap);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int NgxConfigureFn(uint enabled,uint mode,uint preset,[MarshalAs(UnmanagedType.LPUTF8Str)]string? directory);
    static int Main(string[] args)
    {
        string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,name)=>{string path=Path.Combine(bin,name.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};return Run(bin,args.Contains("--light-capacity-only"),args.Contains("--normal-hdr"),args.Contains("--mesh-decals-only"),args.Contains("--buffer-history-only"),args.Contains("--distance-history-only"),args.Contains("--shadow-cache-only"));
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string bin,bool lightCapacityOnly,bool normalHdr,bool meshDecalsOnly,bool bufferHistoryOnly,bool distanceHistoryOnly,bool shadowCacheOnly)
    {
        int checks=0;void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        string output=Path.GetFullPath(normalHdr?"TestResults/WorldPassGpuTests-NormalHdr":"TestResults/WorldPassGpuTests");Directory.CreateDirectory(output);
        Check(Marshal.SizeOf<NativeWorldVertex>()==80 && Marshal.SizeOf<NativeWorkStats>()==112 && Marshal.SizeOf<NativePerformanceStats>()==84,"world ABI vertex/stat packing");
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));if(normalHdr)backend.SetHdrQuality(false);
        if(bufferHistoryOnly){BufferHistoryLifetimeChecks(backend,Check);Console.WriteLine(checks+" asynchronous buffer-history checks passed.");return 0;}
        if(distanceHistoryOnly){DistanceHistoryChecks(backend,Check);Console.WriteLine(checks+" distance-fade history checks passed.");return 0;}
        if(shadowCacheOnly){ShadowCacheMenuChecks(backend,Check);Console.WriteLine(checks+" shadow-cache menu checks passed.");return 0;}
        Check(backend.GetCascadeRecordStats().Enabled==(Environment.ProcessorCount>=4?1u:0u),"parallel recording default follows CPU concurrency when no override is set");
        backend.WorldRecipesEnabled=false; // Explicit legacy ABI reference; recipe cases enable it below.
        var frameSettings=new MyRenderSettings();frameSettings.User.DistanceFade=3000;var frameLight=MyEnvironmentLightData.Default;frameLight.AmbientForwardPass=.3f;
        var encodedState=WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,MatrixD.Identity,64,64,frameLight,renderSettings:frameSettings);
        var frameReader=new ReflectedConstantBuffer(backend.WorldFrameLayout,"Frame");
        var resolutionLibrary=NativeLibrary.Load(NativeBackend.LibraryPath);
        try
        {
            var configure=Marshal.GetDelegateForFunctionPointer<NgxConfigureFn>(NativeLibrary.GetExport(resolutionLibrary,"sevk_ngx_configure"));
            var planner=new VulkanResolution();var device=new MyRenderDeviceSettings{BackBufferWidth=1920,BackBufferHeight=1080};
            try
            {
                foreach(uint mode in new uint[]{1,4,2,0,3,1})
                {
                    Check(configure(1,mode,0,"resolution-test-only")==0,"configure resolution-only NGX request");
                    var size=planner.Update(backend,device,new MyRenderSettings(),true,16);
                    int[] widths={1280,1114,960,640,1920},heights={720,626,540,360,1080};
                    Check(size.X==widths[mode] && size.Y==heights[mode] && planner.Fraction==backend.RenderFraction,"managed planning sees current NGX mode before native EndFrame");
                    var bytes=WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,MatrixD.Identity,size.X,size.Y,frameLight,renderFraction:planner.Fraction);
                    int screen=frameReader.OffsetOf("frame_.Screen.resolution");
                    Check(BitConverter.ToSingle(bytes,screen)==size.X && BitConverter.ToSingle(bytes,screen+4)==size.Y && BitConverter.ToUInt32(bytes,frameReader.OffsetOf("frame_.Light.tiles_x"))==(size.X+15)/16,"frame screen dimensions and light tiles match resolved render target");
                    Check(BitConverter.ToSingle(bytes,frameReader.OffsetOf("frame_.Screen.drs"))==1,"exact-sized SSAO targets do not apply render fraction twice");
                }
            }
            finally { configure(0,0,0,null);backend.SetRenderFraction(1); }
            var restored=planner.Update(backend,device,new MyRenderSettings(),true,16);
            Check(restored.X==1920 && restored.Y==1080 && planner.Fraction==1,"managed plan restores full resolution immediately after NGX disable");
        }
        finally { NativeLibrary.Free(resolutionLibrary); }
        Check(BitConverter.ToSingle(encodedState,frameReader.OffsetOf("frame_.DistanceFade"))==3000,"frame carries stock configured distance-fade threshold rather than zero");
        Check(BitConverter.ToSingle(encodedState,frameReader.OffsetOf("frame_.Light.forwardPassAmbient"))==.3f,"main frame ABI retains gathered stock forward ambient");
        backend.SetParallelCascades(false); // Explicit serial reference for paired GPU checks.
        backend.WideRigidBatchesEnabled=false; // Explicit legacy baseline; wide cases below change it deliberately.
        Check(backend.GetStats().ValidationEnabled!=0,"Khronos validation layer is active");
        Check(backend.WorldLayouts.Count==620 && backend.WorldReady,"core, OLD cutouts, blended mesh decals, 246 distance-fade variants and voxel material/depth pipelines initialize");
        NativeWorldVertex Vertex(float x,float y)
        {
            uint n=VF_Packer.PackNormal(Vector3.Backward);Vector4 tangent=new(1,0,0,1);uint t=VF_Packer.PackTangentSign(ref tangent);
            return new NativeWorldVertex{Material=new NativeGBufferVertex{X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=(t&255)/255f,TangentY=((t>>8)&255)/255f,TangentZ=((t>>16)&255)/255f,TangentW=(t>>24)/255f,U=(x+1)/2,V=(1-y)/2},WeightX=1};
        }
        backend.BeginUploads();backend.UploadTexture(10,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(11,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(12,1,1,0,new byte[]{255,1,0,0},false);backend.UploadWorldMesh(1,new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)},new uint[]{0,1,2});backend.EndUploads();
        Check(backend.GetWorkStats().UploadBatches==1,"four geometry/material uploads use one asynchronous batch");
        MatrixD projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;light.SunDiscIntensity=0;
        int width=64,height=64;float testFraction=1;MatrixD testView=MatrixD.Identity;var lightingSettings=new MyRenderSettings();
        byte[] Render(uint pipeline=0,MatrixD? world=null,float[] bones=null,byte[] stream=null,uint count=1,uint flags=512,float lodAlpha=0)
        {
            backend.BeginPacketCollection();backend.AddWorldPacket(pipeline,1,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[pipeline],world??MatrixD.Identity,Vector3.One,Vector3.Zero,1,lodAlpha,bones),stream,count,flags);
            int rw=Math.Max(1,(int)Math.Round(width*testFraction,MidpointRounding.AwayFromZero)),rh=Math.Max(1,(int)Math.Round(height*testFraction,MidpointRounding.AwayFromZero));
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,testView,projection,rw,rh,light,renderSettings:lightingSettings,renderFraction:testFraction),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(testView*projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"world frame begins");backend.EndFrame();return backend.Readback(width,height);
        }
        int ChangedPixels(byte[] a,byte[] b)=>Enumerable.Range(0,a.Length/4).Count(i=>Math.Abs(a[i*4]-b[i*4])+Math.Abs(a[i*4+1]-b[i*4+1])+Math.Abs(a[i*4+2]-b[i*4+2])>12);
        if(meshDecalsOnly){MeshDecalChecks(backend,projection,light,new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)},Check);File.WriteAllText("TestResults/MeshDecalGpuTests/result.json",System.Text.Json.JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=backend.GetStats().ValidationErrors,Scope="80 shader/blend variants; GBuffer/depth, native recipes, indirect and supported prepared submission"}));Console.WriteLine(checks+" mesh-decal checks passed.");return 0;}
        NativeLight TileLight(float x,float y,float z,float range,float intensity=5)=>new(){PositionRange=new[]{x,y,z,range},ColorIntensity=new[]{.2f,1f,.3f,intensity},DirectionCone=new[]{0f,0f,-1f,.8f}};
        byte[] TileFrame(bool enabled,NativeLight[] source){backend.SetLightTiles(enabled);backend.SetLights(source);return Render();}
        Check(Marshal.SizeOf<NativeLightTileStats>()==64,"light tile stats ABI packing");
        var centralLight=new[]{TileLight(0,0,-2.7f,.7f)};
        var unlitTiles=TileFrame(false,Array.Empty<NativeLight>());var fullTiles=TileFrame(false,centralLight);
        Check(ChangedPixels(unlitTiles,fullTiles)>20,"tile fixture visibly lights central geometry");
        Check(TileFrame(true,centralLight).SequenceEqual(fullTiles),"GPU tile frusta/depth preserve exact point-light pixels");
        Check(TileFrame(true,centralLight).SequenceEqual(fullTiles),"GPU light tiles preserve exact pixels on second fenced slot");
        TileFrame(true,centralLight);var tileStats=backend.GetLightTileStats();
        Check(tileStats.Enabled==1 && tileStats.Dispatches==1 && tileStats.HaveResult==1 && tileStats.Assignments<tileStats.FullAssignments && tileStats.CompletedFrame<tileStats.Frame,"completed GPU tile counters prove removed assignments without a GPU wait");
        foreach(var source in new[]{new[]{TileLight(.7f,.7f,-2.7f,1)},new[]{TileLight(-.7f,-.7f,-2.7f,1)},new[]{TileLight(0,0,0,6)},new[]{TileLight(0,0,3,.2f)},new[]{TileLight(20,0,-3,.3f)}}){var reference=TileFrame(false,source);Check(TileFrame(true,source).SequenceEqual(reference),"edge/near-camera/behind/offscreen light selection is byte-identical");}
        var manyLights=Enumerable.Range(0,64).Select(i=>TileLight((i%8-3.5f)*.14f,(i/8-3.5f)*.1f,-2.7f,.8f,.05f*(i+1))).ToArray();
        var fullMany=TileFrame(false,manyLights);Check(TileFrame(true,manyLights).SequenceEqual(fullMany),"64 overlapping lights preserve deterministic accumulation order");
        Check(TileFrame(true,Array.Empty<NativeLight>()).SequenceEqual(unlitTiles),"empty GPU lists preserve unlit pixels after populated lists");
        backend.Resize(65,49,0);width=65;height=49;var oddReference=TileFrame(false,manyLights);Check(TileFrame(true,manyLights).SequenceEqual(oddReference),"partial edge tile groups preserve exact resized output");
        backend.Resize(64,64,0);width=height=64;
        var centeredProjection=projection;projection.M31=.17;projection.M32=-.13;testView=MatrixD.CreateRotationY(.2);
        var shiftedReference=TileFrame(false,manyLights);Check(TileFrame(true,manyLights).SequenceEqual(shiftedReference),"off-center projection and rotated view preserve exact tiled lighting");
        projection=centeredProjection;testView=MatrixD.Identity;
        testFraction=.7f;backend.SetRenderFraction(testFraction);var fractionReference=TileFrame(false,manyLights);
        Check(TileFrame(true,manyLights).SequenceEqual(fractionReference) && backend.GetLightTileStats().Tiles==9,"fractional render extent uses correct partial GPU tile layout and exact output");
        testFraction=1;backend.SetRenderFraction(1);

        backend.SetLightTiles(false);backend.SetLights(Array.Empty<NativeLight>());
        Check(backend.GetStats().ValidationErrors==0,"GPU light tiles have zero core and synchronization validation errors");
        var savedLight=light;light.SunColorRaw=light.SunSpecularColorRaw=Vector3.Zero;light.AmbientDiffuseFactor=light.AmbientSpecularFactor=0;
        TileFrame(false,new[]{TileLight(0,0,-2,8,1)});var referenceDenseHdr=backend.ReadbackLightingStage(3,64,64);
        foreach(var pair in new[]{(MyRenderQualityEnum.LOW,256),(MyRenderQualityEnum.NORMAL,512),(MyRenderQualityEnum.HIGH,2048),(MyRenderQualityEnum.EXTREME,4096)}){
            lightingSettings.User.LightsQuality=pair.Item1;
            var dense=Enumerable.Range(0,pair.Item2).Select(_=>TileLight(0,0,-2,8,1)).ToArray();
            var conservative=TileFrame(false,dense);var denseHdr=backend.ReadbackLightingStage(3,64,64);
            Console.WriteLine("DENSE "+pair.Item2+" maxNormalizedAbs="+denseHdr.Where((v,i)=>i%4<3).Zip(referenceDenseHdr.Where((v,i)=>i%4<3),(a,b)=>Math.Abs(a/pair.Item2-b)).Max()+" maxReference="+referenceDenseHdr.Max());
            var rawDense=new byte[denseHdr.Length*4];Buffer.BlockCopy(denseHdr,0,rawDense,0,rawDense.Length);File.WriteAllBytes(Path.Combine(output,"dense-"+pair.Item2+".rgba32f"),rawDense);
            var rawReference=new byte[referenceDenseHdr.Length*4];Buffer.BlockCopy(referenceDenseHdr,0,rawReference,0,rawReference.Length);File.WriteAllBytes(Path.Combine(output,"dense-reference.rgba32f"),rawReference);
            Check(denseHdr.Where((v,i)=>i%4<3).All(float.IsFinite)&&denseHdr.Where((v,i)=>i%4<3).Zip(referenceDenseHdr.Where((v,i)=>i%4<3),(a,b)=>Math.Abs(a/pair.Item2-b)).Max()<.002,"all "+pair.Item2+" point lights retain normalized per-light reference radiance at stock quality "+pair.Item1);
            Check(TileFrame(true,dense).SequenceEqual(conservative),"dense stock quality "+pair.Item1+" GPU/conservative tiles preserve exact output");
            Check(backend.ReadbackLightingStage(3,64,64).SequenceEqual(denseHdr),"dense stock quality "+pair.Item1+" GPU/conservative tiles preserve exact HDR values");
            TileFrame(true,dense);TileFrame(true,dense);Check(backend.GetLightTileStats().Points==pair.Item2&&backend.GetLightTileStats().Assignments>(ulong)pair.Item2,"completed dense tile lists retain the complete "+pair.Item2+"-light set");
        }
        var mixedDense=Enumerable.Range(0,32).Select(_=>{var value=TileLight(0,0,-2,8,.01f);value.Flags=1;return value;}).Concat(Enumerable.Range(0,4096).Select(_=>TileLight(0,0,-2,8,1f/4096))).ToArray();
        TileFrame(true,mixedDense);TileFrame(true,mixedDense);TileFrame(true,mixedDense);Check(backend.GetLightTileStats().Points==4096,"32 independent spotlight entries do not displace any of 4096 point lights");
        backend.SetLightTiles(false);backend.SetLights(Array.Empty<NativeLight>());light=savedLight;lightingSettings.User.LightsQuality=MyRenderQualityEnum.LOW;
        Check(backend.GetStats().ValidationErrors==0,"dense mixed local lights have zero core and synchronization validation errors");
        if(lightCapacityOnly){File.WriteAllText(Path.Combine(output,"light-capacity-result.json"),System.Text.Json.JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=backend.GetStats().ValidationErrors}));Console.WriteLine(checks+" local-light capacity checks passed.");return 0;}
        backend.ObjectRetentionEnabled=false;
        byte[] keptA=WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0);
        byte[] keptB=WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(.5,0,0),Vector3.One,Vector3.Zero,1,0);
        byte[] RetainedFrame(bool enabled,bool reverse=false){
            backend.ObjectRetentionEnabled=enabled;backend.BeginPacketCollection();
            foreach(var constants in reverse?new[]{keptB,keptA}:new[]{keptA,keptB})backend.AddWorldPacket(0,1,10,11,12,0,constants,flags:512,recycleConstants:false);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"retained object frame begins");backend.EndFrame();return backend.Readback(width,height);
        }
        var retainedReference=RetainedFrame(false);
        Check(Marshal.SizeOf<NativeObjectRecordStats>()==48,"retained object stats ABI packing");
        Check(RetainedFrame(true).SequenceEqual(retainedReference),"retained object pixels match legacy on first slot");
        Check(RetainedFrame(true).SequenceEqual(retainedReference),"retained object pixels match legacy on second slot");
        Check(RetainedFrame(true,true).SequenceEqual(retainedReference) && backend.GetObjectRecordStats().UploadedBytes==0,"unchanged reordered records require no object upload after both slots initialize");
        var movedConstants=WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(-.7,0,0),Vector3.One,Vector3.Zero,1,0);
        Array.Copy(movedConstants,keptA,keptA.Length); // Same identity, visibly changed transform.
        RetainedFrame(true);Check(backend.GetObjectRecordStats().Changed==1 && backend.GetObjectRecordStats().UploadedBytes==(ulong)keptA.Length,"changed retained constants revise and upload only changed record");
        var changedReference=RetainedFrame(false);
        Check(!changedReference.SequenceEqual(retainedReference),"changed retained transform has visible consequences");
        Check(RetainedFrame(true).SequenceEqual(changedReference),"switching legacy back to retained invalidates slot versions");
        Check(RetainedFrame(true).SequenceEqual(changedReference),"changed retained pixels remain exact on other slot");
        backend.ObjectRetentionEnabled=false;
        var image=Render();int center=(32*64+32)*4;Console.WriteLine("CENTER="+string.Join(',',image.Skip(center).Take(4)));
        Check(image[center]>40 && image[center+3]==255,"stock GBuffer/directional light/HDR composition writes presented pixels");
        Check(backend.GetStats().ValidationErrors==0,"initial world frame has zero Vulkan validation errors");
        void QueueValidationPackets(ulong alpha=10)
        {
            backend.BeginPacketCollection();for(int i=0;i<3;++i)backend.AddWorldPacket(0,1,10,11,12,alpha,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0,null));
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
        }
        backend.TextureValidationCacheEnabled=true;QueueValidationPackets();var reusedValidation=backend.GetQueueValidationStats();
        Check(Marshal.SizeOf<NativeQueueValidationStats>()==32 && reusedValidation.Enabled==1 && reusedValidation.Draws==3 && reusedValidation.Hits==2 && reusedValidation.TextureChecks==4 && reusedValidation.MeshChecks==3,"one queue reuses only texture-tuple validation and still checks every mesh");
        backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();byte[] reusedPixels=backend.Readback(64,64);
        backend.TextureValidationCacheEnabled=false;QueueValidationPackets();var legacyValidation=backend.GetQueueValidationStats();
        Check(legacyValidation.Enabled==0 && legacyValidation.Hits==0 && legacyValidation.TextureChecks==12,"legacy queue checks every texture role");
        backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();Check(reusedPixels.SequenceEqual(backend.Readback(64,64)),"cached and legacy native validation preserve exact GPU pixels");
        backend.TextureValidationCacheEnabled=true;backend.UploadTexture(19,1,1,0,new byte[]{255,255,255,255},false);QueueValidationPackets(19);backend.RemoveResource(0,19);
        bool staleValidationRejected=false;try{QueueValidationPackets(19);}catch(InvalidOperationException){staleValidationRejected=true;}
        Check(staleValidationRejected,"removing a texture between queue calls cannot reuse old validation");backend.TextureValidationCacheEnabled=false;
        int Changed(byte[] a,byte[] b){int n=0;for(int i=0;i<a.Length;i+=4)if(Math.Abs(a[i]-b[i])+Math.Abs(a[i+1]-b[i+1])+Math.Abs(a[i+2]-b[i+2])>12)n++;return n;}
        float[] Bones(MatrixD first){var values=new float[960];for(int i=0;i<60;++i)Array.Copy(NativeBackend.MatrixValues(i==0?first:MatrixD.Identity),0,values,i*16,16);return values;}
        var sparseVertices=new NativeWorldVertex[16];uint[] sparseIndices={2,9,6};
        sparseVertices[2]=Vertex(-1,-1);sparseVertices[9]=Vertex(1,-1);sparseVertices[6]=Vertex(0,1);
        sparseVertices[9].BoneY=37;sparseVertices[9].WeightY=.4f;
        WorldMeshGeometry.Compact(sparseVertices,sparseIndices,out var compactVertices,out var compactIndices);
        Check(compactVertices.Length==3 && compactIndices.Length==3 && sparseIndices.SequenceEqual(new uint[]{2,9,6}),"world part remap removes only unreferenced vertices and preserves CPU index ranges");
        for(int i=0;i<sparseIndices.Length;i++)
        {
            var sourceBytes=MemoryMarshal.AsBytes(new[]{sparseVertices[sparseIndices[i]]}.AsSpan()).ToArray();
            var compactBytes=MemoryMarshal.AsBytes(new[]{compactVertices[compactIndices[i]]}.AsSpan()).ToArray();
            Check(sourceBytes.SequenceEqual(compactBytes),"compaction preserves complete vertex attributes and bone data at index "+i);
        }
        backend.UploadWorldMesh(1,sparseVertices,sparseIndices);var expandedPixels=Render();var expandedSkin=Render(1,bones:Bones(MatrixD.Identity));
        backend.UploadWorldMesh(1,compactVertices,compactIndices);var compactPixels=Render();var compactSkin=Render(1,bones:Bones(MatrixD.Identity));
        Check(expandedPixels.SequenceEqual(compactPixels) && expandedSkin.SequenceEqual(compactSkin),"expanded and compacted rigid/skinned geometry produce identical GPU pixels");
        backend.UploadWorldMesh(1,new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)},new uint[]{0,1,2});
        ulong uploaded=backend.GetStats().UploadedBytes;byte[] skinned=Render(1,bones:Bones(MatrixD.CreateTranslation(.8,0,0)));
        Check(Changed(image,skinned)>90,"stock vertex skinning moves GPU pixels using the bone palette");
        Check(backend.GetStats().UploadedBytes==uploaded,"animated skinning does not reupload immutable geometry");
        byte[] Instance(Matrix m)
        {
            var bytes=new byte[64];Buffer.BlockCopy(new[]{m.M11,m.M21,m.M31,m.M41,m.M12,m.M22,m.M32,m.M42,m.M13,m.M23,m.M33,m.M43,0f,0f,0f,0f},0,bytes,0,64);return bytes;
        }
        byte[] IndirectPair(bool indirect,bool unequal=false,bool cascades=false,bool omitSecond=false,bool mixedRaster=false)
        {
            backend.SetWorldIndirect(indirect);backend.BeginPacketCollection();
            byte[] first=Instance(Matrix.CreateTranslation(-.8f,0,0)).Concat(Instance(Matrix.CreateTranslation(0,.6f,0))).ToArray();
            byte[] second=Instance(Matrix.CreateTranslation(.8f,0,0));
            for(int i=0;i<(omitSecond?1:2);++i)backend.AddWorldPacket(2,1,10,11,12,0,
                WorldConstants.Object(backend.WorldLayouts[2],unequal&&i==1?MatrixD.CreateTranslation(0,.4,0):MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),
                i==0?first:second,i==0?2u:1u,(cascades?0x1000000u:512u)|(mixedRaster?(i==0?NativeBackend.StockBackCull:NativeBackend.StockNoCull):0u));
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(64,64);
        }
        byte[] directPair=IndirectPair(false);uint directCalls=backend.GetPerformanceStats().MainCommands;
        byte[] indirectPair=IndirectPair(true);var indirectStats=backend.GetIndirectStats();
        Check(indirectStats.Supported==1 && indirectStats.Enabled==1 && indirectStats.MainGroups==1 && indirectStats.GroupedDraws==2,"actual feature-enabled native multi-draw group executes");
        Check(directCalls==2 && backend.GetPerformanceStats().MainCommands==1 && backend.GetPerformanceStats().SubmittedDraws==2,"one indirect API command retains two logical packets");
        Check(directPair.SequenceEqual(indirectPair),"direct/indirect distinct instance offsets and counts produce byte-identical pixels");
        Check(IndirectPair(true).SequenceEqual(directPair) && backend.GetIndirectStats().MainGroups==1,"indirect commands preserve pixels on the second fenced frame slot");
        byte[] unequalDirect=IndirectPair(false,true),unequalIndirect=IndirectPair(true,true);
        Check(unequalDirect.SequenceEqual(unequalIndirect) && backend.GetIndirectStats().MainGroups==0,"unequal object constants retain direct draws and exact pixels");
        byte[] rasterDirect=IndirectPair(false,mixedRaster:true),rasterIndirect=IndirectPair(true,mixedRaster:true);
        Check(Changed(directPair,rasterDirect)>25,"opposite stock raster flags produce different visible instance coverage");
        Check(rasterDirect.SequenceEqual(rasterIndirect) && backend.GetIndirectStats().MainGroups==0,"indirect preparation keeps unequal raster states in separate exact draws");
        MatrixD indirectShadow=MatrixD.CreateLookAt(new Vector3D(0,0,2),new Vector3D(0,0,-3),Vector3D.Up)*MatrixD.CreateOrthographic(12,12,.1,20);
        backend.SetShadowCascades(Enumerable.Range(0,4).SelectMany(_=>NativeBackend.MatrixValues(indirectShadow)).ToArray(),new[]{5f,10f,20f,40f});
        byte[] shadowDirect=IndirectPair(false,cascades:true);uint shadowCalls=backend.GetPerformanceStats().ShadowCommands;
        byte[] shadowIndirect=IndirectPair(true,cascades:true);indirectStats=backend.GetIndirectStats();
        Check(shadowCalls==8 && backend.GetPerformanceStats().ShadowCommands==4 && indirectStats.ShadowGroups==4,"four selected cascades use one indirect group each");
        Check(shadowDirect.SequenceEqual(shadowIndirect) && backend.GetStats().ValidationErrors==0,"indirect main/cascades preserve exact pixels with zero synchronization validation errors");
        backend.SetParallelCascades(true);
        Check(Marshal.SizeOf<NativeCascadeRecordStats>()==32,"parallel cascade stats ABI packing");
        Check(IndirectPair(false,cascades:true).SequenceEqual(shadowDirect),"parallel secondary direct cascades preserve serial pixels");
        var cascadeJobs=backend.GetCascadeRecordStats();
        Check(cascadeJobs.Enabled==1 && cascadeJobs.Tasks==4 && cascadeJobs.Commands==8,"four frame-owned cascade jobs record eight direct commands");
        Check(IndirectPair(false,cascades:true).SequenceEqual(shadowDirect) && backend.GetCascadeRecordStats().Tasks==4,"parallel recording preserves pixels on other fenced slot");
        Check(IndirectPair(true,cascades:true).SequenceEqual(shadowIndirect) && backend.GetCascadeRecordStats().Commands==4,"parallel indirect cascades retain exact selected packets and grouped commands");
        var movedSerial=IndirectPair(false,unequal:true,cascades:true);
        backend.SetParallelCascades(false);var movedReference=IndirectPair(false,unequal:true,cascades:true);
        Check(movedSerial.SequenceEqual(movedReference),"changed caster transforms remain exact when recording policy switches");
        var removedReference=IndirectPair(false,cascades:true,omitSecond:true);backend.SetParallelCascades(true);
        Check(IndirectPair(false,cascades:true,omitSecond:true).SequenceEqual(removedReference) && backend.GetCascadeRecordStats().Commands==4,"removed caster membership stays exact in parallel selections");backend.SetParallelCascades(false);
        Check(backend.GetStats().ValidationErrors==0,"parallel cascade workers have zero core and synchronization validation errors");
        backend.SetShadowCascades(Array.Empty<float>(),Array.Empty<float>());backend.SetWorldIndirect(false);
        var arenaTriangle=new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)};
        var arenaQuad=new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(1,1),Vertex(-1,1)};
        void UploadArenaPair(bool arena){backend.SetGeometryArena(arena);backend.BeginUploads();backend.UploadWorldMesh(101,arenaTriangle,new uint[]{0,1,2});backend.UploadWorldMesh(102,arenaQuad,new uint[]{0,1,2,0,2,3});backend.EndUploads();}
        byte[] RenderArenaPair(bool indirect){backend.SetWorldIndirect(indirect);backend.BeginPacketCollection();for(int i=0;i<2;++i)backend.AddWorldPacket(2,(ulong)(101+i),10,11,12,0,WorldConstants.Object(backend.WorldLayouts[2],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),Instance(Matrix.CreateTranslation(i==0?-.8f:.8f,0,0)),1,0x1000000u);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(64,64);}
        UploadArenaPair(false);byte[] separateGeometry=RenderArenaPair(false);
        UploadArenaPair(true);byte[] sharedGeometry=RenderArenaPair(false);var arenaStats=backend.GetGeometryArenaStats();
        Check(arenaStats.Enabled==1 && arenaStats.VertexPages==1 && arenaStats.IndexPages==1 && arenaStats.VertexLive==560 && arenaStats.IndexLive==36,"distinct world meshes share stride-aligned vertex/index pages");
        Check(separateGeometry.SequenceEqual(sharedGeometry),"nonzero shared geometry offsets preserve standalone direct pixels");
        byte[] sharedIndirect=RenderArenaPair(true);
        Check(sharedGeometry.SequenceEqual(sharedIndirect) && backend.GetIndirectStats().MainGroups==1 && backend.GetPerformanceStats().MainCommands==1,"different mesh counts/base offsets batch into one byte-identical indirect group");
        Check(RenderArenaPair(false).SequenceEqual(sharedGeometry),"direct singleton binds recover geometry offsets after indirect page-base binds");
        backend.SetShadowCascades(Enumerable.Range(0,4).SelectMany(_=>NativeBackend.MatrixValues(indirectShadow)).ToArray(),new[]{5f,10f,20f,40f});
        var arenaShadowReference=RenderArenaPair(false);backend.SetParallelCascades(true);
        Check(RenderArenaPair(true).SequenceEqual(arenaShadowReference) && backend.GetCascadeRecordStats().Tasks==4 && backend.GetCascadeRecordStats().Commands==4,"shared geometry offsets and cross-mesh indirect groups retain exact parallel cascade pixels");
        backend.SetParallelCascades(false);backend.SetShadowCascades(Array.Empty<float>(),Array.Empty<float>());
        backend.BeginPacketCollection();backend.BeginUploads();backend.UploadWorldMesh(103,arenaTriangle,new uint[]{0,1,2});backend.RemoveResource(3,103);backend.UploadWorldMesh(103,arenaTriangle,new uint[]{0,1,2});backend.EndUploads();
        RenderArenaPair(false);backend.RemoveResource(3,101);backend.RemoveResource(3,102);backend.RemoveResource(3,103);Render();
        arenaStats=backend.GetGeometryArenaStats();
        Check(arenaStats.VertexLive==0 && arenaStats.IndexLive==0 && arenaStats.VertexPages==0 && arenaStats.IndexPages==0,"completed replacement/removal retires allocations and releases empty GPU pages");
        Check(backend.GetStats().ValidationErrors==0,"shared geometry upload/replacement/removal has zero synchronization validation errors");
        backend.SetGeometryArena(false);backend.SetWorldIndirect(false);
        var stream=Instance(Matrix.CreateTranslation(-.8f,0,0)).Concat(Instance(Matrix.CreateTranslation(.8f,0,0))).ToArray();var before=backend.GetWorkStats();
        byte[] instanced=Render(2,stream:stream,count:2);var after=backend.GetWorkStats();
        Check(after.WorldDraws-before.WorldDraws==1 && after.WorldInstances-before.WorldInstances==2 && Changed(image,instanced)>100,"two stock generic instances use one indexed draw");
        foreach(uint variant in new uint[]{0,5,10,15,2,7,12,17}){
            bool generic=variant%5==2;backend.RetainedSubmissionEnabled=false;var reference=Render(variant,world:MatrixD.CreateTranslation(.2,.1,0),stream:generic?stream:null,count:generic?2u:1u);
            backend.RetainedSubmissionEnabled=true;
            Check(Render(variant,world:MatrixD.CreateTranslation(.2,.1,0),stream:generic?stream:null,count:generic?2u:1u).SequenceEqual(reference),$"owned world blob variant {variant} matches legacy pixels on first slot");
            Check(Render(variant,world:MatrixD.CreateTranslation(.2,.1,0),stream:generic?stream:null,count:generic?2u:1u).SequenceEqual(reference),$"owned world blob variant {variant} matches legacy pixels on second slot");
        }
        backend.RetainedSubmissionEnabled=false;
        byte[] RenderSharedPair(bool share)
        {
            backend.BeginPacketCollection();
            for(int i=0;i<2;++i)backend.AddWorldPacket(2,1,10,11,12,0,
                WorldConstants.Object(backend.WorldLayouts[2],MatrixD.CreateTranslation(0,i*.6,0),Vector3.One,Vector3.Zero,1,0),
                share?stream:(byte[])stream.Clone(),2,512);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            // Queue must retain its own snapshot after the source is modified/unpinned.
            var saved=(byte[])stream.Clone();Array.Clear(stream);
            backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();
            Buffer.BlockCopy(saved,0,stream,0,stream.Length);return backend.Readback(width,height);
        }
        byte[] independentStreams=RenderSharedPair(false);before=backend.GetWorkStats();
        byte[] sharedStreams=RenderSharedPair(true);after=backend.GetWorkStats();
        Check(Changed(independentStreams,sharedStreams)==0 && after.WorldDraws-before.WorldDraws==2 && after.WorldInstances-before.WorldInstances==4,
            "shared instance stream preserves distinct object constants, draw counts and owned GPU bytes");
        backend.RetainedSubmissionEnabled=true;
        Check(RenderSharedPair(true).SequenceEqual(sharedStreams),"flat blob owns shared instance bytes after caller overwrites the source");
        backend.RetainedSubmissionEnabled=false;
        NativeBackend.PreparedActor Prepared(uint variant,Vector3 tint,Vector3 hsv)=>new(new[]{new NativeBackend.PreparedPart(variant,1,10,11,12,0,tint,hsv,1,0,backend.WorldLayouts[variant])});
        var nativeContext=(IntPtr)typeof(NativeBackend).GetField("context",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(backend)!;
        IntPtr Symbol(string name)=>(IntPtr)typeof(NativeBackend).GetMethod("GetSymbol",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,new object[]{name})!;
        var alignmentFn=Marshal.GetDelegateForFunctionPointer<AlignmentFn>(Symbol("sevk_world_object_alignment"));Check(alignmentFn(nativeContext,out uint alignment)==0,"blob alignment is supplied by the initialized device");
        var blobFn=Marshal.GetDelegateForFunctionPointer<BlobFn>(Symbol("sevk_queue_world_blob"));
        byte[] blobObject=WorldConstants.Object(backend.WorldLayouts[2],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),objectBlob=new byte[(blobObject.Length+alignment-1)/alignment*alignment];blobObject.CopyTo(objectBlob,0);
        int BlobContract(long objectOffset,long instanceOffset,ulong mesh=1){var draw=new BlobDraw{Size=96,Pipeline=2,Count=2,Flags=512,Mesh=mesh,Color=10,Normal=11,Extensions=12,Constants=new IntPtr(objectOffset),ConstantBytes=(uint)blobObject.Length,Instances=new IntPtr(instanceOffset),Radius=-1};return blobFn(nativeContext,WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),1280,NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)),new[]{draw},1,objectBlob,(uint)objectBlob.Length,stream,(uint)stream.Length);}
        Check(BlobContract(2,1)!=0,"blob bridge rejects an unaligned constant offset");
        Check(BlobContract(objectBlob.Length+1,1)!=0,"blob bridge rejects a constant slice outside its owner");
        Check(BlobContract(1,2)!=0 && BlobContract(1,129)!=0,"blob bridge rejects unaligned and out-of-bounds instance slices");
        Check(BlobContract(1,1,999999)!=0,"blob bridge retains geometry validation");
        Check(BlobContract(1,1)==0,"valid blob remains accepted after rejected packets");
        byte[] PreparedFrame(NativeBackend.PreparedActor actor,MatrixD matrix,uint flags=512,byte[] data=null,uint count=1,Vector3 origin=default,float lodAlpha=0){
            backend.BeginPacketCollection();backend.SubmitPreparedActor(actor,matrix,data,count,flags,0,origin,3,lodAlpha);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(width,height);
        }
        var retainedActor=Prepared(0,Vector3.One,Vector3.Zero);var movedPose=MatrixD.CreateRotationZ(.25)*MatrixD.CreateTranslation(.4,.2,0);
        var preparedReference=Render(0,world:movedPose);backend.RetainedSubmissionEnabled=true;
        Check(Changed(preparedReference,PreparedFrame(retainedActor,movedPose))<4,"prepared actor preserves a nonidentity pose through generic batching");
        backend.RigidBatchingEnabled=false;
        Check(PreparedFrame(retainedActor,movedPose).SequenceEqual(preparedReference),"prepared actor keeps rigid constants current when batching toggles with unchanged pose");
        backend.RigidBatchingEnabled=true;PreparedFrame(retainedActor,MatrixD.Identity);
        Check(Changed(preparedReference,PreparedFrame(retainedActor,movedPose))<4,"prepared actor replay updates moving transforms on both fenced slots");
        var fadedReference=Render(world:movedPose,lodAlpha:.5f);Check(Changed(preparedReference,fadedReference)>30,"stock object LOD alpha visibly dithers geometry");
        Check(PreparedFrame(retainedActor,movedPose,lodAlpha:.5f).SequenceEqual(fadedReference),"prepared transition uses exact stock object-alpha pixels");
        Check(Changed(preparedReference,PreparedFrame(retainedActor,movedPose))<4,"completed transition clears alpha and restores eligible batching without pose changes");
        Check(!PreparedFrame(retainedActor,movedPose,512|4096).SequenceEqual(preparedReference),"prepared actor revalidates visibility flags instead of replaying stale selection");
        Check(Changed(preparedReference,PreparedFrame(retainedActor,movedPose,origin:new Vector3(64,0,0)))<4,"prepared actor hint migrates across spatial batch cells without changing geometry");
        var replacement=Prepared(0,new Vector3(0,1,0),new Vector3(.2f,.3f,0));
        Check(Changed(PreparedFrame(retainedActor,movedPose),PreparedFrame(replacement,movedPose))>30,"replacement prepared material has visible tint and HSV changes");
        var preparedGeneric=Prepared(2,Vector3.One,Vector3.Zero);backend.RetainedSubmissionEnabled=false;var genericReference=Render(2,world:movedPose,stream:stream,count:2);backend.RetainedSubmissionEnabled=true;
        Check(PreparedFrame(preparedGeneric,movedPose,data:stream,count:2).SequenceEqual(genericReference),"prepared hardware instancing retains current stream, count, and actor transform");
        Check(backend.GetSubmissionStats().Actors==1 && backend.GetSubmissionStats().Parts==1 && backend.GetSubmissionStats().InstanceBlobBytes==128,"retained submission telemetry reports actual actor replay and owned instance bytes");
        backend.RetainedSubmissionEnabled=false;
        var rigidHints=new[]{new NativeBackend.RigidBatchHint(),new NativeBackend.RigidBatchHint()};
        byte[] QueueBatch(bool parallel,bool invalid=false){
            backend.RetainedSubmissionEnabled=true;backend.SetParallelQueue(parallel);backend.BeginPacketCollection();
            var constants=WorldConstants.Object(backend.WorldLayouts[2],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0);
            for(int i=0;i<4096;i++)backend.AddWorldPacket(2,invalid&&i==4095?999999UL:1UL,10,11,12,0,constants,stream,2,512,recycleConstants:false);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(width,height);
        }
        Check(Marshal.SizeOf<NativeQueueWorkStats>()==32,"queue worker stats ABI packing");
        var serialBatch=QueueBatch(false);Check(QueueBatch(true).SequenceEqual(serialBatch),"parallel immutable queue validation preserves batch pixels on first slot");
        var queueWork=backend.GetQueueWorkStats();Check(queueWork.Enabled==1&&queueWork.Workers>=1&&queueWork.Workers<=4&&queueWork.Draws==4096&&backend.GetQueueValidationStats().Draws==4096,"queue worker counters prove the full immutable batch was validated");
        Check(QueueBatch(true).SequenceEqual(serialBatch),"parallel immutable queue validation preserves batch pixels on second slot");
        bool workerRejected=false;try{QueueBatch(true,true);}catch(InvalidOperationException){workerRejected=true;}Check(workerRejected,"invalid geometry in a worker range rejects publication");
        Check(QueueBatch(true).SequenceEqual(serialBatch),"worker exception completion allows a valid subsequent queue");
        backend.UploadWorldMesh(9001,new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)},new uint[]{0,1,2});
        byte[] RecipeOrder(bool enabled,bool reverse=false,uint drawCount=2,float shift=0,uint drawFlags=512){
            backend.WorldRecipesEnabled=enabled;backend.RetainedSubmissionEnabled=true;backend.BeginPacketCollection();
            var constants=WorldConstants.Object(backend.WorldLayouts[2],MatrixD.CreateTranslation(shift,0,0),Vector3.One,Vector3.Zero,1,0);var currentStream=drawCount==2?stream:stream.Take((int)drawCount*64).ToArray();
            for(int i=0;i<4096;i++){int order=reverse?4095-i:i;backend.AddWorldPacket(2,(order%2==0)?1UL:9001UL,10,11,12,0,constants,currentStream,drawCount,drawFlags,recycleConstants:false);}
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(width,height);
        }
        Check(Marshal.SizeOf<NativeRecipeStats>()==40,"native recipe stats ABI packing");
        var recipeReference=RecipeOrder(false);Check(RecipeOrder(true).SequenceEqual(recipeReference),"compact native recipe queue matches legacy pixels");
        Check(RecipeOrder(true,true).SequenceEqual(recipeReference)&&RecipeOrder(true).SequenceEqual(recipeReference),"recipe identity survives packet permutation on both fenced slots");
        var recipeStats=backend.GetRecipeStats();Check(recipeStats.Revalidated==0&&recipeStats.GeometryReused==4096&&recipeStats.MaterialsReused==4096&&recipeStats.Live==2,"steady recipes bypass resource validation and material lookup without a whole ordered-schema match");
        backend.UploadWorldMesh(9002,new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)},new uint[]{0,1,2});
        Check(RecipeOrder(true,true).SequenceEqual(recipeReference)&&backend.GetRecipeStats().Revalidated==0,"unrelated mesh upload does not invalidate existing recipes");
        backend.UploadWorldMesh(1,new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)},new uint[]{0,1,2});
        Check(RecipeOrder(true).SequenceEqual(recipeReference)&&backend.GetRecipeStats().Revalidated==1,"mesh replacement revalidates only its dependent recipe");
        backend.UploadTexture(10,1,1,0,new byte[]{180,180,180,0},true);Check(RecipeOrder(true,true).SequenceEqual(recipeReference)&&backend.GetRecipeStats().Revalidated==2,"texture replacement revalidates both dependent recipes before descriptor reuse");
        NativeBackend.WorldRecipe oldOwnedRecipe=null;var recipeConstants=WorldConstants.Object(backend.WorldLayouts[2],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0);backend.EnsureWorldRecipe(ref oldOwnedRecipe,2,1,10,11,12,0,recipeConstants.Length);
        ulong Handle(NativeBackend.WorldRecipe r)=>(ulong)typeof(NativeBackend.WorldRecipe).GetField("Handle",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(r)!;
        ulong staleRecipeHandle=Handle(oldOwnedRecipe);
        backend.RemoveResource(3,1);bool recipeDeleted=false;try{RecipeOrder(true);}catch(InvalidOperationException){recipeDeleted=true;}Check(recipeDeleted,"deleted mesh cannot replay a retired recipe");
        backend.UploadWorldMesh(1,new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)},new uint[]{0,1,2});Check(RecipeOrder(true).SequenceEqual(recipeReference)&&backend.GetRecipeStats().Retired==1&&backend.GetRecipeStats().Live==2,"recreated mesh registers a new generation in a recycled recipe slot");
        var recipeRaw=Marshal.GetDelegateForFunctionPointer<RecipeRawFn>(Symbol("sevk_queue_world_recipes"));NativeBackend.WorldRecipe currentRecipe=null;backend.EnsureWorldRecipe(ref currentRecipe,2,1,10,11,12,0,recipeConstants.Length);
        byte[] rawObjects=new byte[((recipeConstants.Length+(int)alignment-1)/(int)alignment)*(int)alignment];Buffer.BlockCopy(recipeConstants,0,rawObjects,0,recipeConstants.Length);
        int RawRecipe(ulong handle,uint count=2,uint objectOffset=0,uint instanceOffset=0,uint reserved=0){backend.BeginPacketCollection();var draws=new[]{new RecipeDraw{Recipe=handle,Count=count,Flags=512,Cascades=255,ObjectOffset=objectOffset,InstanceOffset=instanceOffset,Reserved=reserved,Radius=-1}};return recipeRaw(nativeContext,WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),1280,NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)),draws,1,rawObjects,(uint)rawObjects.Length,stream,(uint)stream.Length);}
        Check(Marshal.SizeOf<RecipeDraw>()==48&&staleRecipeHandle!=Handle(currentRecipe)&&RawRecipe(staleRecipeHandle)!=0,"compact ABI48 and native generation check reject an old handle after slot recycling");
        Check(RawRecipe(Handle(currentRecipe),objectOffset:1)!=0&&RawRecipe(Handle(currentRecipe),instanceOffset:1)!=0,"registered recipe raw ABI rejects malformed aligned object/instance spans");
        Check(RawRecipe(Handle(currentRecipe),count:0)!=0&&RawRecipe(Handle(currentRecipe),reserved:1)!=0,"registered recipe raw ABI rejects malformed count/reserved fields");
        Check(RecipeOrder(true,true).SequenceEqual(recipeReference),"failed compact ABI queue allows a subsequent valid frame");
        backend.RemoveResource(0,10);bool recipeTextureDeleted=false;try{RecipeOrder(true,true);}catch(InvalidOperationException){recipeTextureDeleted=true;}Check(recipeTextureDeleted,"missing material cannot bypass registered recipe validation");
        backend.UploadTexture(10,1,1,0,new byte[]{180,180,180,0},true);Check(RecipeOrder(true).SequenceEqual(recipeReference),"material recreation restores registered recipe after failed queue");
        var dynamicRecipeControl=RecipeOrder(false,drawCount:1,shift:.2f);var hiddenRecipeControl=RecipeOrder(false,drawFlags:512|4096);RecipeOrder(true);RecipeOrder(true,true);
        Check(RecipeOrder(true,drawCount:1,shift:.2f).SequenceEqual(dynamicRecipeControl)&&backend.GetRecipeStats().Revalidated==0,"registered recipe still consumes changed constants and instance count/spans");
        Check(RecipeOrder(true,true,drawFlags:512|4096).SequenceEqual(hiddenRecipeControl)&&backend.GetRecipeStats().Revalidated==0,"registered recipe still applies current main-exclusion flags after permutation");
        bool recipeCountRejected=false;try{RecipeOrder(true,drawCount:0);}catch(InvalidOperationException){recipeCountRejected=true;}Check(recipeCountRejected,"registered recipe rejects invalid zero dynamic instance count");
        var modelPartA=new NativeBackend.PreparedPart(0,1,10,11,12,0,Vector3.One,Vector3.Zero,1,0,backend.WorldLayouts[0]);var modelPartB=new NativeBackend.PreparedPart(0,9001,10,11,12,0,Vector3.One,Vector3.Zero,1,0,backend.WorldLayouts[0]);var modelActors=new[]{new NativeBackend.PreparedActor(new[]{modelPartA,modelPartB}),new NativeBackend.PreparedActor(new[]{modelPartA,modelPartB})};
        byte[] ModelFrame(bool enabled,float move=0,uint modelFlags=512,uint modelCascades=255){backend.ModelBatchingEnabled=enabled;backend.RetainedSubmissionEnabled=true;backend.WorldRecipesEnabled=true;backend.RigidBatchingEnabled=true;backend.BeginPacketCollection();for(int i=0;i<2;i++)backend.SubmitPreparedActor(modelActors[i],MatrixD.CreateTranslation((i==0?.25:.75)+move,0,0),null,1,modelFlags,modelCascades,new Vector3(i==0?.25f:.75f,0,-3),2);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(width,height);}
        var modelReference=ModelFrame(false);Check(ModelFrame(true).SequenceEqual(modelReference)&&ModelFrame(true).SequenceEqual(modelReference),"whole-model instance sharing preserves both-slot legacy pixels");
        var modelStats=backend.GetModelBatchStats();Check(modelStats.Actors==2&&modelStats.Parts==4&&modelStats.InstanceBytes==128&&modelStats.Packets==2,"two model parts share one64-byte instance append per actor");
        var modelMoved=ModelFrame(false,.15f);Check(ModelFrame(true,.15f).SequenceEqual(modelMoved),"whole-model batching publishes changed actor transforms");
        var modelHidden=ModelFrame(false,modelFlags:512|4096);Check(ModelFrame(true,modelFlags:512|4096).SequenceEqual(modelHidden),"whole-model key preserves current main visibility flags");
        modelActors=new[]{new NativeBackend.PreparedActor(new[]{new NativeBackend.PreparedPart(0,1,10,11,12,0,Vector3.One,new Vector3(.1f,.2f,0),1,0,backend.WorldLayouts[0]),new NativeBackend.PreparedPart(0,9001,10,11,12,0,new Vector3(.8f,.9f,1),new Vector3(.1f,.2f,0),2,0,backend.WorldLayouts[0])}),new NativeBackend.PreparedActor(new[]{new NativeBackend.PreparedPart(0,1,10,11,12,0,Vector3.One,new Vector3(.3f,.1f,0),1,0,backend.WorldLayouts[0]),new NativeBackend.PreparedPart(0,9001,10,11,12,0,new Vector3(.8f,.9f,1),new Vector3(.3f,.1f,0),2,0,backend.WorldLayouts[0])})};
        var materialModelReference=ModelFrame(false,.15f);Check(ModelFrame(true,.15f).SequenceEqual(materialModelReference),"model template preserves per-part tint/emission and per-actor HSV without mixing immutable material state");
        Check(backend.GetPerformanceStats().LogicalDraws==4,"whole-model logical-draw counter subtracts physical model packets exactly once");
        modelActors=new[]{new NativeBackend.PreparedActor(new[]{modelPartA,modelPartB}),new NativeBackend.PreparedActor(new[]{modelPartA,modelPartB})};
        Check(ModelFrame(false,.15f).SequenceEqual(modelMoved),"batch fallback restores current per-part constants without a pose change");backend.ModelBatchingEnabled=false;
        backend.WorldRecipesEnabled=false;backend.RemoveResource(3,9001);backend.RemoveResource(3,9002);
        backend.SetParallelQueue(false);backend.RetainedSubmissionEnabled=false;
        byte[] RigidPair(bool batch,uint pipeline=0,bool painted=true,bool separateBuckets=false,bool hideSecond=false,bool mixedHint=false,Vector3? tint=null,float emissive=1)
        {
            backend.BeginPacketCollection();
            for(int i=0;i<2;i++)
            {
                var matrix=MatrixD.CreateRotationZ(i*.2)*MatrixD.CreateTranslation(i==0?-.6:.6,0,0);
                Vector3 hsv=painted?new Vector3(i*.4f,.7f,-.1f):Vector3.Zero;
                uint flags=hideSecond && i==1?512u|4096u:512u;Vector3 origin=separateBuckets && i==1?new Vector3(16,0,0):Vector3.Zero;
                if(batch)backend.AddRigidWorldPacket(pipeline,1,10,11,12,0,matrix,tint??Vector3.One,hsv,emissive,0,flags,0,origin,3,mixedHint && i==1?null:rigidHints[i]);
                else backend.AddWorldPacket(pipeline,1,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[pipeline],matrix,tint??Vector3.One,hsv,emissive,0),null,1,flags,0,origin,3);
            }
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(width,height);
        }
        byte[] separateRigid=RigidPair(false);before=backend.GetWorkStats();byte[] batchedRigid=RigidPair(true);after=backend.GetWorkStats();
        Check(Changed(separateRigid,batchedRigid)<4 && after.WorldDraws-before.WorldDraws==1 && after.WorldInstances-before.WorldInstances==2,
            "rigid transforms and HSV batch through the stock generic pipeline with matching GPU pixels");
        var batchingStats=backend.GetPerformanceStats();
        Check(batchingStats.LogicalDraws==2 && batchingStats.SubmittedDraws==1 && batchingStats.MainCommands==1,"batching diagnostics retain logical draw count and count actual commands");
        var greenSeparate=RigidPair(false,painted:false,tint:new Vector3(0,1,0));
        var greenBatched=RigidPair(true,painted:false,tint:new Vector3(0,1,0));
        Check(Changed(greenSeparate,greenBatched)<4,"non-white object multipliers preserve rigid GPU pixels through eligible submission");
        Check(backend.GetPerformanceStats().SubmittedDraws==1,"authenticated tint bridge retains one generic draw for colored rigid objects");
        Check(Changed(greenSeparate,RigidPair(false,painted:false))>30,"object multiplier fixture produces visible green tint");

        Check(Changed(batchedRigid,RigidPair(true))==0,"pooled rigid batches reset counts and bytes on the following frame");
        backend.UploadTexture(12,1,1,0,new byte[]{255,1,0,255},false);
        foreach(uint pipeline in new uint[]{0,5,10,15})
        {
            var paintedSeparate=RigidPair(false,pipeline);before=backend.GetWorkStats();var paintedBatch=RigidPair(true,pipeline);after=backend.GetWorkStats();
            Check(Changed(paintedSeparate,paintedBatch)<4 && after.WorldDraws-before.WorldDraws==1,
                $"rigid batch variant {pipeline} retains painted pixels and one physical draw");
            var tintedSeparate=RigidPair(false,pipeline,tint:new Vector3(.2f,1,.3f));
            Check(Changed(tintedSeparate,RigidPair(true,pipeline,tint:new Vector3(.2f,1,.3f)))<4 && backend.GetPerformanceStats().SubmittedDraws==1,$"variant {pipeline} retains non-white color and one grouped draw");
            Check(Changed(paintedSeparate,RigidPair(false,pipeline,false))>30,$"variant {pipeline} test exercises visible HSV paint");
        }
        backend.UploadTexture(12,1,1,0,new byte[]{255,255,0,0},false);
        var glowingRigid=RigidPair(false,painted:false,tint:new Vector3(0,1,0),emissive:3);
        Check(Changed(glowingRigid,RigidPair(true,painted:false,tint:new Vector3(0,1,0),emissive:3))<4,"generic tint bridge preserves colored emissive output");
        Check(Changed(glowingRigid,RigidPair(false,painted:false,tint:new Vector3(0,1,0),emissive:0))>30,"emissive fixture visibly exercises glow intensity");
        backend.UploadTexture(12,1,1,0,new byte[]{255,1,0,0},false);
        var separatedBuckets=RigidPair(false,separateBuckets:true);var hintedBuckets=RigidPair(true,separateBuckets:true);
        Check(Changed(separatedBuckets,hintedBuckets)<4 && backend.GetPerformanceStats().SubmittedDraws==2,"hint key changes retain separate spatial buckets without changing pixels");
        backend.WideRigidBatchesEnabled=true;
        var wideBuckets=RigidPair(true,separateBuckets:true);
        Check(Changed(separatedBuckets,wideBuckets)<4 && backend.GetPerformanceStats().SubmittedDraws==1,"64 metre grouping preserves rigid pixels across former bucket boundaries");
        var wideVisibility=RigidPair(true,hideSecond:true);
        var wideVisibilityStats=backend.GetPerformanceStats();
        Check(Changed(RigidPair(false,hideSecond:true),wideVisibility)<4 && wideVisibilityStats.SubmittedDraws==2 && wideVisibilityStats.MainCommands==1,"wide grouping preserves distinct visibility identities");

        backend.WideRigidBatchesEnabled=false;
        Check(Changed(separatedBuckets,RigidPair(true,separateBuckets:true))<4 && backend.GetPerformanceStats().SubmittedDraws==2,"cell-width changes reject stale group hints");
        byte[] SpatialPair(bool wide,bool splitCascades=false,bool unknownBound=false)
        {
            backend.WideRigidBatchesEnabled=wide;backend.BeginPacketCollection();
            for(int i=0;i<2;i++){
                float x=i==0?2:18;var matrix=MatrixD.CreateScale(3)*MatrixD.CreateTranslation(x,0,-20);
                backend.AddRigidWorldPacket(0,1,10,11,12,0,matrix,Vector3.One,new Vector3(i*.4f,.7f,-.1f),1,0,512,splitCascades?(uint)(i+1):1,new Vector3(x,-1,-29),unknownBound && i==1?-1:5);
            }
            if(wide && !splitCascades){
                var packets=(List<WorldPacket>)typeof(NativeBackend).GetField("worldPackets",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(backend);
                Check(packets.Count==1 && packets[0].Count==2,"wide grouping retains both transformed instances in one packet");
                var packet=packets[0];
                if(unknownBound)Check(packet.Radius<0,"an unknown source bound keeps the entire batch unbounded");
                else Check(Vector3.Distance(packet.Origin,new Vector3(2,-1,-29))+5<=packet.Radius+.0001f && Vector3.Distance(packet.Origin,new Vector3(18,-1,-29))+5<=packet.Radius+.0001f,"merged sphere encloses both authoritative source spheres");
            }
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,width,height,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(width,height);
        }
        var spatialNarrow=SpatialPair(false);var spatialWide=SpatialPair(true);
        Check(spatialNarrow.SequenceEqual(spatialWide) && backend.GetPerformanceStats().SubmittedDraws==1,"actual transformed geometry crossing a 16 metre boundary has identical wide/narrow GPU pixels");
        SpatialPair(true,splitCascades:true);Check(backend.GetPerformanceStats().SubmittedDraws==2,"wide groups preserve distinct cascade masks");
        SpatialPair(true,unknownBound:true);backend.SetParallelCascades(false); // Exact serial reference; explicit parallel cases below.
        backend.WideRigidBatchesEnabled=false;
        var separatedVisibility=RigidPair(false,hideSecond:true);var hintedVisibility=RigidPair(true,hideSecond:true);
        Check(Changed(separatedVisibility,hintedVisibility)<4 && backend.GetPerformanceStats().SubmittedDraws==2 && backend.GetPerformanceStats().MainCommands==1,"hint key changes preserve main-view exclusion flags");
        RigidPair(true);
        // Populate the real cache budget, then begin a new frame. A stale hint
        // and an ordinary lookup must join the same replacement batch.
        backend.BeginPacketCollection();
        for(int i=0;i<32770;i++)backend.AddRigidWorldPacket(0,1,10,11,12,0,MatrixD.CreateTranslation(i*16.0,0,0),Vector3.One,Vector3.Zero,1,0,512,0,new Vector3(i*16,0,0),3);
        var refreshedHints=RigidPair(true,mixedHint:true);
        Check(Changed(batchedRigid,refreshedHints)<4 && backend.GetPerformanceStats().SubmittedDraws==1,"cache budget reset rejects retired hints and joins fresh lookups");
        Render();Render();Render();
        backend.BeginUploads();backend.UploadTexture(99,1,1,0,new byte[]{1,2,3,4},false);backend.UploadTexture(99,1,1,0,new byte[]{4,3,2,1},false);backend.EndUploads();Render();
        var unrelated=backend.GetPerformanceStats();Check(unrelated.MaterialMisses==0 && unrelated.MaterialResets==0,"replacing an unrelated texture preserves completed material descriptors");backend.RemoveResource(0,99);
        byte[] Cube(MyCubeInstanceData cube)
        {
            var bytes=new byte[64];for(int b=0;b<8;++b){var p=cube.GetPackedBone(b);bytes[b*4]=p.X;bytes[b*4+1]=p.Y;bytes[b*4+2]=p.Z;bytes[b*4+3]=p.W;}
            var t=cube.PackedOrthoMatrix;Buffer.BlockCopy(new[]{t.X,t.Y,t.Z,t.W,0f,0f,0f,0f},0,bytes,32,32);return bytes;
        }
        var cube=new MyCubeInstanceData{LocalMatrix=Matrix.Identity};byte[] rigidCube=Render(3,stream:Cube(cube));Check(Changed(image,rigidCube)<10,"stock packed cube rotation agrees with rigid geometry");
        backend.RetainedSubmissionEnabled=true;Check(PreparedFrame(Prepared(3,Vector3.One,Vector3.Zero),MatrixD.Identity,data:Cube(cube)).SequenceEqual(rigidCube),"prepared cube stream preserves packed bone/ortho contract");backend.RetainedSubmissionEnabled=false;
        cube.EnableSkinning=true;cube.BoneRange=1;for(int b=0;b<9;++b)cube[b]=new Vector3UByte(230,127,127);
        byte[] deformed=Render(4,stream:Cube(cube));Check(Changed(rigidCube,deformed)>90,"stock cube bone deformation moves pixels without CPU vertex expansion");
        light.SunDiffuseFactor=0;light.AmbientDiffuseFactor=0;byte[] dark=Render();
        backend.SetLights(new[]{new NativeLight{PositionRange=new[]{0f,0f,-1f,8f},ColorIntensity=new[]{3f,0f,0f,5f},DirectionCone=new[]{0f,0f,-1f,.8f}}});
        byte[] point=Render();Check(point[center]>dark[center]+30 && point[center]>point[center+1]+30,"stock tiled point lighting reads the GBuffer");
        backend.SetLights(new[]{new NativeLight{PositionRange=new[]{0f,0f,-1f,8f},ColorIntensity=new[]{0f,3f,0f,5f},DirectionCone=new[]{0f,0f,-1f,.8f},Flags=1}});
        byte[] spot=Render();Check(spot[center+1]>dark[center+1]+30 && spot[center+1]>spot[center]+30,"stock spotlight constants and additive lighting read the GBuffer");backend.SetLights(Array.Empty<NativeLight>());
        light.SunDiffuseFactor=1;
        backend.BeginUploads();backend.UploadTexture(10,1,1,0,new byte[]{255,80,80,0},true);backend.UploadTexture(10,1,1,0,new byte[]{80,255,80,0},true);backend.UploadTexture(99,1,1,0,new byte[]{1,2,3,4},false);backend.RemoveResource(0,99);backend.EndUploads();
        byte[] replaced=Render();Check(replaced[center+1]>replaced[center]+30,"replacement and removal in one pending upload batch retain GPU ownership");
        for(int i=0;i<6;++i){backend.BeginUploads();backend.UploadTexture(10,1,1,0,new byte[]{80,255,80,0},true);backend.EndUploads();Render();}
        Check(backend.GetWorkStats().UploadSlotWaits<=backend.GetWorkStats().UploadBatches,"upload waits occur only when recycling bounded batch slots");
        foreach(uint pipeline in Enumerable.Range(0,20).Select(i=>(uint)i))
        {
            uint type=pipeline%5;float[] palette=type==1?Bones(MatrixD.Identity):null;byte[] instance=type==2?Instance(Matrix.Identity):type>=3?Cube(new MyCubeInstanceData{LocalMatrix=Matrix.Identity}):null;
            backend.RetainedSubmissionEnabled=true;backend.WorldRecipesEnabled=false;var variantReference=Render(pipeline,bones:palette,stream:instance);
            backend.WorldRecipesEnabled=true;Check(Render(pipeline,bones:palette,stream:instance).SequenceEqual(variantReference),$"native recipe variant {pipeline} preserves rigid/skinned/instanced pixels on first slot");
            Check(Render(pipeline,bones:palette,stream:instance).SequenceEqual(variantReference),$"native recipe variant {pipeline} preserves pixels on other fenced slot");
        }
        Check(backend.GetStats().ValidationErrors==0,"all 20 material variants rasterize without validation errors");
        MatrixD shadow=MatrixD.CreateLookAt(new Vector3D(0,0,2),new Vector3D(0,0,-3),Vector3D.Up)*MatrixD.CreateOrthographic(12,12,.1,20);
        backend.SetShadowCascades(Enumerable.Range(0,4).SelectMany(_=>NativeBackend.MatrixValues(shadow)).ToArray(),new[]{5f,10f,20f,40f});
        Render(1,bones:Bones(MatrixD.Identity),flags:0);Render(2,stream:stream,count:2,flags:0);Render(4,stream:Cube(cube),flags:0);
        Check(backend.GetStats().ValidationErrors==0,"stock depth partners replay palettes and hardware instances in four cascades");
        backend.SetShadowCascades(Array.Empty<float>(),Array.Empty<float>());
        var constants=WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0);backend.BeginPacketCollection();backend.AddWorldPacket(0,1,10,11,12,0,constants,null,1,512);
        byte[] frame=WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light);backend.QueueWorld(frame,NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Array.Clear(constants);Array.Clear(frame);GC.Collect();
        backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();byte[] copied=backend.Readback(64,64);
        Check(copied[center+1]>copied[center]+30,"native frame/object packets own copies after managed memory changes");
        backend.Resize(0,0,0);backend.BeginUploads();backend.UploadTexture(10,1,1,0,new byte[]{80,255,80,0},true);backend.EndUploads();
        Check(!backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"minimized presentation skips drawing while uploads complete");
        backend.Resize(96,80,1);width=96;height=80;Check(Render().Length==96*80*4,"restore and resize rebuild world attachments");
        backend.Resize(64,64,0);width=height=64;Render();
        Check(backend.GetStats().ValidationErrors==0,"skinning/instancing/local lighting/batched replacement have zero synchronization validation errors: "+backend.GetStats().Detail);
        backend.UploadWorldCube(55,1,1,0,new byte[]{255,0,0,255,0,255,0,255,0,0,255,255,0,0,255,255,0,0,255,255,0,0,255,255},false);backend.SetWorldSkybox(55);light.SkyboxBrightness=1;
        byte[] SkyRotation(double angle){backend.BeginPacketCollection();backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,background:MatrixD.CreateRotationY(angle)),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(64,64);}
        var positive=SkyRotation(Math.PI/2);Check(positive[center]>positive[center+1]+50,"stock background matrix convention rotates the view toward the positive X cubemap face");
        var negative=SkyRotation(-Math.PI/2);Check(negative[center+1]>negative[center]+50,"opposite skybox rotation samples the negative X cubemap face");backend.SetWorldSkybox(0);backend.RemoveResource(4,55);
        backend.UploadWorldMesh(910,new[]{Vertex(-.9f,-.6f),Vertex(-.1f,-.6f),Vertex(-.5f,.6f)},new uint[]{0,1,2});
        backend.UploadWorldMesh(911,new[]{Vertex(.1f,-.6f),Vertex(.9f,-.6f),Vertex(.5f,.6f)},new uint[]{0,1,2});
        backend.UploadTexture(910,1,1,0,new byte[]{150,190,220,0},true);
        NativeBackend.PreparedActor BulkActor(Vector3 hsv,bool mixed=false)=>new(new[]{new NativeBackend.PreparedPart(0,910,910,11,12,0,Vector3.One,hsv,1,0,backend.WorldLayouts[0]),new NativeBackend.PreparedPart(0,911,10,11,12,0,Vector3.One,mixed?Vector3.Zero:hsv,1,0,backend.WorldLayouts[0])});
        var firstBulk=BulkActor(new Vector3(.1f,.2f,0));var secondBulk=BulkActor(new Vector3(.35f,.15f,0));
        var singleBulk=new NativeBackend.PreparedActor(new[]{new NativeBackend.PreparedPart(0,911,10,11,12,0,Vector3.One,Vector3.Zero,1,0,backend.WorldLayouts[0])});
        var mixedBulk=BulkActor(new Vector3(.1f,.2f,0),true);
        var bulkPost=new VulkanPostprocess();bulkPost.Hbao.Enabled=false;bulkPost.Settings.BloomEnabled=false;bulkPost.Settings.Data.ChromaticFactor=0;bulkPost.Settings.Data.VignetteStart=0;
        byte[] BulkFrame(bool bulk,bool outline=false,bool exclude=false,bool mixed=false,uint mask=3,float movement=0,bool retireOther=false){
            backend.ModelBatchingEnabled=true;backend.BulkModelsEnabled=bulk;backend.WorldRecipesEnabled=true;backend.RetainedSubmissionEnabled=true;backend.ObjectRetentionEnabled=false;backend.RigidBatchingEnabled=true;
            backend.SetLights(new[]{new NativeLight{PositionRange=new float[]{0,0,0,20},ColorIntensity=new float[]{8,8,8,1},DirectionCone=new float[]{0,0,-1,.5f},Flags=1}});
            backend.SetSpots(256,new[]{new NativeSpot{Light=0,Flags=1,Projection=NativeBackend.MatrixValues(projection)}});backend.SetSpotRedraw(1,1);
            if(retireOther)backend.UploadWorldMesh(912,new[]{Vertex(-.9f,-.6f),Vertex(-.1f,-.6f),Vertex(-.5f,.6f)},new uint[]{0,1,2});
            backend.BeginPacketCollection();backend.BeginLogicalSelectionCapture();
            using(backend.WithLogicalOwner(900))backend.SubmitPreparedActor(mixed?mixedBulk:firstBulk,MatrixD.Identity,null,1,0x1000000u,mask,new Vector3(0,0,-3),2);
            if(retireOther)backend.RemoveResource(3,912);
            uint identity=backend.AddWorldPacket(0,910,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(.8,0,.2),Vector3.One,Vector3.Zero,1,0),flags:0x1000000u,cascadeMask:mask,origin:new Vector3(.8f,0,-2.8f),radius:2);
            if(outline)backend.AddHighlightPacket(identity,0,3,0,1,new Vector4(0,1,1,1),3);
            var excluded=exclude?new[]{new NativeShadowExclusion{Light=0,Draw=identity}}:Array.Empty<NativeShadowExclusion>();backend.ShadowExclusions=excluded;
            using(backend.WithLogicalOwner(901))backend.SubmitPreparedActor(secondBulk,MatrixD.CreateTranslation(.35+movement,0,0),null,1,0x1000000u,mask,new Vector3(.35f+movement,0,-3),2);
            using(backend.WithLogicalOwner(902))backend.SubmitPreparedActor(singleBulk,MatrixD.CreateTranslation(-.35,0,0),null,1,0x1000000u,mask,new Vector3(-.35f,0,-3),2);
            bulkPost.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:bulkPost.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            if(exclude)Check(excluded[0].Draw==identity,"native model remap does not mutate caller-owned shadow exclusions");
            backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();return backend.Readback(64,64);
        }
        var shadowPair=new float[32];Array.Copy(NativeBackend.MatrixValues(projection),0,shadowPair,0,16);Array.Copy(NativeBackend.MatrixValues(projection),0,shadowPair,16,16);backend.SetShadowCascades(shadowPair,new[]{10f,100f});
        foreach(int fixture in Enumerable.Range(0,6)){
            bool outline=fixture==1,exclude=fixture==2,mixed=fixture==3;uint mask=fixture==4?1u:3u;float movement=fixture==5?.4f:0;
            var expected=BulkFrame(false,outline,exclude,mixed,mask,movement);var referenceLogical=backend.EndLogicalSelectionCapture();var expectedStats=backend.GetPerformanceStats();
            for(int slot=0;slot<3;slot++){
                var actual=BulkFrame(true,outline,exclude,mixed,mask,movement);var actualLogical=backend.EndLogicalSelectionCapture();
                Check(expected.SequenceEqual(actual),"bulk model exact mixed legacy/highlight/exclusion/mask/motion pixels fixture "+fixture+" slot "+slot);
                Check(referenceLogical.SequenceEqual(actualLogical),"bulk model retains ordered logical owner/part identities fixture "+fixture+" slot "+slot);
                Check(expectedStats.LogicalDraws==backend.GetPerformanceStats().LogicalDraws&&expectedStats.SpotCommands==backend.GetPerformanceStats().SpotCommands&&expectedStats.ShadowCommands==backend.GetPerformanceStats().ShadowCommands,"bulk model physical and logical command accounting matches reference fixture "+fixture+" slot "+slot);
                var grouped=backend.GetModelGroupStats();Check(grouped.Enabled==1&&grouped.Instances==(mixed?2u:3u)&&grouped.Packets>0&&grouped.LegacyDraws>=1,"bulk model groups own eligible representations and retain legacy consumers fixture "+fixture+" slot "+slot);
            }
            Check(BulkFrame(false,outline,exclude,mixed,mask,movement).SequenceEqual(expected),"switching bulk off restores reference without a pose change fixture "+fixture);backend.EndLogicalSelectionCapture();
        }
        var withoutOutline=BulkFrame(true);backend.EndLogicalSelectionCapture();var withOutline=BulkFrame(true,outline:true);backend.EndLogicalSelectionCapture();Check(withoutOutline.Where((v,i)=>v!=withOutline[i]).Count()>20,"legacy highlight remap has a visible nonvacuous effect after native groups precede it");
        BulkFrame(true,exclude:true);backend.EndLogicalSelectionCapture();Check(backend.GetPerformanceStats().SpotCommands==3,"excluded legacy caster maps to the correct physical draw after model expansion");
        backend.UploadTexture(910,1,1,0,new byte[]{220,100,80,0},true);var replacementReference=BulkFrame(false);backend.EndLogicalSelectionCapture();Check(BulkFrame(true).SequenceEqual(replacementReference),"texture replacement revalidates model recipe descriptors");backend.EndLogicalSelectionCapture();
        backend.UploadWorldMesh(910,new[]{Vertex(-.7f,-.4f),Vertex(-.1f,-.4f),Vertex(-.4f,.4f)},new uint[]{0,1,2});replacementReference=BulkFrame(false);backend.EndLogicalSelectionCapture();Check(BulkFrame(true).SequenceEqual(replacementReference),"mesh replacement refreshes registered model geometry");backend.EndLogicalSelectionCapture();
        backend.RemoveResource(3,910);backend.UploadWorldMesh(910,new[]{Vertex(-.9f,-.6f),Vertex(-.1f,-.6f),Vertex(-.5f,.6f)},new uint[]{0,1,2});replacementReference=BulkFrame(false);backend.EndLogicalSelectionCapture();Check(BulkFrame(true).SequenceEqual(replacementReference),"mesh retirement resets native template generation before handle reuse");backend.EndLogicalSelectionCapture();
        var retirementReference=BulkFrame(false,retireOther:true);backend.EndLogicalSelectionCapture();
        for(int slot=0;slot<3;slot++){Check(BulkFrame(true,retireOther:true).SequenceEqual(retirementReference)&&backend.GetModelGroupStats().Instances==3,"unrelated mesh retirement preserves unpublished bulk representations slot "+slot);backend.EndLogicalSelectionCapture();}
        var residencyReference=BulkFrame(false);backend.EndLogicalSelectionCapture();
        var residentTemplates=(System.Collections.IList)typeof(NativeBackend).GetField("modelTemplateSlots",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(backend)!;var residentTemplate=residentTemplates[0];while(residentTemplates.Count<=32768)residentTemplates.Add(residentTemplate);
        Check(BulkFrame(true).SequenceEqual(residencyReference)&&residentTemplates.Count<32768,"frame boundary reclaims managed/native template residency and rebuilds exact pixels");backend.EndLogicalSelectionCapture();
        var registerModelRaw=Marshal.GetDelegateForFunctionPointer<RegisterModelRawFn>(Symbol("sevk_register_model"));var clearModelsRaw=Marshal.GetDelegateForFunctionPointer<ClearModelsRawFn>(Symbol("sevk_clear_models"));var appendModelsRaw=Marshal.GetDelegateForFunctionPointer<AppendModelsRawFn>(Symbol("sevk_append_models"));
        var canonicalModelObject=WorldConstants.Object(backend.WorldLayouts[2],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0);NativeBackend.WorldRecipe rawModelRecipe=null;backend.EnsureWorldRecipe(ref rawModelRecipe,2,1,10,11,12,0,canonicalModelObject.Length);
        ulong rawRecipeHandle=(ulong)rawModelRecipe.GetType().GetField("Handle",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(rawModelRecipe)!;
        Check(registerModelRaw(nativeContext,new[]{new RawModelPart{Recipe=rawRecipeHandle,ObjectOffset=1}},1,canonicalModelObject,(uint)canonicalModelObject.Length,out _)!=0,"native model registration rejects a noncanonical object span");
        Check(registerModelRaw(nativeContext,new[]{new RawModelPart{Recipe=rawRecipeHandle,Reserved=NativeBackend.StockBackCull|NativeBackend.StockNoCull}},1,canonicalModelObject,(uint)canonicalModelObject.Length,out _)!=0,"native model registration rejects conflicting raster flags");
        Check(registerModelRaw(nativeContext,new[]{new RawModelPart{Recipe=rawRecipeHandle}},1,canonicalModelObject,(uint)canonicalModelObject.Length,out ulong rawModelHandle)==0,"raw native model registration publishes a valid generational handle");
        void EmptyModelFrame(){backend.BulkModelsEnabled=false;backend.BeginPacketCollection();backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));}
        EmptyModelFrame();Check(appendModelsRaw(nativeContext,new[]{new NativeModelInstance{Model=rawModelHandle,Cell=0,Order=1}},1,null,0,null)!=0,"bulk model frame rejects invalid cell ownership");
        EmptyModelFrame();Check(appendModelsRaw(nativeContext,new[]{new NativeModelInstance{Model=rawModelHandle,Cell=16,Order=1,Reserved=2}},1,null,0,null)!=0,"bulk model frame rejects a group order after its own submission");
        EmptyModelFrame();Check(appendModelsRaw(nativeContext,new[]{new NativeModelInstance{Model=rawModelHandle,Cell=16,Order=1,Reserved=1}},1,null,0,null)==0,"bulk model frame accepts its explicit first-seen group order");
        EmptyModelFrame();Check(appendModelsRaw(nativeContext,new[]{new NativeModelInstance{Model=rawModelHandle,Cell=16,Order=2},new NativeModelInstance{Model=rawModelHandle,Cell=16,Order=1}},2,null,0,null)!=0,"bulk model frame rejects reversed instance publication order");
        Check(clearModelsRaw(nativeContext)==0,"native model registry clears outside recording");EmptyModelFrame();Check(appendModelsRaw(nativeContext,new[]{new NativeModelInstance{Model=rawModelHandle,Cell=16,Order=1}},1,null,0,null)!=0,"retired model handle cannot survive registry reset");
        backend.BulkModelsEnabled=false;backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(512,Array.Empty<NativeSpot>());backend.SetShadowCascades(Array.Empty<float>(),Array.Empty<float>());backend.RemoveResource(3,910);backend.RemoveResource(3,911);backend.RemoveResource(0,910);
        Check(backend.GetStats().ValidationErrors==0,"bulk model registration, slot reuse and retirement have zero synchronization validation errors");
        backend.BeginPacketCollection();backend.RemoveResource(3,1);foreach(ulong id in new ulong[]{10,11,12})backend.RemoveResource(0,id);var clean=backend.GetStats();
        Check(clean.Meshes==0 && clean.Textures==1,"world unload releases persistent geometry and material resources");
        File.WriteAllText(Path.Combine(output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{Checks=checks,Work=backend.GetWorkStats(),Stats=backend.GetStats()},new System.Text.Json.JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        File.WriteAllBytes(Path.Combine(output,"initial.rgba8"),image);
        Console.WriteLine($"{checks} checks passed.");return 0;
    }
}
