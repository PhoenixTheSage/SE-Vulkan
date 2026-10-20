using System;
using System.Collections.Generic;

namespace ClientPlugin.Rendering.Content;

internal sealed class StockOldFileLodIdentity
{
    public readonly string AssetFile;
    public readonly int ResourceIndex,LayoutIndex;
    public readonly float Distance;
    public readonly StockOldVertexLayout Layout;
    public readonly StockOldLodMaterials Materials;
    public StockOldFileLodIdentity(string file,int resource,int layoutIndex,float distance,StockOldVertexLayout layout,StockOldLodMaterials materials)
    {AssetFile=file;ResourceIndex=resource;LayoutIndex=layoutIndex;Distance=distance;Layout=layout;Materials=materials;}
}

internal sealed class StockOldFileIdentity
{
    public readonly string Name;public readonly IReadOnlyList<StockOldFileLodIdentity> Lods;
    public StockOldFileIdentity(string name,StockOldFileLodIdentity[] lods){Name=name;Lods=Array.AsReadOnly(lods);}
}

/// <summary>
/// Retained OLD file-name ownership. Eager LOD/material registration follows
/// LoadMesh; a scene clear must not discard this table. The supplied LOD-index
/// pool and part/material owners are shared with the other OLD resource paths.
/// </summary>
internal sealed class StockOldFileIdentities
{
    readonly Dictionary<string,StockOldFileIdentity> files=new(StringComparer.Ordinal);
    readonly StockOldResourceIndices indices;readonly StockOldPartIdentities parts;
    readonly Func<string,bool,StockOldFileLodSource> read;
    readonly Func<StockOldVertexLayout,int> layout;
    public int Count=>files.Count;
    public IEnumerable<StockOldFileIdentity> Models=>files.Values;
    public bool Contains(string name)=>files.ContainsKey(name);
    public StockOldFileIdentities(StockOldResourceIndices indices,StockOldPartIdentities parts,Func<StockOldVertexLayout,int> layout,Func<string,bool,StockOldFileLodSource> read)
    {this.indices=indices;this.parts=parts;this.layout=layout;this.read=read;}
    StockOldFileLodIdentity Register(StockOldFileLodSource source,float distance)
    {
        var materials=parts.Register(source.Materials,source.CanMerge,source.PrepareSeparate);
        var signature=source.Layout;int layoutIndex=layout(signature),resource=indices.Allocate();
        return new StockOldFileLodIdentity(source.AssetFile,resource,layoutIndex,distance,signature,materials);
    }
    public StockOldFileIdentity Get(string name,bool geometryArrays)
    {
        if(files.TryGetValue(name,out var existing))return existing;
        var source=read(name,geometryArrays)??read("Models/Debug/Error.mwm",geometryArrays)??throw new InvalidOperationException("OLD identity import has no drawable error model.");
        var levels=new List<StockOldFileLodIdentity>{Register(source,0)};
        foreach(var descriptor in source.Lods){string file=descriptor.GetModelAbsoluteFilePath(name);if(file==null)continue;var lod=read(file,geometryArrays);if(lod!=null)levels.Add(Register(lod,descriptor.Distance));}
        var result=new StockOldFileIdentity(name,levels.ToArray());files.Add(name,result);return result;
    }
    public bool Remove(string name)
    {
        if(!files.TryGetValue(name,out var model))return false;
        foreach(var lod in model.Lods)indices.Free(lod.ResourceIndex);files.Remove(name);return true;
    }
    // Removing a same-name runtime erases the file alias too, without retiring
    // the distinct file mesh still referenced by its actors in stock.
    public void Forget(string name)=>files.Remove(name);
}
