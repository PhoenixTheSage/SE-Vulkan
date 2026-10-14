[[vk::binding(128,0)]] Texture2D<float> depth_image;
[[vk::binding(152,0)]] Texture2D<float4> hdr;
[[vk::binding(5,0)]] cbuffer ComposeDimensions {float2 input_size;float2 output_size;float4 hdr_mode;};
[[vk::binding(256,0)]] SamplerState linear_sampler;
struct Result { float4 color:SV_Target0; float depth:SV_Depth; };
Result main(float4 position:SV_Position) {
    Result r;float3 x=max(hdr.SampleLevel(linear_sampler,position.xy/output_size,0).rgb,0);
    r.color=hdr_mode.x>0.5?float4(x,1):float4(saturate((x*(2.51*x+.03))/(x*(2.43*x+.59)+.14)),1);
    r.depth=depth_image[min(uint2(position.xy*input_size/output_size),uint2(input_size)-1)];return r;
}
