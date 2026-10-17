using System.Reflection;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRage;
using VRage.FileSystem;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

internal static class Program
{
    private static byte[] referencePixels;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
    private static int Main(string[] args)
    {
        string bin = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(bin,name.Name+".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin,name.Name+".dll")) : null;
        Run(Path.Combine(Path.GetDirectoryName(bin),"Content"), Path.GetFullPath(args[1])); return 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string content, string output)
    {
        Directory.CreateDirectory(output); MyFileSystem.Init(content,Path.Combine(output,"userdata"));
        typeof(MyVRage).GetProperty(nameof(MyVRage.Platform)).SetValue(null,DispatchProxy.Create<IVRagePlatform,CpuPlatform>());
        typeof(MyRenderProxy).GetField("m_render",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,DispatchProxy.Create<IMyRender,CpuMessageRenderer>());
        ParallelTasks.Parallel.Scheduler = new ParallelTasks.FakeTaskScheduler();
        NativeProbe probe = NativeBackend.Probe(); Require(probe.ValidationAvailable != 0,"Khronos validation layer available");
        using var backend = new NativeBackend(null); backend.Create(64,64,0,true,Path.Combine(output,"pipelines"));
        using var cache = new VulkanTextureCache(backend); var sprites = new VulkanSprites(cache) { DrawTimeSeconds = 0 };
        string fontPath=Path.Combine(content,"Fonts","white","FontDataPA.xml");
        var font = new MyFont(fontPath); Require(font.TryGetGlyphInfo('A',out var glyph),"installed FontDataPA contains A");
        string atlas=Path.Combine(font.FontDirectory,font.BitmapInfoById[glyph.nBitmapID].strFilename);
        using var stream=File.OpenRead(atlas); DdsImage dds=DdsImage.Read(stream);
        if(dds.Format==8)
        {
            string decoded=Path.Combine(output,"font-source-rgba.bin");
            var start=new ProcessStartInfo("python") {UseShellExecute=false};
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"decode_font.py"));start.ArgumentList.Add(atlas);start.ArgumentList.Add(decoded);
            using var process=Process.Start(start);process.WaitForExit();Require(process.ExitCode==0,"independent Pillow BC7 reference decoder completed");
            byte[] bytes=File.ReadAllBytes(decoded);Require(BitConverter.ToInt32(bytes,0)==dds.Width && BitConverter.ToInt32(bytes,4)==dds.Height && bytes.Length==8+dds.Width*dds.Height*4,"BC7 reference dimensions agree with production DDS parser");
            referencePixels=bytes.Skip(8).ToArray();
        }
        Require(dds.Format==0 || dds.Format==1 || dds.Format==4 || dds.Format==8,"installed font atlas uses a source-decodable RGBA/BC3/BC7 format");
        (int X,int Y,byte[] Pixel) edge=default;
        for(int y=glyph.pxLocY;y<glyph.pxLocY+glyph.pxHeight && edge.Pixel==null;++y)
            for(int x=glyph.pxLocX;x<glyph.pxLocX+glyph.pxWidth;++x)
            {
                byte[] pixel=Pixel(dds,x,y); if(pixel[3]>48 && pixel[3]<208 && pixel.Take(3).Any(c=>c>0)) {edge=(x,y,pixel);break;}
            }
        Require(edge.Pixel!=null,"selected a real antialiased installed font edge texel");
        Color tint=new Color(102,153,51,128), background=new Color(68,87,112,255);
        sprites.Process(new MyRenderMessageCreateFont {FontId=1,FontPath=fontPath,ColorMask=Color.White});
        sprites.Process(new MyRenderMessageDrawString {FontIndex=1,ScreenCoord=Vector2.Zero,ScreenScale=1,ScreenMaxWidth=100,Text="A",ColorMask=tint,IgnoreBounds=true});
        NativeSprite packet=sprites.MainPackets(64,64).Single(); Require(packet.Flags==2,"production font packet identifies premultiplied atlas input");
        packet.Destination=new float[]{4,4,16,16};packet.Origin=new float[2];packet.Clip=new[]{0,0,-1,0};
        // A constant coordinate at one texel center avoids font resampling and
        // isolates the production texture/tint/blend path against actual bytes.
        packet.Uv=new[]{(edge.X+.5f)/dds.Width,(edge.Y+.5f)/dds.Height,0f,0f};
        Require(backend.BeginFrame(MatrixD.Identity,Vector3.Zero,Vector3.Zero,Vector3.Zero,background),"native sRGB frame begins");
        backend.DrawSprites(new[]{packet});backend.RequestReadback();backend.EndFrame();byte[] pixels=backend.Readback(64,64);
        float[] tintLinear=NativeBackend.ColorValues(tint), backgroundLinear=NativeBackend.ColorValues(background);
        byte[] prepared=(byte[])edge.Pixel.Clone();
        if(dds.IsRawRgba) for(int c=0;c<3;++c) prepared[c]=(byte)(Math.Pow(Math.Pow(prepared[c]/255.0,2.2)*(prepared[3]/255.0),.45454545454545453)*255);
        double alpha=edge.Pixel[3]/255.0*tintLinear[3];int[] expected=new int[3], actual=new int[3];
        for(int c=0;c<3;++c)
        {
            double blended=Linear(prepared[c]/255.0)*tintLinear[c]*tintLinear[3]+backgroundLinear[c]*(1-alpha);
            expected[c]=(int)Math.Round(Encoded(blended)*255);actual[c]=pixels[(10*64+10)*4+c];
            Require(Math.Abs(actual[c]-expected[c])<=2,"installed font edge channel "+c+" follows stock GUI preprocessing/premultiplied tint/linear blend formula");
        }
        PngWriter.Save(Path.Combine(output,"font-edge.png"),64,64,pixels);
        string dxt4Path=Path.Combine(output,"premultiplied-dxt4.dds");
        using(var writer=new BinaryWriter(File.Create(dxt4Path)))
        {
            var header=new uint[32];header[0]=0x20534444;header[1]=124;header[2]=0x1007;header[3]=header[4]=4;header[5]=16;header[7]=1;
            header[19]=32;header[20]=4;header[21]=0x34545844;header[27]=0x1000;
            foreach(uint word in header)writer.Write(word);
            writer.Write(new byte[]{128,128,0,0,0,0,0,0,8,130,8,130,0,0,0,0});
        }
        using var dxt4Source=File.OpenRead(dxt4Path);var dxt4=DdsImage.Read(dxt4Source);
        ulong dxt4Texture=cache.GetSpriteTexture(dxt4Path,out bool dxt4Premultiplied);
        Require(dxt4.Format==4 && dxt4.IsPremultiplied && dxt4Texture!=0 && dxt4Premultiplied,"DXT4 source loads as BC3 and retains premultiplied sprite semantics");
        var dxt4Packet=packet;dxt4Packet.Texture=dxt4Texture;dxt4Packet.Uv=new[]{.125f,.125f,0f,0f};dxt4Packet.Color=new[]{1f,1f,1f,1f};
        Require(backend.BeginFrame(MatrixD.Identity,Vector3.Zero,Vector3.Zero,Vector3.Zero,background),"DXT4 frame begins");
        backend.DrawSprites(new[]{dxt4Packet});backend.RequestReadback();backend.EndFrame();var dxt4Pixels=backend.Readback(64,64);
        var dxt4Texel=Bc3(dxt4,0,0);
        for(int c=0;c<3;c++)
        {
            int dxt4Expected=(int)Math.Round(Encoded(Linear(dxt4Texel[c]/255.0)+backgroundLinear[c]*(1-dxt4Texel[3]/255.0))*255);
            Require(Math.Abs(dxt4Pixels[(10*64+10)*4+c]-dxt4Expected)<=2,"DXT4 compressed channel "+c+" preserves premultiplied alpha without multiplying twice");
        }
        var mipStart=new ProcessStartInfo("python") {UseShellExecute=false};
        string mipDecoded=Path.Combine(output,"font-mip2-rgba.bin");
        mipStart.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"decode_font.py"));mipStart.ArgumentList.Add(atlas);mipStart.ArgumentList.Add(mipDecoded);mipStart.ArgumentList.Add("2");
        using(var mipProcess=Process.Start(mipStart)) {mipProcess.WaitForExit();Require(mipProcess.ExitCode==0,"independent Pillow decodes installed BC7 mip two");}
        byte[] mipBytes=File.ReadAllBytes(mipDecoded);int mipWidth=BitConverter.ToInt32(mipBytes,0),mipHeight=BitConverter.ToInt32(mipBytes,4);
        Require(mipWidth==dds.Width/4 && mipHeight==dds.Height/4 && mipBytes.Length==8+mipWidth*mipHeight*4,"independent mip reference has correct minified extent");
        (int X,int Y,byte[] Pixel) mipEdge=default;
        for(int y=glyph.pxLocY/4;y<(glyph.pxLocY+glyph.pxHeight)/4 && mipEdge.Pixel==null;++y)
            for(int x=glyph.pxLocX/4;x<(glyph.pxLocX+glyph.pxWidth)/4;++x) {
                int i=8+(y*mipWidth+x)*4;byte alphaMip=mipBytes[i+3];
                if(alphaMip>48 && alphaMip<208) {mipEdge=(x,y,mipBytes.Skip(i).Take(4).ToArray());break;}
            }
        Require(mipEdge.Pixel!=null,"selected actual minified font edge coverage");
        packet.Destination=new float[]{4,4,4,4};packet.Uv=new[]{mipEdge.X*4f/dds.Width,mipEdge.Y*4f/dds.Height,16f/dds.Width,16f/dds.Height};
        Require(backend.BeginFrame(MatrixD.Identity,Vector3.Zero,Vector3.Zero,Vector3.Zero,background),"minified font frame begins");
        backend.DrawSprites(new[]{packet});backend.RequestReadback();backend.EndFrame();pixels=backend.Readback(64,64);
        double mipAlpha=mipEdge.Pixel[3]/255.0*tintLinear[3];int[] mipExpected=new int[3],mipActual=new int[3];
        for(int c=0;c<3;++c) {
            double blended=Linear(mipEdge.Pixel[c]/255.0)*tintLinear[c]*tintLinear[3]+backgroundLinear[c]*(1-mipAlpha);
            mipExpected[c]=(int)Math.Round(Encoded(blended)*255);mipActual[c]=pixels[(4*64+4)*4+c];
            Require(Math.Abs(mipActual[c]-mipExpected[c])<=2,"actual minified font channel "+c+" samples uploaded mip two and blends against independent reference");
        }
        CheckKeenLogo(content, output, backend, sprites);
        NativeStats stats=backend.GetStats();Require(stats.ValidationErrors==0,"font and Keen logo GPU passes have zero core/synchronization validation errors");
        PngWriter.Save(Path.Combine(output,"font-minified.png"),64,64,pixels);
        File.WriteAllText(Path.Combine(output,"font-edge.json"),JsonSerializer.Serialize(new {atlas,edge.X,edge.Y,dds.Format,dds.IsRawRgba,source=edge.Pixel,prepared,tint=tintLinear,background=backgroundLinear,expected,actual,minification=new {mip=2,mipEdge.X,mipEdge.Y,source=mipEdge.Pixel,expected=mipExpected,actual=mipActual},stats.ValidationErrors,stats.SubmittedFrames,scope="Actual installed base/mip-two atlas texels plus stock preprocessing/shader formula; full live DX11 UI image comparison remains separate"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Installed-font GPU evidence: "+output);
    }
    private static void CheckKeenLogo(string content, string output, NativeBackend backend, VulkanSprites sprites)
    {
        string path = Path.Combine(content, "Textures", "GUI", "KeenLogo.dds");
        byte[] source = File.ReadAllBytes(path);
        Require(BitConverter.ToUInt32(source, 80) == 0x20, "GPU logo fixture uses the installed P8 main-menu asset");
        int width = checked((int)BitConverter.ToUInt32(source, 16)), height = checked((int)BitConverter.ToUInt32(source, 12));
        backend.Resize((uint)width, (uint)height, 0);
        sprites.BeginFrame();
        sprites.Process(new MyRenderMessageDrawSprite { Texture = path, DestinationRectangle = new RectangleF(0, 0, width, height), Color = Color.White });
        var packet = sprites.MainPackets(width, height).Single();
        Require(packet.Texture != 0 && packet.Flags == 2, "production logo packet selects the premultiplied GUI texture");
        Color background = new Color(35, 60, 85, 255);
        Require(backend.BeginFrame(MatrixD.Identity, Vector3.Zero, Vector3.Zero, Vector3.Zero, background), "Keen logo GPU frame begins");
        backend.DrawSprites(new[] { packet }); backend.RequestReadback(); backend.EndFrame();
        byte[] pixels = backend.Readback(width, height);
        float[] clear = NativeBackend.ColorValues(background);
        int maxError = 0, transparent = 0, visible = 0, edges = 0;
        // Independent reference: directly index the on-disk palette, then apply
        // stock GUI preprocessing and the linear premultiplied blend formula.
        for (int i = 0; i < width * height; ++i)
        {
            int palette = 128 + source[1152 + i] * 4;
            byte alpha = source[palette + 3];
            if (alpha == 0) ++transparent; else if (alpha == 255) ++visible; else ++edges;
            for (int c = 0; c < 3; ++c)
            {
                byte prepared = (byte)(Math.Pow(Math.Pow(source[palette + c] / 255.0, 2.2) * (alpha / 255.0), .45454545454545453) * 255);
                int expected = (int)Math.Round(Encoded(Linear(prepared / 255.0) + clear[c] * (1 - alpha / 255.0)) * 255);
                maxError = Math.Max(maxError, Math.Abs(pixels[i * 4 + c] - expected));
            }
        }
        Require(transparent > 0 && visible > 0 && edges > 0, "logo reference covers transparent, opaque and antialiased pixels");
        Require(maxError <= 2, "all installed logo pixels match palette/alpha/blend reference within two byte values (max " + maxError + ")");
        PngWriter.Save(Path.Combine(output, "keen-logo.png"), width, height, pixels);
        File.WriteAllText(Path.Combine(output, "keen-logo.json"), JsonSerializer.Serialize(new { path, width, height, maxError, transparent, visible, edges, scope = "Installed main-menu asset through production DDS/cache/sprite code and Vulkan readback; live menu layout is separate." }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static double Linear(double c)=>c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);
    private static double Encoded(double c)=>c<=.0031308?c*12.92:1.055*Math.Pow(c,1/2.4)-.055;
    private static byte[] Pixel(DdsImage image,int x,int y)
    {
        if(image.Format==4) return Bc3(image,x,y);
        int i=(y*image.Width+x)*4;byte[] data=image.Format==8?referencePixels:image.Data;
        if(image.Format==8) return new[]{data[i],data[i+1],data[i+2],data[i+3]};
        return image.Format==0?new[]{data[i],data[i+1],data[i+2],data[i+3]}:new[]{data[i+2],data[i+1],data[i],data[i+3]};
    }
    private static byte[] Bc3(DdsImage image,int x,int y)
    {
        int block=((y/4)*((image.Width+3)/4)+x/4)*16,local=(y%4)*4+x%4;
        byte[] alpha=new byte[8];alpha[0]=image.Data[block];alpha[1]=image.Data[block+1];
        for(int i=2;i<8;++i) alpha[i]=alpha[0]>alpha[1]?(byte)(((8-i)*alpha[0]+(i-1)*alpha[1])/7):i<6?(byte)(((6-i)*alpha[0]+(i-1)*alpha[1])/5):i==6?(byte)0:(byte)255;
        ulong ai=0;for(int i=0;i<6;++i)ai|=(ulong)image.Data[block+2+i]<<(i*8);
        byte[][] colors=new byte[4][];for(int i=0;i<2;++i) {ushort packed=BitConverter.ToUInt16(image.Data,block+8+i*2);int r=packed>>11,g=(packed>>5)&63,b=packed&31;colors[i]=new[]{(byte)((r<<3)|(r>>2)),(byte)((g<<2)|(g>>4)),(byte)((b<<3)|(b>>2))};}
        colors[2]=new byte[3];colors[3]=new byte[3];for(int c=0;c<3;++c){colors[2][c]=(byte)((2*colors[0][c]+colors[1][c])/3);colors[3][c]=(byte)((colors[0][c]+2*colors[1][c])/3);}
        int color=(int)((BitConverter.ToUInt32(image.Data,block+12)>>(local*2))&3);return new[]{colors[color][0],colors[color][1],colors[color][2],alpha[(ai>>(local*3))&7]};
    }
}
public class CpuPlatform : DispatchProxy
{
    private readonly IVRageRender render=DispatchProxy.Create<IVRageRender,CpuRender>();
    protected override object Invoke(MethodInfo method,object[] args)=>method.Name=="get_Render"?render:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
}
public class CpuRender : DispatchProxy
{
    protected override object Invoke(MethodInfo method,object[] args)=>method.Name.StartsWith("GetMemoryBudget")?(ulong)(1024*1024*1024):throw new InvalidOperationException("Unexpected platform GPU call "+method.Name);
}
public class CpuMessageRenderer : DispatchProxy
{
    protected override object Invoke(MethodInfo method,object[] args)=>method.Name=="get_Log"?null:throw new InvalidOperationException("Unexpected message renderer call "+method.Name);
}
