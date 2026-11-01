using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.Library.Utils;
using VRageMath;
using VRageRender;

partial class Program
{
    static void StagedPublicationChecks(NativeBackend backend,VulkanScene scene,MatrixD projection,
        MyEnvironmentLightData light,Func<byte[]> frame,Action<bool,string> check)
    {
        // Establish actual GPU-owned geometry before switching to queue-time latches.
        var expected=frame();
        var ownership=scene.GetGpuSceneStats();
        check((int)ownership.GetType().GetProperty("Actors")!.GetValue(ownership)!>0,"fixture contains GPU-owned geometry");
        backend.SetBuildStages(true);
        void QueueFrame()
        {
            backend.BeginUploads();backend.BeginPacketCollection();
            scene.SetCamera(Vector3D.Zero,MyTimeSpan.Zero,1f/60);scene.Draw(backend);
            backend.EndUploads();scene.PublishNativePassScene();
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,renderSettings:scene.RenderSettings),
                NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
        }
        foreach(bool asynchronous in new[]{false,true,false,true})
        {
            QueueFrame();
            // Deliberately stop stage B between QueueWorld and BeginFrame while
            // stage A republishes. This is the startup race, independent of timing.
            using var entered=new ManualResetEventSlim();
            var publication=Task.Run(()=>{entered.Set();backend.PublishGpuScene(15,0,0);});
            entered.Wait();bool replacedEarly=publication.Wait(200);
            // Always finish the frame before asserting, including on the old DLL.
            check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"staged frame begins");
            backend.RequestReadback();
            if(asynchronous)backend.EndFrameAsync();else backend.EndFrame();
            var pixels=backend.Readback(64,64);
            check(publication.Wait(10000),"publication finishes after recording drains");
            Console.WriteLine("Staged publication changed pixel bytes: "+pixels.Zip(expected,(a,b)=>a!=b).Count(x=>x));
            check(!replacedEarly,"publication preserves the queued frame before BeginFrame");
            check(pixels.SequenceEqual(expected),"queued GPU geometry survives topology replacement (async="+asynchronous+")");
        }
        backend.SetBuildStages(false);
        check(frame().SequenceEqual(expected),"single-stage recording survives staged publication roundtrip");
        backend.SetBuildStages(true);QueueFrame();
        using var clearEntered=new ManualResetEventSlim();
        // Use the real managed caller so its ownership handles are retired too.
        var clearOwnership=typeof(VulkanScene).GetMethod("ClearGpuOwnership",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var clear=Task.Run(()=>{clearEntered.Set();clearOwnership.Invoke(scene,null);});
        clearEntered.Wait();bool clearedEarly=clear.Wait(200);
        check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"frame pending scene clear begins");
        backend.RequestReadback();backend.EndFrameAsync();var lastPixels=backend.Readback(64,64);
        check(clear.Wait(10000),"scene clear finishes after recording drains");
        check(!clearedEarly,"scene clear preserves the queued frame before BeginFrame");
        check(lastPixels.SequenceEqual(expected),"queued GPU geometry survives scene clear");
        backend.SetBuildStages(false);scene.Clear();
        var stats=backend.GetStats();
        check(stats.ValidationEnabled!=0&&stats.ValidationErrors==0,"staged publication has zero core/synchronization errors: "+stats.Detail);
    }
}
