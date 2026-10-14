#pragma once
#include <array>
#include <bit>
#include <cstdint>
#include <cstddef>

// One queue call owns this cache. A hit cannot survive resource mutation or
// pointer/handle reuse between calls. Collisions require complete tuple equality.
template<std::size_t Slots=512> struct WorldTextureValidationCache {
    static_assert(Slots && !(Slots&(Slots-1)));
    using Key=std::array<uint64_t,4>;
    struct Entry { Key key{};bool valid{}; };
    std::array<Entry,Slots> entries{};
    template<class Check> bool validate(const Key& key,Check check) {
        uint64_t h=key[0]^std::rotl(key[1],17)^std::rotl(key[2],31)^std::rotl(key[3],47);
        h*=0x9e3779b97f4a7c15ull;auto& entry=entries[(h^(h>>32))&(Slots-1)];
        if(entry.valid && entry.key==key)return true;
        for(uint64_t id:key)check(id);
        entry.key=key;entry.valid=true;return false;
    }
};
