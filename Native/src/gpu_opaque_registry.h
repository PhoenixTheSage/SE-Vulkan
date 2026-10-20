#pragma once
#include "native_pass_scene.h"
#include "se_vulkan.h"
#include "se_gpu_opaque_reference.h"
#include "gpu_appearance_arena.h"
#include "old_part_order.h"
#include <set>

// Committed source data only. This registry never selects a view/LOD, generates
// an instance plane, or expands a WorldDraw packet. GPU publication consumes its
// immutable topology and changed actor/source inputs.
namespace gpu_scene {
struct Geometry {
    uint64_t mesh{},generation{},vertex_page{},index_page{};
    uint32_t first{},count{};int32_t vertex_offset{};
    bool operator==(const Geometry&)const=default;
};
struct Material {
    uint32_t identity{},colour_pipeline{},depth_pipeline{},forward_pipeline{},flags{};
    std::array<uint64_t,4> textures{};
};
struct Part {Geometry geometry;Material material;bool colour=true,depth=true,forward=true,masked{};SevkOldPartOrder old_order{};};
struct DepthPiece {uint32_t first,count,material;};
struct DepthSegment {Part range;std::vector<DepthPiece> pieces;};
struct Model {std::vector<Part> parts;bool old{};std::vector<DepthSegment> depth;};
struct Source {std::vector<uint8_t> bytes;bool cube{};};
struct Family {std::vector<float> thresholds;std::vector<std::shared_ptr<const Model>> levels;bool old{};};
// A texture-only family change leaves every geometric and pipeline contract
// intact, including depth segment construction and coverage identities.
inline bool compatible_bindings(const Family& a,const Family& b){
    if(a.old!=b.old||a.thresholds!=b.thresholds||a.levels.size()!=b.levels.size())return false;
    for(size_t lod=0;lod<a.levels.size();lod++){auto& x=*a.levels[lod];auto& y=*b.levels[lod];if(x.old!=y.old||x.parts.size()!=y.parts.size())return false;
        for(size_t p=0;p<x.parts.size();p++){auto& m=x.parts[p];auto& n=y.parts[p];auto& s=m.material;auto& t=n.material;
            if(m.geometry!=n.geometry||m.colour!=n.colour||m.depth!=n.depth||m.forward!=n.forward||m.masked!=n.masked||!old_order_equal(m.old_order,n.old_order)||s.identity!=t.identity||s.colour_pipeline!=t.colour_pipeline||s.depth_pipeline!=t.depth_pipeline||s.forward_pipeline!=t.forward_pipeline||s.flags!=t.flags)return false;}}
    return true;
}
inline std::vector<std::array<uint64_t,4>> binding_textures(const Family& family){
    std::vector<std::array<uint64_t,4>> output;
    for(auto& level:family.levels){for(auto& p:level->parts)if(p.colour||p.forward)output.push_back(p.material.textures);
        for(auto& segment:level->depth)for(size_t first=0;first<segment.pieces.size();first+=32){size_t count=std::min(size_t(32),segment.pieces.size()-first);output.push_back(segment.range.material.textures);if(count>1)for(size_t p=0;p<count;p++)output.push_back(segment.range.material.textures);}}
    return output;
}
struct Appearance {uint32_t material{},visible=1;std::array<uint16_t,4> colour{0x3c00,0x3c00,0x3c00,0x3c00};std::array<float,4> old_colour{1,1,1,1};bool operator==(const Appearance&)const=default;};
struct Actor {
    SevkOpaqueActor input{};uint64_t family{},source{},group{};
    uint32_t source_first{},source_count=1;
    std::array<uint8_t,128> object{};std::vector<Appearance> appearances;
    SevkOpaqueSelector selector{};bool replace_appearance=true;
};
struct ResidentActor {
    uint64_t handle{},revision{};Actor input;
    std::shared_ptr<const Family> family;
    std::shared_ptr<const Source> source;
    uint64_t appearance_revision{};
};
struct BatchKey {
    uint64_t vertices{},indices{};uint32_t colour{},depth{},forward{},flags{};bool old{};
    uint32_t group{},part{};
    SevkOldPartOrder old_order{};
    auto operator<=>(const BatchKey& b)const{return std::tuple_cat(std::tie(group,part,vertices,indices,colour,depth,forward,flags,old),old_order_tuple(old_order))<=>std::tuple_cat(std::tie(b.group,b.part,b.vertices,b.indices,b.colour,b.depth,b.forward,b.flags,b.old),old_order_tuple(b.old_order));}
};
inline BatchKey batch_key(const Part& p,bool old,uint32_t family,uint32_t part){return {p.geometry.vertex_page,p.geometry.index_page,p.material.colour_pipeline,p.material.depth_pipeline,p.material.forward_pipeline,p.material.flags,old,old?0u:family+1,old?0u:part,old?p.old_order:SevkOldPartOrder{}};}
struct Snapshot {
    uint64_t topology{},revision{};
    std::vector<ResidentActor> actors;
    std::vector<std::shared_ptr<const Family>> families;
    std::vector<std::shared_ptr<const Source>> sources;
    std::vector<BatchKey> batches;
};
struct Upload {
    Snapshot source;
    std::vector<SevkOpaqueActor> actors;std::vector<SevkOpaqueFamily> families;
    std::vector<SevkOpaquePart> parts;std::vector<SevkOpaqueStockBatch> batches;
    std::vector<uint64_t> part_meshes,textures,handles;
    std::vector<uint8_t> sources,objects;std::vector<SevkOpaqueSelector> selectors;
    std::vector<SevkOpaqueAppearance> appearances;std::vector<SevkOpaqueAppearanceUpdate> appearance_updates;
};
class Registry {
    native_pass::Handles<std::shared_ptr<const Model>> models;
    native_pass::Handles<std::shared_ptr<const Family>> families;
    native_pass::Handles<std::shared_ptr<const Source>> sources;
    native_pass::Handles<ResidentActor> actors;
    std::unordered_map<uint32_t,uint64_t> owners;
    std::set<uint64_t> dirty;
    std::array<uint64_t,5> topology_counts_{};
    uint64_t topology_=1,revision_=1;uint32_t selector_generation{};
    ResidentActor resolve(Actor actor,const ResidentActor* previous=nullptr){
        auto family=previous&&previous->input.family==actor.family?previous->family:families.get(actor.family);
        if(!actor.input.owner||!actor.input.generation||actor.input.family||actor.input.source_first)
            throw std::runtime_error("resident actor supplies unresolved GPU table indices");
        auto& pose=actor.input;if((pose.flags&~255u)||pose.appearance_count||!(pose.scale>0)||!std::isfinite(pose.scale))throw std::runtime_error("invalid resident pose contract");
        for(uint32_t k=0;k<3;k++)if(!std::isfinite(pose.translation[k])||std::abs(pose.translation[k])>1e12||!std::isfinite(pose.minimum[k])||std::abs(pose.minimum[k])>1e12||!std::isfinite(pose.maximum[k])||std::abs(pose.maximum[k])>1e12||pose.minimum[k]>pose.maximum[k]||!std::isfinite(pose.local_minimum[k])||!std::isfinite(pose.local_maximum[k])||pose.local_minimum[k]>pose.local_maximum[k])throw std::runtime_error("invalid resident pose bounds");
        for(float f:pose.basis)if(!std::isfinite(f))throw std::runtime_error("invalid resident basis");for(double d:pose.inverse)if(!std::isfinite(d))throw std::runtime_error("invalid resident parent inverse");for(float f:pose.colour)if(!std::isfinite(f))throw std::runtime_error("invalid resident colour");
        if(bool(actor.input.flags&2)!=family->old)throw std::runtime_error("resident family/input contract differs");
        std::shared_ptr<const Source> source;
        if(family->old){source=previous&&previous->input.source==actor.source?previous->source:sources.get(actor.source);if(!actor.source_count||uint64_t(actor.source_first)+actor.source_count>source->bytes.size()/64||bool(actor.input.flags&32)!=source->cube)
            throw std::runtime_error("resident OLD source range/contract differs");
        }else if(actor.source||actor.source_first||actor.source_count!=1)throw std::runtime_error("resident NEW actor retains a source stream");
        if(!actor.replace_appearance){if(!previous||previous->family!=family)throw std::runtime_error("appearance preservation requires the same resident family");actor.appearances=previous->input.appearances;actor.replace_appearance=true;}
        if(actor.appearances.size()>gpu_opaque::appearance_limit)throw std::runtime_error("resident appearance capacity exceeded");std::set<uint32_t> materials;for(auto& a:actor.appearances){if(a.visible>1||!materials.insert(a.material).second)throw std::runtime_error("resident appearance is ambiguous");for(float f:a.old_colour)if(!std::isfinite(f))throw std::runtime_error("invalid OLD resident material colour");}
        return {0,0,std::move(actor),std::move(family),std::move(source)};
    }
public:
    uint64_t topology()const{return topology_;}uint64_t revision()const{return revision_;}
    const std::array<uint64_t,5>& topology_counts()const{return topology_counts_;}
    uint64_t register_model(Model model){
        if(model.parts.empty())throw std::runtime_error("resident model has no geometry");
        for(auto& p:model.parts){validate_old_order(p.old_order);if(!model.old&&p.old_order.valid)throw std::runtime_error("NEW model supplied OLD identity");}
        for(auto& p:model.parts)if(!p.geometry.mesh||!p.geometry.generation||!p.geometry.vertex_page||!p.geometry.index_page||!p.geometry.count||p.geometry.count%3||p.geometry.first%3||uint64_t(p.geometry.first)+p.geometry.count>UINT32_MAX||p.geometry.vertex_offset<0||(!p.colour&&!p.depth&&!p.forward))
            throw std::runtime_error("resident model has invalid shared geometry");
        model.depth.clear();bool merge=false;
        for(auto& part:model.parts){if(!part.depth){merge=false;continue;}
            if(merge&&!part.masked&&model.depth.back().range.geometry.mesh==part.geometry.mesh&&model.depth.back().range.geometry.generation==part.geometry.generation&&model.depth.back().range.geometry.first+model.depth.back().range.geometry.count==part.geometry.first&&model.depth.back().range.material.depth_pipeline==part.material.depth_pipeline){auto& last=model.depth.back();last.range.geometry.count+=part.geometry.count;last.pieces.push_back({part.geometry.first,part.geometry.count,part.material.identity});}
            else model.depth.push_back({part,{{part.geometry.first,part.geometry.count,part.material.identity}}});merge=!part.masked;
        }
        return models.add(std::make_shared<const Model>(std::move(model)));
    }
    uint64_t register_source(Source source){
        if(source.bytes.empty()||source.bytes.size()%64)throw std::runtime_error("resident source must contain complete stock instances");
        return sources.add(std::make_shared<const Source>(std::move(source)));
    }
    uint64_t register_family(std::vector<float> thresholds,std::span<const uint64_t> levels){
        if(thresholds.size()>16||levels.size()!=thresholds.size()+1||!std::is_sorted(thresholds.begin(),thresholds.end()))throw std::runtime_error("resident family requires every authored LOD");
        for(auto d:thresholds)if(!std::isfinite(d)||d<0)throw std::runtime_error("resident family threshold is invalid");
        Family family;family.thresholds=std::move(thresholds);
        for(auto h:levels){auto model=models.get(h);if(!family.levels.empty()&&model->old!=family.old)throw std::runtime_error("resident family mixes stock pipelines");family.old=model->old;family.levels.push_back(std::move(model));}
        return families.add(std::make_shared<const Family>(std::move(family)));
    }
    uint64_t register_actor(Actor actor){
        auto resolved=resolve(std::move(actor));uint32_t owner=resolved.input.input.owner;if(owners.count(owner))throw std::runtime_error("resident owner already admitted");
        if(selector_generation==UINT32_MAX)throw std::runtime_error("resident selector generation exhausted");resolved.input.input.generation=++selector_generation;resolved.input.selector.generation=selector_generation;
        auto h=actors.add(std::move(resolved));auto& stored=actors.get(h);stored.handle=h;stored.revision=++revision_;stored.appearance_revision=stored.revision;owners.emplace(owner,h);dirty.insert(h);++topology_;++topology_counts_[0];return h;
    }
    void update(std::span<const std::pair<uint64_t,Actor>> updates){
        std::vector<std::pair<uint64_t,ResidentActor>> staged;std::set<uint64_t> unique;
        for(auto& [h,a]:updates){auto& previous=actors.get(h);if(!unique.insert(h).second||a.input.owner!=previous.input.input.owner)throw std::runtime_error("resident batch contains duplicate/reassigned owner");
            auto next=resolve(a,&previous);next.handle=h;
            // Selector is GPU-owned. Pose/material commits cannot import a stale
            // managed selector; handoff is a separate completed-GPU operation.
            next.input.selector=previous.input.selector;next.input.input.generation=previous.input.input.generation;staged.emplace_back(h,std::move(next));}
        uint32_t resets=0;for(auto& [h,next]:staged){auto& previous=actors.get(h);if(previous.family->old!=next.family->old||previous.family->thresholds!=next.family->thresholds)++resets;}if(resets>UINT32_MAX-selector_generation)throw std::runtime_error("resident selector generation exhausted");
        for(auto& [h,next]:staged){auto& previous=actors.get(h);if(previous.family!=next.family)++topology_counts_[2];if(previous.source!=next.source)++topology_counts_[3];if(previous.input.group!=next.input.group)++topology_counts_[4];if(previous.family!=next.family||previous.source!=next.source||previous.input.group!=next.input.group)++topology_;if(previous.family->old!=next.family->old||previous.family->thresholds!=next.family->thresholds)next.input.input.generation=++selector_generation;next.revision=++revision_;next.appearance_revision=next.input.appearances==previous.input.appearances?previous.appearance_revision:next.revision;previous=std::move(next);dirty.insert(h);}
    }
    void remove_actor(uint64_t handle){auto& actor=actors.get(handle);owners.erase(actor.input.input.owner);actors.remove(handle);dirty.erase(handle);++topology_;++revision_;++topology_counts_[1];}
    void retire_model(uint64_t handle){models.remove(handle);}
    void retire_family(uint64_t handle){families.remove(handle);}
    void retire_source(uint64_t handle){sources.remove(handle);}
    Snapshot snapshot(){
        Snapshot output;output.topology=topology_;output.revision=revision_;std::set<std::shared_ptr<const Family>> fs;std::set<std::shared_ptr<const Source>> ss;std::set<BatchKey> batches;
        actors.each([&](uint64_t,ResidentActor& actor){output.actors.push_back(actor);if(fs.insert(actor.family).second)output.families.push_back(actor.family);if(actor.source)ss.insert(actor.source);});
        for(uint32_t f=0;f<output.families.size();++f){uint32_t ordinal=0;for(auto& model:output.families[f]->levels)for(auto& p:model->parts)batches.insert(batch_key(p,model->old,f,ordinal++));}
        output.sources.assign(ss.begin(),ss.end());output.batches.assign(batches.begin(),batches.end());return output;
    }
    Upload upload(){
        Upload output;output.source=snapshot();output.textures.push_back(0);
        std::map<uint64_t,uint32_t> textures{{0,0}};std::map<std::shared_ptr<const Family>,uint32_t> family_indices;
        std::map<std::shared_ptr<const Source>,uint32_t> source_indices;std::map<BatchKey,uint32_t> batch_indices;
        for(uint32_t i=0;i<output.source.batches.size();i++){auto& b=output.source.batches[i];batch_indices[b]=i;output.batches.push_back({0,b.old?1u:0u,b.colour,b.depth,b.forward,b.flags,0});}
        auto texture=[&](uint64_t id){auto [it,added]=textures.emplace(id,uint32_t(output.textures.size()));if(added)output.textures.push_back(id);return it->second;};
        auto append=[&](const Part& part,uint32_t family,uint32_t lod,bool old,uint32_t flags,std::span<const DepthPiece> coverage,uint32_t ordinal){
            auto key=batch_key(part,old,family,ordinal);uint32_t batch=batch_indices.at(key);if(!output.batches[batch].mesh)output.batches[batch].mesh=part.geometry.mesh;
            SevkOpaquePart p{};p.family=family;p.lod=lod;p.first_index=part.geometry.first;p.index_count=part.geometry.count;p.material=part.material.identity;p.flags=flags;p.generation=1;p.reserved=batch;
            for(uint32_t t=0;t<4;t++)p.textures[t]=texture(part.material.textures[t]);p.coverage_count=uint32_t(coverage.size());for(uint32_t t=0;t<coverage.size();t++)p.coverage_materials[t]=coverage[t].material;
            output.parts.push_back(p);output.part_meshes.push_back(part.geometry.mesh);
        };
        for(uint32_t f=0;f<output.source.families.size();f++){auto& family=output.source.families[f];family_indices[family]=f;SevkOpaqueFamily input{};input.generation=1;input.first_part=uint32_t(output.parts.size());input.threshold_count=uint32_t(family->thresholds.size());std::copy(family->thresholds.begin(),family->thresholds.end(),input.thresholds);
            uint32_t ordinal=0;for(uint32_t lod=0;lod<family->levels.size();lod++){auto& model=*family->levels[lod];uint32_t first_ordinal=ordinal;for(auto& part:model.parts){if(part.colour||part.forward)append(part,f,lod,model.old,2|(part.forward?0u:16u)|(part.colour?0u:32u),{},ordinal);++ordinal;}
                for(auto& segment:model.depth)for(size_t first=0;first<segment.pieces.size();first+=32){auto pieces=std::span(segment.pieces).subspan(first,std::min(size_t(32),segment.pieces.size()-first));Part range=segment.range;range.geometry.first=pieces.front().first;range.geometry.count=pieces.back().first+pieces.back().count-range.geometry.first;
                    auto source=std::find_if(model.parts.begin(),model.parts.end(),[&](const Part& p){return p.geometry.mesh==segment.range.geometry.mesh&&p.geometry.first==segment.range.geometry.first;});if(source==model.parts.end())throw std::runtime_error("depth segment lost its source part");uint32_t depth_ordinal=first_ordinal+uint32_t(source-model.parts.begin());
                    append(range,f,lod,model.old,1|(pieces.size()>1?4u:0u),pieces.size()>1?pieces:std::span<const DepthPiece>{},depth_ordinal);
                    if(pieces.size()>1)for(auto& piece:pieces){Part split=range;split.geometry.first=piece.first;split.geometry.count=piece.count;split.material.identity=piece.material;append(split,f,lod,model.old,1|8,pieces,depth_ordinal);}
                }
            }input.part_count=uint32_t(output.parts.size())-input.first_part;output.families.push_back(input);
        }
        for(auto& source:output.source.sources){source_indices[source]=uint32_t(output.sources.size()/64);output.sources.insert(output.sources.end(),source->bytes.begin(),source->bytes.end());}
        for(uint32_t i=0;i<output.source.actors.size();i++){auto& resident=output.source.actors[i];auto a=resident.input.input;a.family=family_indices.at(resident.family);a.source_first=resident.source?source_indices.at(resident.source)+resident.input.source_first:0;a.source_count=resident.input.source_count;a.appearance_count=0;
            output.actors.push_back(a);output.handles.push_back(resident.handle);output.selectors.push_back(resident.input.selector);output.objects.insert(output.objects.end(),resident.input.object.begin(),resident.input.object.end());
            SevkOpaqueAppearanceUpdate update{i,uint32_t(output.appearances.size()),uint32_t(resident.input.appearances.size()),0};output.appearance_updates.push_back(update);
            for(auto& appearance:resident.input.appearances){SevkOpaqueAppearance entry{};entry.material=appearance.material;entry.visible=appearance.visible;entry.colour[0]=uint32_t(appearance.colour[0])|(uint32_t(appearance.colour[1])<<16);entry.colour[1]=uint32_t(appearance.colour[2])|(uint32_t(appearance.colour[3])<<16);std::copy(appearance.old_colour.begin(),appearance.old_colour.end(),entry.old_colour);output.appearances.push_back(entry);}
        }return output;
    }
    std::vector<ResidentActor> changed(uint64_t after){std::vector<ResidentActor> output;for(auto h:dirty){auto& actor=actors.get(h);if(actor.revision>after)output.push_back(actor);}return output;}
    void acknowledge(uint64_t through){for(auto it=dirty.begin();it!=dirty.end();)if(actors.get(*it).revision<=through)it=dirty.erase(it);else ++it;}
    const ResidentActor& actor(uint64_t handle){return actors.get(handle);}
    bool contains(uint64_t handle)const{return actors.valid(handle);}
    bool family_in_use(const std::shared_ptr<const Family>& family){bool used=false;actors.each([&](uint64_t,ResidentActor& actor){used|=actor.family==family;});return used;}
    void selector_handoff(uint64_t handle,const SevkOpaqueSelector& selector){actors.get(handle).input.selector=selector;}
};
}
