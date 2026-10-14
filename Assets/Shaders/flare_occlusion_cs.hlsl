struct Query { uint2 id; float3 position; float radius; };
struct Result { uint2 id; float occlusion; uint reserved; };
[[vk::binding(0)]] cbuffer Constants { row_major float4x4 ViewProjection; uint Width, Height, Count, Reserved; };
[[vk::binding(1)]] StructuredBuffer<Query> Queries;
[[vk::binding(2)]] RWStructuredBuffer<Result> Results;
[[vk::binding(3)]] Texture2D<float> Depth;
[numthreads(64,1,1)] void main(uint3 tid : SV_DispatchThreadID) {
    if(tid.x>=Count)return;
    Query q=Queries[tid.x];Result r;r.id=q.id;r.reserved=0;r.occlusion=1;
    float4 clip=mul(float4(q.position,1),ViewProjection);
    if(clip.w>0){float3 ndc=clip.xyz/clip.w;float2 uv=float2(ndc.x*.5+.5,.5-ndc.y*.5);
        if(all(uv>=0)&&all(uv<=1)&&ndc.z>=0&&ndc.z<=1){float blocked=0;
            [unroll]for(int y=0;y<4;y++)[unroll]for(int x=0;x<4;x++){
                float2 offset=(float2(x,y)/3-.5)*2*max(q.radius,.5);
                int2 pixel=clamp(int2(uv*float2(Width,Height)+offset),int2(0,0),int2(Width-1,Height-1));
                blocked+=Depth.Load(int3(pixel,0))>ndc.z+1e-5?1:0;
            }r.occlusion=blocked/16;
        }
    }Results[tid.x]=r;
}
