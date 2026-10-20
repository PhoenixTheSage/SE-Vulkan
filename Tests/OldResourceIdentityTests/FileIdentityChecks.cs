using ClientPlugin.Rendering.Content;
using FixtureAssets;
using VRageMath;
using VRageRender.Import;

static partial class Program
{
    static void FileIdentityChecks(string bin,Action<bool,string> check)
    {
        string output=Path.GetFullPath(Path.Combine("TestResults/OldFileIdentities",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
        string leaf=MeshDecalFixtureAssets.Model(Path.Combine(output,"leaf"),MyMeshDrawTechnique.MESH,7,255,singleLayer:true,neutral:true);
        string missing=Path.Combine(output,"missing.mwm");
        string main=MeshDecalFixtureAssets.Model(Path.Combine(output,"main"),MyMeshDrawTechnique.MESH,7,255,singleLayer:true,neutral:true,identityLods:new[]{new MyLODDescriptor{Model=missing,Distance=10},new MyLODDescriptor{Model=leaf,Distance=20}});
        var materialIds=new StockOldMaterialIdentities();materialIds.RegisterCommonMaterials();var indices=new StockOldResourceIndices();var parts=new StockOldPartIdentities(materialIds);var reader=new StockOldFileIdentityReader(new StockOldMaterialDescriptions());
        var reads=new List<string>();var layouts=new List<StockOldVertexLayout>();
        var files=new StockOldFileIdentities(indices,parts,l=>{layouts.Add(l);return l.Animated?21:l.GeometryArrays?12:8;},(file,arrays)=>{reads.Add(file);return reader.Read(file,arrays);});
        int runtime=indices.Allocate();check(runtime==0,"external runtime/voxel reservation precedes file LODs in the shared pool");
        var root=files.Get(main,true);
        check(root.Lods.Count==2&&root.Lods[0].AssetFile==main&&root.Lods[1].AssetFile==leaf,"file import eagerly retains successful LODs and compacts a missing descriptor");
        check(root.Lods[0].ResourceIndex==1&&root.Lods[1].ResourceIndex==2&&root.Lods[1].Distance==20,"retained LOD identities share the external source clock and authored distance");
        check(layouts.Count==2&&layouts.All(l=>!l.Animated&&l.StandardStreams&&l.GeometryArrays)&&root.Lods.All(l=>l.LayoutIndex==12),"layout resolver receives source stream/array signatures after preprocessing");
        check(reads.SequenceEqual(new[]{main,missing,leaf}),"eager metadata load order matches root then authored LOD sequence");
        int count=materialIds.Count;
        check(count==3&&root.Lods[0].Materials.SourceMaterials[0]==root.Lods[1].Materials.SourceMaterials[0],"post-array material representations reuse identity across LOD files");
        check(ReferenceEquals(files.Get(main,false),root)&&reads.Count==3&&materialIds.Count==count,"cached file name retains first-loaded representation without repeat import or allocation");
        var standalone=files.Get(leaf,true);check(standalone.Lods[0].ResourceIndex==3&&files.Count==2,"a child loaded by name later owns a separate stock file/LOD identity");
        check(files.Remove(main)&&files.Count==1&&indices.Count==2,"explicit file removal retires only its LOD identities");
        check(indices.Allocate()==2,"file LOD removal frees in source order and preserves LIFO reuse for another resource kind");
        var reloaded=files.Get(main,true);check(reloaded.Lods[0].ResourceIndex==1&&reloaded.Lods[1].ResourceIndex==4&&materialIds.Count==count,"file reload reuses retired LOD indices while retaining material identities");
        check(!files.Remove("not-loaded"),"unknown file removal consumes no identity");

        // OLD DoImport takes geometry metadata (including LODs) from the linked
        // asset. Its material path context remains the originally loaded file.
        string geometry=MeshDecalFixtureAssets.Model(Path.Combine(output,"geometry"),MyMeshDrawTechnique.MESH,7,255,singleLayer:true,neutral:true,identityLods:new[]{new MyLODDescriptor{Model=leaf,Distance=30}});
        string stub=MeshDecalFixtureAssets.Model(Path.Combine(output,"stub"),MyMeshDrawTechnique.MESH,7,255,singleLayer:true,neutral:true,identityLods:new[]{new MyLODDescriptor{Model=missing,Distance=99}},geometryDataAsset:geometry);
        var indirect=files.Get(stub,true);check(indirect.Lods.Count==2&&indirect.Lods[1].AssetFile==leaf&&indirect.Lods[1].Distance==30,"geometry indirection supplies OLD LOD metadata rather than the stub's LOD list");
        string skinned=MeshDecalFixtureAssets.Model(Path.Combine(output,"skinned"),MyMeshDrawTechnique.MESH,7,255,singleLayer:true,neutral:true,deformed:true);
        var skin=files.Get(skinned,false);check(skin.Lods[0].Layout.Animated&&skin.Lods[0].Layout.StandardStreams&&!skin.Lods[0].Layout.GeometryArrays,"retained file metadata keeps real authored skin streams");

        var bones=Enumerable.Range(0,61).Select(i=>new Vector4I(60+i,0,0,0)).ToArray();bones[60]=new Vector4I(60,0,0,0);
        var weights=Enumerable.Repeat(new Vector4(1,0,0,0),61).ToArray();
        var animation=new StockOldFileLodSource("animation",Array.Empty<MyLODDescriptor>(),Array.Empty<StockOldMaterialDescription>(),new[]{Enumerable.Range(0,60).ToArray(),Enumerable.Range(0,60).Concat(new[]{60,60,60}).ToArray()},bones,weights,true,true,false);
        check(animation.CanMerge(new[]{0}),"a sixty-bone group remains eligible for stock animation remapping");
        check(!animation.CanMerge(new[]{1}),"later groups see shared vertex bone remaps when testing the sixty-bone limit");animation.PrepareSeparate(1);
        check(!animation.Layout.Animated,"an unsupported separate part disables skin layout like the OLD importer");

        // Real installed built-ins exercise metadata-only bootstrap input. This
        // does not certify the separate vertex-layout bootstrap allocator yet.
        var builtins=new StockOldMaterialIdentities();var builtinParts=new StockOldPartIdentities(builtins);var builtinPool=new StockOldResourceIndices();
        var builtinFiles=new StockOldFileIdentities(builtinPool,builtinParts,_=>0,reader.Read);
        var cone=builtinFiles.Get("Models/Debug/Cone.mwm",false);var sphere=builtinFiles.Get("Models/Debug/Sphere.mwm",false);int beforeCommon=builtins.Count;builtins.RegisterCommonMaterials();
        check(cone.Lods.Count>0&&sphere.Lods.Count>0&&builtinPool.Count==cone.Lods.Count+sphere.Lods.Count,"installed Cone/Sphere bootstrap imports retain eager LOD identities without geometry upload");
        check(builtins.NullMaterial==beforeCommon&&builtins.DebugMaterial==beforeCommon+1,"common material aliases follow actual built-in metadata registration");
        Console.WriteLine("BUILTIN IDENTITY coneLods="+cone.Lods.Count+" sphereLods="+sphere.Lods.Count+" preCommonMaterials="+beforeCommon);
    }
}
