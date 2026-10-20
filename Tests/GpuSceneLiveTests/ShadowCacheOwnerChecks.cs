using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using VRageMath;
using VRageRender;

partial class Program
{
    static void ShadowCacheOwnerChecks(NativeBackend backend,VulkanScene scene,MatrixD projection,Func<byte[]> render,Action<bool,string> check)
    {
        const int resolution=512; // Stock LOW cascade resolution; spotlights use 256.
        var field=typeof(VulkanScene).GetField("ShadowSystem",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var shadows=field.GetValue(scene)!;var type=shadows.GetType();
        var settings=(MyShadowsSettings)type.GetField("Settings")!.GetValue(shadows)!;
        settings.Data.CascadesCount=1;settings.Data.ShadowCascadeMaxDistance=15;settings.Data.ShadowCascadeMaxDistanceMultiplierHigh=1;settings.Data.ShadowCascadeZOffset=10;settings.Data.EnableShadowBlur=false;
        var state=scene.RenderSettings;state.EnableShadows=true;state.ShadowCascadeUsageBasedSkip=false;state.User.ShadowQuality=MyShadowsQuality.HIGH;state.User.ShadowMemoryQuality=MyRenderQualityEnum.LOW;scene.RenderSettings=state;
        var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        uint Updates()=>(uint)type.GetProperty("UpdateMask")!.GetValue(shadows)!;
        void Frame(){type.GetMethod("Update")!.Invoke(shadows,new object[]{backend,MatrixD.Identity,Vector3.Forward,scene.RenderSettings,projection,Vector3D.Zero});render();type.GetMethod("CommitPreparedFrame")!.Invoke(shadows,null);}
        void Menu(){backend.BeginPacketCollection();check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"owner-cache no-world menu frame begins");backend.EndFrame();}
        foreach(int count in new[]{1,6})foreach(string owner in new[]{"gpu","native","managed"}){
            settings.Data.CascadesCount=count;uint active=(1u<<count)-1,overwritten=active&15;
            string label=owner+" cascades="+count;
            float[][] Capture()=>Enumerable.Range(0,count).Select(c=>backend.ReadbackShadow((uint)c,resolution)).ToArray();
            bool Same(float[][] a,float[][] b)=>a.Zip(b).All(p=>p.First.SequenceEqual(p.Second));
            scene.GpuOpaqueOwnershipEnabled=owner=="gpu";scene.NativePassSceneEnabled=owner!="managed";
            for(int c=0;c<count;c++)settings.ShadowCascadeFrozen[c]=false;Frame();var expected=Capture();
            var actor=actors[200u]!;
            check(((bool)actor.GetType().GetField("GpuOwned")!.GetValue(actor)!)==(owner=="gpu")&&((bool)actor.GetType().GetField("PassOwned")!.GetValue(actor)!)==(owner=="native"),"shadow cache uses actual requested owner: "+owner);
            check(expected.Any(d=>d.Count(v=>v<1)>10),"actual owner fills a positive cascade silhouette: "+label);
            check(backend.GetShadowCacheMask()==active,"every scene cascade has world ownership: "+label);
            for(int c=0;c<count;c++)settings.ShadowCascadeFrozen[c]=true;Frame();
            check(Updates()==0&&backend.GetPerformanceStats().ShadowCommands==0&&Same(Capture(),expected),"frozen managed scheduler retains owner cascades without draws: "+label);
            foreach(int frames in new[]{1,2,3}){
                for(int i=0;i<frames;i++)Menu();
                var menu=Capture();check(menu.Take(4).All(d=>d.All(v=>v==1)),"menu overwrites actual owner cascades: "+label+" frames="+frames);
                check(menu.Skip(4).Zip(expected.Skip(4)).All(p=>p.First.SequenceEqual(p.Second)),"menu preserves untouched frozen far cascades: "+label);
                Frame();var actual=Capture();
                Console.WriteLine("OWNER CACHE "+label+" menuFrames="+frames+" updates="+Updates()+" covered="+actual.Sum(d=>d.Count(v=>v<1)));
                check(Same(actual,expected),"resume republishes actual owner depth views despite frozen scheduler: "+label+" frames="+frames);
                check(Updates()==overwritten&&backend.GetPerformanceStats().ShadowCommands>0,"invalidated owner cache selects exactly overwritten depth views before submission: "+label);
                Frame();check(Updates()==0&&backend.GetPerformanceStats().ShadowCommands==0&&Same(Capture(),expected),"restored owner returns to frozen cache reuse: "+label);
            }
        }
        check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"owner shadow-cache sequence has zero core/synchronization errors");
    }
}
