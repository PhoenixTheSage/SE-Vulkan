// Same bidirectional warp as SE-FG Interpolate.hlsl.
// Motion is current-to-previous pixels. previousPixel = currentPixel + motion.
// Near-zero motion, a footprint that leaves the image, or a rejected depth
// boundary keeps the current pixel. The renderer presents this image once.
[[vk::binding(0, 0)]] cbuffer InterpolateConstants {
    uint Width;
    uint Height;
    float InvWidth;
    float InvHeight;
    float MvScaleX;
    float MvScaleY;
    uint InvertedDepth;
    uint HasVolumeReactive;
};
[[vk::binding(1, 0)]] Texture2D<float4> CurrColor;
[[vk::binding(2, 0)]] Texture2D<float4> PrevColor;
[[vk::binding(3, 0)]] Texture2D<float2> MotionTex;
[[vk::binding(4, 0)]] SamplerState LinearClamp;
[[vk::binding(5, 0)]] RWTexture2D<float4> Output;
[[vk::binding(6, 0)]] Texture2D<float> DepthTex;
[[vk::binding(7, 0)]] Texture2D<float> PrevDepthTex;
[[vk::binding(8, 0)]] Texture2D<float2> VolumeReactive;

float DepthAtUv(Texture2D<float> depth, float2 uv)
{
    uint width, height;
    depth.GetDimensions(width, height);
    int2 pixel = min(int2(saturate(uv) * float2(width, height)), int2(width - 1, height - 1));
    return depth.Load(int3(pixel, 0));
}

float DepthFootprintConfidence(Texture2D<float> depth, float2 uv, float centerDepth)
{
    uint width, height;
    depth.GetDimensions(width, height);
    float2 pos = uv * float2(width, height) - 0.5;
    float2 weight = frac(pos);
    float4 samples = depth.GatherRed(LinearClamp, uv);
    if (!all(isfinite(samples)) || !isfinite(centerDepth))
        return 0.0;
    float centerSignal = InvertedDepth ? centerDepth : 1.0 - centerDepth;
    float4 sampleSignal = InvertedDepth ? samples : 1.0 - samples;
    float scale = max(centerSignal, 1e-7);
    float4 confidence = saturate(1.0 - abs(samples - centerDepth) / (scale * 0.08 + 1e-8));
    confidence *= float4((sampleSignal > 0.0) == (centerSignal > 0.0));
    float4 active = float4(weight.y > 1e-3, all(weight > 1e-3), weight.x > 1e-3, 1.0);
    confidence = lerp(1.0, confidence, active);
    return min(min(confidence.x, confidence.y), min(confidence.z, confidence.w));
}

float ColorHistoryConfidence(float3 previous, float3 current)
{
    if (!all(isfinite(previous)) || !all(isfinite(current)))
        return 0.0;
    float3 difference = abs(previous - current);
    float mismatch = max(difference.r, max(difference.g, difference.b));
    float3 magnitude = max(abs(previous), abs(current));
    float level = max(magnitude.r, max(magnitude.g, magnitude.b));
    float tolerance = 0.01 + 0.25 * level;
    return 1.0 - smoothstep(tolerance, tolerance * 2.0, mismatch);
}

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    int2 pixel = int2(id.xy);
    float4 currPx = CurrColor.Load(int3(pixel, 0));
    float2 uv = (float2(pixel) + 0.5) * float2(InvWidth, InvHeight);
    uint motionWidth, motionHeight;
    MotionTex.GetDimensions(motionWidth, motionHeight);
    int2 motionPixel = min(int2(uv * float2(motionWidth, motionHeight)), int2(motionWidth - 1, motionHeight - 1));
    float2 mvNative = MotionTex.Load(int3(motionPixel, 0)).xy;
    float2 mvPx = mvNative * float2(MvScaleX, MvScaleY);
    float magNative = length(mvNative);
    float magOut = length(mvPx);
    if (magNative < 1.25 || magOut > 96.0 || !all(isfinite(mvPx)))
    {
        Output[pixel] = float4(currPx.rgb, 1.0);
        return;
    }

    float2 mvUv = mvPx * float2(InvWidth, InvHeight);
    float2 uvPrev = uv + 0.5 * mvUv;
    float2 uvCurr = uv - 0.5 * mvUv;
    float2 firstUv = 0.5 * float2(InvWidth, InvHeight);
    float2 lastUv = 1.0 - firstUv;
    if (any(uvPrev < firstUv) || any(uvPrev > lastUv) ||
        any(uvCurr < firstUv) || any(uvCurr > lastUv))
    {
        Output[pixel] = float4(currPx.rgb, 1.0);
        return;
    }

    float centerDepth = DepthAtUv(DepthTex, uv);
    float wPrev = DepthFootprintConfidence(PrevDepthTex, uvPrev, centerDepth);
    float wCurr = DepthFootprintConfidence(DepthTex, uvCurr, centerDepth);
    wPrev = min(wPrev, DepthFootprintConfidence(DepthTex, uvPrev, centerDepth));
    if (min(wPrev, wCurr) < 0.5)
    {
        Output[pixel] = float4(currPx.rgb, 1.0);
        return;
    }

    float historyConfidence = 1.0;
    if ((InvertedDepth ? centerDepth : 1.0 - centerDepth) > 0.0)
    {
        float2 previousSurfaceUv = uv + mvUv;
        if (any(previousSurfaceUv < firstUv) || any(previousSurfaceUv > lastUv))
        {
            Output[pixel] = float4(currPx.rgb, 1.0);
            return;
        }
        float3 previousSurface = PrevColor.SampleLevel(LinearClamp, previousSurfaceUv, 0).rgb;
        historyConfidence = ColorHistoryConfidence(previousSurface, currPx.rgb);
        float3 previousUnwarped = PrevColor.Load(int3(pixel, 0)).rgb;
        if (!all(isfinite(previousUnwarped)))
        {
            Output[pixel] = float4(currPx.rgb, 1.0);
            return;
        }
        float3 stillDifference = abs(previousUnwarped - currPx.rgb);
        float stillMismatch = max(stillDifference.r, max(stillDifference.g, stillDifference.b));
        float3 stillMagnitude = max(abs(previousUnwarped), abs(currPx.rgb));
        float stillTolerance = 0.001 + 0.01 * max(stillMagnitude.r, max(stillMagnitude.g, stillMagnitude.b));
        historyConfidence *= smoothstep(stillTolerance, stillTolerance * 2.0, stillMismatch);
        if (historyConfidence <= 0.0)
        {
            Output[pixel] = float4(currPx.rgb, 1.0);
            return;
        }
    }

    float4 prev = PrevColor.SampleLevel(LinearClamp, uvPrev, 0);
    float4 curr = CurrColor.SampleLevel(LinearClamp, uvCurr, 0);
    float sum = wPrev + wCurr;
    float4 mixed = (prev * wPrev + curr * wCurr) / sum;
    mixed = lerp(currPx, mixed, historyConfidence);
    if (HasVolumeReactive != 0)
    {
        float reject = max(VolumeReactive.SampleLevel(LinearClamp, uv, 0).x,
            max(VolumeReactive.SampleLevel(LinearClamp, saturate(uvPrev), 0).x,
                VolumeReactive.SampleLevel(LinearClamp, saturate(uvCurr), 0).x));
        mixed = lerp(mixed, currPx, saturate(reject));
    }
    Output[pixel] = float4(mixed.rgb, 1.0);
}
