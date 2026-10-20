using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using FixtureAssets;
using VRage;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

partial class Program
{
    static void IndexedMotionChecks(NativeBackend backend,VulkanScene scene,string content,MatrixD projection,Func<byte[]> render,Action renderAsync,Func<bool> owned,Func<int> packets,Action<MatrixD> setProjection,Action<Vector3D> setCamera,Action<bool,string> check)
    {
        const int size=64;string output=Path.GetFullPath("TestResults/IndexedMotion");Directory.CreateDirectory(output);
        object Call(string method,params object[] args)=>typeof(NativeBackend).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(backend,args)!;
        using var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"Compiler"));
        byte[] Compile(string name,string source,string profile){string file=Path.Combine(output,name+".hlsl");File.WriteAllText(file,source);return SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiler.Compile(new VulkanShaderRequest{SourcePath=file,ContentDirectory=output,EntryPoint="main",Profile=profile,NativeAbiVersion=1}).SpirvPath));}
        var vertex=Compile("fullscreen","float4 main(uint id:SV_VertexID):SV_Position{float2 p=float2((id<<1)&2,id&2);return float4(p*float2(2,-2)+float2(-1,1),0,1);}","vs_5_0");
        var pixel=Compile("observe","Texture2D<float4> Input:register(t0);float4 main(float4 p:SV_Position):SV_Target{return Input.Load(int3((int2)p.xy,0));}","ps_5_0");
        ulong pipeline=(ulong)Call("GuestCreatePipeline2",vertex,pixel,"main","main",1);check(pipeline!=0,"indexed motion observation pipeline initializes");Call("GuestCreateImage",99400ul,size,size,1,1);
        float HalfAt(byte[] bytes,int i,int c)=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*8+c*2));
        var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        VRage.Render.Scene.MyActor Actor(uint id){var data=actors[id]!;return (VRage.Render.Scene.MyActor)data.GetType().GetField("Actor")!.GetValue(data)!;}
        bool IsOwned(uint id){var data=actors[id]!;return (bool)data.GetType().GetField("GpuOwned")!.GetValue(data)!;}
        byte[] Observe(){Call("GuestReset");check((bool)Call("GuestSubmit2",4,5,size,size,1,pipeline,new[]{99400ul},null!,new[]{0xFFFF000000000008ul},2048),"indexed motion observation packet accepted");Call("GuestReplayNow",4);var bytes=new byte[size*size*8];Call("GuestReadback",99400ul,bytes);return bytes;}
        float previousX=0,previousSource=0,currentSource=0;bool hasOther=true;
        void Verify(string label,float x,bool reset=false){render();check(owned()&&packets()==0&&(!hasOther||IsOwned(201)),"moving actors remain GPU owned with zero managed packets");
            var depth=backend.ReadbackWorldGbuffer(3,size,size);var values=Enumerable.Range(0,size*size).Select(i=>BitConverter.ToSingle(depth,i*4)).ToArray();float front=values.Max();double eyeDepth=projection.M43/(front+projection.M33),split=size*.5+((x+3.25)*.5+currentSource)*projection.M11*size/(2*eyeDepth);
            var bytes=Observe();
            for(int which=0;which<(hasOther?2:1);which++){
                var covered=Enumerable.Range(0,size*size).Where(i=>front>0&&Math.Abs(values[i]-front)<1e-8&&(!hasOther||(which==0?i%size+.5<split-1:i%size+.5>split+1))).ToArray();check(covered.Length>20,"indexed motion oracle observes actor "+which+" flat front pixels");
                double expected=reset?0:((which==0?previousX-x:0)+previousSource-currentSource)*projection.M11*size/(2*eyeDepth);
                double error=covered.Select(i=>Math.Max(Math.Abs(HalfAt(bytes,i,0)-expected),Math.Abs(HalfAt(bytes,i,1)))).Max();
                Console.WriteLine("INDEXED MOTION "+label+" actor="+which+" samples="+covered.Length+" expected="+expected+" maxError="+error+" validationErrors="+backend.GetStats().ValidationErrors);
                check(error<.01,"indexed motion matches independent actor/source projection");
            }
            File.WriteAllBytes(Path.Combine(output,label+".rgba16f"),bytes);previousX=x;previousSource=currentSource;
        }
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=201,Model=Path.Combine(content,"Models/Cubes/Large/ArmorCenter.mwm"),WorldMatrix=MatrixD.CreateTranslation(3.25,0,-5),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        foreach(float x in new[]{0f,.5f,0f,.5f}){var pose=MatrixD.CreateTranslation(x,0,0);Actor(100).SetMatrix(ref pose);Verify("new-"+x,x);}
        var originalProjection=projection;var zoom=MatrixD.CreatePerspectiveFieldOfView(Math.PI/3,1,.1,100);zoom.M13=zoom.M14-zoom.M13;zoom.M23=zoom.M24-zoom.M23;zoom.M33=zoom.M34-zoom.M33;zoom.M43=zoom.M44-zoom.M43;
        foreach(var target in new[]{zoom,originalProjection}){
            var before=projection;projection=target;setProjection(projection);render();check(owned()&&IsOwned(201)&&packets()==0,"projection change preserves GPU ownership");var depth=backend.ReadbackWorldGbuffer(3,size,size);float front=Enumerable.Range(0,size*size).Select(i=>BitConverter.ToSingle(depth,i*4)).Max();var bytes=Observe();int samples=0;double error=0,excess=double.NegativeInfinity;
            // Motion is RG16F: a fixed .01-pixel bound is smaller than one
            // representable step above 16 pixels. Budget one half-float step
            // plus .001 pixel for raster interpolation of vertex motion.
            double Step(double value){ushort bits=BitConverter.HalfToUInt16Bits((Half)Math.Abs(value));return (double)BitConverter.UInt16BitsToHalf((ushort)(bits+1))-(double)BitConverter.UInt16BitsToHalf(bits);}
            for(int i=0;i<size*size;i++){if(front<=0||Math.Abs(BitConverter.ToSingle(depth,i*4)-front)>=1e-8)continue;double x=(i%size+.5-size*.5)*(before.M11/projection.M11-1),y=(i/size+.5-size*.5)*(before.M22/projection.M22-1);double dx=Math.Abs(HalfAt(bytes,i,0)-x),dy=Math.Abs(HalfAt(bytes,i,1)-y);error=Math.Max(error,Math.Max(dx,dy));excess=Math.Max(excess,Math.Max(dx-Step(x)-.001,dy-Step(y)-.001));samples++;}
            Console.WriteLine("INDEXED PROJECTION MOTION samples="+samples+" maxError="+error+" maxQuantizationBudgetExcess="+excess);check(samples>40,"projection motion oracle observes flat front pixels");check(excess<=0,"indexed motion uses the actual previous projection within RG16F precision");
        }
        var saved=scene.RenderSettings;var nearProjection=projection;
        foreach(uint type in new uint[]{0,2,3,4}){
            projection=nearProjection;setProjection(projection);hasOther=true;
            foreach(uint id in new uint[]{200,201})scene.Process(new MyRenderMessageRemoveRenderObject{ID=id});
            var settings=saved;settings.User.DistanceFade=10000;scene.RenderSettings=settings;
            string model=MeshDecalFixtureAssets.Model(Path.Combine(output,"Models-"+type),MyMeshDrawTechnique.MESH,7,255,deformed:type==4,singleLayer:true,neutral:true);
            foreach(uint id in new uint[]{200,201})scene.Process(new MyRenderMessageCreateRenderEntity{ID=id,Model=model,WorldMatrix=MatrixD.CreateTranslation(id==200?0:3.25,0,-5),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline|RenderFlags.DistanceFade});
            uint buffer=9700+type;var cube=new MyCubeInstanceData{LocalMatrix=Matrix.Identity};if(type==4){cube.EnableSkinning=true;cube.BoneRange=1;for(int i=0;i<9;i++)cube[i]=new Vector3UByte(127,127,127);}
            void Source(float x,bool deform=false){if(type==2)scene.Process(new MyRenderMessageUpdateRenderInstanceBufferRange{ID=buffer,StartOffset=0,Trim=true,InstanceData=new[]{new MyInstanceData(Matrix.CreateTranslation(x,0,0))}});
                else if(type>=3){cube.LocalMatrix=Matrix.CreateTranslation(x,0,0);if(type==4)for(int i=0;i<9;i++)cube[i]=new Vector3UByte((byte)(deform?191:127),127,127);scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=buffer,InstanceData=new List<MyCubeInstanceData>{cube},Capacity=1});}}
            if(type>=2){scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=buffer});Source(0);foreach(uint id in new uint[]{200,201})scene.Process(new MyRenderMessageSetInstanceBuffer{ID=id,InstanceBufferId=buffer,InstanceCount=1,LocalAabb=new BoundingBox(new Vector3(-2),new Vector3(2))});}
            previousX=previousSource=currentSource=0;
            foreach(float x in new[]{0f,.5f,0f,.5f}){var pose=MatrixD.CreateTranslation(x,0,-5);Actor(200).SetMatrix(ref pose);Verify("old-"+type+"-"+x,x,x==0&&previousX==0);}
            if(type>=2){var firstData=actors[200u]!;var secondData=actors[201u]!;check(Convert.ToUInt64(firstData.GetType().GetField("GpuSource")!.GetValue(firstData))!=Convert.ToUInt64(secondData.GetType().GetField("GpuSource")!.GetValue(secondData)),"distinct resident source handles exercise different global source offsets");}
            if(type>=2){Source(.25f);currentSource=.25f;Verify("source-"+type,.5f);if(type==4){Source(.25f,true);currentSource=.25f+128f/255;Verify("deformed-source",.5f);}}
            if(type>=2){
                scene.Process(new MyRenderMessageRemoveRenderObject{ID=201});hasOther=false;Source(0);currentSource=0;
                projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,10000);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;setProjection(projection);
                var farPose=MatrixD.CreateScale(350)*MatrixD.CreateTranslation(0,0,-1050);Actor(200).SetMatrix(ref farPose);settings.User.DistanceFade=10000;scene.RenderSettings=settings;render();render();
                var vertices=new[]{new Vector3D(-1,-1,1.0/64),new Vector3D(1,-1,1.0/64),new Vector3D(1,1,1.0/64),new Vector3D(-1,1,1.0/64)};
                Vector3D World(Vector3D v,float threshold,float x,bool fade){var unscaled=v*350+new Vector3D(x,0,-1050);double factor=fade?Math.Clamp((unscaled.Length()-Math.Max(threshold,900))/(threshold/10),0,1):0;return v*(350*(1-factor))+new Vector3D(x,0,-1050);}
                Vector2D Screen(Vector3D p){var c=Vector4D.Transform(new Vector4D(p,1),projection);return new Vector2D((c.X/c.W*.5+.5)*size,(.5-c.Y/c.W*.5)*size);}
                float priorThreshold=10000,priorX=0;bool priorFade=true;
                foreach(var farStep in new[]{(1100f,0f,true),(1100f,25f,true),(10000f,0f,true),(1100f,0f,true),(1100f,0f,false)}){
                    if(!farStep.Item3){var data=actors[200u]!;data.GetType().GetField("Flags")!.SetValue(data,RenderFlags.Visible|RenderFlags.ForceOldPipeline);typeof(VulkanScene).GetMethod("MarkGeometry",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(scene,new[]{data});}
                    settings.User.DistanceFade=farStep.Item1;scene.RenderSettings=settings;farPose=MatrixD.CreateScale(350)*MatrixD.CreateTranslation(farStep.Item2,0,-1050);Actor(200).SetMatrix(ref farPose);render();check(owned()&&packets()==0,"far motion remains GPU owned through fade/flag changes");
                    var depth=backend.ReadbackWorldGbuffer(3,size,size);var bytes=Observe();var current=vertices.Select(v=>World(v,farStep.Item1,farStep.Item2,farStep.Item3)).ToArray();var before=vertices.Select(v=>World(v,priorThreshold,priorX,priorFade)).ToArray();var screen=current.Select(Screen).ToArray();var delta=before.Select(Screen).Zip(screen,(a,b)=>a-b).ToArray();
                    double Cross(Vector2D a,Vector2D b)=>a.X*b.Y-a.Y*b.X;double error=0;int samples=0;
                    for(int pixelIndex=0;pixelIndex<size*size;pixelIndex++){
                        if(BitConverter.ToSingle(depth,pixelIndex*4)<=0)continue;var point=new Vector2D(pixelIndex%size+.5,pixelIndex/size+.5);
                        foreach(var triangle in new[]{new[]{0,1,2},new[]{0,2,3}}){int a=triangle[0],b=triangle[1],c=triangle[2];double area=Cross(screen[b]-screen[a],screen[c]-screen[a]);double l1=Cross(point-screen[a],screen[c]-screen[a])/area,l2=Cross(screen[b]-screen[a],point-screen[a])/area,l0=1-l1-l2;if(Math.Min(l0,Math.Min(l1,l2))<.08)continue;
                            var weights=new[]{l0/-current[a].Z,l1/-current[b].Z,l2/-current[c].Z};double sum=weights.Sum();var expected=Vector2D.Zero;for(int v=0;v<3;v++)expected+=delta[triangle[v]]*(weights[v]/sum);
                            error=Math.Max(error,Math.Max(Math.Abs(HalfAt(bytes,pixelIndex,0)-expected.X),Math.Abs(HalfAt(bytes,pixelIndex,1)-expected.Y)));samples++;break;
                        }
                    }
                    Console.WriteLine("INDEXED FAR MOTION type="+type+" threshold="+farStep.Item1+" x="+farStep.Item2+" fade="+farStep.Item3+" samples="+samples+" maxError="+error);File.WriteAllBytes(Path.Combine(output,$"far-{type}-{farStep.Item1}-{farStep.Item2}-{farStep.Item3}.rgba16f"),bytes);
                    check(samples>10,"far motion oracle observes surviving interior pixels");check(error<.01,"indexed current/previous fade motion matches independent vertex projection");priorThreshold=farStep.Item1;priorX=farStep.Item2;priorFade=farStep.Item3;
                }
                // Two independently moving rows after an unused authored row.
                // The producer packs that range; this tests local indexing
                // within one OLD indirect command. The two-actor cases above
                // exercise distinct global source offsets.
                projection=nearProjection;setProjection(projection);settings.User.DistanceFade=10000;scene.RenderSettings=settings;var nearPose=MatrixD.CreateTranslation(0,0,-5);Actor(200).SetMatrix(ref nearPose);
                MyCubeInstanceData Cube(float x){var value=new MyCubeInstanceData{LocalMatrix=Matrix.CreateTranslation(x,0,0)};if(type==4){value.EnableSkinning=true;value.BoneRange=1;for(int i=0;i<9;i++)value[i]=new Vector3UByte(127,127,127);}return value;}
                void Multi(float first,float second){if(type==2)scene.Process(new MyRenderMessageUpdateRenderInstanceBufferRange{ID=buffer,StartOffset=0,Trim=true,InstanceData=new[]{new MyInstanceData(Matrix.CreateTranslation(100,0,0)),new MyInstanceData(Matrix.CreateTranslation(first,0,0)),new MyInstanceData(Matrix.CreateTranslation(second,0,0))}});
                    else scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=buffer,InstanceData=new List<MyCubeInstanceData>{Cube(100),Cube(first),Cube(second)},Capacity=3});}
                Multi(-.75f,.75f);scene.Process(new MyRenderMessageSetInstanceBuffer{ID=200,InstanceBufferId=buffer,InstanceStart=1,InstanceCount=2,LocalAabb=new BoundingBox(new Vector3(-2),new Vector3(2))});Verify("source-count-reset-"+type,0,true);render();
                Multi(-.65f,.55f);render();check(owned()&&packets()==0,"multi-instance motion stays GPU owned with authored InstanceStart=1");var multiDepth=backend.ReadbackWorldGbuffer(3,size,size);var multiMotion=Observe();float front=Enumerable.Range(0,size*size).Select(i=>BitConverter.ToSingle(multiDepth,i*4)).Max();double eye=projection.M43/(front+projection.M33);
                for(int instance=0;instance<2;instance++){
                    var samples=Enumerable.Range(0,size*size).Where(i=>front>0&&Math.Abs(BitConverter.ToSingle(multiDepth,i*4)-front)<1e-8).Where(i=>{double worldX=(i%size+.5-size*.5)*2*eye/(projection.M11*size);return instance==0?worldX<.55-1-.05:worldX>-.65+1+.05;}).ToArray();
                    double expected=(instance==0?-.75f-(-.65f):.75f-.55f)*projection.M11*size/(2*eye),error=samples.Select(i=>Math.Max(Math.Abs(HalfAt(multiMotion,i,0)-expected),Math.Abs(HalfAt(multiMotion,i,1)))).DefaultIfEmpty(double.PositiveInfinity).Max();
                    Console.WriteLine("INDEXED SOURCE RANGE type="+type+" instance="+instance+" samples="+samples.Length+" expected="+expected+" maxError="+error);check(samples.Length>20,"source-range motion oracle has unoccluded pixels");check(error<.01,"indexed motion matches each prior row of the selected source range");
                }
                for(int skipped=0;skipped<2;skipped++){backend.BeginPacketCollection();check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"indexed history skips a world frame");backend.EndFrame();}
                nearPose=MatrixD.CreateTranslation(.5,0,-5);Actor(200).SetMatrix(ref nearPose);Verify("gap-reset-"+type,.5f,true);nearPose=MatrixD.CreateTranslation(0,0,-5);Actor(200).SetMatrix(ref nearPose);Verify("gap-recovery-"+type,0);
                setCamera(new Vector3D(.25,0,0));Verify("camera-shift-"+type,-.25f);setCamera(Vector3D.Zero);Verify("camera-return-"+type,0);
            }
        }
        // Grow the previous-source allocation with real deformed OLD input.
        // Only row zero is on screen; the rest still occupy source history.
        foreach(int count in new[]{16,1024,4096}){
            var source=new List<MyCubeInstanceData>(count);for(int i=0;i<count;i++){var cube=new MyCubeInstanceData{LocalMatrix=Matrix.CreateTranslation(i==0?0:100+i,0,0),BoneRange=1};cube.EnableSkinning=true;for(int bone=0;bone<9;bone++)cube[bone]=new Vector3UByte(127,127,127);source.Add(cube);}
            scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=9704,InstanceData=source,Capacity=count});scene.Process(new MyRenderMessageSetInstanceBuffer{ID=200,InstanceBufferId=9704,InstanceStart=0,InstanceCount=count,LocalAabb=new BoundingBox(new Vector3(-2),new Vector3(2))});
            for(int steady=0;steady<3;steady++){renderAsync();check(owned()&&packets()==0,"asynchronous indexed source growth retains GPU ownership: "+count+"/"+steady);check(backend.GetStats().ValidationErrors==0,"indexed source history growth has no validation errors: "+count+"/"+steady);}
        }
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=200});
        int created=0;
        foreach(int count in new[]{64,1024,4096}){
            for(;created<count;created++)scene.Process(new MyRenderMessageCreateRenderEntity{ID=(uint)(200+created),Model=Path.Combine(content,"Models/Cubes/Large/ArmorCenter.mwm"),WorldMatrix=MatrixD.CreateTranslation(created==0?0:100+created,0,-5),Flags=RenderFlags.Visible});
            for(int steady=0;steady<3;steady++){renderAsync();check(owned()&&packets()==0&&IsOwned((uint)(199+count)),"asynchronous indexed actor growth retains GPU ownership: "+count+"/"+steady);check(backend.GetStats().ValidationErrors==0,"indexed actor history growth has no validation errors: "+count+"/"+steady);}
        }
        var stressDepth=backend.ReadbackWorldGbuffer(3,size,size);check(Enumerable.Range(0,size*size).Any(i=>BitConverter.ToSingle(stressDepth,i*4)>0),"asynchronous indexed history completes positive geometry");
        scene.RenderSettings=saved;
        check(backend.GetStats().ValidationErrors==0,"indexed motion has zero core/synchronization validation errors");
    }
}
