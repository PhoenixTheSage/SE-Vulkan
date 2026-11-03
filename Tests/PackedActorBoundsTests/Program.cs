using ClientPlugin.Rendering.Scene;
using VRageMath;

long checks=0;void Check(bool value,string message){checks++;if(!value)throw new Exception(message+" check="+checks);}
BoundingBoxD Box(double x,double y,double z)=>new(new Vector3D(x,y,z),new Vector3D(x+1,y+2,z+3));
bool Same(in BoundingBoxD a,in BoundingBoxD b)=>a.Min.X.Equals(b.Min.X)&&a.Min.Y.Equals(b.Min.Y)&&a.Min.Z.Equals(b.Min.Z)&&a.Max.X.Equals(b.Max.X)&&a.Max.Y.Equals(b.Max.Y)&&a.Max.Z.Equals(b.Max.Z);
var store=new PackedActorBounds(3);var random=new Random(630567);
var active=new List<Actor>();var current=new HashSet<Actor>();var previous=new HashSet<Actor>();var earlier=new HashSet<Actor>();
int nextOwner=0;
Actor Add(){var actor=new Actor{Owner=++nextOwner,Lease=store.Rent(),Bounds=Box(random.Next(-100,100),random.Next(-100,100),random.Next(-300,-1))};active.Add(actor);current.Add(actor);return actor;}
for(int i=0;i<300;i++)Add();
for(int frame=0;frame<360;frame++)
{
    int slot=frame%3,oldBuild=(slot+2)%3;
    var prior=store.Frame(oldBuild).ToArray();
    // The independent oracle is each actor's full current world state. The
    // tested writes use only this frame's dirty set and its two catch-up sets.
    for(int i=0;i<45;i++)
    {
        var actor=active[random.Next(active.Count)];actor.Bounds=Box(random.Next(-1000000000,1000000000),random.Next(-100,100),random.Next(-300,-1));actor.Destroyed=random.Next(7)==0;current.Add(actor);
    }
    for(int i=0;i<5;i++)
    {
        int index=random.Next(active.Count);var actor=active[index];active.RemoveAt(index);
        // Removed references deliberately stay in both catch-up sets. Release
        // is on a Build-like worker; replacement creation remains on Apply.
        Task.Run(()=>store.Release(actor.Lease)).GetAwaiter().GetResult();
        actor.Bounds=Box(999999,999999,999999);actor.Destroyed=true;current.Add(actor);Add();
    }
    foreach(var actor in current)store.Write(slot,actor.Lease,in actor.Bounds,actor.Destroyed);
    foreach(var actor in previous)if(!current.Contains(actor))store.Write(slot,actor.Lease,in actor.Bounds,actor.Destroyed);
    foreach(var actor in earlier)if(!current.Contains(actor)&&!previous.Contains(actor))store.Write(slot,actor.Lease,in actor.Bounds,actor.Destroyed);
    var view=new BoundingFrustumD(MatrixD.CreatePerspectiveFieldOfView(1.1,1.7,.1,1000));var prepared=new FrustumVisibility(view);
    var expectedIds=new HashSet<int>();var actualIds=new HashSet<int>();
    foreach(var actor in active)
    {
        ref readonly var row=ref PackedActorBounds.Read(store.Frame(slot),actor.Lease.Index,actor.Lease.Token);
        Check(row.Destroyed==actor.Destroyed&&Same(in row.Bounds,in actor.Bounds),"dirty/catch-up row differs from full current-world oracle");
        // These eligibility fields stay live and are deliberately never stored
        // in the packed snapshot, matching the renderer's Flags/GpuOwned guard.
        actor.Owned=random.Next(8)!=0;actor.SkipMain=random.Next(9)==0;
        if(!actor.Destroyed&&actor.Owned&&!actor.SkipMain&&view.Contains(actor.Bounds)!=ContainmentType.Disjoint)expectedIds.Add(actor.Owner);
        if(!row.Destroyed&&actor.Owned&&!actor.SkipMain&&prepared.Intersects(in row.Bounds))actualIds.Add(actor.Owner);
    }
    Check(expectedIds.SetEquals(actualIds),"packed game-visible set differs from full current snapshot oracle");
    var old=store.Frame(oldBuild);Check(prior.Length==old.Length,"Apply resized the Build slot");
    for(int i=0;i<prior.Length;i++)Check(prior[i].Token==old[i].Token&&prior[i].Destroyed==old[i].Destroyed&&Same(in prior[i].Bounds,in old[i].Bounds),"Apply changed an in-flight Build row");
    earlier.Clear();(earlier,previous,current)=(previous,current,earlier);
}
// Reuse a row while obsolete dirty references are still present. Writing the
// replacement first makes a missing returned-lease guard fail deterministically.
var retired=active[0].Lease;store.Release(retired);store.Release(retired);
var replacement=store.Rent();var distinct=store.Rent();
Check(replacement.Index==retired.Index,"the stale catch-up test did not exercise immediate index reuse");
Check(replacement.Index!=distinct.Index,"double release made the same index available twice");
var wanted=Box(1,2,3);var obsolete=Box(9,9,9);
for(int slot=0;slot<3;slot++){store.Write(slot,replacement,in wanted,false);store.Write(slot,retired,in obsolete,true);ref readonly var row=ref PackedActorBounds.Read(store.Frame(slot),replacement.Index,replacement.Token);Check(Same(in row.Bounds,in wanted)&&!row.Destroyed,"old catch-up reference overwrote a reused row");}
bool rejected=false;try{PackedActorBounds.Read(store.Frame(0),replacement.Index,retired.Token);}catch(InvalidOperationException){rejected=true;}Check(rejected,"wrong owner token read succeeded");
// Unload invalidates even an unreleased old lease, including late release and
// late catch-up writes after the next world has reused index zero.
var oldWorld=store.Rent();store.Clear();var newWorld=store.Rent();
for(int slot=0;slot<3;slot++){store.Write(slot,newWorld,in wanted,false);store.Write(slot,oldWorld,in obsolete,true);ref readonly var row=ref PackedActorBounds.Read(store.Frame(slot),newWorld.Index,newWorld.Token);Check(Same(in row.Bounds,in wanted),"prior world overwrote a new-world row");}
store.Release(oldWorld);var another=store.Rent();Check(another.Index!=newWorld.Index,"late old-world release freed a live new-world row");
Console.WriteLine($"PASS {checks} packed actor bounds checks: 360 current-world oracle frames, three-slot dirty/catch-up rotation, concurrent release, immediate reuse, stale references, eligibility sets, wrong-owner rejection, unload/reload");
Console.WriteLine($"PASS {DenseVisibilityOracle.Run()} dense visibility checks: live property callbacks, flags without bounds dirty, registration versus ownership, cancelled stale cleanup, destruction, swap removal, index reuse and world epochs");

sealed class Actor
{
    public int Owner;public PackedActorBounds.Lease Lease;public BoundingBoxD Bounds;public bool Destroyed,Owned,SkipMain;
}
