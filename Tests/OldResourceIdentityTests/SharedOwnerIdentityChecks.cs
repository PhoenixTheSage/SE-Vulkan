using ClientPlugin.Rendering.Content;
using FixtureAssets;
using VRageRender.Import;

static partial class Program
{
    static void SharedOwnerIdentityChecks(Action<bool,string> check)
    {
        var owner=new StockOldResourceIdentities();owner.Initialize(false);
        check(owner.Initialized&&owner.Indices.Count==2&&owner.Files.Count==2&&owner.Layouts.Count==15,"shared owner bootstraps actual installed Cone/Sphere LODs and all device layouts");
        check(owner.Materials.NullMaterial==0&&owner.Materials.DebugMaterial==1,"shared owner common materials follow installed builtin registration");
        var cone=owner.Get("Models/Debug/Cone.mwm");check(cone.Resource(0)==0&&cone.Layout(0)==13,"builtin lookup retains first stock LOD and packed-position layout");
        check(owner.Exists("Models/Debug/Cone.mwm")&&!owner.Exists("not-loaded"),"AddRuntimeModel existence guard sees retained files without consuming identities");
        owner.Initialize(false);check(owner.Indices.Count==2&&owner.Layouts.Count==15,"repeated shared-owner initialization preserves clocks");
        string output=Path.GetFullPath(Path.Combine("TestResults/OldSharedOwner",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
        string file=MeshDecalFixtureAssets.Model(output,MyMeshDrawTechnique.DECAL_CUTOUT,1,255,singleLayer:true);
        var model=owner.Get(file);check(model.Resource(0)==2&&model.Layout(0)==12&&model.Material(0,0)==2&&model.Proxy(0,0)==0,"first authored file follows bootstrap with retained LOD/layout/material/proxy fields");
        int material=model.Material(0,0);var technique=owner.Materials.Get(material).Technique;
        check(StockOldSortKey.Pack(StockOldSortKey.IsDecal(technique),StockOldSortKey.Technique(technique),0,model.Layout(0),model.Resource(0),material)==1653665488371970ul,"shared file fields produce complete stock rigid sort key");
        var runtime=owner.Transient.CreateRuntime(file,1,false);owner.Transient.UpdateRuntime(runtime,new[]{owner.Materials.Get(material).Name});var current=owner.Get(file);
        check(current.Runtime==runtime&&current.Resource(0)==3&&current.Material(0,0)==material,"runtime name alias takes precedence over retained same-name file identity");
        check(owner.Remove(file)&&owner.Files.Count==2&&owner.Indices.Count==3,"same-name runtime removal forgets file alias without freeing the file resource still referenced by actors");
        check(!owner.Exists(file),"unnamed retained allocations do not block a later AddRuntimeModel name");
        var reload=owner.Get(file);check(reload.File!=model.File&&reload.Resource(0)==3&&model.Resource(0)==2,"reload after alias removal reuses retired runtime LOD while original file identity stays retained");
        int count=owner.Indices.Count;owner.SessionEnd();check(owner.Indices.Count==count&&owner.Get(file).File==reload.File,"shared session end preserves retained file identities and resource clock");
        check(owner.Remove(file)&&owner.Indices.Count==count-1,"shared explicit file removal retires only currently named file allocation");
        int before=owner.Materials.Count;string name=owner.Materials.Get(material).Name;var change=new VRageRender.Messages.MyTextureChange{ColorMetalFileName="runtime:old-screen"};
        owner.RegisterTextureChange(model,name,change);int variant=owner.Materials.GetByName(name);
        check(owner.Materials.Count==before+1&&variant==before&&model.Material(0,0)==material,"OLD texture replacement allocates a global material variant while preserving the base sorting identity");
        owner.RegisterTextureChange(model,name,change);owner.RegisterTextureChange(model,"unused-material",change);check(owner.Materials.Count==before+1,"repeated and unused OLD texture replacements consume no extra identities");
        var later=owner.Transient.CreateRuntime("later-runtime",1,false);owner.Transient.UpdateRuntime(later,new[]{name});check(later.Material(0)==variant,"runtime material-name lookup observes the latest newly allocated texture variant alias");
    }
}
