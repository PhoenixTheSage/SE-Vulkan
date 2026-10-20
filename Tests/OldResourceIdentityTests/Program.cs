using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using ClientPlugin.Rendering.Content;
using VRage.Utils;
using VRageRender.Import;

static partial class Program
{
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{string path=Path.Combine(bin,new AssemblyName(e.Name).Name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;};return Run(bin);}
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(string bin)
    {
        const BindingFlags all=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        int checks=0;void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        var assembly=Assembly.LoadFrom(Path.Combine(bin,"VRage.Render11.dll"));
        Type materials=assembly.GetType("VRageRender.MyMeshMaterials1",true)!,infoType=assembly.GetType("VRageRender.MyMeshMaterialInfo",true)!,proxies=assembly.GetType("VRageRender.MyMaterials1",true)!;
        var register=materials.GetMethods(all).Single(m=>m.Name=="GetMaterialId"&&m.GetParameters()[0].ParameterType.IsByRef);
        var calculate=materials.GetMethod("CalculateRK",all)!;
        var byName=materials.GetMethods(all).Single(m=>m.Name=="GetMaterialId"&&m.GetParameters().Length==1);
        var nullImport=materials.GetMethods(all).Single(m=>m.Name=="GetMaterialId"&&m.GetParameters().Length==3&&m.GetParameters()[0].ParameterType==typeof(MyMaterialDescriptor));
        var owner=new StockOldMaterialIdentities();
        int Index(object value)=>(int)value.GetType().GetField("Index",all)!.GetValue(value)!;
        object Info(StockOldMaterialIdentity material){object value=Activator.CreateInstance(infoType)!;
            foreach(var pair in new[]{("ColorMetal_Texture",material.Color),("NormalGloss_Texture",material.Normal),("Extensions_Texture",material.Extensions),("Alphamask_Texture",material.Alpha)})infoType.GetField(pair.Item1,all)!.SetValue(value,pair.Item2);
            infoType.GetField("Name",all)!.SetValue(value,MyStringId.GetOrCompute(material.Name));infoType.GetField("Technique",all)!.SetValue(value,material.Technique);return value;}
        (int Materials,int Proxies,int Queries) Footprint(){object pool=materials.GetField("MaterialsPool",all)!.GetValue(null)!,proxy=proxies.GetField("ProxyPool",all)!.GetValue(null)!;return ((int)pool.GetType().GetProperty("Size")!.GetValue(pool)!,(int)proxy.GetType().GetProperty("Size")!.GetValue(proxy)!,((IList)materials.GetField("MaterialQueryResourcesTable",all)!.GetValue(null)!).Count);}
        void ResetStock(){materials.GetMethod("OnDeviceEnd",all)!.Invoke(null,null);proxies.GetMethod("OnDeviceEnd",all)!.Invoke(null,null);}
        int Register(StockOldMaterialIdentity material,string label){object info=Info(material);int rk=(int)calculate.Invoke(null,new[]{info})!;
            Check(rk==material.RepresentationKey,"representation key matches actual stock: "+label);
            var before=Footprint();int actual=owner.GetOrCreate(material);Check(Footprint()==before,"production identity registration leaves stock pools/resource queue untouched: "+label);
            int expected=Index(register.Invoke(null,new object[]{info,null!})!);Check(actual==expected,"allocated identity matches actual stock allocator: "+label);
            Check(owner.GetByName(material.Name)==Index(byName.Invoke(null,new object[]{material.Name})!),"name lookup matches actual stock: "+label);return actual;}
        void Common(){var before=Footprint();owner.RegisterCommonMaterials();Check(Footprint()==before,"common identity registration is CPU-owned");materials.GetMethod("Init",all)!.Invoke(null,null);
            Check(owner.NullMaterial==Index(materials.GetField("NullMaterialId",all)!.GetValue(null)!)&&owner.DebugMaterial==Index(materials.GetField("DebugMaterialId",all)!.GetValue(null)!),"common aliases preserve stock bootstrap order");}
        ResetStock();Check(Footprint()==(0,0,0),"isolated stock oracle starts with empty CPU tables");
        Check(owner.GetOrCreate(null!)==Index(nullImport.Invoke(null,new object[]{null!,null!,null!})!),"uninitialized null descriptor retains stock default identity");
        Register(new("builtin-cone","cone.dds","","","",MyMeshDrawTechnique.MESH),"pre-common mesh material");
        Register(new("builtin-sphere","sphere.dds","","","",MyMeshDrawTechnique.MESH),"second pre-common mesh material");Common();
        Check(owner.NullMaterial==2&&owner.DebugMaterial==3,"common identities are not incorrectly hardcoded to zero and one");
        Check(owner.GetOrCreate(null!)==Index(nullImport.Invoke(null,new object[]{null!,null!,null!})!),"null import follows current common alias");
        Common();Check(owner.Count==4,"repeated common registration reuses identities");
        var first=new StockOldMaterialIdentity("reused","color","normal","ext","alpha",MyMeshDrawTechnique.DECAL_CUTOUT);
        int firstId=Register(first,"first descriptor");Check(Register(first,"same descriptor")==firstId,"identical representation reuses its original identity");
        Register(new("reused","new-color","normal","ext","alpha",MyMeshDrawTechnique.DECAL_CUTOUT),"same name/new textures");
        Register(first,"returning older representation");
        Check(owner.GetByName("unknown-name")==Index(byName.Invoke(null,new object[]{"unknown-name"})!)&&owner.GetByName("unknown-name")==0,"missing name retains default index instead of common null alias");
        Register(new("array-material","ColorMetal-32x32,1:R8G8B8A8_UNorm_SRgb.dds","NormalGloss-32x32,1:BC7_UNorm.dds","Extension-32x32,1:BC7_UNorm_SRgb.dds","Alphamask-32x32,1:BC4_UNorm.dds",MyMeshDrawTechnique.DECAL),"post-array representation names");
        foreach(var technique in Enum.GetValues(typeof(MyMeshDrawTechnique)).Cast<MyMeshDrawTechnique>())Register(new("technique","color","normal","ext","alpha",technique),"technique "+technique);
        for(int n=0;n<270;n++)Register(new("material-"+n,"shared-color","shared-normal","shared-ext","shared-alpha",MyMeshDrawTechnique.MESH),"wrap "+n);

        // Use a fixed, dispersed name sequence rather than consecutive decimal
        // suffixes (the latter can be collision-free in Framework's hash).
        // Keep a valid byte-backed technique; compare actual stock RK below.
        var random=new Random(20261020);var seen=new Dictionary<int,StockOldMaterialIdentity>();StockOldMaterialIdentity collisionA=null!,collisionB=null!;
        for(int n=0;n<500000;n++){var chars=new char[12];for(int c=0;c<chars.Length;c++)chars[c]=(char)('a'+random.Next(26));var candidate=new StockOldMaterialIdentity(new string(chars),"","","","",MyMeshDrawTechnique.MESH);
            if(seen.TryGetValue(candidate.RepresentationKey,out var prior)){if(prior.Name!=candidate.Name){collisionA=prior;collisionB=candidate;break;}}else seen.Add(candidate.RepresentationKey,candidate);}
        Check(collisionA!=null&&collisionB!=null&&collisionA.RepresentationKey==collisionB.RepresentationKey,"seeded name search finds a genuine process-local RK collision");int collisionId=Register(collisionA,"collision A");
        Check(Register(collisionB,"collision B")==collisionId,"distinct colliding names preserve stock hash-only identity aliasing");

        var helper=typeof(MyStringId).Assembly.GetType("VRage.Library.Utils.My64BitValueHelper")??AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("VRage.Library.Utils.My64BitValueHelper")).FirstOrDefault(t=>t!=null);
        if(helper==null)helper=AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=>a.GetTypes().Where(t=>t.Name=="My64BitValueHelper")).Single();
        var setBits=helper.GetMethods(all).Single(m=>m.Name=="SetBits"&&m.GetParameters().Length==4&&m.GetParameters()[0].ParameterType==typeof(ulong).MakeByRefType());
        foreach(int index in new[]{0,1,63,64,127,128,255,256,-1})foreach(uint flags in new uint[]{0,1,16384,0x1ffffff,0xffffffff}){
            ulong expected=0;void Bits(int offset,int count,ulong value){object[] a={expected,offset,count,value};setBits.Invoke(null,a);expected=(ulong)a[0];}
            Bits(50,1,1);Bits(45,5,47);Bits(20,25,flags);Bits(14,6,unchecked((ulong)index));Bits(7,7,unchecked((ulong)(index+1)));Bits(0,7,unchecked((ulong)(index+2)));
            Check(StockOldSortKey.Pack(true,47,flags,index,index+1,index+2)==expected,"full sort-key masks match actual stock SetBits: "+index+"/"+flags);
        }
        Check(StockOldSortKey.Pack(true,15,0,12,23,34)==1653665488374690ul&&StockOldSortKey.Pack(true,15,0,12,24,35)==1653665488374819ul,"codec reproduces actual live A/B CullProxy keys");
        Type shaderMaterials=assembly.GetType("VRageRender.MyMaterialShaders",true)!;var techniqueKey=shaderMaterials.GetMethod("MapTechniqueToSortKey",all)!;var isDecal=shaderMaterials.GetMethod("IsDecal",all)!;
        foreach(MyMeshDrawTechnique technique in Enum.GetValues(typeof(MyMeshDrawTechnique))){if(technique==MyMeshDrawTechnique.COUNT)continue;
            Check(StockOldSortKey.Technique(technique)==(int)techniqueKey.Invoke(null,new object[]{technique})!,"technique order matches installed stock: "+technique);
            Check(StockOldSortKey.IsDecal(technique)==(bool)isDecal.Invoke(null,new object[]{technique})!,"decal classification matches installed stock: "+technique);
        }
        ResourceIndexChecks(assembly,Check);
        VertexLayoutChecks(assembly,Check);
        TransientIdentityChecks(Check);
        MaterialDescriptionChecks(bin,assembly,Check,Footprint,Register);
        PartIdentityChecks(assembly,Check);
        FileIdentityChecks(bin,Check);
        SharedOwnerIdentityChecks(Check);
        owner.ResetDevice();ResetStock();Check(owner.Count==0&&Footprint()==(0,0,0),"device reset clears identity ownership");Common();Check(owner.NullMaterial==0&&owner.DebugMaterial==1,"fresh device common identity allocation restarts correctly");
        Console.WriteLine(checks+" OLD resource identity CPU checks passed.");return 0;
    }
}
