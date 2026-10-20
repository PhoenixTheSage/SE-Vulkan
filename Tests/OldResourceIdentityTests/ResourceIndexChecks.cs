using System.Reflection;
using ClientPlugin.Rendering.Content;

static partial class Program
{
    static void ResourceIndexChecks(Assembly stock,Action<bool,string> check)
    {
        const BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        Type type=stock.GetType("VRageRender.MyFreelist`1",true)!.MakeGenericType(typeof(int));object reference=Activator.CreateInstance(type,new object[]{4,0})!;
        var allocate=type.GetMethod("Allocate",all)!;var free=type.GetMethod("Free",all)!;var clear=type.GetMethod("Clear",all)!;var size=type.GetProperty("Size",all)!;
        var owner=new StockOldResourceIndices();var live=new List<int>();
        int Allocate(){int expected=(int)allocate.Invoke(reference,null)!;int actual=owner.Allocate();check(actual==expected&&owner.Count==(int)size.GetValue(reference)!,"shared LOD index matches actual stock allocation/reuse: "+actual);live.Add(actual);return actual;}
        void Free(int index){free.Invoke(reference,new object[]{index});owner.Free(index);live.Remove(index);check(owner.Count==(int)size.GetValue(reference)!,"shared LOD release retains stock live count: "+index);}
        // Cross both growth regimes of the real freelist. A single pool trace
        // represents interleaved file LOD/runtime/voxel ownership, not actors.
        for(int n=0;n<1600;n++)Allocate();
        var random=new Random(20261020);for(int n=0;n<2500;n++){if(live.Count!=0&&random.Next(3)==0)Free(live[random.Next(live.Count)]);else Allocate();}
        foreach(int index in live.ToArray())Free(index);
        check(owner.Count==0,"shared LOD pool can retire every source without resetting allocation identity");
        for(int n=0;n<32;n++)Allocate();int released=live[7];Free(released);check(Allocate()==released,"released LOD index is reused before fresh indices");
        Free(released);bool ownRejected=false,stockRejected=false;try{owner.Free(released);}catch(ArgumentException){ownRejected=true;}try{free.Invoke(reference,new object[]{released});}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){stockRejected=true;}
        check(ownRejected&&stockRejected,"duplicate retirement rejects corruption like the stock pool");
        owner.Clear();clear.Invoke(reference,null);live.Clear();check(owner.Count==0&&((int)size.GetValue(reference)!)==0,"explicit pool clear matches stock");check(Allocate()==0,"explicit pool clear restarts index allocation");
    }
}
