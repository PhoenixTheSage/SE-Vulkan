[[vk::binding(0,0)]] Texture2D<float4> Source;
[[vk::binding(1,0)]] SamplerState SourceSampler;
// Normalize DDS dimensions/formats on the GPU without changing data channels,
// multiplying by alpha or applying GUI/display encoding.
float4 main(float2 uv : TEXCOORD0) : SV_Target0 {
    return Source.SampleLevel(SourceSampler, uv, 0);
}
