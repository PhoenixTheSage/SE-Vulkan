using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    // Builds sevk_tenant_record batches: 128-byte commands, 32-byte bindings, one blob.
    sealed class TenantList
    {
        readonly List<byte[]> commands = new(), bindings = new(); readonly List<byte> blob = new();
        static void W(byte[] b, int at, uint v) => BitConverter.GetBytes(v).CopyTo(b, at);
        static void W(byte[] b, int at, ulong v) => BitConverter.GetBytes(v).CopyTo(b, at);
        public int First => bindings.Count;
        public void Bind(uint binding, uint kind, ulong id = 0, ulong offset = 0, ulong bytes = 0)
        { var b = new byte[32]; W(b, 0, binding); W(b, 4, kind); W(b, 8, id); W(b, 16, offset); W(b, 24, bytes); bindings.Add(b); }
        public ulong Data(byte[] data) { ulong at = (ulong)blob.Count; blob.AddRange(data); while (blob.Count % 16 != 0) blob.Add(0); return at; }
        public void Uniform(uint binding, byte[] data) { ulong at = Data(data); Bind(binding, 3, 0, at, (ulong)data.Length); }
        public void Command(uint op, int first = 0, ulong pipeline = 0, uint x = 0, uint y = 0, uint z = 0, ulong[] targets = null, uint[] layers = null, ulong source = 0,
            ulong offset = 0, ulong blobOffset = 0, ulong bytes = 0, uint[] value = null, uint flags = 0)
        {
            var c = new byte[128]; W(c, 0, op); W(c, 4, (uint)first); W(c, 8, (uint)(op == 1 || op == 2 ? bindings.Count - first : 0)); W(c, 12, (uint)(targets?.Length ?? 0));
            W(c, 16, x); W(c, 20, y); W(c, 24, z); W(c, 28, flags); W(c, 32, pipeline); W(c, 40, source);
            for (int i = 0; i < (targets?.Length ?? 0); i++) W(c, 48 + i * 8, targets[i]);
            for (int i = 0; i < (layers?.Length ?? 0); i++) W(c, 72 + i * 4, layers[i]);
            W(c, 88, offset); W(c, 96, blobOffset); W(c, 104, bytes); for (int i = 0; i < (value?.Length ?? 0); i++) W(c, 112 + i * 4, value[i]);
            commands.Add(c);
        }
        public byte[] Commands => commands.SelectMany(c => c).ToArray();
        public byte[] Bindings => bindings.SelectMany(b => b).ToArray();
        public byte[] Blob => blob.ToArray();
    }

    // Audit P3 step 4: the native tenant compute session SE-RT's SoftwareDx11 port records into.
    static int TenantSessionRegression(string output, NativeBackend backend, MatrixD projection, MyEnvironmentLightData light)
    {
        int checks = 0; void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
        const ulong Hdr = 0xFFFF000000000001UL, Depth = 0xFFFF000000000002UL, CloseCube = 0xFFFF000000000021UL;
        const int Rgba16f = 97, Rgba32f = 109, R32ui = 98, Rgba8ui = 41;
        var session = backend.TenantSession();
        Check(new[] { "ApiVersion", "CreateBuffer", "CreateImage", "CreateCompute", "CreateRaster", "Destroy", "Record", "Readback", "Stats" }.All(session.ContainsKey) && ((Func<int>)session["ApiVersion"])() == 1,
            "the session exposes its v1 delegates by name");
        using var compiler = VulkanShaderCompiler.CreateBundled(Path.Combine(output, "TenantSessionCache"));
        byte[] Spv(string name, string source, string entry, string profile)
        {
            string file = Path.Combine(output, "tenant." + name + ".hlsl"); File.WriteAllText(file, source);
            return SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiler.Compile(new VulkanShaderRequest { SourcePath = file, EntryPoint = entry, Profile = profile }).SpirvPath));
        }
        var post = new VulkanPostprocess(); post.Hbao.Enabled = false; post.Ssao.Enabled = false; post.Settings.BloomEnabled = false; post.Settings.EnableEyeAdaptation = false;
        void Frame(Action record)
        {
            backend.BeginPacketCollection(); post.Configure(backend, 64, 64, false, view: MatrixD.Identity, projection: projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout, MatrixD.Identity, projection, 64, 64, light, postprocess: post.Settings), NativeBackend.MatrixValues(projection), NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            if (!backend.BeginFrame(projection, Vector3.Forward, Vector3.One, Vector3.Zero, Color.Black)) throw new Exception("tenant frame did not begin");
            try { record?.Invoke(); } finally { backend.EndFrame(); }
        }
        byte[] Read(ulong id, int bytes, int layer = 0, long offset = 0) { var data = new byte[bytes]; backend.TenantReadback(id, layer, offset, data); return data; }
        uint U(byte[] d, int i) => BitConverter.ToUInt32(d, i * 4);
        float F(byte[] d, int i) => BitConverter.ToSingle(d, i * 4);
        float H(byte[] d, int i) => (float)BitConverter.ToHalf(d, i * 2);
        bool Near(float a, float b, float e = 1e-3f) => Math.Abs(a - b) <= e;
        var ids = new List<ulong>();
        ulong Keep(ulong id) { ids.Add(id); return id; }

        // Structured buffers, a uniform and atomics.
        var bufferCs = Keep(backend.TenantCreateCompute(Spv("buffers", "StructuredBuffer<uint> In:register(t0);RWStructuredBuffer<uint> Out:register(u0);RWStructuredBuffer<uint> Count:register(u1);" +
            "cbuffer P:register(b1){uint Scale;uint3 Pad;};[numthreads(64,1,1)]void main(uint3 id:SV_DispatchThreadID){Out[id.x]=In[id.x]*Scale+id.x;uint o;InterlockedAdd(Count[0],1,o);}", "main", "cs_5_0"), "main"));
        var input = Keep(backend.TenantCreateBuffer(256 * 4)); var result = Keep(backend.TenantCreateBuffer(256 * 4)); var counter = Keep(backend.TenantCreateBuffer(16));
        // Storage images DXC types Rgba32f bind RGBA16F, uint and array images.
        var imageCs = Keep(backend.TenantCreateCompute(Spv("images", "RWTexture2D<float4> Color:register(u0);RWTexture2D<uint> Ids:register(u1);RWTexture2DArray<float4> Layers:register(u2);" +
            "[numthreads(8,8,1)]void main(uint3 id:SV_DispatchThreadID){Color[id.xy]=float4(id.x/63.0,id.y/63.0,.5,1);Ids[id.xy]=id.x+id.y*64;Layers[uint3(id.xy,1)]=float4(1,2,3,4)*(id.x+1);}", "main", "cs_5_0"), "main"));
        var color = Keep(backend.TenantCreateImage(64, 64, 1, Rgba16f, 0)); var idImage = Keep(backend.TenantCreateImage(64, 64, 1, R32ui, 0)); var layers = Keep(backend.TenantCreateImage(64, 64, 2, Rgba32f, 0));
        var colorCopy = Keep(backend.TenantCreateImage(64, 64, 1, Rgba16f, 0)); var noise = Keep(backend.TenantCreateImage(4, 4, 2, Rgba8ui, 0));
        // Fullscreen MRT into a tenant image and an array layer; then the renderer HDR target.
        string vs = "struct V{float4 p:SV_Position;float2 uv:TEXCOORD0;};V vs(uint id:SV_VertexID){V o;o.uv=float2((id<<1)&2,id&2);o.p=float4(o.uv*float2(2,-2)+float2(-1,1),0,1);return o;}";
        var vsBytes = Spv("fullscreen", vs, "vs", "vs_5_0");
        var mrt = Keep(backend.TenantCreateRaster(vsBytes, "vs", Spv("mrt", vs + "struct O{float4 a:SV_Target0;float4 b:SV_Target1;};cbuffer P:register(b1){float4 Tint;};" +
            "O ps(V i){O o;o.a=float4(i.uv,0,1)*Tint;o.b=float4(i.p.xy,7,1);return o;}", "ps", "ps_5_0"), "ps", new[] { Rgba32f, Rgba16f }));
        var composite = Keep(backend.TenantCreateRaster(vsBytes, "vs", Spv("composite", vs + "float4 ps(V i):SV_Target0{return float4(2,1,.5,1);}", "ps", "ps_5_0"), "ps", new[] { 0 }));
        var targetA = Keep(backend.TenantCreateImage(32, 16, 1, Rgba32f, 0)); var targetB = Keep(backend.TenantCreateImage(32, 16, 2, Rgba16f, 0));
        // Sampling: a tenant array with a sampler, renderer HDR after the composite, renderer depth/probe, the frame block, and null bindings.
        var sampleCs = Keep(backend.TenantCreateCompute(Spv("sample", "Texture2DArray<float4> Layers:register(t0);Texture2D<float4> Hdr:register(t1);Texture2D<uint> Missing:register(t2);StructuredBuffer<uint> MissingBuffer:register(t3);" +
            "Texture2D<float> Depth:register(t4);TextureCube<float4> Close:register(t5);Texture2DArray<uint4> Noise:register(t6);SamplerState Point:register(s0);RWStructuredBuffer<float4> Out:register(u0);RWStructuredBuffer<uint> Discard:register(u1);" +
            "cbuffer Frame:register(b0){float4 F[80];};cbuffer Unbound:register(b2){float4 Z;};" +
            "[numthreads(1,1,1)]void main(){Out[0]=Layers.SampleLevel(Point,float3(5.5/64,3.5/64,1),0);Out[1]=Hdr.Load(int3(10,10,0));Out[2]=float4(Missing.Load(int3(0,0,0)),MissingBuffer[0],Z.x,0);" +
            "Out[3]=float4(Depth.Load(int3(32,32,0)),Close.SampleLevel(Point,float3(0,0,1),0).xyz);Out[4]=F[0]+F[1]+F[2]+F[3];Out[5]=float4(Noise.Load(int4(2,1,1,0)));Discard[0]=7;}", "main", "cs_5_0"), "main"));
        var samples = Keep(backend.TenantCreateBuffer(8 * 16));
        var copyTarget = Keep(backend.TenantCreateBuffer(256 * 4));
        Check(backend.TenantStats().Pipelines == 5 && backend.TenantStats().Buffers == 5 && backend.TenantStats().Images == 7, "tenant buffers, images and pipelines are created");

        string refused = null; try { backend.TenantRecord(new TenantList().Also(l => l.Command(3, targets: new[] { input })).Commands, null, null); } catch (Exception e) { refused = e.Message; }
        Check(refused != null && refused.Contains("between begin and end frame"), "recording outside a frame is refused");

        var values = Enumerable.Range(0, 256).Select(i => (uint)(i * 3)).ToArray(); var valueBytes = values.SelectMany(BitConverter.GetBytes).ToArray();
        var noiseLayer = Enumerable.Range(0, 64).Select(i => (byte)(i * 3)).ToArray();
        ulong before = backend.TenantStats().Frames;
        Frame(() =>
        {
            var l = new TenantList();
            ulong at = l.Data(valueBytes); l.Command(7, targets: new[] { input }, blobOffset: at, bytes: (ulong)valueBytes.Length);
            l.Command(3, targets: new[] { counter }, value: new uint[] { 5 });
            // Guest register bands: b -> 0, t -> 128, s -> 256, u -> 384.
            int first = l.First; l.Bind(128, 2, input); l.Bind(384, 2, result); l.Bind(385, 2, counter); l.Uniform(1, BitConverter.GetBytes(2u).Concat(new byte[12]).ToArray());
            l.Command(1, first, bufferCs, 4, 1, 1);
            ulong noiseAt = l.Data(noiseLayer); l.Command(8, targets: new[] { noise }, layers: new uint[] { 1 }, blobOffset: noiseAt, bytes: (ulong)noiseLayer.Length);
            first = l.First; l.Bind(384, 1, color); l.Bind(385, 1, idImage); l.Bind(386, 1, layers); l.Command(1, first, imageCs, 8, 8, 1);
            l.Command(6, targets: new[] { colorCopy }, source: color);
            l.Command(5, targets: new[] { copyTarget }, source: result, offset: 0, bytes: 256 * 4);
            first = l.First; l.Uniform(1, new[] { 1f, 2, 4, 1 }.SelectMany(BitConverter.GetBytes).ToArray());
            l.Command(2, first, mrt, 32, 16, targets: new[] { targetA, targetB }, layers: new uint[] { 0, 1 });
            first = l.First; l.Command(2, first, composite, 64, 64, targets: new[] { Hdr });
            first = l.First; l.Bind(128, 1, layers); l.Bind(129, 4, Hdr); l.Bind(132, 4, Depth); l.Bind(133, 4, CloseCube); l.Bind(134, 1, noise); l.Bind(256, 7, 2); l.Bind(0, 6); l.Bind(384, 2, samples);
            l.Command(1, first, sampleCs, 1, 1, 1);
            backend.TenantRecord(l.Commands, l.Bindings, l.Blob);
            string busy = null; try { backend.TenantDestroy(input); } catch (Exception e) { busy = e.Message; }
            Check(busy != null && busy.Contains("used by this frame"), "destroying a resource this frame's commands use is refused");
        });
        var stats = backend.TenantStats();
        Check(stats.Frames == before + 1 && stats.Dispatches == 3 && stats.Draws == 2 && stats.Rejected == 0, "one frame runs three dispatches and two draws without rejection");
        var output0 = Read(result, 256 * 4);
        Check(Enumerable.Range(0, 256).All(i => U(output0, i) == values[i] * 2 + (uint)i), "a dispatch reads an uploaded structured buffer and a blob uniform into a RW buffer");
        Check(U(Read(counter, 16), 0) == 5 + 256, "fill then InterlockedAdd accumulate in a tenant RW buffer");
        Check(Read(copyTarget, 256 * 4).SequenceEqual(output0), "buffer copy duplicates the dispatch output");
        var colorBytes = Read(color, 64 * 64 * 8); int px = (20 * 64 + 40) * 4;
        Check(Near(H(colorBytes, px), 40 / 63f) && Near(H(colorBytes, px + 1), 20 / 63f) && Near(H(colorBytes, px + 2), .5f), "an Rgba32f-typed RWTexture2D writes a RGBA16F tenant image (format-free storage)");
        Check(Read(colorCopy, 64 * 64 * 8).SequenceEqual(colorBytes), "image copy duplicates the storage image");
        var idBytes = Read(idImage, 64 * 64 * 4); Check(U(idBytes, 17 * 64 + 9) == 9 + 17 * 64, "a RWTexture2D<uint> writes a R32_UINT image");
        var layerBytes = Read(layers, 64 * 64 * 16, 1); Check(Near(F(layerBytes, (3 * 64 + 5) * 4 + 1), 12) && Read(layers, 64 * 64 * 16, 0).All(b => b == 0), "a RWTexture2DArray writes one layer and leaves the zeroed other layer");
        var a = Read(targetA, 32 * 16 * 16); int pa = (2 * 32 + 3) * 4;
        Check(Near(F(a, pa), 3.5f / 32) && Near(F(a, pa + 1), 2.5f / 16 * 2) && Near(F(a, pa + 3), 1), "fullscreen MRT target 0 gets DX11 top-left UVs and the blob uniform");
        var b = Read(targetB, 32 * 16 * 8, 1);
        Check(Near(H(b, pa), 3.5f) && Near(H(b, pa + 1), 2.5f) && Near(H(b, pa + 2), 7) && Read(targetB, 32 * 16 * 8, 0).All(x => x == 0), "fullscreen MRT target 1 writes only its array layer with DX11 SV_Position");
        var s = Read(samples, 8 * 16);
        Check(Near(F(s, 0), 6) && Near(F(s, 1), 12) && Near(F(s, 3), 24), "a tenant array samples through the point-clamp tenant sampler");
        Check(Near(F(s, 4), 2) && Near(F(s, 5), 1) && Near(F(s, 6), .5f), "a later dispatch samples renderer HDR after the tenant composite wrote it");
        Check(F(s, 8) == 0 && F(s, 9) == 0 && F(s, 10) == 0, "unbound textures, buffers and uniforms read zero like DX11 null bindings");
        Check(F(s, 12) >= 0 && F(s, 12) <= 1 && Enumerable.Range(13, 3).All(i => float.IsFinite(F(s, i))), "renderer depth and the close environment cube bind by renderer id");
        Check(Enumerable.Range(16, 4).All(i => float.IsFinite(F(s, i))) && Enumerable.Range(16, 4).Any(i => F(s, i) != 0), "the world frame block binds as b0");
        Check(Near(F(s, 20), 72) && Near(F(s, 23), 81), "an uploaded RGBA8_UINT layer samples as uint4");

        // A binding of the wrong kind is rejected at execution without validation errors.
        Frame(() => { var l = new TenantList(); int first = l.First; l.Bind(384, 1, color); l.Command(1, first, bufferCs, 1, 1, 1); backend.TenantRecord(l.Commands, l.Bindings, l.Blob); });
        Check(backend.TenantStats().Rejected == 1 && backend.TenantStats().Dispatches == 3, "a storage buffer bound with an image is rejected, not dispatched");
        refused = null; try { Frame(() => { var l = new TenantList(); l.Command(1, 0, 999999, 1, 1, 1); backend.TenantRecord(l.Commands, l.Bindings, l.Blob); }); } catch (Exception e) { refused = e.Message; }
        Check(refused != null && refused.Contains("pipeline"), "an unknown pipeline is refused at record time");
        Frame(null); Check(backend.TenantStats().Frames == before + 2, "a frame without tenant commands runs nothing");

        foreach (var id in ids) backend.TenantDestroy(id);
        Frame(null); Frame(null);
        var empty = backend.TenantStats();
        Check(empty.Buffers == 0 && empty.Images == 0 && empty.Pipelines == 0 && empty.Bytes == 0, "destroy retires every tenant resource");
        Check(backend.GetStats().ValidationEnabled != 0 && backend.GetStats().ValidationErrors == 0, "tenant session lane raises zero validation errors");
        return checks;
    }
}

static class TenantListExtensions { public static T Also<T>(this T value, Action<T> action) { action(value); return value; } }
