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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int OldOrderFn(IntPtr context,IntPtr orders,uint count);
    OldOrderFn oldOrderApi;NativeOldPartOrder[] oldOrderScratch=Array.Empty<NativeOldPartOrder>();
    unsafe void QueueOldOrder()
    {
        int count=worldPackets.Count;if(count==0)return;bool any=false;foreach(var packet in worldPackets)any|=packet.OldOrder.Valid!=0;
        if(!any&&oldOrderApi==null)return;
        if(oldOrderScratch.Length<count)Array.Resize(ref oldOrderScratch,count);for(int i=0;i<count;i++)oldOrderScratch[i]=worldPackets[i].OldOrder;
        oldOrderApi??=(OldOrderFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_world_old_order"),typeof(OldOrderFn));
        fixed(NativeOldPartOrder* rows=oldOrderScratch)Check(oldOrderApi(context,(IntPtr)rows,(uint)count),context);
    }
}
