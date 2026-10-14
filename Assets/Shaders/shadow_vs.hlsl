struct Light { float3 position; float range; float3 color; float intensity; float3 direction; float cone_cosine; uint flags; uint3 reserved; };
struct Frame { row_major float4x4 view_projection; float4 camera_position; float4 sun_direction; float4 sun_color; float4 ambient_color; float4 clear_color; uint light_count; uint3 reserved; Light lights[64]; row_major float4x4 shadow_matrices[4]; float4 shadow_splits; uint shadow_count; uint3 shadow_reserved; };
struct Draw { row_major float4x4 world; float4 tint; float4 hsv; float dither; float emissivity; uint flags; uint cascade; };
[[vk::binding(0,0)]] ConstantBuffer<Frame> frame;
[[vk::push_constant]] ConstantBuffer<Draw> draw;
struct Input { [[vk::location(0)]] float3 position : POSITION; [[vk::location(1)]] float3 normal : NORMAL; [[vk::location(2)]] float2 uv : TEXCOORD0; };
struct Output { float4 position : SV_Position; float2 uv : TEXCOORD0; };
Output main(Input i) { Output o; o.position=mul(mul(float4(i.position,1),draw.world),frame.shadow_matrices[draw.cascade]); o.uv=i.uv; return o; }
