using System;
using VRageMath;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    public bool RetainedSubmissionEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_RETAINED_SUBMISSION")!="0";
    private SceneSubmissionStats submissionStats;
    public SceneSubmissionStats GetSubmissionStats()=>submissionStats;
    public void NoteSubmissionRebuild(){submissionStats.RebuiltActors++;}
    public sealed class PreparedPart
    {
        public readonly uint Pipeline;
        public readonly uint ViewFlags;
        public readonly ulong Mesh,Color,Normal,Extensions,Alpha;
        public readonly Vector3 Tint,Hsv;
        public readonly float Emissive,Dither;
        internal readonly byte[] Constants;
        internal readonly WorldConstants.MatrixRows Rows;
        internal readonly int AlphaOffset;
        internal readonly RigidBatchHint Hint=new();
        internal WorldRecipe Recipe;
        public PreparedPart(uint pipeline,ulong mesh,ulong color,ulong normal,ulong extensions,ulong alpha,Vector3 tint,Vector3 hsv,float emissive,float dither,ShaderMetadata layout,uint viewFlags=0)
        {Pipeline=pipeline;ViewFlags=viewFlags;Mesh=mesh;Color=color;Normal=normal;Extensions=extensions;Alpha=alpha;Tint=tint;Hsv=hsv;Emissive=emissive;Dither=dither;Constants=WorldConstants.Object(layout,MatrixD.Identity,tint,hsv,emissive,0,null,dither);Rows=WorldConstants.ObjectRows(layout);AlphaOffset=new ReflectedConstantBuffer(layout,"Object").OffsetOf("object_.custom_alpha");}
    }
    public sealed class PreparedActor
    {
        public readonly PreparedPart[] Parts;
        internal readonly byte[] Instance=new byte[64];
        internal MatrixD Pose;
        internal bool HavePose,UsedModelBatch;
        internal readonly ModelBatchHint ModelHint=new();
        internal float LodAlpha;internal uint NativeEligibilityEpoch;internal bool NativeEligibility;
        public PreparedActor(PreparedPart[] parts){Parts=parts??throw new ArgumentNullException(nameof(parts));}
    }
    public unsafe void SubmitPreparedActor(PreparedActor actor,MatrixD world,byte[] stream,uint count,uint flags,uint cascades,Vector3 origin,float radius,float lodAlpha=0)
    {
        submissionStats.Actors++;submissionStats.Parts+=(uint)actor.Parts.Length;
        bool changed=!actor.HavePose||actor.Pose!=world||actor.UsedModelBatch;actor.UsedModelBatch=false;
        bool alphaChanged=actor.LodAlpha!=lodAlpha;actor.LodAlpha=lodAlpha;
        if(changed){actor.Pose=world;actor.HavePose=true;fixed(byte* bytes=actor.Instance){float* f=(float*)bytes;
            f[0]=(float)world.M11;f[1]=(float)world.M21;f[2]=(float)world.M31;f[3]=(float)world.M41;
            f[4]=(float)world.M12;f[5]=(float)world.M22;f[6]=(float)world.M32;f[7]=(float)world.M42;
            f[8]=(float)world.M13;f[9]=(float)world.M23;f[10]=(float)world.M33;f[11]=(float)world.M43;}}
        int cell=WideRigidBatchesEnabled?64:16,x=(int)Math.Floor(origin.X/cell),y=(int)Math.Floor(origin.Y/cell),z=(int)Math.Floor(origin.Z/cell);
        if(lodAlpha==0&&stream==null&&count==1&&TryBulkModel(actor,flags,cascades,origin,radius,cell,x,y,z)){actor.UsedModelBatch=true;return;}
        if(lodAlpha==0&&stream==null&&count==1&&TryModelBatch(actor,flags,cascades,origin,radius,cell,x,y,z)){actor.UsedModelBatch=true;return;}
        foreach(var part in actor.Parts){
            // Keep the rigid reference current too: batching can be disabled
            // without a pose change, and tinted generic coverage can differ.
            if(changed)WorldConstants.WriteMatrix(part.Constants,part.Rows,world);
            if(alphaChanged)fixed(byte* bytes=part.Constants)*(float*)(bytes+part.AlphaOffset)=lodAlpha;
            bool rigid=lodAlpha==0&&RigidBatchingEnabled&&stream==null&&count==1&&part.Pipeline<20&&part.Pipeline%5==0&&(part.Tint==Vector3.One||tintedGenericPipelines.Contains(part.Pipeline+2));
            if(rigid){fixed(byte* bytes=actor.Instance){float* f=(float*)bytes;f[12]=part.Hsv.X;f[13]=part.Hsv.Y;f[14]=part.Hsv.Z;f[15]=0;}AppendPreparedRigid(part,actor.Instance,flags|part.ViewFlags,cascades,origin,radius,cell,x,y,z);}
            else{if(WorldRecipesEnabled)EnsureWorldRecipe(ref part.Recipe,part.Pipeline,part.Mesh,part.Color,part.Normal,part.Extensions,part.Alpha,part.Constants.Length);AddWorldPacket(part.Pipeline,part.Mesh,part.Color,part.Normal,part.Extensions,part.Alpha,part.Constants,stream,count,flags|part.ViewFlags,cascades,origin,radius,false,part.Recipe);}
        }
    }
    private void AppendPreparedRigid(PreparedPart part,byte[] instance,uint flags,uint cascades,Vector3 origin,float radius,int cell,int x,int y,int z)
    {
        var hint=part.Hint;RigidBatch batch;
        if(RigidBatchHintsEnabled&&ReferenceEquals(hint.Owner,this)&&hint.Epoch==rigidCacheEpoch&&hint.Slot>=0&&hint.Slot<rigidBatchSlots.Count&&hint.Key.Flags==flags&&hint.Key.Cascades==cascades&&hint.Key.Cell==cell&&hint.Key.X==x&&hint.Key.Y==y&&hint.Key.Z==z)batch=rigidBatchSlots[hint.Slot];
        else{
            var key=new RigidBatchKey(part.Pipeline,part.Mesh,part.Color,part.Normal,part.Extensions,part.Alpha,part.Tint,part.Emissive,part.Dither,flags,cascades,origin,cell);
            if(!rigidBatches.TryGetValue(key,out batch)){batch=new RigidBatch{Frame=-1,CacheIndex=rigidBatchSlots.Count,Constants=WorldConstants.Object(WorldLayouts[part.Pipeline+2],MatrixD.Identity,part.Tint,Vector3.Zero,part.Emissive,0,null,part.Dither)};rigidBatches.Add(key,batch);rigidBatchSlots.Add(batch);}
            hint.Owner=this;hint.Epoch=rigidCacheEpoch;hint.Slot=batch.CacheIndex;hint.Key=key;
        }
        if(batch.Frame!=batchFrame){if(WorldRecipesEnabled)EnsureWorldRecipe(ref batch.Recipe,part.Pipeline+2,part.Mesh,part.Color,part.Normal,part.Extensions,part.Alpha,batch.Constants.Length);batch.Frame=batchFrame;rigidBatchPackets++;batch.PacketIndex=(int)AddWorldPacket(part.Pipeline+2,part.Mesh,part.Color,part.Normal,part.Extensions,part.Alpha,batch.Constants,batch.Instances,1,flags,cascades,origin,radius,false,batch.Recipe,false);var first=worldPackets[batch.PacketIndex];first.Count=0;first.InstanceCapacity=true;worldPackets[batch.PacketIndex]=first;}
        var packet=worldPackets[batch.PacketIndex];int at=checked((int)packet.Count*64);if(packet.Count>=1000000)throw new InvalidOperationException("Prepared instance budget exceeded.");Grow(ref batch.Instances,at,64);Buffer.BlockCopy(instance,0,batch.Instances,at,64);
        if(packet.Count!=0){if(packet.Radius<0||radius<0)packet.Radius=-1;else{Vector3 shift=origin-packet.Origin;float distance=shift.Length();if(distance+radius>packet.Radius){if(distance+packet.Radius<=radius){packet.Origin=origin;packet.Radius=radius;}else{float combined=(distance+packet.Radius+radius)*.5f;if(distance>0)packet.Origin+=shift*((combined-packet.Radius)/distance);packet.Radius=combined;}}}}
        NoteLogicalDraw(part.Pipeline+2,part.Mesh,1,flags,cascades);
        packet.Count++;packet.Instances=batch.Instances;worldPackets[batch.PacketIndex]=packet;LogicalRigidDraws++;
    }
}
public struct SceneSubmissionStats {public uint Enabled,Actors,Parts,RebuiltActors;public ulong ObjectBlobBytes,InstanceBlobBytes;}
