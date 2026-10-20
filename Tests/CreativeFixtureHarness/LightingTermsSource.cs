namespace CreativeFixtureHarness;

// Shared by the live fixture and its GPU transport/control regression.
public static class LightingTermsSource
{
    public const string Source=@"
#include <Lighting/Light.hlsli>
#include <Postprocess/PostprocessBase.hlsli>
Texture2D<float> Sun : register(t19);
struct Terms {
    float4 normalDepth : SV_Target0;
    float4 viewAO : SV_Target1;
    float4 iblLod : SV_Target2;
    float4 brdfGlossNV : SV_Target3;
    float4 diffuseAO : SV_Target4;
    float4 directShadow : SV_Target5;
    float4 foggedLightAO : SV_Target6;
};
Terms __pixel_shader(PostprocessVertex vertex) {
    SurfaceInterface s=read_gbuffer(vertex.position.xy);
    float farFactor=GetAmbientFarFactor(s.depth);
    float3 R=-reflect(s.V,s.N);R.x=-R.x;
    uint w,h,levels;SkyboxIBLCloseTex.GetDimensions(0,w,h,levels);
    levels-=frame_.Light.SkipIBLevels;
    float level=max((1-s.gloss)*levels,0)+frame_.Light.SkipIBLevels;
    float nv=saturate(dot(s.N,s.V));
    float2 brdf=AmbientBRDFTex.Sample(DefaultSampler,float2(s.gloss,nv));
    float indirectAO=saturate(1-(1-s.ao)*frame_.Light.aoIndirectLight);
    float lightAO=saturate(1-(1-s.ao)*frame_.Light.aoDirLight);
    float shadow=1-(1-Sun[vertex.position.xy])*(1-frame_.Light.shadowFadeout);
    float3 diffuse=AmbientDiffuse(s.albedo,s.N,farFactor)*frame_.Light.ambientDiffuseFactor*indirectAO;
    float3 direct=MainDirectionalLight(s)*shadow*lightAO;
    float3 lit=s.albedo*s.emissive*frame_.Post.BloomEmissiveness+diffuse+direct;
    lit+=AmbientSpecular(s.f0,s.gloss,s.N,s.V,farFactor)*frame_.Light.ambientSpecularFactor*indirectAO;
    Terms result;
    result.normalDepth=float4(s.N,s.depth);result.viewAO=float4(s.V,s.ao);
    result.iblLod=float4(SampleIBL(R,level,farFactor),level);
    result.brdfGlossNV=float4(brdf,s.gloss,nv);result.diffuseAO=float4(diffuse,indirectAO);
    result.directShadow=float4(direct,shadow);result.foggedLightAO=float4(Fog(lit,s.depth),lightAO);
    return result;
}";
}
