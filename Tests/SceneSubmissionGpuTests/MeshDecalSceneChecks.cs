using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using FixtureAssets;
using VRage.Import;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

partial class Program
{
    static void MeshDecalSceneChecks(NativeBackend backend,VulkanScene scene,VulkanShaderLibrary library,string output,Func<byte[]> direct,Func<byte[]> retained,Action<bool,string> check)
    {
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=10});backend.ConfigureNativePasses(library);
        var errors=new List<string>();void Failed(string path,Exception error)=>errors.Add(path+": "+error);scene.ContentFailed+=Failed;
        var queued=typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic);
        bool headersChecked=false;
        try{
            // The installed OLD cache registers invisible proxies too, and
            // survives removal. Seed its first bundle outside the camera.
            string first=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),MyMeshDrawTechnique.DECAL,1);
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=109,Model=first,DebugName="hidden first OLD decal",WorldMatrix=MatrixD.CreateTranslation(1000,0,-3),Flags=RenderFlags.ForceOldPipeline});
            direct();check(!((List<WorldPacket>)queued.GetValue(backend)).Any(p=>WorldShaderVariants.IsBlendedDecal(p.Pipeline)),"invisible first OLD decal registers without drawing");
            scene.Process(new MyRenderMessageRemoveRenderObject{ID=109});
            foreach(var technique in new[]{MyMeshDrawTechnique.DECAL,MyMeshDrawTechnique.DECAL_NOPREMULT})foreach(uint role in new uint[]{1,2,3,7})foreach(bool old in new[]{false,true}){
                string path=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),technique,role);
                if(!headersChecked){
                    var header=Assembly.Load("VRage.Render11").GetType("SharpDX.Toolkit.Graphics.DDSHelper",true).GetMethod("ReadDDSHeader",BindingFlags.Static|BindingFlags.Public);
                    foreach(string texture in Directory.GetFiles(Path.GetDirectoryName(path),"*.dds"))using(var input=File.OpenRead(texture)){
                        var description=header.Invoke(null,new object[]{input,texture});check(description!=null,"installed DX11 DDS header loader accepts "+Path.GetFileName(texture));
                    }
                    headersChecked=true;
                }
                var importer=new MyModelImporter();importer.ImportData(path,new[]{"MeshParts","Vertices"});var tags=importer.GetTagData();
                var parts=(List<MyMeshPartInfo>)tags["MeshParts"];check(parts.Count==2&&parts[1].Technique==technique,"installed MWM round trip retains authored mesh-decal technique "+technique+"/"+role);
                scene.Process(new MyRenderMessageCreateRenderEntity{ID=110,Model=path,DebugName="authored mesh decal",WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|(old?RenderFlags.ForceOldPipeline:0)});
                direct();var referenceBuffers=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();var reference=referenceBuffers[0];var packets=((List<WorldPacket>)queued.GetValue(backend)).ToArray();
                uint expected=WorldShaderVariants.Pipeline(technique,0,false,false,role,old?1u:role);
                check(packets.Any(p=>p.Pipeline==expected&&(p.Flags&(512u|16384u))==(512u|16384u)),"actual scene fallback preserves authored blend bundle, OLD macro cache and view exclusions "+technique+"/"+role+" old="+old);
                int center=(32*64+32)*4;check(reference[center]>100&&reference[center+3]!=255,"authored fixture renders actual material pixels "+technique+"/"+role+" old="+old);
                if(technique==MyMeshDrawTechnique.DECAL_NOPREMULT){
                    var material=backend.ReadbackWorldGbuffer(2,64,64);
                    check(Math.Abs(material[center]-((role&1)!=0?55:0))<=1&&Math.Abs(material[center+1]-((role&2)!=0?126:100))<=1&&Math.Abs(material[center+2]-((role&4)!=0?12:0))<=1,"actual scene mask/color/gloss/emission agree with captured stock sRGB-alpha and first-use OLD material channels "+role+" old="+old);
                }
                if(old && (role&2)!=0){
                    var packed=backend.ReadbackWorldGbuffer(1,64,64);uint n=BitConverter.ToUInt32(packed,center);
                    check((n&0x3fffffffu)==(512u|(512u<<10)|(1023u<<20)),"OLD first color-only macro bundle preserves flat normal after hidden first actor removal "+technique+"/"+role);
                }
                retained();check(referenceBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"actual prepared scene agrees with all direct mesh-decal GBuffer/depth channels "+technique+"/"+role+" old="+old);
                scene.Process(new MyRenderMessageRemoveRenderObject{ID=110});direct();
                check(!((List<WorldPacket>)queued.GetValue(backend)).Any(p=>WorldShaderVariants.IsBlendedDecal(p.Pipeline)),"scene removal retires mesh-decal fallback packets");
            }
            foreach(var extra in new[]{RenderFlags.MetalnessColorable,RenderFlags.DistanceFade}){
                string seed=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),MyMeshDrawTechnique.DECAL,7);
                scene.Process(new MyRenderMessageCreateRenderEntity{ID=111,Model=seed,DebugName="independent OLD shader key",WorldMatrix=MatrixD.CreateTranslation(1000,0,-3),Flags=RenderFlags.ForceOldPipeline|extra});direct();
                scene.Process(new MyRenderMessageRemoveRenderObject{ID=111});
                string normal=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),MyMeshDrawTechnique.DECAL_NOPREMULT,2);
                scene.Process(new MyRenderMessageCreateRenderEntity{ID=111,Model=normal,DebugName="OLD shader key consumer",WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline|extra});direct();
                uint expected=WorldShaderVariants.Pipeline(MyMeshDrawTechnique.DECAL_NOPREMULT,0,false,extra==RenderFlags.MetalnessColorable,2,7);
                check(((List<WorldPacket>)queued.GetValue(backend)).Any(p=>p.Pipeline==expected),"OLD first-use bundles remain independent for "+extra);
                uint packed=BitConverter.ToUInt32(backend.ReadbackWorldGbuffer(1,64,64),(32*64+32)*4);
                check((packed&0xfffffu)!=(512u|(512u<<10)),"independent OLD shader key visibly retains normal map after first actor removal "+extra);
                scene.Process(new MyRenderMessageRemoveRenderObject{ID=111});direct();
            }
            string overlap=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),MyMeshDrawTechnique.DECAL_NOPREMULT,1,overlap:true);
            var overlapImporter=new MyModelImporter();overlapImporter.ImportData(overlap,new[]{"MeshParts"});
            var authored=(List<MyMeshPartInfo>)overlapImporter.GetTagData()["MeshParts"];
            check(authored.Count==3&&authored[1].Technique==MyMeshDrawTechnique.DECAL_NOPREMULT&&authored[2].Technique==MyMeshDrawTechnique.DECAL,"overlap fixture actually authors opposite decal technique order");
            var comparerType=Assembly.Load("VRage.Render11").GetType("VRage.Render11.GeometryStage2.Model.MyMwmData",true).GetNestedType("MyPartsComparer",BindingFlags.NonPublic);
            var stock=new List<MyMeshPartInfo>(authored);stock.Sort((IComparer<MyMeshPartInfo>)Activator.CreateInstance(comparerType,true));
            check(stock[1].Technique==MyMeshDrawTechnique.DECAL&&stock[2].Technique==MyMeshDrawTechnique.DECAL_NOPREMULT,"installed NEW importer comparer requires DECAL before DECAL_NOPREMULT");
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=112,Model=overlap,DebugName="opposite authored NEW decal order",WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible});direct();
            var overlapBuffers=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();
            var overlapPackets=((List<WorldPacket>)queued.GetValue(backend)).Where(p=>WorldShaderVariants.IsBlendedDecal(p.Pipeline)).ToArray();
            check(overlapPackets.Select(p=>p.Pipeline).SequenceEqual(new[]{WorldShaderVariants.Pipeline(MyMeshDrawTechnique.DECAL,0,false,false,1),WorldShaderVariants.Pipeline(MyMeshDrawTechnique.DECAL_NOPREMULT,0,false,false,1)}),"actual NEW scene publishes the installed importer order rather than authored overlap order");
            int overlapCenter=(32*64+32)*4;check(overlapBuffers[0][overlapCenter+2]>overlapBuffers[0][overlapCenter]&&overlapBuffers[0][overlapCenter]>100,"opposite authored NEW overlap visibly blends red straight alpha after blue premultiplied alpha");
            retained();check(overlapBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"all overlapping NEW material/depth channels match direct and prepared submission");
            var liveActors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scene);
            var overlapActor=liveActors[112u];var overlapModel=overlapActor.GetType().GetField("Model").GetValue(overlapActor);var newPartsField=overlapModel.GetType().GetField("NewParts");var sortedParts=newPartsField.GetValue(overlapModel);
            try{
                newPartsField.SetValue(overlapModel,null);direct();var sourceOrder=backend.ReadbackWorldGbuffer(0,64,64);
                File.WriteAllBytes(Path.Combine(output,"new-overlap-original-order.rgba8"),sourceOrder);File.WriteAllBytes(Path.Combine(output,"new-overlap-stock-order.rgba8"),overlapBuffers[0]);
                check(!sourceOrder.SequenceEqual(overlapBuffers[0]),"original authored-order control reproduces a visible NEW overlap mismatch on the same GPU path");
            }finally{newPartsField.SetValue(overlapModel,sortedParts);}
            direct();check(backend.ReadbackWorldGbuffer(0,64,64).SequenceEqual(overlapBuffers[0]),"restoring stock NEW part order restores exact corrected pixels");
            scene.Process(new MyRenderMessageRemoveRenderObject{ID=112});direct();
            scene.Process(new MyRenderMessageCreateRenderEntity{ID=112,Model=overlap,DebugName="opposite authored OLD decal order",WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline});direct();
            check(((List<WorldPacket>)queued.GetValue(backend)).Where(p=>WorldShaderVariants.IsBlendedDecal(p.Pipeline)).All(p=>(p.Flags&NativeBackend.OldBlendedDecalOrder)!=0),"actual OLD scene publishes explicit stock technique-order provenance");
            check(overlapBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"OLD global technique priority corrects reversed authored overlap in every GBuffer/depth channel");
            retained();check(overlapBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"prepared OLD overlap retains native technique priority");
            backend.SetWorldIndirect(true);direct();check(overlapBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"indirect OLD overlap retains native technique priority");backend.SetWorldIndirect(false);
            scene.Process(new MyRenderMessageRemoveRenderObject{ID=112});direct();
            foreach(bool reverseCreation in new[]{false,true}){
                string blue=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),MyMeshDrawTechnique.DECAL,1,blue:true);
                string red=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),MyMeshDrawTechnique.DECAL_NOPREMULT,1);
                var sources=new[]{(Id:113u,Model:blue),(Id:114u,Model:red)};
                foreach(var source in reverseCreation?sources.Reverse():sources)scene.Process(new MyRenderMessageCreateRenderEntity{ID=source.Id,Model=source.Model,DebugName="cross-actor OLD overlap",WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline});
                direct();check(overlapBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"global OLD technique order spans actors independent of creation order "+reverseCreation);
                retained();check(overlapBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"prepared global OLD technique order spans actors "+reverseCreation);
                backend.SetWorldIndirect(true);direct();check(overlapBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"indirect global OLD technique order spans actors "+reverseCreation);backend.SetWorldIndirect(false);
                foreach(var source in sources)scene.Process(new MyRenderMessageRemoveRenderObject{ID=source.Id});direct();
            }
            byte[][] coveredCutoutBuffers=null;
            foreach(bool old in new[]{false,true})foreach(byte alpha in new byte[]{0,255}){
                string cutout=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),MyMeshDrawTechnique.DECAL_NOPREMULT,1,overlap:true,cutoutAlpha:alpha);
                scene.Process(new MyRenderMessageCreateRenderEntity{ID=115,Model=cutout,DebugName="cutout versus blended ordering",WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|(old?RenderFlags.ForceOldPipeline:0)});direct();
                var material=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();int center=(32*64+32)*4;
                File.WriteAllBytes(Path.Combine(output,"cutout-order-"+old+"-"+alpha+".rgba8"),material[0]);
                if(old&&alpha==255)coveredCutoutBuffers=material;
                check(alpha==0?material[0].SequenceEqual(overlapBuffers[0]):material[0][center]==0&&material[0][center+1]>=254&&material[0][center+2]==0,"stock late cutout either discards or visibly covers both blended techniques old="+old+" alpha="+alpha);
                check(material[3].SequenceEqual(overlapBuffers[3]),"late cutout preserves actual opaque depth old="+old+" alpha="+alpha);
                retained();check(material.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"prepared cutout/blended overlap preserves every GBuffer/depth channel old="+old+" alpha="+alpha);
                backend.SetWorldIndirect(true);direct();check(material.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"indirect cutout/blended overlap preserves every GBuffer/depth channel old="+old+" alpha="+alpha);backend.SetWorldIndirect(false);
                scene.Process(new MyRenderMessageRemoveRenderObject{ID=115});direct();
            }
            foreach(bool reverseCreation in new[]{false,true}){
                string green=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),MyMeshDrawTechnique.DECAL_NOPREMULT,1,overlap:true,cutoutAlpha:255);
                string blue=MeshDecalFixtureAssets.Model(Path.Combine(output,"MeshDecalAssets"),MyMeshDrawTechnique.DECAL,1,blue:true);
                var sources=new[]{(Id:115u,Model:green),(Id:116u,Model:blue)};
                foreach(var source in reverseCreation?sources.Reverse():sources)scene.Process(new MyRenderMessageCreateRenderEntity{ID=source.Id,Model=source.Model,DebugName="cross-actor OLD late cutout",WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline});
                direct();check(coveredCutoutBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"global OLD cutout covers external blended actor independent of creation order "+reverseCreation);
                retained();check(coveredCutoutBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"prepared global OLD cutout preserves every channel "+reverseCreation);
                backend.SetWorldIndirect(true);direct();check(coveredCutoutBuffers.Select((buffer,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(buffer)).All(equal=>equal),"indirect global OLD cutout preserves every channel "+reverseCreation);backend.SetWorldIndirect(false);
                foreach(var source in sources)scene.Process(new MyRenderMessageRemoveRenderObject{ID=source.Id});direct();
            }
            check(errors.Count==0,"authored fixture imports without content errors: "+string.Join("; ",errors));
            check(backend.GetStats().ValidationErrors==0,"actual mesh-decal scene admission has zero Vulkan validation errors");
        }finally{scene.ContentFailed-=Failed;scene.Process(new MyRenderMessageRemoveRenderObject{ID=110});scene.Process(new MyRenderMessageRemoveRenderObject{ID=111});scene.Process(new MyRenderMessageRemoveRenderObject{ID=112});scene.Process(new MyRenderMessageRemoveRenderObject{ID=113});scene.Process(new MyRenderMessageRemoveRenderObject{ID=114});scene.Process(new MyRenderMessageRemoveRenderObject{ID=115});scene.Process(new MyRenderMessageRemoveRenderObject{ID=116});}
    }
}
