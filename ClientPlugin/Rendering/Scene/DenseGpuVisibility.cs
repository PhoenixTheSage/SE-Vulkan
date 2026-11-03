using System;
using System.Collections.Generic;

namespace ClientPlugin.Rendering.Scene;

// Registration and current Owned are separate: setting Owned alone cannot
// introduce an actor. Build changes active membership; Apply creates only
// inactive registrations. Normal visibility reads only the compact Row array.
internal sealed class DenseGpuVisibility
{
    internal sealed class Registration
    {
        readonly DenseGpuVisibility registry;
        public readonly object Owner;public readonly uint Id;public readonly int BoundsIndex;public readonly long BoundsToken;
        internal readonly long Epoch;internal uint Flags;internal bool Owned,Registered,Removed;internal int ActiveIndex=-1,PendingIndex=-1;
        internal Registration(DenseGpuVisibility registry,object owner,uint id,int boundsIndex,long boundsToken,uint flags){this.registry=registry;Epoch=registry.epoch;Owner=owner;Id=id;BoundsIndex=boundsIndex;BoundsToken=boundsToken;Flags=flags;}
        public void SetFlags(uint flags)=>registry.SetFlags(this,flags);
        public void SetOwned(bool owned)=>registry.SetOwned(this,owned);
        public void SetRegistered(bool registered)=>registry.SetRegistered(this,registered);
        public void Remove(){registry.SetRegistered(this,false);Removed=true;}
    }
    internal struct Row
    {
        public readonly uint Id;public uint Flags;public readonly int BoundsIndex;public readonly long BoundsToken;public readonly Registration Handle;
        internal Row(Registration entry){Id=entry.Id;Flags=entry.Flags;BoundsIndex=entry.BoundsIndex;BoundsToken=entry.BoundsToken;Handle=entry;}
    }
    Row[] active=Array.Empty<Row>();int count;long epoch;
    readonly List<Registration> pending=new();
    public Row[] Active=>active;public int Count=>count;internal int PendingCount=>pending.Count;
    public Registration Create(object owner,uint id,int boundsIndex,long boundsToken,uint flags)=>new(this,owner,id,boundsIndex,boundsToken,flags);
    void SetFlags(Registration entry,uint flags){entry.Flags=flags;if(entry.Epoch==epoch&&entry.ActiveIndex>=0)active[entry.ActiveIndex].Flags=flags;}
    void SetOwned(Registration entry,bool owned)
    {
        if(entry.Owned==owned)return;entry.Owned=owned;if(entry.Epoch!=epoch)return;Refresh(entry);
        if(owned)Unqueue(entry);else if(entry.Registered)Queue(entry);
    }
    void SetRegistered(Registration entry,bool registered)
    {
        if(entry.Epoch!=epoch||entry.Removed&&registered)return;entry.Registered=registered;Refresh(entry);
        if(!registered)Unqueue(entry);else if(!entry.Owned)Queue(entry);
    }
    void Queue(Registration entry){if(entry.PendingIndex>=0)return;entry.PendingIndex=pending.Count;pending.Add(entry);}
    void Unqueue(Registration entry)
    {
        int index=entry.PendingIndex;if(index<0)return;int last=pending.Count-1;entry.PendingIndex=-1;
        if(index!=last){pending[index]=pending[last];pending[index].PendingIndex=index;}pending.RemoveAt(last);
    }
    void Refresh(Registration entry)
    {
        bool wanted=entry.Registered&&entry.Owned&&!entry.Removed;
        if(wanted&&entry.ActiveIndex<0){if(count==active.Length)Array.Resize(ref active,Math.Max(64,active.Length*2));entry.ActiveIndex=count;active[count++]=new Row(entry);}
        else if(!wanted&&entry.ActiveIndex>=0){int index=entry.ActiveIndex,last=--count;entry.ActiveIndex=-1;if(index!=last){active[index]=active[last];active[index].Handle.ActiveIndex=index;}active[last]=default;}
    }
    // Recheck at publication: false->true before the old loop's observation
    // must keep its registration. False observed at publication prunes it.
    public bool TryPruneUnowned(out object owner)
    {
        while(pending.Count!=0){var entry=pending[pending.Count-1];Unqueue(entry);
            if(entry.Registered&&!entry.Owned){SetRegistered(entry,false);owner=entry.Owner;return true;}}
        owner=null;return false;
    }
    public void Clear(){if(epoch==long.MaxValue)throw new InvalidOperationException("Dense visibility epoch exhausted");epoch++;for(int i=0;i<count;i++){active[i].Handle.Registered=false;active[i].Handle.ActiveIndex=-1;active[i]=default;}count=0;foreach(var entry in pending)entry.PendingIndex=-1;pending.Clear();}
}
