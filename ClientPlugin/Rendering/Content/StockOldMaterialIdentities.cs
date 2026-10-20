using System;
using System.Collections.Generic;
using VRageRender.Import;

namespace ClientPlugin.Rendering.Content;

/// <summary>
/// OLD material identity after import path/geometry-array conversion. These are
/// representation strings, not Vulkan texture handles or per-actor overrides.
/// Facing, wind, tint and texture-type flags are deliberately absent from RK.
/// </summary>
internal sealed class StockOldMaterialIdentity
{
    public readonly string Name,Color,Normal,Extensions,Alpha;
    public readonly MyMeshDrawTechnique Technique;

    public StockOldMaterialIdentity(string name,string color,string normal,string extensions,string alpha,MyMeshDrawTechnique technique)
    {
        Name=name??string.Empty;Color=color??string.Empty;Normal=normal??string.Empty;
        Extensions=extensions??string.Empty;Alpha=alpha??string.Empty;Technique=technique;
    }

    // MyMeshMaterials1.CalculateRK uses process-local string hashes and a
    // hash-only lookup. Preserve that collision behavior; do not persist RK.
    public int RepresentationKey {
        get {unchecked {
            int h=Color.GetHashCode();
            h=Combine(h,Normal.GetHashCode());h=Combine(h,Extensions.GetHashCode());
            h=Combine(h,Alpha.GetHashCode());h=Combine(h,Technique.GetHashCode());
            return Combine(h,Name.GetHashCode());
        }}
    }
    static int Combine(int a,int b)=>unchecked((17*31+a)*31+b);
}

/// <summary>
/// CPU-only equivalent of the OLD material identity table. No stock material
/// registration, proxy allocation, resource queue or graphics API is touched.
/// Its owner must retain this table across scene/session clears and reset it
/// with the rendering device. Bootstrap ordering is explicit: stock loads
/// built-in meshes before registering its common null/debug materials.
/// </summary>
internal sealed class StockOldMaterialIdentities
{
    readonly Dictionary<int,int> representations=new();
    readonly Dictionary<string,int> names=new(StringComparer.Ordinal);
    readonly List<StockOldMaterialIdentity> entries=new();
    public int Count=>representations.Count;
    public int NullMaterial {get;private set;}
    public int DebugMaterial {get;private set;}

    public int GetOrCreate(StockOldMaterialIdentity material)
    {
        if(material==null)return NullMaterial;
        int key=material.RepresentationKey;
        if(representations.TryGetValue(key,out int existing))return existing;
        int index=representations.Count;
        representations.Add(key,index);entries.Add(material);names[material.Name]=index;
        return index;
    }

    // Stock's name-only dictionary Get returns default(MyMeshMaterialId),
    // index zero, for an unknown name rather than the current null alias.
    public int GetByName(string name)=>names.TryGetValue(name??string.Empty,out int index)?index:0;
    public StockOldMaterialIdentity Get(int index)=>entries[index];

    public void RegisterCommonMaterials()
    {
        NullMaterial=GetOrCreate(new StockOldMaterialIdentity("__NULL_MATERIAL","","","","",MyMeshDrawTechnique.MESH));
        DebugMaterial=GetOrCreate(new StockOldMaterialIdentity("__DEBUG_MATERIAL","","","","",MyMeshDrawTechnique.MESH));
    }

    public void ResetDevice(){representations.Clear();entries.Clear();names.Clear();NullMaterial=DebugMaterial=0;}
}

internal static class StockOldSortKey
{
    static readonly int[] techniqueOrder={0,9,6,10,11,12,13,14,15,16,7,8,5,1,2,17,3,4,20,21,18,19};
    public static int Technique(MyMeshDrawTechnique technique)=>techniqueOrder[(uint)technique];
    public static bool IsDecal(MyMeshDrawTechnique technique)=>(uint)((int)technique-6)<=2;
    // MyRenderableComponent.CreateRenderableProxyForPart / SetBits. Allocated
    // indices intentionally wrap in the packed key; their full IDs still own
    // resources. Physical Vulkan pipelines/batches must not replace these IDs.
    public static ulong Pack(bool decal,int technique,uint vertexFlags,int layout,int lodMesh,int material)=>
        ((decal?1ul:0ul)<<50)|((ulong)((uint)technique&31)<<45)|
        ((ulong)(vertexFlags&0x1ffffff)<<20)|((ulong)((uint)layout&63)<<14)|
        ((ulong)((uint)lodMesh&127)<<7)|((uint)material&127);
}
