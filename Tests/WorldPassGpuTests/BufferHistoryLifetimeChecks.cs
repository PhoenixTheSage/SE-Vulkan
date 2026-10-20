using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage.Import;
using VRageMath;
using VRageRender;

partial class Program
{
    static void BufferHistoryLifetimeChecks(NativeBackend backend,Action<bool,string> check)
    {
        string output=Path.GetFullPath("TestResults/BufferHistoryLifetime");Directory.CreateDirectory(output);
        string pixelPath=Path.Combine(output,"history-attachment.hlsl");
        File.WriteAllText(pixelPath,"struct I { float4 position:SV_Position; nointerpolation float4 row0:TEXCOORD0; nointerpolation float4 row1:TEXCOORD1; nointerpolation float4 row2:TEXCOORD2; }; float4 __pixel_shader(I i):SV_Target { return float4(abs(i.row0.x),abs(i.row1.y),abs(i.row2.z),1); }");
        using(var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"Compiler"))){var pixel=compiler.Compile(new VulkanShaderRequest{SourcePath=pixelPath,ContentDirectory=output,EntryPoint="__pixel_shader",Profile="ps_5_0",NativeAbiVersion=1});check(backend.InitGBufferAttachment(0,0,SpirvRuntimeModule.Prepare(File.ReadAllBytes(pixel.SpirvPath))),"GBuffer attachment consumer is enabled during asynchronous source growth");}
        const int size=512;
        backend.Resize(size,size,0);backend.WorldRecipesEnabled=false;backend.RigidBatchingEnabled=false;backend.ModelBatchingEnabled=false;
        var counts=new[]{4096,32768,131072};var sources=new Dictionary<int,byte[]>();
        foreach(int count in counts){var bytes=new byte[count*64];for(int i=0;i<count;i++)foreach(int component in new[]{0,20,40})BitConverter.GetBytes(1f).CopyTo(bytes,i*64+component);sources.Add(count,bytes);}
        uint normal=VF_Packer.PackNormal(Vector3.Backward);
        NativeWorldVertex Vertex(float x,float y)=>new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=normal&65535,NormalY=normal>>16,TangentX=1,TangentW=1},WeightX=1};
        backend.BeginUploads();backend.UploadWorldMesh(99001,new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)},new uint[]{0,2,1});backend.UploadTexture(99002,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(99003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(99004,1,1,0,new byte[]{255,0,0,0},false);backend.EndUploads();
        var projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;
        var frame=WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size,size,light);
        var objects=WorldConstants.Object(backend.WorldLayouts[2],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0);
        // Deliberate overdraw keeps the preceding submission live while the
        // other slot is reused. Each size has steady frames to establish
        // motion history before the next source-buffer growth.
        foreach(int count in counts)for(int steady=0;steady<3;steady++){
            backend.BeginPacketCollection();backend.AddWorldPacket(2,99001,99002,99003,99004,0,objects,sources[count],(uint)count,512|0x08000000);
            backend.QueueWorld(frame,NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"asynchronous history frame begins: instances="+count+" steady="+steady);
            backend.EndFrame(); // No capture/readback wait between submissions.
            var stats=backend.GetStats();check(stats.ValidationEnabled!=0&&stats.ValidationErrors==0,"overlapping motion history survives slot reuse and source growth: "+count+"/"+steady+(stats.ValidationErrors!=0?" "+stats.Detail:""));
        }
        // Readback only after the entire sequence, to retire all submissions.
        var depth=backend.ReadbackWorldGbuffer(3,size,size);
        check(depth.Length>0,"asynchronous history sequence completes its final GPU work");
        check(backend.GetStats().ValidationErrors==0,"asynchronous buffer growth has zero core/synchronization validation errors");
    }
}
