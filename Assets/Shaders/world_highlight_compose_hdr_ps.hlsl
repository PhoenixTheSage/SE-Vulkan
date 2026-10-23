[[vk::binding(128,0)]] Texture2D<float4> source;
[[vk::binding(129,0)]] Texture2D<float4> interior;
[[vk::binding(130,0)]] Texture2D<float4> blurred;
[[vk::binding(131,0)]] Texture2D<float4> overlap;
// HDR display tenancy variant of world_highlight_compose_ps: the source is
// already linear FP16 scRGB, so the outline blends without sRGB decode/encode.
float4 main(float4 position:SV_Position):SV_Target {
    uint2 p=uint2(position.xy);float4 original=source[p];
    if(overlap[p].a>0)return original;
    float4 outline=blurred[p];float3 color=original.rgb;
    color=any(interior[p]!=0)?lerp(color,outline.rgb,outline.a):color+outline.rgb;
    return float4(color,original.a);
}
