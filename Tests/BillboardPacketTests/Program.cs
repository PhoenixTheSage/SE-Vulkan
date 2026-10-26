using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using VRage.Utils;
using VRageMath;
using VRageRender;
class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static int Main(string[] args)
    {
        string bin = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (c,n) => File.Exists(Path.Combine(bin,n.Name+".dll")) ? c.LoadFromAssemblyPath(Path.Combine(bin,n.Name+".dll")) : null;
        Run(); return 0;
    }
    static void Run()
    {
        ParallelTasks.Parallel.Scheduler = new ParallelTasks.FakeTaskScheduler();
        var material = new MyTransparentMaterial(MyStringId.GetOrCompute("HudTriangleTest"), MyTransparentMaterialTextureType.FileTexture,
            "unused.dds", null, 1, false, false, Vector4.One, Vector4.Zero, Vector4.One, Vector4.One, false, false);
        MyTransparentMaterials.AddMaterial(material);
        var scene = new VulkanScene(); var backend = new NativeBackend();
        using var renderer = new VulkanBillboards(scene,backend);
        var triangle = new MyTriangleBillboard { Material=material.Id, BlendType=MyBillboard.BlendTypeEnum.PostPP,
            CustomViewProjection=-1, Color=Vector4.One, ColorIntensity=1,
            Position0=new(101,202,297), Position1=new(104,202,297), Position2=new(101,206,297), Position3=new(999),
            UV0=new(.1f,.2f), UV1=new(.7f,.2f), UV2=new(.1f,.9f), Normal0=Vector3.Forward };
        var snapshot=(MyTriangleBillboard)typeof(VulkanBillboards).GetMethod("Copy",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{triangle});
        triangle.UV0=Vector2.Zero; triangle.Normal0=Vector3.Zero; triangle.Position0=Vector3D.Zero;
        Check(snapshot.UV0==new Vector2(.1f,.2f) && snapshot.Normal0==Vector3.Forward && snapshot.Position0.X==101,"pooled triangle snapshot owns subtype, UVs, normal and positions");
        var shared=new MySharedData(); shared.AddPersistentBillboard(snapshot); shared.BeforeRender(null);
        renderer.Draw(shared);
        Check(backend.BillboardPackets.Count==1,"persistent HUD triangle reaches Vulkan packet queue");
        var packet=backend.BillboardPackets[0]; var v=packet.Vertices;
        Check(v[0]==1 && v[1]==2 && v[2]==-3 && v[3]==.1f && v[4]==.2f && v[8]==.7f && v[14]==.9f,"camera-relative triangle geometry retains final atlas UVs");
        bool degenerate=true; for(int i=0;i<5;i++) degenerate &= v[10+i]==v[15+i];
        Check(degenerate,"second indexed triangle is degenerate rather than a spurious HUD quad");
        Check(packet.Bucket==4 && BitConverter.ToSingle(packet.Data,40)==-1,"PostPP blend bucket and supplied normal survive submission");
        shared.ClearPersistentBillboards(); backend.BillboardPackets.Clear();
        shared.AddPersistentBillboard(new MyBillboard {Material=material.Id, CustomViewProjection=-1, ColorIntensity=1, UVSize=Vector2.One,
            Position0=new(0,1,0),Position1=new(1,1,0),Position2=new(1,0,0),Position3=new(0,0,0)});
        shared.BeforeRender(null); renderer.Draw(shared);
        v=backend.BillboardPackets[0].Vertices;
        Check(v[10]!=v[15] && v[3]==0 && v[8]==1 && v[14]==1,"ordinary quad keeps fourth corner and rectangular UVs");
        shared.ClearPersistentBillboards(); backend.BillboardPackets.Clear();
        // Deliberately vary corner distance: an opaque background submitted
        // first must stay behind the text/cursor submitted after it.
        for (int i=0;i<32;i++)
        {
            var layer=new MyTriangleBillboard {Material=material.Id, BlendType=MyBillboard.BlendTypeEnum.PostPP,
                CustomViewProjection=-1, Color=new Vector4(i/32f,0,0,1), ColorIntensity=1,
                Position0=new(100+i,202,299.95),Position1=new(101+i,202,299.95),Position2=new(100+i,203,299.95)};
            // Rich HUD's actual entry point is the transient billboard list.
            shared.Billboards.Read.Billboards.Add(layer);
        }
        renderer.Draw(shared);
        Check(backend.BillboardPackets.Count==32,"transient Rich HUD triangle list reaches Vulkan");
        for(int i=0;i<32;i++)
            Check(BitConverter.ToSingle(backend.BillboardPackets[i].Data,0)==i/32f,"PostPP layer order "+i);
        // Forked emission (plan 2.2): collect on this thread, flares and emission on a worker, finish
        // here. Equals serial emission with the flare billboard queued first, both when the worker's
        // packets are kept and when a texture miss makes the finish re-emit on this thread.
        string Signature()=>string.Join("|",backend.BillboardPackets.ConvertAll(p=>p.Texture+","+p.Flags+","+p.Bucket+","+string.Join(",",p.Vertices)+","+Convert.ToBase64String(p.Data)));
        void Flare()=>renderer.AddPointBillboard(material.Id,Vector4.One,new Vector3D(110,200,280),2,0);
        VulkanBillboards.Verify=true;
        backend.BillboardPackets.Clear();Flare();renderer.Draw(shared);string serial=Signature();
        foreach(bool resident in new[]{true,false})
        {
            ClientPlugin.Rendering.Content.VulkanTextureCache.Resident=resident;backend.BillboardPackets.Clear();
            renderer.Collect(shared);var worker=new System.Threading.Thread(()=>renderer.EmitAhead(Flare));worker.Start();worker.Join();renderer.FinishAhead();
            Check(Signature()==serial&&backend.BillboardPackets.Count==33,resident?"forked flares and billboards match serial packets":"texture miss falls back to the same packets on the render thread");
        }
        ClientPlugin.Rendering.Content.VulkanTextureCache.Resident=true;
        Check(renderer.AheadFrames==1&&renderer.AheadFallbacks==1,"fork counters record one kept and one re-emitted frame");
        Check(renderer.VerifyMatches==1&&renderer.VerifyMismatches==0,"verify mode re-emits the kept frame on this thread and finds it identical");VulkanBillboards.Verify=false;
    }
}
