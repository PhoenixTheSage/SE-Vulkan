using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRage.Import;
using VRageMath;
using VRageRender;

partial class Program
{
    static void DistanceHistoryChecks(NativeBackend backend,Action<bool,string> check)
    {
        const int size=256;
        string output=Path.GetFullPath("TestResults/DistanceHistory");Directory.CreateDirectory(output);
        object Call(string method,params object[] args)=>typeof(NativeBackend).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(backend,args)!;
        using var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"Compiler"));
        byte[] Compile(string name,string source,string profile,string entry){string file=Path.Combine(output,name+".hlsl");File.WriteAllText(file,source);return SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiler.Compile(new VulkanShaderRequest{SourcePath=file,ContentDirectory=output,EntryPoint=entry,Profile=profile,NativeAbiVersion=1}).SpirvPath));}
        var attachment=Compile("attachment","struct I { float4 position:SV_Position; nointerpolation float4 row0:TEXCOORD0; nointerpolation float4 row1:TEXCOORD1; nointerpolation float4 row2:TEXCOORD2; }; float4 __pixel_shader(I i):SV_Target { return float4(1,1,1,1); }","ps_5_0","__pixel_shader");
        check(backend.InitGBufferAttachment(0,0,attachment),"distance-history attachment consumer initializes");
        var vertex=Compile("fullscreen","float4 main(uint id:SV_VertexID):SV_Position {float2 p=float2((id<<1)&2,id&2);return float4(p*float2(2,-2)+float2(-1,1),0,1);}","vs_5_0","main");
        var pixel=Compile("observe","Texture2D<float4> Input:register(t0); float4 main(float4 p:SV_Position):SV_Target { return Input.Load(int3((int2)p.xy,0)); }","ps_5_0","main");
        ulong pipeline=(ulong)Call("GuestCreatePipeline2",vertex,pixel,"main","main",1);
        check(pipeline!=0,"motion and attachment observation pipeline initializes");
        Call("GuestCreateImage",99100ul,size,size,1,1);
        backend.Resize(size,size,0);backend.WorldRecipesEnabled=false;backend.RigidBatchingEnabled=false;backend.ModelBatchingEnabled=false;
        var positions=new[]{new Vector3(-(float)Math.Sqrt(3)/2,-.5f,-3),new Vector3((float)Math.Sqrt(3)/2,-.5f,-3),new Vector3(0,1,-3)};
        uint n=VF_Packer.PackNormal(Vector3.Backward);
        var vertices=positions.Select(p=>new NativeWorldVertex{Material=new(){X=p.X,Y=p.Y,Z=p.Z,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentW=1},WeightX=1}).ToArray();
        backend.BeginUploads();backend.UploadWorldMesh(99101,vertices,new uint[]{0,2,1});backend.UploadTexture(99102,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(99103,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(99104,1,1,0,new byte[]{255,0,0,0},false);backend.EndUploads();
        var projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,10000);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;
        byte[] Observe(ulong source){Call("GuestReset");check((bool)Call("GuestSubmit2",4,5,size,size,1,pipeline,new[]{99100ul},null!,new[]{source},2048),"history observation packet accepted");Call("GuestReplayNow",4);var bytes=new byte[size*size*8];Call("GuestReadback",99100ul,bytes);return bytes;}
        float HalfAt(byte[] bytes,int pixelIndex,int component)=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,pixelIndex*8+component*2));
        Vector3D World(Vector3 position,float threshold,float translationX,bool fade){var local=(Vector3D)position;var unscaled=local*100+new Vector3D(translationX,0,-600);if(fade){double factor=Math.Clamp((unscaled.Length()-Math.Max(threshold,900))/(threshold/10),0,1);local*=1-factor;}return local*100+new Vector3D(translationX,0,-600);}
        Vector2D Screen(Vector3D position){var clip=Vector4D.Transform(new Vector4D(position,1),projection);return new Vector2D((clip.X/clip.W*.5+.5)*size,(.5-clip.Y/clip.W*.5)*size);}
        byte[] InstanceSource(uint type,float translationX){
            var stream=new byte[64];if(type==2){foreach(int at in new[]{0,20,40})BitConverter.GetBytes(1f).CopyTo(stream,at);BitConverter.GetBytes(translationX).CopyTo(stream,12);}
            else if(type>=3){var cube=new MyCubeInstanceData{LocalMatrix=Matrix.CreateTranslation(translationX,0,0)};if(type==4){cube.EnableSkinning=true;cube.BoneRange=1;for(int i=0;i<9;i++)cube[i]=new Vector3UByte(127,127,127);}for(int i=0;i<8;i++){var b=cube.GetPackedBone(i);stream[i*4]=b.X;stream[i*4+1]=b.Y;stream[i*4+2]=b.Z;stream[i*4+3]=b.W;}var p=cube.PackedOrthoMatrix;var packed=new[]{p.X,p.Y,p.Z,p.W};for(int i=0;i<4;i++)BitConverter.GetBytes(packed[i]).CopyTo(stream,32+i*4);}
            return stream;
        }
        foreach(var variant in new[]{(0u,false),(2u,false),(3u,false),(4u,false),(2u,true),(3u,true),(4u,true)}){
            uint type=variant.Item1;bool fade=variant.Item2;Console.WriteLine("DISTANCE HISTORY type="+type+" distanceFade="+fade);
            byte[] stream=InstanceSource(type,0);
            float previousThreshold=10000,previousX=0;bool havePrevious=false;
            foreach(var step in new[]{(10000f,0f),(10000f,20f),(900f,0f),(900f,20f),(10000f,0f),(10000f,20f)}){
                uint shader=type+(fade?2560u:0u);var settings=new MyRenderSettings();settings.User.DistanceFade=step.Item1;
                var world=MatrixD.CreateScale(100)*MatrixD.CreateTranslation(step.Item2,0,-600);
                backend.BeginPacketCollection();backend.AddWorldPacket(shader,99101,99102,99103,99104,0,WorldConstants.Object(backend.WorldLayouts[shader],world,Vector3.One,Vector3.Zero,1,0),type==0?null:stream,1,512|0x08000000);
                backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size,size,light,renderSettings:settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
                check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"distance-history frame begins: "+type+" threshold="+step.Item1+" x="+step.Item2);backend.EndFrame();
                var depth=backend.ReadbackWorldGbuffer(3,size,size);var attached=Observe(0xFFFF000000000010ul);var motion=Observe(0xFFFF000000000008ul);
                var covered=Enumerable.Range(0,size*size).Where(i=>BitConverter.ToSingle(depth,i*4)>0).ToArray();
                check(covered.Length>20,"distance-history geometry has positive coverage: "+type+" threshold="+step.Item1);
                int missing=covered.Count(i=>HalfAt(attached,i,0)<.9f);File.WriteAllBytes(Path.Combine(output,$"attachment-{type}-{fade}-{step.Item1}-{step.Item2}.rgba16f"),attached);
                check(missing==0,"attachment covers every surviving stock pixel: "+type+" threshold="+step.Item1+" missing="+missing);
                if(havePrevious){
                    // Independent CPU projection and perspective interpolation
                    // of the actual stock per-vertex shrink, including the
                    // previous frame's threshold and transform.
                    var current=positions.Select(p=>World(p,step.Item1,step.Item2,fade)).ToArray();var prior=positions.Select(p=>World(p,previousThreshold,previousX,fade)).ToArray();var screen=current.Select(Screen).ToArray();var delta=prior.Select(Screen).Zip(screen,(p,c)=>p-c).ToArray();
                    double Cross(Vector2D a,Vector2D b)=>a.X*b.Y-a.Y*b.X;double area=Cross(screen[1]-screen[0],screen[2]-screen[0]);double maxError=0,maxMotion=0;int samples=0;
                    foreach(int i in covered){var point=new Vector2D(i%size+.5,i/size+.5);double b1=Cross(point-screen[0],screen[2]-screen[0])/area,b2=Cross(screen[1]-screen[0],point-screen[0])/area,b0=1-b1-b2;if(Math.Min(b0,Math.Min(b1,b2))<.1)continue;var weights=new[]{b0/-current[0].Z,b1/-current[1].Z,b2/-current[2].Z};double sum=weights.Sum();var expected=Vector2D.Zero;for(int v=0;v<3;v++)expected+=delta[v]*(weights[v]/sum);maxError=Math.Max(maxError,Math.Max(Math.Abs(HalfAt(motion,i,0)-expected.X),Math.Abs(HalfAt(motion,i,1)-expected.Y)));maxMotion=Math.Max(maxMotion,expected.Length());samples++;}
                    File.WriteAllBytes(Path.Combine(output,$"motion-{type}-{fade}-{step.Item1}-{step.Item2}.rgba16f"),motion);
                    check(samples>10&&maxMotion>.1,"motion oracle observes moving interior pixels: "+type+" samples="+samples);
                    check(maxError<.01,"motion matches stock current/previous fade projection: "+type+" threshold="+step.Item1+" maxError="+maxError);
                }
                previousThreshold=step.Item1;previousX=step.Item2;havePrevious=true;
            }
        }
        // The two draws deliberately share mesh and instance-buffer handles.
        // Their distinct instance offsets and independent motion must survive
        // the native vertex-binding cache on both fenced slots.
        foreach(uint type in new uint[]{2,3,4})foreach(bool fade in new[]{false,true}){
            uint shader=type+(fade?2560u:0u);var settings=new MyRenderSettings();settings.User.DistanceFade=900;
            var previous=new[]{-1f,1f};
            for(int frameIndex=0;frameIndex<4;frameIndex++){
                var current=frameIndex%2==0?new[]{-1f,1f}:new[]{-.9f,.8f};
                backend.BeginPacketCollection();
                for(int draw=0;draw<2;draw++)backend.AddWorldPacket(shader,99101,99102,99103,99104,0,WorldConstants.Object(backend.WorldLayouts[shader],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),InstanceSource(type,current[draw]),1,512|0x08000000);
                backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size,size,light,renderSettings:settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
                check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"shared-instance history frame begins: type="+type+" fade="+fade+" frame="+frameIndex);backend.EndFrame();
                var depth=backend.ReadbackWorldGbuffer(3,size,size);var attached=Observe(0xFFFF000000000010ul);var motion=Observe(0xFFFF000000000008ul);
                var covered=Enumerable.Range(0,size*size).Where(i=>BitConverter.ToSingle(depth,i*4)>0).ToArray();
                check(covered.Length>100,"shared-instance geometry has positive coverage");
                check(covered.All(i=>HalfAt(attached,i,0)>.9f),"shared-instance attachment covers both draws");
                if(frameIndex>0)for(int draw=0;draw<2;draw++){
                    var pixels=covered.Where(i=>draw==0?i%size<size/2-4:i%size>size/2+4).ToArray();
                    double expected=(previous[draw]-current[draw])*size/6;
                    double error=pixels.Select(i=>Math.Max(Math.Abs(HalfAt(motion,i,0)-expected),Math.Abs(HalfAt(motion,i,1)))).DefaultIfEmpty(double.PositiveInfinity).Max();
                    check(pixels.Length>100&&Math.Abs(expected)>.1,"shared-instance motion oracle observes draw "+draw);
                    File.WriteAllBytes(Path.Combine(output,$"shared-motion-{type}-{fade}-{frameIndex}-{draw}.rgba16f"),motion);
                    check(error<.01,"motion binding preserves distinct instance offsets: type="+type+" fade="+fade+" draw="+draw+" maxError="+error);
                }
                previous=current;
            }
        }
        // Distinct vertex and index offsets within shared arena pages. The
        // second mesh uses indices 3..5; its first three vertices are offscreen.
        backend.SetGeometryArena(true);backend.BeginUploads();backend.UploadWorldMesh(99105,vertices,new uint[]{0,2,1});
        var arenaVertices=Enumerable.Range(0,6).Select(i=>{var value=vertices[i%3];value.Material.Z=-4;if(i<3)value.Material.X+=100;return value;}).ToArray();
        backend.UploadWorldMesh(99106,arenaVertices,new uint[]{3,5,4});backend.EndUploads();var arena=backend.GetGeometryArenaStats();
        check(arena.VertexPages==1&&arena.IndexPages==1,"motion fixture uses shared vertex and index pages");
        var arenaPrevious=new[]{-1f,1f};
        for(int frameIndex=0;frameIndex<4;frameIndex++){
            var current=frameIndex%2==0?new[]{-1f,1f}:new[]{-.9f,.8f};backend.BeginPacketCollection();
            for(int draw=0;draw<2;draw++)backend.AddWorldPacket(0,(ulong)(99105+draw),99102,99103,99104,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(current[draw],0,0),Vector3.One,Vector3.Zero,1,0),flags:512|0x08000000);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size,size,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"shared-geometry history frame begins: "+frameIndex);backend.EndFrame();
            var depth=backend.ReadbackWorldGbuffer(3,size,size);var motion=Observe(0xFFFF000000000008ul);
            if(frameIndex>0)for(int draw=0;draw<2;draw++){
                var pixels=Enumerable.Range(0,size*size).Where(i=>BitConverter.ToSingle(depth,i*4)>0&&(draw==0?i%size<size/2-4:i%size>size/2+4)).ToArray();
                double expected=(arenaPrevious[draw]-current[draw])*size/(draw==0?6:8);
                double error=pixels.Select(i=>Math.Max(Math.Abs(HalfAt(motion,i,0)-expected),Math.Abs(HalfAt(motion,i,1)))).DefaultIfEmpty(double.PositiveInfinity).Max();
                check(pixels.Length>100&&Math.Abs(expected)>.1,"shared-geometry motion oracle observes draw "+draw);
                check(error<.01,"motion binding preserves distinct vertex/index offsets: draw="+draw+" maxError="+error);
            }
            arenaPrevious=current;
        }
        backend.SetGeometryArena(false);
        check(backend.GetStats().ValidationErrors==0,"distance-history has zero core/synchronization validation errors");
    }
}
