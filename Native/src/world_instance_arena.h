#pragma once
#include <cstdint>
#include <cstring>
#include <stdexcept>
#include <algorithm>
#include <vector>

// Source addresses are keys only within one queue call. The arena owns the bytes
// consumed after the managed bridge unpins its packet arenas.
struct WorldInstanceArena {
    struct Key {
        const void* source;
        uint32_t bytes;
        bool operator==(const Key&) const = default;
    };
    struct Entry { Key key{};uint32_t offset{},epoch{}; };
    static size_t hash(const Key& key) noexcept {
        uint64_t value=uint64_t(reinterpret_cast<uintptr_t>(key.source)) ^ (uint64_t(key.bytes)*0x9e3779b97f4a7c15ull);
        value^=value>>33;value*=0xff51afd7ed558ccdull;value^=value>>33;return size_t(value);
    }
    std::vector<uint8_t> data;
    std::vector<Entry> offsets;
    uint32_t epoch=1;size_t live=0;
    static constexpr uint64_t budget = 256ull * 1024 * 1024;

    void clear() { data.clear();live=0;if(++epoch==0){for(auto& entry:offsets)entry.epoch=0;epoch=1;} }
    void grow() {
        std::vector<Entry> next(std::max(size_t(64),offsets.size()*2));
        for(const auto& entry:offsets)if(entry.epoch==epoch){size_t i=hash(entry.key)&(next.size()-1);while(next[i].epoch==epoch)i=(i+1)&(next.size()-1);next[i]=entry;}
        offsets.swap(next);
    }
    uint32_t append(const void* source, uint32_t bytes) {
        Key key{source, bytes};
        if(offsets.empty())grow();
        size_t i=hash(key)&(offsets.size()-1);
        while(offsets[i].epoch==epoch){if(offsets[i].key==key)return offsets[i].offset;i=(i+1)&(offsets.size()-1);}
        if (!source || !bytes || data.size() + uint64_t(bytes) > budget)
            throw std::runtime_error("world instance batch exceeds bounded upload budget or has an invalid stream");
        uint32_t offset = static_cast<uint32_t>(data.size());
        auto* first = static_cast<const uint8_t*>(source);
        data.insert(data.end(), first, first + bytes);
        if((live+1)*2>offsets.size()){grow();i=hash(key)&(offsets.size()-1);while(offsets[i].epoch==epoch)i=(i+1)&(offsets.size()-1);}
        offsets[i]={key,offset,epoch};++live;
        return offset;
    }
};
