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
    static void RasterSceneChecks(NativeBackend backend,VulkanScene scene,string output,Func<byte[]> direct,Func<byte[]> retained,Action<bool,string> check)
    {
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=10});
        var failures=new List<string>();void Failed(string path,Exception error)=>failures.Add(path+": "+error);scene.ContentFailed+=Failed;
        try{
            foreach(var technique in new[]{MyMeshDrawTechnique.MESH,MyMeshDrawTechnique.ALPHA_MASKED,MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED,MyMeshDrawTechnique.DECAL_CUTOUT,MyMeshDrawTechnique.DECAL,MyMeshDrawTechnique.DECAL_NOPREMULT})
            foreach(bool old in new[]{true,false})foreach(bool noCull in new[]{false,true})foreach(bool back in new[]{false,true}){
                string path=MeshDecalFixtureAssets.Model(Path.Combine(output,"RasterAssets"),technique,7,255,reverseWinding:back);
                scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=path,DebugName="material raster contract",WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|(old?RenderFlags.ForceOldPipeline:0)|(noCull?RenderFlags.NoBackFaceCulling:0)});
                direct();var buffers=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();int center=(32*64+32)*4;
                bool visible=buffers[0][center+3]!=255;
                // OLD combines technique flags with actor flags. NEW's part
                // rasterizer is fixed by technique; creation flags are kept
                // only for compatibility if it later converts to OLD.
                bool expected=!back||technique==MyMeshDrawTechnique.ALPHA_MASKED||(old&&noCull);
                string name=technique+" old="+old+" noCull="+noCull+" back="+back;
                File.WriteAllBytes(Path.Combine(output,"raster-last.rgba8"),buffers[0]);
                check(visible==expected,"actual material raster visibility matches stock "+name);
                retained();check(buffers.Select((a,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(a)).All(equal=>equal),"prepared material raster state preserves every channel "+name);
                backend.SetWorldIndirect(true);direct();check(buffers.Select((a,i)=>backend.ReadbackWorldGbuffer((uint)i,64,64).SequenceEqual(a)).All(equal=>equal),"indirect material raster state preserves every channel "+name);backend.SetWorldIndirect(false);
                scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
            }
            foreach(var technique in new[]{MyMeshDrawTechnique.MESH,MyMeshDrawTechnique.ALPHA_MASKED})foreach(bool old in new[]{true,false}){
                string path=MeshDecalFixtureAssets.Model(Path.Combine(output,"RasterAssets"),technique,7,255,reverseWinding:true);
                scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=path,WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|(old?RenderFlags.ForceOldPipeline:0)});
                direct();bool masked=technique==MyMeshDrawTechnique.ALPHA_MASKED;
                var importer=new MyModelImporter();importer.ImportData(path,new[]{"MeshParts"});
                var parts=(List<MyMeshPartInfo>)importer.GetTagData()["MeshParts"];
                string material=parts.Single(p=>p.m_MaterialDesc.TechniqueEnum==technique && p.m_MaterialDesc.MaterialName.StartsWith("FixtureDecal-",StringComparison.Ordinal)).m_MaterialDesc.MaterialName;
                scene.Process(new MyRenderMessageUpdateModelProperties{ID=120,MaterialName=MyStringId.GetOrCompute(material),FlagsChange=masked?new RenderFlagsChange{Remove=RenderFlags.NoBackFaceCulling}:new RenderFlagsChange{Add=RenderFlags.NoBackFaceCulling}});
                direct();var changed=backend.ReadbackWorldGbuffer(0,64,64);
                check((changed[(32*64+32)*4+3]!=255)==!masked,"material add/remove culling overrides technique flags and converts NEW to OLD: "+technique+" old="+old);
                retained();check(backend.ReadbackWorldGbuffer(0,64,64).SequenceEqual(changed),"prepared material culling override invalidates retained state: "+technique+" old="+old);
                scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
            }
            var front=MatrixD.CreateLookAt(new Vector3D(0,0,10),new Vector3D(0,0,-3),Vector3D.Up)*MatrixD.CreateOrthographic(4,4,1,100);
            var opposite=MatrixD.CreateLookAt(new Vector3D(0,0,-20),new Vector3D(0,0,-3),Vector3D.Up)*MatrixD.CreateOrthographic(4,4,1,100);
            backend.SetShadowQuality(256,0);backend.SetShadowCascades(NativeBackend.MatrixValues(front).Concat(NativeBackend.MatrixValues(opposite)).ToArray(),new[]{100f,200f});
            var shadowConstants=new byte[1200];BitConverter.GetBytes(2u).CopyTo(shadowConstants,1192);backend.SetShadowPassConstants(shadowConstants);
            var defaultDepth=new Dictionary<(MyMeshDrawTechnique,bool),float[][]>();
            foreach(var technique in new[]{MyMeshDrawTechnique.MESH,MyMeshDrawTechnique.ALPHA_MASKED,MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED})foreach(bool old in new[]{true,false})foreach(bool noCull in new[]{false,true}){
                string path=MeshDecalFixtureAssets.Model(Path.Combine(output,"RasterAssets"),technique,7,255);
                scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=path,WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=RenderFlags.Visible|RenderFlags.CastShadows|(old?RenderFlags.ForceOldPipeline:0)|(noCull?RenderFlags.NoBackFaceCulling:0)});
                direct();var depth=new[]{backend.ReadbackShadow(0,256),backend.ReadbackShadow(1,256)};
                int views=depth.Count(d=>d[128*256+128]<1);bool both=old&&(noCull||technique==MyMeshDrawTechnique.ALPHA_MASKED);
                check(views==(both?2:1),"stock opposing cascade rasterizers preserve OLD override / NEW view defaults: "+technique+" old="+old+" noCull="+noCull);
                if(!noCull)defaultDepth[(technique,old)]=depth;
                else if(old&&technique!=MyMeshDrawTechnique.ALPHA_MASKED){
                    var baseline=defaultDepth[(technique,old)];int view=Array.FindIndex(baseline,d=>d[128*256+128]<1);
                    check(baseline[view][128*256+128]>depth[view][128*256+128],"OLD no-cull override drops stock cascade depth bias: "+technique);
                }else if(old){
                    // This fixture also has an opaque backing part. The actor
                    // override exposes that backing from its reverse side;
                    // the primary-facing alpha-masked surface stays exact.
                    check(depth.Where((d,i)=>d.SequenceEqual(defaultDepth[(technique,old)][i])).Any(),"intrinsic OLD alpha no-cull retains primary-facing depth while the backing gains its actor override");
                }else check(depth[0].SequenceEqual(defaultDepth[(technique,old)][0])&&depth[1].SequenceEqual(defaultDepth[(technique,old)][1]),"NEW actor no-cull leaves both stock depth views unchanged: "+technique);
                retained();check(depth[0].SequenceEqual(backend.ReadbackShadow(0,256))&&depth[1].SequenceEqual(backend.ReadbackShadow(1,256)),"prepared stock cascade raster state preserves exact depth: "+technique+" old="+old+" noCull="+noCull);
                scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});direct();
            }
            backend.SetShadowPassConstants(null);backend.SetShadowCascades(Array.Empty<float>(),Array.Empty<float>());
            check(failures.Count==0,"raster fixtures import without content errors: "+string.Join("; ",failures));
            check(backend.GetStats().ValidationErrors==0,"raster scene has zero validation errors");
        }finally{scene.ContentFailed-=Failed;scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});}
    }
}
