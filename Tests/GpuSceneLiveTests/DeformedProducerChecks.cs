using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using FixtureAssets;
using System.Collections;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

partial class Program
{
    static void DeformedProducerChecks(NativeBackend backend,VulkanScene scene,IDictionary actors,string output,Func<byte[]> render,Action<MatrixD> projection,Action<bool,string> check)
    {
        var saved=scene.RenderSettings;var settings=saved;settings.User.DistanceFade=10000;scene.RenderSettings=settings;
        string model=MeshDecalFixtureAssets.Model(Path.Combine(output,"DeformedProducerAssets"),MyMeshDrawTechnique.MESH,7,255,deformed:true,neutral:true);
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=400,Model=model,WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline|RenderFlags.DistanceFade});
        var cube=new VRage.MyCubeInstanceData{LocalMatrix=Matrix.Identity,EnableSkinning=true,BoneRange=1};for(int i=0;i<9;i++)cube[i]=new Vector3UByte(127,127,127);
        scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=401});
        void Source(){scene.Process(new MyRenderMessageUpdateRenderCubeInstanceBuffer{ID=401,InstanceData=new List<VRage.MyCubeInstanceData>{cube},Capacity=1});}
        Source();scene.Process(new MyRenderMessageSetInstanceBuffer{ID=400,InstanceBufferId=401,InstanceCount=1,LocalAabb=new BoundingBox(new Vector3(-2),new Vector3(2))});
        var visible=render();var data=actors[400u]!;
        bool Owned()=>(bool)data.GetType().GetField("GpuOwned")!.GetValue(data)!&&((ICollection)typeof(NativeBackend).GetField("worldPackets",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(backend)!).Count==0;
        byte[][] Buffers()=>Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();
        int Coverage()=>backend.ReadbackWorldGbuffer(0,64,64).Where((v,i)=>i%4==3&&v!=255).Count();
        check(Owned()&&Convert.ToUInt32(data.GetType().GetField("GpuType")!.GetValue(data))==4&&Coverage()>100&&visible.Where((v,i)=>i%4<3).Any(v=>v!=0),"normal scene producer admits a visible genuine deformed DF stream without managed packets");
        var gbuffer=Buffers();scene.GpuOpaqueOwnershipEnabled=false;check(render().SequenceEqual(visible)&&Buffers().Select((v,i)=>v.SequenceEqual(gbuffer[i])).All(v=>v),"genuine deformed producer matches every fallback GBuffer channel and final pixels");
        scene.GpuOpaqueOwnershipEnabled=true;check(render().SequenceEqual(visible)&&Owned(),"deformed producer reentry restores GPU ownership and pixels");
        for(int i=0;i<9;i++)cube[i]=new Vector3UByte(191,127,127);Source();var changed=render();bool changedOwned=Owned();var changedGeometry=Buffers();
        if(!changedOwned||changedGeometry.Select((v,i)=>v.SequenceEqual(gbuffer[i])).All(v=>v)){
            var stream=(byte[])data.GetType().GetField("GpuSourceBytes")!.GetValue(data)!;var authored=data.GetType().GetField("Model")!.GetValue(data)!;var vertices=(NativeWorldVertex[])authored.GetType().GetField("WorldVertices")!.GetValue(authored)!;
            Console.WriteLine("DEFORMED producer diagnostic owned="+changedOwned+" sourceBone0="+stream[0]+" sourceSkin="+stream[15]+" range="+stream[19]+" weight="+vertices[0].WeightX+" reason="+data.GetType().GetField("GpuFallbackReason")!.GetValue(data));
            File.WriteAllBytes(Path.Combine(output,"deformed-original.rgba8"),visible);File.WriteAllBytes(Path.Combine(output,"deformed-changed-gpu.rgba8"),changed);
            scene.GpuOpaqueOwnershipEnabled=false;var fallback=render();File.WriteAllBytes(Path.Combine(output,"deformed-changed-fallback.rgba8"),fallback);Console.WriteLine("DEFORMED producer diagnostic fallbackChanged="+!fallback.SequenceEqual(visible)+" gpuMatchesFallback="+fallback.SequenceEqual(changed));
        }
        check(changedOwned&&!changedGeometry.Select((v,i)=>v.SequenceEqual(gbuffer[i])).All(v=>v),"actual GPU deformed bone update changes visible GBuffer geometry");
        gbuffer=Buffers();scene.GpuOpaqueOwnershipEnabled=false;check(render().SequenceEqual(changed)&&Buffers().Select((v,i)=>v.SequenceEqual(gbuffer[i])).All(v=>v),"updated deformed source matches actual fallback geometry in every channel");
        scene.GpuOpaqueOwnershipEnabled=true;check(render().SequenceEqual(changed)&&Owned(),"updated deformed source reentry retains GPU ownership");
        var farProjection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,10000);farProjection.M13=farProjection.M14-farProjection.M13;farProjection.M23=farProjection.M24-farProjection.M23;farProjection.M33=farProjection.M34-farProjection.M33;farProjection.M43=farProjection.M44-farProjection.M43;projection(farProjection);
        for(int i=0;i<9;i++)cube[i]=new Vector3UByte(127,127,127);Source();var actor=(VRage.Render.Scene.MyActor)data.GetType().GetField("Actor")!.GetValue(data)!;var far=MatrixD.CreateScale(350)*MatrixD.CreateTranslation(0,0,-1050);actor.SetMatrix(ref far);
        var farVisible=render();var farGeometry=Buffers();check(Owned()&&Coverage()>100,"far deformed DF control remains visible and GPU-owned");
        settings.User.DistanceFade=900;scene.RenderSettings=settings;render();check(Owned()&&Coverage()==0,"actual deformed GPU producer honors far distance-fade position collapse");
        settings.User.DistanceFade=10000;scene.RenderSettings=settings;check(render().SequenceEqual(farVisible)&&Owned()&&Buffers().Select((v,i)=>v.SequenceEqual(farGeometry[i])).All(v=>v),"deformed GPU producer threshold roundtrip restores exact far pixels");
        scene.GpuOpaqueOwnershipEnabled=false;check(render().SequenceEqual(farVisible)&&Buffers().Select((v,i)=>v.SequenceEqual(farGeometry[i])).All(v=>v),"far deformed DF producer matches fallback pixels and geometry");scene.GpuOpaqueOwnershipEnabled=true;
        scene.Clear();scene.RenderSettings=saved;render();check(!actors.Contains(400u),"deformed producer cleanup retires actor and source ownership");
    }
}
