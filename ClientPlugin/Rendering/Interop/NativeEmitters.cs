using System;
using System.Collections.Generic;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

namespace ClientPlugin.Rendering.Interop;
// Per-producer emitters (plan item 2.1). A producer running on a worker binds
// its own emitter; AddWorldPacket on that thread then appends to the emitter
// with emitter-local orders and the top-level scope state (no main group,
// part, OLD order or owner). The render thread splices the emitter at the
// point where the producer used to run serially: orders are offset by the
// counter at that point, so the merged packet list equals the serial one.
public sealed partial class NativeBackend
{
    public sealed class PacketEmitter
    {
        internal NativeBackend Owner;internal readonly List<WorldPacket> Packets=new();internal readonly List<LogicalWorldDraw> Logical=new();internal uint Orders;
        public int Count=>Packets.Count;
    }
    [ThreadStatic]static PacketEmitter boundEmitter;
    public PacketEmitter CreateEmitter()=>new PacketEmitter{Owner=this};
    public readonly struct EmitterScope:IDisposable
    {
        readonly PacketEmitter previous;readonly bool bound;
        internal EmitterScope(PacketEmitter emitter){previous=boundEmitter;boundEmitter=emitter;bound=true;}
        public void Dispose(){if(bound)boundEmitter=previous;}
    }
    // Binds the emitter to the calling thread until the scope is disposed.
    public EmitterScope BindEmitter(PacketEmitter emitter){if(emitter==null||!ReferenceEquals(emitter.Owner,this))throw new ArgumentException("Emitter belongs to another backend.");return new EmitterScope(emitter);}
    uint AddEmitterPacket(PacketEmitter emitter,uint pipeline,ulong mesh,ulong color,ulong normal,ulong extensions,ulong alpha,byte[] constants,byte[] instances,uint instanceCount,uint flags,uint cascadeMask,Vector3 origin,float radius,bool recycleConstants,WorldRecipe recipe,bool recordLogical)
    {
        if((flags&OldGeometryOrder)!=0)throw new InvalidOperationException("OLD-ordered packets need the render thread's OLD scope.");
        if(recordLogical&&captureLogicalSelection)emitter.Logical.Add(new LogicalWorldDraw{Owner=uint.MaxValue,Pipeline=pipeline,Mesh=mesh,Count=instanceCount,Flags=flags,Cascades=cascadeMask});
        emitter.Packets.Add(new WorldPacket{Order=++emitter.Orders,Recipe=recipe,Pipeline=pipeline,Mesh=mesh,Color=color,Normal=normal,Extensions=extensions,Alpha=alpha,Constants=constants,Instances=instances,Count=instanceCount,Flags=flags,CascadeMask=cascadeMask,Origin=origin,Radius=radius,RecycleConstants=recycleConstants});
        return (uint)emitter.Packets.Count-1;
    }
    // Render thread, at the producer's serial position. The emitter's packets
    // used top-level scope state, so the render thread must be at top level too.
    public void SpliceEmitter(PacketEmitter emitter)
    {
        if(emitter==null||!ReferenceEquals(emitter.Owner,this))throw new ArgumentException("Emitter belongs to another backend.");
        if(boundEmitter!=null)throw new InvalidOperationException("Emitters splice on the render thread.");
        if(mainGroup!=null||MainPart!=0||packetOwner!=0||(captureLogicalSelection&&logicalOwner!=uint.MaxValue))throw new InvalidOperationException("Emitter splice inside a main-group, owner or logical scope.");
        if(worldPackets.Count+emitter.Packets.Count>65536)throw new InvalidOperationException("World packet batch exceeds 65536 draws.");
        uint baseOrder=modelSubmissionOrder;
        foreach(var source in emitter.Packets){var packet=source;packet.Order+=baseOrder;worldPackets.Add(packet);}
        modelSubmissionOrder+=emitter.Orders;
        if(captureLogicalSelection)logicalDraws.AddRange(emitter.Logical);
        emitter.Packets.Clear();emitter.Logical.Clear();emitter.Orders=0;
    }
    // Drops an emitter's packets; recycle returns their pooled constants.
    public void DiscardEmitter(PacketEmitter emitter,bool recycle)
    {
        if(recycle)foreach(var packet in emitter.Packets)if(packet.RecycleConstants)ReflectedConstantBuffer.Recycle(packet.Constants);
        emitter.Packets.Clear();emitter.Logical.Clear();emitter.Orders=0;
    }

    // Packet signature gate (SE_VULKAN_PACKET_SIGNATURE=1 or PacketSignatureEnabled):
    // FNV-1a over every queued world packet's fields, constants and instances at
    // QueueWorld, before packing. A forked producer must leave it unchanged.
    public bool PacketSignatureEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_PACKET_SIGNATURE")=="1";
    public ulong LastPacketSignature {get;private set;}
    public int LastPacketSignatureCount {get;private set;}
    static ulong Mix(ulong hash,ulong value){for(int i=0;i<8;i++){hash^=(byte)(value>>(i*8));hash*=1099511628211UL;}return hash;}
    static ulong MixBytes(ulong hash,byte[] bytes,int count){if(bytes==null)return Mix(hash,ulong.MaxValue);hash=Mix(hash,(ulong)count);for(int i=0;i<count;i++){hash^=bytes[i];hash*=1099511628211UL;}return hash;}
    static unsafe ulong MixFloat(ulong hash,float value)=>Mix(hash,*(uint*)&value);
    ulong ComputePacketSignature()
    {
        ulong hash=14695981039346656037UL;
        for(int i=0;i<worldPackets.Count;i++){
            var p=worldPackets[i];
            hash=Mix(hash,p.Order);hash=Mix(hash,p.MainGroup?.Order??0);hash=Mix(hash,p.MainPart);hash=Mix(hash,p.Recipe?.Handle??0);
            var old=p.OldOrder;hash=Mix(hash,old.Key);hash=Mix(hash,old.Resource);hash=Mix(hash,old.Material);hash=Mix(hash,old.Layout);hash=Mix(hash,old.Proxy);hash=Mix(hash,old.Valid);
            hash=Mix(hash,p.Pipeline);hash=Mix(hash,p.Count);hash=Mix(hash,p.Flags);hash=Mix(hash,p.CascadeMask);hash=Mix(hash,p.Owner);
            hash=Mix(hash,p.Mesh);hash=Mix(hash,p.Color);hash=Mix(hash,p.Normal);hash=Mix(hash,p.Extensions);hash=Mix(hash,p.Alpha);
            hash=MixFloat(hash,p.Origin.X);hash=MixFloat(hash,p.Origin.Y);hash=MixFloat(hash,p.Origin.Z);hash=MixFloat(hash,p.Radius);
            hash=Mix(hash,(p.RecycleConstants?1UL:0)|(p.InstanceCapacity?2UL:0));
            hash=MixBytes(hash,p.Constants,p.Constants?.Length??0);hash=MixBytes(hash,p.Instances,p.Instances==null?0:(int)Math.Min(p.Instances.LongLength,p.Count*64L));
        }
        return hash;
    }
    void NotePacketSignature(){if(!PacketSignatureEnabled)return;LastPacketSignature=ComputePacketSignature();LastPacketSignatureCount=worldPackets.Count;}
}
