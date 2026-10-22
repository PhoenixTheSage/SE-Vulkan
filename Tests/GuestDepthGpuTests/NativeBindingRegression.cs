using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using ClientPlugin.Rendering.Scene;
using VRageRender.Messages;
using VRageMath;
using VRageRender;

partial class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int RawStageBindingsFn(IntPtr context,IntPtr rows,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int RawStageBindingStateFn(IntPtr context,IntPtr state);

    static int NativeBindingRegression(Assembly core,string assets,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var programs=core.GetType("ClientPlugin.Backend.GuestProgram",true);
        var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programs)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        string root=Path.Combine(output,"NativeBindingPack"),dir=Path.Combine(root,"Overlay/Lighting.Dir/LightDir.hlsl");
        Directory.CreateDirectory(Path.GetDirectoryName(dir));File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\"fixture.native.bindings\",\"exclusive\":[\"Lighting\"]}");
        foreach(string file in new[]{"Overlay/Post.FXAA/Fxaa.hlsl","Overlay/Post.HBAO/CoarseAO.hlsl","Overlay/Lighting.Point/LightPoint.hlsl","Overlay/Lighting.Spot/LightSpot.hlsl","Overlay/Post.Tonemap/Main.hlsl"}){string stale=Path.Combine(root,file);if(File.Exists(stale))File.Delete(stale);}
        string extras=File.ReadAllText(Path.Combine(assets,"Shaders/Anomaly/PassExtrasCb.hlsli")).Replace("register(MERGE(b, ANOMALY_EXTRAS_CB_SLOT))","register(b6)");
        void Register(string body){File.WriteAllText(dir,extras+"\n"+body);Call("Backend.GuestPassList","RegisterRoot","fixture.native.bindings",root,null);}
        void Request(string stage,string name,int slot)=>Call("Shaders.ShaderBindRegistry","RequestSrv",stage,name,slot);
        var publications=new Dictionary<string,object>();
        object Publish(string method,string name,params object[] args){
            if(publications.TryGetValue(name,out var old))old.GetType().GetMethod("Clear").Invoke(old,null);
            var buffer=Activator.CreateInstance(core.GetType("ClientPlugin.Buffers.PublishedBuffer",true));buffer.GetType().GetMethod(method).Invoke(buffer,args);
            Call("Buffers.BufferCatalog","Publish","fixture.native.bindings",name,buffer);publications[name]=buffer;return buffer;
        }
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;post.Settings.Data.ChromaticFactor=0;post.Settings.Data.VignetteStart=0;
        backend.SetHdrQuality(true);backend.SetLightingDiagnostics(true);backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(256,Array.Empty<NativeSpot>());
        int outputW=64,outputH=64,w=64,h=64;uint frameIndex=0;bool useFxaa=true;
        void Frame(bool pipelined=false,Action queued=null){
            Call("Backend.RenderHost","PrepareShaderGenerations");
            Check(Property("Backend.GuestPostprocessStages","Error")==null,"native binding generation prepares: "+Property("Backend.GuestPostprocessStages","Error"));
            backend.BeginPacketCollection();var size=backend.ResolveRenderResolution(outputW,outputH);w=size.X;h=size.Y;
            post.Configure(backend,w,h,useFxaa,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,w,h,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"native binding frame begins");
            Check(Call("Backend.GuestFrame","Capture") is true,"native binding frame captures immutable environment");
            frameIndex=(uint)Call("Backend.GuestFrame","U32",16);
            Call("Backend.SceneView","Set",w,h,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);queued?.Invoke();
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
        }
        void Pixel(float red,float green,float blue,string message){
            var values=backend.ReadbackLightingStage(0,w,h);
            Console.WriteLine("binding HDR "+values[0]+","+values[1]+","+values[2]);
            Check(Enumerable.Range(0,w*h).All(i=>Math.Abs(values[i*4]-red)<.004&&Math.Abs(values[i*4+1]-green)<.004&&Math.Abs(values[i*4+2]-blue)<.004),message);
        }
        var caps=backend.GetStageBindingState();Check((caps.Flags&1)!=0,"test device enables genuine nullDescriptor resources");
        var frameInputs=typeof(GuestBridge).GetField("frameInputs",stat);object oldInputs=frameInputs.GetValue(null);
        object AddPass(string id,string name,bool history){
            string file=Path.Combine(output,id+".hlsl");File.WriteAllText(file,"cbuffer Inputs:register(b7){float4 Value;}float4 main(float4 p:SV_Position):SV_Target{return Value;}");
            Call("Backend.GuestPassList","AddProgram",id,"BeforeTonemap",file,"PublishOnly");
            var pass=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)p.GetType().GetField("Id").GetValue(p)==id);
            if(history)Call("Backend.GuestPassList","SetHistory",id,name);else pass.GetType().GetField("OutputName").SetValue(pass,name);
            Call("Backend.GuestCompiler","CompilePending",assets);return pass;
        }
        var context=(IntPtr)typeof(NativeBackend).GetField("context",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(backend);
        IntPtr Symbol(string name)=>(IntPtr)typeof(NativeBackend).GetMethod("GetSymbol",stat).Invoke(null,new object[]{name});
        var rawRows=Marshal.GetDelegateForFunctionPointer<RawStageBindingsFn>(Symbol("sevk_set_stage_bindings"));
        var rawState=Marshal.GetDelegateForFunctionPointer<RawStageBindingStateFn>(Symbol("sevk_get_stage_binding_state"));
        void RawContracts(){
            IntPtr memory=Marshal.AllocHGlobal(240);
            try{
                var bytes=new byte[240];void U(int at,uint value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,bytes,at,4);
                U(0,120);U(4,1);U(8,256);U(12,frameIndex);U(20,1);
                Buffer.BlockCopy(BitConverter.GetBytes(0xFFFF00000000000AUL),0,bytes,32,8);
                foreach(var bad in new[]{new uint[]{0,119},new uint[]{4,2},new uint[]{8,1000},new uint[]{12,frameIndex+1},new uint[]{20,2},new uint[]{28,1}}){
                    uint old=BitConverter.ToUInt32(bytes,(int)bad[0]);U((int)bad[0],bad[1]);Marshal.Copy(bytes,0,memory,240);
                    Check(rawRows(context,memory,1)!=0,"native binding row refuses malformed size/version/stage/frame/mask/reserved field at "+bad[0]);U((int)bad[0],old);
                }
                Buffer.BlockCopy(bytes,0,bytes,120,120);Marshal.Copy(bytes,0,memory,240);
                Check(rawRows(context,memory,2)!=0,"native binding rows reject duplicate owner IDs atomically");
                Check(rawRows(context,IntPtr.Zero,1)!=0&&rawRows(context,IntPtr.Zero,83)!=0,"native binding rows reject missing payload and excessive row count");
                Check(backend.GetStageBindingState().Rows==1,"invalid native packets retain the previously queued valid row");
                Check(rawRows(context,memory,1)==0,"raw size/version-checked native binding row accepted");
                Marshal.WriteInt64(memory,32,0); // Native recording must retain its own copy.
                Marshal.WriteInt32(memory,0,47);Marshal.WriteInt32(memory,4,1);
                Check(rawState(context,memory)!=0,"native binding status rejects a short ABI packet");
                Marshal.WriteInt32(memory,0,48);Marshal.WriteInt32(memory,4,2);
                Check(rawState(context,memory)!=0,"native binding status rejects an unknown ABI version");
            }
            finally{Marshal.FreeHGlobal(memory);}
        }
        object historyPass=null,currentPass=null;
        try{
            Frame();var stock=backend.ReadbackLightingStage(0,w,h);
            Register("float4 __pixel_shader():SV_Target{return float4(AnomalyLightingRenderSize.x,AnomalyLightingHasVelocity,AnomalyLightingFrameIndex%8,1);}");
            bool checkWire=true;
            foreach(float fraction in new[]{1f,.5f,.75f,1f}){
                backend.SetRenderFraction(fraction);Frame(fraction!=1,checkWire?(Action)RawContracts:null);checkWire=false;Pixel(w,1,frameIndex%8,"b6 contains full raster dimensions, actual current velocity availability and the same frame index at DRS "+fraction);
            }
            outputW=95;outputH=73;backend.Resize(95,73,0);backend.SetRenderFraction(.5f);Frame(true);Pixel(w,1,frameIndex%8,"b6 keeps renderer raster dimensions at odd output sizes");
            outputW=outputH=64;backend.Resize(64,64,0);backend.SetRenderFraction(1);
            typeof(GuestBridge).GetMethod("SetFrameInputs",stat).Invoke(null,new object[]{Vector3D.Zero,new Vector3(2,3,4),new Vector3(0,0,-2),2.5f,.2f});
            Register("float4 __pixel_shader():SV_Target{return float4(AnomalySunColor.x,AnomalySunDiffuse,AnomalySunToward.z,1);}");Frame();Pixel(2,2.5f,1,"native b6 uses raw managed light color, diffuse and normalized toward-sun direction");
            frameInputs.SetValue(null,oldInputs);
            const string imageName="fixture.native.binding.image",dataName="fixture.native.binding.data";
            Request("Lighting",imageName,6);Request("Lighting",dataName,7);
            Publish("PublishRgba8",imageName,Enumerable.Range(0,4).SelectMany(_=>new byte[]{64,128,192,255}).ToArray(),2,2);
            Publish("PublishStructuredFloats",dataName,new float[]{2,3,4,5,6,7,8,9},16);
            Register("Texture2D<float4> Image:register(t6);StructuredBuffer<float4> Data:register(t7);float4 __pixel_shader():SV_Target{uint w,h,n,s;Image.GetDimensions(w,h);Data.GetDimensions(n,s);return float4(Image.Load(int3(0,0,0)).r,Data[1].z,w+n,1);}");
            Frame(true);Pixel(64/255f,8,4,"native lighting reads catalog 2D texture and per-frame structured buffer");
            Check(backend.GetStageBindingState().Resolved>=2&&backend.GetStageBindingState().Mismatched==0,"native binding status distinguishes actual descriptor resolution from registration");
            Publish("PublishRgba8",imageName,new byte[]{0,0,0,255,255,0,0,255,0,255,0,255,255,255,255,255},2,2);
            Register("Texture2D<float4> Image:register(t6);SamplerState Point:register(s1);float4 __pixel_shader():SV_Target{return Image.SampleLevel(Point,float2(1.25,.5),0);}");
            Frame();Pixel(1,1,1,"native lighting supports the stock s1 point-clamp sampler with catalog textures");
            Register("Texture2D<float4> Image:register(t6);SamplerState Linear:register(s2);float4 __pixel_shader():SV_Target{return Image.SampleLevel(Linear,float2(1.25,.5),0);}");
            Frame();Pixel(1,.5f,.5f,"native lighting preserves the stock s2 linear-clamp sampler");
            Call("Buffers.BufferCatalog","Unpublish","fixture.native.bindings",imageName);Call("Buffers.BufferCatalog","Unpublish","fixture.native.bindings",dataName);
            Register("Texture2D<float4> Image:register(t6);StructuredBuffer<float4> Data:register(t7);float4 __pixel_shader():SV_Target{uint w,h,n,s;Image.GetDimensions(w,h);Data.GetDimensions(n,s);return float4(w,h,n+Image.Load(int3(0,0,0)).a,1)+Image.Load(int3(0,0,0))+Data[0];}");Frame();Pixel(0,0,0,"missing 2D/buffer descriptors return zero dimensions and values, matching DX11 WARP");
            Register("StructuredBuffer<float4> Data:register(t7);float4 __pixel_shader():SV_Target{uint n,s;Data.GetDimensions(n,s);return float4(n,s,Data[9].x,1);}");Frame(true);Pixel(0,16,0,"null structured dimensions and compile-time stride match independent DX11 execution");
            foreach(string type in new[]{"Texture3D<float4>","Texture2DArray<float4>"}){
                Register(type+" Image:register(t6);float4 __pixel_shader():SV_Target{uint w,h,d;Image.GetDimensions(w,h,d);return float4(w,h,d,1)+Image.Load(int4(0,0,0,0));}");
                Frame();Pixel(0,0,0,"null "+type+" dimensions and loads match DX11 WARP");
            }
            Register("Buffer<float4> Data:register(t7);float4 __pixel_shader():SV_Target{uint n;Data.GetDimensions(n);return float4(n,Data[0].xyz);}");Frame();Pixel(0,0,0,"null typed buffer matches DX11 WARP");
            Publish("PublishVolumeRgba8",imageName,Enumerable.Range(0,8).SelectMany(_=>new byte[]{100,0,0,255}).ToArray(),2,2,2);
            Register("Texture3D<float4> Image:register(t6);float4 __pixel_shader():SV_Target{uint w,h,d;Image.GetDimensions(w,h,d);return float4(Image.Load(int4(0,0,0,0)).r,w,h+d,1);}");Frame(true);Pixel(100/255f,2,4,"native catalog bindings retain genuine 3D view dimensions");
            Register("Texture2DArray<float4> Image:register(t6);float4 __pixel_shader():SV_Target{uint w,h,d;Image.GetDimensions(w,h,d);return float4(w,h,d,1)+Image.Load(int4(0,0,0,0));}");Frame();Pixel(0,0,0,"mismatched catalog view dimensions fail closed through a typed null");
            Check(backend.GetStageBindingState().Mismatched>0,"native status exposes incompatible catalog resource types");
            Check(Call("Buffers.CatalogImages","EnsureColorTarget","fixture.native.bindings",imageName,1,1,10) is true,"native lighting target request accepted before frame allocation");
            Call("Buffers.CatalogImages","ClearColorTarget",imageName,2f,3f,4f,-1f);
            Register("Texture2D<float4> Image:register(t6);float4 __pixel_shader():SV_Target{uint w,h;Image.GetDimensions(w,h);float4 v=Image.Load(int3(0,0,0));return float4(v.r,v.a,w,1);}");
            Frame();Pixel(2,-1,1,"global FP16 catalog clear produces HDR and alpha -1 before native lighting");
            Frame(true);Pixel(2,-1,1,"persistent catalog targets retain initialized contents without a new producer");
            Call("Buffers.BufferCatalog","Unpublish","fixture.native.bindings",imageName);
            Publish("PublishRgba8",imageName,new byte[]{128,64,32,255},1,1);
            string resourceLight=extras+"\nTexture2D<float4> Image:register(t6);float4 __pixel_shader():SV_Target{return float4(Image.Load(int3(0,0,0)).r,AnomalyLightingHasVelocity,AnomalyLightingRenderSize.x*.01,1);}";
            string point=Path.Combine(root,"Overlay/Lighting.Point/LightPoint.hlsl");Directory.CreateDirectory(Path.GetDirectoryName(point));File.WriteAllText(point,resourceLight);
            Register("float4 __pixel_shader():SV_Target{return 0;}");Frame(true);Pixel(128/255f,1,.64f,"point lighting binds its own catalog/extras descriptor generation");
            File.Delete(point);
            string spot=Path.Combine(root,"Overlay/Lighting.Spot/LightSpot.hlsl");Directory.CreateDirectory(Path.GetDirectoryName(spot));File.WriteAllText(spot,resourceLight);
            foreach(bool shadow in new[]{false,true}){
                backend.SetLights(new[]{new NativeLight{PositionRange=new float[]{0,0,0,10},ColorIntensity=new float[]{1,1,1,1},DirectionCone=new float[]{0,0,-1,.5f},Flags=1}});
                backend.SetSpots(256,new[]{new NativeSpot{Light=0,Flags=shadow?1u:0u,Projection=NativeBackend.MatrixValues(MatrixD.CreatePerspectiveFieldOfView(Math.PI*.65,1,.1,10))}});
                Register("float4 __pixel_shader():SV_Target{return 0;}");Frame(true);Pixel(128/255f,1,.64f,"spot lighting binds catalog/extras with dynamic spot data, shadow="+shadow);
                var state=backend.GetPostprocessGeneration(shadow?259u:258u);
                Check(state.LastFrame==frameIndex&&state.Executions>0&&state.LastCompletion<=state.Completed,"selected spot resource generation records completed execution");
            }
            backend.SetHdrQuality(false);Frame(true);Pixel(.5f,1,.625f,"catalog/extras packed HDR conversion matches independent DX11 WARP and HAL");
            backend.SetHdrQuality(true);Frame();Pixel(128/255f,1,.64f,"catalog/extras layout survives FP16 HDR generation replacement");
            backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(256,Array.Empty<NativeSpot>());File.Delete(spot);
            const string historyName="fixture.native.binding.history",currentName="fixture.native.binding.current";
            Request("Lighting",historyName,8);Request("Lighting",currentName,9);
            historyPass=AddPass("fixture.binding.history.producer",historyName,true);currentPass=AddPass("fixture.binding.current.producer",currentName,false);
            Register("Texture2D<float4> Previous:register(t8);Texture2D<float4> Late:register(t9);float4 __pixel_shader():SV_Target{uint w,h;Late.GetDimensions(w,h);return float4(Previous.Load(int3(0,0,0)).r,Previous.Load(int3(0,0,0)).a,w+Late.Load(int3(0,0,0)).r,1);}");
            for(int frame=0;frame<4;frame++){
                historyPass.GetType().GetField("Uniforms").SetValue(historyPass,new float[]{frame+1,0,0,-1});
                currentPass.GetType().GetField("Uniforms").SetValue(currentPass,new float[]{9,0,0,1});
                Frame((frame&1)!=0);Pixel(frame,frame==0?0:-1,0,"native lighting sees exact previous history and never stale late current-frame output at frame "+frame);
            }
            string fxaa=Path.Combine(root,"Overlay/Post.FXAA/Fxaa.hlsl");Directory.CreateDirectory(Path.GetDirectoryName(fxaa));
            Publish("PublishRgba8",imageName,new byte[]{64,128,192,255},1,1);
            Request("Post.FXAA",imageName,6);
            File.WriteAllText(fxaa,"Texture2D<float4> Image:register(t6);float4 __pixel_shader():SV_Target{return float4(Image.Load(int3(0,0,0)).rgb,1);}");
            Action<object> change=_=>Request("Post.FXAA",imageName,7);
            Call("Shaders.OwnedPassRegistry","RegisterPortable","fixture.binding.mutate","AfterLighting",-1000,1,change,0);
            Register("float4 __pixel_shader():SV_Target{return 0;}");Frame();
            var first=backend.Readback(64,64);Check(Math.Abs(first[0]-64)<=1&&Math.Abs(first[1]-128)<=1&&Math.Abs(first[2]-192)<=1,"native binding requests freeze before portable callbacks mutate registration");
            Frame(true);var second=backend.Readback(64,64);Check(second[0]==0&&second[1]==0&&second[2]==0,"changed binding registration takes effect on the next frame only");
            Call("Shaders.OwnedPassRegistry","Unregister","fixture.binding.mutate");File.Delete(fxaa);
            string coarse=Path.Combine(root,"Overlay/Post.HBAO/CoarseAO.hlsl");Directory.CreateDirectory(Path.GetDirectoryName(coarse));File.WriteAllText(coarse,extras+"\nfloat __pixel_shader():SV_Target{return (AnomalyLightingRenderSize.x+AnomalyLightingHasVelocity)*.01;}");
            post.Hbao=MyHBAOData.Default;post.Hbao.Enabled=true;post.Hbao.BlurEnable=false;post.Hbao.PowerExponent=1;
            Register("float4 __pixel_shader():SV_Target{return 0;}");
            foreach(bool clamp in new[]{true,false}){
                post.Hbao.DepthClampToEdge=clamp;Frame(true);var ao=backend.ReadbackLightingInput(0,w,h);
                Check(ao.All(v=>Math.Abs(v-166)<=2),"HBAO extras use full raster size and current velocity in clamp/border mode "+clamp);
            }
            File.Delete(coarse);post.Hbao.Enabled=false;
            Request("Post.FXAA","historyColor",8);
            File.WriteAllText(fxaa,"Texture2D<float4> Previous:register(t8);float4 __pixel_shader():SV_Target{return float4(Previous.Load(int3(0,0,0)).rgb,1);}");
            Register("float4 __pixel_shader():SV_Target{return float4((AnomalyLightingFrameIndex%16)*.01,0,0,1);}");
            float previous=0;
            for(int age=0;age<3;age++){Frame(true);var color=backend.Readback(64,64);Check(Math.Abs(color[0]-previous*255)<=2&&color[1]==0&&color[2]==0,"native post consumer sees reserved historyColor from the preceding frame, age step "+age);previous=(frameIndex%16)*.01f;}
            Call("Backend.GuestPassList","SetEnabled","anomaly.owned.HistoryCopy",false);Frame();Check(backend.Readback(64,64)[0]==0,"disabled reserved color-history producer is unavailable to native consumers");
            Call("Backend.GuestPassList","SetEnabled","anomaly.owned.HistoryCopy",true);Frame();Check(backend.Readback(64,64)[0]==0,"reserved color-history rejects a skipped producer frame");
            previous=(frameIndex%16)*.01f;Frame(true);Check(Math.Abs(backend.Readback(64,64)[0]-previous*255)<=2,"reserved color-history recovers after one valid producer frame");
            File.Delete(fxaa);
            string tone=Path.Combine(root,"Overlay/Post.Tonemap/Main.hlsl");Directory.CreateDirectory(Path.GetDirectoryName(tone));
            Request("Post.Tonemap",imageName,9);Publish("PublishRgba8",imageName,new byte[]{64,128,192,255},1,1);
            File.WriteAllText(tone,extras+"\nTexture2D<float4> Image:register(t9);RWTexture2D<unorm float4> Output:register(u0);[numthreads(8,8,1)]void __compute_shader(uint3 at:SV_DispatchThreadID){uint w,h;Output.GetDimensions(w,h);if(at.x>=w||at.y>=h)return;Output[at.xy]=float4(AnomalyLightingRenderSize.x/128,AnomalyLightingHasVelocity,Image.Load(int3(0,0,0)).r,1);}");
            Register("float4 __pixel_shader():SV_Target{return 0;}");
            foreach(bool enabled in new[]{true,false})foreach(bool aa in new[]{false,true}){
                post.Settings.EnableTonemapping=enabled;useFxaa=aa;Frame(true);var color=backend.Readback(64,64);
                Check(Math.Abs(color[0]-128)<=2&&color[1]==255&&Math.Abs(color[2]-64)<=2,"compute tonemap reads native catalog and b6 resources, tone="+enabled+" FXAA="+aa);
                var state=backend.GetPostprocessGeneration(9u+(aa?1u:0u)+(enabled?0u:2u));
                Check(state.LastFrame==frameIndex&&state.Executions>0&&state.LastCompletion<=state.Completed,"selected compute resource permutation executes and completes");
            }
            File.Delete(tone);post.Settings.EnableTonemapping=true;useFxaa=true;
            Call("Backend.GuestPassList","UnregisterRoot","fixture.native.bindings");Frame();Check(stock.SequenceEqual(backend.ReadbackLightingStage(0,w,h)),"unregister restores exact stock lighting after typed bindings and layout replacement");
            Check(backend.GetStats().ValidationErrors==0,"native catalog/extras bindings report zero core/synchronization errors");
        }
        finally{
            Call("Backend.GuestPassList","SetEnabled","anomaly.owned.HistoryCopy",true);
            Call("Shaders.OwnedPassRegistry","Unregister","fixture.binding.mutate");Call("Backend.GuestPassList","UnregisterRoot","fixture.native.bindings");
            Call("Backend.GuestPassList","UnregisterRoot","fixture.binding.history.producer");Call("Backend.GuestPassList","UnregisterRoot","fixture.binding.current.producer");
            foreach(var publication in publications){Call("Buffers.BufferCatalog","Unpublish","fixture.native.bindings",publication.Key);publication.Value.GetType().GetMethod("Clear").Invoke(publication.Value,null);}
            Call("Buffers.BufferCatalog","UnpublishAll","fixture.native.bindings");frameInputs.SetValue(null,oldInputs);backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(256,Array.Empty<NativeSpot>());backend.SetHdrQuality(true);backend.SetRenderFraction(1);backend.Resize(64,64,0);backend.SetLightingDiagnostics(false);
        }
        return checks;
    }
}
