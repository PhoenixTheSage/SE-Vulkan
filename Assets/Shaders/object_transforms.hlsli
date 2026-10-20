// Shared stock instance/deformation/distance-fade geometry for auxiliary passes.
float4x4 FromRows(float4 a, float4 b, float4 c)
{
    return transpose(float4x4(a, b, c, float4(0, 0, 0, 1)));
}

static const float3 kPackedDir[6] =
{
    float3(0, 0, -1), float3(0, 0, 1), float3(-1, 0, 0), float3(1, 0, 0), float3(0, 1, 0), float3(0, -1, 0)
};

float4x4 CubeMatrix(float4 packed)
{
    float cell = floor(packed.w / 6);
    float up = packed.w - cell * 6;
    float3 forward = kPackedDir[(uint)clamp(cell, 0, 5)];
    float3 upDir = kPackedDir[(uint)clamp(up, 0, 5)];
    float3 right = cross(upDir, -forward);
    return float4x4(float4(right, 0), float4(upDir, 0), float4(-forward, 0), float4(packed.xyz, 1));
}

float3 UnpackBone(float4 position, float range)
{
    return (position.xyz + (0.5 / 255.0) - 0.5) * range * 2;
}

float4x4 DeformedMatrix(float4 p0, float4 p1, float4 p2, float4 p3, float4 p4, float4 p5, float4 p6, float4 p7, float4 cube, uint4 indices, float4 weights)
{
    float4x4 m = CubeMatrix(cube);
    if (p3.w)
    {
        float4 bones[9] =
        {
            p0, p1, p2, p3, p4, p5, p6, p7, float4(p0.w, p1.w, p2.w, 0)
        };
        float4x4 blended = float4x4(bones[indices.x], bones[indices.y], bones[indices.z], bones[indices.w]);
        float3 extra = UnpackBone(mul(weights, blended), p4.w / 10.0 * 255.0);
        m._41_42_43 += extra;
    }
    return m;
}

float3 WorldPosition(float4 position, float4x4 instance, float4x4 objectMatrix, bool instanced, float fadeParameter)
{
    float4 local = float4(position.xyz * position.w, 1);
    // Negative (including -0) carries the exact threshold. Legacy native
    // builds leave these push words at +0, which must remain disabled.
    if (instanced && (asuint(fadeParameter) & 0x80000000u) != 0)
    {
        float fadeThreshold = -fadeParameter;
        // Stock VertexTemplateBase scales the object-space vertex after
        // measuring its unscaled, camera-relative instance position.
        float3 unscaled = mul(mul(local, instance), objectMatrix).xyz;
        float scaleDistanceFactor = saturate((length(unscaled) - max(fadeThreshold, 900)) / (fadeThreshold / 10));
        local.xyz *= 1 - scaleDistanceFactor;
    }
    if (instanced)
        local = mul(local, instance);
    return mul(local, objectMatrix).xyz;
}
