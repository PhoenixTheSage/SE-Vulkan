using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    internal sealed class MainGroupState {internal uint Token,Order;}
    readonly Dictionary<object,MainGroupState> mainGroups=new();
    MainGroupState mainGroup;
    internal uint MainPart {get;set;}
    uint MainGroupToken=>mainGroup?.Token??0;
    readonly Dictionary<uint,uint> mainOwnerOrders=new();
    void BeginMainGroups(){mainGroups.Clear();mainOwnerOrders.Clear();mainGroup=null;MainPart=0;oldPartOrder=default;}
    internal void DeclareMainOwner(uint owner,object key){if(key==null)return;using var scope=WithMainGroup(key);if(mainGroup.Order==0)mainGroup.Order=++modelSubmissionOrder;mainOwnerOrders[owner]=mainGroup.Order;}
    public readonly struct MainGroupScope:IDisposable
    {
        readonly NativeBackend backend;readonly MainGroupState previous;readonly uint part;
        internal MainGroupScope(NativeBackend backend,object key){this.backend=backend;previous=backend.mainGroup;part=backend.MainPart;backend.MainPart=0;
            if(key==null)backend.mainGroup=null;else{if(!backend.mainGroups.TryGetValue(key,out var group)){group=new MainGroupState{Token=(uint)backend.mainGroups.Count+1};backend.mainGroups.Add(key,group);}backend.mainGroup=group;}}
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
        QueueOldOrder();
        if(mainOwnerScratch.Length<mainOwnerOrders.Count)Array.Resize(ref mainOwnerScratch,mainOwnerOrders.Count);int at=0;foreach(var pair in mainOwnerOrders)mainOwnerScratch[at++]=new MainOwnerOrder{Owner=pair.Key,Group=pair.Value};
        mainOwnersApi??=(MainOwnersFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_main_owners"),typeof(MainOwnersFn));
        fixed(MainOwnerOrder* owners=mainOwnerScratch)Check(mainOwnersApi(context,(IntPtr)owners,(uint)mainOwnerOrders.Count),context);
        if(worldPackets.Count==0)return;
        if(mainOrderScratch.Length<worldPackets.Count)Array.Resize(ref mainOrderScratch,worldPackets.Count);
        for(int i=0;i<worldPackets.Count;i++){var packet=worldPackets[i];uint order=packet.MainGroup?.Order??0;mainOrderScratch[i]=new MainDrawOrder{Group=order!=0?order:packet.Order,Part=packet.MainPart};}
        mainOrderApi??=(MainOrderFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_main_order"),typeof(MainOrderFn));
        fixed(MainDrawOrder* orders=mainOrderScratch)Check(mainOrderApi(context,(IntPtr)orders,(uint)worldPackets.Count),context);
    }
}
