// One visibility sample per pixel. Not the software temporal stack.
// Proceed() is the ray-query completion loop; the hardware traversal is finite.
[[vk::binding(0, 0)]] RaytracingAccelerationStructure Scene;
[[vk::binding(1, 0)]] cbuffer Params {
    row_major float4x4 InvView;
    row_major float4x4 InvProj;
    float4 SunDirection;
    float4 SunColor;
    float4 Settings; // bias metres, exposure, solar radians, shadows
    uint Width, Height, LightCount, Reserved;
    float4 LightPosRange[8];
    float4 LightColorIntensity[8];
};
[[vk::binding(2, 0)]] Texture2D<float> Depth;
[[vk::binding(3, 0)]] [[vk::image_format("rgba16f")]] RWTexture2D<float4> Hdr;

bool Occluded(float3 origin, float3 direction, float reach) {
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = direction;
    ray.TMin = 0;
    ray.TMax = reach;
    RayQuery<RAY_FLAG_FORCE_OPAQUE> query;
    query.TraceRayInline(Scene, RAY_FLAG_FORCE_OPAQUE, 0xFF, ray);
    while (query.Proceed()) { }
    return query.CommittedStatus() != COMMITTED_NOTHING;
}

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID) {
    if (id.x >= Width || id.y >= Height) return;
    float depth = Depth.Load(int3(id.xy, 0));
    if (depth <= 1e-6) return;
    float2 uv = (float2(id.xy) + 0.5) / float2(Width, Height);
    float2 ndc = float2(uv.x * 2 - 1, 1 - uv.y * 2);
    float4 view = mul(float4(ndc, depth, 1), InvProj);
    if (abs(view.w) < 1e-8) return;
    view /= view.w;
    float4 world = mul(float4(view.xyz, 1), InvView);
    float3 origin = world.xyz;
    float3 sun = SunDirection.xyz;
    float sunLen = length(sun);
    if (sunLen < 1e-5) return;
    sun /= sunLen;
    float3 side = normalize(abs(sun.y) < 0.9 ? cross(sun, float3(0, 1, 0)) : cross(sun, float3(1, 0, 0)));
    float3 sunDir = normalize(sun + side * sin(Settings.z));
    float3 add = 0;
    float bias = max(Settings.x, 0);
    if (Settings.w > 0.5) {
        if (!Occluded(origin + sunDir * bias, sunDir, 1e6))
            add += SunColor.rgb * Settings.y;
    } else add += SunColor.rgb * Settings.y;
    uint lights = min(LightCount, 8u);
    [loop]
    for (uint i = 0; i < lights; i++) {
        float3 to = LightPosRange[i].xyz - origin;
        float dist = length(to);
        float range = LightPosRange[i].w;
        if (!(dist > 1e-3) || dist > range) continue;
        float3 dir = to / dist;
        if (Settings.w <= 0.5 || !Occluded(origin + dir * bias, dir, dist))
            add += LightColorIntensity[i].rgb * saturate(1 - dist / range);
    }
    float4 hdr = Hdr[id.xy];
    hdr.rgb += add;
    Hdr[id.xy] = hdr;
}
