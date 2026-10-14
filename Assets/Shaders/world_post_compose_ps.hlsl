[[vk::binding(128,0)]] Texture2D<float> depth_image;
[[vk::binding(152,0)]] Texture2D<float4> source;
[[vk::binding(5,0)]] cbuffer ComposeDimensions {float2 input_size;float2 output_size;float4 hdr_mode;};
[[vk::binding(256,0)]] SamplerState linear_sampler;
struct Result {float4 color:SV_Target;float depth:SV_Depth;};
// Stock tonemapping produces encoded sRGB in an UNORM storage image. Decode
// once before the sRGB presentation attachment performs its final encode.
Result main(float4 position:SV_Position) {
    Result r;float3 x=source.SampleLevel(linear_sampler,position.xy/output_size,0).rgb;
    r.color=hdr_mode.x>0.5?float4(max(x,0),1):float4(select(x<=.04045,x/12.92,pow((x+.055)/1.055,2.4)),1);
    r.depth=depth_image[min(uint2(position.xy*input_size/output_size),uint2(input_size)-1)];return r;
}
