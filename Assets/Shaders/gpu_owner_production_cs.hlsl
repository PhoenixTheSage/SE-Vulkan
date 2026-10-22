// Reduction over immutable indirect arguments actually submitted this frame.
[[vk::binding(0,0)]] StructuredBuffer<uint4> Ranges;
[[vk::binding(1,0)]] ByteAddressBuffer Counts;
[[vk::binding(2,0)]] ByteAddressBuffer Arguments;
[[vk::binding(3,0)]] RWStructuredBuffer<uint> Work;
[numthreads(64,1,1)]
void main(uint3 thread:SV_DispatchThreadID){
    uint rows,stride;Ranges.GetDimensions(rows,stride);uint row=thread.x;if(row>=rows)return;
    uint4 range=Ranges[row];if(!range.y||(range.w!=0xffffffff&&Counts.Load(range.w*4)==0)){Work[row]=0;return;}if(range.x==0xffffffff){Work[row]=1;return;}uint count=min(Counts.Load(range.z*4),range.y);uint produced=0;
    for(uint i=0;i<count;i++){uint at=(range.x+i)*20;if(Arguments.Load(at)&&Arguments.Load(at+4)){produced=1;break;}}
    Work[row]=produced;
}
