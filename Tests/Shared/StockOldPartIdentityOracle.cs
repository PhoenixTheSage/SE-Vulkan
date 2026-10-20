using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClientPlugin.Rendering.Content;
using VRage.Utils;
using VRageRender;
using VRageRender.Import;
using VRageMath.PackedVector;

namespace FixtureAssets;

// Run on the initialized DX11 render thread after the held fixture capture.
// Never clear/reseed the real tables or invoke this from a Vulkan/CPU process.
internal static class StockOldPartIdentityOracle
{
    public static void Run(Assembly stock,Action<bool,string> check)
    {
        const BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        Type materials=stock.GetType("VRageRender.MyMeshMaterials1",true),infoType=stock.GetType("VRageRender.MyMeshMaterialInfo",true),proxies=stock.GetType("VRageRender.MyMaterials1",true),meshes=stock.GetType("VRageRender.MyMeshes",true);
        var register=materials.GetMethods(all).Single(m=>m.Name=="GetMaterialId"&&m.GetParameters()[0].ParameterType.IsByRef);
        var build=meshes.GetMethod("CreateMergedPartInfos",all);var counter=meshes.GetField("m_mergedPartCounter",all);
        Type idType=register.ReturnType,groupsType=typeof(Dictionary<,>).MakeGenericType(idType,typeof(List<int>));
        StockOldMaterialIdentities identities=null;StockOldPartIdentities owner=null;string prefix=null;
        int Index(object value)=>(int)idType.GetField("Index",all).GetValue(value);
        object Pool()=>materials.GetField("MaterialsPool",all).GetValue(null);
        int Count(object pool)=>(int)pool.GetType().GetProperty("Size").GetValue(pool);
        (int,int,int) Footprint()=> (Count(Pool()),Count(proxies.GetField("ProxyPool",all).GetValue(null)),((IList)materials.GetField("MaterialQueryResourcesTable",all).GetValue(null)).Count);
        StockOldMaterialIdentity Identity(object value){Type t=value.GetType();string Field(string name)=>(string)t.GetField(name,all).GetValue(value);
            return new StockOldMaterialIdentity(t.GetField("Name",all).GetValue(value).ToString(),Field("ColorMetal_Texture"),Field("NormalGloss_Texture"),Field("Extensions_Texture"),Field("Alphamask_Texture"),(MyMeshDrawTechnique)t.GetField("Technique",all).GetValue(value));}
        void Snapshot(){identities=new StockOldMaterialIdentities();object pool=Pool();var table=(Array)pool.GetType().GetProperty("Data").GetValue(pool);int count=Count(pool);
            for(int i=0;i<count;i++)check(identities.GetOrCreate(Identity(table.GetValue(i)))==i,"snapshot preserves real material index "+i);
            identities.RegisterCommonMaterials();owner=new StockOldPartIdentities(identities);owner.ResetMergedCounter((int)counter.GetValue(null));prefix="PartIdentityOracle-"+Guid.NewGuid().ToString("N")+"-";
        }
        void Case(string label,string[] names,bool[] arrays,int[][] merged,int[][] separate,Func<IReadOnlyList<int>,bool> canMerge=null){
            var descriptions=names.Select((name,p)=>new StockOldMaterialDescription(new StockOldMaterialIdentity(prefix+name,"","","","",MyMeshDrawTechnique.MESH),arrays[p])).ToArray();
            var before=Footprint();var actual=owner.Register(descriptions,canMerge);check(Footprint()==before,"production part registration leaves real tables untouched: "+label);
            var referenceIds=new object[names.Length];var sourceParts=new List<MyMeshPartInfo>();
            for(int p=0;p<names.Length;p++){
                var description=descriptions[p].Identity;object info=Activator.CreateInstance(infoType);
                infoType.GetField("Name",all).SetValue(info,MyStringId.GetOrCompute(description.Name));infoType.GetField("Technique",all).SetValue(info,description.Technique);
                foreach(string field in new[]{"ColorMetal_Texture","NormalGloss_Texture","Extensions_Texture","Alphamask_Texture"})infoType.GetField(field,all).SetValue(info,string.Empty);
                referenceIds[p]=register.Invoke(null,new object[]{info,null});
                sourceParts.Add(new MyMeshPartInfo{m_MaterialDesc=new MyMaterialDescriptor(prefix+names[p]){TechniqueEnum=MyMeshDrawTechnique.MESH},Technique=MyMeshDrawTechnique.MESH,m_indices=new List<int>{p*3,p*3+1,p*3+2}});
            }
            object Groups(int[][] sources){object value=Activator.CreateInstance(groupsType);foreach(var group in sources)groupsType.GetMethod("Add").Invoke(value,new object[]{referenceIds[group[0]],group.ToList()});return value;}
            var data=new MyMeshData{PartInfos=sourceParts,Positions=new HalfVector4[names.Length*3],MatsIndices=new Dictionary<string,Tuple<int,int,int>>()};
            object[] args={"CPU-part-identity-oracle",Groups(merged),Groups(separate),data,null};build.Invoke(null,args);
            var parts=(Array)args[4];var indices=((IEnumerable)data.NewIndices).Cast<uint>().ToArray();var expectedMaterials=Enumerable.Repeat(-1,names.Length).ToArray();var expectedProxies=Enumerable.Repeat(-1,names.Length).ToArray();var expectedProxyMaterials=new List<int>();
            for(int p=0;p<parts.Length;p++){
                object part=parts.GetValue(p);Type t=part.GetType();int material=Index(t.GetField("Material",all).GetValue(part));expectedProxyMaterials.Add(material);
                int first=(int)t.GetField("StartIndex",all).GetValue(part),count=(int)t.GetField("IndexCount",all).GetValue(part),vertex=(int)t.GetField("BaseVertex",all).GetValue(part);
                for(int n=first;n<first+count;n++){int source=(int)(indices[n]+vertex)/3;expectedMaterials[source]=material;expectedProxies[source]=p;}
            }
            check(actual.SourceMaterials.SequenceEqual(expectedMaterials),"source material identities match actual stock: "+label);
            check(actual.SourceProxies.SequenceEqual(expectedProxies),"source proxy order matches actual stock: "+label);
            check(actual.ProxyMaterials.SequenceEqual(expectedProxyMaterials),"proxy/material stream matches actual stock: "+label);
            check(owner.MergedPartCounter==(int)counter.GetValue(null),"merged-name counter matches actual stock: "+label);
            check(identities.Count==Count(Pool()),"material allocation clock matches actual stock: "+label);
        }
        Snapshot();Case("non-array A/B/A",new[]{"A","B","A"},new[]{false,false,false},Array.Empty<int[]>(),new[]{new[]{0,2},new[]{1}});
        Snapshot();Case("array merge and singleton migration",new[]{"A","B","A","C"},new[]{true,true,true,true},new[]{new[]{0,2}},new[]{new[]{1},new[]{3}});
        Case("next LOD retains merged counter/material reuse",new[]{"A","B","A"},new[]{true,false,true},new[]{new[]{0,2}},new[]{new[]{1}});
        Snapshot();Case("mixed array/raw/singleton order",new[]{"A","B","C","B","D"},new[]{false,true,false,true,true},new[]{new[]{1,3}},new[]{new[]{0},new[]{2},new[]{4}});
        Snapshot();Case("two merged groups",new[]{"A","B","A","B"},new[]{true,true,true,true},new[]{new[]{0,2},new[]{1,3}},Array.Empty<int[]>());
        Snapshot();Case("rejected merge follows singleton",new[]{"A","B","A"},new[]{true,true,true},Array.Empty<int[]>(),new[]{new[]{1},new[]{0,2}},_=>false);
    }
}
