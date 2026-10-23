[[vk::constant_id(0)]] const uint encode_display=1;
float4 main(float4 position : SV_Position, float4 color : COLOR0) : SV_Target0 {
    if(encode_display!=0) color.rgb=lerp(color.rgb*12.92,1.055*pow(max(color.rgb,0),1/2.4)-.055,step(.0031308,color.rgb));
    return color;
}
