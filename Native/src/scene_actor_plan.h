#pragma once
#include "spot_bounds.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <span>
#include <stdexcept>

// Numerical core for the resident scene contract. It owns selector progression;
// callers supply current committed actor/frame data, never cached visible output.
namespace scene_actor {
struct Vec3 {double x{},y{},z{};};
struct Bounds {Vec3 minimum{},maximum{};};
struct Plane {double x{},y{},z{},d{};};
struct Frustum {std::array<Plane,6> planes{};bool enabled{};};
inline bool disjoint(const Bounds& box,const Frustum& view){
    if(!view.enabled)return false;
    for(const auto& p:view.planes){
        double x=p.x>=0?box.minimum.x:box.maximum.x;
        double y=p.y>=0?box.minimum.y:box.maximum.y;
        double z=p.z>=0?box.minimum.z:box.maximum.z;
        if(p.x*x+p.y*y+p.z*z+p.d>0)return true;
    }
    return false;
}
struct GlobalLod {
    bool explicit_lod{},updates=true;int selection{};
    float hysteresis{},dead_zone{},dead_zone_mult{},maximum_seconds=1,minimum_seconds=.25f;
    float distance_add{},distance_mult=1;double smooth_camera_distance=100;
};
struct LodState {
    int current{},previous=-1;float transition{},start{};bool initialized{};
    void update(float distance,std::span<const float> thresholds,const GlobalLod& settings,float delta,bool immediate){
        auto select=[&]{int i=0;while(i<int(thresholds.size()) && distance>=thresholds[i])++i;return i;};
        if(settings.explicit_lod){current=std::min(int(thresholds.size()),std::max(0,settings.selection));previous=-1;transition=0;initialized=true;return;}
        if(!settings.updates && initialized)return;
        if(!initialized || immediate){current=select();previous=-1;transition=0;initialized=true;return;}
        if(transition==0){int chosen=current;
            while(chosen>0 && distance<thresholds[chosen-1]*(1-settings.hysteresis))--chosen;
            while(chosen<int(thresholds.size()) && distance>thresholds[chosen]*(1+settings.hysteresis))++chosen;
            if(chosen==current)return;previous=current;current=chosen;start=distance;
        }
        float moved=std::abs(start-distance),dead=std::max(settings.dead_zone,settings.dead_zone_mult*moved);
        float duration=moved<=dead?settings.maximum_seconds:settings.minimum_seconds;
        transition+=duration>0?std::max(0.f,delta)/duration:1;
        if(transition>=1){transition=0;previous=-1;}
    }
};
struct PassLod {int minimum{},shift{},visible_shift{};};
struct Spot {float position[3]{},direction[3]{},range{},cosine{};};
struct Actor {
    Vec3 translation{};Bounds bounds{};float forward_scale=1;
    bool visible=true,skip_main{},skip_forward{},cast_flag=true,casts=true;
};
struct Frame {
    Vec3 camera{};Frustum main;std::array<Frustum,8> cascades;uint32_t cascade_count{};
    float probe_distance{},shadow_distance=1500,coefficient=1,delta{};
    GlobalLod global;PassLod main_lod,forward_lod,spot_lod;std::array<PassLod,8> cascade_lod;
    std::span<const Spot> spots;
};
struct Slice {int lod{};float alpha{};bool main{},probe{},spots{};uint32_t cascades{};};
struct Result {
    std::array<Slice,12> slices{};uint32_t count{},cascade_memberships{};
    bool selected{},main_candidate{},main{},probe{},spot{};int spot_lod=-1;
    Vec3 centre{};double radius{};
    void add(Slice slice){
        for(uint32_t i=0;i<count;++i)if(slices[i].lod==slice.lod && slices[i].alpha==slice.alpha){auto& existing=slices[i];existing.main|=slice.main;existing.probe|=slice.probe;existing.spots|=slice.spots;existing.cascades|=slice.cascades;return;}
        if(count==slices.size())throw std::runtime_error("resident actor slice budget exceeded");slices[count++]=slice;
    }
};
inline Result cull(const Actor& actor,const Frame& frame){
    Result result{};if(!actor.visible)return result;
    result.centre={(actor.bounds.minimum.x+actor.bounds.maximum.x)*.5,(actor.bounds.minimum.y+actor.bounds.maximum.y)*.5,(actor.bounds.minimum.z+actor.bounds.maximum.z)*.5};
    double x=(actor.bounds.maximum.x-actor.bounds.minimum.x)*.5,y=(actor.bounds.maximum.y-actor.bounds.minimum.y)*.5,z=(actor.bounds.maximum.z-actor.bounds.minimum.z)*.5;
    result.radius=std::sqrt(x*x+y*y+z*z);x=result.centre.x-frame.camera.x;y=result.centre.y-frame.camera.y;z=result.centre.z-frame.camera.z;
    double distance=std::sqrt(x*x+y*y+z*z);bool main= !actor.skip_main && !disjoint(actor.bounds,frame.main);
    result.probe=frame.probe_distance>0 && distance<=frame.probe_distance+result.radius;
    if(!main && !result.probe && (!actor.cast_flag || distance>frame.shadow_distance+result.radius))return result;
    result.selected=true;result.main_candidate=main;return result;
}
inline Result plan(const Actor& actor,const Frame& frame,LodState& state,std::span<const float> thresholds,bool immediate,bool occluded){
    auto result=cull(actor,frame);if(!result.selected)return result;
    float x=float(actor.translation.x-frame.camera.x),y=float(actor.translation.y-frame.camera.y),z=float(actor.translation.z-frame.camera.z),scale=actor.forward_scale>0?actor.forward_scale:1;
    float distance=(std::sqrt(x*x+y*y+z*z)/scale*frame.coefficient+frame.global.distance_add)*frame.global.distance_mult;
    state.update(distance,thresholds,frame.global,frame.delta,immediate);
    result.main=result.main_candidate&&!occluded;result.probe=result.probe&&!actor.skip_forward;
    auto lod_for=[&](const PassLod& pass,int lod){return std::min(int(thresholds.size()),std::max(pass.minimum,lod+(result.main?pass.visible_shift:pass.shift)));};
    if(result.main){int current=lod_for(frame.main_lod,state.current);
        if(state.previous>=0 && state.transition>0){result.add({current,3-state.transition,true,false,false,0});result.add({lod_for(frame.main_lod,state.previous),state.transition,true,false,false,0});}
        else result.add({current,0,true,false,false,0});}
    if(result.probe)result.add({lod_for(frame.forward_lod,state.current),0,false,true,false,0});
    if(actor.casts){
        for(uint32_t c=0;c<std::min(8u,frame.cascade_count);++c)if(!disjoint(actor.bounds,frame.cascades[c])){result.add({lod_for(frame.cascade_lod[c],state.current),0,false,false,false,1u<<c});++result.cascade_memberships;}
        float centre[]{float(result.centre.x-frame.camera.x),float(result.centre.y-frame.camera.y),float(result.centre.z-frame.camera.z)};
        for(const auto& light:frame.spots){float delta[]{centre[0]-light.position[0],centre[1]-light.position[1],centre[2]-light.position[2]};if(spot_sphere_visible(delta,light.direction,light.range,light.cosine,float(result.radius))){result.spot=true;break;}}
        if(result.spot){result.spot_lod=lod_for(frame.spot_lod,state.current);result.add({result.spot_lod,0,false,false,true,0});}
    }
    return result;
}
}
