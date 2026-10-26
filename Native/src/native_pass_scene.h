#pragma once
#include "scene_actor_plan.h"
#include "native_pass_view_jobs.h"
#include <map>
#include <vector>
#include <tuple>
#include <unordered_map>
#include <limits>
#include <bit>
#include <compare>
#include <chrono>
#include <memory>
#include <cstring>

// Resident preparation-to-emission boundary. Prepared selections stay native;
// callers receive only unique query owners, missing resources and fallbacks.
namespace native_pass {
using scene_actor::Bounds;using scene_actor::Vec3;using scene_actor::LodState;
enum class Kind:uint32_t {Colour,Depth,Forward};
struct Part {
    uint64_t mesh{},colour{},normal{},extensions{},alpha{};
    uint32_t first{},count{},material{},shader{};
    bool masked{},double_sided{},depth=true,forward=true;
};
struct DepthPiece {uint32_t first{},count{},material{};};
struct DepthSegment {Part range;std::vector<DepthPiece> pieces;};
struct Model {std::vector<Part> parts;bool valid=true;std::vector<DepthSegment> depth;bool glass{};};
struct Family {std::vector<float> distances;std::vector<uint64_t> levels;};
struct Appearance {uint32_t material{};uint16_t colour[4]{0x3c00,0x3c00,0x3c00,0x3c00};bool visible=true;};
struct Actor {
    uint64_t history_identity{};
    uint32_t owner{};uint64_t family{},group{};
    scene_actor::Actor selection;LodState lod;Vec3 last_camera;bool have_camera{},supported=true,dynamic{};
    // Camera-independent transform: three basis rows plus double translation.
    float basis[9]{1,0,0,0,1,0,0,0,1};uint16_t key_colour[3]{};
    std::vector<Appearance> appearance;
    // Pose and ownership commits leave independently resident materials intact.
    // This is an input operation; stored actors always own complete appearance.
    bool replace_appearance=true;
    uint64_t depth_epoch=1;
};
struct View {
    uint32_t id{};Kind kind{};scene_actor::Frustum frustum;scene_actor::PassLod lod;
    bool enabled=true,only_dynamic{},only_static{};double minimum_size_squared{},maximum_distance{};
};
struct Frame {scene_actor::Frame selection;std::vector<View> views;};
struct QueryOwner {uint64_t group{},views{};uint32_t representative{};};
struct QueryResult {uint64_t group{},hidden_views{};};
struct Missing {uint64_t family{};uint32_t lod{};};
struct Fallback {uint64_t actor{};uint32_t owner{};LodState lod;};
struct TransparentRequest {uint64_t actor{};uint32_t owner{},lod{};float alpha{};};
struct Transform {float rows[12]{};};
struct ColourInstance {Transform transform;uint16_t key[4]{},colour[4]{};};
static_assert(sizeof(Transform)==48 && sizeof(ColourInstance)==64);
struct GroupKey {
    uint32_t view{},shader{},first{},count{};uint64_t mesh{},colour{},normal{},extensions{},alpha{};
    bool masked{},double_sided{};
    auto operator<=>(const GroupKey&)const=default;
};
struct InstanceStream {
    uint32_t index{};std::vector<uint8_t> instances;std::vector<uint32_t> owners;std::vector<uint64_t> identities;
};
struct Group {
    GroupKey key;Kind kind{};std::shared_ptr<InstanceStream> stream;uint32_t main_part{};
    uint32_t stride()const{return kind==Kind::Depth?48:64;}
    uint32_t count()const{return uint32_t(stream->owners.size());}
};
template<class T>class Handles {
    struct Entry {T value;uint32_t generation{};bool alive{};};
    std::vector<Entry> entries;std::vector<uint32_t> free;uint32_t generation{};
public:
    uint64_t add(T value){if(generation==UINT32_MAX)throw std::runtime_error("pass handle generation exhausted");uint32_t i;if(free.empty()){i=uint32_t(entries.size());entries.emplace_back();}else{i=free.back();free.pop_back();}entries[i]={std::move(value),++generation,true};return (uint64_t(generation)<<32)|(i+1);}
    T& get(uint64_t handle){uint32_t i=uint32_t(handle);if(!i||i>entries.size()||!entries[i-1].alive||entries[i-1].generation!=uint32_t(handle>>32))throw std::runtime_error("stale pass handle");return entries[i-1].value;}
    bool valid(uint64_t handle)const{uint32_t i=uint32_t(handle);return i&&i<=entries.size()&&entries[i-1].alive&&entries[i-1].generation==uint32_t(handle>>32);}
    void remove(uint64_t handle){get(handle);uint32_t i=uint32_t(handle)-1;entries[i].alive=false;entries[i].value={};free.push_back(i);}
    template<class F>void each(F&& fn){for(uint32_t i=0;i<entries.size();++i)if(entries[i].alive)fn((uint64_t(entries[i].generation)<<32)|(i+1),entries[i].value);}
    uint32_t slots()const{return uint32_t(entries.size());}
    template<class F>void each_range(uint32_t begin,uint32_t end,F&& fn){for(uint32_t i=begin;i<end&&i<entries.size();++i)if(entries[i].alive)fn((uint64_t(entries[i].generation)<<32)|(i+1),entries[i].value);}
};
class Scene {
    Handles<Model> models;Handles<Family> families;Handles<Actor> actors;
    struct Selection {uint64_t handle{},views{};LodState next;Vec3 camera;bool main_candidate{};};
    std::vector<Selection> selected;Frame frame;bool pending{};
    std::vector<QueryOwner> queries;std::vector<Missing> missing;std::vector<Fallback> fallbacks;std::vector<Group> groups;std::vector<std::shared_ptr<InstanceStream>> streams;std::vector<uint32_t> visible_owners;
    // Published groups are cleared with the frame so a retired publication cannot
    // be recorded. The grouped instance storage stays. The next publication
    // rewrites a row only when its owner, epoch, transition or material
    // activation changes, and patches camera-relative translation in place.
    using BatchKey=std::tuple<uint32_t,uint64_t,bool,std::vector<uint32_t>>;
    // Batches are only looked up; publication order comes from batch_order.
    struct BatchHash {size_t operator()(const BatchKey& key)const noexcept{
        uint64_t h=(uint64_t(std::get<0>(key))<<1|uint64_t(std::get<2>(key)))*0x9e3779b97f4a7c15ull^std::get<1>(key);
        for(uint32_t material:std::get<3>(key)){h^=material;h*=0x100000001b3ull;}h^=h>>29;h*=0xbf58476d1ce4e5b9ull;h^=h>>32;return size_t(h);}};
    struct RetainedBatch {
        std::vector<uint32_t> owners,activated;std::vector<uint64_t> epochs;std::vector<float> states;
        scene_actor::Vec3 camera{};std::shared_ptr<InstanceStream> base;std::map<uint32_t,std::shared_ptr<InstanceStream>> materials;
    };
    std::unordered_map<BatchKey,RetainedBatch,BatchHash> retained_batches;
    // Optional lanes for per-batch publication; output is identical either way.
    std::unique_ptr<ViewJobs> jobs;
    struct SpotKeyHash {size_t operator()(const std::pair<uint32_t,uint32_t>& key)const noexcept{uint64_t h=(uint64_t(key.first)<<32|key.second)*0x9e3779b97f4a7c15ull;return size_t(h^h>>31);}};
    std::unordered_map<std::pair<uint32_t,uint32_t>,std::pair<uint64_t,uint64_t>,SpotKeyHash> spot_signature;
    uint64_t spot_updates{};
    // Retained streams and spotlight signatures outlive individual actors.
    // Never reuse a commit epoch when an owner ID is removed and recreated.
    uint64_t actor_epoch{};
    std::vector<TransparentRequest> transparent;
    bool model_ready(uint64_t handle){return models.valid(handle)&&models.get(handle).valid;}
    static uint16_t half(float f){
        // Stock packed instance stream uses IEEE binary16, round to nearest even.
        uint32_t bits=std::bit_cast<uint32_t>(f),sign=(bits>>16)&0x8000,exponent=(bits>>23)&255,mantissa=bits&0x7fffff;
        if(exponent==255)return uint16_t(sign|0x7c00|(mantissa?0x200:0));int e=int(exponent)-127+15;
        if(e>=31)return uint16_t(sign|0x7c00);if(e<=0){if(e<-10)return uint16_t(sign);mantissa|=0x800000;uint32_t shift=uint32_t(14-e),result=mantissa>>shift,remainder=mantissa&((1u<<shift)-1),mid=1u<<(shift-1);return uint16_t(sign|result+(remainder>mid||(remainder==mid&&(result&1))));}
        uint32_t result=(uint32_t(e)<<10)|(mantissa>>13),remainder=mantissa&8191;return uint16_t(sign|result+(remainder>4096||(remainder==4096&&(result&1))));
    }
    void idle()const{settle();if(pending)throw std::runtime_error("resident mutation during prepared pass frame");}
    static Transform transform(const Actor& actor,const Vec3& camera){Transform output{};const double translation[]{actor.selection.translation.x-camera.x,actor.selection.translation.y-camera.y,actor.selection.translation.z-camera.z};for(uint32_t r=0;r<3;++r){for(uint32_t c=0;c<3;++c)output.rows[r*4+c]=actor.basis[r*3+c];output.rows[r*4+3]=float(translation[r]);}return output;}
    static const Appearance* appearance(const Actor& actor,uint32_t material){const Appearance* defaults=nullptr;for(auto& item:actor.appearance){if(item.material==material)return &item;if(item.material==UINT32_MAX)defaults=&item;}return defaults;}
    int lod_for(const Family& family,const View& view,const Selection& selection,bool main)const{return std::min(int(family.distances.size()),std::max(view.lod.minimum,selection.next.current+(main?view.lod.visible_shift:view.lod.shift)));}
    void request(uint64_t family,uint32_t lod){auto item=Missing{family,lod};if(std::none_of(missing.begin(),missing.end(),[&](const Missing& m){return m.family==family&&m.lod==lod;}))missing.push_back(item);}
public:
    struct Profile {uint64_t prepares{},publishes{},committed_actors{},selected_actors{},emitted_rows{},stream_bytes{},groups{};double commits_us{},prepare_us{},requests_us{},publish_us{},emit_us{},spots_us{};uint64_t appearance_entries{};double actor_parse_us{},actor_apply_us{};double pack_us{},select_us{},merge_us{};};
    struct CpuScope {double* destination{};std::chrono::steady_clock::time_point start;CpuScope(bool enabled,double& value){if(enabled){destination=&value;start=std::chrono::steady_clock::now();}}~CpuScope(){if(destination)*destination+=std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-start).count();}};
    bool profiling{};Profile profile;
    uint64_t register_model(Model model){
        settle();
        if((model.parts.empty()&&!model.glass)||model.parts.size()>4096)throw std::runtime_error("invalid pass model");
        for(auto& part:model.parts)if(!part.mesh||!part.count||part.count%3||part.first%3||uint64_t(part.first)+part.count>UINT32_MAX)throw std::runtime_error("invalid immutable pass range");
        // Stock depth preprocessing belongs to immutable model/LOD registration.
        // Shared mesh ranges retain alpha/skipped boundaries. Solid segments keep
        // source material pieces so later visibility changes can split coverage
        // without replaying the colour-material representation in every view.
        model.depth.clear();bool merge=false;
        for(auto& part:model.parts){
            if(!part.depth){merge=false;continue;}
            if(merge&&!part.masked&&model.depth.back().range.mesh==part.mesh&&model.depth.back().range.first+model.depth.back().range.count==part.first){
                model.depth.back().range.count+=part.count;model.depth.back().pieces.push_back({part.first,part.count,part.material});
            }else model.depth.push_back({part,{{part.first,part.count,part.material}}});
            merge=!part.masked;
        }
        // Lazy resolution may register a model while a frame is prepared.
        return models.add(std::move(model));
    }
    uint64_t register_family(std::vector<float> distances){idle();if(distances.size()>1024||!std::is_sorted(distances.begin(),distances.end()))throw std::runtime_error("invalid pass family");for(auto distance:distances)if(!std::isfinite(distance)||distance<0)throw std::runtime_error("invalid authored LOD distance");Family family{std::move(distances),{}};family.levels.resize(family.distances.size()+1);return families.add(std::move(family));}
    void bind_level(uint64_t family,uint32_t lod,uint64_t model){settle();models.get(model);auto& f=families.get(family);if(lod>=f.levels.size())throw std::runtime_error("invalid pass model LOD");f.levels[lod]=model;}
    uint64_t register_actor(Actor actor){idle();families.get(actor.family);if(!actor.replace_appearance)throw std::runtime_error("registration requires complete resident appearance");actor.depth_epoch=++actor_epoch;uint64_t handle=actors.add(std::move(actor));actors.get(handle).history_identity=handle;return handle;}
    void update_actor(uint64_t handle,Actor actor){idle();families.get(actor.family);if(!actor.replace_appearance&&!actor.appearance.empty())throw std::runtime_error("preserved appearance commit contains replacement data");auto& current=actors.get(handle);actor.history_identity=handle;actor.lod=current.lod;actor.last_camera=current.last_camera;actor.have_camera=current.have_camera;actor.depth_epoch=++actor_epoch;if(!actor.replace_appearance)actor.appearance=std::move(current.appearance);actor.replace_appearance=true;current=std::move(actor);}
    void update_actors(std::vector<std::pair<uint64_t,Actor>> updates){idle();std::map<uint64_t,bool> unique;for(auto& [handle,actor]:updates){actors.get(handle);families.get(actor.family);if(!actor.replace_appearance&&!actor.appearance.empty())throw std::runtime_error("preserved appearance commit contains replacement data");if(!unique.emplace(handle,true).second)throw std::runtime_error("duplicate resident actor commit");}for(auto& [handle,actor]:updates)update_actor(handle,std::move(actor));}
    void remove_actor(uint64_t handle){idle();actors.remove(handle);}
    void remove_family(uint64_t handle){idle();families.remove(handle);}
    void remove_model(uint64_t handle){idle();models.remove(handle);}
    Actor& actor_state(uint64_t handle){idle();return actors.get(handle);}
    void invalidate_dependency(uint32_t kind,uint64_t id){settle();if(kind!=0&&kind!=3)return;models.each([&](uint64_t,Model& model){for(auto& part:model.parts)if(kind==3?part.mesh==id:part.colour==id||part.normal==id||part.extensions==id||part.alpha==id){model.valid=false;break;}});}
    void clear_frame(){idle();selected.clear();queries.clear();missing.clear();fallbacks.clear();groups.clear();streams.clear();visible_owners.clear();transparent.clear();spot_updates=0;}
    uint64_t static_spot_updates()const{return spot_updates;}
    const auto& main_visible_owners()const{return visible_owners;}
    // Packing runs after selection on one background worker. Every reader of
    // groups, streams or resident state joins it first, so output is unchanged.
    void set_async_packing(bool enabled){settle();if(enabled&&!packer)packer=std::make_unique<Background>();else if(!enabled)packer.reset();}
    bool async_packing()const{return packer!=nullptr;}
    // Called with the blocked microseconds when a join had to wait (diagnostics).
    std::function<void(double)> on_blocked;
    void settle()const{if(!packing)return;packing=preparing=false;if(!on_blocked||!packer->pending()){packer->wait();return;}
        auto start=std::chrono::steady_clock::now();packer->wait();on_blocked(std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-start).count());}
    void set_parallel(bool enabled){settle();if(enabled&&!jobs)jobs=std::make_unique<ViewJobs>();else if(!enabled)jobs.reset();}
    bool parallel()const{return jobs!=nullptr;}
    bool parallel_selection=true;
    void prepare(Frame current){idle();prepare_body(std::move(current));}
    // Early preparation: the same selection on the background worker. Its readers (query, missing and
    // fallback accessors, publish, cancel and every resident mutation) join it first.
    void prepare_async(Frame current){idle();if(!packer){prepare_body(std::move(current));return;}
        packing=preparing=true;packer->start([this,frame=std::move(current)]() mutable {prepare_body(std::move(frame));});}
    void prepare_body(Frame current){
        CpuScope total(profiling,profile.prepare_us);if(profiling)++profile.prepares;
        if(current.views.size()>64)throw std::runtime_error("pass view budget");uint64_t ids=0;for(auto& view:current.views){if(view.id>=64||(ids&(uint64_t(1)<<view.id)))throw std::runtime_error("duplicate pass view");ids|=uint64_t(1)<<view.id;}
        frame=std::move(current);selected.clear();queries.clear();missing.clear();fallbacks.clear();groups.clear();streams.clear();transparent.clear();
        // Actor ranges select views and LODs into range-local outputs; merging
        // them in range order reproduces the serial selection, query, request
        // and fallback order exactly.
        struct Chunk {std::vector<Selection> selected;std::vector<Fallback> fallbacks;std::vector<QueryOwner> queries;std::vector<Missing> requests;uint64_t selected_count{};double requests_us{};};
        auto prepare_actor=[&](Chunk& out,uint64_t handle,Actor& actor){
            if(!actor.supported||!families.valid(actor.family)){out.fallbacks.push_back({handle,actor.owner,actor.lod});return;}if(!actor.selection.visible)return;
            auto& family=families.get(actor.family);Selection selection{handle,0,actor.lod,frame.selection.camera,false};
            double dx=actor.last_camera.x-frame.selection.camera.x,dy=actor.last_camera.y-frame.selection.camera.y,dz=actor.last_camera.z-frame.selection.camera.z;
            bool immediate=!actor.have_camera||std::sqrt(dx*dx+dy*dy+dz*dz)>frame.selection.global.smooth_camera_distance;
            float x=float(actor.selection.translation.x-frame.selection.camera.x),y=float(actor.selection.translation.y-frame.selection.camera.y),z=float(actor.selection.translation.z-frame.selection.camera.z),scale=actor.selection.forward_scale>0?actor.selection.forward_scale:1;
            float distance=(std::sqrt(x*x+y*y+z*z)/scale*frame.selection.coefficient+frame.selection.global.distance_add)*frame.selection.global.distance_mult;
            for(auto& view:frame.views){if(!view.enabled||(view.kind==Kind::Colour&&actor.selection.skip_main)||(view.kind==Kind::Forward&&actor.selection.skip_forward)||(view.kind==Kind::Depth&&(!actor.selection.cast_flag||!actor.selection.casts))||(view.only_dynamic&&!actor.dynamic)||(view.only_static&&actor.dynamic)||scene_actor::disjoint(actor.selection.bounds,view.frustum))continue;
                double sx=actor.selection.bounds.maximum.x-actor.selection.bounds.minimum.x,sy=actor.selection.bounds.maximum.y-actor.selection.bounds.minimum.y,sz=actor.selection.bounds.maximum.z-actor.selection.bounds.minimum.z;
                if(view.maximum_distance>0){double radius=std::sqrt(sx*sx+sy*sy+sz*sz)*.5,dx=(actor.selection.bounds.minimum.x+actor.selection.bounds.maximum.x)*.5-frame.selection.camera.x,dy=(actor.selection.bounds.minimum.y+actor.selection.bounds.maximum.y)*.5-frame.selection.camera.y,dz=(actor.selection.bounds.minimum.z+actor.selection.bounds.maximum.z)*.5-frame.selection.camera.z;if(std::sqrt(dx*dx+dy*dy+dz*dz)>view.maximum_distance+radius)continue;}
                if(view.kind==Kind::Depth && sx*sx+sy*sy+sz*sz<=view.minimum_size_squared)continue;
                selection.views|=uint64_t(1)<<view.id;if(view.kind==Kind::Colour)selection.main_candidate=true;
            }
            if(!selection.views)return;selection.next.update(distance,family.distances,frame.selection.global,frame.selection.delta,immediate);
            out.selected.push_back(selection);++out.selected_count;
            if(actor.group)out.queries.push_back({actor.group,selection.views,actor.owner});
            // Query results can change the per-view visible LOD shift. Resolve both
            // choices now; publication reuses this selector without advancing it.
            CpuScope resources(profiling,out.requests_us);
            for(auto& view:frame.views)if(selection.views&(uint64_t(1)<<view.id))for(bool main:{false,true}){
                int lod=lod_for(family,view,selection,main);if(!model_ready(family.levels[lod]))out.requests.push_back({actor.family,uint32_t(lod)});
                if(view.kind==Kind::Colour&&selection.next.previous>=0){int old=std::min(int(family.distances.size()),std::max(view.lod.minimum,selection.next.previous+(main?view.lod.visible_shift:view.lod.shift)));if(!model_ready(family.levels[old]))out.requests.push_back({actor.family,uint32_t(old)});}
            }
        };
        uint32_t slots=actors.slots(),count=jobs&&slots>=2?std::min(16u,std::max(2u,(slots+255)/256)):1u;std::vector<Chunk> chunks(count);
        auto run_chunk=[&](uint32_t c){actors.each_range(uint32_t(uint64_t(slots)*c/count),uint32_t(uint64_t(slots)*(c+1)/count),[&](uint64_t handle,Actor& actor){prepare_actor(chunks[c],handle,actor);});};
        if(count>1)jobs->run(count,run_chunk);else run_chunk(0);
        std::map<uint64_t,uint32_t> query_indices;
        for(auto& chunk:chunks){
            selected.insert(selected.end(),chunk.selected.begin(),chunk.selected.end());fallbacks.insert(fallbacks.end(),chunk.fallbacks.begin(),chunk.fallbacks.end());
            for(auto& query:chunk.queries){auto [it,added]=query_indices.emplace(query.group,uint32_t(queries.size()));if(added)queries.push_back(query);else queries[it->second].views|=query.views;}
            for(auto& item:chunk.requests)request(item.family,item.lod);
            if(profiling){profile.selected_actors+=chunk.selected_count;profile.requests_us+=chunk.requests_us;}
        }
        pending=true;
    }
    // These join only an in-flight preparation, not the packing that publish started.
    const auto& query_owners()const{if(preparing)settle();return queries;}const auto& missing_levels()const{if(preparing)settle();return missing;}const auto& explicit_fallbacks()const{if(preparing)settle();return fallbacks;}const auto& render_groups()const{settle();return groups;}const auto& instance_streams()const{settle();return streams;}
    const auto& transparent_requests()const{return transparent;}
    void publish(std::span<const QueryResult> results){
        settle();CpuScope total(profiling,profile.publish_us);if(profiling)++profile.publishes;
        if(!pending)throw std::runtime_error("pass publication without preparation");
        std::unordered_map<uint64_t,uint64_t> hidden;for(auto& result:results)if(!hidden.emplace(result.group,result.hidden_views).second)throw std::runtime_error("duplicate pass query result");
        auto hidden_views=[&](uint64_t group){auto it=hidden.find(group);return it==hidden.end()?uint64_t(0):it->second;};
        groups.clear();streams.clear();visible_owners.clear();transparent.clear();
        decltype(spot_signature) next_spots;next_spots.reserve(spot_signature.size());
        struct Row {const Actor* actor;float state;};
        struct Batch {const Model* model;const View* view;std::vector<Row> rows;};
        // Stock groups by model/LOD, view and render state. Hidden material
        // boundaries need distinct coverage; appearance values stay in streams.
        struct PlainKey {uint64_t model;uint32_t view;bool transition;bool operator==(const PlainKey&)const=default;};
        struct PlainHash {size_t operator()(const PlainKey& k)const noexcept{uint64_t h=k.model*0x9e3779b97f4a7c15ull^(uint64_t(k.view)<<1|k.transition);h^=h>>32;h*=0xbf58476d1ce4e5b9ull;return size_t(h^h>>29);}};
        // Selection runs over contiguous ranges of the prepared selections. Each
        // range assigns batches in its own first-selection order; merging ranges
        // in order reproduces the serial batch order, row order and outputs.
        struct Chunk {
            std::unordered_map<PlainKey,uint32_t,PlainHash> plain_index;std::unordered_map<BatchKey,uint32_t,BatchHash> batch_index;
            std::vector<Batch> batches;std::vector<BatchKey> batch_order;std::vector<TransparentRequest> transparent;
            std::vector<std::pair<std::pair<uint32_t,uint32_t>,std::pair<uint64_t,uint64_t>>> spots;std::vector<Fallback> fallbacks;std::vector<uint32_t> visible;
        };
        auto select_batch=[&](Chunk& out,const Actor& actor,uint64_t model_handle,const View& view,float state,bool transition){
            auto& model=models.get(model_handle);std::vector<uint32_t> hidden_materials;
            if(model.parts.empty())return;
            if(std::any_of(actor.appearance.begin(),actor.appearance.end(),[](const Appearance& a){return !a.visible;})){
                for(auto& part:model.parts){auto material=appearance(actor,part.material);if(material&&!material->visible)hidden_materials.push_back(part.material);}
                std::sort(hidden_materials.begin(),hidden_materials.end());hidden_materials.erase(std::unique(hidden_materials.begin(),hidden_materials.end()),hidden_materials.end());
            }
            // Keys without hidden materials (nearly all) use a plain-key map;
            // both maps assign batch indices in first-selection order.
            uint32_t index;
            if(hidden_materials.empty()){auto [it,added]=out.plain_index.try_emplace(PlainKey{model_handle,view.id,transition},uint32_t(out.batches.size()));index=it->second;if(added){out.batches.push_back({&model,&view,{}});out.batch_order.push_back({view.id,model_handle,transition,{}});}}
            else{auto [it,added]=out.batch_index.try_emplace({view.id,model_handle,transition,std::move(hidden_materials)},uint32_t(out.batches.size()));index=it->second;if(added){out.batches.push_back({&model,&view,{}});out.batch_order.push_back(it->first);}}
            out.batches[index].rows.push_back({&actor,state});
        };
        std::unordered_map<PlainKey,uint32_t,PlainHash> plain_index;
        std::unordered_map<BatchKey,uint32_t,BatchHash> batch_index;
        std::vector<Batch> batches;std::vector<BatchKey> batch_order;
        // One scope for the whole selection; a per-call clock pair inflated emission.
        {CpuScope selection_scope(profiling,profile.emit_us);
        uint32_t total_selected=uint32_t(selected.size()),count=jobs&&parallel_selection&&total_selected>=512?std::min(jobs->worker_count()+1,(total_selected+255)/256):1u;
        std::vector<Chunk> chunks(count);chunks[0].plain_index.reserve(retained_batches.size()+64);
        auto run_chunk=[&](uint32_t c){auto& out=chunks[c];
            for(uint32_t s=uint32_t(uint64_t(total_selected)*c/count),end=uint32_t(uint64_t(total_selected)*(c+1)/count);s<end;++s){auto& selection=selected[s];
            auto& actor=actors.get(selection.handle);auto& family=families.get(actor.family);uint64_t view_mask=selection.views & ~(actor.group?hidden_views(actor.group):0);bool main=false;for(auto& view:frame.views)if(view.kind==Kind::Colour&&(view_mask&(uint64_t(1)<<view.id)))main=true;
            bool missing_model=false;for(auto& view:frame.views)if(view_mask&(uint64_t(1)<<view.id)){int lod=lod_for(family,view,selection,main);if(!model_ready(family.levels[lod]))missing_model=true;if(view.kind==Kind::Colour&&selection.next.previous>=0){int old=std::min(int(family.distances.size()),std::max(view.lod.minimum,selection.next.previous+(main?view.lod.visible_shift:view.lod.shift)));if(!model_ready(family.levels[old]))missing_model=true;}}
            if(missing_model){out.fallbacks.push_back({selection.handle,actor.owner,actor.lod});continue;}
            for(auto& view:frame.views)if(view_mask&(uint64_t(1)<<view.id)){
                int lod=lod_for(family,view,selection,main);bool transition=view.kind==Kind::Colour&&selection.next.previous>=0&&selection.next.transition>0;
                select_batch(out,actor,family.levels[lod],view,transition?1-selection.next.transition:0,transition);
                if(view.id==0&&view.kind==Kind::Colour&&models.get(family.levels[lod]).glass)out.transparent.push_back({selection.handle,actor.owner,uint32_t(lod),transition?3-selection.next.transition:0});
                if(view.id>=15&&view.only_static)out.spots.push_back({{view.id,actor.owner},{family.levels[lod],actor.depth_epoch}});
                if(transition){int old=std::min(int(family.distances.size()),std::max(view.lod.minimum,selection.next.previous+(main?view.lod.visible_shift:view.lod.shift)));select_batch(out,actor,family.levels[old],view,2+selection.next.transition,true);if(view.id==0&&models.get(family.levels[old]).glass)out.transparent.push_back({selection.handle,actor.owner,uint32_t(old),selection.next.transition});}
            }
            // The game-facing visibility set follows the managed contract:
            // main frustum candidates are reported before occlusion rejection.
            if(selection.main_candidate)out.visible.push_back(actor.owner);
            actor.lod=selection.next;actor.last_camera=selection.camera;actor.have_camera=true;
            }
        };
        {CpuScope select_scope(profiling,profile.select_us);if(count>1)jobs->run(count,run_chunk);else run_chunk(0);}
        CpuScope merge_scope(profiling,profile.merge_us);
        // The first range's batches become the merged set; later ranges append.
        plain_index=std::move(chunks[0].plain_index);batch_index=std::move(chunks[0].batch_index);batches=std::move(chunks[0].batches);batch_order=std::move(chunks[0].batch_order);
        for(uint32_t c=0;c<count;++c){auto& chunk=chunks[c];
            fallbacks.insert(fallbacks.end(),chunk.fallbacks.begin(),chunk.fallbacks.end());transparent.insert(transparent.end(),chunk.transparent.begin(),chunk.transparent.end());
            visible_owners.insert(visible_owners.end(),chunk.visible.begin(),chunk.visible.end());for(auto& [key,value]:chunk.spots)next_spots[key]=value;
            if(c)for(uint32_t i=0;i<chunk.batches.size();++i){auto& key=chunk.batch_order[i];auto& local=chunk.batches[i];uint32_t index;bool added;
                if(std::get<3>(key).empty()){auto [it,inserted]=plain_index.try_emplace(PlainKey{std::get<1>(key),std::get<0>(key),std::get<2>(key)},uint32_t(batches.size()));index=it->second;added=inserted;}
                else{auto [it,inserted]=batch_index.try_emplace(key,uint32_t(batches.size()));index=it->second;added=inserted;}
                if(added){batches.push_back(std::move(local));batch_order.push_back(std::move(key));}
                else batches[index].rows.insert(batches[index].rows.end(),local.rows.begin(),local.rows.end());
            }
        }}
        spot_updates=0;
        {CpuScope spots(profiling,profile.spots_us);
        for(auto& [key,value]:next_spots){auto previous=spot_signature.find(key);if(previous==spot_signature.end()||previous->second!=value)spot_updates|=uint64_t(1)<<key.first;}
        for(auto& [key,value]:spot_signature)if(!next_spots.count(key))spot_updates|=uint64_t(1)<<key.first;
        spot_signature=std::move(next_spots);}
        pending=false;
        // Selection results above are consumed by the caller right away; groups,
        // streams and retained rows are read only by recording.
        auto pack=[this,batches=std::move(batches),batch_order=std::move(batch_order)]()mutable{
        std::unordered_map<BatchKey,RetainedBatch,BatchHash> next_retained;next_retained.reserve(batch_order.size());
        {
            CpuScope emission(profiling,profile.emit_us);CpuScope packing_scope(profiling,profile.pack_us);
            // Row bookkeeping and packing touch only one batch and its own
            // retained entry, so batches prepare in parallel. Stream indices,
            // groups and the next retained map are assembled in batch order.
            struct Packed {RetainedBatch stored;uint64_t rows{},bytes{};};
            auto& ordered=batches;
            std::vector<Packed> prepared(batch_order.size());
            auto prepare_batch=[&](uint32_t index){auto& batch_key=batch_order[index];auto& batch=ordered[index];
                auto& view=*batch.view;auto& rows=batch.rows;auto& hidden_materials=std::get<3>(batch_key);
                uint32_t stride=view.kind==Kind::Depth?48u:64u;
                auto pack_row=[&](uint8_t* destination,size_t i,uint32_t material){
                    auto& actor=*rows[i].actor;ColourInstance packed{};packed.transform=transform(actor,frame.selection.camera);
                    if(view.kind!=Kind::Depth){std::copy_n(actor.key_colour,3,packed.key);packed.key[3]=half(rows[i].state);const uint16_t defaults[]{0x3c00,0x3c00,0x3c00,0x3c00};auto override=appearance(actor,material);std::copy_n(override?override->colour:defaults,4,packed.colour);}
                    std::memcpy(destination,&packed,stride);
                };
                std::vector<uint32_t> activated;
                auto material_active=[&](uint32_t material){return std::any_of(rows.begin(),rows.end(),[&](const Row& row){return std::any_of(row.actor->appearance.begin(),row.actor->appearance.end(),[&](const Appearance& item){return item.material==material;});});};
                if(view.kind!=Kind::Depth)for(auto& part:batch.model->parts)if(!std::binary_search(hidden_materials.begin(),hidden_materials.end(),part.material)&&(view.kind!=Kind::Forward||part.forward)&&material_active(part.material)&&std::find(activated.begin(),activated.end(),part.material)==activated.end())activated.push_back(part.material);
                auto found=retained_batches.find(batch_key);
                // Unchanged layouts compare and update the retained rows in place.
                bool same_layout=found!=retained_batches.end()&&found->second.owners.size()==rows.size()&&found->second.activated==activated&&found->second.base&&found->second.base->instances.size()==rows.size()*stride&&found->second.materials.size()==activated.size();
                if(same_layout)for(size_t i=0;i<rows.size();++i)if(found->second.owners[i]!=rows[i].actor->owner){same_layout=false;break;}
                if(same_layout)for(uint32_t material:activated){auto it=found->second.materials.find(material);if(it==found->second.materials.end()||!it->second||it->second->instances.size()!=rows.size()*stride)same_layout=false;}
                auto& stored=prepared[index].stored;
                if(same_layout){
                    bool same_camera=found->second.camera.x==frame.selection.camera.x&&found->second.camera.y==frame.selection.camera.y&&found->second.camera.z==frame.selection.camera.z;
                    stored=std::move(found->second);
                    bool same_rows=stored.epochs.size()==rows.size()&&stored.states.size()==rows.size();
                    if(same_rows)for(size_t i=0;i<rows.size();++i)if(stored.epochs[i]!=rows[i].actor->depth_epoch||stored.states[i]!=rows[i].state){same_rows=false;break;}
                    if(!same_rows||!same_camera){
                        auto rewrite=[&](InstanceStream& stream,uint32_t material){
                            for(size_t i=0;i<rows.size();++i){
                                bool dirty=rows[i].actor->depth_epoch!=stored.epochs[i]||(view.kind!=Kind::Depth&&rows[i].state!=stored.states[i]);
                                if(dirty){pack_row(stream.instances.data()+i*stride,i,material);++prepared[index].rows;prepared[index].bytes+=stride;}
                                else if(!same_camera){auto relative=transform(*rows[i].actor,frame.selection.camera);std::memcpy(stream.instances.data()+i*stride,&relative,sizeof(Transform));}
                            }
                        };
                        rewrite(*stored.base,UINT32_MAX);for(uint32_t material:activated)rewrite(*stored.materials[material],material);
                    }
                    stored.epochs.resize(rows.size());stored.states.resize(rows.size());for(size_t i=0;i<rows.size();++i){stored.epochs[i]=rows[i].actor->depth_epoch;stored.states[i]=rows[i].state;}stored.camera=frame.selection.camera;
                }else{
                    std::vector<uint32_t> owners;std::vector<uint64_t> epochs;std::vector<float> states;
                    owners.reserve(rows.size());epochs.reserve(rows.size());states.reserve(rows.size());
                    for(auto& row:rows){owners.push_back(row.actor->owner);epochs.push_back(row.actor->depth_epoch);states.push_back(row.state);}
                    auto make_stream=[&](uint32_t material){
                        auto stream=std::make_shared<InstanceStream>();stream->instances.resize(rows.size()*stride);stream->owners=owners;
                        for(size_t i=0;i<rows.size();++i)pack_row(stream->instances.data()+i*stride,i,material);
                        prepared[index].rows+=rows.size();prepared[index].bytes+=stream->instances.size();return stream;
                    };
                    stored.base=make_stream(UINT32_MAX);for(uint32_t material:activated)stored.materials.emplace(material,make_stream(material));
                    stored.owners=std::move(owners);stored.activated=std::move(activated);stored.epochs=std::move(epochs);stored.states=std::move(states);stored.camera=frame.selection.camera;
                }
            };
            if(jobs&&batch_order.size()>1)jobs->run(uint32_t(batch_order.size()),prepare_batch);else for(uint32_t index=0;index<batch_order.size();++index)prepare_batch(index);
            for(uint32_t index=0;index<batch_order.size();++index){auto& batch_key=batch_order[index];auto& batch=ordered[index];auto& stored=prepared[index].stored;
                auto& view=*batch.view;auto& rows=batch.rows;auto& hidden_materials=std::get<3>(batch_key);
                if(profiling){profile.emitted_rows+=prepared[index].rows;profile.stream_bytes+=prepared[index].bytes;}
                auto publish_stream=[&](const std::shared_ptr<InstanceStream>& stream){stream->index=uint32_t(streams.size());stream->identities.clear();stream->identities.reserve(rows.size());for(const auto& row:rows)stream->identities.push_back(row.actor->history_identity);streams.push_back(stream);};
                publish_stream(stored.base);for(auto& item:stored.materials)publish_stream(item.second);
                auto stream_for=[&](uint32_t material){if(view.kind==Kind::Depth)return stored.base;auto it=stored.materials.find(material);return it==stored.materials.end()?stored.base:it->second;};
                auto append=[&](const Part& part){GroupKey key{view.id,part.shader,part.first,part.count,part.mesh,part.colour,part.normal,part.extensions,part.alpha,part.masked,part.double_sided};
                    if(view.kind==Kind::Depth){key.colour=key.normal=key.extensions=0;if(!part.masked)key.alpha=0;key.shader=part.masked?1:0;key.double_sided=false;}
                    groups.push_back({key,view.kind,stream_for(part.material),view.kind==Kind::Colour?uint32_t(&part-batch.model->parts.data()):0u});
                };
                if(view.kind!=Kind::Depth){
                    for(auto& part:batch.model->parts)if(!std::binary_search(hidden_materials.begin(),hidden_materials.end(),part.material)&&(view.kind!=Kind::Forward||part.forward))append(part);
                }else for(auto& segment:batch.model->depth){
                    bool split=std::any_of(segment.pieces.begin(),segment.pieces.end(),[&](const DepthPiece& piece){return std::binary_search(hidden_materials.begin(),hidden_materials.end(),piece.material);});
                    if(!split){append(segment.range);continue;}
                    Part visible=segment.range;bool have=false;
                    auto flush=[&](){if(have){append(visible);have=false;}};
                    for(auto& piece:segment.pieces){
                        if(std::binary_search(hidden_materials.begin(),hidden_materials.end(),piece.material)){flush();continue;}
                        if(have&&visible.first+visible.count==piece.first)visible.count+=piece.count;
                        else{flush();visible=segment.range;visible.first=piece.first;visible.count=piece.count;have=true;}
                    }flush();
                }
                next_retained.emplace(batch_key,std::move(stored));
            }
        }
        retained_batches.swap(next_retained);
        if(profiling)profile.groups+=groups.size();
        };
        if(packer){packing=true;packer->start(std::move(pack));}else pack();
    }
    void cancel(){settle();if(!pending)throw std::runtime_error("pass cancel without preparation");pending=false;selected.clear();groups.clear();streams.clear();transparent.clear();}
private:
    // Declared last: destroyed first, joining any packing that still reads members.
    std::unique_ptr<Background> packer;mutable bool packing{},preparing{};
};
}
