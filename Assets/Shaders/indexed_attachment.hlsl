#pragma pack_matrix(row_major)
#include "object_transforms.hlsli"

cbuffer Projection : register(b0) { float4x4 ViewProj; };
#ifdef INDEXED_MOTION
cbuffer PreviousProjection : register(b1) { float4x4 PrevViewProj; };
ByteAddressBuffer References : register(t4,space1);
ByteAddressBuffer Previous : register(t5,space1);
ByteAddressBuffer PreviousSources : register(t6,space1);
#endif
struct IndexedPush { uint draw_base; float fade_parameter;
#ifdef INDEXED_MOTION
    float2 size;
#endif
};
[[vk::push_constant]] ConstantBuffer<IndexedPush> indexed;
struct Draw { uint object_offset, material, source, reserved; uint4 textures; };
ByteAddressBuffer Instances : register(t0,space1);
ByteAddressBuffer Objects : register(t1,space1);
ByteAddressBuffer Sources : register(t2,space1);
StructuredBuffer<Draw> Draws : register(t3,space1);
struct Input {
    [[vk::location(0)]] float4 position : POSITION0;
#if INDEXED_KIND == 3
    [[vk::location(1)]] float4 weights : BLENDWEIGHT0;
    [[vk::location(2)]] uint4 indices : BLENDINDICES0;
#endif
};
struct Output {
    float4 position : SV_Position;
#ifdef INDEXED_MOTION
    float2 motion : TEXCOORD0;
#else
    nointerpolation float4 row0 : TEXCOORD0;
    nointerpolation float4 row1 : TEXCOORD1;
    nointerpolation float4 row2 : TEXCOORD2;
#endif
};
float4 Bone(uint offset) {
    uint p=Sources.Load(offset);
    return float4(p&255,(p>>8)&255,(p>>16)&255,p>>24)/255.0;
}
Output __vertex_shader(Input input,uint instance_id:SV_InstanceID,[[vk::builtin("DrawIndex")]] uint draw_index:SEVK_DRAW_INDEX) {
    Draw draw=Draws[indexed.draw_base+draw_index];
    float4x4 identity=float4x4(1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1);
    float4x4 objectM=identity,instance=identity;
    Output output;
    float4 row0,row1,row2;
#if INDEXED_KIND == 4
    uint at=instance_id*64;
    row0=asfloat(Instances.Load4(at));row1=asfloat(Instances.Load4(at+16));row2=asfloat(Instances.Load4(at+32));
    instance=FromRows(row0,row1,row2);
#else
    uint object_at=draw.object_offset;
    row0=asfloat(Objects.Load4(object_at+32));row1=asfloat(Objects.Load4(object_at+48));row2=asfloat(Objects.Load4(object_at+64));
    objectM=FromRows(row0,row1,row2);
#if INDEXED_KIND == 1
    uint at=instance_id*64;
    instance=FromRows(asfloat(Sources.Load4(at)),asfloat(Sources.Load4(at+16)),asfloat(Sources.Load4(at+32)));
#elif INDEXED_KIND == 2
    instance=CubeMatrix(asfloat(Sources.Load4(instance_id*64+32)));
#elif INDEXED_KIND == 3
    uint at=instance_id*64;
    instance=DeformedMatrix(Bone(at),Bone(at+4),Bone(at+8),Bone(at+12),Bone(at+16),Bone(at+20),Bone(at+24),Bone(at+28),asfloat(Sources.Load4(at+32)),input.indices,input.weights);
#endif
#endif
    float3 world=WorldPosition(input.position,instance,objectM,INDEXED_KIND!=0,indexed.fade_parameter);
    output.position=mul(float4(world,1),ViewProj);
#ifndef INDEXED_MOTION
    output.row0=row0;output.row1=row1;output.row2=row2;
#else
    uint actor=INDEXED_KIND==4?References.Load(instance_id*80):draw.reserved;
    uint header=actor*64,valid=Previous.Load(header+56);
    if(!(valid&1)||((INDEXED_KIND>0&&INDEXED_KIND<4)&&!(valid&2))){output.motion=0;return output;}
    float4x4 priorObject=FromRows(asfloat(Previous.Load4(header)),asfloat(Previous.Load4(header+16)),asfloat(Previous.Load4(header+32)));
    float4x4 priorInstance=identity;
#if INDEXED_KIND == 1
    uint source=(Previous.Load(header+48)+instance_id-draw.source)*64;
    priorInstance=FromRows(asfloat(PreviousSources.Load4(source)),asfloat(PreviousSources.Load4(source+16)),asfloat(PreviousSources.Load4(source+32)));
#elif INDEXED_KIND == 2
    uint source=(Previous.Load(header+48)+instance_id-draw.source)*64;
    priorInstance=CubeMatrix(asfloat(PreviousSources.Load4(source+32)));
#elif INDEXED_KIND == 3
    uint source=(Previous.Load(header+48)+instance_id-draw.source)*64;
    float4 bones[8];[unroll]for(uint i=0;i<8;i++){uint p=PreviousSources.Load(source+i*4);bones[i]=float4(p&255,(p>>8)&255,(p>>16)&255,p>>24)/255.0;}
    priorInstance=DeformedMatrix(bones[0],bones[1],bones[2],bones[3],bones[4],bones[5],bones[6],bones[7],asfloat(PreviousSources.Load4(source+32)),input.indices,input.weights);
#endif
    float3 prior=WorldPosition(input.position,priorInstance,priorObject,INDEXED_KIND>0&&INDEXED_KIND<4,asfloat(Previous.Load(header+60)));
    float4 currentClip=output.position,priorClip=mul(float4(prior,1),PrevViewProj);
    float2 delta=(float2(priorClip.x,-priorClip.y)/max(priorClip.w,1e-6)-float2(currentClip.x,-currentClip.y)/max(currentClip.w,1e-6))*.5*indexed.size;
    output.motion=all(isfinite(delta))?clamp(delta,-indexed.size,indexed.size):0;
#endif
    return output;
}
