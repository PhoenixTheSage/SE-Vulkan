// Renderer temporal upscale. DLSS evaluate is the driver NGX path. FRS
// evaluate is the FidelityFX Vulkan library (FSR 3.1.4). This compute
// pass remains the fallback. It is not NGX and it is not FSR.
[[vk::binding(0, 0)]] cbuffer UpscaleConstants {
    float2 input_size;
    float2 output_size;
    float history_weight;
    float pad0;
    float jitter_x;
    float jitter_y;
};
[[vk::binding(1, 0)]] Texture2D<float4> source_color;
[[vk::binding(2, 0)]] Texture2D<float4> history_color;
[[vk::binding(3, 0)]] Texture2D<float2> motion_image;
[[vk::binding(4, 0)]] SamplerState linear_sampler;
[[vk::binding(5, 0)]] RWTexture2D<float4> destination;

float Catmull(float x)
{
    float a = abs(x);
    if (a < 1)
        return (1.5 * a - 2.5) * a * a + 1;
    if (a < 2)
        return ((-0.5 * a + 2.5) * a - 4) * a + 2;
    return 0;
}

float3 SampleFiltered(Texture2D<float4> tex, float2 uv, float2 size)
{
    float2 pixel = uv * size - 0.5;
    float2 base = floor(pixel);
    float2 fraction = pixel - base;
    float3 accumulated = 0;
    float weight_sum = 0;
    [unroll]
    for (int y = -1; y <= 2; y++)
    {
        [unroll]
        for (int x = -1; x <= 2; x++)
        {
            float weight = Catmull(float(x) - fraction.x) * Catmull(float(y) - fraction.y);
            float2 sample_uv = (base + float2(x, y) + 0.5) / size;
            accumulated += tex.SampleLevel(linear_sampler, sample_uv, 0).rgb * weight;
            weight_sum += weight;
        }
    }
    return weight_sum > 0 ? accumulated / weight_sum : 0;
}

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= uint(output_size.x) || id.y >= uint(output_size.y))
        return;
    float2 uv = (float2(id.xy) + 0.5) / output_size;
    float2 source_uv = uv + float2(jitter_x, jitter_y) / max(input_size, float2(1, 1));
    float3 current = SampleFiltered(source_color, source_uv, input_size);
    float3 color = current;
    if (history_weight > 0)
    {
        float2 motion = motion_image.SampleLevel(linear_sampler, source_uv, 0).xy;
        float2 history_uv = uv - motion / max(input_size, float2(1, 1));
        float3 previous = SampleFiltered(history_color, history_uv, output_size);
        float current_luma = dot(current, float3(0.2126, 0.7152, 0.0722));
        float history_luma = dot(previous, float3(0.2126, 0.7152, 0.0722));
        float accept = history_uv.x > 0 && history_uv.y > 0 && history_uv.x < 1 && history_uv.y < 1
            && abs(current_luma - history_luma) < max(0.25, current_luma * 2);
        color = lerp(current, previous, accept ? history_weight : 0);
    }
    destination[id.xy] = float4(max(color, 0), 1);
}
