#pragma once
#include "se_vulkan.h"
#include "se_gpu_opaque_reference.h"
#include <algorithm>
#include <span>
#include <stdexcept>
#include <tuple>
#include <unordered_map>
#include <vector>

namespace gpu_opaque {
struct MainEntry {
    uint32_t order,part,kind,index;
    bool operator==(const MainEntry&)const=default;
};
inline bool main_entry_less(const MainEntry& a,const MainEntry& b){
    // Equal order and part formerly inherited append order from stable_sort:
    // managed, stock, native. Within each kind retain the original order.
    return std::tie(a.order,a.part,a.kind)<std::tie(b.order,b.part,b.kind);
}
using OwnerOrders=std::unordered_map<uint32_t,uint32_t>;

// One published topology. Callers reset this cache when installing stock batches.
class StockMainOrder {
    struct ActorKey {uint32_t owner,family;bool operator==(const ActorKey&)const=default;};
    bool initialized{},sorted_valid{};
    uint64_t resident_revision{};
    std::vector<uint64_t> versions;
    std::vector<ActorKey> actors;
    std::unordered_multimap<uint32_t,uint32_t> by_owner;
    std::vector<uint32_t> base_orders,resolved,family_orders,next_families,previous_batches;
    std::vector<std::pair<uint32_t,uint32_t>> previous_overrides,overrides;
    std::vector<MainEntry> sorted;
public:
    std::vector<MainEntry> dynamic;
    template<class BatchInfo>
    bool update(std::span<const SevkOpaqueActor> rows,std::span<const uint64_t> revisions,uint32_t families,
                const OwnerOrders& resident,uint64_t revision,const OwnerOrders& frame,
                std::span<const uint32_t> admitted,BatchInfo batch_info){
        const bool cold=!initialized||rows.size()!=actors.size()||families!=family_orders.size();
        initialized=false; // A retry after a failed allocation or lookup must reconstruct all keys.
        if(cold){actors.resize(rows.size());versions.resize(rows.size());}
        bool identities=cold;
        for(size_t i=0;i<rows.size();++i){
            if(!cold&&versions[i]==revisions[i])continue;
            ActorKey next=rows[i].flags&2?ActorKey{0,UINT32_MAX}:ActorKey{rows[i].owner,rows[i].family};
            identities|=actors[i]!=next;actors[i]=next;versions[i]=revisions[i];
        }
        if(identities){
            by_owner.clear();by_owner.reserve(actors.size());
            for(uint32_t i=0;i<actors.size();++i)if(actors[i].family!=UINT32_MAX)by_owner.emplace(actors[i].owner,i);
        }
        bool orders_changed=identities||revision!=resident_revision;
        if(orders_changed){
            base_orders.assign(actors.size(),UINT32_MAX);
            for(auto [owner,i]:by_owner){
                auto it=resident.find(owner);base_orders[i]=it==resident.end()?UINT32_MAX:it->second;
            }
            resident_revision=revision;
        }
        overrides.clear();
        for(auto [owner,order]:frame){
            auto [first,last]=by_owner.equal_range(owner);
            for(auto it=first;it!=last;++it)
                if(base_orders[it->second]!=order)overrides.emplace_back(it->second,order);
        }
        std::sort(overrides.begin(),overrides.end());
        if(orders_changed||overrides!=previous_overrides){
            resolved=base_orders;
            for(auto [i,order]:overrides)resolved[i]=order;
            previous_overrides=overrides;
            next_families.assign(families,UINT32_MAX);
            for(uint32_t i=0;i<actors.size();++i)if(actors[i].family!=UINT32_MAX){
                auto& order=next_families.at(actors[i].family);
                order=std::min(order,resolved[i]);
            }
            if(cold||family_orders!=next_families){family_orders.swap(next_families);sorted_valid=false;}
        }
        bool hit=sorted_valid&&std::equal(admitted.begin(),admitted.end(),previous_batches.begin(),previous_batches.end());
        if(!hit){
            sorted_valid=false;sorted.clear();sorted.reserve(admitted.size());
            for(uint32_t b:admitted){
                const auto& key=batch_info(b);if(key.old)continue;
                if(!key.group)throw std::runtime_error("NEW batch lost its family order identity");
                uint32_t order=family_orders.at(key.group-1);
                sorted.push_back({order==UINT32_MAX?UINT32_MAX-2:order,order==UINT32_MAX?b:key.part,1,b});
            }
            std::stable_sort(sorted.begin(),sorted.end(),main_entry_less);
            previous_batches.assign(admitted.begin(),admitted.end());sorted_valid=true;
        }
        initialized=true;return hit;
    }
    void merge(std::vector<MainEntry>& output){
        std::stable_sort(dynamic.begin(),dynamic.end(),main_entry_less);
        output.resize(dynamic.size()+sorted.size());
        std::merge(dynamic.begin(),dynamic.end(),sorted.begin(),sorted.end(),output.begin(),main_entry_less);
    }
};
}
