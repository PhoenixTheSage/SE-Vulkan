#pragma once
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <stdexcept>
#include <unordered_map>
#include <vector>

// CPU-owned object bytes. GPU consumers track the global revision, never source
// addresses or an assumed immutable handle. Offset reuse always gets a new version.
struct WorldObjectRecords {
    struct Record { uint32_t offset{},bytes{},capacity{};uint64_t revision{},seen{}; };
    std::unordered_map<uint64_t,Record> records;
    std::unordered_map<uint32_t,std::vector<uint32_t>> free_ranges;
    std::vector<uint8_t> data;
    std::vector<uint64_t> selected;
    uint64_t frame{},revision{},live{};
    uint32_t changed{};
    uint32_t alignment=256, budget=256*1024*1024;
    void reclaim(uint64_t age) {
        for(auto i=records.begin();i!=records.end();) {
            auto& r=i->second;
            if(frame-r.seen>=age){free_ranges[r.capacity].push_back(r.offset);live-=r.bytes;i=records.erase(i);}
            else ++i;
        }
    }
    void begin() { ++frame;selected.clear();changed=0;if(frame%64==0)reclaim(128); }
    uint32_t select(uint64_t id,const void* source,uint32_t bytes) {
        if(!frame || !id || !source || !bytes || !alignment || bytes>budget)throw std::runtime_error("invalid retained object");
        auto it=records.find(id);
        if(it!=records.end() && it->second.bytes!=bytes)throw std::runtime_error("retained object layout changed without new identity");
        if(it==records.end()) {
            uint64_t cap=(uint64_t(bytes)+alignment-1)/alignment*alignment;
            if(cap>budget)throw std::runtime_error("retained object exceeds storage budget");
            if(data.size()+cap>budget && free_ranges[uint32_t(cap)].empty())reclaim(3);
            auto& free=free_ranges[uint32_t(cap)];uint32_t offset;
            if(!free.empty()){offset=free.back();free.pop_back();}
            else {if(data.size()+cap>budget)throw std::runtime_error("retained object storage budget exhausted");offset=uint32_t(data.size());data.resize(data.size()+cap);}
            it=records.emplace(id,Record{offset,bytes,uint32_t(cap),0,0}).first;live+=bytes;
        }
        auto& r=it->second;
        bool different=!r.revision || std::memcmp(data.data()+r.offset,source,bytes)!=0;
        if(r.seen==frame && different)throw std::runtime_error("inconsistent duplicate retained object");
        if(different){std::memcpy(data.data()+r.offset,source,bytes);r.revision=++revision;++changed;}
        if(r.seen!=frame)selected.push_back(id);
        r.seen=frame;return r.offset;
    }
};
