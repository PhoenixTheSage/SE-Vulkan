using System;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Content;

internal sealed class StockOldModelIdentity
{
    public readonly StockOldFileIdentity File;
    public readonly StockOldRuntimeIdentity Runtime;
    public StockOldModelIdentity(StockOldFileIdentity file){File=file;}
    public StockOldModelIdentity(StockOldRuntimeIdentity runtime){Runtime=runtime;}
    public int LodCount=>File?.Lods.Count??1;
    public int Resource(int lod)=>File!=null?File.Lods[lod].ResourceIndex:Runtime.ResourceIndex;
    public int Layout(int lod)=>File!=null?File.Lods[lod].LayoutIndex:Runtime.LayoutIndex;
    public int Material(int lod,int part)=>File!=null?File.Lods[lod].Materials.SourceMaterials[part]:Runtime.Material(part);
    public int Proxy(int lod,int part)=>File!=null?File.Lods[lod].Materials.SourceProxies[part]:part;
}

/// <summary>One retained owner for OLD file/runtime/voxel/layout allocation.</summary>
internal sealed class StockOldResourceIdentities
{
    public readonly StockOldResourceIndices Indices=new();
    public readonly StockOldMaterialIdentities Materials=new();
    public readonly StockOldVertexLayouts Layouts=new();
    public readonly StockOldPartIdentities Parts;
    public readonly StockOldFileIdentities Files;
    public readonly StockOldTransientIdentities Transient;
    public bool Initialized {get;private set;}
    public bool GeometryArrays {get;set;}
    public StockOldResourceIdentities(Func<string,bool,StockOldFileLodSource> read=null)
    {
        Parts=new(Materials);Files=new(Indices,Parts,Layouts.File,read??new StockOldFileIdentityReader(new StockOldMaterialDescriptions()).Read);
        Transient=new(Indices,Materials,Layouts);
    }
    public void Initialize(bool geometryArrays,bool stereo=false)
    {
        GeometryArrays=geometryArrays;if(Initialized)return;
        Layouts.RegisterBeforeBuiltinMeshes();Files.Get("Models/Debug/Cone.mwm",geometryArrays);
        Layouts.RegisterAfterBuiltinCone();Files.Get("Models/Debug/Sphere.mwm",geometryArrays);
        Layouts.RegisterAfterBuiltinSphere(stereo);Materials.RegisterCommonMaterials();Initialized=true;
    }
    public StockOldModelIdentity Get(string name)
    {
        if(!Initialized)throw new InvalidOperationException("OLD resource owner has not been initialized.");
        var runtime=Transient.FindRuntime(name);return runtime!=null?new(runtime):new(Files.Get(name,GeometryArrays));
    }
    public bool Remove(string name)
    {
        var runtime=Transient.FindRuntime(name);
        if(runtime==null)return Files.Remove(name);
        bool removed=Transient.RemoveRuntime(runtime);Files.Forget(name);return removed;
    }
    public bool Exists(string name)=>Files.Contains(name)||Transient.FindRuntime(name)!=null;
    public int InstanceLayout(bool cube)=>Layouts.Get(new StockOldVertexComponent(cube?MyVertexInputComponentType.CUBE_INSTANCE:MyVertexInputComponentType.GENERIC_INSTANCE,2,true));
    public void RegisterTextureChange(StockOldModelIdentity model,string materialName,MyTextureChange change)
    {
        for(int lod=0;lod<model.LodCount;lod++){
            var values=model.File!=null?model.File.Lods[lod].Materials.ProxyMaterials:model.Runtime.Materials;
            foreach(int id in values){var original=Materials.Get(id);if(original.Name==(materialName??string.Empty))Materials.GetOrCreate(StockOldMaterialDescriptions.ChangeTextures(original,change));}
        }
    }
    public void SessionEnd()=>Transient.SessionEnd();
}
