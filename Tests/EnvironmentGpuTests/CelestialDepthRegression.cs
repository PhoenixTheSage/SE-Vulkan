using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Interop;

static class CelestialDepthRegression
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Pipeline(IntPtr ctx, byte[] vs, ulong vb, [MarshalAs(UnmanagedType.LPStr)] string ve, byte[] ps, ulong pb, [MarshalAs(UnmanagedType.LPStr)] string pe, out ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Reset(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Image(IntPtr ctx, ulong id, uint w, uint h, uint d, uint format);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Submit(IntPtr ctx, byte[] passes, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Read(IntPtr ctx, ulong id, [Out] byte[] pixels, ulong bytes);

    public static void Run(NativeBackend backend, Func<byte[]> render, Action<bool,string> check, string output)
    {
        check(backend.GetStats().ValidationEnabled!=0,"celestial regression requires active Vulkan validation");
        var ctx=(IntPtr)typeof(NativeBackend).GetField("context",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(backend)!;
        var module=NativeLibrary.Load(NativeBackend.LibraryPath);
        T Entry<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
        var pipeline=Entry<Pipeline>("sevk_guest_create_pipeline");var reset=Entry<Reset>("sevk_guest_reset");
        var image=Entry<Image>("sevk_guest_create_image");var submit=Entry<Submit>("sevk_guest_submit");var read=Entry<Read>("sevk_guest_readback");
        byte[] Compile(string name,string stage,string source)
        {
            string path=Path.Combine(output,name+".hlsl"),binary=Path.Combine(output,name+".spv");File.WriteAllText(path,source);
            var start=new ProcessStartInfo(Path.GetFullPath("Native/build/dxc/bin/x64/dxc.exe")){UseShellExecute=false,CreateNoWindow=true};
            foreach(string arg in new[]{"-spirv","-fspv-target-env=vulkan1.3","-E","main","-T",stage,"-fvk-t-shift","128","0","-Fo",binary,path})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Celestial depth fixture shader failed: "+name);return File.ReadAllBytes(binary);
        }
        try
        {
            var vs=Compile("celestial-depth-vs","vs_6_0","float4 main(uint id:SV_VertexID):SV_Position{float2 p=float2((id<<1)&2,id&2);return float4(p*float2(2,-2)+float2(-1,1),0,1);}");
            var sky=Compile("celestial-depth-sky","ps_6_0","Texture2D<float> Depth:register(t0);float4 main(float4 p:SV_Position):SV_Target{if(Depth.Load(int3(uint2(p.xy),0))>0)discard;return float4(1,0,0,1);}");
            var sample=Compile("celestial-depth-sample","ps_6_0","Texture2D<float> Depth:register(t0);float4 main(float4 p:SV_Position):SV_Target{uint b=asuint(Depth.Load(int3(uint2(p.xy),0)));return float4(b&255,(b>>8)&255,(b>>16)&255,b>>24)/255.0;}");
            check(pipeline(ctx,vs,(ulong)vs.Length,"main",sky,(ulong)sky.Length,"main",out ulong celestial)==0&&celestial!=0,"celestial depth fixture pipeline initializes");
            check(pipeline(ctx,vs,(ulong)vs.Length,"main",sample,(ulong)sample.Length,"main",out ulong sampler)==0&&sampler!=0,"downstream depth consumer initializes");
            const ulong target=987654;check(image(ctx,target,64,64,1,0)==0,"depth evidence target initializes");
            byte[] Pass(ulong id,uint mark,ulong storage,uint flags=0)
            {
                var bytes=new byte[416];void U32(int at,uint value)=>BitConverter.GetBytes(value).CopyTo(bytes,at);void U64(int at,ulong value)=>BitConverter.GetBytes(value).CopyTo(bytes,at);
                U32(0,416);U32(4,1);U32(8,mark);U32(12,5);U32(16,64);U32(20,64);U64(24,id);U64(32,storage);U32(44,flags);U64(304,0xFFFF000000000002ul);return bytes;
            }
            byte[] Capture(bool withSky)
            {
                check(reset(ctx)==0,"guest depth fixture resets");var consumer=Pass(sampler,3,target);
                var passes=withSky?Pass(celestial,1,0xFFFF000000000001ul,1).Concat(consumer).ToArray():consumer;
                check(submit(ctx,passes,withSky?2u:1u)==0,"depth fixture submits");render();
                var pixels=new byte[64*64*4];check(read(ctx,target,pixels,(ulong)pixels.Length)==0,"downstream depth evidence reads back");return pixels;
            }
            var before=Capture(false);var after=Capture(true);
            check(Enumerable.Range(0,4096).Any(i=>BitConverter.ToSingle(before,i*4)>0)&&Enumerable.Range(0,4096).Any(i=>BitConverter.ToSingle(before,i*4)==0),"depth oracle contains foreground geometry and clear sky");
            check(before.SequenceEqual(after),"celestial rendering preserves foreground and sky depth for later passes");
            check(backend.GetStats().ValidationErrors==0,"celestial sky draw has no Vulkan validation errors");
        }
        finally{reset(ctx);NativeLibrary.Free(module);}
    }
}
