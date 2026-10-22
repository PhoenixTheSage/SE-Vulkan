using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private PostprocessFunctions postApi;
    private PostprocessFunctions PostApi=>postApi??(postApi=new PostprocessFunctions(GetSymbol));
    public readonly Dictionary<uint,ShaderMetadata> PostprocessLayouts=new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int HbaoJitterFn(IntPtr ctx,float[] values,uint count);
    private HbaoJitterFn hbaoJitter;
    public void InitializeHbaoJitter(int? randomSeed=null){
        var random=randomSeed.HasValue?new VRage.Library.Utils.MyRandom(randomSeed.Value):new VRage.Library.Utils.MyRandom();
        var values=new float[64];
        for(int slice=0;slice<16;slice++){float angle=(float)Math.PI*2f*random.NextFloat()/8f;values[slice*4]=(float)Math.Cos(angle);values[slice*4+1]=(float)Math.Sin(angle);values[slice*4+2]=random.NextFloat();values[slice*4+3]=random.NextFloat();}
        hbaoJitter??=(HbaoJitterFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_hbao_jitter"),typeof(HbaoJitterFn));Check(hbaoJitter(context,values,64),context);
    }
    public void ConfigurePostprocess(VulkanShaderLibrary library)
    {
        postprocessLibrary=library;
        InitializeHbaoJitter();
        for(uint i=0;i<PostprocessShaderVariants.Stages.Length;i++){var artifact=library.Load(PostprocessShaderVariants.Stages[i]);var bytes=SpirvRuntimeModule.FromArtifact(artifact);Check(PostApi.Init(context,i,bytes,(ulong)bytes.Length),context);PostprocessLayouts[i]=artifact.Layout;}
        using(var compiler=VulkanShaderCompiler.CreateBundled(System.IO.Path.Combine(resourceCacheDirectory,"HbaoBorder")))
        using(var batch=compiler.CreateBatch(library.HbaoBorderRequest(PostprocessShaderVariants.Stages[36]).ContentDirectory)){
            for(uint variant=0;variant<16;variant++){
                var artifact=VulkanShaderArtifact.FromCompilation(batch.Compile(library.HbaoBorderRequest(PostprocessShaderVariants.Stages[36+variant])),"hbao-negative-border-"+variant);
                if(artifact.Layout.SourceTransform!=HbaoBorderShaderAdapter.Contract || artifact.Layout.BaseSpirvSha256==null)throw new System.IO.InvalidDataException("HBAO border provenance missing.");
                var bytes=SpirvRuntimeModule.FromArtifact(artifact);Check(PostApi.Init(context,128+variant,bytes,(ulong)bytes.Length),context);
            }
        }
        for(uint i=0;i<HighlightShaderVariants.Blurs.Length;i++){var artifact=library.Load(HighlightShaderVariants.Blurs[i]);var bytes=SpirvRuntimeModule.FromArtifact(artifact);Check(PostApi.Init(context,62+i,bytes,(ulong)bytes.Length),context);PostprocessLayouts[62+i]=artifact.Layout;}
        for(uint i=0;i<PostprocessShaderVariants.ShadowBlurs.Length;i++){var a=library.Load(PostprocessShaderVariants.ShadowBlurs[i]);var b=SpirvRuntimeModule.FromArtifact(a);Check(PostApi.Init(context,126+i,b,(ulong)b.Length),context);PostprocessLayouts[126+i]=a.Layout;}
        var shadow=SpirvRuntimeModule.FromArtifact(library.Load("76fcba14c7221e345e678f03aa4620250f69f7c39d6ea83ed764123721fcbf89"));Check(PostApi.Shadows(context,shadow,(ulong)shadow.Length),context);
        InitializeShadowUsage(SpirvRuntimeModule.FromArtifact(library.Load("e3018f2ed8a28163358b30cf04ed44894f1cd2c8571abdab7256bd0aa4744457")));
    }
    public void SetPostprocess(NativePostprocessConfig config)=>Check(PostApi.Set(context,ref config),context);
    public void SetShadowOptions(bool blur)=>Check(PostApi.Options(context,blur?1u:0),context);
    public void SetShadowQuality(uint resolution,float bias)=>Check(PostApi.Quality(context,resolution,bias),context);
    public void SetHbao(NativeHbaoConfig config)=>Check(PostApi.Hbao(context,ref config),context);
    public NativePostprocessStats GetPostprocessStats(){var result=new NativePostprocessStats{Size=(uint)Marshal.SizeOf(typeof(NativePostprocessStats)),AbiVersion=AbiVersion};Check(PostApi.Stats(context,ref result),context);return result;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativePostprocessConfig
{
    public uint Size,AbiVersion,Flags,BloomSize,ChromaticIterations,Reserved;public ulong Dirt;
    [MarshalAs(UnmanagedType.ByValArray,SizeConst=64)]public byte[] Histogram;
    [MarshalAs(UnmanagedType.ByValArray,SizeConst=288)]public byte[] Ssao;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativePostprocessStats {public uint Size,AbiVersion;public ulong Frames,ExposurePasses,HistogramDispatches,BloomPasses,ToneDispatches,FxaaPasses,SsaoPasses,ChromaticDispatches,HbaoPasses,ShadowDispatches;}
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeHbaoConfig {public uint Size,AbiVersion,Flags,Coarse,Blur,Reserved;[MarshalAs(UnmanagedType.ByValArray,SizeConst=200)]public byte[] Constants;}
internal sealed class PostprocessFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int InitFn(IntPtr ctx,uint id,byte[] data,ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int SetFn(IntPtr ctx,ref NativePostprocessConfig config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int StatsFn(IntPtr ctx,ref NativePostprocessStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int HbaoFn(IntPtr ctx,ref NativeHbaoConfig config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int QualityFn(IntPtr ctx,uint resolution,float bias);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int ShadowsFn(IntPtr ctx,byte[] data,ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int OptionsFn(IntPtr ctx,uint flags);
    internal readonly OptionsFn Options;internal readonly QualityFn Quality;internal readonly InitFn Init;internal readonly SetFn Set;internal readonly StatsFn Stats;internal readonly HbaoFn Hbao;internal readonly ShadowsFn Shadows;
    internal PostprocessFunctions(Func<string,IntPtr> symbol){T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));Options=Get<OptionsFn>("sevk_set_shadow_options");Quality=Get<QualityFn>("sevk_set_shadow_quality");Init=Get<InitFn>("sevk_init_postprocess");Set=Get<SetFn>("sevk_set_postprocess");Stats=Get<StatsFn>("sevk_get_postprocess_stats");Hbao=Get<HbaoFn>("sevk_set_hbao");Shadows=Get<ShadowsFn>("sevk_init_stock_shadows");}
}
