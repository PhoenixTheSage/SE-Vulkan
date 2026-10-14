[[vk::binding(0,1)]] Texture2D<float4> image;
[[vk::binding(1,1)]] SamplerState image_sampler;
[[vk::constant_id(0)]] const uint encode_display=1;
struct Push { float4 rect; float4 uv; float4 color; float2 screen; float angle; uint flags; float2 origin; float2 pad; };
[[vk::push_constant]] ConstantBuffer<Push> draw;
float4 main(float4 position : SV_Position, float2 uv : TEXCOORD0, float4 color : COLOR0) : SV_Target0 {
    float4 texel=image.Sample(image_sampler,uv);
    // Stock Sprites.hlsl premultiplies straight input when PREMULTIPLY_ALPHA
    // is defined, and always premultiplies the linear tint by tint alpha.
    if((draw.flags&4)!=0 && (draw.flags&2)==0) texel.rgb*=texel.a;
    if((draw.flags&6)!=0) color.rgb*=color.a;
    float4 sample=texel*color;
    if(encode_display!=0) sample.rgb=lerp(sample.rgb*12.92,1.055*pow(max(sample.rgb,0),1/2.4)-.055,step(.0031308,sample.rgb));
    return sample;
}
