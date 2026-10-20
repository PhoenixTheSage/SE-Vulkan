using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRage.FileSystem;

string output=Path.GetFullPath("TestResults/MaterialTextureContract");Directory.CreateDirectory(output);
string texture=Path.Combine(output,"mid-gray-add.dds");
using(var writer=new BinaryWriter(File.Create(texture))) {
    writer.Write(0x20534444u);writer.Write(124u);writer.Write(0x2100fu);
    writer.Write(4u);writer.Write(4u);writer.Write(16u);writer.Write(0u);writer.Write(3u);
    for(int i=0;i<11;i++)writer.Write(0u);
    foreach(uint value in new uint[]{32,0x41,0,32,0xff,0xff00,0xff0000,0xff000000,0x401008,0,0,0,0})writer.Write(value);
    foreach(int size in new[]{4,2,1})writer.Write(Enumerable.Repeat((byte)128,size*size*4).ToArray());
}
MyFileSystem.Init(Path.GetDirectoryName(texture),output);
int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
var backend=new NativeBackend();using var cache=new VulkanTextureCache(backend);
cache.LoadFailed+=(path,error)=>throw new Exception(path,error);
ulong extension=cache.GetWorldExtensions(texture);
ulong alphaMask=cache.GetWorldAlpha(texture);Check(alphaMask!=0&&backend.Uploads[alphaMask].Srgb,"RGBA mesh alpha mask uses stock MakeSrgb view semantics");
// Installed MyGeometryTextureSystem.GetArrayTextureFromFilepath and
// MyFileTexture.Load convert Extension to sRGB, leaving NormalGloss linear.
Check(extension!=0 && backend.Uploads[extension].Srgb,"extension AO/emissive RGB uses the installed DX11 sRGB view contract");
ulong normal=cache.GetWorldNormal(texture);
Check(normal!=extension && !backend.Uploads[normal].Srgb,"normal/gloss uses a distinct linear view of the same bytes");
Check(cache.GetTexture(texture,true)==extension,"color and extension requests reuse the matching sRGB cache entry");
Check(backend.Uploads[extension].Levels.Length==3 && backend.Uploads[extension].Levels.All(m=>m.All(b=>b==128)),"role selection preserves all mip bytes and the linear alpha/mask channel");
double decoded=Math.Pow((128/255d+.055)/1.055,2.4);
Check(Math.Abs(decoded-.2158605001)<1e-9,"reference sRGB mid-gray is 0.21586 rather than linear 0.50196");
ulong neutralExtension=cache.GetWorldExtensions(null),neutralNormal=cache.GetWorldNormal(null);
Check(backend.Uploads[neutralExtension].Levels[0].SequenceEqual(new byte[]{255,0,0,0}) && backend.Uploads[neutralNormal].Levels[0].SequenceEqual(new byte[]{127,127,255,0}),"missing-map AO/emission and normal/gloss match stock generated texture bytes");
string floatPath=Path.Combine(output,"float-cookie.dds");byte[] floatHeader=File.ReadAllBytes(texture).Take(128).ToArray();
Buffer.BlockCopy(BitConverter.GetBytes(4u),0,floatHeader,80,4);Buffer.BlockCopy(BitConverter.GetBytes(112u),0,floatHeader,84,4);Array.Clear(floatHeader,88,20);
using(var writer=new BinaryWriter(File.Create(floatPath))){writer.Write(floatHeader);for(int pixel=0;pixel<21;pixel++){writer.Write((ushort)0x3800);writer.Write((ushort)0x3400);}}
ulong floatCookie=cache.GetTexture(floatPath,true);
Check(floatCookie!=0&&!backend.Uploads[floatCookie].Srgb&&backend.Uploads[floatCookie].Levels.Length==3,"floating-point CUSTOM textures retain a supported linear view and all authored mips");
string cubePath=Path.Combine(output,"cube-"+Guid.NewGuid().ToString("N")+".dds");
using(var writer=new BinaryWriter(File.Create(cubePath))) {
    writer.Write(0x20534444u);writer.Write(124u);writer.Write(0x2100fu);
    writer.Write(4u);writer.Write(4u);writer.Write(16u);writer.Write(0u);writer.Write(3u);
    for(int i=0;i<11;i++)writer.Write(0u);
    foreach(uint value in new uint[]{32,0x41,0,32,0xff,0xff00,0xff0000,0xff000000,0x401008,0xfe00,0,0,0})writer.Write(value);
    for(int face=0;face<6;face++)for(int mip=0;mip<3;mip++)writer.Write(Enumerable.Repeat((byte)(face*24+mip*4),Math.Max(1,4>>mip)*Math.Max(1,4>>mip)*4).ToArray());
}
ulong cube=cache.GetWorldCube(cubePath);
Check(backend.Uploads[cube].Levels.Length==18 && backend.Uploads[cube].Levels.Select((m,i)=>m.All(b=>b==(i/3)*24+(i%3)*4)).All(x=>x),"world cubemap preserves all six authored face mip chains in DDS order");
var cubeFaces=cache.GetCubeTextures(cubePath);
Check(cubeFaces.Length==6 && cubeFaces.All(f=>backend.Uploads[f].Levels.Length==3),"flat cubemap fallback also retains authored mip levels");
Check(cache.GetWorldProbeCube(cubePath)==cube,"missing indirect skybox uses the visible cube for probe capture");
string indirectPath=Path.Combine(output,Path.GetFileNameWithoutExtension(cubePath)+"Indirect.dds");var indirectBytes=File.ReadAllBytes(cubePath);Array.Fill(indirectBytes,(byte)200,128,indirectBytes.Length-128);File.WriteAllBytes(indirectPath,indirectBytes);
ulong indirectCube=cache.GetWorldProbeCube(cubePath);
Check(indirectCube!=cube && backend.Uploads[indirectCube].Levels.All(m=>m.All(b=>b==200)),"existing Indirect DDS supplies its own complete probe cube mip chains");
Check(cache.GetWorldCube(cubePath)==cube,"indirect probe selection preserves the visible skybox resource");
cache.Dispose();Check(backend.Uploads.Count==0,"role-specific uploads release on cache disposal");
Console.WriteLine("PASS "+checks+" actual production texture-cache contract checks; upload observation only, not GPU/live parity.");

namespace ClientPlugin.Rendering.Interop {
    public sealed class NativeBackend {
        public sealed record Upload(bool Srgb,byte[][] Levels);
        public readonly Dictionary<ulong,Upload> Uploads=new();
        public void UploadTexture(ulong id,int w,int h,int format,byte[] data,bool srgb=true)=>UploadTextureMips(id,w,h,format,new[]{data},srgb);
        public void UploadTextureMips(ulong id,int w,int h,int format,byte[][] levels,bool srgb=true)=>Uploads[id]=new Upload(srgb,levels.Select(m=>(byte[])m.Clone()).ToArray());
        public void UploadWorldCube(ulong id,int w,int h,int format,byte[] data,bool srgb)=>UploadTexture(id,w,h,format,data,srgb);
        public void UploadWorldCubeMips(ulong id,int w,int h,int format,byte[][][] faces,bool srgb)=>Uploads[id]=new Upload(srgb,faces.SelectMany(f=>f).Select(m=>(byte[])m.Clone()).ToArray());
        public void RemoveResource(uint kind,ulong id)=>Uploads.Remove(id);
    }
}
