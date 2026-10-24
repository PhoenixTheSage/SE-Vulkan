using System.IO.Compression;
using System.Reflection;
using ClientPlugin.Rendering.Interop;

partial class Program
{
    // Audit P4 remainder: display state, low latency, HDR screenshot EXR + paper-white SDR (HdrRender DX11 parity).
    static int DisplayContractRegression(Assembly core,NativeBackend backend,Action frame)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        var display=core.GetType("ClientPlugin.Shaders.DisplayServices",true);
        object Info()=>display.GetProperty("Info",stat)!.GetValue(null)!;T Field<T>(object o,string n)=>(T)o.GetType().GetField(n)!.GetValue(o)!;
        var info=Info();
        Check(info!=null&&Field<int>(info,"PresentMode")==-1&&!Field<bool>(info,"HdrActive")&&!Field<bool>(info,"DisplayKnown"),"offscreen display info reports no swapchain, SDR and no DXGI monitor");
        try{
            display.GetMethod("RequestFor",stat)!.Invoke(null,new object[]{"fixture.display",64});frame();frame();
            Check(Field<bool>(Info(),"LowLatency"),"low-latency request (bit 64) reaches the renderer");
        }finally{display.GetMethod("ReleaseOwner",stat)!.Invoke(null,new object[]{"fixture.display"});}
        frame();Check(!Field<bool>(Info(),"LowLatency"),"releasing the owner restores two frames in flight");
        string refused=null;try{backend.ReadbackHdr(64,64);}catch(Exception e){refused=e.Message;}
        Check(refused!=null&&refused.Contains("FP16"),"HDR readback refuses a non-FP16 target");

        // Screenshot pipeline on a synthetic scRGB image: 0, paper white (200 nits = 2.5), 4x paper white, mid grey.
        var screenshot=typeof(ClientPlugin.Rendering.GuestBridge).Assembly.GetType("ClientPlugin.Rendering.Content.HdrScreenshot",true)!;
        int w=4,h=1;var pixels=new byte[w*h*8];float[] values={0,2.5f,10f,.5f};
        for(int x=0;x<w;x++)for(int c=0;c<4;c++){ushort half=BitConverter.HalfToUInt16Bits((Half)(c==3?1f:values[x]));BitConverter.GetBytes(half).CopyTo(pixels,(x*4+c)*2);}
        var sdr=(byte[])screenshot.GetMethod("ToSdr",stat)!.Invoke(null,new object[]{pixels,w,h,200f})!;
        static float Srgb(float c)=>c<=.0031308f?c*12.92f:1.055f*MathF.Pow(c,1/2.4f)-.055f;
        static byte Expect(float n){float t=n<=.75f?n:.75f+.25f*(n-.75f)/(n-.75f+.5f);return (byte)Math.Round(Math.Clamp(Srgb(t),0,1)*255);}
        Console.WriteLine("DISPLAY sdr="+string.Join(",",sdr));
        Check(sdr[0]==0&&sdr[4]==Expect(1)&&sdr[8]==Expect(4)&&sdr[12]==Expect(.2f)&&sdr[3]==255,"SDR PNG anchors paper white and uses HdrRender's knee/shoulder curve");
        string path=Path.Combine(Path.GetTempPath(),"sevk-hdr-"+Guid.NewGuid().ToString("N")+".png");
        try{
            screenshot.GetMethod("Save",stat)!.Invoke(null,new object[]{path,w,h,pixels,200f});
            string exr=Path.ChangeExtension(path,".exr");Check(File.Exists(path)&&File.Exists(exr),"a PNG screenshot request writes the SDR PNG and keeps the HDR EXR");
            var bytes=File.ReadAllBytes(exr);Check(BitConverter.ToInt32(bytes,0)==20000630&&BitConverter.ToInt32(bytes,4)==2,"EXR has the OpenEXR magic and single-part scanline version");
            // Walk the attribute list (name, type, size, value) to its terminating null; one 8-byte offset follows (one 16-line chunk).
            int at=8;string Text(){int start=at;while(bytes[at]!=0)at++;return System.Text.Encoding.ASCII.GetString(bytes,start,at++-start);}
            while(true){string name=Text();if(name.Length==0)break;Text();int length=BitConverter.ToInt32(bytes,at);at+=4+length;}
            long offset=BitConverter.ToInt64(bytes,at);int y=BitConverter.ToInt32(bytes,(int)offset),size=BitConverter.ToInt32(bytes,(int)offset+4);
            var block=bytes.AsSpan((int)offset+8,size).ToArray();int raw=w*2*3;byte[] decoded;
            if(size==raw)decoded=block;else{using var input=new MemoryStream(block,2,block.Length-6);using var inflate=new DeflateStream(input,CompressionMode.Decompress);using var output=new MemoryStream();inflate.CopyTo(output);var tmp=output.ToArray();
                for(int i=1;i<tmp.Length;i++)tmp[i]=(byte)((tmp[i-1]+tmp[i]-128)&255);decoded=new byte[tmp.Length];int halfLen=(tmp.Length+1)/2;for(int i=0;i<tmp.Length;i++)decoded[i]=(i&1)==0?tmp[i/2]:tmp[halfLen+i/2];}
            bool exact=true;for(int x=0;x<w;x++)for(int c=0;c<3;c++){int channel=2-c;int pos=channel*w*2+x*2;exact&=decoded[pos]==pixels[(x*4+c)*2]&&decoded[pos+1]==pixels[(x*4+c)*2+1];}
            Check(y==0&&exact,"EXR ZIP chunk decodes back to the exact source half-float B,G,R channels");
        }finally{foreach(var f in new[]{path,Path.ChangeExtension(path,".exr")})if(File.Exists(f))File.Delete(f);}
        Check(backend.GetStats().ValidationErrors==0,"display contract checks raise zero validation errors");
        return checks;
    }
}
