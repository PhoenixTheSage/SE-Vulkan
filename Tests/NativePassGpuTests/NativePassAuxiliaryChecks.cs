using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static unsafe void NativePassAuxiliaryChecks(NativeBackend backend,MatrixD projection,MyEnvironmentLightData light,bool attachmentsOnly,Action<bool,string> check)
    {
        const int size=64;
        string output=Path.GetFullPath("TestResults/NativePassAuxiliary");Directory.CreateDirectory(output);
        object Call(string method,params object[] args)=>typeof(NativeBackend).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(backend,args)!;
        using var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"Compiler"));
        byte[] Compile(string name,string source,string profile,string entry){string file=Path.Combine(output,name+".hlsl");File.WriteAllText(file,source);return SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiler.Compile(new VulkanShaderRequest{SourcePath=file,ContentDirectory=output,EntryPoint=entry,Profile=profile,NativeAbiVersion=1}).SpirvPath));}
        var attachment=Compile("attachment","struct I { float4 position:SV_Position; nointerpolation float4 row0:TEXCOORD0; nointerpolation float4 row1:TEXCOORD1; nointerpolation float4 row2:TEXCOORD2; }; float4 __pixel_shader(I i):SV_Target { return float4(1,i.row0.w,i.row1.w,i.row2.w); }","ps_5_0","__pixel_shader");
        check(backend.InitGBufferAttachment(0,1,attachment),"native-pass attachment consumer initializes");
        var vertex=Compile("fullscreen","float4 main(uint id:SV_VertexID):SV_Position {float2 p=float2((id<<1)&2,id&2);return float4(p*float2(2,-2)+float2(-1,1),0,1);}","vs_5_0","main");
        var pixel=Compile("observe","Texture2D<float4> Input:register(t0); float4 main(float4 p:SV_Position):SV_Target { return Input.Load(int3((int2)p.xy,0)); }","ps_5_0","main");
        ulong pipeline=(ulong)Call("GuestCreatePipeline2",vertex,pixel,"main","main",1);check(pipeline!=0,"native-pass auxiliary observation pipeline initializes");
        Call("GuestCreateImage",99200ul,size,size,1,1);
        byte[] Observe(ulong source){Call("GuestReset");check((bool)Call("GuestSubmit2",4,5,size,size,1,pipeline,new[]{99200ul},null!,new[]{source},2048),"native-pass auxiliary observation packet accepted");Call("GuestReplayNow",4);var bytes=new byte[size*size*8];Call("GuestReadback",99200ul,bytes);return bytes;}
        float HalfAt(byte[] bytes,int index,int component)=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,index*8+component*2));
        ulong model=backend.RegisterPassModel(new[]{new NativePassPart{Mesh=1,Color=10,Normal=11,Extensions=12,First=0,Count=6,Material=10,Flags=2}}),family=backend.RegisterPassFamily(Array.Empty<float>());backend.BindPassLevel(family,0,model);
        var actor=new NativePassActor{Owner=951,Family=family,Group=7,Flags=1|8|16|32,Scale=1,Lod=new(){Previous=-1}};actor.Translation[2]=-5;actor.Bounds[0]=actor.Bounds[1]=-2;actor.Bounds[2]=-5;actor.Bounds[3]=actor.Bounds[4]=2;actor.Bounds[5]=-5;actor.Basis[0]=actor.Basis[4]=actor.Basis[8]=1;actor.Handle=backend.RegisterPassActor(ref actor,Array.Empty<NativePassAppearance>());
        var other=actor;other.Owner=952;other.Handle=0;other.Translation[0]=2.5;other.Handle=backend.RegisterPassActor(ref other,Array.Empty<NativePassAppearance>());
        var frame=new NativePassFrame{Coefficient=1,Delta=.1f,GlobalFlags=3,SmoothDistance=100};frame.Global[4]=.25f;frame.Global[6]=1;
        var views=new[]{new NativePassView{Id=0,Kind=0,Flags=1}};
        float previousX=0,previousCamera=0;
        int stepIndex=0;
        foreach(var step in new[]{(0f,0f,false,false),(.5f,0f,false,false),(0f,0f,false,false),(.5f,0f,false,false),(0f,0f,true,false),(.5f,0f,false,false),(0f,0f,false,true),(.5f,0f,false,false),(.5f,.25f,false,false),(.5f,0f,false,false),(0f,0f,false,false),(.5f,0f,false,false)}){
            bool gap=stepIndex++==10;
            if(gap)for(int skipped=0;skipped<2;skipped++){backend.BeginPacketCollection();check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"native auxiliary history skips a world submission");backend.EndFrame();}
            float x=step.Item1,cameraX=step.Item2;frame.Camera[0]=cameraX;actor.Translation[0]=x;
            if(step.Item3){ulong previousHandle=actor.Handle;backend.RemovePassResource(2,actor.Handle);actor.Handle=0;actor.Handle=backend.RegisterPassActor(ref actor,Array.Empty<NativePassAppearance>());check(actor.Handle!=previousHandle,"recreated owner receives a distinct generational history identity");}
            var appearance=new NativePassAppearance{Material=10,Visible=1};for(int c=0;c<4;c++)appearance.Color[c]=0x3c00;appearance.Color[0]=0x3800;
            actor.AppearanceCount=step.Item4?1u:0u;backend.UpdatePassActors(new[]{actor},1,step.Item4?new[]{appearance}:Array.Empty<NativePassAppearance>(),step.Item4?1:0);
            backend.BeginPacketCollection();backend.PreparePassFrame(ref frame,views,views.Length);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);
            check(backend.GetPassGroupAudit(out var owners).Length==1&&owners.OrderBy(v=>v).SequenceEqual(new uint[]{951,952}),"native-pass owns two actors in one instanced group");
            var packets=(System.Collections.ICollection)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;
            check(packets.Count==0,"auxiliary test uses zero managed world draw packets");
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size,size,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"native-pass auxiliary frame begins at x="+x+" camera="+cameraX+" recreate="+step.Item3+" appearance="+step.Item4);backend.EndFrame();
            var depth=backend.ReadbackWorldGbuffer(3,size,size);var attached=Observe(0xFFFF000000000010ul);var motion=Observe(0xFFFF000000000008ul);
            var covered=Enumerable.Range(0,size*size).Where(i=>BitConverter.ToSingle(depth,i*4)>0).ToArray();check(covered.Length>40,"native-pass main geometry has positive stock depth coverage");
            int missing=covered.Count(i=>HalfAt(attached,i,0)<.9f);double expected=step.Item3||gap?0:((previousX-previousCamera)-(x-cameraX))*size/10;
            double otherExpected=(cameraX-previousCamera)*size/10,split=size*.5+((x+2.5)*.5-cameraX)*size/10;
            bool First(int i)=>i%size+.5<split;
            double error=covered.Select(i=>Math.Max(Math.Abs(HalfAt(motion,i,0)-(First(i)?expected:otherExpected)),Math.Abs(HalfAt(motion,i,1)))).Max();
            Console.WriteLine("NATIVE AUX x="+x+" covered="+covered.Length+" missingAttachment="+missing+" expectedMotion="+expected+" maxMotionError="+error+" validationErrors="+backend.GetStats().ValidationErrors);
            File.WriteAllBytes(Path.Combine(output,"attachment-"+x+".rgba16f"),attached);File.WriteAllBytes(Path.Combine(output,"motion-"+x+".rgba16f"),motion);
            check(missing==0,"native-pass attachment covers all surviving stock pixels");
            check(covered.Count(First)>40&&covered.Count(i=>!First(i))>40,"both independently transformed native instances have positive coverage");
            check(covered.All(i=>Math.Abs(HalfAt(attached,i,1)-((First(i)?x:2.5)-cameraX))<.01&&Math.Abs(HalfAt(attached,i,2))<.01&&Math.Abs(HalfAt(attached,i,3)+5)<.01),"native-pass attachment carries each actual actor transform");
            if(!attachmentsOnly)check(error<.01,"native-pass motion matches independent actor projection");
            previousX=x;previousCamera=cameraX;
        }
        if(!attachmentsOnly){
            // Register dormant actors before submitting the overlap sequence,
            // so registration itself does not serialize each growth boundary.
            const int stressSize=512;var growth=new NativePassActor[4094];
            for(int i=0;i<growth.Length;i++){var extra=actor;extra.Handle=0;extra.Owner=(uint)(1000+i);extra.AppearanceCount=0;extra.Flags=8|16|32;extra.Translation[0]=0;extra.Handle=backend.RegisterPassActor(ref extra,Array.Empty<NativePassAppearance>());growth[i]=extra;}
            backend.Resize(stressSize,stressSize,0);
            foreach(int count in new[]{64,1024,4096}){
                for(int i=0;i<growth.Length;i++){var extra=growth[i];extra.Flags=(i<count-2?1u:0u)|8|16|32;growth[i]=extra;}backend.UpdatePassActors(growth,growth.Length,Array.Empty<NativePassAppearance>(),0);
                for(int steady=0;steady<3;steady++){
                    backend.BeginPacketCollection();backend.PreparePassFrame(ref frame,views,views.Length);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);
                    check(backend.GetPassGroupAudit(out var stressOwners).Length==1&&stressOwners.Length==count,"native history growth preserves one instanced group: "+count+"/"+steady);
                    backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,stressSize,stressSize,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
                    check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"asynchronous native history frame begins: "+count+"/"+steady);backend.EndFrame();
                    check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"native history survives source growth without per-frame readback: "+count+"/"+steady);
                }
            }
            var finalDepth=backend.ReadbackWorldGbuffer(3,stressSize,stressSize);
            check(Enumerable.Range(0,stressSize*stressSize).Any(i=>BitConverter.ToSingle(finalDepth,i*4)>0),"asynchronous native history growth completes positive geometry");
        }
        check(backend.GetStats().ValidationErrors==0,"native-pass auxiliary has zero core/synchronization validation errors");
    }
}
