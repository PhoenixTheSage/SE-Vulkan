using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using FixtureAssets;
using System.Reflection;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

partial class Program
{
    static void DistanceFadeSceneChecks(NativeBackend backend,VulkanScene scene,string output,Func<byte[]> direct,Func<byte[]> retained,Func<bool,Action,byte[]> configured,Action<Vector3D> setCamera,Action<bool,string> check)
    {
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=10});
        string model=MeshDecalFixtureAssets.Model(Path.Combine(output,"DistanceFadeAssets"),MyMeshDrawTechnique.MESH,7,255);
        var settings=scene.RenderSettings;settings.User.DistanceFade=1;scene.RenderSettings=settings;
        int Coverage()=>backend.ReadbackWorldGbuffer(0,64,64).Where((value,index)=>index%4==3&&value!=255).Count();
        var packets=typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!;
        string deformedModel=MeshDecalFixtureAssets.Model(Path.Combine(output,"DistanceFadeAssets"),MyMeshDrawTechnique.MESH,7,255,deformed:true);
        VRage.MyCubeInstanceData Cube(uint kind){var value=new VRage.MyCubeInstanceData{LocalMatrix=Matrix.CreateTranslation(0,0,-3)};if(kind==4){value.EnableSkinning=true;value.BoneRange=1;for(int i=0;i<9;i++)value[i]=new Vector3UByte(127,127,127);}return value;}
        foreach(bool forceOld in new[]{true,false})foreach(uint kind in new uint[]{2,3,4})foreach(bool fade in new[]{false,true}){
            bool cube=kind>=3;
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=kind==4?deformedModel:model,WorldMatrix=MatrixD.Identity,Flags=RenderFlags.Visible|(forceOld?RenderFlags.ForceOldPipeline:0)|(fade?RenderFlags.DistanceFade:0)});
            if(cube){scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=2900});scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=2900,InstanceData=new List<VRage.MyCubeInstanceData>{Cube(kind)},Capacity=1});}
            scene.Process(new MyRenderMessageSetInstanceBuffer{ID=120,InstanceBufferId=2900,InstanceCount=1,InstanceData=cube?null:new[]{new VRage.MyInstanceData(Matrix.CreateTranslation(0,0,-3))},LocalAabb=new BoundingBox(new Vector3(-1,-1,-3),new Vector3(1,1,-2.9f))});
            direct();check(((List<WorldPacket>)packets.GetValue(backend)!).Count==2&&((List<WorldPacket>)packets.GetValue(backend)!).All(p=>p.Pipeline==kind+(fade?2560u:0u)),"distance-fade source stream selects the exact shader class: "+kind+" fade="+fade+" forceOld="+forceOld);int coverage=Coverage();check(fade&&!cube?coverage==0:coverage>100,"actual OLD instance honors DistanceFade="+fade+" stream="+kind+" (coverage="+coverage+")");
            var reference=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();retained();check(reference.Select((v,i)=>v.SequenceEqual(backend.ReadbackWorldGbuffer((uint)i,64,64))).All(v=>v),"retained distance-fade submission preserves every GBuffer channel: "+fade);
            scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
        }
        // All four vertices in this single quad are equidistant. Compare
        // source-computed fade against the analytically equivalent explicit
        // custom-alpha input, retaining stock pixel dither as the oracle.
        string single=MeshDecalFixtureAssets.Model(Path.Combine(output,"DistanceFadeAssets"),MyMeshDrawTechnique.MESH,7,255,singleLayer:true);
        float vertexDistance=new Vector3(1,1,-3+.015625f).Length();
        foreach(float alpha in new[]{.23f,.53f,.83f}){
            settings=scene.RenderSettings;settings.User.DistanceFade=vertexDistance/(1+alpha/10);scene.RenderSettings=settings;
            byte[][] Capture(bool fade){
                scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=single,WorldMatrix=MatrixD.Identity,Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline|(fade?RenderFlags.DistanceFade:0)});
                scene.Process(new MyRenderMessageSetInstanceBuffer{ID=120,InstanceBufferId=2900,InstanceCount=1,InstanceData=new[]{new VRage.MyInstanceData(Matrix.CreateTranslation(0,0,-3))},LocalAabb=new BoundingBox(new Vector3(-1,-1,-3),new Vector3(1,1,-2.9f))});
                if(!fade)scene.Process(new MyRenderMessageUpdateRenderEntity{ID=120,Dithering=alpha});
                direct();var result=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();
                int coverage=Coverage();check(coverage>0&&coverage<484,"intermediate fade has partial visible coverage: "+alpha+" shader="+fade+" pixels="+coverage);
                retained();check(result.Select((v,i)=>v.SequenceEqual(backend.ReadbackWorldGbuffer((uint)i,64,64))).All(v=>v),"retained intermediate fade preserves every channel: "+alpha+" shader="+fade);
                scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();return result;
            }
            var reference=Capture(false);var actual=Capture(true);
            check(reference.Select((v,i)=>v.SequenceEqual(actual[i])).All(v=>v),"source distance fade equals analytic explicit-alpha dither in every GBuffer channel: "+alpha);
        }
        // Cube output replaces customAlpha.y after the distance branch, so
        // its near-field dither is unchanged. Beyond max(threshold,900), the
        // stock position scaling still collapses its geometry. A far projection
        // and uniform model scale keep the positive control clearly visible.
        settings=scene.RenderSettings;settings.User.DistanceFade=900;scene.RenderSettings=settings;
        foreach(uint kind in new uint[]{3,4})foreach(bool fade in new[]{false,true}){
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=kind==4?deformedModel:model,WorldMatrix=MatrixD.CreateScale(350),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline|(fade?RenderFlags.DistanceFade:0)});
            scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=2900});scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=2900,InstanceData=new List<VRage.MyCubeInstanceData>{Cube(kind)},Capacity=1});
            scene.Process(new MyRenderMessageSetInstanceBuffer{ID=120,InstanceBufferId=2900,InstanceCount=1,LocalAabb=new BoundingBox(new Vector3(-1,-1,-3),new Vector3(1,1,-2.9f))});
            direct();int coverage=Coverage();check(fade?coverage==0:coverage>100,"stock cube distance scaling honors DistanceFade="+fade+" stream="+kind+" (coverage="+coverage+")");
            var reference=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();retained();check(reference.Select((v,i)=>v.SequenceEqual(backend.ReadbackWorldGbuffer((uint)i,64,64))).All(v=>v),"retained cube distance scaling preserves every GBuffer channel: "+fade);
            scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
        }
        backend.SetShadowQuality(256,0);
        foreach(uint kind in new uint[]{2,3,4})foreach(bool fade in new[]{false,true}){
            bool cube=kind>=3;float extent=cube?1400:4;
            var shadow=MatrixD.CreateLookAt(Vector3D.Zero,new Vector3D(0,0,-3),Vector3D.Up)*MatrixD.CreateOrthographic(extent,extent,1,10000);
            backend.SetShadowCascades(NativeBackend.MatrixValues(shadow),new[]{10000f});var constants=new byte[1200];BitConverter.GetBytes(1u).CopyTo(constants,1192);backend.SetShadowPassConstants(constants);
            settings=scene.RenderSettings;settings.User.DistanceFade=cube?900:1;scene.RenderSettings=settings;
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=kind==4?deformedModel:model,WorldMatrix=cube?MatrixD.CreateScale(350):MatrixD.Identity,Flags=RenderFlags.Visible|RenderFlags.CastShadows|RenderFlags.NoBackFaceCulling|RenderFlags.ForceOldPipeline|(fade?RenderFlags.DistanceFade:0)});
            if(cube){scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=2900});scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=2900,InstanceData=new List<VRage.MyCubeInstanceData>{Cube(kind)},Capacity=1});}
            scene.Process(new MyRenderMessageSetInstanceBuffer{ID=120,InstanceBufferId=2900,InstanceCount=1,InstanceData=cube?null:new[]{new VRage.MyInstanceData(Matrix.CreateTranslation(0,0,-3))},LocalAabb=new BoundingBox(new Vector3(-1,-1,-3),new Vector3(1,1,-2.9f))});
            direct();var depth=backend.ReadbackShadow(0,256);int coverage=depth.Count(value=>value<1);
            check(fade?coverage==0:coverage>100,"stock cascade executes distance-fade depth variant: stream="+kind+" fade="+fade+" depth pixels="+coverage);
            retained();check(depth.SequenceEqual(backend.ReadbackShadow(0,256)),"retained distance fade preserves exact stock cascade depth: stream="+kind+" fade="+fade);
            scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
        }
        // A global threshold change must invalidate a cached map even when
        // actor identity, pose, shader class and cascade projection stay fixed.
        var fixedShadow=MatrixD.CreateLookAt(Vector3D.Zero,new Vector3D(0,0,-3),Vector3D.Up)*MatrixD.CreateOrthographic(4,4,1,10000);
        backend.SetShadowCascades(NativeBackend.MatrixValues(fixedShadow),new[]{10000f});var fixedConstants=new byte[1200];BitConverter.GetBytes(1u).CopyTo(fixedConstants,1192);backend.SetShadowPassConstants(fixedConstants);
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=model,WorldMatrix=MatrixD.Identity,Flags=RenderFlags.Visible|RenderFlags.CastShadows|RenderFlags.NoBackFaceCulling|RenderFlags.ForceOldPipeline|RenderFlags.DistanceFade});
        scene.Process(new MyRenderMessageSetInstanceBuffer{ID=120,InstanceBufferId=2900,InstanceCount=1,InstanceData=new[]{new VRage.MyInstanceData(Matrix.CreateTranslation(0,0,-3))},LocalAabb=new BoundingBox(new Vector3(-1,-1,-3),new Vector3(1,1,-2.9f))});
        float? lastThreshold=null;
        foreach(float threshold in new[]{1000f,1000f,1f,1f,1000f,1000f}){
            settings=scene.RenderSettings;settings.User.DistanceFade=threshold;scene.RenderSettings=settings;
            direct();int coverage=backend.ReadbackShadow(0,256).Count(value=>value<1);
            check(threshold==1?coverage==0:coverage>100,"distance threshold invalidates cached cascade contents without actor/projection changes: "+threshold+" depth pixels="+coverage);
            if(lastThreshold==threshold)check(backend.GetPerformanceStats().ShadowCommands==0,"unchanged distance threshold preserves cached cascade reuse: "+threshold);
            lastThreshold=threshold;
        }
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
        backend.SetShadowPassConstants(null);backend.SetShadowCascades(Array.Empty<float>(),Array.Empty<float>());
        // Only the global fade input changes. Keep an off-cone second map
        // as a witness that unrelated cached lights do not get rebuilt.
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=model,WorldMatrix=MatrixD.Identity,Flags=RenderFlags.Visible|RenderFlags.CastShadows|RenderFlags.NoBackFaceCulling|RenderFlags.ForceOldPipeline|RenderFlags.DistanceFade});
        scene.Process(new MyRenderMessageSetInstanceBuffer{ID=120,InstanceBufferId=2900,InstanceCount=1,InstanceData=new[]{new VRage.MyInstanceData(Matrix.CreateTranslation(0,0,-3))},LocalAabb=new BoundingBox(new Vector3(-1,-1,-3),new Vector3(1,1,-2.9f))});
        direct(); // Finish the actor-admission dirty frame before testing a static cache.
        backend.SetShadowPassConstants(new byte[1200]); // Zero cascades, stock spotlight raster profile.
        var spotProjection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);
        var otherProjection=MatrixD.CreateLookAt(new Vector3D(30,0,0),new Vector3D(30,0,-1),Vector3D.Up)*spotProjection;
        bool firstSpot=true;float? previousSpotThreshold=null;
        foreach(float threshold in new[]{1000f,1000f,1f,1f,1000f,1000f}){
            settings=scene.RenderSettings;settings.User.DistanceFade=threshold;scene.RenderSettings=settings;
            ulong mapsBefore=backend.GetSpotStats().Maps;
            configured(false,()=>{
                backend.SetLights(new[]{new NativeLight{PositionRange=new float[]{0,0,0,20},ColorIntensity=new float[]{1,1,1,1},DirectionCone=new float[]{0,0,-1,.5f},Flags=1},new NativeLight{PositionRange=new float[]{30,0,0,20},ColorIntensity=new float[]{1,1,1,1},DirectionCone=new float[]{0,0,-1,.5f},Flags=1}});
                backend.SetSpots(256,new[]{new NativeSpot{Light=0,Flags=1,Projection=NativeBackend.MatrixValues(spotProjection)},new NativeSpot{Light=1,Flags=1,Projection=NativeBackend.MatrixValues(otherProjection)}});
                backend.SetSpotRedraw(firstSpot?3ul:0ul,firstSpot?3ul:0ul);
                if(firstSpot)Console.WriteLine("SPOT admission "+string.Join("; ",((List<WorldPacket>)packets.GetValue(backend)!).Select(p=>"pipeline="+p.Pipeline+" flags="+p.Flags+" origin="+p.Origin+" radius="+p.Radius)));
            });
            int coverage=backend.ReadbackSpot(0,256).Count(value=>value<1);ulong rebuilt=backend.GetSpotStats().Maps-mapsBefore;
            check(threshold==1?coverage==0:coverage>100,"distance threshold updates cached spotlight geometry: "+threshold+" depth pixels="+coverage);
            check(backend.ReadbackSpot(1,256).All(value=>value==1),"off-cone spotlight remains empty at threshold "+threshold);
            if(!firstSpot)check(rebuilt==(previousSpotThreshold==threshold?0ul:1ul),"only an affected spotlight rebuilds on a changed fade input: "+threshold+" maps="+rebuilt);
            firstSpot=false;previousSpotThreshold=threshold;
        }
        settings=scene.RenderSettings;settings.User.DistanceFade=3.5f;scene.RenderSettings=settings;
        float? previousCamera=null;float[] originalSpot=null;
        foreach(float cameraZ in new[]{0f,0f,1f,1f,0f}){
            setCamera(new Vector3D(0,0,cameraZ));ulong mapsBefore=backend.GetSpotStats().Maps;
            configured(false,()=>{
                var position=new Vector3D(0,0,-cameraZ);var other=new Vector3D(30,0,-cameraZ);
                backend.SetLights(new[]{new NativeLight{PositionRange=new float[]{0,0,-cameraZ,20},ColorIntensity=new float[]{1,1,1,1},DirectionCone=new float[]{0,0,-1,.5f},Flags=1},new NativeLight{PositionRange=new float[]{30,0,-cameraZ,20},ColorIntensity=new float[]{1,1,1,1},DirectionCone=new float[]{0,0,-1,.5f},Flags=1}});
                backend.SetSpots(256,new[]{new NativeSpot{Light=0,Flags=1,Projection=NativeBackend.MatrixValues(MatrixD.CreateLookAt(position,position+Vector3D.Forward,Vector3D.Up)*spotProjection)},new NativeSpot{Light=1,Flags=1,Projection=NativeBackend.MatrixValues(MatrixD.CreateLookAt(other,other+Vector3D.Forward,Vector3D.Up)*spotProjection)}});
                backend.SetSpotRedraw(0,0);
            });
            var depth=backend.ReadbackSpot(0,256);int coverage=depth.Count(value=>value<1);ulong rebuilt=backend.GetSpotStats().Maps-mapsBefore;
            check(cameraZ==1?coverage==0:coverage>100,"camera-relative fade updates a physically fixed cached spotlight: cameraZ="+cameraZ+" depth pixels="+coverage);
            check(rebuilt==(previousCamera==cameraZ?0ul:1ul),"camera-dependent fade refreshes only an affected map, preserving steady reuse: maps="+rebuilt);
            if(cameraZ==0){if(originalSpot==null)originalSpot=depth;else check(depth.SequenceEqual(originalSpot),"camera roundtrip restores exact cached spotlight depth");}
            previousCamera=cameraZ;
        }
        setCamera(Vector3D.Zero);
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
        settings=scene.RenderSettings;settings.User.DistanceFade=1;scene.RenderSettings=settings;
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=model,WorldMatrix=MatrixD.Identity,Flags=RenderFlags.Visible});
        scene.Process(new MyRenderMessageSetInstanceBuffer{ID=120,InstanceBufferId=2900,InstanceCount=1,InstanceData=new[]{new VRage.MyInstanceData(Matrix.CreateTranslation(0,0,-3))},LocalAabb=new BoundingBox(new Vector3(-1,-1,-3),new Vector3(1,1,-2.9f))});
        var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;var actor=actors[120u]!;
        var actorFlags=actor.GetType().GetField("Flags")!;var markGeometry=typeof(VulkanScene).GetMethod("MarkGeometry",BindingFlags.Instance|BindingFlags.NonPublic)!;
        foreach(bool fade in new[]{false,true,false}){
            actorFlags.SetValue(actor,RenderFlags.Visible|(fade?RenderFlags.DistanceFade:0));markGeometry.Invoke(scene,new[]{actor});
            retained();int coverage=Coverage();check(fade?coverage==0:coverage>100,"retained shader cache follows a distance-fade flag roundtrip: "+fade+" pixels="+coverage);
        }
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
        foreach(bool old in new[]{false,true}){
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=model,WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|RenderFlags.DistanceFade|(old?RenderFlags.ForceOldPipeline:0)});
            direct();check(Coverage()>100,"distance-fade flag leaves a non-instanced source unchanged: old="+old);
            scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
        }
        check(backend.GetStats().ValidationErrors==0,"distance fade scene has zero Vulkan validation errors");
    }
}
