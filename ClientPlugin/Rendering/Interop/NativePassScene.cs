using System;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

namespace ClientPlugin.Rendering.Interop;

[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePassPart {public ulong Mesh,Color,Normal,Extensions,Alpha;public uint First,Count,Material,Shader,Flags,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativePassActor {
    public ulong Handle,Family,Group;public uint Owner,Flags;
    public fixed double Translation[3];public fixed double Bounds[6];public fixed float Basis[9];public float Scale;
    public fixed ushort KeyColor[3];public ushort Reserved;public NativePassLod Lod;public fixed double LastCamera[3];public uint HaveCamera,AppearanceOffset,AppearanceCount,Padding;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativePassAppearance {public uint Material,Visible;public fixed ushort Color[4];}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePassLod {public int Current,Previous;public float Transition,Start;public uint Initialized;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativePassFrame {
    public fixed double Camera[3];public fixed double Planes[216];public uint Enabled,CascadeCount;public float ProbeDistance,ShadowDistance,Coefficient,Delta;
    public fixed float Global[9];public uint GlobalFlags;public int Selection;public fixed int Passes[33];public double SmoothDistance;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativePassView {
    public uint Id,Kind,Flags,Light;public fixed double Planes[24];public double MinimumSizeSquared,MaximumDistance;public int MinimumLod,Shift,VisibleShift,Padding;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePassQuery {public ulong Group,Views;public uint Representative,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePassQueryResult {public ulong Group,HiddenViews;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePassMissing {public ulong Family;public uint Lod,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePassTransparent {public ulong Actor;public uint Owner,Lod;public float Alpha;public uint Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePassFallback {public ulong Actor;public uint Owner,Reserved;public NativePassLod Lod;public uint Padding;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePassGroupAudit {public uint View,Kind,Shader,First,Count,Instances,OwnerOffset,Flags;public ulong Mesh,Color,Normal,Extensions,Alpha;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePassProfile {public uint Size,AbiVersion;public ulong Prepares,Publishes,CommittedActors,SelectedActors,EmittedRows,StreamBytes,Groups;public double CommitsUs,PrepareUs,RequestsUs,PublishUs,EmitUs,SpotsUs;public ulong AppearanceEntries;public double ActorParseUs,ActorApplyUs;}
public sealed class NativePassPreparation {public NativePassQuery[] Queries;public NativePassMissing[] Missing;public NativePassFallback[] Fallbacks;}

public sealed partial class NativeBackend {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassPipelineFn(IntPtr ctx,uint id,byte[] vs,ulong vb,byte[] ps,ulong pb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassPlaceholderFn(IntPtr ctx,byte[] bytes,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassModelFn(IntPtr ctx,IntPtr parts,uint count,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassMixedModelFn(IntPtr ctx,IntPtr parts,uint count,uint glass,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassTransparentFn(IntPtr ctx,[Out]NativePassTransparent[] requests,uint capacity,out uint count);
    PassMixedModelFn passMixedModel;PassTransparentFn passTransparent;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassFamilyFn(IntPtr ctx,float[] distances,uint count,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassBindFn(IntPtr ctx,ulong family,uint lod,ulong model);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassActorFn(IntPtr ctx,ref NativePassActor actor,IntPtr appearance,uint count,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassUpdateFn(IntPtr ctx,IntPtr actors,uint count,IntPtr appearance,uint appearanceCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassRemoveFn(IntPtr ctx,uint kind,ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassPrepareFn(IntPtr ctx,ref NativePassFrame frame,IntPtr views,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassGetFn(IntPtr ctx,IntPtr queries,uint queryCapacity,out uint queryCount,IntPtr missing,uint missingCapacity,out uint missingCount,IntPtr fallback,uint fallbackCapacity,out uint fallbackCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassPublishFn(IntPtr ctx,IntPtr results,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassSelectorFn(IntPtr ctx,ulong actor,uint write,ref NativePassLod lod,[In,Out]double[] camera,ref uint haveCamera);
    PassPipelineFn passPipeline;PassPlaceholderFn passPlaceholder;PassModelFn passModel;PassFamilyFn passFamily;PassBindFn passBind;PassActorFn passActor;PassUpdateFn passUpdate;PassRemoveFn passRemove;PassPrepareFn passPrepare;PassGetFn passGet;PassPublishFn passPublish;NativeFunctions.ContextFn passCancel;
    public bool NativePassReady{get;private set;}
    NativeFunctions.ContextFn passClear;PassSelectorFn passSelector;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassVisibleFn(IntPtr ctx,[Out]uint[] owners,uint capacity,out uint count);
    PassVisibleFn passVisible;uint[] passVisibleOwners=Array.Empty<uint>();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassAuditFn(IntPtr ctx,[Out]NativePassGroupAudit[] groups,uint capacity,out uint count,[Out]uint[] owners,uint ownerCapacity,out uint ownerCount);
    PassAuditFn passAudit;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PassProfileFn(IntPtr ctx,uint enable,ref NativePassProfile profile);
    PassProfileFn passProfile;
    T PassFunction<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(GetSymbol(name),typeof(T));
    public void ConfigureNativePasses(VulkanShaderLibrary library){
        if(NativePassReady)return;if(!WorldReady)throw new InvalidOperationException("Configure world before native pass models.");
        passPipeline=PassFunction<PassPipelineFn>("sevk_pass_init_pipeline");passPlaceholder=PassFunction<PassPlaceholderFn>("sevk_pass_set_placeholder");
        foreach(var item in NativePassShaderVariants.Materials){byte[] vs=SpirvRuntimeModule.FromArtifact(library.Load(item.Vertex)),ps=SpirvRuntimeModule.FromArtifact(library.Load(item.Pixel));Check(passPipeline(context,item.Id,vs,(ulong)vs.Length,ps,(ulong)ps.Length),context);}
        var layout=library.Load(NativePassShaderVariants.Materials[0].Vertex).Layout;byte[] placeholder=WorldConstants.Object(layout,MatrixD.Identity,Vector3.One,Vector3.Zero,0,0,lod:255);if(placeholder.Length!=128)throw new InvalidOperationException("Stock pass placeholder ABI changed.");Check(passPlaceholder(context,placeholder,128),context);NativePassReady=true;if(ClientPlugin.Diagnostics.RendererCpuProfiler.Enabled)GetPassProfile(true);
    }
    public unsafe ulong RegisterPassModel(NativePassPart[] parts){passModel??=PassFunction<PassModelFn>("sevk_pass_register_model");fixed(NativePassPart* input=parts){Check(passModel(context,(IntPtr)input,(uint)parts.Length,out ulong handle),context);return handle;}}
    public unsafe ulong RegisterMixedPassModel(NativePassPart[] parts,bool glass){passMixedModel??=PassFunction<PassMixedModelFn>("sevk_pass_register_mixed_model");fixed(NativePassPart* input=parts){Check(passMixedModel(context,(IntPtr)input,(uint)parts.Length,glass?1u:0u,out ulong handle),context);return handle;}}
    public NativePassTransparent[] GetPassTransparentRequests(){passTransparent??=PassFunction<PassTransparentFn>("sevk_pass_transparent_requests");Check(passTransparent(context,null,0,out uint count),context);var requests=new NativePassTransparent[count];if(count!=0)Check(passTransparent(context,requests,count,out count),context);return requests;}
    public ulong RegisterPassFamily(float[] distances){passFamily??=PassFunction<PassFamilyFn>("sevk_pass_register_family");Check(passFamily(context,distances,(uint)distances.Length,out ulong handle),context);return handle;}
    public void BindPassLevel(ulong family,uint lod,ulong model){passBind??=PassFunction<PassBindFn>("sevk_pass_bind_level");Check(passBind(context,family,lod,model),context);}
    public unsafe ulong RegisterPassActor(ref NativePassActor actor,NativePassAppearance[] appearance){passActor??=PassFunction<PassActorFn>("sevk_pass_register_actor");fixed(NativePassAppearance* input=appearance){Check(passActor(context,ref actor,(IntPtr)input,(uint)appearance.Length,out ulong handle),context);return handle;}}
    public unsafe void UpdatePassActors(NativePassActor[] actors,int count,NativePassAppearance[] appearance,int appearanceCount){if(count<0||count>actors.Length||appearanceCount<0||appearanceCount>appearance.Length)throw new ArgumentOutOfRangeException();passUpdate??=PassFunction<PassUpdateFn>("sevk_pass_update_actors");fixed(NativePassActor* inputs=actors)fixed(NativePassAppearance* materials=appearance)Check(passUpdate(context,(IntPtr)inputs,(uint)count,(IntPtr)materials,(uint)appearanceCount),context);}
    public void RemovePassResource(uint kind,ulong handle){passRemove??=PassFunction<PassRemoveFn>("sevk_pass_remove");Check(passRemove(context,kind,handle),context);}
    public unsafe void PreparePassFrame(ref NativePassFrame frame,NativePassView[] views,int count){if(count<0||count>views.Length)throw new ArgumentOutOfRangeException();passPrepare??=PassFunction<PassPrepareFn>("sevk_pass_prepare");fixed(NativePassView* input=views)Check(passPrepare(context,ref frame,(IntPtr)input,(uint)count),context);}
    public unsafe NativePassPreparation GetPassPreparation(){
        passGet??=PassFunction<PassGetFn>("sevk_pass_get_preparation");Check(passGet(context,IntPtr.Zero,0,out uint q,IntPtr.Zero,0,out uint m,IntPtr.Zero,0,out uint f),context);
        var result=new NativePassPreparation{Queries=new NativePassQuery[q],Missing=new NativePassMissing[m],Fallbacks=new NativePassFallback[f]};
        fixed(NativePassQuery* queries=result.Queries)fixed(NativePassMissing* missing=result.Missing)fixed(NativePassFallback* fallback=result.Fallbacks)Check(passGet(context,(IntPtr)queries,q,out q,(IntPtr)missing,m,out m,(IntPtr)fallback,f,out f),context);return result;
    }
    public unsafe void PublishPassFrame(NativePassQueryResult[] results,int count){if(count<0||count>results.Length)throw new ArgumentOutOfRangeException();passPublish??=PassFunction<PassPublishFn>("sevk_pass_publish");fixed(NativePassQueryResult* input=results)Check(passPublish(context,(IntPtr)input,(uint)count),context);}
    public void CancelPassFrame(){passCancel??=PassFunction<NativeFunctions.ContextFn>("sevk_pass_cancel");Check(passCancel(context),context);}
    public void ClearPassFrame(){passClear??=PassFunction<NativeFunctions.ContextFn>("sevk_pass_clear_frame");Check(passClear(context),context);}
    public void PassActorSelector(ulong actor,bool write,ref NativePassLod lod,ref Vector3D camera,ref bool haveCamera){passSelector??=PassFunction<PassSelectorFn>("sevk_pass_actor_selector");var values=new[]{camera.X,camera.Y,camera.Z};uint have=haveCamera?1u:0u;Check(passSelector(context,actor,write?1u:0u,ref lod,values,ref have),context);camera=new Vector3D(values[0],values[1],values[2]);haveCamera=have!=0;}
    public uint[] GetPassVisibleOwners(out int count){passVisible??=PassFunction<PassVisibleFn>("sevk_pass_visible_owners");Check(passVisible(context,null,0,out uint needed),context);if(passVisibleOwners.Length<needed)Array.Resize(ref passVisibleOwners,(int)needed);Check(passVisible(context,passVisibleOwners,(uint)passVisibleOwners.Length,out needed),context);count=(int)needed;return passVisibleOwners;}
    public NativePassGroupAudit[] GetPassGroupAudit(out uint[] owners){passAudit??=PassFunction<PassAuditFn>("sevk_pass_group_audit");Check(passAudit(context,null,0,out uint count,null,0,out uint ownerCount),context);var groups=new NativePassGroupAudit[count];owners=new uint[ownerCount];Check(passAudit(context,groups,count,out count,owners,ownerCount,out ownerCount),context);return groups;}
    public NativePassProfile GetPassProfile(bool enable){passProfile??=PassFunction<PassProfileFn>("sevk_pass_profile");var profile=new NativePassProfile{Size=(uint)Marshal.SizeOf<NativePassProfile>(),AbiVersion=AbiVersion};Check(passProfile(context,enable?1u:0u,ref profile),context);return profile;}
}
