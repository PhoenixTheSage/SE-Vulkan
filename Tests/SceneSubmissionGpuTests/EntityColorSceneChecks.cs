using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using FixtureAssets;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using VRageRender.Import;
using VRage.Utils;

partial class Program
{
    static void EntityColorSceneChecks(NativeBackend backend,VulkanScene scene,string output,Func<byte[]> direct,Func<byte[]> retained,Action<bool,string> check)
    {
        scene.Process(new MyRenderMessageRemoveRenderObject{ID=10});
        string model=MeshDecalFixtureAssets.Model(Path.Combine(output,"EntityColorAssets"),MyMeshDrawTechnique.MESH,7,255,neutral:true);
        var settings=scene.RenderSettings;settings.User.DistanceFade=3000;scene.RenderSettings=settings;
        byte[][] Buffers()=>Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();
        bool Same(byte[][] expected)=>expected.Select((v,i)=>v.SequenceEqual(backend.ReadbackWorldGbuffer((uint)i,64,64))).All(v=>v);
        // CreateRenderEntity forwards diffuse color through UpdateRenderEntity.
        // MyRender11 ignores that field and the character creation value. Material color and
        // HSV updates are distinct contracts and must remain independent.
        foreach(string kind in new[]{"old","new","generic","generic-fade","character"}){
            void Create(Color color){
                var flags=RenderFlags.Visible|(kind=="new"?0:RenderFlags.ForceOldPipeline)|(kind=="generic-fade"?RenderFlags.DistanceFade:0);
                if(kind=="character")scene.Process(new MyRenderMessageCreateRenderCharacter{ID=120,Model=model,WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=flags,DiffuseColor=color});
                else scene.Process(new MyRenderMessageCreateRenderEntity{ID=120,Model=model,WorldMatrix=MatrixD.CreateTranslation(0,0,-3),Flags=flags});
                scene.Process(new MyRenderMessageUpdateRenderEntity{ID=120,DiffuseColor=color});
                if(kind.StartsWith("generic",StringComparison.Ordinal)){
                    scene.Process(new MyRenderMessageCreateRenderInstanceBuffer{ID=2900});
                    scene.Process(new MyRenderMessageUpdateRenderInstanceBufferRange{ID=2900,InstanceData=new[]{new VRage.MyInstanceData(Matrix.Identity)}});
                    scene.Process(new MyRenderMessageSetInstanceBuffer{ID=120,InstanceBufferId=2900,InstanceCount=1,LocalAabb=new BoundingBox(new Vector3(-1,-1,0),new Vector3(1,1,.015625f))});
                }
                direct();
            }
            void Remove(){scene.Process(new MyRenderMessageRemoveRenderObject{ID=120});scene.Process(new MyRenderMessageRemoveRenderObject{ID=2900});direct();}
            Create(Color.White);var baseline=Buffers();
            check(baseline[0][(32*64+32)*4+3]!=255,"entity color fixture has visible geometry: "+kind);
            foreach(var color in new[]{Color.Red,Color.Green,Color.Blue}){
                scene.Process(new MyRenderMessageUpdateRenderEntity{ID=120,DiffuseColor=color});
                direct();check(Same(baseline),"stock ignores entity diffuse update in every GBuffer channel: "+kind+" "+color);
                retained();check(Same(baseline),"prepared submission preserves ignored entity diffuse update: "+kind+" "+color);
            }
            if(kind!="character")foreach(var color in new[]{Color.Red,Color.Green,Color.Blue}){
                foreach(string prefix in new[]{"FixtureBase-","FixtureDecal-"})scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=120,MaterialName=MyStringId.GetOrCompute(prefix+Path.GetFileNameWithoutExtension(model)),DiffuseColor=color,Emissivity=1});
                direct();bool generic=kind.StartsWith("generic",StringComparison.Ordinal);
                check(Same(baseline)==generic,"stock material tint distinguishes actual generic streams from rigid optimization: "+kind+" "+color);
                var material=Buffers();retained();check(Same(material),"prepared material tint preserves the source stream contract: "+kind+" "+color);
                scene.Process(new MyRenderMessageUpdateRenderEntity{ID=120,DiffuseColor=Color.White});direct();check(Same(material),"ignored entity update preserves effective material tint: "+kind+" "+color);
            }
            Remove();Create(Color.Red);check(Same(baseline),"stock ignores entity diffuse creation value: "+kind);Remove();
        }
        check(backend.GetStats().ValidationErrors==0,"entity diffuse contract has zero Vulkan validation errors");
    }
}
