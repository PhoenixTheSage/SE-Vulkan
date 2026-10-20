using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using FixtureAssets;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;
using VRage.Utils;

partial class Program
{
    static void MixedNewOwnerChecks(NativeBackend backend,VulkanScene scene,Func<byte[]> render,Action<bool,string> check)
    {
        const int size=64;string output=Path.GetFullPath("TestResults/MixedNewOwners");Directory.CreateDirectory(output);
        var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        bool Owned(uint id,string field){var data=actors[id]!;return (bool)data.GetType().GetField(field)!.GetValue(data)!;}
        byte[][] Capture(string label){render();var buffers=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,size,size)).ToArray();for(int i=0;i<4;i++)File.WriteAllBytes(Path.Combine(output,label+"-"+i+".bin"),buffers[i]);return buffers;}
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=200});backend.SetGeometryArena(true);
        string first=MeshDecalFixtureAssets.Model(Path.Combine(output,"First"),MyMeshDrawTechnique.DECAL_CUTOUT,1,255);
        string last=MeshDecalFixtureAssets.Model(Path.Combine(output,"Last"),MyMeshDrawTechnique.DECAL_NOPREMULT,1,128,blue:true);
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=300,Model=first,WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible});
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=301,Model=last,WorldMatrix=MatrixD.CreateTranslation(0,0,-4.9921875),Flags=RenderFlags.Visible});
        foreach(bool gpu in new[]{true,false}){
            scene.GpuOpaqueOwnershipEnabled=gpu;scene.NativePassSceneEnabled=true;
            scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=300,Visible=false});var expected=Capture(gpu+"-baseline");int center=(size/2*size+size/2)*4;
            check(expected[0][center+2]>expected[0][center]&&BitConverter.ToSingle(expected[3],center)>0,"mixed NEW reference contains the authored blue blend");
            var samples=Enumerable.Range(0,size*size).Where(i=>BitConverter.ToSingle(expected[3],i*4)>0).ToArray();check(samples.Length>40,"mixed NEW reference has interior coverage");
            scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=300,Visible=true});
            foreach(bool indirect in new[]{false,true}){
                backend.SetWorldIndirect(indirect);var actual=Capture(gpu+"-overlap-"+indirect);
                check(Owned(300,"GpuOwned")==gpu&&Owned(300,"PassOwned")==!gpu,"first NEW group retains requested accelerated ownership");
                check(!Owned(301,"GpuOwned")&&!Owned(301,"PassOwned"),"later blended NEW group uses actual managed fallback");
                var packets=(List<WorldPacket>)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;
                check((!gpu||!Owned(300,"GpuOld"))&&packets.Count>0&&packets.All(p=>(p.Flags&NativeBackend.OldGeometryOrder)==0),"both mixed groups retain actual NEW renderer provenance");
                Console.WriteLine($"MIXED NEW OWNER gpu={gpu} indirect={indirect} center={actual[0][center]},{actual[0][center+1]},{actual[0][center+2]} reference={expected[0][center]},{expected[0][center+1]},{expected[0][center+2]}");
                for(int channel=0;channel<4;channel++){int stride=expected[channel].Length/(size*size);check(samples.All(i=>actual[channel].AsSpan(i*stride,stride).SequenceEqual(expected[channel].AsSpan(i*stride,stride))),"mixed NEW owners preserve first-seen groups in channel "+channel);}
            }
        }
        scene.GpuOpaqueOwnershipEnabled=true;scene.GpuSparseAppearanceEnabled=false;
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=302,Model=first,WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible});
        for(int i=0;i<32;i++)scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=302,MaterialName=MyStringId.GetOrCompute("mixed-owner-inert-"+i),DiffuseColor=Color.White,Emissivity=1});
        foreach(uint id in new uint[]{300,302})scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=id,Visible=false});
        var tripleExpected=Capture("three-owner-baseline");var tripleSamples=Enumerable.Range(0,size*size).Where(i=>BitConverter.ToSingle(tripleExpected[3],i*4)>0).ToArray();check(tripleSamples.Length>40,"three-owner reference has interior coverage");
        foreach(uint id in new uint[]{300,302})scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=id,Visible=true});
        foreach(bool indirect in new[]{false,true}){
            backend.SetWorldIndirect(indirect);var actual=Capture("three-owner-"+indirect);
            check(Owned(300,"GpuOwned")&&!Owned(300,"PassOwned")&&!Owned(302,"GpuOwned")&&Owned(302,"PassOwned")&&!Owned(301,"GpuOwned")&&!Owned(301,"PassOwned"),"GPU/native/managed owners participate in one real main sequence");
            for(int channel=0;channel<4;channel++){int stride=tripleExpected[channel].Length/(size*size);check(tripleSamples.All(i=>actual[channel].AsSpan(i*stride,stride).SequenceEqual(tripleExpected[channel].AsSpan(i*stride,stride))),"GPU and native members share group/part order before managed B: channel "+channel);}
        }
        // Owner labels are uints; ordering metadata must not reserve zero.
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=0,Model=last,WorldMatrix=MatrixD.CreateTranslation(100,0,-5),Flags=RenderFlags.Visible});
        var zeroOwner=Capture("owner-zero");check(!Owned(0,"GpuOwned")&&!Owned(0,"PassOwned"),"zero owner retains managed fallback with mixed scheduling");
        for(int channel=0;channel<4;channel++)check(zeroOwner[channel].SequenceEqual(tripleExpected[channel]),"zero owner ordering metadata preserves visible channel "+channel);
        backend.SetWorldIndirect(false);check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"mixed NEW owners have zero core/synchronization errors");
    }
}
