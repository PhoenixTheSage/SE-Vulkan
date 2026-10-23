using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int FoliageGenerationRegression(Assembly core,string assets,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light,VulkanShaderLibrary library)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        const string owner="fixture.native.foliage";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);
        var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        var settings=new MyRenderSettings();settings.User.GrassDrawDistance=100;settings.GrassGeometryScalingNearDistance=50;settings.GrassGeometryScalingFarDistance=350;settings.GrassGeometryDistanceScalingFactor=5;
        backend.UploadVoxelArray(952001,1,1,0,true,1,1,new byte[]{0,220,0,255});backend.UploadVoxelArray(952002,1,1,0,false,1,1,new byte[]{128,128,255,0});backend.UploadFoliage(952003,new[]{new NativeFoliagePoint{X=0,Y=-.3f,Z=-2.5f,NormalSeedMaterial=127|191u<<8|42u<<16}});
        var draw=new NativeFoliageDraw{Size=168,Mesh=952003,Color=952001,Normal=952002,Constants=WorldConstants.Object(backend.EnvironmentLayouts[4],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0)};
        var table=new byte[4096];Buffer.BlockCopy(new[]{.6f,1f,0f},0,table,0,12);BitConverter.GetBytes(1u).CopyTo(table,12);
        uint frame=0;byte[][] Frame(bool rock=false,bool pipelined=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();draw.Kind=rock?1u:0u;backend.QueueFoliage(new(){draw},table);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,renderSettings:settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"foliage generation frame begins; rock="+rock+" pipelined="+pipelined);Check(Call("Backend.GuestFrame","Capture") is true,"foliage captures immutable frame metadata");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();
        }
        string root=Path.Combine(output,"FoliagePack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        void Write(string relative,string value){string file=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,value);}
        void Register()=>Call("Backend.GuestPassList","RegisterRoot",owner,root,null);
        const string source="""
Texture2D<float4> VertexInput:register(t7);
Texture2D<float4> GeometryInput:register(t8);
Texture2D<float4> PixelInput:register(t9);
SamplerState FirstPoint:register(s1);
cbuffer Extras:register(b6){float2 RenderSize;}
struct VIn { float3 p:POSITION;float4 n:TEXCOORD0; };
struct VOut { float4 p:POSITION; };
struct PIn { float4 p:SV_Position; };
void __vertex_shader(VIn input,out VOut output){output.p=float4(input.p.x+VertexInput.SampleLevel(FirstPoint,float2(.5,.5),0).r*RenderSize.x/64,0,0,1);}
[maxvertexcount(3)]void __geometry_shader(point VOut input[1],inout TriangleStream<PIn> stream){
#ifdef ROCK_FOLIAGE
float center=.5;
#else
float center=-.5;
#endif
float scale=GeometryInput.Load(int3(0,0,0)).g*RenderSize.y/64;float shift=input[0].p.x;PIn v;
v.p=float4(center-.3*scale+shift,-.4*scale,.5,1);stream.Append(v);
v.p=float4(center+.3*scale+shift,-.4*scale,.5,1);stream.Append(v);
v.p=float4(center+shift,.4*scale,.5,1);stream.Append(v);stream.RestartStrip();}
struct G {float4 color:SV_Target0;float4 normal:SV_Target1;float4 material:SV_Target2;};
void __pixel_shader(PIn input,out G output){
#ifdef ROCK_FOLIAGE
float3 color=float3(0,1,0);
#else
float3 color=float3(1,0,0);
#endif
output.color=float4((color+float3(0,0,PixelInput.Load(int3(0,0,0)).b))*RenderSize.x/64,1);output.normal=float4(.5,.5,1,0);output.material=0;}
""";
        void Clear(int slot,float r,float g,float b){Call("Buffers.CatalogImages","ClearColorTarget",owner+".t"+slot,r,g,b,0f);}
        void Pixel(byte[] bytes,int x,int y,byte r,byte g,byte b,string text){int i=(y*64+x)*4;Console.WriteLine("foliage pixel "+x+","+y+"="+string.Join(",",bytes.Skip(i).Take(4)));Check(bytes[i]==r&&bytes[i+1]==g&&bytes[i+2]==b,text);}
        int Coverage(byte[] bytes)=>Enumerable.Range(0,64*64).Count(i=>bytes[i*4]>0||bytes[i*4+1]>0||bytes[i*4+2]>0);
        try {
            Check((GuestBridge.SupportedPacketFlags&33554432)!=0,"native foliage closure advertises bit25");var stockGrass=Frame();var stockRock=Frame(true);
            foreach(int slot in new[]{7,8,9}){string name=owner+".t"+slot;Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,name,1,1,10) is true,"foliage catalog target requested for t"+slot);Call("Shaders.ShaderBindRegistry","RequestSrv","Foliage",name,slot);}
            Clear(7,0,0,0);Clear(8,0,1,0);Clear(9,0,0,0);Write("anomaly.json","{\"id\":\""+owner+"\"}");Write("Overlay/Foliage/Foliage.hlsl",source);Register();var grass=Frame(false,true);
            Check(Property("Backend.GuestPostprocessStages","Error")==null,"complete foliage family prepares: "+Property("Backend.GuestPostprocessStages","Error"));Pixel(grass[0],16,32,255,0,0,"grass GS and PS produce the independent left red triangle");Pixel(grass[0],48,32,0,0,0,"grass does not produce the rock triangle");
            var rock=Frame(true);Pixel(rock[0],48,32,0,255,0,"rock GS and PS produce the independent right green triangle");Pixel(rock[0],16,32,0,0,0,"rock does not produce the grass triangle");
            ulong token=backend.GetPostprocessGeneration(768).Active;Check(token!=0&&Enumerable.Range(768,5).All(i=>{var s=backend.GetPostprocessGeneration((uint)i);return s.Active==token&&s.Executions>0&&s.LastCompletion<=s.Completed;}),"all five actual shader modules activate together and record completed execution");
            Clear(7,.25f,0,0);var moved=Frame();Pixel(moved[0],24,32,255,0,0,"vertex-only catalog binding shifts the emitted triangle by eight pixels");Pixel(moved[0],16,32,0,0,0,"vertex stage removes coverage at the original center");int large=Coverage(moved[0]);
            Clear(8,0,.5f,0);var shrunk=Frame(false,true);int small=Coverage(shrunk[0]);Check(small>0&&small<large*.4&&small>large*.1,"geometry-only catalog binding scales actual triangle area near one quarter");
            Clear(9,0,0,1);Pixel(Frame()[0],24,32,255,0,255,"pixel-only catalog binding changes the exact final GBuffer color");Check(backend.GetStageBindingState().Resolved>=3,"merged VS/GS/PS resource rows resolve all three native catalog bindings");
            Write("Overlay/Foliage/Foliage.hlsl",source.Replace("output.p=float4(","output.p=BrokenSyntax("));Register();Pixel(Frame()[0],24,32,255,0,255,"failed foliage compilation retains all prior shader-stage outputs");Check(Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(768).Active==token,"invalid shared VS cannot partially replace either GS/PS pair");
            try{backend.PreparePostprocessGeneration(new Dictionary<uint,VulkanShaderArtifact>{{768,library.Load(EnvironmentShaderVariants.Stages[4])}},"fixture.incomplete.foliage");throw new Exception("incomplete foliage closure accepted");}
            catch(InvalidOperationException error){Check(error.Message.Contains("incomplete"),"native packet rejects an incomplete shared VS/GS/PS closure");}
            Check(backend.GetPostprocessGeneration(768).Active==token,"incomplete native packet leaves the full active generation intact");
            Write("Overlay/Foliage/Foliage.hlsl","struct Other {uint4 p:POSITION;};\n"+source.Replace("point VOut input[1]","point Other input[1]"));Register();Pixel(Frame()[0],24,32,255,0,255,"native interface rejection preserves all prior stage outputs");Check((Property("Backend.GuestPostprocessStages","Error") as string)?.Contains("stage interface type mismatch")==true&&backend.GetPostprocessGeneration(768).Active==token,"integer/float shader-stage mismatch refuses before pipeline activation");
            Call("Backend.GuestPassList","UnregisterRoot",owner);var restoredGrass=Frame();var restoredRock=Frame(true,true);Check(restoredGrass.Zip(stockGrass,(x,y)=>x.SequenceEqual(y)).All(v=>v)&&restoredRock.Zip(stockRock,(x,y)=>x.SequenceEqual(y)).All(v=>v),"unregister restores exact stock grass/rock GBuffer and depth");Frame();Check(backend.GetPostprocessGeneration(768).Retired==0,"foliage layouts and both raster pipelines retire after completion");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"foliage family has zero core/synchronization errors");
        } finally {Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Buffers.BufferCatalog","UnpublishAll",owner);}
        return checks;
    }
}
