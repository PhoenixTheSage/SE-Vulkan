#pragma once
#include "se_vulkan.h"
#include "se_gpu_opaque_reference.h"
#include <vector>
#include <span>
#include <cmath>
#include <algorithm>
#include <array>

namespace gpu_opaque {
// Conservative recording admission, independent of GPU LOD/transition selection.
// Ignore appearance, projected-size and distance pruning: false positives cost
// commands, but must never lose geometry selected by the GPU.
inline bool recording_member(const SevkOpaqueActor& a,const SevkOpaqueView& v,uint32_t view){
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
inline std::vector<std::vector<uint32_t>> recording_batches(std::span<const SevkOpaqueActor> actors,
    std::span<const FamilyBatches> families,std::span<const SevkOpaqueView> views,uint32_t batches){
    std::vector<uint64_t> family_views(families.size());
    for(auto& a:actors)for(uint32_t v=0;v<views.size();v++)
        if(!(family_views[a.family]&(uint64_t(1)<<v))&&recording_member(a,views[v],v))family_views[a.family]|=uint64_t(1)<<v;
    std::vector<std::vector<uint32_t>> output(views.size());std::vector<uint8_t> admitted(batches);
    for(uint32_t v=0;v<views.size();v++){
        std::fill(admitted.begin(),admitted.end(),0);uint32_t role=(views[v].flags&2)?1:(views[v].flags&32)?2:0;
        for(uint32_t f=0;f<families.size();f++)if(family_views[f]&(uint64_t(1)<<v))for(uint32_t b:families[f][role])admitted[b]=1;
        for(uint32_t b=0;b<batches;b++)if(admitted[b])output[v].push_back(b);
    }
    return output;
}
}
