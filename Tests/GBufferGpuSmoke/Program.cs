using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage.Import;
using VRageMath;

internal static class Program
{
    private const string VertexId = VanillaGBufferShaders.VertexId;
    private const string PixelId = VanillaGBufferShaders.PixelId;
    private static int Main(string[] args)
    {
        if (args.Length < 1) throw new ArgumentException("Pass the installed Bin64 directory and optionally the output directory.");
        string bin = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => { string path = Path.Combine(bin, name.Name + ".dll"); return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null; };
        return Run(bin, args.Length > 1 ? Path.GetFullPath(args[1]) : Path.GetFullPath("TestResults/GBufferGpuSmoke"));
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Run(string bin, string output)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS " + message); }
        void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is InvalidOperationException || e is InvalidDataException || e is ArgumentException) { Check(true, message); return; } throw new Exception("Accepted invalid input: " + message); }
        Check(Marshal.SizeOf<NativeGBufferVertex>() == 48 && Marshal.SizeOf<NativeGBufferDraw>() == 168 && Marshal.SizeOf<NativeGBufferStats>() == 48, "stock C ABI packing");
        var library = new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"), Path.Combine(bin, "../Content/Shaders"), Path.Combine(bin, "VRage.Render11.dll"));
        var vs = library.Load(VertexId); var ps = library.Load(PixelId);
        var frame = new ReflectedConstantBuffer(ps.Layout, "Frame");
        float[] identity = NativeBackend.MatrixValues(MatrixD.Identity);
        frame.Set("frame_.Environment.view_matrix", identity);
        frame.Set("frame_.Screen.resolution", 64, 64);
        foreach (string name in new[] { "Albedo", "Metalness", "Gloss", "Ao", "Emissive", "ColorMask" }) frame.Set("frame_.TextureDebugMultipliers." + name + "Multiplier", 1);
        Reject(() => frame.Set("frame_.Screen.resolution", 1), "constant writer rejects wrong vector size");
        Reject(() => frame.SetUInt("frame_.Environment.view_matrix", 1), "constant writer rejects incompatible scalar type");
        Reject(() => frame.Set("frame_.missing", 0), "constant writer rejects unknown layout path");
        using var backend = new NativeBackend(null);
        backend.Create(64, 64, 0, true, Path.Combine(output, "Cache")); backend.InitializeGBuffer(vs, ps);
        backend.UploadTexture(10, 1, 1, 0, new byte[] {64,128,192,64}, true);
        backend.UploadTexture(11, 1, 1, 0, new byte[] {128,128,255,128}, false);
        backend.UploadTexture(12, 1, 1, 0, new byte[] {200,64,0,0}, false);
        NativeGBufferVertex Vertex(float x, float y, float u, float v)
        {
            uint normal = VF_Packer.PackNormal(Vector3.Forward * -1); Vector4 tangent = new Vector4(1,0,0,1); uint packedTangent = VF_Packer.PackTangentSign(ref tangent);
            return new NativeGBufferVertex { X=x,Y=y,Z=.5f,Scale=1,NormalX=normal&65535,NormalY=normal>>16,TangentX=(packedTangent&255)/255f,TangentY=((packedTangent>>8)&255)/255f,TangentZ=((packedTangent>>16)&255)/255f,TangentW=(packedTangent>>24)/255f,U=u,V=v };
        }
        var vertices = new[] { Vertex(-.8f,-.8f,0,0),Vertex(.8f,-.8f,1,0),Vertex(0,.8f,.5f,1) };
        backend.UploadGBufferMesh(1, vertices, new uint[] {0,1,2});
        Reject(() => backend.UploadGBufferMesh(2, vertices, new uint[] {0,1,3}), "native stock indices are bounded");
        ReflectedConstantBuffer Object(MatrixD world, Vector3 tint, float dither = 0)
        {
            var result = new ReflectedConstantBuffer(ps.Layout, "Object");
            result.Set("object_.matrix_row0", (float)world.M11,(float)world.M21,(float)world.M31,(float)world.M41);
            result.Set("object_.matrix_row1", (float)world.M12,(float)world.M22,(float)world.M32,(float)world.M42);
            result.Set("object_.matrix_row2", (float)world.M13,(float)world.M23,(float)world.M33,(float)world.M43);
            result.Set("object_.color_mul", tint.X,tint.Y,tint.Z);result.Set("object_.emissive",2);result.Set("object_.custom_alpha",dither,0);
            result.SetUInt("object_.LOD", 7); return result;
        }
        var draw = new NativeGBufferDraw { Mesh=1,Color=10,Normal=11,Extensions=12,ObjectConstants=Object(MatrixD.Identity,Vector3.One).Bytes };
        backend.RenderGBuffer(64,64,frame.Bytes,identity,new[] {draw});
        var color = backend.ReadGBuffer(0,64,64);var normalAo = backend.ReadGBuffer(1,64,64);var material = backend.ReadGBuffer(2,64,64);var depth = backend.ReadGBuffer(3,64,64);
        int center=(32*64+32)*4;
        Console.WriteLine($"CENTER color={string.Join(',',color.Skip(center).Take(4))}; material={string.Join(',',material.Skip(center).Take(4))}; normalAo=0x{BitConverter.ToUInt32(normalAo,center):x8}; depth={BitConverter.ToSingle(depth,center)}");
        Check(Math.Abs(color[center]-64)<=2 && Math.Abs(color[center+1]-128)<=2 && Math.Abs(color[center+2]-192)<=2 && color[center+3]==7,"stock color sRGB round-trip and LOD channel");
        Check(Math.Abs(material[center]-64)<=1 && Math.Abs(material[center+1]-128)<=2 && Math.Abs(material[center+2]-126)<=2,"stock metalness/gloss/emissivity channels");
        uint packed = BitConverter.ToUInt32(normalAo,center);
        Check(Math.Abs((int)(packed&1023)-512)<=4 && Math.Abs((int)((packed>>10)&1023)-512)<=4 && Math.Abs((int)((packed>>20)&1023)-802)<=2,"stock encoded view normal and AO in 10-bit attachment");
        Check(Math.Abs(BitConverter.ToSingle(depth,center)-.5f)<.00001f && BitConverter.ToSingle(depth,0)==0,"reversed depth writes geometry and clears background");
        Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output,"color.rgba8"),color);File.WriteAllBytes(Path.Combine(output,"normal-ao.a2b10g10r10"),normalAo);File.WriteAllBytes(Path.Combine(output,"material.rgba8"),material);File.WriteAllBytes(Path.Combine(output,"depth.float32"),depth);
        draw.ObjectConstants = Object(MatrixD.CreateTranslation(.7,0,0),Vector3.One).Bytes;
        backend.RenderGBuffer(64,64,frame.Bytes,identity,new[] {draw});
        var moved=backend.ReadGBuffer(3,64,64);Check(BitConverter.ToSingle(moved,center)==0 && BitConverter.ToSingle(moved,(32*64+53)*4)>.49f,"stock object matrix propagates translation");
        draw.ObjectConstants=Object(MatrixD.Identity,Vector3.One,.5f).Bytes;backend.RenderGBuffer(64,64,frame.Bytes,identity,new[] {draw});
        var dithered=backend.ReadGBuffer(3,64,64);int full=Enumerable.Range(0,4096).Count(i=>BitConverter.ToSingle(depth,i*4)>0),half=Enumerable.Range(0,4096).Count(i=>BitConverter.ToSingle(dithered,i*4)>0);
        Check(half>full*.4 && half<full*.6,"stock 8x8 dither retains approximately half the surface");
        draw.ObjectConstants=Object(MatrixD.Identity,Vector3.One).Bytes; backend.RenderGBuffer(32,48,frame.Bytes,identity,new[] {draw});
        Check(backend.ReadGBuffer(3,32,48).Length==32*48*4,"stock attachment resize and readback");
        backend.RenderGBuffer(32,48,frame.Bytes,identity,Array.Empty<NativeGBufferDraw>());
        Check(backend.ReadGBuffer(3,32,48).All(b=>b==0),"empty stock frame clears stale geometry");
        frame.Set("frame_.Environment.view_matrix",NativeBackend.MatrixValues(MatrixD.CreateRotationX(Math.PI/2)));
        backend.RenderGBuffer(64,64,frame.Bytes,identity,new[]{draw});var rotated=backend.ReadGBuffer(1,64,64);
        Check(Math.Abs((int)((BitConverter.ToUInt32(rotated,center)>>10)&1023)-150)<6,"stock view matrix rotates the encoded normal with DirectX layout");
        frame.Set("frame_.Environment.view_matrix",identity);
        backend.RenderGBuffer(64,64,frame.Bytes,NativeBackend.MatrixValues(MatrixD.CreateScale(.5,1,1)),new[]{draw});
        var narrower=backend.ReadGBuffer(3,64,64);int scaled=Enumerable.Range(0,4096).Count(i=>BitConverter.ToSingle(narrower,i*4)>0);
        Check(scaled>full*.45 && scaled<full*.55,"stock projection buffer changes rasterized coverage");
        var left=draw;left.ObjectConstants=Object(MatrixD.CreateScale(.35)*MatrixD.CreateTranslation(-.45,0,0),new Vector3(1,0,0)).Bytes;
        var right=draw;right.ObjectConstants=Object(MatrixD.CreateScale(.35)*MatrixD.CreateTranslation(.45,0,0),new Vector3(0,0,1)).Bytes;
        backend.RenderGBuffer(64,64,frame.Bytes,identity,new[]{left,right});var batched=backend.ReadGBuffer(0,64,64);
        Check(batched[(32*64+17)*4]>50 && batched[(32*64+17)*4+2]==0 && batched[(32*64+46)*4]==0 && batched[(32*64+46)*4+2]>180,"batched dynamic object offsets preserve separate transforms and colors");
        var near=draw;near.ObjectConstants=Object(MatrixD.CreateTranslation(0,0,.2),Vector3.One).Bytes;
        backend.RenderGBuffer(64,64,frame.Bytes,identity,new[]{near,draw});Check(Math.Abs(BitConverter.ToSingle(backend.ReadGBuffer(3,64,64),center)-.7f)<.00001,"reversed depth keeps the nearer stock surface independently of draw order");
        foreach(ref var vertex in vertices.AsSpan())vertex.Z=.75f;backend.UploadGBufferMesh(1,vertices,new uint[]{0,1,2});
        backend.RenderGBuffer(64,64,frame.Bytes,identity,new[]{draw});Check(Math.Abs(BitConverter.ToSingle(backend.ReadGBuffer(3,64,64),center)-.75f)<.00001,"stock mesh replacement updates persistent geometry");
        var missing=draw;missing.Normal=999999;Reject(()=>backend.RenderGBuffer(64,64,frame.Bytes,identity,new[]{missing}),"missing stock material resources fail before submission");
        Reject(()=>backend.ReadGBuffer(4,32,48),"unknown attachment rejected");
        Reject(()=>backend.ReadGBuffer(0,16,16),"wrong readback dimensions rejected");
        Reject(()=>backend.ReadGBuffer(0,32,128),"readback rejects a different shape with the same byte count");
        var stats=backend.GetStats();var stock=backend.GetGBufferStats();
        Check(stats.ValidationErrors==0,"stock pass has zero core/synchronization validation errors");
        Check(stock.Frames==10 && stock.Draws==11,"stock pass counters track actual submissions");
        backend.RemoveResource(2,1);
        Reject(()=>backend.RenderGBuffer(64,64,frame.Bytes,identity,new[]{draw}),"stock mesh removal rejects stale actor submissions");
        var alphaVs=library.Load(VanillaGBufferShaders.AlphaVertexId);var alphaPs=library.Load(VanillaGBufferShaders.AlphaPixelId);
        backend.InitializeGBuffer(alphaVs,alphaPs);
        Check(backend.GetGBufferStats().Frames==0 && backend.GetGBufferStats().Ready==1,"stock pipeline replacement resets resources and evidence");
        backend.UploadGBufferMesh(1,vertices,new uint[]{0,1,2});
        backend.UploadTexture(13,2,1,0,new byte[]{0,0,0,255,255,0,0,255},false);
        draw.Alpha=13;backend.RenderGBuffer(64,64,frame.Bytes,identity,new[]{draw});
        var clipped=backend.ReadGBuffer(3,64,64);int cutout=Enumerable.Range(0,4096).Count(i=>BitConverter.ToSingle(clipped,i*4)>0);
        Check(cutout>full*.4 && cutout<full*.6 && BitConverter.ToSingle(clipped,(40*64+18)*4)==0 && BitConverter.ToSingle(clipped,(40*64+46)*4)>.74f,"stock AlphaMasked samples red alpha and clips half the surface");
        backend.UploadTexture(13,1,1,0,new byte[]{0,255,255,255},false);backend.RenderGBuffer(64,64,frame.Bytes,identity,new[]{draw});
        Check(backend.ReadGBuffer(3,64,64).All(b=>b==0),"stock zero red alpha suppresses depth independently of texture alpha");
        backend.UploadTexture(13,1,1,0,new byte[]{255,0,0,0},false);backend.RenderGBuffer(64,64,frame.Bytes,identity,new[]{draw});
        var opaque=backend.ReadGBuffer(3,64,64);
        Check(Enumerable.Range(0,4096).Count(i=>BitConverter.ToSingle(opaque,i*4)>0)==full,"stock full red alpha restores exact coverage independently of texture alpha");
        var alphaStock=backend.GetGBufferStats();stats=backend.GetStats();
        Check(alphaStock.Frames==3 && alphaStock.Draws==3 && stats.ValidationErrors==0,"both stock pipelines have zero core/synchronization validation errors");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new {Checks=checks,VertexId,PixelId,stock.Frames,stock.Draws,AlphaVertexId=VanillaGBufferShaders.AlphaVertexId,AlphaPixelId=VanillaGBufferShaders.AlphaPixelId,AlphaFrames=alphaStock.Frames,AlphaDraws=alphaStock.Draws,stats.ValidationErrors,stats.DeviceName,FullPixels=full,DitheredPixels=half,AlphaPixels=cutout,Scope="Native offscreen stock material passes; game lighting/composition and GPU skinning/instancing pending"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(checks+" stock GBuffer checks passed.");return 0;
    }
}
