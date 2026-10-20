using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using ClientPlugin.Rendering.Scene;
using VRage.Render.Scene;
using VRage.Render.Scene.Components;
using VRageMath;

class Program
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,name)=>{string path=Path.Combine(bin,name.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};try{return Run(bin);}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    [MethodImpl(MethodImplOptions.NoInlining)] static int Run(string bin)
    {
        var assembly=Assembly.LoadFrom(Path.Combine(bin,"VRage.Render11.dll"));
        var lightType=assembly.GetType("VRage.Render11.Scene.Components.MyLightComponent",true);
        var queryType=assembly.GetType("VRage.Render11.Culling.MyCullQuery",true);
        var renderer=assembly.GetType("VRage.Render11.LightingStage.MyLightsRendering",true);
        var pointMethod=renderer.GetMethod("CullPointLights",Fields);var spotMethod=renderer.GetMethod("CullSpotLights",Fields);
        var budgetField=renderer.GetField("m_maxPointLights",Fields);
        var scene=new FixtureScene();var random=new Random(20261018);
        var pool=new List<MyLightComponent>();var ids=new Dictionary<MyLightComponent,int>();
        for(int i=0;i<5000;i++){
            var actor=new MyActor(scene);actor.Construct("selection input "+i);
            var component=(MyLightComponent)Activator.CreateInstance(lightType,true);component.Construct();actor.AddComponent<MyLightComponent>(component);
            ids.Add(component,i);pool.Add(component);
        }
        object Value(object instance,string field)=>instance.GetType().GetField(field,Fields).GetValue(instance);
        int[] Reference(List<MyLightComponent> candidates,List<StockLightSelection.Group> groups,int capacity,bool spots){
            var query=Activator.CreateInstance(queryType,true);var results=Value(query,"Results");
            object list=Value(results,spots?"SpotLights":"PointLights");var add=list.GetType().GetMethod("Add",new[]{lightType});
            foreach(var candidate in candidates)add.Invoke(list,new object[]{candidate});
            if(!spots){
                var inputGroups=(IList)Value(query,"Groups");var groupInfo=(IList)Value(query,"PointLightsInfo");
                Type infoType=groupInfo.GetType().GetGenericArguments()[0];
                for(int i=0;i<groups.Count;i++){
                    var group=groups[i];var actor=new MyActor(scene);actor.Construct("group input");
                    // The selector's only group input is distance to its world AABB.
                    // Seed that state directly; this is not a discovery/culling test.
                    double x=Math.Sqrt(group.Distance);
                    typeof(MyActor).GetField("m_aabb",Fields).SetValue(actor,new BoundingBoxD(new Vector3D(x,0,0),new Vector3D(x+1,1,1)));
                    inputGroups.Add(new MyManualCullTreeData{Actor=actor});object info=Activator.CreateInstance(infoType);
                    infoType.GetField("Start",Fields).SetValue(info,group.Start);infoType.GetField("Count",Fields).SetValue(info,group.Count);infoType.GetField("GroupsIndex",Fields).SetValue(info,i);groupInfo.Add(info);
                }
                budgetField.SetValue(null,capacity);
            }
            (spots?spotMethod:pointMethod).Invoke(null,new[]{query});
            return ((IEnumerable)Value(results,spots?"SpotLights":"PointLights")).Cast<MyLightComponent>().Select(item=>ids[item]).ToArray();
        }
        int checks=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
        List<MyLightComponent> Inputs(int count,bool ties){
            var result=pool.GetRange(0,count);
            for(int i=count-1;i>0;i--){int j=random.Next(i+1);(result[i],result[j])=(result[j],result[i]);}
            foreach(var item in result){double x=ties?random.Next(8):random.NextDouble()*200;var matrix=MatrixD.CreateTranslation(x,random.NextDouble()*.001,0);item.Owner.SetMatrix(ref matrix);}
            return result;
        }
        foreach(int capacity in new[]{4,256,512,2048,4096})foreach(bool ties in new[]{false,true}){
            foreach(int count in new[]{capacity-1,capacity,capacity+17}){
                var input=Inputs(count,ties);var groups=new List<StockLightSelection.Group>();
                var actual=StockLightSelection.Points(input,groups,capacity,item=>item.Owner.CalculateCameraDistanceSquaredFast()).Select(item=>ids[item]).ToArray();
                Check(actual.SequenceEqual(Reference(input,groups,capacity,false)),"root point ordering differs from installed method");
            }
            for(int variant=0;variant<12;variant++){
                int count=Math.Min(4999,capacity+17+random.Next(80));var input=Inputs(count,ties);
                int roots=variant%3==0?0:Math.Min(capacity/2,random.Next(count));var groups=new List<StockLightSelection.Group>();
                for(int start=roots;start<count;){int length=Math.Min(count-start,1+random.Next(Math.Max(2,capacity)));float distance=(float)Math.Pow(random.Next(1,70),2);groups.Add(new StockLightSelection.Group(start,length,distance));start+=length;}
                var actual=StockLightSelection.Points(input,groups,capacity,item=>item.Owner.CalculateCameraDistanceSquaredFast()).Select(item=>ids[item]).ToArray();
                Check(actual.SequenceEqual(Reference(input,groups,capacity,false)),"grouped point selection differs from installed method; capacity="+capacity+" variant="+variant);
            }
        }
        foreach(int count in new[]{0,1,31,32,33,64,257})foreach(bool ties in new[]{false,true}){
            var input=Inputs(count,ties);var actual=StockLightSelection.Spots(input,item=>item.Owner.CalculateCameraDistanceSquaredFast()).Select(item=>ids[item]);
            Check(actual.SequenceEqual(Reference(input,new List<StockLightSelection.Group>(),0,true)),"spot ordering differs from installed method");
        }
        string output=Path.GetFullPath("TestResults/LightSelectionTests");Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,StockAssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),Scope="Exact ordered selected IDs versus installed private DX11 methods on identical synthetic query inputs; excludes spatial discovery, GPU and live scene appearance."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(checks+" installed-method light selection checks passed.");return 0;
    }
    sealed class FixtureScene:MyScene
    {
        public FixtureScene():base(null,null,null,new MyDynamicAABBTreeD(Vector3D.Zero),new MyDynamicAABBTreeD(Vector3D.Zero),new MyDynamicAABBTreeD(Vector3D.Zero),new MyDynamicAABBTreeD(Vector3D.Zero)){}
        public override float FadeOutTime=>0;
        public override MyManualCullTreeData AllocateGroupData()=>throw new NotSupportedException();
        public override void FreeGroupData(MyManualCullTreeData value)=>throw new NotSupportedException();
        public override MyChildCullTreeData CompileCullData(MyChildCullTreeData value)=>value;
        public override void SetActorParent(IMyActor parent,IMyActor child,Matrix? relative)=>throw new NotSupportedException();
    }
}
