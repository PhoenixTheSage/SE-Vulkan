struct Light { float3 position; float range; float3 color; float intensity; float3 direction; float cone_cosine; uint flags; uint3 reserved; };
struct Frame { row_major float4x4 view_projection; float4 camera_position; float4 sun_direction; float4 sun_color; float4 ambient_color; float4 clear_color; uint light_count; uint3 reserved; Light lights[64]; row_major float4x4 shadow_matrices[4]; float4 shadow_splits; uint shadow_count; uint3 shadow_reserved; };
struct Draw { row_major float4x4 world; float4 tint; float4 hsv; float dither; float emissivity; uint flags; uint pad; };
[[vk::binding(0,0)]] ConstantBuffer<Frame> frame;
[[vk::binding(0,1)]] Texture2D<float4> image;
[[vk::constant_id(0)]] const uint encode_display=1;
[[vk::binding(1,1)]] SamplerState image_sampler;
[[vk::binding(2,1)]] Texture2D<float4> normal_gloss;
[[vk::binding(3,1)]] Texture2D<float4> extensions;
[[vk::binding(4,1)]] Texture2D<float4> alpha_mask;
[[vk::binding(0,2)]] Texture2D<float> shadow_maps[4];
[[vk::binding(1,2)]] SamplerComparisonState shadow_sampler;
[[vk::push_constant]] ConstantBuffer<Draw> draw;
float3 to_srgb(float3 x) { return lerp(x*12.92,1.055*pow(max(x,0),1/2.4)-.055,step(.0031308,x)); }
float3 to_linear(float3 x) { return lerp(x/12.92,pow(max((x+.055)/1.055,0),2.4),step(.04045,x)); }
float3 rgb_to_hsv(float3 c) {
    float4 k=float4(0,-1/3.0,2/3.0,-1); float4 p=lerp(float4(c.bg,k.wz),float4(c.gb,k.xy),step(c.b,c.g));
    float4 q=lerp(float4(p.xyw,c.r),float4(c.r,p.yzx),step(p.x,c.r)); float d=q.x-min(q.w,q.y);
    return float3(abs(q.z+(q.w-q.y)/(6*d+1e-10)),d/(q.x+1e-10),q.x);
}
float3 hsv_to_rgb(float3 c) { return c.z*lerp(float3(1,1,1),saturate(abs(frac(c.xxx+float3(0,2/3.0,1/3.0))*6-3)-1),c.y); }
float3 fixed_exposure_tone_map(float3 x) { x=max(x,0);return saturate((x*(2.51*x+.03))/(x*(2.43*x+.59)+.14)); }
float visibility(float3 position) {
    if (frame.shadow_count==0) return 1;
    float distance=length(position); uint cascade=0;
    while(cascade+1<frame.shadow_count && distance>frame.shadow_splits[cascade]) ++cascade;
    float4 clip_position=mul(float4(position,1),frame.shadow_matrices[cascade]);
    float3 projected=clip_position.xyz/max(clip_position.w,1e-6); float2 uv=projected.xy*float2(.5,-.5)+.5;
    if (any(uv<0) || any(uv>1) || projected.z<0 || projected.z>1) return 1;
    float2 texel=1.0/1024; float visibility=0;
    // Explicit constant resource indices avoid requiring descriptor indexing.
    for(uint sample=0;sample<4;++sample) {
        float2 p=uv+(float2(sample&1,sample>>1)-.5)*texel;
        if(cascade==0) visibility+=shadow_maps[0].SampleCmpLevelZero(shadow_sampler,p,projected.z-.0005);
        else if(cascade==1) visibility+=shadow_maps[1].SampleCmpLevelZero(shadow_sampler,p,projected.z-.0005);
        else if(cascade==2) visibility+=shadow_maps[2].SampleCmpLevelZero(shadow_sampler,p,projected.z-.0005);
        else visibility+=shadow_maps[3].SampleCmpLevelZero(shadow_sampler,p,projected.z-.0005);
    }
    return visibility*.25;
}
float4 main(float4 position : SV_Position,float3 normal : NORMAL,float2 uv : TEXCOORD0,float3 world_position : TEXCOORD1) : SV_Target0 {
    float4 texel=image.Sample(image_sampler,uv);
    if ((draw.flags & 1) != 0) clip(((draw.flags & 128) != 0 ? alpha_mask.Sample(image_sampler,uv).r : texel.a) - .5);
    if (abs(draw.dither)>0) {
        const uint bayer[16]={0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5};
        uint2 pixel=(uint2)position.xy; clip((bayer[(pixel.y&3)*4+(pixel.x&3)]+.5)/16.0-saturate(abs(draw.dither)));
    }
    float4 add_maps=(draw.flags & 64)!=0 ? extensions.Sample(image_sampler,uv) : float4(1,0,0,0);
    float paint=add_maps.a;
    if ((draw.flags&16)==0) paint*=saturate(1-saturate(texel.a-.4)/.3);
    float3 hsv=rgb_to_hsv(to_srgb(texel.rgb)); hsv.xy=0;
    float3 base=lerp(texel.rgb,to_linear(hsv_to_rgb(saturate(hsv+draw.hsv.xyz))),paint)*draw.tint.rgb;
    float3 n=normalize(normal);
    if ((draw.flags&32)!=0) {
        float3 dp1=ddx(world_position),dp2=ddy(world_position); float2 duv1=ddx(uv),duv2=ddy(uv);
        float3 t=cross(dp2,n)*duv1.x+cross(n,dp1)*duv2.x;
        float3 b=cross(dp2,n)*duv1.y+cross(n,dp1)*duv2.y;
        float scale=rsqrt(max(max(dot(t,t),dot(b,b)),1e-10));
        float3 map=normal_gloss.Sample(image_sampler,uv).xyz*2-1;
        // Stock NormalBuildTangent's derivative basis uses the opposite X
        // convention from meshes with an imported tangent.
        map.x=-map.x;
        n=normalize(t*scale*map.x+b*scale*map.y+n*map.z);
    }
    float3 light=frame.ambient_color.rgb*add_maps.r+saturate(dot(n,-frame.sun_direction.xyz))*frame.sun_color.rgb*visibility(world_position);
    for (uint i=0;i<min(frame.light_count,64);++i) {
        Light l=frame.lights[i]; float3 delta=l.position-world_position; float distance=length(delta);
        float3 direction=delta/max(distance,1e-5); float attenuation=pow(saturate(1-distance/max(l.range,1e-5)),2);
        if ((l.flags&2)!=0) {
            float t=saturate(distance/max(l.range,1e-5));
            float hard=pow(saturate(1-t*t*t*t),3);
            attenuation=hard/(1+max(asfloat(l.reserved.x),0)*t*t)*max(asfloat(l.reserved.y),0)/3.14159265359;
        }
        if ((l.flags&1)!=0) attenuation*=smoothstep(l.cone_cosine,min(1,l.cone_cosine+.08),dot(-direction,normalize(l.direction)));
        light+=l.color*l.intensity*attenuation*saturate(dot(n,direction));
    }
    float emissive=saturate(add_maps.g-1/255.0)*max(draw.emissivity,0);
    float3 result=(draw.flags&2)!=0 ? base : base*light+base*emissive;
    float alpha=(draw.flags&12)!=0 ? texel.a*draw.tint.a : draw.tint.a;
    result=(draw.flags&2)!=0?result:fixed_exposure_tone_map(result);
    return float4(encode_display!=0?to_srgb(result):result,alpha);
}
