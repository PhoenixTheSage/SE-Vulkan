[[vk::binding(12,0)]] Texture2D<float4> material_textures[];
[[vk::binding(13,0)]] SamplerState material_sampler;
struct Input {float4 position:SV_Position;float4 colour:COLOR0;nointerpolation uint material:MATERIAL;};
float4 main(Input input):SV_Target{return input.colour*material_textures[NonUniformResourceIndex(input.material)].Sample(material_sampler,float2(.5,.5));}
