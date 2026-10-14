struct Light { float3 position; float range; float3 color; float intensity; float3 direction; float cone_cosine; uint flags; uint3 reserved; };
struct Frame { row_major float4x4 view_projection; float4 camera_position; float4 sun_direction; float4 sun_color; float4 ambient_color; float4 clear_color; uint light_count; uint3 reserved; Light lights[64]; };
struct Draw { row_major float4x4 world; float4 tint; float4 hsv; float dither; float emissivity; uint flags; uint pad; };
[[vk::binding(0,0)]] ConstantBuffer<Frame> frame;
[[vk::push_constant]] ConstantBuffer<Draw> draw;
struct Input { [[vk::location(0)]] float3 position : POSITION; [[vk::location(1)]] float3 normal : NORMAL; [[vk::location(2)]] float2 uv : TEXCOORD0; };
struct Output { float4 position : SV_Position; float3 normal : NORMAL; float2 uv : TEXCOORD0; float3 world_position : TEXCOORD1; };
Output main(Input i) { Output o; float4 world=mul(float4(i.position,1),draw.world); o.position = mul(world,frame.view_projection); if((draw.flags&256)!=0) o.position.z=0; o.world_position=world.xyz; o.normal = normalize(mul(float4(i.normal,0),draw.world).xyz); o.uv = i.uv; return o; }
