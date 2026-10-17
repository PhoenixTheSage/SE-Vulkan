// One conservative sun-visibility sample per pixel, before stock lighting.
// Proceed() is the ray-query completion loop; the hardware traversal is finite.
[[vk::binding(0, 0)]] RaytracingAccelerationStructure Scene;
[[vk::binding(1, 0)]] cbuffer Params {
    row_major float4x4 InvViewProjection;
    float4 SunDirection;
    float4 Settings; // bias metres, exposure, solar radians, shadows
    uint Width, Height, Reserved0, Reserved1;
};
[[vk::binding(2, 0)]] Texture2D<float> Depth;
[[vk::binding(3, 0)]] [[vk::image_format("r8")]] RWTexture2D<float> Visibility;

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
    float4 world = mul(float4(ndc, depth, 1), InvViewProjection);
    if (abs(world.w) < 1e-8) return;
    float3 origin = world.xyz / world.w;
    // Stock directionalLightVec points from the sun toward the scene.
    float3 sun = -SunDirection.xyz;
    float sunLen = length(sun);
    if (sunLen < 1e-5) return;
    sun /= sunLen;
    float3 side = normalize(abs(sun.y) < 0.9 ? cross(sun, float3(0, 1, 0)) : cross(sun, float3(1, 0, 0)));
    float3 sunDir = normalize(sun + side * sin(Settings.z));
    float bias = max(Settings.x, 0);
    // Only a certified opaque hit changes visibility. Native cascades retain
    // far, voxel, skinned, cutout and off-screen coverage absent from this TLAS.
    // Stock lighting applies albedo, BRDF, normals, AO and exposure exactly once.
    if (Settings.w > 0.5 && Occluded(origin + sunDir * bias, sunDir, 1e6))
        Visibility[id.xy] = 0;
}
