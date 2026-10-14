#pragma once
#include "native_pass_scene.h"
#include "resident_proxy_references.h"

namespace resident_proxy {
// CPU side of the resident OLD proxy boundary. Geometry bindings and source
// instance resources are validated by the render owner before publication.
// The GPU consumer receives model/actor references, never copied instance rows.
struct Model {bool valid=true;};
struct Family {std::vector<float> distances;std::vector<uint64_t> levels;};
struct Actor {
    uint64_t family{},group{},stream{};uint32_t owner{},first{},count{1};
    scene_actor::Actor selection;Lod lod;bool supported=true,dynamic{},parented{},depth_dirty=true,size_policy{},limit_far{},size_culled{};
    scene_actor::Bounds local_bounds;
    double inverse[12]{1,0,0,0,0,1,0,0,0,0,1,0};
};
struct Fallback {uint64_t actor{};uint32_t owner{};Lod original;};
class Scene {
    native_pass::Handles<Model> models;native_pass::Handles<Family> families;native_pass::Handles<Actor> actors;
    struct Selected{uint64_t handle{},views{},static_views{};Actor* actor{};const Family* family{};Lod next;};
    std::vector<Selected> selected;native_pass::Frame frame;bool pending{};
    std::vector<native_pass::QueryOwner> queries;std::vector<native_pass::Missing> missing;std::vector<Fallback> fallbacks;
    std::vector<ViewReferences> references;std::vector<uint32_t> visible;
    std::vector<std::vector<uint32_t>> view_selected;
    uint64_t static_dirty_views{};
    std::array<float,16> size_projection{};uint32_t size_width{},size_height{};bool size_enabled{};
    std::vector<std::pair<Actor*,bool>> size_updates;
    void idle()const{if(pending)throw std::runtime_error("resident proxy mutation during prepared epoch");}
    bool ready(uint64_t model){return models.valid(model)&&models.get(model).valid;}
    static double distance_squared(const Actor& actor,const scene_actor::Frame& frame){
        double squared;
        if(actor.parented){const auto& m=actor.inverse;auto p=frame.camera;scene_actor::Vec3 local{m[0]*p.x+m[1]*p.y+m[2]*p.z+m[3],m[4]*p.x+m[5]*p.y+m[6]*p.z+m[7],m[8]*p.x+m[9]*p.y+m[10]*p.z+m[11]};squared=squared_distance(actor.local_bounds,local);}
        else squared=squared_distance(actor.selection.bounds,frame.camera);
        return squared;
    }
    static float distance(const Actor& actor,const scene_actor::Frame& frame){return (std::sqrt(float(distance_squared(actor,frame)))*frame.coefficient+frame.global.distance_add)*frame.global.distance_mult;}
    bool size_cull(const Actor& actor,const Family& family)const{
        if(!size_enabled||!actor.size_policy||!size_width||!size_height)return false;float squared=float(distance_squared(actor,frame.selection)),last=0;for(float threshold:family.distances)last=std::max(last,threshold*threshold);if(!(squared>last))return false;
        auto bounds=actor.parented?actor.local_bounds:actor.selection.bounds;float extent=float(std::max({bounds.maximum.x-bounds.minimum.x,bounds.maximum.y-bounds.minimum.y,bounds.maximum.z-bounds.minimum.z}));float z=-float(std::sqrt(double(squared))),inverse=1.f/(z*size_projection[11]+size_projection[15]);float x=(extent*size_projection[0])*inverse*float(size_width)/2.f,y=(extent*size_projection[5])*inverse*float(size_height)/2.f,area=std::abs(x*y);return std::isfinite(area)&&area<20.f;
    }
    bool member(const Actor& actor,const native_pass::View& view)const{
        auto& a=actor.selection;if(!view.enabled||(actor.limit_far&&view.id>=5&&view.id<=8)||(view.kind==native_pass::Kind::Colour&&a.skip_main)||(view.kind==native_pass::Kind::Forward&&a.skip_forward)||(view.kind==native_pass::Kind::Depth&&(!a.cast_flag||!a.casts))||scene_actor::disjoint(a.bounds,view.frustum))return false;
        double x=a.bounds.maximum.x-a.bounds.minimum.x,y=a.bounds.maximum.y-a.bounds.minimum.y,z=a.bounds.maximum.z-a.bounds.minimum.z,size=x*x+y*y+z*z;
        if(view.kind==native_pass::Kind::Depth&&size<=view.minimum_size_squared)return false;
        if(view.maximum_distance>0){double radius=std::sqrt(size)*.5,dx=(a.bounds.minimum.x+a.bounds.maximum.x)*.5-frame.selection.camera.x,dy=(a.bounds.minimum.y+a.bounds.maximum.y)*.5-frame.selection.camera.y,dz=(a.bounds.minimum.z+a.bounds.maximum.z)*.5-frame.selection.camera.z;if(std::sqrt(dx*dx+dy*dy+dz*dz)>view.maximum_distance+radius)return false;}
        return true;
    }
public:
    uint64_t register_model(){return models.add({});}
    void set_size_policy(std::array<float,16> projection,uint32_t width,uint32_t height,bool enabled){idle();size_projection=projection;size_width=width;size_height=height;size_enabled=enabled;}
    uint64_t register_family(std::vector<float> distances){idle();if(distances.size()>1024||!std::is_sorted(distances.begin(),distances.end()))throw std::runtime_error("invalid proxy LOD thresholds");for(float d:distances)if(!std::isfinite(d)||d<0)throw std::runtime_error("invalid proxy distance");Family f{std::move(distances),{}};f.levels.resize(f.distances.size()+1);return families.add(std::move(f));}
    void bind_level(uint64_t family,uint32_t lod,uint64_t model){models.get(model);auto& f=families.get(family);if(lod>=f.levels.size())throw std::runtime_error("invalid proxy LOD binding");f.levels[lod]=model;}
    void validate_actor(const Actor& actor){const auto& family=families.get(actor.family);if(actor.lod.current<0||actor.lod.next<0||uint32_t(actor.lod.current)>=family.levels.size()||uint32_t(actor.lod.next)>=family.levels.size())throw std::runtime_error("proxy selector outside family");}
    uint64_t register_actor(Actor actor){idle();validate_actor(actor);return actors.add(std::move(actor));}
    void update_actor(uint64_t handle,Actor actor){idle();actor.lod=actors.get(handle).lod;actor.size_culled=actors.get(handle).size_culled;validate_actor(actor);actors.get(handle)=std::move(actor);}
    void remove_actor(uint64_t handle){idle();actors.remove(handle);}
    void remove_model(uint64_t handle){idle();models.remove(handle);}
    void remove_family(uint64_t handle){idle();families.remove(handle);}
    void invalidate_model(uint64_t handle){models.get(handle).valid=false;}
    Actor& actor_state(uint64_t handle){idle();return actors.get(handle);}
    const auto& query_owners()const{return queries;}const auto& missing_levels()const{return missing;}const auto& explicit_fallbacks()const{return fallbacks;}const auto& view_references()const{return references;}const auto& visible_owners()const{return visible;}
    uint64_t static_updates()const{return static_dirty_views;}
    void clear_frame(){idle();selected.clear();queries.clear();missing.clear();fallbacks.clear();references.clear();visible.clear();view_selected.clear();}
    void prepare(native_pass::Frame input,native_pass::ViewJobs& jobs){
        idle();if(input.views.size()>64)throw std::runtime_error("proxy view budget");uint64_t ids=0;for(auto& v:input.views){if(v.id>=64||(ids&(uint64_t(1)<<v.id)))throw std::runtime_error("duplicate proxy view");ids|=uint64_t(1)<<v.id;}
        frame=std::move(input);selected.clear();queries.clear();missing.clear();fallbacks.clear();references.clear();visible.clear();view_selected.clear();
        struct Candidate{uint64_t handle;Actor* actor;const Family* family;};std::vector<Candidate> candidates;
        size_updates.clear();actors.each([&](uint64_t handle,Actor& actor){if(!actor.supported||!families.valid(actor.family)){fallbacks.push_back({handle,actor.owner,actor.lod});return;}if(actor.selection.visible){auto& family=families.get(actor.family);bool culled=size_cull(actor,family);size_updates.emplace_back(&actor,culled);if(!culled)candidates.push_back({handle,&actor,&family});}});
        std::vector<std::vector<uint32_t>> memberships(frame.views.size());jobs.run(uint32_t(frame.views.size()),[&](uint32_t v){for(uint32_t i=0;i<candidates.size();++i)if(member(*candidates[i].actor,frame.views[v]))memberships[v].push_back(i);});
        std::vector<uint64_t> masks(candidates.size());for(uint32_t v=0;v<frame.views.size();++v)for(uint32_t i:memberships[v])masks[i]|=uint64_t(1)<<frame.views[v].id;
        std::vector<uint32_t> indices(candidates.size(),UINT32_MAX);std::map<uint64_t,uint32_t> query_indices;
        for(uint32_t i=0;i<candidates.size();++i)if(masks[i]){auto& c=candidates[i];auto next=c.actor->lod;scene_actor::PassLod lod{};for(auto& v:frame.views)if(v.kind==native_pass::Kind::Colour){lod=v.lod;break;}
            next.advance(distance(*c.actor,frame.selection),c.family->distances,frame.selection.global,lod,frame.selection.delta);
            uint64_t static_views=0;bool dynamic=c.actor->dynamic||next.transition!=0;for(auto& v:frame.views){uint64_t bit=uint64_t(1)<<v.id;if(v.only_static)static_views|=masks[i]&bit;if((v.only_static&&dynamic)||(v.only_dynamic&&!dynamic))masks[i]&=~bit;}
            indices[i]=uint32_t(selected.size());selected.push_back({c.handle,masks[i],static_views,c.actor,c.family,next});
            if(c.actor->group){auto [it,added]=query_indices.emplace(c.actor->group,uint32_t(queries.size()));if(added)queries.push_back({c.actor->group,masks[i],c.actor->owner});else queries[it->second].views|=masks[i];}
            for(int level:{next.current,next.transition!=0?next.next:next.current})if(!ready(c.family->levels[level])&&std::none_of(missing.begin(),missing.end(),[&](auto& m){return m.family==c.actor->family&&m.lod==uint32_t(level);}))missing.push_back({c.actor->family,uint32_t(level)});
        }
        view_selected.resize(frame.views.size());for(uint32_t v=0;v<frame.views.size();++v)for(uint32_t i:memberships[v])view_selected[v].push_back(indices[i]);pending=true;
    }
    void publish(std::span<const native_pass::QueryResult> results,native_pass::ViewJobs& jobs,const std::function<void(const std::vector<ViewReferences>&)>& validate={}){
        if(!pending)throw std::runtime_error("proxy publication without preparation");std::unordered_map<uint64_t,uint64_t> hidden;for(auto& r:results)if(!hidden.emplace(r.group,r.hidden_views).second)throw std::runtime_error("duplicate proxy visibility result");
        std::vector<Input> inputs;inputs.reserve(selected.size());std::vector<uint32_t> input_index(selected.size(),UINT32_MAX),next_visible;auto next_fallbacks=fallbacks;
        for(uint32_t i=0;i<selected.size();++i){auto& s=selected[i];auto& actor=*s.actor;auto hidden_group=hidden.find(actor.group);uint64_t mask=s.views&~(!actor.group||hidden_group==hidden.end()?0:hidden_group->second);
            if(!ready(s.family->levels[s.next.current])||(s.next.transition!=0&&!ready(s.family->levels[s.next.next]))){next_fallbacks.push_back({s.handle,actor.owner,actor.lod});continue;}
            input_index[i]=uint32_t(inputs.size());inputs.push_back({s.handle,actor.family,actor.stream,mask,actor.owner,actor.first,actor.count,s.next,s.family->levels});for(auto& v:frame.views)if(v.kind==native_pass::Kind::Colour&&(s.views&(uint64_t(1)<<v.id))){next_visible.push_back(actor.owner);break;}
        }
        std::vector<std::vector<uint32_t>> view_indices(frame.views.size());std::vector<PreparedView> views;for(uint32_t v=0;v<frame.views.size();++v){for(uint32_t s:view_selected[v])if(input_index[s]!=UINT32_MAX)view_indices[v].push_back(input_index[s]);views.push_back({frame.views[v].id,view_indices[v]});}
        auto output=prepare_references(inputs,views,jobs);
        if(validate)validate(output);
        uint64_t changed=0;for(auto& [actor,culled]:size_updates)if(actor->size_culled!=culled)for(auto& view:frame.views)if(view.only_static)changed|=uint64_t(1)<<view.id;for(uint32_t i=0;i<selected.size();++i)if(input_index[i]!=UINT32_MAX){auto& s=selected[i];const auto& previous=s.actor->lod;if(s.actor->depth_dirty||previous.current!=s.next.current||previous.next!=s.next.next||previous.transition!=s.next.transition)changed|=s.static_views;}
        references=std::move(output);fallbacks=std::move(next_fallbacks);visible=std::move(next_visible);static_dirty_views=changed;for(auto& [actor,culled]:size_updates)actor->size_culled=culled;for(uint32_t i=0;i<selected.size();++i)if(input_index[i]!=UINT32_MAX){selected[i].actor->lod=selected[i].next;selected[i].actor->depth_dirty=false;}pending=false;
    }
    void cancel(){if(!pending)throw std::runtime_error("proxy cancel without preparation");pending=false;selected.clear();view_selected.clear();references.clear();visible.clear();}
};
}
