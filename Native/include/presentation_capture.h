#pragma once
#include <cmath>
#include <cstdint>
#include <limits>

namespace sevk {
inline float capture_half(uint16_t bits) {
    uint32_t exponent=(bits>>10)&31, mantissa=bits&1023;
    float value=exponent==31?(mantissa?std::numeric_limits<float>::quiet_NaN():std::numeric_limits<float>::infinity()):
        std::ldexp(float(exponent?1024+mantissa:mantissa),exponent?int(exponent)-25:-24);
    return bits&0x8000?-value:value;
}
// PNG readback is an SDR RGBA8 preview of linear scRGB presentation.
inline void capture_scrgb_rgba8(const uint16_t* source,uint8_t* destination,uint64_t pixels) {
    for(uint64_t i=0;i<pixels*4;++i){
        float value=capture_half(source[i]);value=value>0?std::fmin(value,1.f):0.f;
        if(i%4!=3)value=value<=.0031308f?12.92f*value:1.055f*std::pow(value,1.f/2.4f)-.055f;
        destination[i]=uint8_t(std::lround(value*255.f));
    }
}
}
