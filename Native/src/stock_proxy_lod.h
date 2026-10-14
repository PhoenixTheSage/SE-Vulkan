#pragma once
#include "scene_actor_plan.h"

namespace resident_proxy {
// Installed MyRenderableComponent selector. OLD proxies keep current/next
// meshes and float object dithering; NEW current/previous instance data is a
// different contract. There is no instance-group emitter in this helper.
struct Lod {
    int current{},next{};float transition{},start{};bool initialized{},border{};
    void advance(float distance,std::span<const float> thresholds,const scene_actor::GlobalLod& global,scene_actor::PassLod pass,float delta){
        if(thresholds.empty()||!global.updates)return;
        auto clamp=[&](int value){return std::clamp(value,0,int(thresholds.size()));};
        auto select=[&]{int value=0;while(value<int(thresholds.size())&&distance>=thresholds[value])++value;return clamp(std::max(pass.minimum,value+pass.shift));};
        if(global.explicit_lod){current=next=clamp(global.selection);transition=0;initialized=true;return;}
        if(!initialized){current=next=select();transition=0;initialized=true;return;}
        if(transition!=0){float width=std::max(4.f*std::pow(2.f,float(next)),4.f);float progress=std::max(transition+delta/global.maximum_seconds,std::clamp(std::abs(distance-start)/width/8,0.f,1.f));float step=std::min(std::abs(transition-progress),.25f);if(!std::isfinite(step))step=1;transition+=step;if(transition>=1){current=next;transition=0;}return;}
        if(border){float threshold=current==0?0:thresholds[current-1];if(std::abs(distance-threshold)>threshold*.1f)border=false;return;}
        int value=select();if(value!=current){next=value;transition=.001f;start=distance;border=true;}
    }
};
inline double squared_distance(scene_actor::Bounds bounds,scene_actor::Vec3 point){auto axis=[](double value,double minimum,double maximum){return value<minimum?minimum-value:value>maximum?value-maximum:0.;};double x=axis(point.x,bounds.minimum.x,bounds.maximum.x),y=axis(point.y,bounds.minimum.y,bounds.maximum.y),z=axis(point.z,bounds.minimum.z,bounds.maximum.z);return x*x+y*y+z*z;}
}
