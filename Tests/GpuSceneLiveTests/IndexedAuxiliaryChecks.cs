using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using ClientPlugin.Rendering.Scene;
using FixtureAssets;
using VRage;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

partial class Program
{
    static void IndexedAuxiliaryChecks(NativeBackend backend,VulkanScene scene,Func<byte[]> render,Func<bool> owned,Func<int> packets,Action<MatrixD> setProjection,Action<bool,string> check)
    {
        const int size=64;string output=Path.GetFullPath("TestResults/IndexedAuxiliary");Directory.CreateDirectory(output);
        object Call(string method,params object[] args)=>typeof(NativeBackend).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(backend,args)!;
        using var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"Compiler"));
        byte[] Compile(string name,string source,string profile,string entry){string file=Path.Combine(output,name+".hlsl");File.WriteAllText(file,source);return SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiler.Compile(new VulkanShaderRequest{SourcePath=file,ContentDirectory=output,EntryPoint=entry,Profile=profile,NativeAbiVersion=1}).SpirvPath));}
        var attachment=Compile("attachment","struct I {float4 p:SV_Position;nointerpolation float4 row0:TEXCOORD0;nointerpolation float4 row1:TEXCOORD1;nointerpolation float4 row2:TEXCOORD2;};float4 __pixel_shader(I i):SV_Target{return float4(1,i.row0.w,i.row1.w,i.row2.w);}","ps_5_0","__pixel_shader");
        bool initialized=backend.InitGBufferAttachment(0,1,attachment);if(!initialized)Console.WriteLine(ClientPlugin.Diagnostics.VulkanDiagnostics.Report());check(initialized,"indexed attachment consumer initializes");
        var basis=Compile("basis","struct I {float4 p:SV_Position;nointerpolation float4 row0:TEXCOORD0;nointerpolation float4 row1:TEXCOORD1;nointerpolation float4 row2:TEXCOORD2;};float4 __pixel_shader(I i):SV_Target{return float4(i.row0.x,i.row1.y,i.row2.z,1);}","ps_5_0","__pixel_shader");
        check(backend.InitGBufferAttachment(1,1,basis),"second indexed attachment consumer initializes");
        var vertex=Compile("fullscreen","float4 main(uint id:SV_VertexID):SV_Position {float2 p=float2((id<<1)&2,id&2);return float4(p*float2(2,-2)+float2(-1,1),0,1);}","vs_5_0","main");
        var pixel=Compile("observe","Texture2D<float4> Input:register(t0);float4 main(float4 p:SV_Position):SV_Target{return Input.Load(int3((int2)p.xy,0));}","ps_5_0","main");
        ulong pipeline=(ulong)Call("GuestCreatePipeline2",vertex,pixel,"main","main",1);check(pipeline!=0,"indexed auxiliary observation pipeline initializes");Call("GuestCreateImage",99300ul,size,size,1,1);
        float HalfAt(byte[] bytes,int i,int c)=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*8+c*2));
        byte[] Observe(ulong source){Call("GuestReset");check((bool)Call("GuestSubmit2",4,5,size,size,1,pipeline,new[]{99300ul},null!,new[]{source},2048),"indexed attachment observation packet accepted");Call("GuestReplayNow",4);var bytes=new byte[size*size*8];Call("GuestReadback",99300ul,bytes);return bytes;}
        int Verify(string label,float expectedZ=-5,float expectedScale=1,uint? expectedType=null){int coverage=0;for(int slot=0;slot<2;slot++){
            render();check(owned()&&packets()==0,"auxiliary geometry stays GPU owned with zero managed packets");var depth=backend.ReadbackWorldGbuffer(3,size,size);
            if(expectedType.HasValue){var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;var data=actors[200u]!;check(Convert.ToUInt32(data.GetType().GetField("GpuType")!.GetValue(data))==expectedType,"indexed source class is genuine: "+expectedType);}
            var bytes=Observe(0xFFFF000000000010ul);var basisBytes=Observe(0xFFFF000000000011ul);
            var covered=Enumerable.Range(0,size*size).Where(i=>BitConverter.ToSingle(depth,i*4)>0).ToArray();check(covered.Length>20,"indexed main geometry has positive depth coverage");int missing=covered.Count(i=>HalfAt(bytes,i,0)<.9f);
            File.WriteAllBytes(Path.Combine(output,label+"-attachment-"+slot+".rgba16f"),bytes);File.WriteAllBytes(Path.Combine(output,label+"-basis-"+slot+".rgba16f"),basisBytes);Console.WriteLine("INDEXED AUX "+label+" slot="+slot+" covered="+covered.Length+" missing="+missing+" validationErrors="+backend.GetStats().ValidationErrors);
            check(missing==0,"indexed attachment covers every surviving GPU pixel");check(covered.All(i=>Math.Abs(HalfAt(bytes,i,1))<.01&&Math.Abs(HalfAt(bytes,i,2))<.01&&Math.Abs(HalfAt(bytes,i,3)-expectedZ)<.01),"indexed attachment carries actual actor translation");
            check(covered.All(i=>Math.Abs(HalfAt(basisBytes,i,0)-expectedScale)<.01&&Math.Abs(HalfAt(basisBytes,i,1)-expectedScale)<.01&&Math.Abs(HalfAt(basisBytes,i,2)-expectedScale)<.01&&HalfAt(basisBytes,i,3)>.9f),"second indexed attachment carries actual actor basis");coverage=covered.Length;
        }return coverage;}
        Verify("new");
        var saved=scene.RenderSettings;var settings=saved;settings.User.DistanceFade=10000;scene.RenderSettings=settings;
        var farProjection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,10000);farProjection.M13=farProjection.M14-farProjection.M13;farProjection.M23=farProjection.M24-farProjection.M23;farProjection.M33=farProjection.M34-farProjection.M33;farProjection.M43=farProjection.M44-farProjection.M43;setProjection(farProjection);
        foreach(uint type in new uint[]{0,2,3,4})foreach(bool fade in type==0?new[]{false}:new[]{false,true}){
            scene.Process(new MyRenderMessageRemoveRenderObject{ID=200});
            string model=MeshDecalFixtureAssets.Model(Path.Combine(output,"Models-"+type),MyMeshDrawTechnique.MESH,7,255,deformed:type==4,singleLayer:true,neutral:true);
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=200,Model=model,WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline|(fade?RenderFlags.DistanceFade:0)});
            settings.User.DistanceFade=fade?5.1f:10000;scene.RenderSettings=settings;
            if(type==2)scene.Process(new MyRenderMessageSetInstanceBuffer{ID=200,InstanceBufferId=9600,InstanceCount=1,InstanceData=new[]{new MyInstanceData(Matrix.CreateTranslation(.2f,0,0))},LocalAabb=new BoundingBox(new Vector3(-2),new Vector3(2))});
            else if(type>=3){uint buffer=9600+type;var cube=new MyCubeInstanceData{LocalMatrix=Matrix.CreateTranslation(.2f,0,0)};if(type==4){cube.EnableSkinning=true;cube.BoneRange=1;for(int i=0;i<9;i++)cube[i]=new Vector3UByte(191,127,127);}
                scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=buffer});scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=buffer,InstanceData=new List<MyCubeInstanceData>{cube},Capacity=1});scene.Process(new MyRenderMessageSetInstanceBuffer{ID=200,InstanceBufferId=buffer,InstanceCount=1,LocalAabb=new BoundingBox(new Vector3(-2),new Vector3(2))});}
            Verify("old-"+type+"-"+fade,expectedType:type);
            if(fade){var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;var data=actors[200u]!;var actor=(VRage.Render.Scene.MyActor)data.GetType().GetField("Actor")!.GetValue(data)!;var far=MatrixD.CreateScale(350)*MatrixD.CreateTranslation(0,0,-1050);actor.SetMatrix(ref far);
                settings.User.DistanceFade=10000;scene.RenderSettings=settings;int full=Verify("far-control-"+type,-1050,350,type);settings.User.DistanceFade=1100;scene.RenderSettings=settings;int partial=Verify("far-partial-"+type,-1050,350,type);check(partial<full,"active far fade reduces actual GPU coverage for class "+type);
            }
        }
        scene.RenderSettings=saved;
        check(backend.GetStats().ValidationErrors==0,"indexed auxiliary has zero core/synchronization validation errors");
    }
}
