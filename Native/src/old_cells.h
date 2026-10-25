#pragma once
#include "scene_actor_plan.h"
#include "stock_proxy_lod.h"
#include "native_pass_view_jobs.h"
#include "se_old_order.h"
#include <array>
#include <cmath>
#include <cstring>
#include <map>
#include <vector>

// Native producer for static OLD cube grid cells. Each frame reproduces the
// managed retained-submission decision for one cell (ancestor visibility,
// main/probe/shadow distance, OLD LOD with two-mesh transitions, cascade
// mask, group occlusion) and emits the same world draws, with identical
// object constants and instance bytes, after the managed packets.
namespace old_cells {
struct Part {
    uint64_t recipe{};uint32_t view_flags{},row0{},row1{},row2{},alpha_offset{},main_part{};
    SevkOldPartOrder old_order{};std::vector<uint8_t> constants;
};
// model identifies the drawn model object; equal keys mean stock draws one mesh.
struct Level {uint32_t model{};std::vector<Part> parts;};
struct Cell {
    uint32_t owner{};uint64_t group{};
    bool visible{},skip_main{},cast_flag{},casts{},dynamic_shadow{},parented{},lod_eligible{},skinning{},outside_far{};
    scene_actor::Bounds bounds;float local_minimum[3]{},local_maximum[3]{};
    double world[16]{},inverse[16]{};
    std::vector<float> thresholds;std::vector<Level> levels;
    std::vector<uint8_t> instances;uint32_t count{};
    resident_proxy::Lod lod;
    // Last drawn level models (-1 none); a change invalidates occlusion and spots.
    int shadow_current=-1,shadow_next=-1;float shadow_alpha{};
};
struct Cascade {bool active{},size_test{},fitted{};double size_squared{};scene_actor::Frustum frustum;};
struct Frame {
    scene_actor::Vec3 camera{};scene_actor::Frustum main;bool new_views{},stock_far{};
    double probe_distance{},shadow_distance{};std::array<Cascade,8> cascades{};
    scene_actor::GlobalLod global;scene_actor::PassLod pass;float coefficient=1,delta{};
};
struct Query {uint64_t group{};uint32_t representative{},cascades{};bool main{};};
struct Result {uint64_t group{};bool main_hidden{};uint32_t cascades_hidden{};};
struct Change {double centre[3]{},radius{};};
// One emitted world draw; constants and instances are final bytes.
struct Draw {
    const Part* part{};const Cell* cell{};uint32_t flags{},cascades{};float origin[3]{},radius{},alpha{};
};

class Scene {
    struct Entry {Cell cell;uint32_t generation{};bool alive{};};
    std::vector<Entry> entries;std::vector<uint32_t> free;uint32_t generation{};
    struct Decision {bool drawn{},probe_only{},outside_probe{},occluded{};int current{},next=-1;float alpha{},next_alpha{};uint32_t cascades{};resident_proxy::Lod lod;};
    std::vector<Decision> decisions;Frame frame;bool pending{},published{},build_pending{};std::vector<Result> deferred_results;
    std::vector<Query> queries;std::vector<uint32_t> visible;std::vector<Change> changes;std::vector<Draw> draws;
    Cell& get(uint64_t handle){uint32_t i=uint32_t(handle);if(!i||i>entries.size()||!entries[i-1].alive||entries[i-1].generation!=uint32_t(handle>>32))throw std::runtime_error("stale OLD cell handle");return entries[i-1].cell;}
    // A deferred draw build reads and updates cells, so it finishes before any mutation.
    void idle(){if(pending)throw std::runtime_error("OLD cell mutation during prepared frame");finish();}
    static void validate(const Cell& cell){
        if(cell.levels.empty()||cell.levels.size()!=cell.thresholds.size()+1||cell.count>1000000||cell.instances.size()!=uint64_t(cell.count)*64)throw std::runtime_error("invalid OLD cell representation");
        for(auto& level:cell.levels)for(auto& part:level.parts){if(!part.recipe||part.constants.empty()||part.constants.size()>65536)throw std::runtime_error("invalid OLD cell part");
            for(uint32_t at:{part.row0,part.row1,part.row2})if(uint64_t(at)+16>part.constants.size())throw std::runtime_error("OLD cell matrix row outside constants");
            if(uint64_t(part.alpha_offset)+4>part.constants.size())throw std::runtime_error("OLD cell alpha outside constants");}
        if(cell.lod.current<0||cell.lod.next<0||uint32_t(cell.lod.current)>=cell.levels.size()||uint32_t(cell.lod.next)>=cell.levels.size())throw std::runtime_error("OLD cell selector outside levels");
    }
    // MyActor.CalculateCameraDistanceSquared: parented actors transform the
    // camera into local space (double) and measure the float local AABB.
    float distance_squared(const Cell& cell)const{
        if(!cell.parented){auto axis=[](double v,double a,double b){return v<a?a-v:v>b?v-b:0.;};const auto& b=cell.bounds;double x=axis(frame.camera.x,b.minimum.x,b.maximum.x),y=axis(frame.camera.y,b.minimum.y,b.maximum.y),z=axis(frame.camera.z,b.minimum.z,b.maximum.z);return float(x*x+y*y+z*z);}
        const double* m=cell.inverse;const auto& p=frame.camera;
        double w=1.0/(p.x*m[3]+p.y*m[7]+p.z*m[11]+m[15]);
        float local[3]{float((p.x*m[0]+p.y*m[4]+p.z*m[8]+m[12])*w),float((p.x*m[1]+p.y*m[5]+p.z*m[9]+m[13])*w),float((p.x*m[2]+p.y*m[6]+p.z*m[10]+m[14])*w)};
        bool inside=true;for(int i=0;i<3;i++)inside&=local[i]>=cell.local_minimum[i]&&local[i]<=cell.local_maximum[i];if(inside)return 0.f;
        float d[3];for(int i=0;i<3;i++)d[i]=std::clamp(local[i],cell.local_minimum[i],cell.local_maximum[i])-local[i];return d[0]*d[0]+d[1]*d[1]+d[2]*d[2];
    }
    uint32_t cascade_mask(const scene_actor::Bounds& b)const{
        uint32_t mask=0;double x=b.maximum.x-b.minimum.x,y=b.maximum.y-b.minimum.y,z=b.maximum.z-b.minimum.z,size=x*x+y*y+z*z;
        for(uint32_t c=0;c<8;c++){auto& cascade=frame.cascades[c];if(!cascade.active)continue;if(cascade.size_test&&size<=cascade.size_squared)continue;
            if(!cascade.fitted||!scene_actor::disjoint(b,cascade.frustum))mask|=1u<<c;}
        return mask;
    }
    void decide(Cell& cell,Decision& out){
        out={};out.lod=cell.lod;if(!cell.visible||(cell.skip_main&&!frame.new_views))return;
        const auto& b=cell.bounds;out.probe_only=cell.skip_main||scene_actor::disjoint(b,frame.main);
        double cx=(b.minimum.x+b.maximum.x)*.5,cy=(b.minimum.y+b.maximum.y)*.5,cz=(b.minimum.z+b.maximum.z)*.5;
        double dx=cx-frame.camera.x,dy=cy-frame.camera.y,dz=cz-frame.camera.z,distance=std::sqrt(dx*dx+dy*dy+dz*dz);
        double hx=(b.maximum.x-b.minimum.x)*.5,hy=(b.maximum.y-b.minimum.y)*.5,hz=(b.maximum.z-b.minimum.z)*.5,radius=std::sqrt(hx*hx+hy*hy+hz*hz);
        out.outside_probe=frame.probe_distance<=0||distance>frame.probe_distance+radius;
        if(out.probe_only&&out.outside_probe&&(!cell.cast_flag||distance>frame.shadow_distance+radius))return;
        out.drawn=true;
        int lod=0;float alpha=0;
        if(cell.lod_eligible){float distance_lod=float(std::sqrt(double(distance_squared(cell))));distance_lod=(distance_lod*frame.coefficient+frame.global.distance_add)*frame.global.distance_mult;if(cell.skinning)distance_lod*=.25f;
            out.lod.advance(distance_lod,cell.thresholds,frame.global,frame.pass,frame.delta);lod=out.lod.current;alpha=out.lod.transition;}
        int next=cell.lod_eligible&&alpha!=0?out.lod.next:-1;
        if(next>=0&&cell.levels[lod].model==cell.levels[next].model){next=-1;alpha=0;}
        out.current=lod;out.next=next;out.alpha=alpha;out.next_alpha=next>=0?3-alpha:0;
        if(cell.casts){uint32_t mask=cascade_mask(b);if(frame.stock_far&&!cell.outside_far)mask&=15u;out.cascades=mask;}
    }
public:
    uint64_t add(Cell cell){idle();validate(cell);if(generation==UINT32_MAX)throw std::runtime_error("OLD cell generation exhausted");uint32_t i;if(free.empty()){i=uint32_t(entries.size());entries.emplace_back();}else{i=free.back();free.pop_back();}entries[i]={std::move(cell),++generation,true};return (uint64_t(generation)<<32)|(i+1);}
    // Commits keep the native selector and last representation.
    void update(uint64_t handle,Cell cell){idle();auto& current=get(handle);cell.lod=current.lod;cell.shadow_current=current.shadow_current;cell.shadow_next=current.shadow_next;cell.shadow_alpha=current.shadow_alpha;
        if(cell.lod.current<0||uint32_t(cell.lod.current)>=cell.levels.size()||uint32_t(cell.lod.next)>=cell.levels.size()){cell.lod=resident_proxy::Lod{};cell.shadow_current=cell.shadow_next=-1;}validate(cell);current=std::move(cell);}
    // Transform/visibility commits keep representation, selector and stream.
    Cell& pose_target(uint64_t handle){idle();return get(handle);}
    resident_proxy::Lod remove(uint64_t handle){idle();auto lod=get(handle).lod;uint32_t i=uint32_t(handle)-1;entries[i].alive=false;entries[i].cell={};free.push_back(i);return lod;}
    const Cell& cell(uint64_t handle){finish();return get(handle);}
    uint32_t live()const{uint32_t n=0;for(auto& e:entries)n+=e.alive;return n;}
    void clear_frame(){pending=published=build_pending=false;queries.clear();visible.clear();changes.clear();draws.clear();}
    void prepare(Frame input,native_pass::ViewJobs* jobs){
        idle();frame=input;published=false;queries.clear();visible.clear();changes.clear();draws.clear();decisions.assign(entries.size(),{});
        uint32_t count=uint32_t(entries.size()),chunks=jobs&&count>=512?std::min(16u,(count+255)/256):1u;
        auto run=[&](uint32_t c){uint32_t begin=uint32_t(uint64_t(count)*c/chunks),end=uint32_t(uint64_t(count)*(c+1)/chunks);for(uint32_t i=begin;i<end;++i)if(entries[i].alive)decide(entries[i].cell,decisions[i]);};
        if(chunks>1)jobs->run(chunks,run);else run(0);
        std::map<uint64_t,uint32_t> by_group;
        for(uint32_t i=0;i<count;++i){auto& d=decisions[i];if(!d.drawn)continue;auto& cell=entries[i].cell;if(!d.probe_only)visible.push_back(cell.owner);
            if(cell.group&&(!d.probe_only||d.cascades)){auto [it,added]=by_group.emplace(cell.group,uint32_t(queries.size()));if(added)queries.push_back({cell.group,cell.owner,0,false});auto& q=queries[it->second];q.main|=!d.probe_only;q.cascades|=d.cascades;}}
        pending=true;
    }
    // With defer the results are checked now and the draws are built by finish(), which runs when the
    // draws are emitted (recording) or before the next mutation, so the cell state is unchanged.
    void publish(const std::vector<Result>& results,bool defer=false){
        if(!pending)throw std::runtime_error("OLD cell publication without preparation");
        std::map<uint64_t,const Result*> hidden;for(auto& r:results)if(!hidden.emplace(r.group,&r).second)throw std::runtime_error("duplicate OLD cell visibility result");
        for(auto& q:queries)if(!hidden.count(q.group))throw std::runtime_error("missing OLD cell visibility result");
        if(defer){deferred_results=results;build_pending=true;pending=false;published=true;return;}
        build(hidden);
    }
    void finish(){if(!build_pending)return;build_pending=false;std::map<uint64_t,const Result*> hidden;for(auto& r:deferred_results)hidden.emplace(r.group,&r);build(hidden);}
    bool building()const{return build_pending;}
    void build(const std::map<uint64_t,const Result*>& hidden){
        draws.clear();changes.clear();
        for(uint32_t i=0;i<decisions.size();++i){auto& d=decisions[i];if(!d.drawn)continue;auto& cell=entries[i].cell;
            const Result* r=nullptr;if(cell.group){auto it=hidden.find(cell.group);if(it!=hidden.end())r=it->second;}
            d.occluded=!d.probe_only&&r&&r->main_hidden;uint32_t cascades=d.cascades&~(r?r->cascades_hidden:0u);
            const auto& b=cell.bounds;double cx=(b.minimum.x+b.maximum.x)*.5,cy=(b.minimum.y+b.maximum.y)*.5,cz=(b.minimum.z+b.maximum.z)*.5,hx=(b.maximum.x-b.minimum.x)*.5,hy=(b.maximum.y-b.minimum.y)*.5,hz=(b.maximum.z-b.minimum.z)*.5,radius=std::sqrt(hx*hx+hy*hy+hz*hz);
            int current_model=int(cell.levels[d.current].model),next_model=d.next>=0?int(cell.levels[d.next].model):-1;
            if(cell.shadow_current!=current_model||cell.shadow_next!=next_model||cell.shadow_alpha!=d.alpha){if(cell.shadow_current>=0||cell.lod_eligible)changes.push_back({{cx,cy,cz},radius});cell.shadow_current=current_model;cell.shadow_next=next_model;cell.shadow_alpha=d.alpha;}
            cell.lod=d.lod;if(!cell.count)continue;
            float origin[3]{float(cx-frame.camera.x),float(cy-frame.camera.y),float(cz-frame.camera.z)};
            auto emit=[&](int level,float alpha){uint32_t flags=(cell.casts?0u:512u)|(d.probe_only||d.occluded?4096u:0u)|(d.outside_probe?16384u:0u)|(cell.dynamic_shadow||alpha!=0?0x1000000u:0u);
                for(auto& part:cell.levels[level].parts){Draw draw{&part,&cell,flags|part.view_flags,cascades,{origin[0],origin[1],origin[2]},float(radius),alpha};draws.push_back(draw);}};
            emit(d.current,d.alpha);if(d.next>=0)emit(d.next,d.next_alpha);
        }
        pending=false;published=true;
    }
    void cancel(){pending=false;published=false;build_pending=false;draws.clear();}
    bool ready()const{return published;}
    void consume(){finish();published=false;draws.clear();}
    const Frame& current_frame()const{return frame;}
    const std::vector<Query>& requests()const{return queries;}
    const std::vector<uint32_t>& visible_owners()const{return visible;}
    const std::vector<Change>& representation_changes()const{return changes;}
    const std::vector<Draw>& emitted()const{return draws;}
    // Same float rows as WorldConstants.WriteMatrix of the camera-relative pose.
    static void write_constants(const Draw& draw,const scene_actor::Vec3& camera,uint8_t* destination){
        const auto& part=*draw.part;const double* w=draw.cell->world;std::memcpy(destination,part.constants.data(),part.constants.size());
        double tx=w[12]-camera.x,ty=w[13]-camera.y,tz=w[14]-camera.z;
        float row0[4]{float(w[0]),float(w[4]),float(w[8]),float(tx)},row1[4]{float(w[1]),float(w[5]),float(w[9]),float(ty)},row2[4]{float(w[2]),float(w[6]),float(w[10]),float(tz)};
        std::memcpy(destination+part.row0,row0,16);std::memcpy(destination+part.row1,row1,16);std::memcpy(destination+part.row2,row2,16);std::memcpy(destination+part.alpha_offset,&draw.alpha,4);
    }
};
}
