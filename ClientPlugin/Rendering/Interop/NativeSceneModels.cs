using System;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    public bool BulkModelsEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_BULK_MODELS")=="1";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int RegisterModelFn(IntPtr context,IntPtr parts,uint count,byte[] objects,uint bytes,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ClearModelsFn(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int AppendModelsFn(IntPtr context,IntPtr instances,uint count,IntPtr order,uint legacyCount,IntPtr remap);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ModelStatsFn(IntPtr context,ref NativeModelGroupStats stats);
    RegisterModelFn registerModel;ClearModelsFn clearModels;AppendModelsFn appendModels;ModelStatsFn modelStats;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int RegisterModelOrderedFn(IntPtr context,IntPtr parts,IntPtr orders,uint count,byte[] objects,uint bytes,out ulong handle);
    RegisterModelOrderedFn registerModelOrdered;
    NativeModelInstance[] modelInstances=Array.Empty<NativeModelInstance>();
    uint[] modelLegacyOrder=Array.Empty<uint>(),modelLegacyRemap=Array.Empty<uint>();
    uint nativeModelEpoch=1,nativeModelBatchEpoch,modelSubmissionOrder,nativeModelPackets,nativeModelResourceRevision,nativeModelRegistrations;
    int nativeModelCount;
    bool modelIndicesRemapped;
    NativeModelGroupStats completedModelGroups;
    public NativeModelGroupStats GetModelGroupStats()=>completedModelGroups;
    void ResetNativeModels()
    {
        if(context!=IntPtr.Zero && nativeModelCount>0 && !modelIndicesRemapped)throw new InvalidOperationException("Model retirement cannot discard an unpublished bulk scene frame.");
        if(clearModels!=null && context!=IntPtr.Zero)Check(clearModels(context),context);
        nativeModelEpoch++;nativeModelCount=0;nativeModelPackets=0;nativeModelRegistrations=0;
    }
    void BeginNativeModels()
    {
        nativeModelCount=0;nativeModelPackets=0;
        if(nativeModelBatchEpoch!=modelBatchEpoch || nativeModelRegistrations>=32768){ResetNativeModels();nativeModelBatchEpoch=modelBatchEpoch;}modelSubmissionOrder=0;modelIndicesRemapped=false;
        completedModelGroups=new NativeModelGroupStats{Size=48,AbiVersion=AbiVersion,Enabled=BulkModelsEnabled?1u:0u};
    }
    unsafe bool TryBulkModel(PreparedActor actor,uint flags,uint cascades,Vector3 origin,float radius,int cell,int x,int y,int z)
    {
        if(!BulkModelsEnabled || !ModelBatchingEnabled || !RigidBatchingEnabled || !WorldRecipesEnabled || ObjectRetentionEnabled || actor.Parts.Length==0)return false;
        if(actor.NativeEligibilityEpoch!=nativeModelEpoch){
            var hsv=actor.Parts[0].Hsv;bool eligible=true;
            foreach(var p in actor.Parts)if(p.Pipeline>=20 || p.Pipeline%5!=0 || p.Hsv!=hsv || (p.Tint!=Vector3.One&&!tintedGenericPipelines.Contains(p.Pipeline+2)))eligible=false;
            actor.NativeEligibility=eligible;actor.NativeEligibilityEpoch=nativeModelEpoch;
        }
        if(!actor.NativeEligibility)return false;
        var template=TemplateFor(actor);
        if(template.NativeEpoch==nativeModelEpoch && template.NativeResourceRevision!=nativeModelResourceRevision){foreach(var recipe in template.NativeRecipes)if(!recipe.Alive)template.NativeEpoch=0;template.NativeResourceRevision=nativeModelResourceRevision;}
        if(template.NativeEpoch!=nativeModelEpoch){
            if(registerModel==null){registerModel=(RegisterModelFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_register_model"),typeof(RegisterModelFn));clearModels=(ClearModelsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_clear_models"),typeof(ClearModelsFn));appendModels=(AppendModelsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_append_models"),typeof(AppendModelsFn));modelStats=(ModelStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_model_group_stats"),typeof(ModelStatsFn));}
            var parts=new NativeModelPart[template.Parts.Length];template.NativeRecipes=new WorldRecipe[parts.Length];int used=0;var constants=new byte[template.Parts.Length][];
            for(int i=0;i<parts.Length;i++){var p=template.Parts[i];constants[i]=WorldConstants.Object(WorldLayouts[p.Pipeline+2],MatrixD.Identity,p.Tint,Vector3.Zero,p.Emissive,0,null,p.Dither,lod:p.Lod);WorldRecipe recipe=null;EnsureWorldRecipe(ref recipe,p.Pipeline+2,p.Mesh,p.Color,p.Normal,p.Extensions,p.Alpha,constants[i].Length);template.NativeRecipes[i]=recipe;parts[i]=new NativeModelPart{Recipe=recipe.Handle,ObjectOffset=(uint)used,Reserved=p.ViewFlags};used=checked(used+constants[i].Length);}
            var objects=new byte[used];for(int i=0;i<parts.Length;i++)Buffer.BlockCopy(constants[i],0,objects,(int)parts[i].ObjectOffset,constants[i].Length);
            var orders=new NativeOldPartOrder[parts.Length];bool ordered=false;for(int i=0;i<parts.Length;i++){orders[i]=template.Parts[i].OldOrder;ordered|=orders[i].Valid!=0;}
            fixed(NativeModelPart* definitions=parts){if(ordered){registerModelOrdered??=(RegisterModelOrderedFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_register_model_ordered"),typeof(RegisterModelOrderedFn));fixed(NativeOldPartOrder* rows=orders)Check(registerModelOrdered(context,(IntPtr)definitions,(IntPtr)rows,(uint)parts.Length,objects,(uint)used,out template.NativeHandle),context);}else Check(registerModel(context,(IntPtr)definitions,(uint)parts.Length,objects,(uint)used,out template.NativeHandle),context);}
            template.NativeEpoch=nativeModelEpoch;template.NativeResourceRevision=nativeModelResourceRevision;nativeModelRegistrations++;
        }
        if(nativeModelCount>=65536)throw new InvalidOperationException("Bulk model representation budget exceeded.");
        if(modelInstances.Length<=nativeModelCount)Array.Resize(ref modelInstances,Math.Max(256,modelInstances.Length*2));
        ref var instance=ref modelInstances[nativeModelCount++];instance.Model=template.NativeHandle;instance.Order=++modelSubmissionOrder;instance.Flags=flags;instance.Cascades=cascades;instance.Cell=(uint)cell;instance.X=x;instance.Y=y;instance.Z=z;instance.OriginX=origin.X;instance.OriginY=origin.Y;instance.OriginZ=origin.Z;instance.Radius=radius;instance.Reserved=NoteMainGroupOrder(flags,instance.Order);
        fixed(float* dest=instance.Instance)fixed(byte* source=actor.Instance){Buffer.MemoryCopy(source,dest,64,64);var hsv=actor.Parts[0].Hsv;dest[12]=hsv.X;dest[13]=hsv.Y;dest[14]=hsv.Z;dest[15]=0;}
        if(captureLogicalSelection)foreach(var p in actor.Parts)NoteLogicalDraw(p.Pipeline+2,p.Mesh,1,flags|p.ViewFlags,cascades);
        LogicalRigidDraws+=(uint)actor.Parts.Length;return true;
    }
    unsafe void QueueNativeModels()
    {
        if(nativeModelCount==0)return;
        if(modelIndicesRemapped)throw new InvalidOperationException("Bulk model frame was published twice.");
        if(modelLegacyOrder.Length<worldPackets.Count){Array.Resize(ref modelLegacyOrder,worldPackets.Count);Array.Resize(ref modelLegacyRemap,worldPackets.Count);}
        for(int i=0;i<worldPackets.Count;i++)modelLegacyOrder[i]=worldPackets[i].Order;
        fixed(NativeModelInstance* instances=modelInstances)fixed(uint* order=modelLegacyOrder)fixed(uint* remap=modelLegacyRemap)
            Check(appendModels(context,(IntPtr)instances,(uint)nativeModelCount,(IntPtr)order,(uint)worldPackets.Count,(IntPtr)remap),context);
        var stats=new NativeModelGroupStats{Size=48,AbiVersion=AbiVersion};Check(modelStats(context,ref stats),context);completedModelGroups=stats;nativeModelPackets=stats.Packets;
        submissionStats.ObjectBlobBytes+=stats.ObjectBytes;submissionStats.InstanceBlobBytes+=stats.InstanceBytes;
        // Logical ordering is preserved; downstream consumers require the new
        // physical index after native model groups are merged before legacy draws.
        modelIndicesRemapped=true;

    }
    uint SceneDrawIndex(uint draw){if(!modelIndicesRemapped)return draw;if(draw>=(uint)worldPackets.Count)throw new InvalidOperationException("Identity consumer outside legacy scene frame.");return modelLegacyRemap[draw];}

}
[StructLayout(LayoutKind.Sequential,Pack=8)]internal struct NativeModelPart {public ulong Recipe;public uint ObjectOffset,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeModelInstance
{
    public ulong Model;public uint Order,Flags,Cascades,Cell;public int X,Y,Z;public float OriginX,OriginY,OriginZ,Radius;public uint Reserved;public fixed float Instance[16];
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeModelGroupStats
{
    public uint Size,AbiVersion,Enabled,Instances,Groups,Packets,Templates,LegacyDraws;public ulong ObjectBytes,InstanceBytes;
}
