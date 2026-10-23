[[vk::binding(0,1)]] Texture2D<float4> image;
[[vk::binding(1,1)]] SamplerState image_sampler;
[[vk::constant_id(0)]] const uint encode_display=1;
// hdr_ui: paper white x UI gain (scRGB), shoulder, peak (scRGB), enabled. Set only for the presented HUD.
struct Push { float4 rect; float4 uv; float4 color; float2 screen; float angle; uint flags; float2 origin; float2 pad; float4 hdr_ui; };
[[vk::push_constant]] ConstantBuffer<Push> draw;
float4 main(float4 position : SV_Position, float2 uv : TEXCOORD0, float4 color : COLOR0) : SV_Target0 {
    float4 texel=image.Sample(image_sampler,uv);
    // Stock Sprites.hlsl premultiplies straight input when PREMULTIPLY_ALPHA
    // is defined, and always premultiplies the linear tint by tint alpha.
    if((draw.flags&4)!=0 && (draw.flags&2)==0) texel.rgb*=texel.a;
    if((draw.flags&6)!=0) color.rgb*=color.a;
    float4 sample=texel*color;
    // HdrRender's DX11 UI composite, per sprite: inverse-Reinhard shoulder, paper-white gain, peak cap.
    // Premultiplied outputs (flags&6) expand the unpremultiplied color and re-apply alpha.
    if(encode_display==0 && draw.hdr_ui.w>0){
        bool premultiplied=(draw.flags&6)!=0;
        if(!premultiplied || sample.a>1e-5){
            float3 n=saturate(premultiplied?sample.rgb/sample.a:sample.rgb);
            float3 contrasted=pow(max(n,0),1+draw.hdr_ui.y);
            float3 hdr=min(contrasted/max(1-contrasted*draw.hdr_ui.y,1e-3)*draw.hdr_ui.x,draw.hdr_ui.z);
            sample.rgb=premultiplied?hdr*sample.a:hdr;
        }
    }
    if(encode_display!=0) sample.rgb=lerp(sample.rgb*12.92,1.055*pow(max(sample.rgb,0),1/2.4)-.055,step(.0031308,sample.rgb));
    return sample;
}
