using System;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;
public sealed class VulkanPostprocess
{
    public MyPostprocessSettings Settings=MyPostprocessSettings.Default;
    public MySSAOSettings Ssao=MySSAOSettings.Default;
    public MyHBAOData Hbao=MyHBAOData.Default;
    private bool reset=true,aoDebugEnabled=true;
    public bool Process(MyRenderMessageBase message)
    {
        if(message is MyRenderMessageUpdateDebugOverrides debug)aoDebugEnabled=debug.Overrides.Postprocessing && debug.Overrides.SSAO;
        if(message is MyRenderMessageUpdatePostprocessSettings p){Settings=p.Settings;return true;}
        if(message is MyRenderMessageUpdateSSAOSettings s){Ssao=s.Settings;return true;}
        if(message is MyRenderMessageUpdateHBAO pHbao){Hbao=pHbao.Settings;return true;}
        if(message.MessageType==MyRenderMessageEnum.UnloadData)reset=true;
        return false;
    }
    public NativePostprocessConfig Configure(NativeBackend backend,int width,int height,bool fxaa,ulong dirt=0,MatrixD? view=null,MatrixD? projection=null,bool ambientOcclusion=true)
    {
        bool enableAo=ambientOcclusion && aoDebugEnabled;
        var p=Settings;float range=p.HistogramLogMax-p.HistogramLogMin;
        if(!(range>0) || p.HistogramFilterMax<=p.HistogramFilterMin || p.HistogramFilterMin<0 || p.HistogramFilterMax>100)throw new ArgumentException("Invalid histogram range/percentiles.");
        var h=new ReflectedConstantBuffer(backend.PostprocessLayouts[3],"HistogramConstants");
        h.Set("HistogramScaleOffset",1/range,-p.HistogramLogMin/range,range,p.HistogramLogMin);
        h.Set("HistogramMinBrightness",(float)Math.Pow(2,p.MinEyeAdaptationLogBrightness));h.Set("HistogramMaxBrightness",(float)Math.Pow(2,p.MaxEyeAdaptationLogBrightness));h.Set("HistogramMinFilter",p.HistogramFilterMin*.01f);h.Set("HistogramMaxFilter",p.HistogramFilterMax*.01f);
        int w=Math.Max(1,width/2),heightHalf=Math.Max(1,height/2);h.Set("HistogramGatherArea",w,heightHalf,1f/(w*heightHalf),p.HistogramLuminanceThreshold);h.Set("HistogramSkyboxFactor",p.HistogramSkyboxFactor);
        var s=new ReflectedConstantBuffer(backend.PostprocessLayouts[14],"SSAO_params");var data=Ssao.Data;
        s.Set("min_radius",data.MinRadius);s.Set("max_radius",data.MaxRadius);s.Set("radius_grow",data.RadiusGrowZScale);s.Set("falloff",data.Falloff);s.Set("radius_bias",data.RadiusBias);s.Set("contrast",data.Contrast);s.Set("normalization",data.Normalization);s.Set("colorScale",data.ColorScale);
        Vector2[] kernel={new(1,1),new(.25f,-.25f),new(-1,-1),new(-.25f,.25f),new(.5f,.5f),new(.75f,-.75f),new(-.5f,-.5f),new(-.75f,.75f)};float[] points=new float[32],flipped=new float[32];
        for(int i=0;i<8;i++){var k=kernel[i]/(float)Math.Sqrt(2);points[i*4]=flipped[i*4]=k.X;points[i*4+1]=k.Y;flipped[i*4+1]=-k.Y;points[i*4+3]=(float)Math.Sqrt(Math.Max(0,1-k.LengthSquared()));}s.Set("OcclPos",points);s.Set("OcclPosFlipped",flipped);
        uint flags=1u|(p.EnableEyeAdaptation?2u:0)|(p.BloomEnabled?4u:0)|(p.HighQualityBloom?8u:0)|(p.BloomAntiFlickerFilter?16u:0)|(fxaa?32u:0)|(enableAo && Ssao.Enabled?64u:0)|(enableAo && Ssao.Enabled && Ssao.UseBlur?128u:0)|(p.Data.ChromaticFactor!=0 || p.Data.VignetteStart!=0?256u:0)|(reset?512u:0)|(p.EyeAdaptationPrioritizeScreenCenter?1024u:0)|(!p.EnableTonemapping?2048u:0);
        var config=new NativePostprocessConfig{Size=384,AbiVersion=NativeBackend.AbiVersion,Flags=flags,BloomSize=(uint)MathHelper.Clamp(p.BloomSize,1,8),ChromaticIterations=(uint)MathHelper.Clamp(p.ChromaticIterations,1,16),Histogram=h.Bytes,Ssao=s.Bytes,Dirt=dirt};backend.SetPostprocess(config);ConfigureHbao(backend,width,height,view??MatrixD.Identity,projection??MatrixD.Identity,enableAo);reset=false;return config;
    }
    private void ConfigureHbao(NativeBackend backend,int width,int height,MatrixD view,MatrixD projection,bool enableAo)
    {
        var p=Hbao;
        // Stock resolves SSAO first, then HBAO, under the shared user/debug gate.
        p.Enabled=p.Enabled && enableAo && !Ssao.Enabled;
        var b=new ReflectedConstantBuffer(backend.PostprocessLayouts[33],"GlobalConstantBuffer");
        void F(string name,float value)=>b.Set("g_"+name,value);
        if(p.Enabled)
        {
            float invX=1/(float)Math.Abs(projection.M11),invY=1/(float)Math.Abs(projection.M22);float radius=Math.Max(p.Radius,1e-6f),rScreen=radius*.5f/invY*height;
            b.Set("g_f2InvQuarterResolution",1f/((width+3)/4),1f/((height+3)/4));b.Set("g_f2InvFullResolution",1f/width,1f/height);b.Set("g_f2UVToViewA",2*invX,-2*invY);b.Set("g_f2UVToViewB",-invX,invY);
            F("fRadiusToScreen",rScreen);F("fR2",radius*radius);F("fNegInvR2",-1/(radius*radius));float bias=MathHelper.Clamp(p.Bias,0,.5f),scale=1/(1-bias);F("fNDotVBias",bias);F("fSmallScaleAOAmount",MathHelper.Clamp(p.SmallScaleAO,0,4)*scale*2);F("fLargeScaleAOAmount",MathHelper.Clamp(p.LargeScaleAO,0,4)*scale);F("fPowExponent",MathHelper.Clamp(p.PowerExponent,1,8));
            float near=Math.Max((float)(projection.M43/(projection.M33+1)),1e-6f),far=Math.Max((float)(projection.M43/projection.M33),near);float invNear=Math.Max(1/near,1e-6f),invFar=Math.Max(1/far,1e-6f);F("fLinearizeDepthA",invNear-invFar);F("fLinearizeDepthB",invFar);F("fInverseDepthRangeA",1);F("fInverseDepthRangeB",0);
            b.Set("g_f44NormalMatrix",NativeBackend.MatrixValues(view));F("fNormalDecodeScale",2);F("fNormalDecodeBias",-1);
            float background=Math.Max(p.BackgroundViewDepth,1e-6f);if(p.AdaptToFOV){float fov=2*(float)Math.Atan(invX);background=MathHelper.Lerp(6000,background,Math.Min(1,fov/MathHelper.ToRadians(30)));}F("fBackgroundAORadiusPixels",rScreen/background);F("fForegroundAORadiusPixels",rScreen/Math.Max(p.ForegroundViewDepth,1e-6f));
            float sharp=Math.Max(0,p.BlurSharpness);F("fBlurSharpness0",sharp*(p.BlurSharpnessFunctionEnable?Math.Max(0,p.BlurSharpnessFunctionForegroundScale):1));F("fBlurSharpness1",sharp);F("fBlurViewDepth0",p.BlurSharpnessFunctionEnable?Math.Max(0,p.BlurSharpnessFunctionForegroundViewDepth):0);F("fBlurViewDepth1",p.BlurSharpnessFunctionEnable?Math.Max(p.BlurSharpnessFunctionBackgroundViewDepth,Math.Max(0,p.BlurSharpnessFunctionForegroundViewDepth)+1e-6f):1);
            F("fViewDepthThresholdNegInv",p.DepthThresholdEnable?-1/Math.Max(p.DepthThreshold,1e-6f):0);F("fViewDepthThresholdSharpness",p.DepthThresholdEnable?Math.Max(0,p.DepthThresholdSharpness):1);
        }
        uint coarse=(p.BackgroundAOEnable?1u:0)|(p.ForegroundAOEnable?2u:0)|(p.DepthThresholdEnable?4u:0)|(p.UseGBufferNormals?8u:0);
        backend.SetHbao(new NativeHbaoConfig{Size=224,AbiVersion=NativeBackend.AbiVersion,Flags=(p.Enabled?1u:0)|(p.BlurEnable?2u:0)|(p.DepthClampToEdge?4u:0),Coarse=coarse,Blur=(p.BlurRadius4?1u:0)|(p.BlurSharpnessFunctionEnable?2u:0),Constants=b.Bytes});
    }
}
