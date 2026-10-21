using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;

[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeOldPartOrder:IEquatable<NativeOldPartOrder>
{
    public ulong Key;
    public uint Resource,Material,Layout,Proxy,Valid,Reserved;
    public bool Equals(NativeOldPartOrder b)=>Key==b.Key&&Resource==b.Resource&&Material==b.Material&&Layout==b.Layout&&Proxy==b.Proxy&&Valid==b.Valid;
    public override bool Equals(object value)=>value is NativeOldPartOrder b&&Equals(b);
    public override int GetHashCode(){unchecked{int h=Key.GetHashCode();h=h*31+(int)Resource;h=h*31+(int)Material;h=h*31+(int)Layout;h=h*31+(int)Proxy;return h*31+(int)Valid;}}
}

public sealed partial class NativeBackend
{
    NativeOldPartOrder oldPartOrder;
    public readonly struct OldOrderScope:IDisposable
    {
        readonly NativeBackend backend;readonly NativeOldPartOrder previous;
        internal OldOrderScope(NativeBackend backend,NativeOldPartOrder order){this.backend=backend;previous=backend.oldPartOrder;backend.oldPartOrder=order;}
        public void Dispose(){if(backend!=null)backend.oldPartOrder=previous;}
    }
    public OldOrderScope WithOldOrder(NativeOldPartOrder order)=>new(this,order);
    // Logical owner of new packets; equal stock OLD keys order by owner, then
    // submission. Shared batches keep owner zero.
    uint packetOwner;
    public bool OldOwnerOrderEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_OLD_OWNER_ORDER")!="0";
    public readonly struct PacketOwnerScope:IDisposable
    {
        readonly NativeBackend backend;readonly uint previous;
        internal PacketOwnerScope(NativeBackend backend,uint owner){this.backend=backend;previous=backend.packetOwner;backend.packetOwner=owner;}
        public void Dispose(){if(backend!=null)backend.packetOwner=previous;}
    }
    public PacketOwnerScope WithPacketOwner(uint owner)=>new(this,owner);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int DrawOwnersFn(IntPtr context,IntPtr owners,uint count);
    DrawOwnersFn drawOwnersApi;uint[] drawOwnerScratch=Array.Empty<uint>();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int OldOrderFn(IntPtr context,IntPtr orders,uint count);
    OldOrderFn oldOrderApi;NativeOldPartOrder[] oldOrderScratch=Array.Empty<NativeOldPartOrder>();
    unsafe void QueueOldOrder()
    {
        int count=worldPackets.Count;if(count==0)return;bool any=false;for(int i=0;i<count;i++)any|=PacketAt(i).OldOrder.Valid!=0;
        if(!any&&oldOrderApi==null)return;
        if(oldOrderScratch.Length<count)Array.Resize(ref oldOrderScratch,count);for(int i=0;i<count;i++)oldOrderScratch[i]=PacketAt(i).OldOrder;
        oldOrderApi??=(OldOrderFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_old_order"),typeof(OldOrderFn));
        fixed(NativeOldPartOrder* rows=oldOrderScratch)Check(oldOrderApi(context,(IntPtr)rows,(uint)count),context);
        if(!OldOwnerOrderEnabled||!any)return;
        if(drawOwnerScratch.Length<count)Array.Resize(ref drawOwnerScratch,count);for(int i=0;i<count;i++)drawOwnerScratch[i]=PacketAt(i).Owner;
        drawOwnersApi??=(DrawOwnersFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_draw_owners"),typeof(DrawOwnersFn));
        fixed(uint* owners=drawOwnerScratch)Check(drawOwnersApi(context,(IntPtr)owners,(uint)count),context);
    }
}
