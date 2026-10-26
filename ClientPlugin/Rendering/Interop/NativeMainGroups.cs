using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    internal sealed class MainGroupState {internal uint Token,Order;}
    // Keys may remember the group they resolved to during the current group
    // epoch; equal keys still meet through the dictionary on first use.
    internal abstract class MainGroupKeyBase {internal MainGroupState Group;internal NativeBackend Owner;internal uint Epoch;}
    readonly Dictionary<object,MainGroupState> mainGroups=new();
    MainGroupState mainGroup;uint mainGroupEpoch=1;
    internal uint MainPart {get;set;}
    uint MainGroupToken=>mainGroup?.Token??0;
    // Candidates are unique per frame, so owners are appended without lookup;
    // a group's order is fixed at its first declaration and read when queued.
    readonly List<uint> mainOwnerIds=new();readonly List<MainGroupState> mainOwnerGroups=new();
    // States are reused frame to frame: keys only trust a cached group from the current epoch, and
    // packets holding a group are cleared before the next BeginMainGroups.
    readonly List<MainGroupState> mainGroupPool=new();int mainGroupsRented;
    void BeginMainGroups(){mainGroups.Clear();mainGroupsRented=0;mainGroupEpoch++;mainOwnerIds.Clear();mainOwnerGroups.Clear();mainGroup=null;MainPart=0;oldPartOrder=default;packetOwner=0;}
    MainGroupState ResolveMainGroup(object key){
        var cached=key as MainGroupKeyBase;if(cached!=null&&cached.Epoch==mainGroupEpoch&&ReferenceEquals(cached.Owner,this))return cached.Group;
        if(!mainGroups.TryGetValue(key,out var group)){if(mainGroupsRented==mainGroupPool.Count)mainGroupPool.Add(new MainGroupState());group=mainGroupPool[mainGroupsRented++];group.Token=(uint)mainGroups.Count+1;group.Order=StableMainOrderEnabled?StableOrder(key):0;mainGroups.Add(key,group);}
        if(cached!=null){cached.Group=group;cached.Owner=this;cached.Epoch=mainGroupEpoch;}
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
    unsafe void FlushResidentMainOwners(){
        if(residentSets.Count==0&&residentRemovals.Count==0)return;
        if(residentSetScratch.Length<residentSets.Count)residentSetScratch=new MainOwnerOrder[residentSets.Count*2];if(residentRemoveScratch.Length<residentRemovals.Count)residentRemoveScratch=new uint[residentRemovals.Count*2];
        int sets=0,removals=0;foreach(var entry in residentSets)residentSetScratch[sets++]=new MainOwnerOrder{Owner=entry.Key,Group=entry.Value};foreach(uint owner in residentRemovals)residentRemoveScratch[removals++]=owner;
        residentOwnersApi??=(ResidentOwnersFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_resident_main_owners"),typeof(ResidentOwnersFn));
        fixed(MainOwnerOrder* set=residentSetScratch)fixed(uint* removed=residentRemoveScratch)Check(residentOwnersApi(context,(IntPtr)set,(uint)sets,(IntPtr)removed,(uint)removals),context);
        residentSets.Clear();residentRemovals.Clear();
    }
    internal void DeclareMainOwner(uint owner,object key){if(key==null)return;var group=ResolveMainGroup(key);if(group.Order==0)group.Order=++modelSubmissionOrder;mainOwnerIds.Add(owner);mainOwnerGroups.Add(group);}
    public readonly struct MainGroupScope:IDisposable
    {
        readonly NativeBackend backend;readonly MainGroupState previous;readonly uint part;
        internal MainGroupScope(NativeBackend backend,object key){this.backend=backend;previous=backend.mainGroup;part=backend.MainPart;backend.MainPart=0;
            backend.mainGroup=key==null?null:backend.ResolveMainGroup(key);}
        public void Dispose(){if(backend!=null){backend.mainGroup=previous;backend.MainPart=part;}}
    }
    public MainGroupScope WithMainGroup(object key)=>new(this,key);
    uint NoteMainGroupOrder(uint flags,uint order){if(mainGroup!=null&&mainGroup.Order==0&&(flags&(4096u|8192u|OldGeometryOrder))==0)mainGroup.Order=order;return mainGroup?.Order??0;}
    [StructLayout(LayoutKind.Sequential,Pack=4)]struct MainDrawOrder {public uint Group,Part;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int MainOrderFn(IntPtr context,IntPtr orders,uint count);
    MainOrderFn mainOrderApi;MainDrawOrder[] mainOrderScratch=Array.Empty<MainDrawOrder>();
    [StructLayout(LayoutKind.Sequential,Pack=4)]struct MainOwnerOrder {public uint Owner,Group;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int MainOwnersFn(IntPtr context,IntPtr owners,uint count);
    MainOwnersFn mainOwnersApi;MainOwnerOrder[] mainOwnerScratch=Array.Empty<MainOwnerOrder>();
    unsafe void QueueMainOrder(){
        QueueOldOrder();FlushResidentMainOwners();
        int ownerCount=mainOwnerIds.Count;if(mainOwnerScratch.Length<ownerCount)Array.Resize(ref mainOwnerScratch,ownerCount);for(int i=0;i<ownerCount;i++)mainOwnerScratch[i]=new MainOwnerOrder{Owner=mainOwnerIds[i],Group=mainOwnerGroups[i].Order};
        mainOwnersApi??=(MainOwnersFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_main_owners"),typeof(MainOwnersFn));
        fixed(MainOwnerOrder* owners=mainOwnerScratch)Check(mainOwnersApi(context,(IntPtr)owners,(uint)ownerCount),context);
        if(worldPackets.Count==0)return;
        if(mainOrderScratch.Length<worldPackets.Count)Array.Resize(ref mainOrderScratch,worldPackets.Count);
        for(int i=0;i<worldPackets.Count;i++){ref readonly var packet=ref PacketAt(i);uint order=packet.MainGroup?.Order??0;mainOrderScratch[i]=new MainDrawOrder{Group=order!=0?order:packet.Order,Part=packet.MainPart};}
        mainOrderApi??=(MainOrderFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_main_order"),typeof(MainOrderFn));
        fixed(MainDrawOrder* orders=mainOrderScratch)Check(mainOrderApi(context,(IntPtr)orders,(uint)worldPackets.Count),context);
    }
}
