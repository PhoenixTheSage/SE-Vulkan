using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate uint CatalogMaskFn(IntPtr context);
    static int AnomalyTransaction(string root,string output,NativeBackend backend,VulkanScene scene,VulkanVoxels voxels,MatrixD projection,MyEnvironmentLightData light)
    {
        int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        string anomaly=Path.GetFullPath(Path.Combine(root,"../Anomaly")),assets=Path.Combine(anomaly,"Assets");
        Environment.SetEnvironmentVariable("SE_VULKAN_ASSET_DIR",Path.Combine(root,"Assets"));
        var core=Assembly.LoadFrom(Path.Combine(anomaly,"ClientPlugin/bin/Release/net10.0/Anomaly.dll"));
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(null);
        void SceneCall(string method,params object[] args)=>typeof(VulkanScene).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scene,args);
        typeof(GuestBridge).GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{backend,scene});
        var host=core.GetType("ClientPlugin.Backend.RenderHost",true);
        host.GetField("committed",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,true);
        host.GetField("choice",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,1);
        Check(Call("Backend.GuestDevice","Synchronize") is true,"initial core device epoch synchronized");
        Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);
        Call("Backend.GuestOwned","Register",assets);
        string provider=Path.Combine(output,"TransactionMedium.hlsli");
        File.WriteAllText(provider,"AnomalyMediumSample EvaluateMedium(float3 p) { AnomalyMediumSample m=AnomalyEmptyMedium();m.extinction=.02;m.emission=float3(.04,.06,.08);return m; }");
        const string id="fixture.live.volume";
        Check(Call("Shaders.VolumetricMediumRegistry","Register",id,provider,Array.Empty<string>()) is true,"actual Anomaly core registers transaction provider");
        Call("Shaders.VolumetricMediumRegistry","SetParameters",id,Array.Empty<float>(),new[]{-1e8,-1e8,-1e8,1e8,1e8,1e8},new float[3]);
        Call("Shaders.VolumetricMediumRegistry","SetEnabled",id,true);Call("Shaders.VolumetricMediumRegistry","RequestRenderer",id,true);Call("Shaders.VolumetricMediumRegistry","Configure",0,100f,0);
        float interval=-1;Call("Shaders.VolumetricMediumRegistry","SetIntervalCallback",id,(Action<float>)(v=>interval=v));
        Call("Backend.GuestVolumes","PreparePrograms",assets);Call("Backend.GuestCompiler","CompilePending",assets);
        var programs=Call("Backend.GuestPassList","Snapshot");
        foreach(object p in (System.Collections.IEnumerable)programs)Check((ulong)p.GetType().GetField("Pipeline").GetValue(p)!=0,"actual core pipeline ready: "+p.GetType().GetField("Id").GetValue(p)+" "+p.GetType().GetField("Error").GetValue(p));
        var snapshotType=core.GetType("ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot+Snapshot",true);
        var gridType=core.GetType("ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot+Grid",true);
        void FreshRooms()
        {
            object snapshot=Activator.CreateInstance(snapshotType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{Array.CreateInstance(gridType,0),true,"Controlled empty world",Environment.TickCount},null);
            core.GetType("ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot",true).GetField("current",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,snapshot);
        }
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.NonPublic|BindingFlags.Instance);
        IntPtr dll=NativeLibrary.Load(Path.Combine(NativeBackend.AssetsRoot,"Native/win-x64/se_vulkan_native.dll"));
        var mask=Marshal.GetDelegateForFunctionPointer<CatalogMaskFn>(NativeLibrary.GetExport(dll,"sevk_guest_catalog_mask"));
        IntPtr context=(IntPtr)typeof(NativeBackend).GetField("context",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(backend);
        MatrixD guestView=MatrixD.Identity;byte[] lastGuestFrame=new byte[344];
        bool Frame(bool submit,bool occluder=false,Action afterSubmit=null)
        {
            typeof(GuestBridge).GetMethod("BeginScene",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{backend,scene});
            if(submit)Call("Backend.GuestVolumeShadows","Prepare",new double[3],new[]{0f,1f,0f});
            backend.BeginPacketCollection();scene.Draw(backend);SceneCall("CollectGuestSceneDepth",voxels);
            if(occluder)backend.AddWorldPacket(0,1,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),flags:512);
            var viewProjection=guestView*projection;
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,guestView,projection,64,64,light),NativeBackend.MatrixValues(viewProjection),NativeBackend.MatrixValues(MatrixD.Invert(viewProjection)));
            Check(backend.BeginFrame(viewProjection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"transaction real world frame begins");
            GuestBridge.TryGetFrame(lastGuestFrame);
            if(submit){FreshRooms();Call("Backend.GuestBridgeClient","Submit",programs);}
            afterSubmit?.Invoke();
            bool active=Property("Backend.GuestVolumes","Active") is true;
            Console.WriteLine("transaction before EndFrame: active="+active+"; "+Property("Shaders.VolumetricMediumRegistry","StatusLine"));
            backend.EndFrame();return active;
        }
        Frame(false);var baselineHdr=new byte[64*64*8];read.Invoke(backend,new object[]{0xFFFF000000000001UL,baselineHdr});
        bool committed=Frame(true);
        // First-use allocation can outlive the injected snapshot's 250 ms
        // freshness window. Permit only that diagnosed cold-start fallback;
        // a new complete snapshot on the next frame must recover. Production
        // snapshot freshness and interval cancellation remain unchanged.
        if(!committed && ((string)Property("Shaders.VolumetricMediumRegistry","StatusLine")).Contains("stale simulation snapshot"))
            committed=Frame(true);
        Check(committed,"actual core commits complete volume transaction: "+Property("Shaders.VolumetricMediumRegistry","StatusLine"));
        Check(interval==100,"provider callback receives exact committed near interval");
        Check(mask(context)==7,"native current frame contains velocity, volume reject and host reactive products");
        object product=Call("Buffers.BufferCatalog","Active","volumeReactive");Check(product!=null && product.GetType().GetProperty("IsAvailable").GetValue(product) is true,"actual core exposes current volume catalog product");
        ulong reactive=(ulong)product.GetType().GetProperty("ImageId").GetValue(product);var bytes=new byte[64*64*4];read.Invoke(backend,new object[]{reactive,bytes});
        var routeProgram=Activator.CreateInstance(core.GetType("ClientPlugin.Backend.GuestProgram",true));
        var srvField=routeProgram.GetType().GetField("Srvs");var routes=(System.Collections.IList)Activator.CreateInstance(srvField.FieldType);srvField.SetValue(routeProgram,routes);
        var route=Activator.CreateInstance(core.GetType("ClientPlugin.Backend.SrvBind",true));route.GetType().GetField("Slot").SetValue(route,7);route.GetType().GetField("Name").SetValue(route,"volumeReactive");routes.Add(route);
        for(int mark=1;mark<=6;mark++) {
            routeProgram.GetType().GetField("Mark").SetValue(routeProgram,mark);
            var routed=(ulong[])core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("ImagesFor",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{routeProgram,0UL,5});
            Check(routed[7]==(mark<=2?0UL:reactive),"volume catalog publication matches DX11 consumer stage "+mark);
        }
        double coverage=1-Math.Exp(-2),expected=Math.Max(coverage,2*Math.Min(coverage,1-coverage));
        Check(Enumerable.Range(0,64*64).All(i=>Math.Abs(BitConverter.ToSingle(bytes,i*4)-expected)<.015),"live transaction reject mask matches analytic uniform medium");
        var hostMask=new byte[64*64*4];read.Invoke(backend,new object[]{0xFFFF00000000000CUL,hostMask});
        Check(Enumerable.Range(0,64*64).All(i=>Math.Abs(hostMask[i*4]/255f-expected)<.02),"host reactive merge samples the current pinned volume mask");
        object color=core.GetType("ClientPlugin.Backend.GuestVolumes",true).GetField("color",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        ulong colorId=(ulong)color.GetType().GetField("Id",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(color);var colors=new byte[64*64*8];read.Invoke(backend,new object[]{colorId,colors});
        Check(Enumerable.Range(0,64*64).All(i=>Math.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(colors,i*8))-2*coverage)<.02),"live transaction scattering matches analytic emission/transmittance");
        var composedHdr=new byte[64*64*8];read.Invoke(backend,new object[]{0xFFFF000000000001UL,composedHdr});
        Check(Enumerable.Range(0,64*64*4).All(i=>{
            float before=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(baselineHdr,i*2)),after=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(composedHdr,i*2));
            double wanted=i%4==3?before:(i%4+2)*coverage+before*Math.Exp(-2);return Math.Abs(after-wanted)<.025;
        }),"actual linear HDR composite equals scene times transmittance plus scattering and preserves alpha");
        Check(Frame(true),"consecutive shared volume frame commits history resources");
        read.Invoke(backend,new object[]{reactive,bytes});
        Check(BitConverter.ToSingle(bytes,(32*64+32)*4)<expected-.2,"accepted froxel history reduces current center-pixel rejection");
        guestView=MatrixD.CreateLookAt(Vector3D.Zero,new Vector3D(-15,-11,19),Vector3D.Up);
        Check((guestView*projection).M11<0 && Frame(true),"rotated overview camera with negative view-projection diagonal still commits shared volumes");
        Check(Math.Abs(BitConverter.ToSingle(lastGuestFrame,60)-projection.M11)<1e-6 && Math.Abs(BitConverter.ToSingle(lastGuestFrame,64)-projection.M22)<1e-6,"guest projection scales come from the unrotated stock projection matrix");
        guestView=MatrixD.Identity;Check(Frame(true),"shared volume frame recovers after returning from the rotated camera");
        Call("Shaders.VolumetricMediumRegistry","SetIntervalCallback",id,(Action<float>)(v=>{if(v>0)throw new InvalidOperationException("controlled interval activation failure");}));
        Check(!Frame(true),"interval callback failure cancels shared transaction");
        Check((mask(context)&2)==0 && Call("Buffers.BufferCatalog","Active","volumeReactive")==null,"failed transaction leaves no current reactive product");
        Call("Shaders.VolumetricMediumRegistry","SetIntervalCallback",id,(Action<float>)(v=>interval=v));
        Check(Frame(true) && interval==100 && mask(context)==7,"shared transaction recovers after callback failure");
        Check(product.GetType().GetProperty("IsAvailable").GetValue(product) is false,"older product handle does not become valid when a new frame commits");
        Frame(false);Check(mask(context)==0,"unsubmitted next frame cannot reuse pinned catalog products or old passes");
        Check(product.GetType().GetProperty("IsAvailable").GetValue(product) is false,"retained catalog handle expires on the next frame");
        Check(Frame(true,true),"opaque receiver shared volume frame commits");
        var clippedColor=new byte[64*64*8];read.Invoke(backend,new object[]{colorId,clippedColor});
        Check((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(clippedColor,(32*64+32)*8))<coverage,"current main depth truncates volume scattering before the opaque receiver");
        object depthProduct=Call("Buffers.BufferCatalog","Active","volumeRepresentativeDepth");
        ulong depthId=(ulong)depthProduct.GetType().GetProperty("ImageId").GetValue(depthProduct);var depths=new byte[6*6*4];read.Invoke(backend,new object[]{depthId,depths});
        Check(BitConverter.ToSingle(depths,(3*6+3)*4)<10 && BitConverter.ToSingle(depths,0)>90,"froxel representative depth distinguishes opaque receiver and clear background");
        // Exercise the real motion overlay with independent camera/volume
        // vectors: only confident, stable volume pixels replace camera motion.
        var create=typeof(NativeBackend).GetMethod("GuestCreateImage",BindingFlags.Instance|BindingFlags.NonPublic);
        var update=typeof(NativeBackend).GetMethod("GuestUpdateImage",BindingFlags.Instance|BindingFlags.NonPublic);
        var submit=typeof(NativeBackend).GetMethod("GuestSubmit2",BindingFlags.Instance|BindingFlags.NonPublic);
        var replay=typeof(NativeBackend).GetMethod("GuestReplayNow",BindingFlags.Instance|BindingFlags.NonPublic);
        object shader=core.GetType("ClientPlugin.Backend.GuestVolumes",true).GetField("velocityMerge",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        ulong velocityPipeline=(ulong)shader.GetType().GetField("Pipeline").GetValue(shader);
        byte[] HalfPixels(params float[] values){var data=new byte[64*64*values.Length*2];for(int i=0;i<64*64*values.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)values[i%values.Length])),0,data,i*2,2);return data;}
        const ulong cameraId=0xFFFF00000000000AUL,volumeId=6101,velocityId=6102;
        create.Invoke(backend,new object[]{volumeId,64,64,1,1});create.Invoke(backend,new object[]{velocityId,64,64,1,3});
        update.Invoke(backend,new object[]{cameraId,HalfPixels(1.5f,-2.5f)});
        foreach(var test in new (float Reactive,float Coverage,float X,float Y)[]{(.05f,.99f,3,4),(.2f,.99f,1.5f,-2.5f),(.05f,.5f,1.5f,-2.5f)}) {
            update.Invoke(backend,new object[]{volumeId,HalfPixels(3,4,test.Reactive,test.Coverage)});GuestBridge.Reset();
            Check(submit.Invoke(backend,new object[]{4,5,64,64,1,velocityPipeline,new[]{velocityId},null,new[]{cameraId,volumeId},2048}) is true,"motion overlay packet accepted");
            replay.Invoke(backend,new object[]{4});var velocities=new byte[64*64*4];read.Invoke(backend,new object[]{velocityId,velocities});
            Check(Enumerable.Range(0,64*64).All(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(velocities,i*4))==test.X && (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(velocities,i*4+2))==test.Y),"motion overlay preserves camera fallback or selects confident volume velocity");
        }
        // Force distinct camera and confident volume motion after preparation.
        // An ordinary BeforeTonemap consumer must see the volume overlay already.
        object AddProbe(string suffix,string source,int contract) {
            string file=Path.Combine(output,suffix+".hlsl");File.WriteAllText(file,source);string key="fixture.motion."+suffix;
            Call("Backend.GuestPassList","AddProgram",key,"BeforeTonemap",file,"PublishOnly");
            var p=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(v=>(string)v.GetType().GetField("Id").GetValue(v)==key);
            p.GetType().GetField("ResourceContract").SetValue(p,contract);p.GetType().GetField("Enabled").SetValue(p,false);return p;
        }
        object fill=AddProbe("fill","cbuffer Values:register(b0){float4 Color;}float4 main(float4 p:SV_Position):SV_Target{return Color;}",0);
        object observe=AddProbe("observe","Texture2D<float2> V:register(t0);float4 main(float4 p:SV_Position):SV_Target{return float4(V.Load(int3((int2)p.xy,0)),0,1);}",1);
        Call("Backend.GuestCompiler","CompilePending",assets);
        ulong Pipe(object p)=>(ulong)p.GetType().GetField("Pipeline").GetValue(p);
        Check(Pipe(fill)!=0 && Pipe(observe)!=0,"real motion stage probes compile");
        object motionImage=core.GetType("ClientPlugin.Backend.GuestVolumes",true).GetField("motion",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        ulong motionId=(ulong)motionImage.GetType().GetField("Id",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(motionImage);
        const ulong observed=6201;create.Invoke(backend,new object[]{observed,64,64,1,1});
        var submitLegacy=typeof(NativeBackend).GetMethod("GuestSubmit",BindingFlags.Instance|BindingFlags.NonPublic);
        void Fill(ulong target,params float[] values){var data=new byte[16];Buffer.BlockCopy(values,0,data,0,16);submitLegacy.Invoke(backend,new object[]{3,4,64,64,Pipe(fill),target,data,Array.Empty<ulong>(),32});}
        Check(Frame(true,false,()=>{
            Fill(cameraId,1.5f,-2.5f,0,0);Fill(motionId,3,4,.05f,.99f);
            Check(submit.Invoke(backend,new object[]{4,5,64,64,1,Pipe(observe),new[]{observed},null,new[]{cameraId},0}) is true,"BeforeTonemap observer packet accepted");
        }),"real shared motion frame commits");
        var observedPixels=new byte[64*64*8];read.Invoke(backend,new object[]{observed,observedPixels});
        Check(Enumerable.Range(0,64*64).All(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(observedPixels,i*8))==3 && (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(observedPixels,i*8+2))==4),"ordinary BeforeTonemap consumers see confident volume velocity before fullscreen work");
        Call("Shaders.VolumetricMediumRegistry","RequestRenderer",id,false);Call("Shaders.VolumetricMediumRegistry","SetEnabled",id,false);
        NativeLibrary.Free(dll);return checks;
    }
}
