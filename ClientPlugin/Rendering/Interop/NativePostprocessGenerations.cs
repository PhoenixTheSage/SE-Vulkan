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
    public ulong PreparePostprocessGeneration(IDictionary<uint,VulkanShaderArtifact> artifacts,string fingerprint)=>PrepareNativeGeneration(artifacts,fingerprint,false);
    ulong PrepareNativeGeneration(IDictionary<uint,VulkanShaderArtifact> artifacts,string fingerprint,bool restoresStock)
    {
        if((GuestFeatures()&524288)==0)throw new NotSupportedException("Update SE-Vulkan native library: postprocess generations require bit19.");
        if(artifacts!=null&&artifacts.Keys.Any(GeometryModule)&&(GuestFeatures()&GeometryGenerationFeature)==0)throw new NotSupportedException("Update SE-Vulkan native library: stock world geometry generations require bit31.");
        if(artifacts!=null&&artifacts.Keys.Any(id=>ExtendedGeometryModule(id)&&(NativeOwnerFeatures()&ExtendedGeometryFeature(id))==0))throw new NotSupportedException("Update SE-Vulkan: extended native geometry owner capabilities unavailable.");
        if(artifacts==null||artifacts.Count==0)throw new ArgumentException("Native owner generation requires stages.");
        if(artifacts.Keys.Any(id=>id>=256&&id<260)&&(GuestFeatures()&1048576)==0)throw new NotSupportedException("Update SE-Vulkan native library: lighting owner generations require bit20.");
        if(artifacts.Keys.Any(id=>id>=512&&id<515)&&(GuestFeatures()&16777216)==0)throw new NotSupportedException("Update SE-Vulkan native library: atmosphere owner generations require bit24.");
        if(artifacts.Keys.Any(id=>id==2048)&&(GuestFeatures()&1073741824)==0)throw new NotSupportedException("Update SE-Vulkan native library: OIT resolve generations require bit30.");
        if(artifacts.Keys.Any(id=>id>=1792&&id<1794)&&(GuestFeatures()&536870912)==0)throw new NotSupportedException("Update SE-Vulkan native library: shadow owner generations require bit29.");
        if(artifacts.Keys.Any(id=>id>=1536&&id<1546)&&(GuestFeatures()&268435456)==0)throw new NotSupportedException("Update SE-Vulkan native library: particle owner generations require bit28.");
        if(artifacts.Keys.Any(id=>id>=1280&&id<1284)&&(GuestFeatures()&134217728)==0)throw new NotSupportedException("Update SE-Vulkan native library: probe processing generations require bit27.");
        if(artifacts.Keys.Any(id=>id>=1024&&id<1034)&&(GuestFeatures()&67108864)==0)throw new NotSupportedException("Update SE-Vulkan native library: decal owner generations require bit26.");
        if(artifacts.Keys.Any(id=>id>=768&&id<773)&&(GuestFeatures()&33554432)==0)throw new NotSupportedException("Update SE-Vulkan native library: foliage owner generations require bit25.");
        if(artifacts.Values.Any(a=>UsesStageBindings(a.Layout))&&(GuestFeatures()&4194304)==0)
            throw new NotSupportedException("Update SE-Vulkan native library: catalog/extras native stage resources require bit22.");
        ulong expected=GetPostprocessGeneration(13).Revision;var pins=new List<GCHandle>();
        try
        {
            var modules=artifacts.OrderBy(p=>p.Key).Select(p=>{byte[] bytes=SpirvRuntimeModule.FromArtifact(p.Value);if(p.Key==1544)bytes=SpirvRuntimeModule.RebaseVertexBindings(bytes);var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);pins.Add(pin);return new PostModule{Size=32,AbiVersion=AbiVersion,Stage=p.Key,Data=pin.AddrOfPinnedObject(),Bytes=(ulong)bytes.Length};}).ToArray();
            var layouts=ActivePostprocessGeneration==null?new Dictionary<uint,ShaderMetadata>(PostprocessLayouts):ActivePostprocessGeneration.Layouts.ToDictionary(p=>p.Key,p=>p.Value);
            var fingerprints=ActivePostprocessGeneration==null?new Dictionary<uint,string>():ActivePostprocessGeneration.Fingerprints.ToDictionary(p=>p.Key,p=>p.Value);
            foreach(var entry in artifacts){layouts[entry.Key]=entry.Value.Layout;fingerprints[entry.Key]=fingerprint;}
            preparePost??=PostFunction<PreparePostFn>("sevk_prepare_postprocess_generation");
            ulong token;
            if(NativeOwnerFeatures()!=0){var modulePin=GCHandle.Alloc(modules,GCHandleType.Pinned);pins.Add(modulePin);var request=new NativeGeneration{Size=32,Abi=AbiVersion,Revision=expected,Flags=restoresStock?1u:0u,Count=(uint)modules.Length,Modules=modulePin.AddrOfPinnedObject()};prepareNative??=PostFunction<PrepareNativeFn>("sevk_prepare_native_generation");Check(prepareNative(context,ref request,out token),context);}
            else Check(preparePost(context,expected,modules,(uint)modules.Length,out token),context);
            try{postprocessCandidates.Add(token,new PostprocessGenerationMetadata(token,GuestEpoch,fingerprint,layouts,fingerprints));}
            catch{DiscardPostprocessGeneration(token);throw;}return token;
        }
        finally{foreach(var pin in pins)pin.Free();}
    }
    public object[][] PostprocessCatalog()
    {
        if(postprocessLibrary==null)return Array.Empty<object[]>();
        var ids=Enumerable.Range(0,62).Concat(Enumerable.Range(128,16));
        if((GuestFeatures()&1048576)!=0)ids=ids.Concat(Enumerable.Range(256,4));
        if((GuestFeatures()&16777216)!=0&&EnvironmentLayouts.ContainsKey(0)&&EnvironmentLayouts.ContainsKey(1))ids=ids.Concat(Enumerable.Range(512,3));
        if((GuestFeatures()&33554432)!=0&&EnvironmentLayouts.ContainsKey(8))ids=ids.Concat(Enumerable.Range(768,5));
        if((GuestFeatures()&67108864)!=0)ids=ids.Concat(Enumerable.Range(1024,10));
        if((GuestFeatures()&134217728)!=0)ids=ids.Concat(Enumerable.Range(1280,4));
        if((GuestFeatures()&268435456)!=0)ids=ids.Concat(Enumerable.Range(1536,10));
        if((GuestFeatures()&536870912)!=0)ids=ids.Concat(Enumerable.Range(1792,2));
        if((GuestFeatures()&1073741824)!=0)ids=ids.Concat(new[]{2048});
        return ids.Select(index=>{uint id=(uint)index;var artifact=postprocessLibrary.Load(NativeOwnerVariant(id));var dependencies=artifact.Layout.Dependencies.Select(d=>d.Path.Replace('\\','/')).Where(p=>p.StartsWith("root0/",StringComparison.OrdinalIgnoreCase)).Select(p=>p.Substring(6)).Append(artifact.Source).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p=>p,StringComparer.Ordinal).ToArray();return new object[]{id,artifact.Source,artifact.Profile,dependencies};}).Concat(GeometryCatalog()).Concat(ExtendedGeometryCatalog()).ToArray();
    }
    static string NativeOwnerVariant(uint id)
    {
        if(id==2048)return TransparencyShaderVariants.Resolve;
        if(id==1792)return "76fcba14c7221e345e678f03aa4620250f69f7c39d6ea83ed764123721fcbf89";
        if(id==1793)return "e3018f2ed8a28163358b30cf04ed44894f1cd2c8571abdab7256bd0aa4744457";
        if(id>=1536&&id<1546)return TransparencyShaderVariants.Particles[id-1536];
        if(id>=1280&&id<1284)return ProbeShaderVariants.Stages[id-1280];
        if(id>=1024&&id<1034){var pair=TransparencyShaderVariants.Decals[(id-1024)/2];return id%2==0?pair.Vertex:pair.Pixel;}
        if(id>=768&&id<773)return EnvironmentShaderVariants.Stages[id-764];
        if(id==514)return "1bb6f40fef889661d4a74d17f3dc6c2cdfbc083270382103ff395321ada20f05";
        if(id>=512&&id<514)return EnvironmentShaderVariants.Stages[id-512];
        if(id>=256&&id<260)return id==259?"7e7f8a00e6cfc94f5e7f149a9a4df82ce433557cf56c91d2a08dfaa598952493":WorldShaderVariants.Lighting[id-255];
        if(id<62||id>=128&&id<144)return PostprocessShaderVariants.Stages[id>=128?id-92:id];
        throw new ArgumentException("Stage is outside the captured native owner catalog.");
    }
    public object[] PostprocessGenerationState(uint id)
    {
        var state=GetPostprocessGeneration(id);string fingerprint=null;
        if(state.Active!=0)ActivePostprocessGeneration?.Fingerprints.TryGetValue(id,out fingerprint);
        return new object[]{state.Active,state.Executions,state.LastCompletion,state.Completed,state.LastFrame,pendingPostprocess?.Token??0UL,fingerprint,PostprocessGenerationError,GuestEpoch,state.Format,state.Revision};
    }
    public object[] CompilePostprocessGeneration(string identity,Dictionary<string,byte[]> sources,Dictionary<string,string> defines,uint[] stages)=>CompileNativeOwnerGeneration(identity,sources,defines,stages,false);
    public object[] CompileNativeOwnerGeneration(string identity,Dictionary<string,byte[]> sources,Dictionary<string,string> defines,uint[] stages,bool restoresStock,uint contracts=0)
    {
        if(postprocessLibrary==null)throw new InvalidOperationException("Stock postprocess owner is not initialized.");
        if((contracts&~1u)!=0||(contracts&1)!=0&&(NativeOwnerFeatures()&GBufferVelocityFeature)==0)throw new NotSupportedException("Update coordinated SE-Vulkan: requested GBuffer velocity owner contract unavailable.");
        var generation=postprocessLibrary.CaptureSourceGeneration(identity,sources);
        var artifacts=new Dictionary<uint,VulkanShaderArtifact>();
        using(var compiler=VulkanShaderCompiler.CreateBundled(System.IO.Path.Combine(resourceCacheDirectory,"PostprocessGenerations")))
        foreach(uint id in stages)
        {
            string variant=ExtendedGeometryModule(id)?"native-extended-"+id:GeometryModule(id)?"native-world-"+id:NativeOwnerVariant(id);
            if(id==2048&&(GuestFeatures()&1073741824)==0)throw new NotSupportedException("Update SE-Vulkan native library: OIT resolve generations require bit30.");
            if(id>=1792&&id<1794&&(GuestFeatures()&536870912)==0)throw new NotSupportedException("Update SE-Vulkan native library: shadow owner generations require bit29.");
            if(id>=1536&&id<1546&&(GuestFeatures()&268435456)==0)throw new NotSupportedException("Update SE-Vulkan native library: particle owner generations require bit28.");
            if(id>=1280&&id<1284&&(GuestFeatures()&134217728)==0)throw new NotSupportedException("Update SE-Vulkan native library: probe processing generations require bit27.");
            if(id>=1024&&id<1034&&(GuestFeatures()&67108864)==0)throw new NotSupportedException("Update SE-Vulkan native library: decal owner generations require bit26.");
            if(id>=768&&id<773&&(GuestFeatures()&33554432)==0)throw new NotSupportedException("Update SE-Vulkan native library: foliage owner generations require bit25.");
            if(id>=512&&id<515&&(GuestFeatures()&16777216)==0)throw new NotSupportedException("Update SE-Vulkan native library: atmosphere owner generations require bit24.");
            if(id>=256&&id<260&&(GuestFeatures()&1048576)==0)throw new NotSupportedException("Update SE-Vulkan native library: lighting owner generations require bit20.");
            var request=ExtendedGeometryModule(id)?ExtendedGeometryRequest(id,generation,defines,(contracts&1)!=0):GeometryModule(id)?GeometryRequest(id,identity,sources,defines,(contracts&1)!=0):postprocessLibrary.DerivedRequest(variant,generation,defines);
            if(id>=128&&id<144)
            {
                // A replacement without the quarter-depth descriptor has no border
                // fetch to adapt. Reuse its authenticated base variant in both modes.
                if(artifacts.TryGetValue(id-92,out var coarse)&&!coarse.Layout.Descriptors.Any(d=>d.Set==0&&d.RegisterClass=='t'&&d.RegisterIndex==0))
                {artifacts.Add(id,coarse);continue;}
                var border=postprocessLibrary.HbaoBorderRequest(variant);request.HbaoNegativeBorder=true;
                request.HbaoBaseModule=border.HbaoBaseModule;request.HbaoBaseDependencies=border.HbaoBaseDependencies;
            }
            artifacts.Add(id,VulkanShaderArtifact.FromCompilation(compiler.Compile(request),variant+"/"+generation.Fingerprint));
        }
        ulong token=PrepareNativeGeneration(artifacts,generation.Fingerprint,restoresStock);return new object[]{token,generation.Fingerprint};
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
