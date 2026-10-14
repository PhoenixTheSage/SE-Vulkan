[[vk::binding(128,0)]] Texture2D<float4> source;
[[vk::binding(129,0)]] Texture2D<float4> interior;
[[vk::binding(130,0)]] Texture2D<float4> blurred;
[[vk::binding(131,0)]] Texture2D<float4> overlap;
float3 decode(float3 x){return select(x<=.04045,x/12.92,pow((x+.055)/1.055,2.4));}
float3 encode(float3 x){return select(x<=.0031308,x*12.92,1.055*pow(max(x,0),1/2.4)-.055);}
// Texture masks reproduce stock highlight stencil bits 64 and 128 while
// retaining the backend's sampled D32 reverse-depth attachment.
float4 main(float4 position:SV_Position):SV_Target {
    uint2 p=uint2(position.xy);float4 original=source[p];
    if(overlap[p].a>0)return original;
    float4 outline=blurred[p];float3 color=decode(original.rgb);
    color=any(interior[p]!=0)?lerp(color,outline.rgb,outline.a):color+outline.rgb;
    return float4(encode(color),original.a);
}
