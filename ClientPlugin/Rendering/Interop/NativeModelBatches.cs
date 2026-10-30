using System;
using System.Collections.Generic;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    public bool ModelBatchingEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_MODEL_BATCHING")=="1";
    ModelBatchStats modelBatchStats;
    public ModelBatchStats GetModelBatchStats()=>modelBatchStats;
    private sealed class ModelTemplate
    {
        public PreparedPart[] Parts;public int Hash,Id;public ulong NativeHandle;public uint NativeEpoch,NativeResourceRevision;public WorldRecipe[] NativeRecipes;
        public bool Same(PreparedPart[] parts){if(parts.Length!=Parts.Length)return false;for(int i=0;i<parts.Length;i++){var a=Parts[i];var b=parts[i];if(!a.OldOrder.Equals(b.OldOrder)||a.ViewFlags!=b.ViewFlags||a.Pipeline!=b.Pipeline||a.Lod!=b.Lod||a.Mesh!=b.Mesh||a.Color!=b.Color||a.Normal!=b.Normal||a.Extensions!=b.Extensions||a.Alpha!=b.Alpha||a.Tint!=b.Tint||a.Emissive!=b.Emissive||a.Dither!=b.Dither)return false;}return true;}
    }
    internal sealed class ModelBatchHint {public NativeBackend Owner;public uint Epoch;public int Template,Slot=-1;public uint Flags,Cascades,MainGroup;public int Cell,X,Y,Z;}
    readonly struct ModelBatchKey:IEquatable<ModelBatchKey>
    {
        readonly int Template,Cell,X,Y,Z;readonly uint Flags,Cascades,MainGroup;
        public ModelBatchKey(int template,uint flags,uint cascades,int cell,int x,int y,int z,uint group){Template=template;Flags=flags;Cascades=cascades;MainGroup=group;Cell=cell;X=x;Y=y;Z=z;}
        public bool Equals(ModelBatchKey b)=>Template==b.Template&&Flags==b.Flags&&Cascades==b.Cascades&&MainGroup==b.MainGroup&&Cell==b.Cell&&X==b.X&&Y==b.Y&&Z==b.Z;
        public override bool Equals(object value)=>value is ModelBatchKey key&&Equals(key);
        public override int GetHashCode(){unchecked{int h=Template;h=h*31+(int)Flags;h=h*31+(int)Cascades;h=h*31+(int)MainGroup;h=h*31+Cell;h=h*31+X;h=h*31+Y;return h*31+Z;}}
    }
    private sealed class ModelBatch {public ModelTemplate Template;public int Frame=-1,Slot;public uint Count;public byte[] Instances=new byte[256];public int[] Packets;public byte[][] Constants;public WorldRecipe[] Recipes;public Vector3 Origin;public float Radius;}
    readonly Dictionary<int,List<ModelTemplate>> modelTemplates=new();readonly List<ModelTemplate> modelTemplateSlots=new();readonly Dictionary<ModelBatchKey,ModelBatch> modelBatches=new();readonly List<ModelBatch> modelBatchSlots=new(),activeModelBatches=new();uint modelBatchEpoch;
    void BeginModelBatches(){modelBatchStats=new ModelBatchStats{Enabled=ModelBatchingEnabled?1u:0u};activeModelBatches.Clear();if(modelBatches.Count>32768 || modelTemplateSlots.Count>32768){modelBatches.Clear();modelBatchSlots.Clear();modelTemplates.Clear();modelTemplateSlots.Clear();modelBatchEpoch++;}}
    ModelTemplate TemplateFor(PreparedActor actor){
        var hint=actor.ModelHint;if(ReferenceEquals(hint.Owner,this)&&hint.Epoch==modelBatchEpoch&&hint.Template>=0&&hint.Template<modelTemplateSlots.Count)return modelTemplateSlots[hint.Template];
        unchecked{int hash=actor.Parts.Length;foreach(var p in actor.Parts){hash=hash*31+(int)p.ViewFlags;hash=hash*31+(int)p.Pipeline;hash=hash*31+(int)p.Lod;hash=hash*31+p.Mesh.GetHashCode();hash=hash*31+p.Color.GetHashCode();hash=hash*31+p.Normal.GetHashCode();hash=hash*31+p.Extensions.GetHashCode();hash=hash*31+p.Alpha.GetHashCode();hash=hash*31+p.Tint.GetHashCode();hash=hash*31+p.Emissive.GetHashCode();hash=hash*31+p.Dither.GetHashCode();}
            if(!modelTemplates.TryGetValue(hash,out var bucket))modelTemplates[hash]=bucket=new List<ModelTemplate>();ModelTemplate selected=null;foreach(var candidate in bucket)if(candidate.Same(actor.Parts)){selected=candidate;break;}
            if(selected==null){selected=new ModelTemplate{Parts=actor.Parts,Hash=hash,Id=modelTemplateSlots.Count};modelTemplateSlots.Add(selected);bucket.Add(selected);}
            hint.Owner=this;hint.Epoch=modelBatchEpoch;hint.Template=selected.Id;hint.Slot=-1;return selected;}
    }
    bool TryModelBatch(PreparedActor actor,uint flags,uint cascades,Vector3 origin,float radius,int cell,int x,int y,int z){
        if(!ModelBatchingEnabled||!RigidBatchingEnabled||actor.Parts.Length<2)return false;
        var hsv=actor.Parts[0].Hsv;foreach(var p in actor.Parts)if(p.Pipeline>=20||p.Pipeline%5!=0||p.Hsv!=hsv||(p.Tint!=Vector3.One&&!tintedGenericPipelines.Contains(p.Pipeline+2)))return false;
        var template=TemplateFor(actor);var hint=actor.ModelHint;ModelBatch batch;
        if(hint.Slot>=0&&hint.Slot<modelBatchSlots.Count&&hint.Flags==flags&&hint.Cascades==cascades&&hint.MainGroup==MainGroupToken&&hint.Cell==cell&&hint.X==x&&hint.Y==y&&hint.Z==z)batch=modelBatchSlots[hint.Slot];
        else{var key=new ModelBatchKey(template.Id,flags,cascades,cell,x,y,z,MainGroupToken);if(!modelBatches.TryGetValue(key,out batch)){batch=new ModelBatch{Template=template,Slot=modelBatchSlots.Count,Packets=new int[template.Parts.Length],Constants=new byte[template.Parts.Length][],Recipes=new WorldRecipe[template.Parts.Length]};
                for(int i=0;i<template.Parts.Length;i++){var p=template.Parts[i];batch.Constants[i]=WorldConstants.Object(WorldLayouts[p.Pipeline+2],MatrixD.Identity,p.Tint,Vector3.Zero,p.Emissive,0,null,p.Dither,lod:p.Lod);}modelBatches.Add(key,batch);modelBatchSlots.Add(batch);}
            hint.Slot=batch.Slot;hint.Flags=flags;hint.Cascades=cascades;hint.MainGroup=MainGroupToken;hint.Cell=cell;hint.X=x;hint.Y=y;hint.Z=z;}
        if(batch.Frame!=E.BatchFrame){using var sharedOwner=WithPacketOwner(0);batch.Frame=E.BatchFrame;batch.Count=0;batch.Origin=origin;batch.Radius=radius;activeModelBatches.Add(batch);modelBatchStats.Batches++;
            for(int i=0;i<template.Parts.Length;i++){var p=template.Parts[i];if(WorldRecipesEnabled)EnsureWorldRecipe(ref batch.Recipes[i],p.Pipeline+2,p.Mesh,p.Color,p.Normal,p.Extensions,p.Alpha,batch.Constants[i].Length);
                using var oldOrderScope=WithOldOrder(p.OldOrder);MainPart=(uint)i;batch.Packets[i]=(int)AddWorldPacket(p.Pipeline+2,p.Mesh,p.Color,p.Normal,p.Extensions,p.Alpha,batch.Constants[i],batch.Instances,1,flags|p.ViewFlags,cascades,origin,radius,false,batch.Recipes[i],false);modelBatchStats.Packets++;E.RigidBatchPackets++;}}
        int at=checked((int)batch.Count*64);if(batch.Count>=1000000)throw new InvalidOperationException("Model instance budget exceeded.");Grow(ref batch.Instances,at,64);
        // Actor.Instance carries its current camera-relative transform; HSV varies
        // by actor while immutable material state belongs to each model part.
        unsafe{fixed(byte* bytes=actor.Instance){float* f=(float*)bytes;f[12]=hsv.X;f[13]=hsv.Y;f[14]=hsv.Z;f[15]=0;}}Buffer.BlockCopy(actor.Instance,0,batch.Instances,at,64);
        if(batch.Count!=0){if(batch.Radius<0||radius<0)batch.Radius=-1;else{var shift=origin-batch.Origin;float distance=shift.Length();if(distance+radius>batch.Radius){if(distance+batch.Radius<=radius){batch.Origin=origin;batch.Radius=radius;}else{float combined=(distance+batch.Radius+radius)*.5f;if(distance>0)batch.Origin+=shift*((combined-batch.Radius)/distance);batch.Radius=combined;}}}}
        batch.Count++;modelBatchStats.Actors++;modelBatchStats.Parts+=(uint)actor.Parts.Length;modelBatchStats.InstanceBytes+=64;
        if(captureLogicalSelection)foreach(var p in template.Parts)NoteLogicalDraw(p.Pipeline+2,p.Mesh,1,flags|p.ViewFlags,cascades);LogicalRigidDraws+=(uint)actor.Parts.Length;return true;
    }
    void FinalizeModelBatches(){foreach(var batch in activeModelBatches)for(int i=0;i<batch.Packets.Length;i++){var p=E.WorldPackets[batch.Packets[i]];p.Instances=batch.Instances;p.Count=batch.Count;p.Origin=batch.Origin;p.Radius=batch.Radius;p.InstanceCapacity=true;E.WorldPackets[batch.Packets[i]]=p;}}
}
public struct ModelBatchStats {public uint Enabled,Actors,Parts,Batches,Packets;public ulong InstanceBytes;}
