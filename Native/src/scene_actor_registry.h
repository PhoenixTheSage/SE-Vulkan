#pragma once
#include "scene_actor_plan.h"
#include "../include/se_scene_actor.h"
#include <vector>
#include <unordered_set>

namespace scene_actor {
static_assert(sizeof(SevkSceneActor)==136 && sizeof(SevkSceneFrame)==1960 && sizeof(SevkSceneResult)==224);
struct Resident {Actor actor;LodState lod;Vec3 camera;bool have_camera{},alive{};uint32_t generation{};std::vector<float> thresholds;};
class Registry {
    std::vector<Resident> actors;std::vector<uint32_t> free;
    uint32_t generation{};bool pending{};
    Frame frame;std::vector<Spot> spots;std::vector<uint64_t> candidates;
    Resident& get(uint64_t handle){uint32_t index=uint32_t(handle);if(!index || index>actors.size() || !actors[index-1].alive || actors[index-1].generation!=uint32_t(handle>>32))throw std::runtime_error("stale scene actor");return actors[index-1];}
    static Vec3 vec(const double* p){return {p[0],p[1],p[2]};}
    static LodState state(const SevkSceneLod& p){return {p.current,p.previous,p.transition,p.start,p.initialized!=0};}
    static PassLod pass(const int32_t* p){return {p[0],p[1],p[2]};}
    static SevkSceneLod state(const LodState& p){return {p.current,p.previous,p.transition,p.start,p.initialized?1u:0u};}
public:
    uint64_t add(const SevkSceneActor& input,const float* distances,uint32_t count){
        if(pending || count>1024 || (count&&!distances))throw std::runtime_error("invalid scene registration");
        for(uint32_t i=0;i<count;++i)if(!std::isfinite(distances[i]) || (i && distances[i]<distances[i-1]))throw std::runtime_error("invalid scene LOD threshold");
        uint32_t index;if(free.empty()){if(actors.size()>=1000000)throw std::runtime_error("scene actor budget");index=uint32_t(actors.size());actors.emplace_back();}else{index=free.back();free.pop_back();}
        auto& a=actors[index];a={};a.alive=true;a.generation=++generation;if(!generation)throw std::runtime_error("scene generation exhausted");if(count)a.thresholds.assign(distances,distances+count);
        uint64_t handle=(uint64_t(a.generation)<<32)|(index+1);auto copied=input;copied.handle=handle;update(&copied,1);return handle;
    }
    void remove(uint64_t handle){if(pending)throw std::runtime_error("scene mutation during pending frame");auto& a=get(handle);a.alive=false;a.thresholds.clear();free.push_back(uint32_t(handle)-1);}
    void update(const SevkSceneActor* inputs,uint32_t count){
        if(pending || (count&&!inputs))throw std::runtime_error("invalid scene update");
        for(uint32_t i=0;i<count;++i){const auto& p=inputs[i];auto& a=get(p.handle);
            for(double v:p.translation)if(!std::isfinite(v))throw std::runtime_error("nonfinite scene translation");for(double v:p.bounds)if(!std::isfinite(v))throw std::runtime_error("nonfinite scene bounds");
            if(p.lod.current<0 || p.lod.current>int(a.thresholds.size()) || p.lod.previous<-1 || p.lod.previous>int(a.thresholds.size()))throw std::runtime_error("scene selector outside model");
            a.actor={vec(p.translation),{vec(p.bounds),vec(p.bounds+3)},p.scale,bool(p.flags&1),bool(p.flags&2),bool(p.flags&4),bool(p.flags&8),bool(p.flags&16)};
            a.lod=state(p.lod);a.camera=vec(p.camera);a.have_camera=p.have_camera!=0;
        }
    }
    void cull_frame(const SevkSceneFrame& input,const SevkSceneSpot* lights,uint32_t light_count,const uint64_t* handles,uint32_t count,uint8_t* gates){
        if(pending || count>1000000 || light_count>64 || (count&&(!handles||!gates)) || (light_count&&!lights) || input.cascade_count>8)throw std::runtime_error("invalid scene frame");
        frame={};frame.camera=vec(input.camera);frame.probe_distance=input.probe_distance;frame.shadow_distance=input.shadow_distance;frame.coefficient=input.coefficient;frame.delta=input.delta;frame.cascade_count=input.cascade_count;
        frame.global={bool(input.global_flags&1),bool(input.global_flags&2),input.selection,input.global[0],input.global[1],input.global[2],input.global[3],input.global[4],input.global[5],input.global[6],input.smooth_distance};
        frame.main_lod=pass(input.passes);frame.forward_lod=pass(input.passes+3);frame.spot_lod=pass(input.passes+6);for(int c=0;c<8;++c)frame.cascade_lod[c]=pass(input.passes+9+c*3);
        for(int v=0;v<9;++v){auto& f=v?frame.cascades[v-1]:frame.main;f.enabled=(input.enabled&(1u<<v))!=0;for(int p=0;p<6;++p){const double* values=input.planes+v*24+p*4;f.planes[p]={values[0],values[1],values[2],values[3]};}}
        spots.clear();for(uint32_t i=0;i<light_count;++i){Spot s{};std::copy_n(lights[i].position,3,s.position);std::copy_n(lights[i].direction,3,s.direction);s.range=lights[i].range;s.cosine=lights[i].cosine;spots.push_back(s);}frame.spots=spots;
        candidates.clear();if(count)candidates.assign(handles,handles+count);std::unordered_set<uint64_t> unique;
        for(uint32_t i=0;i<count;++i){if(!handles[i]){gates[i]=0;continue;}if(!unique.insert(handles[i]).second)throw std::runtime_error("duplicate scene candidate");auto r=cull(get(handles[i]).actor,frame);gates[i]=uint8_t((r.selected?1:0)|(r.main_candidate?2:0));}
        pending=true;
    }
    void finish(const uint8_t* hidden,SevkSceneResult* output,uint32_t count){
        if(!pending || count!=candidates.size() || (count&&(!hidden||!output)))throw std::runtime_error("scene frame completion mismatch");
        for(uint32_t i=0;i<count;++i){output[i]={};if(!candidates[i])continue;auto& a=get(candidates[i]);double x=a.camera.x-frame.camera.x,y=a.camera.y-frame.camera.y,z=a.camera.z-frame.camera.z;
            auto r=plan(a.actor,frame,a.lod,a.thresholds,!a.have_camera || std::sqrt(x*x+y*y+z*z)>frame.global.smooth_camera_distance,hidden[i]!=0);
            auto& o=output[i];o.lod=state(a.lod);o.flags=(r.selected?1u:0u)|(r.main_candidate?2u:0u)|(r.main?4u:0u)|(r.probe?8u:0u)|(r.spot?16u:0u);o.count=r.count;o.spot_lod=r.spot_lod;
            for(uint32_t s=0;s<r.count;++s){auto& v=r.slices[s];o.slices[s]={v.lod,v.alpha,(v.main?1u:0u)|(v.probe?2u:0u)|(v.spots?4u:0u),v.cascades};}
            if(r.selected){a.camera=frame.camera;a.have_camera=true;}
        }
        pending=false;
    }
};
}
