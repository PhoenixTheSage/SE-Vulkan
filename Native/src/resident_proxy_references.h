#pragma once
#include "stock_proxy_lod.h"
#include "native_pass_view_jobs.h"
#include <span>
#include <vector>

namespace resident_proxy {
// View preparation retains source GPU instance ranges. It never creates a
// vertex/instance stream or expands a part into the colour packet bridge.
struct Input {
    uint64_t actor{},family{},stream{},views{};uint32_t owner{},first{},count{1};
    Lod prepared;std::span<const uint64_t> levels;
};
struct Reference {
    uint64_t actor{},model{},stream{};uint32_t owner{},first{},count{};float alpha{};
};
struct ViewReferences {uint32_t view{};std::vector<Reference> proxies;};
struct PreparedView {uint32_t id{};std::span<const uint32_t> actors;};
inline std::vector<ViewReferences> prepare_references(std::span<const Input> actors,std::span<const PreparedView> views,native_pass::ViewJobs& jobs){
    std::vector<ViewReferences> result(views.size());
    // The render owner has generation-validated models/streams and resolved
    // selectors and view occlusion. No resource request or actor mutation is
    // allowed between dispatch and this function's completion barrier.
    for(auto& actor:actors){auto valid=[&](int lod){return lod>=0&&uint32_t(lod)<actor.levels.size()&&actor.levels[lod];};if(!actor.actor||!actor.count||uint64_t(actor.first)+actor.count>UINT32_MAX||(!actor.stream&&(actor.first||actor.count!=1))||!valid(actor.prepared.current)||(actor.prepared.transition!=0&&!valid(actor.prepared.next)))throw std::runtime_error("unresolved resident proxy epoch");}
    uint64_t mask=0;for(auto& view:views){if(view.id>=64||(mask&(uint64_t(1)<<view.id)))throw std::runtime_error("duplicate resident proxy view");mask|=uint64_t(1)<<view.id;for(uint32_t index:view.actors)if(index>=actors.size())throw std::runtime_error("invalid prepared proxy selection");}
    jobs.run(uint32_t(views.size()),[&](uint32_t index){auto& output=result[index];output.view=views[index].id;uint64_t bit=uint64_t(1)<<output.view;
        for(uint32_t actor_index:views[index].actors){auto& actor=actors[actor_index];if(!(actor.views&bit))continue;bool transition=actor.prepared.transition!=0&&actor.levels[actor.prepared.next]!=actor.levels[actor.prepared.current];output.proxies.push_back({actor.actor,actor.levels[actor.prepared.current],actor.stream,actor.owner,actor.first,actor.count,transition?actor.prepared.transition:0});
            if(transition)output.proxies.push_back({actor.actor,actor.levels[actor.prepared.next],actor.stream,actor.owner,actor.first,actor.count,3-actor.prepared.transition});}
    });return result;
}
}
