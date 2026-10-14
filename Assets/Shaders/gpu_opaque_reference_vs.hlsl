#include "gpu_opaque_reference.hlsli"
struct Input {[[vk::location(0)]] float3 position:POSITION;uint instance:SV_InstanceID;[[vk::builtin("DrawIndex")]] uint draw:DRAW_INDEX;};
struct Output {float4 position:SV_Position;float4 colour:COLOR0;nointerpolation uint material:MATERIAL;};
Output main(Input input){
    DrawRecord d=draw_records[control.batch*control.draw_capacity+input.draw];
    Reference r=references[d.old?d.reference:input.instance];Actor a=actors[r.actor];float3 p=input.position;
    if(d.old){uint at=input.instance*64;
        if(a.flags&32){
            // Installed VertexTransformations.hlsli 50AFA1F8: packed direction
            // order and row-vector rotation. Disabled skinning is admitted only
            // when source packed3.w is zero; deformation remains unsupported.
            const float3 directions[6]={float3(0,0,-1),float3(0,0,1),float3(-1,0,0),float3(1,0,0),float3(0,1,0),float3(0,-1,0)};
            float4 packed=asfloat(source_instances.Load4(at+32));uint forward_index=(uint)(packed.w/6),up_index=(uint)packed.w-forward_index*6;
            float3 forward=directions[forward_index],up=directions[up_index],right=cross(up,-forward);
            p=p.x*right+p.y*up-p.z*forward+packed.xyz;
        }else {float4 x=asfloat(source_instances.Load4(at)),y=asfloat(source_instances.Load4(at+16)),z=asfloat(source_instances.Load4(at+32));p=float3(dot(x,float4(p,1)),dot(y,float4(p,1)),dot(z,float4(p,1)));}
    }
    float3 world=float3(dot(float4(p,1),float4(r.rows[0],r.rows[1],r.rows[2],r.rows[3])),dot(float4(p,1),float4(r.rows[4],r.rows[5],r.rows[6],r.rows[7])),dot(float4(p,1),float4(r.rows[8],r.rows[9],r.rows[10],r.rows[11])));
    Output result;result.position=mul(float4(world,1),views[r.view].projection);result.colour=a.colour;result.material=r.material;return result;
}
