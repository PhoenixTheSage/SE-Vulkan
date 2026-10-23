// Stock Primitives/Lines.hlsl: camera-relative positions, sRGB vertex color decoded and premultiplied.
struct Frame { row_major float4x4 view_projection; };
[[vk::binding(0,0)]] ConstantBuffer<Frame> frame;
struct Input { [[vk::location(0)]] float3 position : POSITION; [[vk::location(1)]] float4 color : COLOR; };
struct Output { float4 position : SV_Position; float4 color : COLOR0; };
Output main(Input i) {
    Output o; o.position = mul(float4(i.position,1), frame.view_projection);
    float3 linear_color = select(i.color.rgb<=.04045, i.color.rgb/12.92, pow((abs(i.color.rgb)+.055)/1.055,2.4));
    o.color = float4(linear_color*i.color.a, i.color.a); return o;
}
