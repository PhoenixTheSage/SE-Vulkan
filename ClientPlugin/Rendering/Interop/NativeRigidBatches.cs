using System;
using System.Collections.Generic;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    public bool RigidBatchingEnabled {get;set;}=true;
    public bool RigidBatchHintsEnabled {get;set;}=true;
    public bool WideRigidBatchesEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_WIDE_RIGID_BATCHES")!="0";
    private readonly Dictionary<RigidBatchKey,RigidBatch> rigidBatches=new Dictionary<RigidBatchKey,RigidBatch>();
    private readonly List<RigidBatch> rigidBatchSlots=new List<RigidBatch>();
    private int batchFrame;
    private uint rigidBatchPackets;
    private uint rigidCacheEpoch;
    public uint LogicalRigidDraws { get; private set; }
    internal readonly struct RigidBatchKey : IEquatable<RigidBatchKey>
    {
        public readonly uint Pipeline,Flags,Cascades,Lod,MainGroup;
        public readonly NativeOldPartOrder OldOrder;
        public readonly ulong Mesh,Color,Normal,Extensions,Alpha;
        public readonly Vector3 Tint;
        public readonly float Emissive,Dither;
        public readonly int X,Y,Z,Cell;
        public RigidBatchKey(uint pipeline,ulong mesh,ulong color,ulong normal,ulong extensions,ulong alpha,Vector3 tint,float emissive,float dither,uint flags,uint cascades,Vector3 origin,int cell,uint lod=0,uint mainGroup=0,NativeOldPartOrder oldOrder=default)
        {OldOrder=oldOrder;Pipeline=pipeline;Lod=lod;MainGroup=mainGroup;Mesh=mesh;Color=color;Normal=normal;Extensions=extensions;Alpha=alpha;Tint=tint;Emissive=emissive;Dither=dither;Flags=flags;Cascades=cascades;Cell=cell;X=(int)Math.Floor(origin.X/cell);Y=(int)Math.Floor(origin.Y/cell);Z=(int)Math.Floor(origin.Z/cell);}
        public bool Equals(RigidBatchKey b)=>OldOrder.Equals(b.OldOrder)&&Pipeline==b.Pipeline && Lod==b.Lod && MainGroup==b.MainGroup && Mesh==b.Mesh && Color==b.Color && Normal==b.Normal && Extensions==b.Extensions && Alpha==b.Alpha && Tint==b.Tint && Emissive==b.Emissive && Dither==b.Dither && Flags==b.Flags && Cascades==b.Cascades && Cell==b.Cell && X==b.X && Y==b.Y && Z==b.Z;
        public override bool Equals(object b)=>b is RigidBatchKey key && Equals(key);
        public override int GetHashCode(){unchecked{int h=(int)Pipeline;h=h*31+(int)Lod;h=h*31+(int)MainGroup;h=h*31+Mesh.GetHashCode();h=h*31+Color.GetHashCode();h=h*31+Normal.GetHashCode();h=h*31+Extensions.GetHashCode();h=h*31+Alpha.GetHashCode();h=h*31+Tint.GetHashCode();h=h*31+Emissive.GetHashCode();h=h*31+Dither.GetHashCode();h=h*31+(int)Flags;h=h*31+(int)Cascades;h=h*31+Cell;h=h*31+X;h=h*31+Y;return h*31+Z;}}
    }
    internal sealed class RigidBatch
    {
        public int Frame,PacketIndex,CacheIndex;
        public WorldRecipe Recipe;
        public byte[] Constants,Instances=new byte[256];
    }
    public sealed class RigidBatchHint
    {
        internal NativeBackend Owner;
        internal uint Epoch;
        internal RigidBatchKey Key;
        internal int Slot=-1;
    }
    private void BeginRigidBatches()
    {
        submissionStats=new SceneSubmissionStats{Enabled=RetainedSubmissionEnabled?1u:0u};
        if(PacketProfilingEnabled)ResetPacketPreparation();
        batchFrame++;BeginModelBatches();BeginNativeModels();BeginMainGroups();LogicalRigidDraws=0;rigidBatchPackets=0;
        // Bound CPU residency across travel/world reloads; every entry can be
        // reconstructed from this frame's authoritative material parameters.
        if(rigidBatches.Count>32768){rigidBatches.Clear();rigidBatchSlots.Clear();rigidCacheEpoch++;}
    }
    // Only the scene's stock opaque rigid path calls this. When highlights or
    // shadow exclusions consume individual draw IDs, it uses AddWorldPacket.
    public unsafe uint AddRigidWorldPacket(uint pipeline,ulong mesh,ulong color,ulong normal,ulong extensions,ulong alpha,MatrixD world,Vector3 tint,Vector3 hsv,float emissive,float dither,uint flags,uint cascades,Vector3 origin,float radius,RigidBatchHint hint=null,uint lod=0)
    {
        long detailAt=PacketProfilingEnabled?System.Diagnostics.Stopwatch.GetTimestamp():0;
        if(pipeline>=20 || pipeline%5!=0 || !WorldReady || !WorldLayouts.ContainsKey(pipeline+2))throw new InvalidOperationException("Rigid batching requires a matching stock generic pipeline.");
        // Catalogue-only generic variants omit object tint. Retain a rigid
        // reference when the authenticated object-tint supplement is unavailable.
        if(tint!=Vector3.One && !tintedGenericPipelines.Contains(pipeline+2))
            return AddWorldPacket(pipeline,mesh,color,normal,extensions,alpha,
                WorldConstants.Object(WorldLayouts[pipeline],world,tint,hsv,emissive,0,null,dither,true,lod),
                null,1,flags,cascades,origin,radius);
        var key=new RigidBatchKey(pipeline,mesh,color,normal,extensions,alpha,tint,emissive,dither,flags,cascades,origin,WideRigidBatchesEnabled?64:16,lod,MainGroupToken,oldPartOrder);
        RigidBatch batch;
        if(RigidBatchHintsEnabled && hint!=null && ReferenceEquals(hint.Owner,this) && hint.Epoch==rigidCacheEpoch && hint.Slot>=0 && hint.Slot<rigidBatchSlots.Count && hint.Key.Equals(key))
        {
            batch=rigidBatchSlots[hint.Slot];
            if(PacketProfilingEnabled)batchHintHits++;
        }
        else if(!rigidBatches.TryGetValue(key,out batch))
        {
            batch=new RigidBatch{Frame=-1,CacheIndex=rigidBatchSlots.Count,Constants=WorldConstants.Object(WorldLayouts[pipeline+2],MatrixD.Identity,tint,Vector3.Zero,emissive,0,null,dither,lod:lod)};
            rigidBatches.Add(key,batch);
            rigidBatchSlots.Add(batch);
            if(PacketProfilingEnabled)createdBatches++;
        }
        if(RigidBatchHintsEnabled && hint!=null){hint.Owner=this;hint.Epoch=rigidCacheEpoch;hint.Key=key;hint.Slot=batch.CacheIndex;}
        if(PacketProfilingEnabled){long now=System.Diagnostics.Stopwatch.GetTimestamp();rigidLookupTicks+=now-detailAt;detailAt=now;}
        if(batch.Frame!=batchFrame)
        {
            if(WorldRecipesEnabled)EnsureWorldRecipe(ref batch.Recipe,pipeline+2,mesh,color,normal,extensions,alpha,batch.Constants.Length);
            batch.Frame=batchFrame;
            rigidBatchPackets++;
            batch.PacketIndex=(int)AddWorldPacket(pipeline+2,mesh,color,normal,extensions,alpha,batch.Constants,batch.Instances,1,flags,cascades,origin,radius,false,batch.Recipe,false);
            ref var first=ref PacketAt(batch.PacketIndex);first.Count=0;first.InstanceCapacity=true;
        }
        ref var packet=ref PacketAt(batch.PacketIndex);int at=checked((int)packet.Count*64);
        if(packet.Count>=1000000)throw new InvalidOperationException("Rigid instance batch exceeds native instance count.");
        Grow(ref batch.Instances,at,64);
        fixed(byte* bytes=batch.Instances){float* f=(float*)(bytes+at);
            f[0]=(float)world.M11;f[1]=(float)world.M21;f[2]=(float)world.M31;f[3]=(float)world.M41;
            f[4]=(float)world.M12;f[5]=(float)world.M22;f[6]=(float)world.M32;f[7]=(float)world.M42;
            f[8]=(float)world.M13;f[9]=(float)world.M23;f[10]=(float)world.M33;f[11]=(float)world.M43;
            f[12]=hsv.X;f[13]=hsv.Y;f[14]=hsv.Z;f[15]=0;}
        if(packet.Count!=0)
        {
            if(packet.Radius<0 || radius<0)packet.Radius=-1;
            else{Vector3 shift=origin-packet.Origin;float distance=shift.Length();
                if(distance+radius>packet.Radius){if(distance+packet.Radius<=radius){packet.Origin=origin;packet.Radius=radius;}
                    else{float combined=(distance+packet.Radius+radius)*.5f;if(distance>0)packet.Origin+=shift*((combined-packet.Radius)/distance);packet.Radius=combined;}}}
        }
        NoteLogicalDraw(pipeline+2,mesh,1,flags,cascades);
        packet.Count++;packet.Instances=batch.Instances;LogicalRigidDraws++;
        if(PacketProfilingEnabled)rigidAppendTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-detailAt;
        return (uint)batch.PacketIndex;
    }
}
