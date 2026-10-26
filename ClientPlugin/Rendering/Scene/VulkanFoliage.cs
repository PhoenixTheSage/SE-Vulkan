using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRage;
using VRage.Import;
using VRageMath;
using VRageRender;
using VRageRender.Voxels;

namespace ClientPlugin.Rendering.Scene;
/// <summary>Persistent point streams; stock GS expands/shades grass and pebbles.</summary>
public sealed class VulkanFoliage : IDisposable
{
    private sealed class Material {public int Version;public ulong Color,Normal;public uint Kind;}
    public sealed class Stream {public ulong Mesh;public byte Material;}
    private readonly NativeBackend backend;
    private readonly VulkanTextureCache textures;
    private readonly VulkanVoxelMaterials materials;
    private readonly Dictionary<byte,Material> resident=new Dictionary<byte,Material>();
    private readonly List<NativeFoliageDraw> packets=new List<NativeFoliageDraw>();
    private readonly byte[] table=new byte[4096];
    private ulong next=0x7100000000000000;
    public float Distance=250,Density=1;
    public long GeneratedPoints {get;private set;}
    public VulkanFoliage(NativeBackend backend,VulkanTextureCache textures,VulkanVoxelMaterials materials){this.backend=backend;this.textures=textures;this.materials=materials;}
    public void BeginFrame()=>packets.Clear();
    // Set while voxel emission runs on a worker; see VulkanVoxelMaterials.OffOwner.
    internal bool OffOwner;
    public void EndFrame()=>backend.QueueFoliage(packets,table);
    private Material Get(byte id)
    {
        if(resident.TryGetValue(id,out var cached)&&cached.Version==materials.Version)return cached;
        if(OffOwner)throw new VulkanVoxels.OwnerThreadRequired();
        if(cached!=null){backend.RemoveResource(5,cached.Color);backend.RemoveResource(5,cached.Normal);resident.Remove(id);}
        if(!materials.TryGetFoliage(id,out var definition) || definition.Entries==null || definition.Entries.Length==0)throw new InvalidDataException("Foliage definition missing for material "+id);
        if(definition.Entries.Length>256)throw new InvalidDataException("Foliage texture count exceeds atlas limit.");
        var material=new Material{Version=materials.Version,Color=next++,Normal=next++,Kind=definition.Type==MyFoliageType.Grass?0u:1u};
        try{ulong[] cm=definition.Entries.Select(e=>textures.GetTexture(e.ColorAlphaTexture,true)).ToArray(),ng=definition.Entries.Select(e=>textures.GetTexture(e.NormalGlossTexture,false)).ToArray();if(cm.Any(t=>t==0)||ng.Any(t=>t==0))throw new FileNotFoundException("Foliage texture unavailable for material "+id);
            backend.BuildVoxelArray(material.Color,512,true,cm);backend.BuildVoxelArray(material.Normal,512,false,ng);
            var first=definition.Entries[0];int at=id*16;foreach(float f in new[]{first.Size.X,first.Size.Y,first.SizeVariation}){Buffer.BlockCopy(BitConverter.GetBytes(f),0,table,at,4);at+=4;}Buffer.BlockCopy(BitConverter.GetBytes((uint)definition.Entries.Length),0,table,at,4);resident.Add(id,material);return material;
        }catch{backend.RemoveResource(5,material.Color);backend.RemoveResource(5,material.Normal);throw;}
    }
    public List<Stream> Generate(MyVertexFormatVoxelSingleData[] vertices,uint[] indices,MyVoxelMaterialTriple triple,int lod,MatrixD actor)
    {
        var streams=new List<Stream>();
        try{byte[] ids={triple.I0,triple.I1,triple.I2};for(int slot=0;slot<3;++slot){byte id=ids[slot];if(id==255 || !materials.TryGetFoliage(id,out var definition) || !(definition.Density>0) || (definition.Entries==null || definition.Entries.Length==0))continue;
            var points=GeneratePoints(vertices,indices,slot,id,definition.Density*Density/(1<<lod),1<<lod,actor);
            if(points.Length==0)continue;var stream=new Stream{Mesh=next++,Material=id};backend.UploadFoliage(stream.Mesh,points);streams.Add(stream);GeneratedPoints+=points.Length;
        }return streams;}catch{Retire(streams);throw;}
    }
    public void Draw(List<Stream> streams,MatrixD matrix,float dither)
    {
        foreach(var stream in streams){var material=Get(stream.Material);packets.Add(new NativeFoliageDraw{Size=168,Kind=material.Kind,Mesh=stream.Mesh,Color=material.Color,Normal=material.Normal,Constants=Shaders.WorldConstants.Object(backend.EnvironmentLayouts[4],matrix,Vector3.One,Vector3.Zero,1,dither)});}
    }
    public void Retire(List<Stream> streams){if(streams==null)return;foreach(var stream in streams)backend.RemoveResource(3,stream.Mesh);streams.Clear();}
    public void Clear(){packets.Clear();foreach(var m in resident.Values){backend.RemoveResource(5,m.Color);backend.RemoveResource(5,m.Normal);}resident.Clear();Array.Clear(table,0,table.Length);}
    public void Dispose()=>Clear();
    // Source: FoliageStreaming.hlsl. Copy triangle distribution and Park-Miller
    // sequence once per replacement, avoiding Vulkan transform-feedback extensions.
    public static NativeFoliagePoint[] GeneratePoints(MyVertexFormatVoxelSingleData[] vertices,uint[] indices,int slot,byte material,float density,float scale,MatrixD rotation)
    {
        var result=new List<NativeFoliagePoint>();
        unchecked{for(int i=0;i<indices.Length;i+=3){var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];Vector3 p0=a.Position*scale,p1=b.Position*scale,p2=c.Position*scale;
            float weight=(((a.Material.PackedValue>>24)==slot?1:0)+((b.Material.PackedValue>>24)==slot?1:0)+((c.Material.PackedValue>>24)==slot?1:0))/3f;
            if(weight==0)continue;uint Seed(float v)=> (uint)(v*10+.5f);uint seed=Seed(p0.X)^(Seed(p1.X)*10)^(Seed(p2.X)*1000)^Seed(p0.Y)^(Seed(p1.Y)*10)^(Seed(p2.Y)*1000)^Seed(p0.Z)^(Seed(p1.Z)*10)^(Seed(p2.Z)*1000)^material;
            var random=new StockRandom(0x12345678^seed);float spawn=Vector3.Cross(p1-p0,p2-p0).Length()*.5f*density*weight;int count=Math.Min(24,(int)spawn+(random.Float()<spawn-(float)Math.Floor(spawn)?1:0));
            Vector3 normal=Vector3.Normalize(VF_Packer.UnpackNormal(a.PackedNormal)+VF_Packer.UnpackNormal(b.PackedNormal)+VF_Packer.UnpackNormal(c.PackedNormal));normal=Vector3.Normalize(Vector3.TransformNormal(normal,(Matrix)rotation));uint packed=(uint)((normal.X*.5f+.5f)*255)|(((uint)((normal.Z*.5f+.5f)*127)|(normal.Y>0?128u:0))<<8);
            for(int point=0;point<count;++point){float r1=random.Float(),r2=random.Float(),root=(float)Math.Sqrt(r1);uint r=random.Int();Vector3 p=(1-root)*p0+(1-r2)*root*p1+r2*root*p2;result.Add(new NativeFoliagePoint{X=p.X,Y=p.Y,Z=p.Z,NormalSeedMaterial=packed|((r&255)<<16)|((uint)material<<24)});if(result.Count>4000000)throw new InvalidDataException("Foliage cell exceeds four million point limit.");}
        }}return result.ToArray();
    }
    private struct StockRandom
    {
        private int seed;
        public StockRandom(uint initial){seed=unchecked((int)initial);Cycle();}
        private void Cycle(){unchecked{seed^=123459876;int k=(int)((uint)seed/127773u);seed=(int)(16807*((uint)seed-(uint)k*127773u)-2836*(uint)k);if(seed<0)seed+=2147483647;seed^=123459876;}}
        public float Float(){Cycle();return (1f/2147483647)*seed;}
        public uint Int(){Cycle();return unchecked((uint)seed);}
    }
}
