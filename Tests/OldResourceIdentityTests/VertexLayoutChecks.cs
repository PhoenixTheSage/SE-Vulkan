using System.Reflection;
using ClientPlugin.Rendering.Content;
using VRageRender;

static partial class Program
{
    static void VertexLayoutChecks(Assembly stock,Action<bool,string> check)
    {
        const BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        // Framework LoadFrom can load a second copy beside the compile-time
        // reference. ValueType hashes include the runtime type identity. Use
        // the exact same installed image in the production type's load context.
        Assembly loaded=typeof(MyRenderSettings1Extensions).Assembly;
        using(var hash=System.Security.Cryptography.SHA256.Create())check(hash.ComputeHash(File.ReadAllBytes(stock.Location)).SequenceEqual(hash.ComputeHash(File.ReadAllBytes(loaded.Location))),"layout oracle uses the exact installed stock assembly image");
        stock=loaded;
        Type layouts=stock.GetType("VRageRender.MyVertexLayouts",true)!,component=stock.GetType("VRageRender.MyVertexInputComponent",true)!;
        var allocate=layouts.GetMethod("GetLayout",all,null,new[]{component.MakeArrayType()},null)!;
        object pool=layouts.GetField("Layouts",all)!.GetValue(null)!;
        int Count()=>(int)pool.GetType().GetProperty("Size")!.GetValue(pool)!;
        var owner=new StockOldVertexLayouts();
        Array Values(StockOldVertexComponent[] values){var result=Array.CreateInstance(component,values.Length);for(int i=0;i<values.Length;i++){
            object v=Activator.CreateInstance(component)!;component.GetField("Type",all)!.SetValue(v,values[i].Type);component.GetField("Slot",all)!.SetValue(v,values[i].Slot);var f=component.GetField("Freq",all)!;f.SetValue(v,Enum.ToObject(f.FieldType,values[i].PerInstance?1:0));result.SetValue(v,i);
        }return result;}
        int Index(object id)=>(int)id.GetType().GetField("Index",all)!.GetValue(id)!;
        int Register(string label,params StockOldVertexComponent[] values){int before=Count(),actual=owner.Get(values);check(Count()==before,"production layout leaves stock metadata pool untouched: "+label);
            int expected=Index(allocate.Invoke(null,new object[]{Values(values)})!);check(actual==expected&&owner.Count==Count(),"layout allocation/hash reuse matches installed stock: "+label);return actual;}
        check(Count()==1&&owner.Count==1,"layout tables start with the retained empty identity");
        Register("empty",Array.Empty<StockOldVertexComponent>());
        foreach(MyVertexInputComponentType type in Enum.GetValues(typeof(MyVertexInputComponentType)))foreach(int slot in new[]{0,1,2})foreach(bool instance in new[]{false,true}){
            var value=new StockOldVertexComponent(type,slot,instance);int actualHash=StockOldVertexLayouts.ComponentHash(value),referenceHash=Values(new[]{value}).GetValue(0)!.GetHashCode();
            check(value.Type==type&&actualHash==referenceHash,"component ValueType hash matches actual boxed stock: "+type+"/"+slot+"/"+instance);
            Register(type+"/"+slot+"/"+instance,value);
        }
        foreach(bool skin in new[]{false,true})foreach(bool streams in new[]{false,true})foreach(bool arrays in new[]{false,true}){
            var signature=new StockOldVertexLayout(skin,streams,arrays);int expected=Register("file "+skin+"/"+streams+"/"+arrays,StockOldVertexLayouts.FileComponents(signature));check(owner.File(signature)==expected,"file identity reuses full stream signature");
        }
        int file=owner.File(new(false,true,true));var instanceComponents=new[]{new StockOldVertexComponent(MyVertexInputComponentType.GENERIC_INSTANCE,2,true)};
        int instanceId=Register("generic instance buffer",instanceComponents);int combined=Register("combined file/instance",StockOldVertexLayouts.FileComponents(new(false,true,true)).Concat(instanceComponents).ToArray());
        check(owner.Combine(file,instanceId)==combined,"actor combined layout retains ordered file and instance streams");
        var independent=new StockOldVertexLayouts();independent.RegisterBeforeBuiltinMeshes();int cone=independent.File(new(false,false,false));independent.RegisterAfterBuiltinCone();int sphere=independent.File(new(false,false,false));independent.RegisterAfterBuiltinSphere(false);
        check(cone==sphere,"builtin mesh layouts reuse retained signatures");int count=independent.Count;independent.RegisterBeforeBuiltinMeshes();independent.RegisterAfterBuiltinCone();independent.RegisterAfterBuiltinSphere(false);check(independent.Count==count,"repeated device bootstrap preserves layout identities without resetting allocation");
        Console.WriteLine("LAYOUT BOOTSTRAP beforeScene="+count+" arrayFile="+independent.File(new(false,true,true)));
    }
}
