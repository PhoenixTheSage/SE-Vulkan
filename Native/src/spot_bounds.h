#pragma once
#include <algorithm>
#include <cmath>

// Stock MyLightComponent.UpdateViewProjection spotlight shadow near plane.
constexpr float spot_shadow_near=.5f;

// Conservative overlap with the light's range sphere, infinite cone and stock
// shadow near plane. Stock culls casters against the full spot frustum, so a
// lamp housing wholly inside the near plane never reaches the map; drawing it
// fills the map at the near plane. The sphere encloses the caster bounds, so
// this never rejects a caster the stock frustum test keeps.
// direction is the normalized light axis; a negative/unknown radius is retained.
inline bool spot_sphere_visible(const float* delta,const float* direction,float range,float cosine,float radius) {
    if(!(radius>=0))return true;
    float distance2=delta[0]*delta[0]+delta[1]*delta[1]+delta[2]*delta[2];
    float reach=range+radius;if(distance2>reach*reach)return false;
    float along=delta[0]*direction[0]+delta[1]*direction[1]+delta[2]*direction[2];
    if(along<spot_shadow_near-radius)return false;
    if(cosine>0.05f && cosine<0.999f) {
        float side2=std::max(0.f,distance2-along*along);
        float sine=std::sqrt(std::max(0.f,1.f-cosine*cosine));
        // Cone side-plane distance is side*cosine - along*sine.
        // A horizontal radius margin underestimates the sphere's reach.
        float limit=(along*sine+radius)/cosine;
        if(side2>limit*limit)return false;
    }
    return true;
}
