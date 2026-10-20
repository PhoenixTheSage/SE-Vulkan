using System.Reflection;
using ClientPlugin.Rendering.Content;
using FixtureAssets;
using VRage.FileSystem;
using VRageMath;
using VRageRender.Import;

static partial class Program
{
    static void MaterialDescriptionChecks(string bin,Assembly stock,Action<bool,string> check,Func<(int,int,int)> footprint,Func<StockOldMaterialIdentity,string,int> register)
    {
        const BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        string output=Path.GetFullPath(Path.Combine("TestResults/OldResourceIdentity",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
        MyFileSystem.Init(Path.GetFullPath(Path.Combine(bin,"../Content")),output);
        string mod=Path.Combine(output,"Mod"),model=Path.Combine(mod,"Models/Test.mwm"),texture=Path.Combine(mod,"Textures/coat.dds");Directory.CreateDirectory(Path.GetDirectoryName(model)!);Directory.CreateDirectory(Path.GetDirectoryName(texture)!);
        MeshDecalFixtureAssets.Texture(texture,255,0,0,255);
        var source=new MyMaterialDescriptor("CPU-import"){TechniqueEnum=MyMeshDrawTechnique.DECAL_CUTOUT};source.Textures["ColorMetalTexture"]="Textures/coat.dds";
        var convert=stock.GetType("VRageRender.MyMeshMaterials1",true)!.GetMethod("ConvertImportDescToMeshMaterialInfo",all)!;
        StockOldMaterialIdentity Expected(MyMaterialDescriptor descriptor,string content){object value=convert.Invoke(null,new object[]{descriptor,content,model})!;Type t=value.GetType();string Field(string name)=>(string)t.GetField(name,all)!.GetValue(value)!;
            return new(t.GetField("Name",all)!.GetValue(value)!.ToString()!,Field("ColorMetal_Texture"),Field("NormalGloss_Texture"),Field("Extensions_Texture"),Field("Alphamask_Texture"),(MyMeshDrawTechnique)t.GetField("Technique",all)!.GetValue(value)!);}
        void Same(StockOldMaterialIdentity actual,StockOldMaterialIdentity expected,string label){check(actual.Name==expected.Name&&actual.Color==expected.Color&&actual.Normal==expected.Normal&&actual.Extensions==expected.Extensions&&actual.Alpha==expected.Alpha&&actual.Technique==expected.Technique,"CPU descriptor matches actual stock import conversion: "+label);register(actual,label);}
        var before=footprint();var producer=new StockOldMaterialDescriptions();var raw=producer.Describe(model,new[]{source},false)[0];
        check(footprint()==before,"path/header identity producer leaves stock material/proxy/resource queues untouched");
        var headlessArray=producer.Describe(model,new[]{source},true)[0];
        check(VRage.MyVRage.Platform==null&&footprint()==before,"production path and actual image-header producer need no platform renderer initialization");
        InitializeOraclePlatform(); // Reference conversion's RenderProxy cctor needs memory-budget queries.
        Same(raw,Expected(source,mod),"mod-relative path");
        source.UserData["Facing"]="Full";source.UserData["WindScale"]="2";source.UserData["WindFrequency"]="3";
        Same(producer.Describe(model,new[]{source},false)[0],raw,"non-identity facing/wind changes");
        check(producer.Describe(model,new MyMaterialDescriptor[]{null!},true)[0]==null,"null imported material remains a null-alias request");

        var arrayType=stock.GetType("VRage.Render11.GeometryStage.MyGeometryTextureSystem",true)!.GetNestedType("MyArrayTextureKey",all)!;
        var formatType=arrayType.GetField("Format",all)!.FieldType;var channelType=arrayType.GetField("Channel",all)!.FieldType;
        string ArrayName(string channel,int width,int height,int mips,string format){object value=Activator.CreateInstance(arrayType)!;arrayType.GetField("Channel",all)!.SetValue(value,Enum.Parse(channelType,channel));arrayType.GetField("ResolutionInFile",all)!.SetValue(value,new Vector2I(width,height));arrayType.GetField("MipLevels",all)!.SetValue(value,mips);arrayType.GetField("Format",all)!.SetValue(value,Enum.Parse(formatType,format));return value.ToString()!;}
        before=footprint();var arrays=producer.Describe(model,new[]{source},true)[0];check(footprint()==before,"stock CPU image-header path creates no material registrations/proxies/resource requests");
        var expectedArray=new StockOldMaterialIdentity(source.MaterialName,ArrayName("ColorMetal",16,16,1,"R8G8B8A8_UNorm_SRgb"),ArrayName("NormalGloss",16,16,5,"BC7_UNorm"),ArrayName("Extension",16,16,5,"BC7_UNorm_SRgb"),"",source.TechniqueEnum);
        Same(arrays,expectedArray,"actual DDS header and stock array-name formatter");
        Same(headlessArray,expectedArray,"same complete array representation produced without a platform renderer");
        check(producer.Describe(model,new[]{source},true)[0].RepresentationKey==arrays.RepresentationKey,"repeated header reads retain material representation");

        foreach(string name in new[]{"runtime:target","Textures/nonexistent.dds",texture,Path.Combine(Path.GetDirectoryName(texture)!,"../Textures/coat.dds"),"texture (variant).dds"}){
            var pathCase=new MyMaterialDescriptor("path-case"){TechniqueEnum=MyMeshDrawTechnique.MESH};pathCase.Textures["ColorMetalTexture"]=name;
            Same(producer.Describe(model,new[]{pathCase},false)[0],Expected(pathCase,mod),"generated/absolute/fallback/normalized path: "+name);
        }
        foreach(string name in new[]{"runtime:target","Textures/nonexistent.dds",texture,string.Empty}){
            var replacement=new VRageRender.Messages.MyTextureChange{ColorMetalFileName=name};var expected=new MyMaterialDescriptor(raw.Name){TechniqueEnum=raw.Technique};
            expected.Textures["ColorMetalTexture"]=name;expected.Textures["NormalGlossTexture"]=raw.Normal;expected.Textures["AddMapsTexture"]=raw.Extensions;expected.Textures["AlphamaskTexture"]=raw.Alpha;
            var changed=StockOldMaterialDescriptions.ChangeTextures(raw,replacement);Same(changed,Expected(expected,null),"texture replacement resolves independently of original mod context: "+name);register(changed,"texture replacement "+name);
            check(changed.Normal==raw.Normal&&changed.Extensions==raw.Extensions&&changed.Alpha==raw.Alpha&&changed.Name==raw.Name,"partial texture replacement retains other RK fields");
        }

        var impostor=new MyMaterialDescriptor("impostor"){TechniqueEnum=MyMeshDrawTechnique.MESH};impostor.UserData["Facing"]="Impostor";impostor.Textures["ColorMetalTexture"]="Textures/coat.dds";
        var fixedImpostor=new MyMaterialDescriptor("impostor"){TechniqueEnum=MyMeshDrawTechnique.MESH};fixedImpostor.Textures["ColorMetalTexture"]="Textures/coat.dds";fixedImpostor.Textures["NormalGlossTexture"]="Textures/coat_normal_depth.dds";
        Same(producer.Describe(model,new[]{impostor},false)[0],Expected(fixedImpostor,mod),"impostor normal-depth inference");check(!impostor.Textures.ContainsKey("NormalGlossTexture"),"CPU identity production does not mutate imported material descriptors");

        var arrayFile=new MyMaterialDescriptor("already-array"){TechniqueEnum=MyMeshDrawTechnique.MESH};arrayFile.Textures["ColorMetalTexture"]=Path.Combine(output,"array.dds");
        int headerCalls=0;var arrayProducer=new StockOldMaterialDescriptions(_=>{headerCalls++;return new StockOldTextureHeader(32,16,4,2,28);});
        Same(arrayProducer.Describe(model,new[]{arrayFile},true)[0],Expected(arrayFile,mod),"authored array excludes geometry-array substitution");check(headerCalls==1,"CPU texture-header facts are cached");

        var missing=new MyMaterialDescriptor("missing"){TechniqueEnum=MyMeshDrawTechnique.MESH};missing.Textures["ColorMetalTexture"]=Path.Combine(output,"missing.dds");
        headerCalls=0;var missingProducer=new StockOldMaterialDescriptions(_=>{headerCalls++;return null;});var missingIdentity=missingProducer.Describe(model,new[]{missing},true)[0];
        // Stock LoadFromFile caches default params on failure; its subsequent
        // cache-hit branch reports success, including zero mips/Unknown format.
        check(missingIdentity.Color==ArrayName("ColorMetal",0,0,0,"Unknown"),"failed header cache hit retains stock zero-sized/Unknown identity semantics");
        check(missingIdentity.Normal==ArrayName("NormalGloss",1024,1024,11,"BC7_UNorm")&&missingIdentity.Extensions==ArrayName("Extension",1024,1024,11,"BC7_UNorm_SRgb"),"missing/empty channels retain source fallback dimensions and mip counts");
        check(headerCalls==1&&missingProducer.Describe(model,new[]{missing},true)[0].RepresentationKey==missingIdentity.RepresentationKey,"failed header metadata remains sticky without repeated disk work");
        register(missingIdentity,"source-compatible failed-header representation");

        var parameters=stock.GetType("VRage.Render11.Resources.MyFileTextureParamsManager",true)!;
        object cache=parameters.GetField("m_dictCached",all)!.GetValue(null)!;Type entry=cache.GetType().GetGenericArguments()[1];string cachedMissing=Path.GetFullPath(Path.Combine(output,"preseeded-missing.dds"));
        cache.GetType().GetMethod("TryAdd")!.Invoke(cache,new[]{(object)cachedMissing,Activator.CreateInstance(entry)!});
        before=footprint();object[] args={cachedMissing,Activator.CreateInstance(entry)!,false};bool cachedResult=(bool)parameters.GetMethod("LoadFromFile",all)!.Invoke(null,args)!;
        check(cachedResult&&(int)entry.GetField("MipLevels")!.GetValue(args[1])! ==0,"actual stock cached-default lookup confirms missing-header success behavior");
        check(footprint()==before,"cached-default reference probe leaves material/proxy/resource queues untouched");
    }
}
