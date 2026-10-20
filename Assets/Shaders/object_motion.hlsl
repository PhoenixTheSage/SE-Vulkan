#pragma pack_matrix(row_major)

// Object motion for the Vulkan GBuffer. Current and previous positions use the
// same camera-relative rows and view-projection as the stock vertex shader.
// The pixel delta matches AnomalyClipToPixelDelta: (prevUv - currUv) * size.

#ifdef MOTION_VERTEX
struct MotionPush
{
    float4 rows[6];
    float4 screen;
};

[[vk::push_constant]] ConstantBuffer<MotionPush> motion;

cbuffer Projection : register(b0)
{
    float4x4 ViewProj;
};

cbuffer PreviousProjection : register(b1)
{
    float4x4 PrevViewProj;
};

#include "object_transforms.hlsli"

float2 PixelDelta(float3 current, float3 previous)
{
    float4 curr = mul(float4(current, 1), ViewProj);
    float4 prev = mul(float4(previous, 1), PrevViewProj);
    curr.xyz /= max(curr.w, 1e-6);
    prev.xyz /= max(prev.w, 1e-6);
    float2 currUv = float2(curr.x * 0.5 + 0.5, 0.5 - curr.y * 0.5);
    float2 prevUv = float2(prev.x * 0.5 + 0.5, 0.5 - prev.y * 0.5);
    float2 mv = (prevUv - currUv) * motion.screen.xy;
    if (!all(isfinite(mv)))
        return float2(0, 0);
    return clamp(mv, -motion.screen.xy, motion.screen.xy);
}

struct MotionOut
{
    float4 position : SV_Position;
    float2 motion : TEXCOORD0;
};

struct AttachOut
{
    float4 position : SV_Position;
    nointerpolation float4 row0 : TEXCOORD0;
    nointerpolation float4 row1 : TEXCOORD1;
    nointerpolation float4 row2 : TEXCOORD2;
};

#if defined(MOTION_DEFORMED)
struct MotionIn
{
    [[vk::location(0)]] float4 position : POSITION0;
    [[vk::location(1)]] float4 weights : BLENDWEIGHT0;
    [[vk::location(2)]] uint4 indices : BLENDINDICES0;
    [[vk::location(3)]] float4 bone0 : TEXCOORD1;
    [[vk::location(4)]] float4 bone1 : TEXCOORD2;
    [[vk::location(5)]] float4 bone2 : TEXCOORD3;
    [[vk::location(6)]] float4 bone3 : TEXCOORD4;
    [[vk::location(7)]] float4 bone4 : TEXCOORD5;
    [[vk::location(8)]] float4 bone5 : TEXCOORD6;
    [[vk::location(9)]] float4 bone6 : TEXCOORD7;
    [[vk::location(10)]] float4 bone7 : TEXCOORD8;
    [[vk::location(11)]] float4 cube : TEXCOORD9;
    [[vk::location(12)]] float4 prev0 : TEXCOORD11;
    [[vk::location(13)]] float4 prev1 : TEXCOORD12;
    [[vk::location(14)]] float4 prev2 : TEXCOORD13;
    [[vk::location(15)]] float4 prev3 : TEXCOORD14;
    [[vk::location(16)]] float4 prev4 : TEXCOORD15;
    [[vk::location(17)]] float4 prev5 : TEXCOORD16;
    [[vk::location(18)]] float4 prev6 : TEXCOORD17;
    [[vk::location(19)]] float4 prev7 : TEXCOORD18;
    [[vk::location(20)]] float4 prevCube : TEXCOORD19;
};
#elif defined(MOTION_CUBE)
struct MotionIn
{
    [[vk::location(0)]] float4 position : POSITION0;
    [[vk::location(1)]] float4 cube : TEXCOORD1;
    [[vk::location(2)]] float4 prevCube : TEXCOORD2;
};
#elif defined(MOTION_GENERIC)
struct MotionIn
{
    [[vk::location(0)]] float4 position : POSITION0;
    [[vk::location(1)]] float4 curr0 : TEXCOORD1;
    [[vk::location(2)]] float4 curr1 : TEXCOORD2;
    [[vk::location(3)]] float4 curr2 : TEXCOORD3;
    [[vk::location(4)]] float4 prev0 : TEXCOORD4;
    [[vk::location(5)]] float4 prev1 : TEXCOORD5;
    [[vk::location(6)]] float4 prev2 : TEXCOORD6;
};
#else
struct MotionIn
{
    [[vk::location(0)]] float4 position : POSITION0;
};
#endif

#ifdef ATTACH_VERTEX
AttachOut __vertex_shader(MotionIn input)
#else
MotionOut __vertex_shader(MotionIn input)
#endif
{
    float4x4 objectM = FromRows(motion.rows[0], motion.rows[1], motion.rows[2]);
    float4x4 prevObject = FromRows(motion.rows[3], motion.rows[4], motion.rows[5]);
#if defined(MOTION_DEFORMED)
    float4x4 instance = DeformedMatrix(input.bone0, input.bone1, input.bone2, input.bone3, input.bone4, input.bone5, input.bone6, input.bone7, input.cube, input.indices, input.weights);
    float4x4 previous = DeformedMatrix(input.prev0, input.prev1, input.prev2, input.prev3, input.prev4, input.prev5, input.prev6, input.prev7, input.prevCube, input.indices, input.weights);
    bool instanced = true;
#elif defined(MOTION_CUBE)
    float4x4 instance = CubeMatrix(input.cube);
    float4x4 previous = CubeMatrix(input.prevCube);
    bool instanced = true;
#elif defined(MOTION_GENERIC)
    float4x4 instance = FromRows(input.curr0, input.curr1, input.curr2);
    float4x4 previous = FromRows(input.prev0, input.prev1, input.prev2);
    bool instanced = true;
#else
    float4x4 instance = float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    float4x4 previous = instance;
    bool instanced = false;
#endif
    float3 current = WorldPosition(input.position, instance, objectM, instanced, motion.screen.z);
#ifdef ATTACH_VERTEX
    AttachOut output;
    output.position = mul(float4(current, 1), ViewProj);
    output.row0 = motion.rows[0];
    output.row1 = motion.rows[1];
    output.row2 = motion.rows[2];
#ifdef MOTION_GENERIC
    // Native simple-instancing streams already carry the actor transform.
    // A negative width selects those rows for the attachment payload only.
    if (motion.screen.x < 0)
    {
        output.row0 = input.curr0;
        output.row1 = input.curr1;
        output.row2 = input.curr2;
    }
#endif
    return output;
#else
    float3 prior = WorldPosition(input.position, previous, prevObject, instanced, motion.screen.w);
    MotionOut output;
    output.position = mul(float4(current, 1), ViewProj);
    output.motion = PixelDelta(current, prior);
#ifdef MOTION_GENERIC
    // Native pass history marks a newly visible/recreated actor with invalid
    // prior rows. Its current geometry remains valid, but has no object motion.
    if (!all(isfinite(input.prev0)) || !all(isfinite(input.prev1)) || !all(isfinite(input.prev2)))
        output.motion = 0;
#endif
    return output;
#endif
}

#endif

float4 __pixel_shader(float4 position : SV_Position, float2 delta : TEXCOORD0) : SV_Target
{
    return float4(delta, 0, 1);
}
