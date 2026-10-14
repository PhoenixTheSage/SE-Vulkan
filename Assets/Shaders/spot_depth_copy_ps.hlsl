[[vk::binding(0,0)]] Texture2D<float> frozen_depth;
float main(float4 position:SV_Position):SV_Depth {
    return frozen_depth.Load(int3(int2(position.xy),0));
}
