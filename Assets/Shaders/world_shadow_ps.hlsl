struct Context { row_major float4x4 inverse_view_projection; row_major float4x4 shadow_matrices[4]; float4 splits; uint count,width,height,reserved; };
[[vk::binding(4,0)]] ConstantBuffer<Context> context;
[[vk::binding(128,0)]] Texture2D<float> depth_image;
[[vk::binding(148,0)]] Texture2D<float> shadow0;
[[vk::binding(149,0)]] Texture2D<float> shadow1;
[[vk::binding(150,0)]] Texture2D<float> shadow2;
[[vk::binding(151,0)]] Texture2D<float> shadow3;
[[vk::binding(260,0)]] SamplerComparisonState comparison_sampler;
float main(float4 position:SV_Position):SV_Target0 {
    float z=depth_image[(uint2)position.xy];if(z==0 || context.count==0)return 1;
    float4 p=mul(float4(position.x/context.width*2-1,1-position.y/context.height*2,z,1),context.inverse_view_projection);p/=p.w;
    uint cascade=0;while(cascade+1<context.count && length(p.xyz)>context.splits[cascade])++cascade;
    float4 q=mul(p,context.shadow_matrices[cascade]);float3 v=q.xyz/q.w;float2 uv=float2(v.x*.5+.5,.5-v.y*.5);
    if(any(uv<0) || any(uv>1) || v.z<0 || v.z>1)return 1;
    float result=0;
    [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++) {
        float2 t=uv+float2(x,y)/1024;
        if(cascade==0)result+=shadow0.SampleCmpLevelZero(comparison_sampler,t,v.z-.0005);
        else if(cascade==1)result+=shadow1.SampleCmpLevelZero(comparison_sampler,t,v.z-.0005);
        else if(cascade==2)result+=shadow2.SampleCmpLevelZero(comparison_sampler,t,v.z-.0005);
        else result+=shadow3.SampleCmpLevelZero(comparison_sampler,t,v.z-.0005);
    }return result/9;
}
