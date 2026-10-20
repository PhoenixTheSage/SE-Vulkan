using System;
using System.Collections.Generic;

namespace ClientPlugin.Rendering.Content;

internal sealed class StockOldLodMaterials
{
    public readonly IReadOnlyList<int> SourceMaterials,SourceProxies,ProxyMaterials;
    public StockOldLodMaterials(int[] materials,int[] proxies,int[] proxyMaterials)
    {SourceMaterials=Array.AsReadOnly(materials);SourceProxies=Array.AsReadOnly(proxies);ProxyMaterials=Array.AsReadOnly(proxyMaterials);}
}

/// <summary>
/// MyMeshes.CreatePartInfos/CreateMergedPartInfos material allocation and part
/// ordering, retained independently of uploaded geometry. One owner spans all
/// OLD file models/LODs so synthetic Merged::N names follow stock allocation.
/// </summary>
internal sealed class StockOldPartIdentities
{
    sealed class Group {public int Material;public List<int> Parts=new();}
    sealed class Groups
    {
        public readonly List<Group> Order=new();readonly Dictionary<int,Group> byMaterial=new();
        public void Append(int material,int part){if(!byMaterial.TryGetValue(material,out var group)){group=new Group{Material=material};byMaterial.Add(material,group);Order.Add(group);}group.Parts.Add(part);}
        public void Assign(Group source){if(byMaterial.TryGetValue(source.Material,out var existing))existing.Parts=source.Parts;else{byMaterial.Add(source.Material,source);Order.Add(source);}}
    }
    readonly StockOldMaterialIdentities materials;
    public int MergedPartCounter {get;private set;}
    public StockOldPartIdentities(StockOldMaterialIdentities materials){this.materials=materials??throw new ArgumentNullException(nameof(materials));}
    public void ResetMergedCounter(int value=0){if(value<0)throw new ArgumentOutOfRangeException(nameof(value));MergedPartCounter=value;}

    public StockOldLodMaterials Register(IReadOnlyList<StockOldMaterialDescription> descriptions,Func<IReadOnlyList<int>,bool> canMerge=null,Action<int> prepareSeparate=null)
    {
        var merge=new Groups();var separate=new Groups();
        var sourceMaterials=new int[descriptions.Count];var sourceProxies=new int[descriptions.Count];
        for(int p=0;p<descriptions.Count;p++){
            sourceProxies[p]=-1;int material=materials.GetOrCreate(descriptions[p].Identity);sourceMaterials[p]=material;
            (descriptions[p].UsesGeometryArray?merge:separate).Append(material,p);
        }
        // Singleton array groups move after existing non-array groups, before
        // failed merges are appended. Preserve dictionary replacement position.
        foreach(var group in merge.Order)if(group.Parts.Count==1)separate.Assign(group);
        var proxyMaterials=new List<int>();
        void Emit(Group group,int material,bool merged){
            if(merged){int proxy=proxyMaterials.Count;proxyMaterials.Add(material);foreach(int p in group.Parts){sourceMaterials[p]=material;sourceProxies[p]=proxy;}}
            else foreach(int p in group.Parts){prepareSeparate?.Invoke(p);sourceProxies[p]=proxyMaterials.Count;proxyMaterials.Add(material);}
        }
        foreach(var group in merge.Order)if(group.Parts.Count>1){
            if(canMerge!=null&&!canMerge(group.Parts.AsReadOnly())){separate.Assign(group);continue;}
            var original=materials.Get(group.Material);
            int material=materials.GetOrCreate(new StockOldMaterialIdentity("Merged::"+MergedPartCounter,original.Color,original.Normal,original.Extensions,original.Alpha,original.Technique));
            MergedPartCounter++;Emit(group,material,true);
        }
        foreach(var group in separate.Order)Emit(group,group.Material,false);
        return new StockOldLodMaterials(sourceMaterials,sourceProxies,proxyMaterials.ToArray());
    }
}
