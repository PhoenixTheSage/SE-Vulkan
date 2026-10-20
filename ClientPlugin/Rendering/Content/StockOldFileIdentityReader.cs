using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRage.FileSystem;
using VRage.Import;
using VRageMath;
using VRageRender.Import;

namespace ClientPlugin.Rendering.Content;

internal sealed class StockOldFileLodSource
{
    public readonly string AssetFile;
    public readonly MyLODDescriptor[] Lods;
    public readonly StockOldMaterialDescription[] Materials;
    readonly int[][] indices;readonly Vector4I[] bones;readonly Vector4[] weights;
    readonly bool streams,arrays;bool animated;
    public StockOldVertexLayout Layout=>new(animated,streams,arrays);
    public StockOldFileLodSource(string file,MyLODDescriptor[] lods,StockOldMaterialDescription[] materials,int[][] indices,Vector4I[] bones,Vector4[] weights,bool animated,bool streams,bool arrays)
    {AssetFile=file;Lods=lods;Materials=materials;this.indices=indices;this.bones=bones;this.weights=weights;this.animated=animated;this.streams=streams;this.arrays=arrays;}

    bool PrepareAnimations(IEnumerable<int> source)
    {
        if(!animated)return true;var vertices=source.ToArray();var used=new HashSet<int>();
        foreach(int vertex in vertices){var b=bones[vertex];var w=weights[vertex];if(w.X>0)used.Add(b.X);if(w.Y>0)used.Add(b.Y);if(w.Z>0)used.Add(b.Z);if(w.W>0)used.Add(b.W);}
        if(used.Count>60)return false;if(used.Count==0||used.Max()<60)return true;
        var order=used.OrderBy(v=>v).ToArray();var remap=new Dictionary<int,int>();for(int i=0;i<order.Length;i++)remap.Add(order[i],i);
        // Stock remaps the shared CPU bone stream once per touched vertex;
        // later part groups observe those edits, so process in proxy order.
        foreach(int vertex in vertices.Distinct()){var b=bones[vertex];var w=weights[vertex];if(w.X>0)b.X=remap[b.X];if(w.Y>0)b.Y=remap[b.Y];if(w.Z>0)b.Z=remap[b.Z];if(w.W>0)b.W=remap[b.W];bones[vertex]=b;}
        return true;
    }
    public bool CanMerge(IReadOnlyList<int> parts)=>PrepareAnimations(parts.SelectMany(p=>indices[p]));
    public void PrepareSeparate(int part){if(!PrepareAnimations(indices[part]))animated=false;}
}

/// <summary>Metadata-only OLD MWM import. No geometry upload or stock manager.</summary>
internal sealed class StockOldFileIdentityReader
{
    static readonly string[] tags={"Vertices","Normals","Tangents","Binormals","TexCoords0","BlendIndices","BlendWeights","MeshParts","LODs","GeometryDataAsset"};
    readonly StockOldMaterialDescriptions descriptions;
    public StockOldFileIdentityReader(StockOldMaterialDescriptions descriptions){this.descriptions=descriptions;}
    public StockOldFileLodSource Read(string file,bool geometryArrays)
    {
        string path=Path.IsPathRooted(file)?file:Path.Combine(MyFileSystem.ContentPath,file);if(!MyFileSystem.FileExists(path))return null;
        var importer=new MyModelImporter();
        try{
            importer.ImportData(path,tags);var data=importer.GetTagData();
            if(data.TryGetValue("GeometryDataAsset",out object asset)){
                string geometry=new MyLODDescriptor{Model=(string)asset}.GetModelAbsoluteFilePath(path);importer.ImportData(geometry,tags);data=importer.GetTagData();
            }
            var positions=(Array)data["Vertices"];int count=positions.Length;if(count==0)return null;
            int Length(string name)=>((Array)data[name]).Length;
            bool streams=Length("Normals")!=0&&Length("Normals")==count&&Length("TexCoords0")!=0&&Length("TexCoords0")==count&&Length("Tangents")!=0&&Length("Tangents")==count&&Length("Binormals")!=0&&Length("Binormals")==count;
            var bones=(Vector4I[])data["BlendIndices"];var weights=(Vector4[])data["BlendWeights"];bool animated=bones.Length!=0&&weights.Length!=0&&bones.Length==count&&weights.Length==count;
            var parts=(List<MyMeshPartInfo>)data["MeshParts"];var materials=descriptions.DescribeParts(file,parts.Select(p=>p.m_MaterialDesc).ToArray(),geometryArrays);
            var lods=data.TryGetValue("LODs",out object levels)?(MyLODDescriptor[])levels:Array.Empty<MyLODDescriptor>();
            return new StockOldFileLodSource(file,(MyLODDescriptor[])lods.Clone(),materials,parts.Select(p=>p.m_indices.ToArray()).ToArray(),animated?(Vector4I[])bones.Clone():Array.Empty<Vector4I>(),animated?(Vector4[])weights.Clone():Array.Empty<Vector4>(),animated,streams,geometryArrays);
        }finally{importer.Clear();}
    }
}
