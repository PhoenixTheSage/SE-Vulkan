using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage.Import;
using VRageMath;
using VRageRender;

partial class Program
{
    static void ShadowCacheMenuChecks(NativeBackend backend,Action<bool,string> check)
    {
        const int resolution=256;
        string output=Path.GetFullPath("TestResults/ShadowCacheMenu");Directory.CreateDirectory(output);
        backend.WorldRecipesEnabled=false;backend.RigidBatchingEnabled=false;backend.ModelBatchingEnabled=false;
        backend.SetParallelCascades(false);backend.SetShadowQuality(resolution,0);
        uint normal=VF_Packer.PackNormal(Vector3.Backward);
        var vertices=new[]{new Vector3(-1,-1,-3),new Vector3(1,-1,-3),new Vector3(0,1,-3)}.Select(p=>new NativeWorldVertex{Material=new(){X=p.X,Y=p.Y,Z=p.Z,Scale=1,NormalX=normal&65535,NormalY=normal>>16,TangentX=1,TangentW=1},WeightX=1}).ToArray();
        backend.BeginUploads();backend.UploadWorldMesh(99501,vertices,new uint[]{0,2,1});backend.UploadTexture(99502,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(99503,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(99504,1,1,0,new byte[]{255,0,0,0},false);backend.EndUploads();
        var projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var shadow=MatrixD.CreateLookAt(new Vector3D(0,0,10),new Vector3D(0,0,-3),Vector3D.Up)*MatrixD.CreateOrthographic(8,8,1,100);
        var light=MyEnvironmentLightData.Default;
        uint active=1;bool scheduled=true;
        void Draw(bool world,uint updates=0){
            backend.BeginPacketCollection();backend.SetShadowSchedule(scheduled,updates,active);
            if(world){backend.AddWorldPacket(0,99501,99502,99503,99504,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),flags:0x10000000,cascadeMask:active);
                backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));}
            check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"shadow-cache frame begins world="+world);backend.EndFrame();
        }
        void Save(string name,float[] data)=>File.WriteAllBytes(Path.Combine(output,name+".depth32f"),System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan()).ToArray());
        foreach(bool policy in new[]{true,false})foreach(int count in new[]{1,4,8})foreach(bool parallel in new[]{false,true}){
            scheduled=policy;active=(1u<<count)-1;backend.SetParallelCascades(parallel);
            backend.SetShadowCascades(Enumerable.Range(0,count).SelectMany(_=>NativeBackend.MatrixValues(shadow)).ToArray(),Enumerable.Range(1,count).Select(i=>i*100f).ToArray());
            string label=$"scheduled-{scheduled}-cascades-{count}-parallel-{parallel}";
            float[][] Capture()=>Enumerable.Range(0,count).Select(c=>backend.ReadbackShadow((uint)c,resolution)).ToArray();
            Draw(true,active);var expected=Capture();Save(label+"-world-before",expected[0]);
            check(expected.All(d=>d.Count(v=>v<1)>100),"every world cascade contains a genuine shadow silhouette: "+label);
            check(backend.GetShadowCacheMask()==active,"world publishes CPU ownership for every rendered cascade: "+label);
            Draw(true);check(backend.GetPerformanceStats().ShadowCommands==0&&Capture().Zip(expected).All(p=>p.First.SequenceEqual(p.Second)),"unchanged world reuses exact cascades without draw commands: "+label);
            foreach(int menuFrames in new[]{1,2,3}){
                for(int frame=0;frame<menuFrames;frame++)Draw(false);
                var menu=Capture();Save(label+"-menu-"+menuFrames,menu[0]);
                check(menu.Take(Math.Min(4,count)).All(d=>d.All(v=>v==1)),"menu actually replaces shared cascade contents: "+label+" frames="+menuFrames);
                check(backend.GetShadowCacheMask()==(active&~15u),"menu invalidates only overwritten cascade ownership: "+label);
                check(menu.Skip(4).Zip(expected.Skip(4)).All(p=>p.First.SequenceEqual(p.Second)),"menu preserves untouched far cascade images: "+label);
                Draw(true);var resumed=Capture();Save(label+"-world-resumed-"+menuFrames,resumed[0]);
                check(resumed.Zip(expected).All(p=>p.First.SequenceEqual(p.Second)),"world resume restores every exact cascade despite no requested refresh: "+label+" frames="+menuFrames);
                check(backend.GetPerformanceStats().ShadowCommands>0,"overwritten world cascade ownership forces regeneration: "+label);
                check(backend.GetShadowCacheMask()==active,"resume restores complete cascade ownership: "+label);
                Draw(true);check(backend.GetPerformanceStats().ShadowCommands==0&&Capture().Zip(expected).All(p=>p.First.SequenceEqual(p.Second)),"restored world returns to cached shadow reuse: "+label);
            }
        }
        check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"shadow-cache menu sequence has zero core/synchronization errors");
    }
}
