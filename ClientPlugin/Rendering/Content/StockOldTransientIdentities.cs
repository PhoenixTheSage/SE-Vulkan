using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientPlugin.Rendering.Content;

internal sealed class StockOldRuntimeIdentity
{
    public readonly string Name;
    public readonly int ResourceIndex;
    public readonly bool Dynamic;
    public int LayoutIndex {get;internal set;}
    internal readonly int[] Materials;
    internal StockOldRuntimeIdentity(string name,int resource,int parts,bool dynamic){Name=name;ResourceIndex=resource;Dynamic=dynamic;Materials=new int[parts];}
    public int Material(int part)=>Materials[part];
}

internal sealed class StockOldVoxelIdentity
{
    public readonly int ResourceIndex,LayoutIndex;
    internal StockOldVoxelIdentity(int resource,int layout){ResourceIndex=resource;LayoutIndex=layout;}
}

/// <summary>
/// OLD runtime/voxel allocations share the file LOD pool. Runtime creation is
/// always fresh, including duplicate names. Session end retires only currently
/// named dynamic meshes and all voxel cells; static and overwritten allocations
/// remain reserved exactly as in MyMeshes.OnSessionEnd.
/// </summary>
internal sealed class StockOldTransientIdentities
{
    readonly StockOldResourceIndices indices;
    readonly StockOldMaterialIdentities materials;
    readonly StockOldVertexLayouts layouts;
    readonly Dictionary<string,StockOldRuntimeIdentity> names=new(StringComparer.Ordinal);
    readonly HashSet<StockOldRuntimeIdentity> runtime=new();
    readonly Dictionary<StockOldVoxelIdentity,bool> voxels=new();
    public int RuntimeAllocations=>runtime.Count;
    public int VoxelAllocations=>voxels.Count;
    public StockOldTransientIdentities(StockOldResourceIndices indices,StockOldMaterialIdentities materials,StockOldVertexLayouts layouts){this.indices=indices;this.materials=materials;this.layouts=layouts;}
    public StockOldRuntimeIdentity CreateRuntime(string name,int parts,bool dynamic)
    {
        if(name==null)throw new ArgumentNullException(nameof(name));if(parts<0)throw new ArgumentOutOfRangeException(nameof(parts));
        var result=new StockOldRuntimeIdentity(name,indices.Allocate(),parts,dynamic);runtime.Add(result);names[name]=result;return result;
    }
    public StockOldRuntimeIdentity FindRuntime(string name)=>names.TryGetValue(name,out var result)?result:null;
    public void UpdateRuntime(StockOldRuntimeIdentity value,IReadOnlyList<string> sectionMaterials)
    {
        if(!runtime.Contains(value))throw new ArgumentException("Foreign or retired OLD runtime identity.",nameof(value));
        value.LayoutIndex=layouts.File(new StockOldVertexLayout(false,true,false));
        for(int i=0;i<sectionMaterials.Count;i++)value.Materials[i]=materials.GetByName(sectionMaterials[i]);
    }
    public bool RemoveRuntime(StockOldRuntimeIdentity value)
    {
        if(value==null||!runtime.Remove(value))return false;
        indices.Free(value.ResourceIndex);names.Remove(value.Name);return true;
    }
    public StockOldVoxelIdentity CreateVoxel()
    {
        int layout=layouts.Get(new StockOldVertexComponent(VRageRender.MyVertexInputComponentType.VOXEL_POSITION_MAT),new StockOldVertexComponent(VRageRender.MyVertexInputComponentType.VOXEL_NORMAL,1));
        var result=new StockOldVoxelIdentity(indices.Allocate(),layout);voxels.Add(result,true);return result;
    }
    public bool RemoveVoxel(StockOldVoxelIdentity value)
    {
        if(value==null||!voxels.Remove(value))return false;indices.Free(value.ResourceIndex);return true;
    }
    public void SessionEnd()
    {
        foreach(var value in names.Values.ToArray())if(value.Dynamic)RemoveRuntime(value);
        foreach(var value in voxels.Keys.ToArray())RemoveVoxel(value);
        names.Clear();
    }
}
