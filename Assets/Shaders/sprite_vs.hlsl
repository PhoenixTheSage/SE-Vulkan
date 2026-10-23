struct Push { float4 rect; float4 uv; float4 color; float2 screen; float angle; uint flags; float2 origin; float2 pad; float4 hdr_ui; };
[[vk::push_constant]] ConstantBuffer<Push> draw;
struct Output { float4 position : SV_Position; float2 uv : TEXCOORD0; float4 color : COLOR0; };
Output main(uint id : SV_VertexID) {
    const float2 corners[6] = { float2(0,0),float2(1,0),float2(0,1),float2(0,1),float2(1,0),float2(1,1) };
    float2 local = corners[id] * draw.rect.zw - draw.origin;
    float sn = sin(draw.angle), cs = cos(draw.angle);
    float2 pixel = draw.rect.xy + draw.origin + float2(local.x*cs-local.y*sn,local.x*sn+local.y*cs);
    Output o; o.position = float4(pixel / draw.screen * 2 - 1,0,1);
    o.uv = draw.uv.xy + corners[id] * draw.uv.zw; o.color = draw.color; return o;
}
