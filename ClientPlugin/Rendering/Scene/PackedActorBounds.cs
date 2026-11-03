using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Runtime.CompilerServices;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

// The Apply thread alone rents rows and writes its current snapshot slot. Build
// reads a different, latched slot, just like ActorFrame. Release may run on
// Build: a returned lease cannot write again, including from either catch-up
// dirty set, and its index becomes available to Apply only after that guard.
internal sealed class PackedActorBounds
{
    internal sealed class Lease
    {
        public readonly int Index;public readonly long Token,Epoch;
        internal int Returned;
        internal Lease(int index,long token,long epoch){Index=index;Token=token;Epoch=epoch;}
    }
    internal readonly struct Row
    {
        public readonly BoundingBoxD Bounds;public readonly long Token;public readonly bool Destroyed;
        internal Row(in BoundingBoxD bounds,bool destroyed,long token){Bounds=bounds;Destroyed=destroyed;Token=token;}
    }
    readonly Row[][] slots;
    readonly ConcurrentQueue<int> free=new();
    int nextIndex;long nextToken,epoch;
    public PackedActorBounds(int slotCount){if(slotCount<2)throw new ArgumentOutOfRangeException(nameof(slotCount));slots=new Row[slotCount][];for(int i=0;i<slots.Length;i++)slots[i]=Array.Empty<Row>();}
    public Lease Rent()
    {
        if(!free.TryDequeue(out int index))index=nextIndex++;
        if(nextToken==long.MaxValue)throw new InvalidOperationException("Packed actor bounds identity exhausted");
        return new Lease(index,++nextToken,epoch);
    }
    public void Release(Lease lease)
    {
        if(lease==null||lease.Epoch!=Volatile.Read(ref epoch)||Interlocked.Exchange(ref lease.Returned,1)!=0)return;
        free.Enqueue(lease.Index);
    }
    public void Write(int slot,Lease lease,in BoundingBoxD bounds,bool destroyed)
    {
        if(lease==null||lease.Epoch!=epoch||Volatile.Read(ref lease.Returned)!=0)return;
        ref var rows=ref slots[slot];
        if(rows.Length<=lease.Index)Array.Resize(ref rows,Math.Max(lease.Index+1,Math.Max(64,rows.Length*2)));
        rows[lease.Index]=new Row(in bounds,destroyed,lease.Token);
    }
    public Row[] Frame(int slot)=>slots[slot];
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref readonly Row Read(Row[] frame,int index,long token)
    {
        if((uint)index>=(uint)frame.Length||frame[index].Token!=token)throw new InvalidOperationException("Packed actor bounds do not belong to the latched actor snapshot");
        return ref frame[index];
    }
    // Unload only, with Apply/Build/Launch joined. Old leases remain invalid even
    // when actor IDs and row indices are reused in the next world.
    public void Clear()
    {
        if(epoch==long.MaxValue)throw new InvalidOperationException("Packed actor bounds epoch exhausted");
        epoch++;nextIndex=0;while(free.TryDequeue(out _)){}
        for(int i=0;i<slots.Length;i++)slots[i]=Array.Empty<Row>();
    }
}
