#pragma once
#include "se_vulkan.h"
#include "se_gpu_opaque_reference.h"
#include <vector>
#include <span>
#include <cmath>
#include <algorithm>
#include <array>
#include <bit>

namespace gpu_opaque {
// Conservative recording admission, independent of GPU LOD/transition selection.
// Ignore appearance, projected-size and distance pruning: false positives cost
// commands, but must never lose geometry selected by the GPU.
template<class Actor> inline bool recording_member(const Actor& a,const SevkOpaqueView& v,uint32_t view){
    if(!(a.flags&1)||!(v.flags&1)||((v.flags&2)&&!(a.flags&16)))return false;
    if(a.hidden_views[view/32]&(1u<<(view%32)))return false;
    if(((v.flags&32)&&(a.flags&128))||(!(v.flags&34)&&(a.flags&64)))return false;
    if(((v.flags&8)&&!(a.flags&8))||((v.flags&16)&&(a.flags&8)))return false;
    if(v.flags&4)for(uint32_t p=0;p<6;p++){
        const double* plane=v.planes+p*4;double terms[4]{plane[3],0,0,0};
        for(uint32_t k=0;k<3;k++)terms[k+1]=plane[k]*(plane[k]>=0?a.minimum[k]:a.maximum[k]);
        double d=terms[1]+terms[2]+terms[3]+terms[0];
        double envelope=(std::abs(terms[0])+std::abs(terms[1])+std::abs(terms[2])+std::abs(terms[3]))*3.552713678800501e-15;
        if(d>envelope)return false;
    }
    return true;
}
using FamilyBatches=std::array<std::vector<uint32_t>,3>;
inline std::vector<FamilyBatches> recording_family_batches(std::span<const SevkOpaquePart> parts,uint32_t families){
    std::vector<FamilyBatches> output(families);
    for(auto& p:parts){auto& roles=output[p.family];if((p.flags&2)&&!(p.flags&32))roles[0].push_back(p.reserved);
        if(p.flags&1)roles[1].push_back(p.reserved);if((p.flags&2)&&!(p.flags&16))roles[2].push_back(p.reserved);}
    for(auto& f:output)for(auto& role:f){std::sort(role.begin(),role.end());role.erase(std::unique(role.begin(),role.end()),role.end());}
    return output;
}
// The view mask of each family over a range of actors; ranges OR together, so callers may split the actors.
inline void recording_family_views(std::span<const SevkOpaqueActor> actors,std::span<const SevkOpaqueView> views,std::vector<uint64_t>& family_views){
    for(auto& a:actors)for(uint32_t v=0;v<views.size();v++)
        if(!(family_views[a.family]&(uint64_t(1)<<v))&&recording_member(a,views[v],v))family_views[a.family]|=uint64_t(1)<<v;
}
// Recording-only cache. Actor versions belong to the immutable recording snapshot;
// appearance/pose commits may invalidate more than necessary, never less. Only
// fields read by recording_member participate in the view key (not LOD, camera
// projection, or the GPU's retained-spot redraw flags).
struct RecordingMembershipCache {
    // A renderer revision also covers LOD, appearance and object rows. Those
    // are GPU inputs, but do not change this conservative recording predicate.
    struct ActorKey {
        double minimum[3],maximum[3];uint32_t flags,hidden_views[2],family;
        explicit ActorKey(const SevkOpaqueActor& a):flags(a.flags&(1|8|16|64|128)),hidden_views{a.hidden_views[0],a.hidden_views[1]},family(a.family){
            std::copy_n(a.minimum,3,minimum);std::copy_n(a.maximum,3,maximum);
        }
        bool matches(const SevkOpaqueActor& a)const{
            return flags==(a.flags&(1|8|16|64|128))&&hidden_views[0]==a.hidden_views[0]&&hidden_views[1]==a.hidden_views[1]&&
                std::equal(std::begin(minimum),std::end(minimum),a.minimum)&&std::equal(std::begin(maximum),std::end(maximum),a.maximum);
        }
    };
    static_assert(sizeof(ActorKey)==64);
    std::vector<uint64_t> versions,masks;
    std::vector<ActorKey> actor_keys;
    std::vector<SevkOpaqueView> views;
    bool valid{};
    uint64_t changed_views(std::span<const SevkOpaqueView> current)const{
        uint64_t changed=0;
        for(uint32_t v=0;v<current.size();++v){
            const auto& b=current[v];bool same=valid&&v<views.size();
            if(same){const auto& a=views[v];same=(a.flags&63)==(b.flags&63);
                if(same&&(b.flags&4))same=std::equal(std::begin(a.planes),std::end(a.planes),std::begin(b.planes));}
            if(!same)changed|=uint64_t(1)<<v;
        }
        return changed;
    }
    bool unchanged(std::span<const uint64_t> current,std::span<const SevkOpaqueView> next)const{
        return valid&&views.size()==next.size()&&versions.size()==current.size()&&
            changed_views(next)==0&&std::equal(current.begin(),current.end(),versions.begin());
    }
    void resize(size_t count){if(masks.size()!=count){valid=false;masks.assign(count,0);versions.assign(count,0);actor_keys.assign(count,ActorKey(SevkOpaqueActor{}));}}
    // Disjoint actor ranges may run in parallel; each caller owns its family OR
    // reduction. Do not publish the new view key until every range has joined.
    uint64_t update(std::span<const SevkOpaqueActor> actors,std::span<const uint64_t> current,
        std::span<const SevkOpaqueView> next,uint64_t changed,uint32_t begin,uint32_t end,std::vector<uint64_t>& families,bool narrow_keys=true){
        const uint64_t all=next.size()==64?~uint64_t(0):(uint64_t(1)<<next.size())-1;
        uint64_t tests=0;
        for(uint32_t i=begin;i<end;++i){
            bool different=!valid||(versions[i]!=current[i]&&(!narrow_keys||!actor_keys[i].matches(actors[i])));
            // Revision changes own all actor-row updates. Keep predicate data
            // contiguous and read the larger GPU row only when it changes.
            if(different)actor_keys[i]=ActorKey(actors[i]);
            else if(versions[i]!=current[i])actor_keys[i].family=actors[i].family;
            uint64_t pending=different?all:changed;
            tests+=std::popcount(pending);
            auto mask=masks[i]&all;
            while(pending){uint32_t v=uint32_t(std::countr_zero(pending));pending&=pending-1;uint64_t bit=uint64_t(1)<<v;
                if(recording_member(actor_keys[i],next[v],v))mask|=bit;else mask&=~bit;}
            versions[i]=current[i];masks[i]=mask;families[actor_keys[i].family]|=mask;
        }
        return tests;
    }
    void finish(std::span<const SevkOpaqueView> next){views.assign(next.begin(),next.end());valid=true;}
};
inline std::vector<std::vector<uint32_t>> recording_batches(std::span<const uint64_t> family_views,
    std::span<const FamilyBatches> families,std::span<const SevkOpaqueView> views,uint32_t batches){
    std::vector<std::vector<uint32_t>> output(views.size());std::vector<uint8_t> admitted(batches);
    for(uint32_t v=0;v<views.size();v++){
        std::fill(admitted.begin(),admitted.end(),0);uint32_t role=(views[v].flags&2)?1:(views[v].flags&32)?2:0;
        for(uint32_t f=0;f<families.size();f++)if(family_views[f]&(uint64_t(1)<<v))for(uint32_t b:families[f][role])admitted[b]=1;
        for(uint32_t b=0;b<batches;b++)if(admitted[b])output[v].push_back(b);
    }
    return output;
}
inline std::vector<std::vector<uint32_t>> recording_batches(std::span<const SevkOpaqueActor> actors,
    std::span<const FamilyBatches> families,std::span<const SevkOpaqueView> views,uint32_t batches){
    std::vector<uint64_t> family_views(families.size());recording_family_views(actors,views,family_views);
    return recording_batches(std::span<const uint64_t>(family_views),families,views,batches);
}
// The family-to-batch topology is immutable for this cache's lifetime. Compare
// the reduced family population per view, not the actor revisions: changes to
// one light must not rebuild every other view's sorted execution list.
struct RecordingBatchCache {
    std::vector<uint64_t> family_masks;
    std::vector<uint32_t> roles;
    uint32_t batch_count{};bool valid{};
    uint32_t update(std::span<const uint64_t> masks,std::span<const FamilyBatches> families,
        std::span<const SevkOpaqueView> views,uint32_t count,std::vector<std::vector<uint32_t>>& output){
        uint64_t changed=0;
        bool same=valid&&batch_count==count&&family_masks.size()==masks.size();
        if(same)for(size_t f=0;f<masks.size();++f)changed|=family_masks[f]^masks[f];
        else changed=~uint64_t(0);
        size_t previous=output.size();output.resize(views.size());
        std::vector<uint8_t> admitted;uint32_t rebuilt=0;
        for(uint32_t v=0;v<views.size();++v){uint32_t role=(views[v].flags&2)?1:(views[v].flags&32)?2:0;
            if(same&&v<previous&&v<roles.size()&&roles[v]==role&&!(changed&(uint64_t(1)<<v)))continue;
            ++rebuilt;admitted.assign(count,0);auto& list=output[v];list.clear();
            for(uint32_t f=0;f<families.size();++f)if(masks[f]&(uint64_t(1)<<v))for(uint32_t b:families[f][role])admitted[b]=1;
            for(uint32_t b=0;b<count;++b)if(admitted[b])list.push_back(b);
        }
        family_masks.assign(masks.begin(),masks.end());roles.resize(views.size());
        for(size_t v=0;v<views.size();++v)roles[v]=(views[v].flags&2)?1:(views[v].flags&32)?2:0;
        batch_count=count;valid=true;return rebuilt;
    }
};
}
