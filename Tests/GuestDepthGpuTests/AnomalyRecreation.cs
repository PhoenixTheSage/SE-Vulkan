using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int AnomalyRecreation(string root,string output,string bin,VulkanShaderLibrary library,NativeBackend oldBackend,VulkanScene oldScene,VulkanVoxels oldVoxels,MatrixD projection,MyEnvironmentLightData light)
    {
        int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        string anomaly=Path.GetFullPath(Path.Combine(root,"../Anomaly")),assets=Path.Combine(anomaly,"Assets");
        var core=Assembly.LoadFrom(Path.Combine(anomaly,"ClientPlugin/bin/Release/net10.0/Anomaly.dll"));
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Field(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetField(name,stat).GetValue(null);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        object Upload(string method,params object[] args){var buffer=Activator.CreateInstance(core.GetType("ClientPlugin.Buffers.PublishedBuffer",true));buffer.GetType().GetMethod(method).Invoke(buffer,args);Call("Buffers.BufferCatalog","Publish","fixture.epoch",method,buffer);return buffer;}
        ulong Id(object buffer)=>(ulong)buffer.GetType().GetProperty("ImageId").GetValue(buffer);
        byte[] tex={37,0,0,255,37,0,0,255,37,0,0,255,37,0,0,255};
        byte[][] mips={Enumerable.Repeat((byte)12,32).Concat(new byte[]{5,5,5,5}).ToArray(),new byte[]{220,0,0,255,99,99,99,99}};
        float[] values={7,8,9,10};byte[] art=Enumerable.Repeat((byte)99,16).ToArray();
        object texture=Upload("PublishRgba8",tex,2,2),volume=Upload("PublishVolumeMips",mips,2),data=Upload("PublishStructuredFloats",values,16);
        Call("Backend.GuestColor","SetCelestialData","fixture.epoch",values);Call("Backend.GuestColor","SetCelestialArt","fixture.epoch",2,1,2,art);
        Check(Id(texture)!=0 && Id(volume)!=0 && Id(data)!=0,"CPU-backed pack resources uploaded before recreation");
        ulong textureId=Id(texture),volumeId=Id(volume),dataId=Id(data);
        texture.GetType().GetMethod("PublishRgba8").Invoke(texture,new object[]{tex,2,2});volume.GetType().GetMethod("PublishVolumeMips").Invoke(volume,new object[]{mips,2});data.GetType().GetMethod("PublishStructuredFloats").Invoke(data,new object[]{values,16});
        Check(Id(texture)==textureId && Id(volume)==volumeId && Id(data)==dataId,"repeated texture, mip-volume and structured uploads reuse native IDs");
        object retained=Activator.CreateInstance(core.GetType("ClientPlugin.Buffers.GuestRendererImage",true),new object[]{Id(texture),2,2,1});
        Check(retained.GetType().GetProperty("IsAvailable").GetValue(retained) is true,"retained guest wrapper starts on its owning device");
        tex[0]=0;mips[1][0]=0;values[2]=0;art[15]=0;
        int ended=0;Call("Buffers.BufferCatalog","RegisterLifetime","fixture.epoch",null,(Action)(()=>ended++));
        Call("Buffers.BufferCatalog","RequestLitMips",3);Call("Buffers.BufferCatalog","RequestOccupancy",true);Call("Buffers.BufferCatalog","RequestPointShadows",2,32);
        long previous=(long)Property("Backend.GuestBridgeClient","ResourceEpoch");
        oldVoxels.Dispose();oldScene.Dispose();oldBackend.Dispose();
        Check((long)Property("Backend.GuestBridgeClient","ResourceEpoch")==0,"disposed native context exposes no resource epoch");
        Check(Call("Backend.GuestDevice","Synchronize") is true && ended==1,"device teardown invokes registered lifetime callback exactly once");
        Check(Id(texture)==0 && Id(volume)==0 && Id(data)==0,"teardown invalidates all CPU-backed native image IDs");
        Check(retained.GetType().GetProperty("IsAvailable").GetValue(retained) is false,"retained guest wrapper expires when its device is destroyed");
        var programs=(System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot");
        Check(programs.Cast<object>().All(p=>(ulong)p.GetType().GetField("Pipeline").GetValue(p)==0 && (ulong)p.GetType().GetField("StorageImage").GetValue(p)==0),"teardown invalidates all retained pipeline and storage IDs");
        Check(((System.Collections.IDictionary)Field("Backend.GuestColor+Celestial","Data")).Count==0,"teardown removes celestial device IDs");
        using var backend=new NativeBackend(null);
        backend.Create(64,64,0,true,Path.Combine(output,"Cache"));
        backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));
        backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));backend.SetParallelCascades(false);backend.WorldRecipesEnabled=false;
        using var scene=new VulkanScene(backend);using var voxels=new VulkanVoxels(backend,scene.TextureCache);
        scene.SetCamera(Vector3D.Zero,VRage.Library.Utils.MyTimeSpan.Zero,1f/60);
        typeof(GuestBridge).GetMethod("Bind",stat).Invoke(null,new object[]{backend,scene});
        Check((long)Property("Backend.GuestBridgeClient","ResourceEpoch")!=previous && Call("Backend.GuestDevice","Synchronize") is true,"new native context restores the retained CPU registries");
        Check(ended==1 && Id(texture)!=0 && Id(volume)!=0 && Id(data)!=0,"restoration keeps lifetime notification singular and reuploads pack images");
        Check(retained.GetType().GetProperty("IsAvailable").GetValue(retained) is false,"retained guest wrapper cannot revive on the replacement device");
        Check(Property("Backend.GuestOccupancy","Requested") is true && (int)Property("Backend.GuestOccupancy","PointCap")==2 && (int)Property("Backend.GuestOwned","ExplicitLitMipLevels")==3,"producer requests survive native device replacement");
        // Read all retained payload kinds through their actual shader resource
        // types, including the non-base mip and array layer.
        string file=Path.Combine(output,"EpochUpload.hlsl");
        File.WriteAllText(file,"Texture3D<float4> V:register(t0); StructuredBuffer<float4> D:register(t1); Texture2D<float4> T:register(t2); StructuredBuffer<float4> C:register(t3); Texture2DArray<float4> A:register(t4); float4 main(float4 p:SV_Position):SV_Target{return float4(V.Load(int4(0,0,0,1)).r,(D[0].z+C[0].x)/255,T.Load(int3(0,0,0)).r,A.Load(int4(1,0,1,0)).a);}");
        Call("Backend.GuestPassList","AddProgram","fixture.epoch.upload","AfterLighting",file,"PublishOnly");
        object probe=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)p.GetType().GetField("Id").GetValue(p)=="fixture.epoch.upload");
        void Set(string name,object value)=>probe.GetType().GetField(name).SetValue(probe,value);
        Set("ResourceContract",1);Call("Backend.GuestCompiler","CompilePending",assets);Set("Enabled",false);
        ulong pipeline=(ulong)probe.GetType().GetField("Pipeline").GetValue(probe);
        Check(pipeline!=0,"restored device compiles the retained-payload GPU oracle");
        var celestialData=(System.Collections.IDictionary)Field("Backend.GuestColor+Celestial","Data");var celestialArt=(System.Collections.IDictionary)Field("Backend.GuestColor+Celestial","Art");
        var create=typeof(NativeBackend).GetMethod("GuestCreateImage",BindingFlags.Instance|BindingFlags.NonPublic);
        var submit=typeof(NativeBackend).GetMethod("GuestSubmit2",BindingFlags.Instance|BindingFlags.NonPublic);
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        var replay=typeof(NativeBackend).GetMethod("GuestReplayNow",BindingFlags.Instance|BindingFlags.NonPublic);
        const ulong target=7001;create.Invoke(backend,new object[]{target,1,1,1,0});GuestBridge.Reset();
        Check(submit.Invoke(backend,new object[]{2,5,1,1,1,pipeline,new[]{target},null,new[]{Id(volume),Id(data),Id(texture),(ulong)celestialData["fixture.epoch"],(ulong)celestialArt["fixture.epoch"]},0}) is true,"restored payload GPU packet accepted");
        replay.Invoke(backend,new object[]{2});var pixels=new byte[4];read.Invoke(backend,new object[]{target,pixels});
        Check(pixels.SequenceEqual(new byte[]{220,16,37,99}),"actual recreated GPU reads cloned volume mip, structured data, texture and celestial array payloads: "+string.Join(",",pixels));
        const string id="fixture.live.volume";float interval=-1;
        Call("Shaders.VolumetricMediumRegistry","SetEnabled",id,true);Call("Shaders.VolumetricMediumRegistry","RequestRenderer",id,true);Call("Shaders.VolumetricMediumRegistry","SetIntervalCallback",id,(Action<float>)(v=>interval=v));
        Call("Backend.GuestVolumes","PreparePrograms",assets);Call("Backend.GuestCompiler","CompilePending",assets);
        int outputWidth=64,outputHeight=64;
        bool Frame()
        {
            var size=backend.ResolveRenderResolution(outputWidth,outputHeight);
            typeof(GuestBridge).GetMethod("BeginScene",stat).Invoke(null,new object[]{backend,scene});
            Call("Backend.SceneView","Set",size.X,size.Y,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestVolumeShadows","Prepare",new double[3],new[]{0f,1f,0f});
            backend.BeginPacketCollection();scene.Draw(backend);typeof(VulkanScene).GetMethod("CollectGuestSceneDepth",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scene,new object[]{voxels});
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size.X,size.Y,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"recreated world frame begins");
            Check(Call("Backend.GuestFrame","Capture") is true,"recreated core captures current renderer frame");
            var grid=core.GetType("ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot+Grid",true);var snap=core.GetType("ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot+Snapshot",true);
            core.GetType("ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot",true).GetField("current",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,Activator.CreateInstance(snap,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{Array.CreateInstance(grid,0),true,"Controlled empty world",Environment.TickCount},null));
            Call("Backend.GuestBridgeClient","Submit",Call("Backend.GuestPassList","Snapshot"));bool active=Property("Backend.GuestVolumes","Active") is true;backend.EndFrame();return active;
        }
        Check(!Frame() && interval==0,"first replacement frame keeps fallback while stock HDR initializes");
        Check(Frame() && interval==100,"actual shared volume owner resumes on the replacement device: "+Property("Shaders.VolumetricMediumRegistry","StatusLine"));
        object reject=Call("Buffers.BufferCatalog","Active","volumeReactive");var rejectPixels=new byte[64*64*4];read.Invoke(backend,new object[]{Id(reject),rejectPixels});
        double coverage=1-Math.Exp(-2),expected=Math.Max(coverage,2*Math.Min(coverage,1-coverage));
        Check(Enumerable.Range(0,64*64).All(i=>Math.Abs(BitConverter.ToSingle(rejectPixels,i*4)-expected)<.015),"recreated volume rejection matches the first-frame analytic oracle");
        foreach(string name in new[]{"litMips","occupancy","pointShadowAtlas","playerDepth"}) {
            object image=Call("Buffers.BufferCatalog","Active",name);Check(image!=null && image.GetType().GetProperty("IsAvailable").GetValue(image) is true,"recreated producer publishes "+name);
        }
        var occupancy=new byte[512*512*4];read.Invoke(backend,new object[]{Id(Call("Buffers.BufferCatalog","Active","occupancy")),occupancy});
        Check(occupancy.All(v=>v==0),"recreated empty-world occupancy contains no old-device stamps");
        var player=new byte[64*64*4];read.Invoke(backend,new object[]{Id(Call("Buffers.BufferCatalog","Active","playerDepth")),player});
        Check(Enumerable.Range(0,64*64).All(i=>BitConverter.ToSingle(player,i*4)==100000),"recreated spectator depth is cleared across every pixel");
        var atlas=new byte[192*33*16];read.Invoke(backend,new object[]{Id(Call("Buffers.BufferCatalog","Active","pointShadowAtlas")),atlas});
        Check(Enumerable.Range(192*4,192*32*4).All(i=>BitConverter.ToSingle(atlas,i*4)==100000) && BitConverter.ToSingle(atlas,263*4)==0,"recreated spectator atlas has cleared faces and an invalid character header");
        Check(backend.GetStats().ValidationErrors==0,"recreated device reports no validation errors");
        void Resize(int width,int height){outputWidth=width;outputHeight=height;backend.Resize((uint)width,(uint)height,0);}
        int resizeChecks=VolumeResizeRegression(core,backend,Frame,Resize,()=>interval);checks+=resizeChecks;
        Call("Shaders.VolumetricMediumRegistry","RequestRenderer",id,false);Call("Shaders.VolumetricMediumRegistry","SetEnabled",id,false);Call("Buffers.BufferCatalog","UnregisterLifetime","fixture.epoch");
        int packChecks=RealPackVolumes(core,root,assets,backend,Frame);checks+=packChecks;
        checks+=SnapshotRegression(core,assets,output,backend,library,projection,light);
        checks+=StageBindingFacadeContracts(core);
        checks+=EarlyOwnedRegression(core,assets,output,backend,projection,light);
        checks+=OwnedStageRegression(core,assets,output,backend,projection,light);
        checks+=PostprocessGenerationRegression(core,assets,output,backend,library,projection,light);
        checks+=LightingGenerationRegression(core,assets,output,backend,library,projection,light);
        checks+=NativeBindingRegression(core,assets,output,backend,projection,light);
        checks+=CatalogLifecycleRegression(core,backend,projection,light);
        checks+=LateColorHistoryRegression(core,assets,output,backend,projection,light);
        checks+=ClassicNullRegression(core,assets,output,backend,projection,light);
        var finalStats=backend.GetStats();
        Check(finalStats.ValidationErrors==0,"entire recreated volume/resize/provider/snapshot lane reports no validation errors");
        Console.WriteLine("recreated volume lane validation enabled="+finalStats.ValidationEnabled);
        return checks;
    }
}
