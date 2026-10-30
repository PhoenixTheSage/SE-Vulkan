using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    internal sealed class MainGroupState {internal uint Token,Order;}
    // Keys may remember the group they resolved to during the current group
    // epoch; equal keys still meet through the dictionary on first use.
    internal abstract class MainGroupKeyBase {internal MainGroupState Group;internal object Owner;internal uint Epoch;}
    internal uint MainPart {get=>E.MainPart;set=>E.MainPart=value;}
    uint MainGroupToken=>E.MainGroup?.Token??0;
    // Candidates are unique per frame, so owners are appended without lookup;
    // a group's order is fixed at its first declaration and read when queued.
    // States are reused frame to frame: keys only trust a cached group from the current epoch, and
    // packets holding a group are cleared before the next BeginMainGroups.
    void BeginMainGroups(){E.MainGroups.Clear();E.MainGroupsRented=0;E.MainGroupEpoch++;E.MainOwnerIds.Clear();E.MainOwnerGroups.Clear();E.MainGroup=null;MainPart=0;E.OldPartOrder=default;E.PacketOwner=0;}
    MainGroupState ResolveMainGroup(object key){
        var cached=key as MainGroupKeyBase;if(cached!=null&&cached.Epoch==E.MainGroupEpoch&&ReferenceEquals(cached.Owner,E))return cached.Group;
        if(!E.MainGroups.TryGetValue(key,out var group)){if(E.MainGroupsRented==E.MainGroupPool.Count)E.MainGroupPool.Add(new MainGroupState());group=E.MainGroupPool[E.MainGroupsRented++];group.Token=(uint)E.MainGroups.Count+1;group.Order=StableMainOrderEnabled?StableOrder(key):0;E.MainGroups.Add(key,group);}
        if(cached!=null){cached.Group=group;cached.Owner=E;cached.Epoch=E.MainGroupEpoch;}
        return group;
    }
    // Stable main order (default; SE_VULKAN_STABLE_MAIN_ORDER=0 restores per-frame declaration order): a group keeps the order number its key got when
    // first seen this session, so pass-owned actors need no per-frame declaration. Their orders live in
    // native as resident owner orders, updated only when an actor's pass commit changes.
    public bool StableMainOrderEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_STABLE_MAIN_ORDER")!="0";
    readonly Dictionary<object,uint> stableOrders=new();uint nextStableOrder;
    uint StableOrder(object key){if(!stableOrders.TryGetValue(key,out uint order)){if(nextStableOrder>=0x7FFFFFF0u)throw new InvalidOperationException("Stable main order space exhausted.");stableOrders.Add(key,order=++nextStableOrder);}return order;}
    readonly Dictionary<uint,uint> residentOwners=new();readonly Dictionary<uint,uint> residentSets=new();readonly HashSet<uint> residentRemovals=new();
    internal void SetResidentMainOwner(uint owner,object key){if(key==null){RemoveResidentMainOwner(owner);return;}uint order=StableOrder(key);if(residentOwners.TryGetValue(owner,out uint old)&&old==order)return;residentOwners[owner]=order;residentSets[owner]=order;residentRemovals.Remove(owner);}
    internal void RemoveResidentMainOwner(uint owner){if(!residentOwners.Remove(owner))return;residentSets.Remove(owner);residentRemovals.Add(owner);}
    internal void ClearStableMainOrders(){ClearResidentMainOwners();stableOrders.Clear();nextStableOrder=0;}
    internal void ClearResidentMainOwners(){foreach(uint owner in residentOwners.Keys)residentRemovals.Add(owner);residentOwners.Clear();residentSets.Clear();}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ResidentOwnersFn(IntPtr context,IntPtr set,uint setCount,IntPtr removed,uint removedCount);
    ResidentOwnersFn residentOwnersApi;MainOwnerOrder[] residentSetScratch=Array.Empty<MainOwnerOrder>();uint[] residentRemoveScratch=Array.Empty<uint>();
    // QueueMainOrder: the changes stage A sealed into this frame's emission (SealFrameEmission), then any made since
    // on this timeline (single thread, or a frame launched without a seal).
    unsafe void FlushResidentMainOwners(){
        var e=E;if(residentSets.Count==0&&residentRemovals.Count==0&&e.ResidentSets.Count==0&&e.ResidentRemovals.Count==0)return;
        int setTotal=e.ResidentSets.Count+residentSets.Count,removeTotal=e.ResidentRemovals.Count+residentRemovals.Count;
        if(residentSetScratch.Length<setTotal)residentSetScratch=new MainOwnerOrder[setTotal*2];if(residentRemoveScratch.Length<removeTotal)residentRemoveScratch=new uint[removeTotal*2];
        int sets=0,removals=0;foreach(var entry in e.ResidentSets)residentSetScratch[sets++]=entry;foreach(uint owner in e.ResidentRemovals)residentRemoveScratch[removals++]=owner;e.ResidentSets.Clear();e.ResidentRemovals.Clear();
        foreach(var entry in residentSets)residentSetScratch[sets++]=new MainOwnerOrder{Owner=entry.Key,Group=entry.Value};foreach(uint owner in residentRemovals)residentRemoveScratch[removals++]=owner;
        residentOwnersApi??=(ResidentOwnersFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_resident_main_owners"),typeof(ResidentOwnersFn));
        fixed(MainOwnerOrder* set=residentSetScratch)fixed(uint* removed=residentRemoveScratch)Check(residentOwnersApi(context,(IntPtr)set,(uint)sets,(IntPtr)removed,(uint)removals),context);
        residentSets.Clear();residentRemovals.Clear();
    }
    internal void DeclareMainOwner(uint owner,object key){if(key==null)return;var group=ResolveMainGroup(key);if(group.Order==0)group.Order=++modelSubmissionOrder;E.MainOwnerIds.Add(owner);E.MainOwnerGroups.Add(group);}
    public readonly struct MainGroupScope:IDisposable
    {
        readonly NativeBackend backend;readonly MainGroupState previous;readonly uint part;
        internal MainGroupScope(NativeBackend backend,object key){this.backend=backend;previous=backend.E.MainGroup;part=backend.MainPart;backend.MainPart=0;
            backend.E.MainGroup=key==null?null:backend.ResolveMainGroup(key);}
        public void Dispose(){if(backend!=null){backend.E.MainGroup=previous;backend.MainPart=part;}}
    }
    public MainGroupScope WithMainGroup(object key)=>new(this,key);
    uint NoteMainGroupOrder(uint flags,uint order){if(E.MainGroup!=null&&E.MainGroup.Order==0&&(flags&(4096u|8192u|OldGeometryOrder))==0)E.MainGroup.Order=order;return E.MainGroup?.Order??0;}
    [StructLayout(LayoutKind.Sequential,Pack=4)]struct MainDrawOrder {public uint Group,Part;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int MainOrderFn(IntPtr context,IntPtr orders,uint count);
    MainOrderFn mainOrderApi;MainDrawOrder[] mainOrderScratch=Array.Empty<MainDrawOrder>();
    [StructLayout(LayoutKind.Sequential,Pack=4)]internal struct MainOwnerOrder {public uint Owner,Group;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int MainOwnersFn(IntPtr context,IntPtr owners,uint count);
    MainOwnersFn mainOwnersApi;MainOwnerOrder[] mainOwnerScratch=Array.Empty<MainOwnerOrder>();
    unsafe void QueueMainOrder(){
        QueueOldOrder();FlushResidentMainOwners();
        int ownerCount=E.MainOwnerIds.Count;if(mainOwnerScratch.Length<ownerCount)Array.Resize(ref mainOwnerScratch,ownerCount);for(int i=0;i<ownerCount;i++)mainOwnerScratch[i]=new MainOwnerOrder{Owner=E.MainOwnerIds[i],Group=E.MainOwnerGroups[i].Order};
        mainOwnersApi??=(MainOwnersFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_main_owners"),typeof(MainOwnersFn));
        fixed(MainOwnerOrder* owners=mainOwnerScratch)Check(mainOwnersApi(context,(IntPtr)owners,(uint)ownerCount),context);
        if(E.WorldPackets.Count==0)return;
        if(mainOrderScratch.Length<E.WorldPackets.Count)Array.Resize(ref mainOrderScratch,E.WorldPackets.Count);
        for(int i=0;i<E.WorldPackets.Count;i++){ref readonly var packet=ref PacketAt(i);uint order=packet.MainGroup?.Order??0;mainOrderScratch[i]=new MainDrawOrder{Group=order!=0?order:packet.Order,Part=packet.MainPart};}
        mainOrderApi??=(MainOrderFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_main_order"),typeof(MainOrderFn));
        fixed(MainDrawOrder* orders=mainOrderScratch)Check(mainOrderApi(context,(IntPtr)orders,(uint)E.WorldPackets.Count),context);
    }
}
