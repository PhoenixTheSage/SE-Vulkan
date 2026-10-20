using System;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using ClientPlugin.Rendering.Content;
using VRageRender;

namespace FixtureAssets;

internal static class StockOldLayoutIdentityOracle
{
    public static void Run(Assembly stock,XElement report,Action<bool,string> check)
    {
        const BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        Type layouts=stock.GetType("VRageRender.MyVertexLayouts",true);object pool=layouts.GetField("Layouts",all).GetValue(null);
        int Count()=>(int)pool.GetType().GetProperty("Size").GetValue(pool);
        int before=Count();var data=(Array)pool.GetType().GetProperty("Data").GetValue(pool);var snapshot=new XElement("Layouts",new XAttribute("Count",before));report.Add(snapshot);
        StockOldVertexComponent[] Values(int index){object entry=data.GetValue(index);var values=(Array)entry.GetType().GetField("Components",all).GetValue(entry);if(values==null)return Array.Empty<StockOldVertexComponent>();
            return values.Cast<object>().Select(v=>{Type t=v.GetType();return new StockOldVertexComponent((MyVertexInputComponentType)t.GetField("Type",all).GetValue(v),(int)t.GetField("Slot",all).GetValue(v),Convert.ToInt32(t.GetField("Freq",all).GetValue(v))==1);}).ToArray();}
        string Text(StockOldVertexComponent[] values)=>string.Join(";",values.Select(v=>v.Type+":"+v.Slot+":"+(v.PerInstance?1:0)));
        for(int i=0;i<before;i++)snapshot.Add(new XElement("Layout",new XAttribute("Index",i),new XAttribute("Components",Text(Values(i)))));
        Type materialType=stock.GetType("VRageRender.MyMeshMaterials1",true);object materialPool=materialType.GetField("MaterialsPool",all).GetValue(null);int materialCount=(int)materialPool.GetType().GetProperty("Size").GetValue(materialPool);var materialData=(Array)materialPool.GetType().GetProperty("Data").GetValue(materialPool);
        var materialReport=new XElement("MaterialTable");report.Add(materialReport);for(int i=0;i<materialCount;i++){object m=materialData.GetValue(i);Type t=m.GetType();string Field(string name)=>Convert.ToString(t.GetField(name,all).GetValue(m));materialReport.Add(new XElement("Material",new XAttribute("Index",i),new XAttribute("Name",Field("Name")),new XAttribute("Color",Field("ColorMetal_Texture")),new XAttribute("Normal",Field("NormalGloss_Texture")),new XAttribute("Extensions",Field("Extensions_Texture")),new XAttribute("Alpha",Field("Alphamask_Texture")),new XAttribute("Technique",Field("Technique"))));}
        // Inspect the already-loaded file table, without requesting/loading a
        // model. This preserves global clock evidence for the Vulkan producer.
        Type meshes=stock.GetType("VRageRender.MyMeshes",true);var meshNames=(System.Collections.IEnumerable)meshes.GetField("MeshNameIndex",all).GetValue(null);
        var fileReport=new XElement("Files");report.Add(fileReport);
        var getLod=meshes.GetMethods(all).Single(m=>m.Name=="GetLodMesh"&&m.GetParameters().Length==2);
        foreach(object pair in meshNames){Type p=pair.GetType();object mesh=p.GetProperty("Value").GetValue(pair);object info=mesh.GetType().GetProperty("Info",all).GetValue(mesh);int count=(int)info.GetType().GetField("LodsNum",all).GetValue(info);
            var fileEntry=new XElement("File",new XAttribute("Name",p.GetProperty("Key").GetValue(pair).ToString()));fileReport.Add(fileEntry);
            for(int l=0;l<count;l++){object lod=getLod.Invoke(null,new[]{mesh,(object)l});fileEntry.Add(new XElement("Lod",new XAttribute("Resource",lod.GetType().GetField("Index",all).GetValue(lod))));}
        }
        var owner=new StockOldVertexLayouts();owner.RegisterBeforeBuiltinMeshes();owner.File(new(false,false,false));owner.RegisterAfterBuiltinCone();owner.File(new(false,false,false));owner.RegisterAfterBuiltinSphere(false);
        int bootstrap=owner.Count;report.SetAttributeValue("ExpectedBootstrapCount",bootstrap);
        check(before>=bootstrap,"actual stock retains the complete device bootstrap prefix");
        for(int i=0;i<bootstrap;i++)check(Text(owner.Components(i))==Text(Values(i)),"device bootstrap component sequence matches stock layout "+i);
        int file=owner.File(new(false,true,false));check(file<before&&Text(owner.Components(file))==Text(Values(file)),"OLD standard file layout reuses stock MeshTable bootstrap identity in this held fixture");
        // Replay later real allocations in recorded order to test identity
        // hashing against the initialized runtime, without registering stock.
        for(int i=bootstrap;i<before;i++)check(owner.Get(Values(i))==i,"retained layout allocation matches actual stock index "+i);
        check(Count()==before,"production layout proof leaves actual stock pool unchanged");
    }
}
