#pragma once
#include <algorithm>
#include <cmath>

// Conservative overlap with the light's range sphere and infinite cone.
// direction is the normalized light axis; a negative/unknown radius is retained.
inline bool spot_sphere_visible(const float* delta,const float* direction,float range,float cosine,float radius) {
    if(!(radius>=0))return true;
    float distance2=delta[0]*delta[0]+delta[1]*delta[1]+delta[2]*delta[2];
    float reach=range+radius;if(distance2>reach*reach)return false;
    if(cosine>0.05f && cosine<0.999f) {
        float along=delta[0]*direction[0]+delta[1]*direction[1]+delta[2]*direction[2];
        if(along<-radius)return false;
        float side2=std::max(0.f,distance2-along*along);
        float sine=std::sqrt(std::max(0.f,1.f-cosine*cosine));
        // Cone side-plane distance is side*cosine - along*sine.
        // A horizontal radius margin underestimates the sphere's reach.
        float limit=(along*sine+radius)/cosine;
        if(side2>limit*limit)return false;
    }
    return true;
}
