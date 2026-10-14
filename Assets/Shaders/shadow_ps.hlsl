struct Draw { row_major float4x4 world; float4 tint; float4 hsv; float dither; float emissivity; uint flags; uint cascade; };
[[vk::binding(1,1)]] SamplerState image_sampler;
[[vk::binding(0,1)]] Texture2D<float4> color_metal;
[[vk::binding(4,1)]] Texture2D<float4> alpha_mask;
[[vk::push_constant]] ConstantBuffer<Draw> draw;
void main(float4 position : SV_Position,float2 uv : TEXCOORD0) {
    if ((draw.flags&1)!=0) clip(((draw.flags&128)!=0?alpha_mask.Sample(image_sampler,uv).r:color_metal.Sample(image_sampler,uv).a)-.5);
    if (abs(draw.dither)>0) {
        const uint bayer[16]={0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5}; uint2 pixel=(uint2)position.xy;
        clip((bayer[(pixel.y&3)*4+(pixel.x&3)]+.5)/16.0-saturate(abs(draw.dither)));
    }
}
