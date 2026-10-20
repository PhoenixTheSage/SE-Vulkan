using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using FixtureAssets;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

partial class Program
{
    static void MixedOrderingChecks(NativeBackend backend,VulkanScene scene,Func<byte[]> render,Action<bool,string> check)
    {
        const int size=64;string output=Path.GetFullPath("TestResults/MixedOrdering");Directory.CreateDirectory(output);
        var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        bool Owned(uint id,string field){var data=actors[id]!;return (bool)data.GetType().GetField(field)!.GetValue(data)!;}
        void Create(uint id,string model,double z,bool old)=>scene.Process(new MyRenderMessageCreateRenderEntity{ID=id,Model=model,WorldMatrix=MatrixD.CreateTranslation(0,0,z),Flags=RenderFlags.Visible|(old?RenderFlags.ForceOldPipeline:0)});
        byte[][] Capture(string label){render();var buffers=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,size,size)).ToArray();for(int i=0;i<4;i++)File.WriteAllBytes(Path.Combine(output,label+"-"+i+".bin"),buffers[i]);return buffers;}
        string root=Path.Combine(output,"Models");
        string background=MeshDecalFixtureAssets.Model(Path.Combine(root,"base"),MyMeshDrawTechnique.MESH,1,255,singleLayer:true,neutral:true);
        string redCutout=MeshDecalFixtureAssets.Model(Path.Combine(root,"cutout"),MyMeshDrawTechnique.DECAL_CUTOUT,1,255,singleLayer:true);
        string blueSolid=MeshDecalFixtureAssets.Model(Path.Combine(root,"solid"),MyMeshDrawTechnique.MESH,1,255,blue:true,singleLayer:true);
        string redBlend=MeshDecalFixtureAssets.Model(Path.Combine(root,"blend"),MyMeshDrawTechnique.DECAL,1,128,singleLayer:true);
        string blueCutout=MeshDecalFixtureAssets.Model(Path.Combine(root,"new-cutout"),MyMeshDrawTechnique.DECAL_CUTOUT,1,255,blue:true,singleLayer:true);
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=200});
        foreach(string mode in new[]{"gpu","native","managed"}){
            scene.GpuOpaqueOwnershipEnabled=mode=="gpu";scene.NativePassSceneEnabled=mode!="managed";
            Create(200,background,-5,true);Create(202,blueSolid,-4.99,false);
            var expected=Capture(mode+"-baseline");
            check(Owned(202,"GpuOwned")== (mode=="gpu")&&Owned(202,"PassOwned")== (mode=="native"),"mixed order baseline uses requested NEW owner: "+mode);
            int center=(size/2*size+size/2)*4;
            check(expected[0][center]==0&&expected[0][center+1]==0&&expected[0][center+2]==255,"mixed order baseline is independently blue: "+mode);
            Create(201,redCutout,-4.98,true);
            foreach(bool newFirst in new[]{true,false}){
                if(!newFirst){scene.Process(new MyRenderMessageRemoveRenderObject{ID=202});Create(202,blueSolid,-4.99,false);}
                foreach(bool indirect in new[]{false,true}){
                    backend.SetWorldIndirect(indirect);var actual=Capture(mode+"-old-cutout-new-solid-"+newFirst+"-"+indirect);
                    check(Owned(201,"GpuOwned")== (mode=="gpu")&&!Owned(201,"PassOwned"),"mixed order cutout uses requested OLD owner: "+mode);
                    Console.WriteLine($"MIXED ORDER {mode} newFirst={newFirst} indirect={indirect} center={actual[0][center]},{actual[0][center+1]},{actual[0][center+2]} depthEqual={actual[3].SequenceEqual(expected[3])}");
                    // Stock submits all OLD work (including read-only cutouts),
                    // then NEW. The solid between OLD base/cutout depths
                    // passes against the base and overwrites cutout material.
                    for(int i=0;i<4;i++)check(actual[i].SequenceEqual(expected[i]),"OLD read-only cutout precedes NEW solid in GBuffer/depth channel "+i+": "+mode);
                }
            }
            backend.SetWorldIndirect(false);
            foreach(uint id in new uint[]{200,201,202})scene.Process(new MyRenderMessageRemoveRenderObject{ID=id});render();
            Create(200,background,-5,true);Create(202,blueCutout,-4.99,false);var cutoutExpected=Capture(mode+"-new-cutout-baseline");
            check(cutoutExpected[0][center]==0&&cutoutExpected[0][center+1]==0&&cutoutExpected[0][center+2]==255,"NEW cutout baseline is independently blue: "+mode);
            Create(201,redBlend,-4.98,true);
            foreach(bool indirect in new[]{false,true}){
                backend.SetWorldIndirect(indirect);var actual=Capture(mode+"-old-blend-new-cutout-"+indirect);
                check(!Owned(201,"GpuOwned")&&!Owned(201,"PassOwned"),"OLD blend retains actual managed fallback ownership: "+mode);
                check(Owned(202,"GpuOwned")== (mode=="gpu")&&Owned(202,"PassOwned")== (mode=="native"),"NEW cutout retains requested owner: "+mode);
                for(int i=0;i<4;i++)check(actual[i].SequenceEqual(cutoutExpected[i]),"managed OLD blend precedes NEW cutout in GBuffer/depth channel "+i+": "+mode);
            }
            backend.SetWorldIndirect(false);foreach(uint id in new uint[]{200,201,202})scene.Process(new MyRenderMessageRemoveRenderObject{ID=id});render();
        }
        check(backend.GetStats().ValidationErrors==0,"mixed ordering has zero core/synchronization validation errors");
    }
}
