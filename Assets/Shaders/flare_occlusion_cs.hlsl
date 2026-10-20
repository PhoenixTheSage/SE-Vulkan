struct Query { uint2 id; float3 center; float2 halfExtent; float area; };
struct Result { uint2 id; float occlusion; uint reserved; };
[[vk::binding(0)]] cbuffer Constants { row_major float4x4 ViewProjection; uint Width, Height, Count, Reserved; };
[[vk::binding(1)]] StructuredBuffer<Query> Queries;
[[vk::binding(2)]] RWStructuredBuffer<Result> Results;
[[vk::binding(3)]] Texture2D<float> Depth;
groupshared uint Visible[64];
[numthreads(64,1,1)] void main(uint3 group:SV_GroupID,uint lane:SV_GroupIndex) {
    if(group.x>=Count)return;
    Query q=Queries[group.x];uint visible=0;
    float2 dimensions=float2(Width,Height);
    float2 center=(q.center.xy*float2(.5,-.5)+.5)*dimensions;
    float2 radius=q.halfExtent*dimensions*.5;
    // Stock quad coverage at pixel centers; clip before integer conversion.
    int2 lo=int2(ceil(clamp(center-radius-.5,0,dimensions)));
    int2 hi=int2(ceil(clamp(center+radius-.5,0,dimensions)));
    uint width=max(hi.x-lo.x,0),height=max(hi.y-lo.y,0);
    if(q.center.z>=0 && q.center.z<=1 && width>0){
        for(uint i=lane;i<width*height;i+=64){
            int2 pixel=lo+int2(i%width,i/width);
            // Stock DepthTestReadOnly uses strict Greater for reverse depth.
            visible+=q.center.z>Depth.Load(int3(pixel,0))?1:0;
        }
    }
    Visible[lane]=visible;GroupMemoryBarrierWithGroupSync();
    for(uint step=32;step>0;step/=2){if(lane<step)Visible[lane]+=Visible[lane+step];GroupMemoryBarrierWithGroupSync();}
    if(lane==0){Result r;r.id=q.id;r.reserved=0;r.occlusion=1-min(Visible[0]/(q.area*Width*Height*.25),1);Results[group.x]=r;}
}
