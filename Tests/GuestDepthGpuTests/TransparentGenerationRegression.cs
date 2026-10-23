using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.Import;
using VRageMath;
using VRageRender;
partial class Program
{
    // Glass/holo owners: Geometry/Passes/Transparent into OIT, TransparentForDecals into the glass decal depth layers.
    static int TransparentGenerationRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.transparent",catalog=owner+".input";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        Check((backend.NativeOwnerFeatures()&16)!=0,"native owner extension advertises transparent glass owners (flag16)");
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var rows=backend.PostprocessCatalog().Where(r=>(uint)r[0]>=2304&&(uint)r[0]<2344).ToArray();
        Check(rows.Length==40&&rows.All(r=>(string)r[1]=="Geometry/Passes/"+((uint)r[0]<2324?"Transparent":"TransparentForDecals")+(((uint)r[0]&1)==0?"/VertexStage.hlsli":"/PixelStage.hlsli")),"catalog lists 20 glass/holo VS/PS owner pairs keyed by their DX11 pass files");
        Check(rows.All(r=>((string[])r[3]).Contains("Geometry/Passes/"+((uint)r[0]<2324?"Transparent":"TransparentForDecals")+"/Declarations.hlsli")),"every stock owner artifact compiles through its pass declarations");
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-2,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2}};}
        backend.UploadWorldMesh(953001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});backend.UploadWorldMesh(953004,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,2,1});backend.UploadTexture(953002,1,1,0,new byte[]{255,255,255,255},false);backend.UploadTexture(953003,1,1,0,new byte[]{128,128,255,100},false);
        var material=new byte[96];Buffer.BlockCopy(new[]{.25f,.5f,.75f,.5f},0,material,16,16);
        // A transparent (role 8) decal on zero-alpha glass draws through the glass decal depth layers, which cull clockwise back faces.
        NativeDecalDraw Decal(){MatrixD w=MatrixD.CreateScale(2,2,1)*MatrixD.CreateTranslation(0,0,-2);var c=new byte[128];Buffer.BlockCopy(new[]{(float)w.M11,(float)w.M21,(float)w.M31,(float)w.M41,(float)w.M12,(float)w.M22,(float)w.M32,(float)w.M42,(float)w.M13,(float)w.M23,(float)w.M33,(float)w.M43},0,c,0,48);BitConverter.GetBytes(1f).CopyTo(c,48);Buffer.BlockCopy(NativeBackend.MatrixValues(MatrixD.Invert(w)),0,c,64,64);return new(){Size=176,Role=8,Alpha=953002,Color=953002,Normal=953003,Extensions=953002,Constants=c};}
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;var decalLight=light;light.AmbientDiffuseFactor=0;light.SunDiffuseFactor=0;
        uint frame=0;
        float[] Frame(bool pipelined=false,bool glass=true,bool decal=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            if(decal)backend.DecalPackets.Add(Decal());if(glass)backend.AddGlassPacket(0,decal?953004UL:953001UL,953002,953003,WorldConstants.Object(backend.GlassLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),decal?new byte[96]:material);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,decal?decalLight:light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"transparent owner frame begins; pipelined="+pipelined+" glass="+glass+" decal="+decal);
            Call("Backend.GuestFrame","Capture");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return backend.ReadbackLightingStage(3,64,64);
        }
        int at=(32*64+32)*4;string Hdr(float[] data)=>string.Join(",",data.Skip(at).Take(3));
        bool Near(float[] data,float r,float g,float b)=>Math.Abs(data[at]-r)<.015&&Math.Abs(data[at+1]-g)<.015&&Math.Abs(data[at+2]-b)<.015;
        string root=Path.Combine(output,"TransparentPack-"+Guid.NewGuid().ToString("N"));File.WriteAllText(Path.Combine(Directory.CreateDirectory(root).FullName,"anomaly.json"),"{\"id\":\""+owner+"\"}");
        string Stage(string pass)=>File.ReadAllText(Path.Combine(bin,"../Content/Shaders/Geometry/Passes",pass,"PixelStage.hlsli"));
        string glassFile=Path.Combine(root,"Overlay/Geometry/Passes/Transparent/PixelStage.hlsli"),depthFile=Path.Combine(root,"Overlay/Geometry/Passes/TransparentForDecals/PixelStage.hlsli");
        // Swizzled color scaled by a catalog SRV (t7) and the pack extras (b6): resolve is linear in the accumulated color.
        const string anchor="float4 resultColor = float4(material_output.base_color, material_output.transparency);";
        string glass=Stage("Transparent");Check(glass.Contains(anchor),"stock Transparent pixel stage exposes the OIT color anchor");string swizzle=glass.Replace(anchor,anchor+"\n    resultColor.rgb = resultColor.bgr;");
        glass="Texture2D<float4> AnomalyGlassInput : register(t7);\ncbuffer AnomalyGlassFrame : register(b6) { float2 GlassRenderSize; };\n"+glass.Replace(anchor,anchor+"\n    resultColor.rgb = resultColor.bgr * (AnomalyGlassInput.Load(int3(0, 0, 0)).r * 4) * (GlassRenderSize.x / 64);");
        const string depthAnchor="float2 screenUV = screen_to_uv(vertex.position.xy);";
        string depth=Stage("TransparentForDecals");Check(depth.Contains(depthAnchor),"stock TransparentForDecals pixel stage exposes its depth-compare anchor");depth=depth.Replace(depthAnchor,"clip(-1.0f);\n\t"+depthAnchor);
        void Register(string glassSource,string depthSource){
            foreach(var (file,source) in new[]{(glassFile,glassSource),(depthFile,depthSource)}){if(source==null){if(File.Exists(file))File.Delete(file);continue;}Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,source);}
            Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        try{
            backend.SetHdrQuality(true);var stock=Frame();Console.WriteLine("TRANSPARENT stock="+Hdr(stock));
            Check(Near(stock,.25f,.5f,.75f)&&stock.SequenceEqual(Frame(true)),"stock glass OIT output is the material ColorAdd and exact across recording modes");
            Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,catalog,1,1,10) is true,"glass catalog target accepted");Call("Shaders.ShaderBindRegistry","RequestSrv","Transparent",catalog,7);Call("Buffers.CatalogImages","ClearColorTarget",catalog,.25f,.125f,.0625f,0f);
            Register(swizzle,null);var swizzled=Frame(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"glass owner generation prepares: "+Property("Backend.GuestPostprocessStages","Error"));Console.WriteLine("TRANSPARENT swizzled="+Hdr(swizzled));
            Check(Near(swizzled,.75f,.5f,.25f),"glass owner swizzles its OIT color from its own invocation");
            Register(glass,null);var owned=Frame(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"glass owner declaring reserved inputs prepares: "+Property("Backend.GuestPostprocessStages","Error"));Console.WriteLine("TRANSPARENT owned="+Hdr(owned));
            // DX11 binds Transparent reserved inputs only around ResolveOIT, so glass draws read t7 and b6 as unbound (zero).
            Check(Near(owned,0,0,0),"glass owner reads DX11-unbound reserved inputs (t7, b6) as zero");
            var state=backend.GetPostprocessGeneration(2305);Check(state.Active!=0&&state.Executions>0&&state.LastFrame==frame&&state.LastCompletion<=state.Completed&&backend.GetPostprocessGeneration(2304).Executions==state.Executions,"selected glass VS/PS owner pair executes and completes this frame");
            Check(!backend.StageBindingOwners().Any(r=>(uint)r[0]>=2304&&(uint)r[0]<2344),"glass owners take no stage-binding rows, as on DX11");
            Check(owned.SequenceEqual(Frame()),"glass owner output agrees exactly between recording modes");
            Register(swizzle,null);var swapped=Frame(true);Check(Near(swapped,.75f,.5f,.25f),"re-registering the swizzle owner replaces the reserved-input owner");
            ulong count=backend.GetPostprocessGeneration(2305).Executions;Frame(true,false);Check(backend.GetPostprocessGeneration(2305).Executions==count&&backend.GetPostprocessGeneration(2305).LastFrame!=frame,"active glass owner has no invented execution in a frame without glass");
            ulong previous=backend.GetPostprocessGeneration(2305).Active;Register("BrokenSyntax",null);var broken=Frame(true);
            Check(Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(2305).Active==previous&&Near(broken,.75f,.5f,.25f),"failed glass compilation retains the previous owner pair");
            Call("Backend.GuestPassList","UnregisterRoot",owner);Check(stock.SequenceEqual(Frame(true)),"unregister restores exact stock glass output");
            // TransparentForDecals: discarding every glass depth fragment leaves the decal depth layer at the opaque copy.
            var decalStock=Frame(false,true,true);Console.WriteLine("TRANSPARENT decal stock="+Hdr(decalStock));Check(decalStock.SequenceEqual(Frame(true,true,true)),"stock glass decal layers agree across recording modes");
            Register(null,depth);var decalOwned=Frame(true,true,true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"glass decal-depth owner generation prepares: "+Property("Backend.GuestPostprocessStages","Error"));Console.WriteLine("TRANSPARENT decal owned="+Hdr(decalOwned));
            var depthState=backend.GetPostprocessGeneration(2325);Check(depthState.Active!=0&&depthState.Executions>0&&depthState.LastFrame==frame&&backend.GetPostprocessGeneration(2324).Executions==depthState.Executions,"selected TransparentForDecals owner pair executes in the glass decal layers this frame");
            Check(decalStock[at]>.5f&&decalOwned.Skip(at).Take(3).All(v=>v==0),"stock transparent decal lights the glass surface; discarding every glass depth fragment removes that surface, so the decal composite is exactly zero");
            Check(decalOwned.SequenceEqual(Frame(false,true,true)),"glass decal-depth owner output agrees exactly between recording modes");
            ulong depthCount=backend.GetPostprocessGeneration(2325).Executions;Frame(true);Check(backend.GetPostprocessGeneration(2325).Executions==depthCount,"decal-depth owner has no invented execution without transparent decals");
            Call("Backend.GuestPassList","UnregisterRoot",owner);Check(decalStock.SequenceEqual(Frame(true,true,true))&&stock.SequenceEqual(Frame()),"unregister restores exact stock glass and decal-layer output");
            Frame();Check(backend.GetPostprocessGeneration(2305).Retired==0,"glass owner pipelines and layouts retire after completion");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"glass owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Buffers.BufferCatalog","UnpublishAll",owner);backend.SetHdrQuality(true);}
        return checks;
    }
}
