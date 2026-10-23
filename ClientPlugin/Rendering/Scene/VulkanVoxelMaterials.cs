using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRage.FileSystem;
using VRageMath;
using VRageRender.Voxels;

namespace ClientPlugin.Rendering.Scene;
public sealed class VulkanVoxelMaterials : IDisposable
{
    private readonly NativeBackend backend;
    private readonly VulkanTextureCache textures;
    private readonly Dictionary<byte,MyRenderVoxelMaterialData> materials=new Dictionary<byte,MyRenderVoxelMaterialData>();
    private readonly Dictionary<MyVoxelMaterialTriple,Group> groups=new Dictionary<MyVoxelMaterialTriple,Group>(MyVoxelMaterialTriple.Comparer);
    private readonly Dictionary<MyVoxelMaterialTriple,Group> resolved=new Dictionary<MyVoxelMaterialTriple,Group>(MyVoxelMaterialTriple.Comparer);
    private sealed class Group { public MyVoxelMaterialTriple Key;public ulong Id; public ulong[] Arrays; public byte[] Materials; public int Size; public long Bytes; public long Used; }
    public bool LookupCacheEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_VOXEL_MATERIAL_CACHE")!="0";
    public int ResolvedCount=>resolved.Count;
    // Separate native resource namespaces; do not collide with model/texture handles.
    private long epoch;private readonly HashSet<MyVoxelMaterialTriple> used=new HashSet<MyVoxelMaterialTriple>(MyVoxelMaterialTriple.Comparer);
    public void BeginFrame(){epoch++;used.Clear();}
    private ulong next=0x7000000000000000;
    private ulong randomTexture;
    private readonly ulong[] defaults=new ulong[3];
    public int Version {get;private set;}
    public bool TryGetFoliage(byte id,out MyRenderFoliageData foliage){if(materials.TryGetValue(id,out var material) && material.Foliage.HasValue){foliage=material.Foliage.Value;return true;}foliage=default;return false;}
    public int Count=>materials.Count;
    public int ResidentGroups=>groups.Count;
    public string AtlasStatus=>string.Join(",",groups.Values.GroupBy(g=>g.Size).OrderBy(g=>g.Key).Select(g=>g.Key+"px:"+g.Count()));
    public long ResidentBytes {get;private set;}
    public VulkanVoxelMaterials(NativeBackend backend,VulkanTextureCache textures)
    {
        this.backend=backend;this.textures=textures;
        if(backend.VoxelMaterialLayout!=null)
        {
            var descriptor=backend.VoxelMaterialLayout.Descriptors.Single(d=>d.Name=="VoxelMaterialsConstants");var entries=descriptor.Members.Single();
            if(descriptor.BlockSize!=30720||entries.ArrayStride!=240)throw new InvalidDataException("Voxel material table layout changed.");
            foreach(var expected in new[]{("SimpleTiling",0u,64u),("StandardTiling",64u,64u),("color_far3",128u,12u),("FullQuality",140u,4u),("slices",144u,96u)})
            {var field=entries.Members.Single(m=>m.Name==expected.Item1);if(field.Offset!=expected.Item2||field.Size!=expected.Item3)throw new InvalidDataException("Voxel material member layout changed: "+expected.Item1);}
        }
    }
    public void Update(MyRenderVoxelMaterialData[] data,bool replace)
    {
        Version++;if(replace){DisposeGroups();materials.Clear();}
        if(data==null)return;
        if(data.Length>128)throw new InvalidDataException("Voxel material table exceeds the stock 128-entry shader contract.");
        for(int position=0;position<data.Length;++position)
        {
            var original=data[position];if(replace)original.Index=(byte)position;
            if(original.Index>=128 || original.TextureSets==null || original.TextureSets.Length!=3)throw new InvalidDataException("Stock voxel material requires index <128 and near/far/far2 texture sets.");
            var own=original;if(original.Foliage.HasValue){var foliage=original.Foliage.Value;foliage.Entries=foliage.Entries==null?null:(MyRenderFoliageData.FoliageEntry[])foliage.Entries.Clone();own.Foliage=foliage;}own.TextureSets=(MyRenderVoxelMaterialData.TextureSet[])original.TextureSets.Clone();
            for(int i=0;i<3;++i)own.TextureSets[i].Check();materials[own.Index]=own;
            foreach(var key in groups.Where(g=>g.Value.Materials.Contains(own.Index)).Select(g=>g.Key).ToArray())Remove(key);
        }
    }
    public void Preload(byte[] ids)
    {
        // A preload is a residency hint, not a visible-frame pin. Larger lists
        // may evict older hints instead of exceeding the atlas working budget.
        if(ids!=null)foreach(byte id in (byte[])ids.Clone()){var key=new MyVoxelMaterialTriple((int)id,255,255);var prior=new HashSet<MyVoxelMaterialTriple>(used,MyVoxelMaterialTriple.Comparer);Get(key);used.IntersectWith(prior);}
    }
    private ulong Source(string path,int role)
    {
        if(!string.IsNullOrWhiteSpace(path)){ulong result=textures.GetTexture(path,role!=1);if(result==0)throw new FileNotFoundException("Voxel DDS missing or unsupported: "+path,path);return result;}
        if(defaults[role]==0)
        {
            // Exact vanilla generated BC7 missing-channel patterns.
            byte[][] patterns={new byte[]{8,252,255,255,63,0,0,0,252,255,255,63,0,0,0,0},new byte[]{192,223,239,247,251,255,1,128,54,51,51,51,51,51,51,51},new byte[]{16,255,3,0,0,0,0,0,0,0,0,0,0,0,0,0}};
            defaults[role]=next++;backend.UploadTexture(defaults[role],4,4,8,patterns[role],role!=1);
        }
        return defaults[role];
    }
    // Prepare is a pure function of the ordered distinct triples and the group
    // set. A frame that resolved every page to an existing group is recorded;
    // while neither input changes, later frames only mark those groups used.
    readonly HashSet<MyVoxelMaterialTriple> distinctSet=new HashSet<MyVoxelMaterialTriple>(MyVoxelMaterialTriple.Comparer);
    readonly List<MyVoxelMaterialTriple> distinctOrder=new List<MyVoxelMaterialTriple>(),preparedOrder=new List<MyVoxelMaterialTriple>(),preparedKeys=new List<MyVoxelMaterialTriple>();
    int groupVersion,preparedVersion=-1;
    public void Prepare(IEnumerable<MyVoxelMaterialTriple> triples)
    {
        distinctSet.Clear();distinctOrder.Clear();foreach(var triple in triples)if(distinctSet.Add(triple))distinctOrder.Add(triple);
        if(VulkanVoxels.FastPathEnabled&&preparedVersion==groupVersion&&distinctOrder.Count==preparedOrder.Count){bool same=true;for(int i=0;i<distinctOrder.Count&&same;i++)same=MyVoxelMaterialTriple.Comparer.Equals(distinctOrder[i],preparedOrder[i]);
            if(same){foreach(var key in preparedKeys){groups[key].Used=epoch;used.Add(key);}return;}}
        int version=groupVersion;preparedKeys.Clear();
        var pages=new List<(HashSet<byte> Ids,MyVoxelMaterialTriple Key)>();
        foreach(var triple in distinctOrder){
            byte[] ids=new[]{triple.I0,triple.I1,triple.I2}.Where(i=>i!=255).Distinct().ToArray();if(ids.Length==0)continue;
            int page=pages.FindIndex(p=>p.Ids.Union(ids).Count()<=42);
            if(page<0)pages.Add((new HashSet<byte>(ids),triple));else pages[page].Ids.UnionWith(ids);
        }
        int size=512,total=pages.Sum(p=>p.Ids.Count);while(size>64 && total*6L*size*size*4*4/3*3>512L*1024*1024)size/=2;
        if(total*6L*size*size*4*4/3*3>512L*1024*1024)throw new InvalidOperationException("Visible voxel material pages exceed the bounded atlas budget even at the minimum tier.");
        foreach(var page in pages){byte[] ids=page.Ids.OrderBy(i=>i).ToArray();var cached=groups.FirstOrDefault(p=>p.Value.Size==size && p.Value.Materials.OrderBy(i=>i).SequenceEqual(ids));
            if(cached.Value!=null){cached.Value.Used=epoch;used.Add(cached.Key);preparedKeys.Add(cached.Key);continue;}
            if(groups.ContainsKey(page.Key))Remove(page.Key);Create(ids,page.Key,size);used.Add(page.Key);
        }
        if(groupVersion==version){preparedVersion=version;preparedOrder.Clear();preparedOrder.AddRange(distinctOrder);}else preparedVersion=-1;
    }
    public ulong Get(MyVoxelMaterialTriple triple)
    {
        if(LookupCacheEnabled && resolved.TryGetValue(triple,out var cached)){
            used.Add(cached.Key);cached.Used=epoch;return cached.Id;
        }
        byte[] ids=new[]{triple.I0,triple.I1,triple.I2}.Where(i=>i!=255).Distinct().ToArray();if(ids.Length==0)throw new InvalidDataException("Empty voxel material triple.");
        foreach(var pair in groups)if(ids.All(i=>pair.Value.Materials.Contains(i))){used.Add(pair.Key);pair.Value.Used=epoch;if(LookupCacheEnabled)resolved[triple]=pair.Value;return pair.Value.Id;}
        used.Add(triple);ulong id=Create(ids,triple,512);if(LookupCacheEnabled)resolved[triple]=groups[triple];return id;
    }
    private ulong Create(byte[] ids,MyVoxelMaterialTriple triple,int size)
    {
        if(ids.Length==0)throw new InvalidDataException("Empty voxel material triple.");
        var constants=new byte[30720];var slices=new int[ids.Length,3,6];var group=new Group{Key=triple,Id=next++,Arrays=new ulong[3],Materials=ids,Size=size};long bytes=0;
        long required=ids.Length*6L*size*size*4*4/3*3;while(ResidentBytes+required>512L*1024*1024){var evict=groups.Where(g=>!used.Contains(g.Key)).OrderBy(g=>g.Value.Used).FirstOrDefault();if(evict.Value==null)throw new InvalidOperationException("Visible voxel atlas budget exhausted.");Remove(evict.Key);}
        try
        {
            for(int role=0;role<3;++role)
            {
                var sources=new List<ulong>();
                for(int n=0;n<ids.Length;++n)
                {
                    if(!materials.TryGetValue(ids[n],out var material))throw new InvalidDataException("Undefined voxel material "+ids[n]);
                    for(int level=0;level<3;++level)
                    {
                        var set=material.TextureSets[level];string[] paths=role==0?new[]{set.ColorMetalXZnY,set.ColorMetalY}:role==1?new[]{set.NormalGlossXZnY,set.NormalGlossY}:new[]{set.ExtXZnY,set.ExtY};
                        for(int side=0;side<2;++side){slices[n,level,role*2+side]=sources.Count;sources.Add(Source(paths[side],role));}
                    }
                }
                group.Arrays[role]=next++;backend.BuildVoxelArray(group.Arrays[role],size,role!=1,sources.ToArray());bytes+=sources.Count*(long)size*size*4*4/3;
            }
            for(int n=0;n<ids.Length;++n)
            {
                var material=materials[ids[n]];int at=ids[n]*240;
                Tiling(constants,at,material.SimpleTilingSetup);Tiling(constants,at+64,material.StandardTilingSetup);Put(constants,at+128,material.Far3Color.X,material.Far3Color.Y,material.Far3Color.Z);UInt(constants,at+140,1);
                for(int level=0;level<3;++level)for(int j=0;j<6;++j)UInt(constants,at+144+level*32+j*4,(uint)slices[n,level,j]);
            }
            while(ResidentBytes+bytes>512L*1024*1024){var evict=groups.Where(g=>!used.Contains(g.Key)).OrderBy(g=>g.Value.Used).FirstOrDefault();if(evict.Value==null)throw new InvalidOperationException("Visible voxel texture working set exceeds 512 MiB atlas budget.");Remove(evict.Key);}
            if(randomTexture==0)
            {
                // Vanilla uses a generated 64x64 single-channel uniform random texture.
                // A fixed seed makes the Vulkan material noise stable across captures.
                var noise=new byte[64*64];new Random(43191).NextBytes(noise);var rgba=new byte[noise.Length*4];for(int i=0;i<noise.Length;++i){rgba[i*4]=noise[i];rgba[i*4+3]=255;}
                randomTexture=next++;backend.UploadTexture(randomTexture,64,64,0,rgba,false);
            }
            backend.SetVoxelMaterial(group.Id,constants,group.Arrays,randomTexture);group.Bytes=bytes;group.Used=epoch;groups.Add(triple,group);groupVersion++;resolved.Clear();ResidentBytes+=bytes;return group.Id;
        }
        catch{backend.RemoveResource(6,group.Id);foreach(ulong id in group.Arrays)if(id!=0)backend.RemoveResource(5,id);throw;}
    }
    private static void UInt(byte[] b,int at,uint x)=>Buffer.BlockCopy(BitConverter.GetBytes(x),0,b,at,4);
    private static void Put(byte[] b,int at,params float[] values){foreach(float value in values){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);at+=4;}}
    private static void Tiling(byte[] b,int at,MyRenderVoxelMaterialData.TilingSetup t)
    {
        Put(b,at,t.DistanceAndScale.X,t.DistanceAndScale.Y,t.DistanceAndScale.Z,t.DistanceAndScale.W,t.DistanceAndScaleFar.X,t.DistanceAndScaleFar.Y,t.DistanceAndScaleFar.Z,t.TilingScale==0?1:1/t.TilingScale,t.DistanceAndScaleFar2.X,t.DistanceAndScaleFar2.Y,t.DistanceAndScaleFar2.Z,0,t.DistanceAndScaleFar3.X,t.DistanceAndScaleFar3.Y,t.DistanceAndScaleFar3.Z,t.ExtensionDetailScale);
    }
    public static byte[] Object(ShaderMetadata layout,MatrixD matrix,Vector3D offset,int lod,float dither,float? spherizeRadius=null,Vector3D spherizeCenter=default)
        =>VoxelObjectConstants.Enabled?VoxelObjectConstants.Write(layout,matrix,offset,lod,dither,spherizeRadius,spherizeCenter):ObjectReflected(layout,matrix,offset,lod,dither,spherizeRadius,spherizeCenter);
    public static byte[] ObjectReflected(ShaderMetadata layout,MatrixD matrix,Vector3D offset,int lod,float dither,float? spherizeRadius=null,Vector3D spherizeCenter=default)
    {
        var result=new ReflectedConstantBuffer(layout,"Object");float scale=(float)(1<<lod);
        result.Set("object_.voxelLodSize",lod);result.Set("object_.voxel_offset",(float)offset.X,(float)offset.Y,(float)offset.Z);result.Set("object_.voxel_scale",scale,scale,scale);
        result.Set("object_.spherizeTo",spherizeRadius??0);result.Set("object_.spherizeStart",spherizeRadius.HasValue?spherizeRadius.Value*1.1f:0);result.Set("object_.spherizeCenter",(float)spherizeCenter.X,(float)spherizeCenter.Y,(float)spherizeCenter.Z);
        result.Set("object_.matrix_row0",(float)matrix.M11,(float)matrix.M21,(float)matrix.M31,(float)matrix.M41);
        result.Set("object_.matrix_row1",(float)matrix.M12,(float)matrix.M22,(float)matrix.M32,(float)matrix.M42);
        result.Set("object_.matrix_row2",(float)matrix.M13,(float)matrix.M23,(float)matrix.M33,(float)matrix.M43);
        result.Set("object_.color_mul",1,1,1);result.Set("object_.emissive",1);result.Set("object_.custom_alpha",dither,0);return result.Bytes;
    }
    private void Remove(MyVoxelMaterialTriple key)
    {var group=groups[key];backend.RemoveResource(6,group.Id);foreach(ulong id in group.Arrays)backend.RemoveResource(5,id);ResidentBytes-=group.Bytes;groups.Remove(key);groupVersion++;resolved.Clear();}
    public void ClearResident(){DisposeGroups();used.Clear();if(randomTexture!=0){backend.RemoveResource(0,randomTexture);randomTexture=0;}for(int i=0;i<3;++i)if(defaults[i]!=0){backend.RemoveResource(0,defaults[i]);defaults[i]=0;}}
    private void DisposeGroups(){foreach(var key in groups.Keys.ToArray())Remove(key);ResidentBytes=0;}
    public void Dispose(){ClearResident();materials.Clear();}
}
