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
    static void OldResourceOrderChecks(NativeBackend backend,VulkanScene scene,Func<byte[]> render,Action<bool,string> check)
    {
        const int size=64;string output=Path.GetFullPath("TestResults/OldResourceOrder");Directory.CreateDirectory(output);
        scene.InitializeStockOldIdentities(false);check(System.Runtime.InteropServices.Marshal.SizeOf<NativeOldPartOrder>()==32,"OLD ordering metadata has the native ABI stride");string currentOwner="";
        var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        bool Owned(uint id,string field)=>(bool)actors[id]!.GetType().GetField(field)!.GetValue(actors[id])!;
        void Create(uint id,string model,double z){scene.Process(new MyRenderMessageCreateRenderEntity{ID=id,Model=model,WorldMatrix=MatrixD.CreateTranslation(0,0,z),Flags=RenderFlags.Visible|RenderFlags.ForceOldPipeline});
            if(id==402&&currentOwner=="mixed")for(int i=0;i<33;i++)scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=id,MaterialName=VRage.Utils.MyStringId.GetOrCompute("unused-order-fallback-"+i),DiffuseColor=Color.White,Emissivity=.5f});}
        byte[][] Capture(string label){render();var data=Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,size,size)).ToArray();for(int i=0;i<4;i++)File.WriteAllBytes(Path.Combine(output,label+"-"+i+".bin"),data[i]);return data;}
        string root=Path.Combine(output,"Models");
        string background=MeshDecalFixtureAssets.Model(Path.Combine(root,"base"),MyMeshDrawTechnique.MESH,1,255,singleLayer:true,neutral:true);
        string red=MeshDecalFixtureAssets.Model(Path.Combine(root,"A"),MyMeshDrawTechnique.DECAL_CUTOUT,1,255,singleLayer:true);
        string blue=MeshDecalFixtureAssets.Model(Path.Combine(root,"B"),MyMeshDrawTechnique.DECAL_CUTOUT,1,255,singleLayer:true,blue:true);
        // Stock live oracle: MeshOldOrder fixture64f1d73 preloads A then B.
        // Actual keys1653665488374690/1653665488374819 survive reversed actors
        // and A recreation. Both read-only cutouts pass against the base;
        // the higher B resource key wins in every authored panel.
        // The earlier unannotated same-owner control passed a renderer that
        // failed the spatial fixture. This now also requires actual metadata
        // and mixed ownership; the four-panel live gate remains separate.
        foreach(string model in new[]{background,red,blue})scene.Process(new MyRenderMessagePreloadModel{Name=model,Rescale=1,ForceOldPipeline=true});
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=200});
        foreach(string owner in new[]{"managed","gpu","mixed"}){
            currentOwner=owner;scene.GpuOpaqueOwnershipEnabled=owner!="managed";scene.NativePassSceneEnabled=false;
            Create(400,background,-5);Create(402,blue,-4.99);var expected=Capture(owner+"-blue-reference");int center=(size/2*size+size/2)*4;
            check(expected[0][center]==0&&expected[0][center+1]==0&&expected[0][center+2]==255,"OLD resource reference is independently blue: "+owner);
            scene.Process(new MyRenderMessageRemoveRenderObject{ID=402});Create(401,red,-4.98);Create(402,blue,-4.99);
            foreach(string sequence in new[]{"A-B","B-A-recreated"}){
            if(sequence=="B-A-recreated"){scene.Process(new MyRenderMessageRemoveRenderObject{ID=401});Create(401,red,-4.98);}
            foreach(string path in owner=="gpu"?new[]{"prepared"}:new[]{"legacy","prepared","retained-objects","bulk"}){
            backend.RetainedSubmissionEnabled=path!="legacy";backend.ObjectRetentionEnabled=path=="retained-objects";backend.ModelBatchingEnabled=path=="bulk";backend.BulkModelsEnabled=path=="bulk";
            foreach(bool indirect in new[]{false,true}){
                backend.SetWorldIndirect(indirect);var actual=Capture(owner+"-"+sequence+"-"+path+"-"+indirect);
                check(Owned(401,"GpuOwned")== (owner!="managed")&&Owned(402,"GpuOwned")== (owner=="gpu")&&!Owned(401,"PassOwned")&&!Owned(402,"PassOwned"),"OLD resource case uses actual requested owners: "+owner);
                if(owner!="gpu"){
                    var packets=(List<WorldPacket>)typeof(NativeBackend).GetField("worldPackets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!;
                    var main=packets.Where(p=>(p.Flags&(4096u|8192u))==0).ToArray();
                    check(main.Length>0&&main.All(p=>(p.Flags&NativeBackend.OldGeometryOrder)!=0),"resource-order main packets carry actual OLD provenance");
                    var orderField=typeof(WorldPacket).GetField("OldOrder",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    check(main.All(p=>((NativeOldPartOrder)orderField.GetValue(p)!).Valid==1),"managed OLD packets carry retained source identity metadata");
                }
                if(owner!="managed")check(typeof(NativeBackend).GetField("gpuModelOrdered",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!=null,"GPU models use the ordered immutable registration API");
                if(owner=="managed"&&path=="bulk")check(typeof(NativeBackend).GetField("registerModelOrdered",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!=null,"bulk models use the ordered immutable registration API");
                Console.WriteLine($"OLD RESOURCE {owner} sequence={sequence} indirect={indirect} center={actual[0][center]},{actual[0][center+1]},{actual[0][center+2]} depthEqual={actual[3].SequenceEqual(expected[3])}");
                for(int i=0;i<4;i++)check(actual[i].SequenceEqual(expected[i]),"OLD resource keys survive actor submission in GBuffer/depth channel "+i+": "+owner+" "+sequence);
            }}}
            backend.SetWorldIndirect(false);foreach(uint id in new uint[]{400,401,402})scene.Process(new MyRenderMessageRemoveRenderObject{ID=id});render();
        }
        check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"OLD resource-order case has zero core/synchronization errors");
    }
}
