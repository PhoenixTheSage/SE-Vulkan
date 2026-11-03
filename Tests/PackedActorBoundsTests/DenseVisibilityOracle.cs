using ClientPlugin.Rendering.Scene;
using VRageMath;

static class DenseVisibilityOracle
{
    sealed class Actor
    {
        public uint Id,Flags;public bool Owned,Destroyed,Removed;
        public BoundingBoxD Bounds;public PackedActorBounds.Lease Lease;public DenseGpuVisibility.Registration Metadata;
    }
    public static long Run()
    {
        long checks=0;void Check(bool value,string message){checks++;if(!value)throw new Exception(message+" check="+checks);}
        var bounds=new PackedActorBounds(3);var dense=new DenseGpuVisibility();
        var registered=new HashSet<Actor>();var actors=new List<Actor>();var random=new Random(692890);uint nextId=0;
        var frustum=new BoundingFrustumD(MatrixD.CreatePerspectiveFieldOfView(1.2,1.7,.1,1000));var prepared=new FrustumVisibility(frustum);
        void Write(Actor a){for(int s=0;s<3;s++)bounds.Write(s,a.Lease,in a.Bounds,a.Destroyed);}
        Actor Create(){var a=new Actor{Id=++nextId,Lease=bounds.Rent(),Bounds=new BoundingBoxD(new Vector3D(-1,-1,-10),new Vector3D(1,1,-8))};a.Metadata=dense.Create(a,a.Id,a.Lease.Index,a.Lease.Token,a.Flags);actors.Add(a);Write(a);return a;}
        void Flags(Actor a,uint value){a.Flags=value;a.Metadata.SetFlags(value);}
        void Owned(Actor a,bool value){a.Owned=value;a.Metadata.SetOwned(value);}
        void Register(Actor a){if(a.Removed)return;registered.Add(a);a.Metadata.SetRegistered(true);}
        void Remove(Actor a){registered.Remove(a);a.Metadata.Remove();bounds.Release(a.Lease);a.Removed=true;actors.Remove(a);}
        void Publish(int slot)
        {
            // Independent old-loop oracle, including exactly when its HashSet
            // registration disappears. Metadata callbacks do not update this set.
            var expected=new HashSet<uint>();var stale=new HashSet<Actor>();
            foreach(var a in registered){if(a.Destroyed||!a.Owned)stale.Add(a);else if((a.Flags&1)==0&&frustum.Contains(a.Bounds)!=ContainmentType.Disjoint)expected.Add(a.Id);}
            foreach(var a in stale)registered.Remove(a);
            while(dense.TryPruneUnowned(out object owner)){var a=(Actor)owner;Check(!a.Owned&&stale.Contains(a),"compact pending cleanup pruned a cancelled transition");}
            var actual=new HashSet<uint>();var destroyed=new List<Actor>();var active=dense.Active;int count=dense.Count;
            for(int i=0;i<count;i++)
            {
                ref readonly var row=ref active[i];var a=(Actor)row.Handle.Owner;
                Check(row.Id==a.Id&&row.Flags==a.Flags&&row.BoundsIndex==a.Lease.Index&&row.BoundsToken==a.Lease.Token,"compact metadata differs from current backing values");
                Check(a.Owned&&!a.Removed&&(registered.Contains(a)||a.Destroyed&&stale.Contains(a)),"dense row is not a current registered owned actor");
                ref readonly var snapshot=ref PackedActorBounds.Read(bounds.Frame(slot),row.BoundsIndex,row.BoundsToken);
                if(snapshot.Destroyed)destroyed.Add(a);else if((row.Flags&1)==0&&prepared.Intersects(in snapshot.Bounds))actual.Add(row.Id);
            }
            foreach(var a in destroyed)a.Metadata.SetRegistered(false);
            Check(expected.SetEquals(actual),"dense publication differs from independent original HashSet traversal");
            Check(dense.Count==registered.Count(a=>a.Owned),"dense population differs after original stale cleanup");
            for(int i=dense.Count;i<dense.Active.Length;i++)Check(dense.Active[i].Handle==null,"inactive metadata tail retains an actor registration");
        }
        var a=Create();Owned(a,true);Publish(0);Check(dense.Count==0,"Owned=true introduced an unregistered actor");
        Register(a);Publish(1);Check(dense.Count==1,"registration did not activate current owned actor");
        Owned(a,false);Owned(a,true);Publish(2);Check(registered.Contains(a)&&dense.Count==1,"false->true before cleanup lost registration");
        Owned(a,false);Publish(0);Check(!registered.Contains(a)&&dense.Count==0,"observed false did not prune registration");
        Owned(a,true);Publish(1);Check(dense.Count==0,"Owned=true resurrected a pruned registration");
        Register(a);Flags(a,1);Publish(2);Flags(a,0);Publish(0); // No bounds writes between flags changes.
        a.Destroyed=true;Write(a);Publish(1);Check(!registered.Contains(a)&&dense.Count==0,"destroyed row kept registration");
        a.Destroyed=false;Write(a);Publish(2);Check(dense.Count==0,"bounds recovery silently re-registered a pruned actor");Register(a);
        for(int i=0;i<160;i++){var actor=Create();Owned(actor,true);Register(actor);}
        for(int frame=0;frame<240;frame++)
        {
            for(int change=0;change<35;change++)
            {
                var actor=actors[random.Next(actors.Count)];
                switch(random.Next(7))
                {
                    case 0:Flags(actor,actor.Flags^1u);break; // Deliberately no bounds dirty event.
                    case 1:Owned(actor,!actor.Owned);break;
                    case 2:Owned(actor,false);Owned(actor,true);break;
                    case 3:Register(actor);break;
                    case 4:actor.Destroyed=!actor.Destroyed;Write(actor);break;
                    case 5:actor.Bounds=new BoundingBoxD(new Vector3D(random.Next(-20,20),-1,-10),new Vector3D(random.Next(21,40),1,-8));Write(actor);break;
                    case 6:Remove(actor);var replacement=Create();Owned(replacement,random.Next(2)==0);Register(replacement);break;
                }
            }
            Publish(frame%3);
        }
        var retired=actors[0];int index=retired.Lease.Index;Remove(retired);var fresh=Create();
        Check(fresh.Lease.Index==index,"identity test did not exercise bounds index reuse");Owned(fresh,true);Register(fresh);
        Owned(retired,true);Flags(retired,0);retired.Metadata.SetRegistered(true);Publish(0);
        var transient=Create();Owned(transient,true);Register(transient);Owned(transient,false);Check(dense.PendingCount==1,"false ownership did not queue cleanup");
        transient.Metadata.SetRegistered(false);registered.Remove(transient);Check(dense.PendingCount==0,"explicit unregistration retains a pending actor");
        Register(transient);Owned(transient,true);Check(dense.PendingCount==0,"cancelled cleanup retains a pending actor");
        Owned(transient,false);Remove(transient);Check(dense.PendingCount==0,"explicit removal retains a pending actor");
        var oldInactive=Create();oldInactive.Metadata.SetRegistered(true);var oldActive=Create();Owned(oldActive,true);Register(oldActive);
        dense.Clear();registered.Clear();bounds.Clear();actors.Clear();
        Owned(oldInactive,true);oldInactive.Metadata.SetRegistered(true);Flags(oldInactive,1);Flags(oldActive,1);Check(dense.Count==0&&dense.PendingCount==0,"old-world callbacks reactivated or queued actors after clear");
        var next=Create();Owned(next,true);Register(next);Flags(next,1);Flags(oldActive,0);Check(dense.Active[0].Flags==1,"old-world flags callback overwrote a current row");Flags(next,0);Publish(1);
        return checks;
    }
}
