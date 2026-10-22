using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int CatalogLifecycleRegression(Assembly core, NativeBackend backend, MatrixD projection, MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        const string owner="fixture.catalog.owner", other="fixture.catalog.other", name="fixture.catalog.color";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Active(string key)=>Call("Buffers.BufferCatalog","Active",key);
        ulong Id(object buffer)=>(ulong)buffer.GetType().GetProperty("ImageId").GetValue(buffer);
        object Texture(byte red){var value=Activator.CreateInstance(core.GetType("ClientPlugin.Buffers.PublishedBuffer",true));value.GetType().GetMethod("PublishRgba8").Invoke(value,new object[]{new byte[]{red,0,0,255},1,1});return value;}
        bool Publish(string pack,string key,object value)=>Call("Buffers.BufferCatalog","Publish",pack,key,value) is true;
        bool Ensure(string pack,string key)=>Call("Buffers.CatalogImages","EnsureColorTarget",pack,key,1,1,10) is true;
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        bool Exists(ulong id){try{read.Invoke(backend,new object[]{id,new byte[256]});return true;}catch(TargetInvocationException error) when(error.InnerException.Message.Contains("guest readback missing")){return false;}}
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        ulong readerPipeline=0;const ulong readerOutput=915001;
        var submit=typeof(NativeBackend).GetMethod("GuestSubmit",BindingFlags.Instance|BindingFlags.NonPublic);
        void Frame(bool pipelined=false,ulong sample=0){
            backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"catalog lifecycle frame begins");
            if(Call("Backend.GuestFrame","Capture") is not true)throw new Exception("Catalog lifecycle frame metadata was unavailable");Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);
            Call("Backend.GuestBridgeClient","Submit",new object[]{null});
            if(sample!=0)submit.Invoke(backend,new object[]{2,5,1,1,readerPipeline,readerOutput,null,new[]{sample},0});
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
        }
        var buffers=new List<object>();object Make(byte red){var b=Texture(red);buffers.Add(b);return b;}
        try{
            var committed=core.GetType("ClientPlugin.Backend.RenderHost",true).GetField("committed",stat);
            committed.SetValue(null,false);
            object pending;
            try{pending=Make(23);Check(Id(pending)==0&&Publish(owner,name+".pending",pending),"publication before renderer selection retains CPU payload without native allocation");}
            finally{committed.SetValue(null,true);}
            Call("Buffers.BufferCatalog","ApplyDeferred");Check(Id(pending)!=0,"pending publication uploads after renderer selection");
            ulong pendingId=Id(pending);Call("Buffers.BufferCatalog","Unpublish",owner,name+".pending");Frame();Check(!Exists(pendingId),"pending publication follows normal unpublish retirement");
            foreach(int format in new[]{0,1}){
                string key=name+".legacy"+format;
                Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,key,1,1,format) is true,"legacy catalog target format accepted: "+format);
                Call("Buffers.CatalogImages","ClearColorTarget",key,0f,0f,0f,format==1?-1f:0f);Frame();
                var bytes=new byte[8];read.Invoke(backend,new object[]{Id(Active(key)),bytes});
                Check(format==1?BitConverter.ToUInt16(bytes,6)==0xbc00:bytes[3]==0,"legacy target preserves format and negative-alpha clear: "+format);
            }
            Call("Buffers.BufferCatalog","UnpublishAll",owner);Frame();
            object first=Make(37),second=Make(81);ulong firstId=Id(first),secondId=Id(second);
            Check(firstId!=0&&secondId!=0,"catalog lifecycle fixture owns native uploads");
            Check(!Publish(null,name,first)&&!Publish(" ",name,first)&&!Publish(owner," ",first)&&!Publish(owner,name,null),"malformed publications refuse without ownership changes");
            foreach(string reserved in new[]{"velocity","linearDepth","hiZ","historyColor","reactiveMask","fullscreenIsolated","hdrColor","upscaledColor","avgLuminance","bloom","dirt","litMips","pointLights","tileIndices","historyDepth","occupancy","pointShadowAtlas","playerDepth"})
                Check(!Publish(owner,reserved.ToUpperInvariant(),first)&&!Ensure(owner,reserved),"reserved publication/target refuses: "+reserved);
            Check(Publish(" "+owner+" "," "+name+" ",first)&&ReferenceEquals(first,Active(name.ToUpperInvariant())),"publication trims ownership and matches catalog names without case");
            Check(!Publish(other,name.ToUpperInvariant(),second)&&!Ensure(other,name),"ordinary publication blocks another owner's upload and target");
            Call("Buffers.BufferCatalog","Unpublish",other,name);Check(ReferenceEquals(first,Active(name))&&Id(first)==firstId,"wrong-owner unpublish preserves upload");
            string file=Path.GetFullPath("TestResults/CatalogLifecycle/Read.hlsl");Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file,"Texture2D<float4> Source:register(t0);float4 main(float4 p:SV_Position):SV_Target{return Source.Load(int3(0,0,0));}");
            Call("Backend.GuestPassList","AddProgram","fixture.catalog.reader","AfterLighting",file,"PublishOnly");
            string assets=(string)core.GetType("ClientPlugin.Backend.RenderHost",true).GetField("assetFolder",stat).GetValue(null);Call("Backend.GuestCompiler","CompilePending",assets);
            var reader=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)p.GetType().GetField("Id").GetValue(p)=="fixture.catalog.reader");
            readerPipeline=(ulong)reader.GetType().GetField("Pipeline").GetValue(reader);
            typeof(NativeBackend).GetMethod("GuestCreateImage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(backend,new object[]{readerOutput,1,1,1,0});
            Check(readerPipeline!=0,"catalog lifetime GPU reader compiles through the owning host");
            Frame(true,firstId);Check(Publish(owner.ToUpperInvariant(),name.ToUpperInvariant(),second),"same owner replaces publication during pipelined lifecycle");
            Check(Id(first)==0&&((byte[])first.GetType().GetProperty("Rgba").GetValue(first))[0]==37,"replacement retires the native ID and retains cloned CPU payload");
            Frame();var copied=new byte[4];read.Invoke(backend,new object[]{readerOutput,copied});Check(copied[0]==37,"in-flight draw completes using the replaced image's original payload");
            Check(!Exists(firstId)&&Exists(secondId),"replacement removes old native lookup after queued retirement and retains new image");
            Check(Publish(owner,name,second)&&Id(second)==secondId,"idempotent publish retains its live allocation");
            Check(Ensure(owner,name),"same owner converts publication to requested target");Check(Id(second)==0,"target conversion retires previous uploaded image");Frame();
            ulong target=Id(Active(name));Check(target!=0&&!Exists(secondId),"requested target replaces ordinary upload through shared ownership");
            Check(!Publish(other,name,first),"requested target blocks competing ordinary publication");
            Check(Publish(owner,name,first),"same owner converts target to CPU-backed publication");Call("Buffers.BufferCatalog","ApplyDeferred");Frame(true);
            Check(Id(first)!=0&&!Exists(target)&&Exists(Id(first)),"target conversion retires target and restores retained CPU payload");
            ulong restored=Id(first);Call("Buffers.BufferCatalog","Unpublish",owner.ToUpperInvariant(),name.ToUpperInvariant());Call("Buffers.BufferCatalog","Unpublish",owner,name);
            Check(Active(name)==null&&Id(first)==0,"case-insensitive repeated unpublish removes ownership and upload once");Frame();Check(!Exists(restored),"unpublish retires native allocation");
            var update=typeof(NativeBackend).GetMethod("GuestUpdateImage",BindingFlags.Instance|BindingFlags.NonPublic);
            bool BufferExists(ulong id){try{update.Invoke(backend,new object[]{id,new byte[16]});return true;}catch(TargetInvocationException error) when(error.InnerException.Message.Contains("guest image update missing")){return false;}}
            object data=Activator.CreateInstance(core.GetType("ClientPlugin.Buffers.PublishedBuffer",true));buffers.Add(data);
            data.GetType().GetMethod("PublishStructuredFloats").Invoke(data,new object[]{new float[]{1,2,3,4},16});
            Check(Publish(owner,name+".structured",data)&&BufferExists(Id(data)),"structured publication owns a usable native buffer");
            ulong dataId=Id(data);Call("Buffers.BufferCatalog","UnpublishAll",owner);Frame(true);Check(Id(data)==0&&!BufferExists(dataId),"unpublish-all retires structured buffers alongside textures");
            object volume=Activator.CreateInstance(core.GetType("ClientPlugin.Buffers.PublishedBuffer",true));buffers.Add(volume);
            volume.GetType().GetMethod("PublishVolumeMips").Invoke(volume,new object[]{new byte[][]{new byte[32],new byte[4]},2});
            Check(Publish(owner,name+".volume",volume)&&Exists(Id(volume)),"mip-volume publication owns a usable native image");
            ulong volumeId=Id(volume);Call("Buffers.BufferCatalog","UnpublishAll",owner);Frame();Check(Id(volume)==0&&!Exists(volumeId),"unpublish-all retires mip-volume allocations");
            object survivor=Make(99);Check(Publish(other,"fixture.catalog.survivor",survivor),"separate owner's survivor registered");
            for(int cycle=0;cycle<8;cycle++){
                var a=Make((byte)(cycle+1));var b=Make((byte)(cycle+17));string key=name+cycle;
                Check(Publish(owner,key,a)&&Publish(owner,key+".b",b)&&Ensure(owner,key+".target"),"reload cycle publishes uploads and a requested target "+cycle);Frame(cycle%2==0);
                ulong aId=Id(a),bId=Id(b),tId=Id(Active(key+".target"));
                Call("Backend.RenderHost","QueueUnregister",owner);
                Check(Active(key)==null&&Active(key+".b")==null&&Active(key+".target")==null&&Id(a)==0&&Id(b)==0,"public pack unregister removes all publications in cycle "+cycle);
                Frame(cycle%2!=0);Check(!Exists(aId)&&!Exists(bId)&&!Exists(tId)&&Exists(Id(survivor)),"reload cycle retires all owned IDs and preserves other pack "+cycle);
            }
        }finally{
            Call("Buffers.BufferCatalog","UnpublishAll",owner);Call("Buffers.BufferCatalog","UnpublishAll",other);
            foreach(var buffer in buffers)buffer.GetType().GetMethod("Clear").Invoke(buffer,null);
            Call("Backend.GuestPassList","UnregisterRoot","fixture.catalog.reader");
            Frame();if(readerPipeline!=0)Call("Backend.GuestBridgeClient","ResourceCommand",1,readerOutput,1,null);
        }
        var stats=backend.GetStats();Check(stats.ValidationEnabled!=0&&stats.ValidationErrors==0,"catalog lifecycle including cleanup reports zero core/synchronization errors");
        return checks;
    }
}
