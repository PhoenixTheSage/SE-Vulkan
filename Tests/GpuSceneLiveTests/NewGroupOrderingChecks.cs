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
    static void NewGroupOrderingChecks(NativeBackend backend,VulkanScene scene,Func<byte[]> render,Action<bool,string> check)
    {
        const int size=64;string output=Path.GetFullPath("TestResults/NewGroupOrdering");Directory.CreateDirectory(output);
        var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        bool Owned(uint id,string field){var data=actors[id]!;return (bool)data.GetType().GetField(field)!.GetValue(data)!;}
        byte[][] Capture(string label){render();var buffers=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,size,size)).ToArray();for(int i=0;i<4;i++)File.WriteAllBytes(Path.Combine(output,label+"-"+i+".bin"),buffers[i]);return buffers;}
        void Visible(uint id,bool value)=>scene.Process(new MyRenderMessageUpdateRenderObjectVisibility{ID=id,Visible=value});
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=200});scene.GpuOpaqueOwnershipEnabled=false;scene.NativePassSceneEnabled=false;backend.SetGeometryArena(true);
        string first=MeshDecalFixtureAssets.Model(Path.Combine(output,"First"),MyMeshDrawTechnique.DECAL_CUTOUT,1,255);
        string last=MeshDecalFixtureAssets.Model(Path.Combine(output,"Last"),MyMeshDrawTechnique.MESH,1,255,blue:true,singleLayer:true);
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=300,Model=first,WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible});
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=301,Model=last,WorldMatrix=MatrixD.CreateTranslation(0,0,-5.0078125),Flags=RenderFlags.Visible});
        foreach(string mode in new[]{"managed","native","gpu"}){
            scene.GpuOpaqueOwnershipEnabled=mode=="gpu";scene.NativePassSceneEnabled=mode!="managed";
            // Resolve B while A is hidden: resource creation order is not the
            // first-seen order of selected groups on the later overlap frame.
            Visible(300,false);var expected=Capture(mode+"-baseline");
            var arena=backend.GetGeometryArenaStats();check(arena.Enabled!=0&&arena.VertexPages==1&&arena.IndexPages==1&&arena.VertexLive>0&&arena.IndexLive>0,"NEW group fixture actually shares geometry pages: "+mode);
            int center=(size/2*size+size/2)*4;check(expected[0][center]==0&&expected[0][center+1]==0&&expected[0][center+2]==255,"NEW group baseline is independently blue: "+mode);
            var samples=Enumerable.Range(0,size*size).Where(i=>BitConverter.ToSingle(expected[3],i*4)>0).ToArray();check(samples.Length>40,"NEW group oracle has interior coverage: "+mode);
            Visible(300,true);
            foreach(bool indirect in new[]{false,true}){
                backend.SetWorldIndirect(indirect);var actual=Capture(mode+"-overlap-"+indirect);
                check(new uint[]{300,301}.All(id=>Owned(id,"GpuOwned")== (mode=="gpu")&&Owned(id,"PassOwned")== (mode=="native")),"both NEW groups retain requested ownership: "+mode);
                if(mode=="managed"){
                    var packets=(List<WorldPacket>)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;
                    check(Math.Abs(packets.First(p=>(p.Flags&(4096|8192))==0).Origin.Z-(-4.9921875))<1e-5,"managed producer actually encounters group A first");
                }
                if(mode=="native"){
                    var groups=backend.GetPassGroupAudit(out var owners);var main=groups.Where(g=>g.View==0&&g.Kind==0).ToArray();
                    Console.WriteLine("NEW NATIVE GROUP OWNERS "+string.Join(",",main.Select(g=>owners[g.OwnerOffset])));
                    check(main.Length==3&&owners[main[0].OwnerOffset]==300&&owners[main[1].OwnerOffset]==300&&owners[main[2].OwnerOffset]==301,"native first-seen groups survive earlier B resource registration");
                }
                Console.WriteLine($"NEW GROUP ORDER {mode} indirect={indirect} center={actual[0][center]},{actual[0][center+1]},{actual[0][center+2]}");
                // A's base and read-only cutout are contiguous, then B's
                // closer-than-base solid overwrites the cutout material.
                for(int channel=0;channel<4;channel++){int stride=expected[channel].Length/(size*size);check(samples.All(i=>actual[channel].AsSpan(i*stride,stride).SequenceEqual(expected[channel].AsSpan(i*stride,stride))),"NEW group parts remain contiguous in channel "+channel+": "+mode);}
            }
            backend.SetWorldIndirect(false);
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=302,Model=first,WorldMatrix=MatrixD.CreateTranslation(0,0,-5),Flags=RenderFlags.Visible});
            foreach(string submission in mode=="managed"?new[]{"blob","pointer","retained","bulk"}:new[]{"blob"})foreach(bool indirect in new[]{false,true}){
                backend.RetainedSubmissionEnabled=submission!="pointer";backend.ObjectRetentionEnabled=submission=="retained";backend.ModelBatchingEnabled=backend.BulkModelsEnabled=submission=="bulk";
                backend.SetWorldIndirect(indirect);var repeated=Capture(mode+"-repeated-group-"+submission+"-"+indirect);
                check(Owned(302,"GpuOwned")== (mode=="gpu")&&Owned(302,"PassOwned")== (mode=="native"),"repeated NEW actor retains requested ownership: "+mode);
                if(submission=="bulk")check(backend.GetModelGroupStats().Instances>0,"ordering test actually emits a native bulk model");
                Console.WriteLine($"NEW REPEATED GROUP {mode} submission={submission} indirect={indirect} center={repeated[0][center]},{repeated[0][center+1]},{repeated[0][center+2]}");
                for(int channel=0;channel<4;channel++){int stride=expected[channel].Length/(size*size);check(samples.All(i=>repeated[channel].AsSpan(i*stride,stride).SequenceEqual(expected[channel].AsSpan(i*stride,stride))),"A/B/A actors preserve first-seen NEW groups in channel "+channel+": "+mode);}
            }
            backend.RetainedSubmissionEnabled=true;backend.ObjectRetentionEnabled=false;backend.ModelBatchingEnabled=backend.BulkModelsEnabled=false;
            if(mode=="managed"){
                var name=MyStringId.GetOrCompute("FixtureDecal-"+Path.GetFileNameWithoutExtension(first));
                scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=302,MaterialName=name,DiffuseColor=Color.Green,Emissivity=.75f});var coloured=Capture("managed-instance-appearance");
                for(int channel=0;channel<4;channel++){int stride=expected[channel].Length/(size*size);check(samples.All(i=>coloured[channel].AsSpan(i*stride,stride).SequenceEqual(expected[channel].AsSpan(i*stride,stride))),"per-instance appearance does not split NEW group channel "+channel);}
                scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=302,MaterialName=name,DiffuseColor=Color.White,Emissivity=1});
                string green=Path.Combine(output,"green-variant.dds");MeshDecalFixtureAssets.Texture(green,0,255,0,255);
                void Texture(string file)=>scene.Process(new MyRenderMessageChangeMaterialTexture{RenderObjectID=302,Changes=new Dictionary<MyStringId,MyTextureChange>{{name,new MyTextureChange{ColorMetalFileName=file}}}});
                Texture(green);var variant=Capture("managed-binding-variant");check(variant[0][center]==0&&variant[0][center+1]==255&&variant[0][center+2]==0,"texture variant creates a later NEW group with independent green output");check(variant[3].SequenceEqual(expected[3]),"texture variant cutout preserves underlying depth");
                var packets=(List<WorldPacket>)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;
                check(packets.Where(p=>p.Pipeline<20&&(p.Flags&(4096|8192))==0).All(p=>p.Count==1),"rigid batching preserves distinct binding-group boundaries");
                Texture(null!);var restored=Capture("managed-binding-restored");check(restored[0][center]==0&&restored[0][center+1]==0&&restored[0][center+2]==255,"binding reset rejoins the first-seen NEW group");
                check(packets.Any(p=>p.Pipeline<20&&p.Count==2),"binding reset restores shared rigid instancing");
            }
            backend.SetWorldIndirect(false);scene.Process(new MyRenderMessageRemoveRenderObject{ID=302});render();
        }
        check(backend.GetStats().ValidationErrors==0,"NEW group ordering has zero core/synchronization validation errors");
    }
}
