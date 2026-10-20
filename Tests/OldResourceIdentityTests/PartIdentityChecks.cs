using System.Reflection;
using ClientPlugin.Rendering.Content;
using VRageRender.Import;

static partial class Program
{
    static void PartIdentityChecks(Assembly stock,Action<bool,string> check)
    {
        var identities=new StockOldMaterialIdentities();identities.RegisterCommonMaterials();var owner=new StockOldPartIdentities(identities);
        StockOldMaterialDescription[] Input(string[] names,bool[] arrays)=>names.Select((name,p)=>new StockOldMaterialDescription(new StockOldMaterialIdentity(name,"","","","",MyMeshDrawTechnique.MESH),arrays[p])).ToArray();
        void Same(StockOldLodMaterials actual,int[] materials,int[] proxies,int[] output,string label){check(actual.SourceMaterials.SequenceEqual(materials),"retained source material map: "+label);check(actual.SourceProxies.SequenceEqual(proxies),"retained source proxy map: "+label);check(actual.ProxyMaterials.SequenceEqual(output),"retained proxy material stream: "+label);}
        Same(owner.Register(Input(new[]{"A","B","A"},new[]{false,false,false})),new[]{2,3,2},new[]{0,2,1},new[]{2,2,3},"non-array A/B/A");
        Same(owner.Register(Input(new[]{"A","B","A","C"},new[]{true,true,true,true})),new[]{5,3,5,4},new[]{0,1,0,2},new[]{5,3,4},"array merge/singletons");
        Same(owner.Register(Input(new[]{"A","B","A"},new[]{true,false,true})),new[]{6,3,6},new[]{0,1,0},new[]{6,3},"next LOD retains counter");
        check(owner.MergedPartCounter==2,"merged allocation counter spans LOD registrations");
        Same(owner.Register(Input(new[]{"A","B","A"},new[]{true,true,true}),_=>false),new[]{2,3,2},new[]{1,0,2},new[]{3,2,2},"failed merge after migrated singleton");check(owner.MergedPartCounter==2,"failed merge consumes no synthetic name");
        var empty=owner.Register(Array.Empty<StockOldMaterialDescription>());check(empty.SourceMaterials.Count==0&&empty.ProxyMaterials.Count==0,"empty part registration creates no proxies");
        // MyMeshes' independent private-method comparison needs the initialized
        // isolated DX11 fixture because its cctor owns D3D managers.
    }
}
