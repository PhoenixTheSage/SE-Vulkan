using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;

[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativePostprocessGenerationStats
{
    public uint Size,AbiVersion;public ulong Active,Pending,Executions,LastCompletion,Completed;
    public uint LastFrame,Prepared,Retired,Format;public ulong Revision;
}
public sealed class PostprocessGenerationMetadata
{
    public ulong Token {get;}
    public long Epoch {get;}
    public string Fingerprint {get;}
    public IReadOnlyDictionary<uint,ShaderMetadata> Layouts {get;}
    public IReadOnlyDictionary<uint,string> Fingerprints {get;}
    internal PostprocessGenerationMetadata(ulong token,long epoch,string fingerprint,IDictionary<uint,ShaderMetadata> layouts,IDictionary<uint,string> fingerprints)
    {Fingerprints=new ReadOnlyDictionary<uint,string>(new Dictionary<uint,string>(fingerprints));Token=token;Epoch=epoch;Fingerprint=fingerprint;Layouts=new ReadOnlyDictionary<uint,ShaderMetadata>(new Dictionary<uint,ShaderMetadata>(layouts));}
}
public sealed partial class NativeBackend
{
    VulkanShaderLibrary postprocessLibrary;
    readonly Dictionary<ulong,PostprocessGenerationMetadata> postprocessCandidates=new();
    PostprocessGenerationMetadata pendingPostprocess;
    Func<bool> postprocessGuard;
    public PostprocessGenerationMetadata ActivePostprocessGeneration {get;private set;}
    public string PostprocessGenerationError {get;private set;}
    [StructLayout(LayoutKind.Sequential,Pack=8)]
    struct PostModule {public uint Size,AbiVersion,Stage,Reserved;public IntPtr Data;public ulong Bytes;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PreparePostFn(IntPtr ctx,ulong expected,[In] PostModule[] modules,uint count,out ulong token);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PostTokenFn(IntPtr ctx,ulong token);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PostStateFn(IntPtr ctx,uint stage,ref NativePostprocessGenerationStats state);
    PreparePostFn preparePost;PostTokenFn activatePost,discardPost;PostStateFn postState;
    T PostFunction<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(GetSymbol(name),typeof(T));
    void ResetPostprocessGenerations(){postprocessCandidates.Clear();pendingPostprocess=null;postprocessGuard=null;ActivePostprocessGeneration=null;PostprocessGenerationError=null;postprocessLibrary=null;}
    public NativePostprocessGenerationStats GetPostprocessGeneration(uint stage)
    {
        var state=new NativePostprocessGenerationStats{Size=(uint)Marshal.SizeOf(typeof(NativePostprocessGenerationStats)),AbiVersion=AbiVersion};
        postState??=PostFunction<PostStateFn>("sevk_get_postprocess_generation");Check(postState(context,stage,ref state),context);return state;
    }
    public ulong PreparePostprocessGeneration(IDictionary<uint,VulkanShaderArtifact> artifacts,string fingerprint)
    {
        if((GuestFeatures()&524288)==0)throw new NotSupportedException("Update SE-Vulkan native library: postprocess generations require bit19.");
        if(artifacts==null||artifacts.Count==0)throw new ArgumentException("Postprocess generation requires stages.");
        ulong expected=GetPostprocessGeneration(13).Revision;var pins=new List<GCHandle>();
        try
        {
            var modules=artifacts.OrderBy(p=>p.Key).Select(p=>{byte[] bytes=SpirvRuntimeModule.FromArtifact(p.Value);var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);pins.Add(pin);return new PostModule{Size=32,AbiVersion=AbiVersion,Stage=p.Key,Data=pin.AddrOfPinnedObject(),Bytes=(ulong)bytes.Length};}).ToArray();
            var layouts=ActivePostprocessGeneration==null?new Dictionary<uint,ShaderMetadata>(PostprocessLayouts):ActivePostprocessGeneration.Layouts.ToDictionary(p=>p.Key,p=>p.Value);
            var fingerprints=ActivePostprocessGeneration==null?new Dictionary<uint,string>():ActivePostprocessGeneration.Fingerprints.ToDictionary(p=>p.Key,p=>p.Value);
            foreach(var entry in artifacts){layouts[entry.Key]=entry.Value.Layout;fingerprints[entry.Key]=fingerprint;}
            preparePost??=PostFunction<PreparePostFn>("sevk_prepare_postprocess_generation");
            Check(preparePost(context,expected,modules,(uint)modules.Length,out ulong token),context);
            try{postprocessCandidates.Add(token,new PostprocessGenerationMetadata(token,GuestEpoch,fingerprint,layouts,fingerprints));}
            catch{DiscardPostprocessGeneration(token);throw;}return token;
        }
        finally{foreach(var pin in pins)pin.Free();}
    }
    public object[][] PostprocessCatalog()
    {
        if(postprocessLibrary==null)return Array.Empty<object[]>();
        return Enumerable.Range(0,62).Concat(Enumerable.Range(128,16)).Select(index=>{uint id=(uint)index;var artifact=postprocessLibrary.Load(PostprocessShaderVariants.Stages[id>=128?id-92:id]);return new object[]{id,artifact.Source};}).ToArray();
    }
    public object[] PostprocessGenerationState(uint id)
    {
        var state=GetPostprocessGeneration(id);string fingerprint=null;
        ActivePostprocessGeneration?.Fingerprints.TryGetValue(id,out fingerprint);
        return new object[]{state.Active,state.Executions,state.LastCompletion,state.Completed,state.LastFrame,pendingPostprocess?.Token??0UL,fingerprint,PostprocessGenerationError,GuestEpoch,state.Format,state.Revision};
    }
    public object[] CompilePostprocessGeneration(string identity,Dictionary<string,byte[]> sources,Dictionary<string,string> defines,uint[] stages)
    {
        if(postprocessLibrary==null)throw new InvalidOperationException("Stock postprocess owner is not initialized.");
        var generation=postprocessLibrary.CaptureSourceGeneration(identity,sources);
        var artifacts=new Dictionary<uint,VulkanShaderArtifact>();
        using(var compiler=VulkanShaderCompiler.CreateBundled(System.IO.Path.Combine(resourceCacheDirectory,"PostprocessGenerations")))
        foreach(uint id in stages)
        {
            if(id>=62&&id<128||id>=144)throw new ArgumentException("Stage is outside the postprocess owner generation.");
            string variant=PostprocessShaderVariants.Stages[id>=128?id-92:id];
            var request=postprocessLibrary.DerivedRequest(variant,generation,defines);
            if(id>=128)
            {
                var border=postprocessLibrary.HbaoBorderRequest(variant);request.HbaoNegativeBorder=true;
                request.HbaoBaseModule=border.HbaoBaseModule;request.HbaoBaseDependencies=border.HbaoBaseDependencies;
            }
            artifacts.Add(id,VulkanShaderArtifact.FromCompilation(compiler.Compile(request),variant+"/"+generation.Fingerprint));
        }
        ulong token=PreparePostprocessGeneration(artifacts,generation.Fingerprint);return new object[]{token,generation.Fingerprint};
    }
    public void QueuePostprocessGeneration(ulong token,Func<bool> guard=null)
    {
        if(!postprocessCandidates.TryGetValue(token,out var candidate)||candidate.Epoch!=GuestEpoch)throw new InvalidOperationException("Stale postprocess device generation.");
        if(pendingPostprocess!=null&&pendingPostprocess.Token!=token)DiscardPostprocessGeneration(pendingPostprocess.Token);
        pendingPostprocess=candidate;postprocessGuard=guard;
    }
    public void DiscardPostprocessGeneration(ulong token)
    {
        if(postprocessCandidates.TryGetValue(token,out var candidate)&&candidate.Epoch!=GuestEpoch){postprocessCandidates.Remove(token);if(pendingPostprocess==candidate)pendingPostprocess=null;return;}
        discardPost??=PostFunction<PostTokenFn>("sevk_discard_postprocess_generation");Check(discardPost(context,token),context);
        postprocessCandidates.Remove(token);if(pendingPostprocess?.Token==token)pendingPostprocess=null;
    }
    public bool ApplyPendingPostprocessGeneration()
    {
        var candidate=pendingPostprocess;if(candidate==null)return true;
        pendingPostprocess=null;
        try
        {
            if(postprocessGuard!=null&&!postprocessGuard())throw new InvalidOperationException("Stale Anomaly source generation.");
            if(candidate.Epoch!=GuestEpoch)throw new InvalidOperationException("Stale postprocess device generation.");
            activatePost??=PostFunction<PostTokenFn>("sevk_activate_postprocess_generation");Check(activatePost(context,candidate.Token),context);
            ActivePostprocessGeneration=candidate;postprocessCandidates.Remove(candidate.Token);PostprocessGenerationError=null;return true;
        }
        catch(Exception error){PostprocessGenerationError=error.Message;try{DiscardPostprocessGeneration(candidate.Token);}catch{} return false;}
        finally{postprocessGuard=null;}
    }
}
