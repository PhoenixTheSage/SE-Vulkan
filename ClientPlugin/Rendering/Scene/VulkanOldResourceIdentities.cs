using System;
using System.Collections.Generic;
using System.Xml.Linq;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    void EnsureOldModelIdentity(ActorData actor)
    {
        if(!OldResources.Initialized||!actor.TrackOldIdentity||actor.Model==null||actor.OldModel!=null)return;
        if(KnownStockOldPipeline(actor)||(actor.StaticInstances==null&&!models.IsStockNewModel(actor.Model)))actor.OldModel=OldResources.Get(actor.Model.Name);
    }
    void RegisterOldIdentityVariants(ActorData actor)
    {
        if(actor.OldModel==null||(actor.OldVariantModel==actor.OldModel&&actor.OldVariantRevision==actor.MaterialRevision))return;
        foreach(var item in actor.Materials)OldResources.RegisterTextureChange(actor.OldModel,item.Key,item.Value.Textures);
        actor.OldVariantModel=actor.OldModel;actor.OldVariantRevision=actor.MaterialRevision;
    }
    void RegisterOldActorLayouts(ActorData actor)
    {
        if(actor.OldModel==null)return;
        int stream=actor.InstanceBuffer!=uint.MaxValue&&instances.TryGetValue(actor.InstanceBuffer,out var buffer)?buffer.OldLayout:0;
        if(actor.OldLayoutModel==actor.OldModel&&actor.OldLayoutInstance==actor.InstanceBuffer&&actor.OldLayoutStream==stream)return;
        var model=actor.OldModel;var layouts=new int[model.LodCount];
        for(int lod=0;lod<layouts.Length;lod++){int original=model.Layout(lod);layouts[lod]=stream!=0?OldResources.Layouts.Combine(original,stream):original;}
        actor.OldLodLayouts=layouts;actor.OldLayoutModel=model;actor.OldLayoutInstance=actor.InstanceBuffer;actor.OldLayoutStream=stream;
    }
    // Source LOD matches resolve and canonicalize asset paths; they depend only
    // on the identity, the drawn model file and the requested level.
    readonly Dictionary<(object Identity,string Model,int Requested),int> oldLodMatches=new();
    NativeOldPartOrder OldPartOrder(ActorData actor,VulkanModel model,VulkanModelPart part,int requestedLod)
    {
        var identity=actor.OldModel;if(identity==null)return default;
        RegisterOldActorLayouts(actor);int lod=0;
        if(identity.File!=null&&!oldLodMatches.TryGetValue((identity,model.Name,requestedLod),out lod)){
            string Path(string name)=>System.IO.Path.GetFullPath(VulkanTextureCache.ResolvePath(name));string file=Path(model.Name);
            bool Match(int index)=>index>=0&&index<identity.LodCount&&string.Equals(Path(identity.File.Lods[index].AssetFile),file,StringComparison.OrdinalIgnoreCase);
            if(Match(requestedLod))lod=requestedLod;
            else {lod=-1;for(int i=0;i<identity.LodCount;i++)if(Match(i)){lod=i;break;}if(lod<0)throw new InvalidOperationException("OLD draw has no retained source LOD identity.");}
            oldLodMatches[(identity,model.Name,requestedLod)]=lod;
        }
        int count=identity.File!=null?identity.File.Lods[lod].Materials.SourceMaterials.Count:identity.Runtime.Materials.Length;
        if(part.SourceIndex<0||part.SourceIndex>=count)return default;
        int material=identity.Material(lod,part.SourceIndex),resource=identity.Resource(lod),layout=actor.OldLodLayouts[lod];
        uint flags=actor.SkinMatrices!=null?512u:0u;
        if(actor.InstanceBuffer!=uint.MaxValue&&instances.TryGetValue(actor.InstanceBuffer,out var buffer)&&buffer.OldLayout!=0){bool bones=identity.File!=null&&identity.File.Lods[lod].Layout.Animated;flags|=buffer.OldCube?(bones?8192u:4096u):16384u;}
        var technique=OldResources.Materials.Get(material).Technique;
        return new NativeOldPartOrder{Key=StockOldSortKey.Pack(StockOldSortKey.IsDecal(technique),StockOldSortKey.Technique(technique),flags,layout,resource,material),Resource=(uint)resource,Material=(uint)material,Layout=(uint)layout,Proxy=(uint)identity.Proxy(lod,part.SourceIndex),Valid=1};
    }
    // Bounded CPU diagnostic, called on the render owner after held captures.
    // These identities do not yet replace submission order.
    public string StockOldIdentitySnapshot(uint[] ids)
    {
        var root=new XElement("OldResourceIdentities",new XAttribute("Initialized",OldResources.Initialized),new XAttribute("Lods",OldResources.Indices.Count),new XAttribute("Materials",OldResources.Materials.Count),new XAttribute("Layouts",OldResources.Layouts.Count));
        var files=new XElement("Files");root.Add(files);foreach(var file in OldResources.Files.Models){var item=new XElement("File",new XAttribute("Name",file.Name));files.Add(item);foreach(var lod in file.Lods)item.Add(new XElement("Lod",new XAttribute("Resource",lod.ResourceIndex),new XAttribute("Layout",lod.LayoutIndex),new XAttribute("Materials",string.Join(",",lod.Materials.SourceMaterials))));}
        var materials=new XElement("MaterialTable");root.Add(materials);for(int i=0;i<OldResources.Materials.Count;i++){var m=OldResources.Materials.Get(i);materials.Add(new XElement("Material",new XAttribute("Index",i),new XAttribute("Name",m.Name),new XAttribute("Color",m.Color),new XAttribute("Normal",m.Normal),new XAttribute("Extensions",m.Extensions),new XAttribute("Alpha",m.Alpha),new XAttribute("Technique",m.Technique)));}
        var layoutTable=new XElement("LayoutTable");root.Add(layoutTable);for(int i=0;i<OldResources.Layouts.Count;i++){var values=OldResources.Layouts.Components(i);layoutTable.Add(new XElement("Layout",new XAttribute("Index",i),new XAttribute("Components",string.Join(";",System.Linq.Enumerable.Select(values,v=>v.Type+":"+v.Slot+":"+(v.PerInstance?1:0))))));}
        foreach(uint id in ids){if(!actors.TryGetValue(id,out var actor)||actor.OldModel==null)continue;var identity=actor.OldModel;
            var entry=new XElement("Actor",new XAttribute("Id",id),new XAttribute("Model",actor.Model.Name));root.Add(entry);
            for(int l=0;l<identity.LodCount;l++){
                var lod=new XElement("Lod",new XAttribute("Index",l),new XAttribute("Resource",identity.Resource(l)),new XAttribute("Layout",identity.Layout(l)));entry.Add(lod);
                int count=identity.File!=null?identity.File.Lods[l].Materials.SourceMaterials.Count:identity.Runtime.Materials.Length;
                for(int p=0;p<count;p++){int material=identity.Material(l,p);var technique=OldResources.Materials.Get(material).Technique;
                    lod.Add(new XElement("Part",new XAttribute("Source",p),new XAttribute("Proxy",identity.Proxy(l,p)),new XAttribute("Material",material),new XAttribute("Technique",technique),new XAttribute("RigidSortKey",StockOldSortKey.Pack(StockOldSortKey.IsDecal(technique),StockOldSortKey.Technique(technique),0,identity.Layout(l),identity.Resource(l),material))));}
            }
        }
        return root.ToString();
    }
}
