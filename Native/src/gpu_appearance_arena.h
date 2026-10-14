#pragma once
#include "se_vulkan.h"
#include "se_gpu_opaque_reference.h"
#include <vector>
#include <span>
#include <algorithm>
#include <cstring>
#include <stdexcept>

namespace gpu_opaque {
constexpr uint32_t appearance_limit=32768, appearance_header_stride=144;
// CPU source mirror only; each fenced frame slot has its own GPU copy.
class AppearanceArena {
public:
    struct Range {uint32_t first{},count{},capacity{};};
    struct Update {uint32_t actor;std::vector<SevkOpaqueAppearance> entries;};
    std::vector<uint8_t> bytes;
    std::vector<Range> ranges;
    std::vector<uint64_t> versions;
    uint64_t revision{};
    void reset(uint32_t actors){bytes.assign(uint64_t(actors)*appearance_header_stride,0);ranges.assign(actors,{});versions.assign(actors,0);revision=0;}
    void object(uint32_t actor,const void* source){std::memcpy(bytes.data()+uint64_t(actor)*appearance_header_stride,source,128);}
    bool equal(uint32_t actor,std::span<const SevkOpaqueAppearance> entries)const{
        auto& r=ranges.at(actor);return r.count==entries.size()&&(!r.count||std::memcmp(bytes.data()+r.first,entries.data(),r.count*sizeof(SevkOpaqueAppearance))==0);
    }
    void commit(std::span<const Update> updates){
        uint64_t required=bytes.size();for(auto& u:updates){auto& r=ranges.at(u.actor);
            if(u.entries.size()>appearance_limit)throw std::runtime_error("appearance span exceeds admitted model budget");
            if(u.entries.size()>r.capacity)required+=u.entries.size()*sizeof(SevkOpaqueAppearance);}
        if(required>128ull*1024*1024)throw std::runtime_error("resident appearance arena exceeds storage range");
        bytes.reserve(size_t(required)); // Entire batch has capacity before any mutation.
        for(auto& u:updates){if(equal(u.actor,u.entries))continue;auto& r=ranges[u.actor];
            if(u.entries.size()>r.capacity){r.first=uint32_t(bytes.size());r.capacity=uint32_t(u.entries.size());bytes.resize(bytes.size()+r.capacity*sizeof(SevkOpaqueAppearance));}
            r.count=uint32_t(u.entries.size());if(r.count)std::memcpy(bytes.data()+r.first,u.entries.data(),r.count*sizeof(SevkOpaqueAppearance));
            std::memcpy(bytes.data()+uint64_t(u.actor)*appearance_header_stride+128,&r.first,4);versions[u.actor]=++revision;
        }
    }
};
}
