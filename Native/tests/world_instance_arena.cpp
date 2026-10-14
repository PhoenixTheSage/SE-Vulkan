#include "../src/world_instance_arena.h"
#include "../src/world_texture_validation_cache.h"
#include <unordered_set>
#include <array>
#include <chrono>
#include <iostream>

void require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}
int main(int argc, char**) {
    WorldTextureValidationCache<1> validation;
    std::unordered_set<uint64_t> resources{1,2,3,4,5};uint32_t checks=0;
    auto exists=[&](uint64_t id){++checks;if(!resources.count(id))throw std::runtime_error("missing texture");};
    require(!validation.validate({1,2,3,4},exists) && checks==4,"first material did not validate all roles");
    require(validation.validate({1,2,3,4},exists) && checks==4,"repeated exact tuple was not reused");
    require(!validation.validate({1,2,3,5},exists) && checks==8,"collision skipped a changed texture role");
    bool invalid=false;try{validation.validate({1,2,3,99},exists);}catch(const std::runtime_error&){invalid=true;}
    require(invalid,"missing texture escaped validation after a collision");
    auto before_retry=checks;invalid=false;try{validation.validate({1,2,3,99},exists);}catch(const std::runtime_error&){invalid=true;}
    require(invalid && checks>before_retry,"failed validation was cached");
    resources.erase(4);WorldTextureValidationCache<1> next_call;
    invalid=false;try{next_call.validate({1,2,3,4},exists);}catch(const std::runtime_error&){invalid=true;}
    require(invalid,"a new queue call retained validation of a removed resource");
    resources.insert(4);require(!next_call.validate({1,2,3,4},exists),"restored resource was not validated again");
    std::cout<<"PASS exact material reuse, role collisions, failed validation, removal and new-call ownership\n";
    WorldInstanceArena arena;
    std::array<uint8_t,128> a{}, b{};
    a.fill(17);b.fill(31);
    require(arena.append(a.data(),128)==0,"first offset");
    require(arena.append(a.data(),128)==0 && arena.data.size()==128,"shared stream was copied twice");
    require(arena.append(a.data(),64)==128,"same address with different count aliased");
    require(arena.append(b.data(),128)==192,"distinct streams aliased");
    a.fill(99);
    require(arena.data[0]==17 && arena.data[128]==17 && arena.data[192]==31,"queue did not own a snapshot");
    auto capacity=arena.data.capacity();arena.clear();
    require(arena.append(a.data(),128)==0 && arena.data[0]==99,"pointer reused next frame kept stale bytes");
    require(arena.data.capacity()==capacity,"frame reset discarded storage capacity");
    bool rejected=false;
    try{arena.append(a.data(),uint32_t(WorldInstanceArena::budget+1));}catch(const std::runtime_error&){rejected=true;}
    require(rejected,"oversized stream was not rejected before reading source");
    std::vector<uint8_t> many(4096*64,73);arena.clear();
    for(uint32_t i=0;i<4096;++i)require(arena.append(many.data()+i*64,64)==i*64,"flat lookup growth lost an offset");
    for(uint32_t i=4096;i--;)require(arena.append(many.data()+i*64,64)==i*64,"collision chain did not preserve an existing stream");
    auto table_capacity=arena.offsets.capacity();arena.clear();many[0]=91;
    require(arena.append(many.data(),64)==0 && arena.data[0]==91 && arena.offsets.capacity()==table_capacity,"epoch reset retained a stale snapshot or discarded lookup storage");
    std::cout << "PASS shared stream/count identity, deep copy, frame reset, capacity reuse and budget\n";
    if(argc>1){
        constexpr uint32_t streams=256,instances=256,parts=8,frames=200;
        std::vector<uint8_t> source(streams*instances*64,17), expanded;
        WorldInstanceArena shared;
        auto run=[&](bool deduplicate){auto start=std::chrono::steady_clock::now();uint64_t checksum=0;
            for(uint32_t frame=0;frame<frames;++frame){expanded.clear();shared.clear();
                for(uint32_t stream=0;stream<streams;++stream)for(uint32_t part=0;part<parts;++part){auto* p=source.data()+stream*instances*64;
                    if(deduplicate)shared.append(p,instances*64);else expanded.insert(expanded.end(),p,p+instances*64);}
                checksum+=(deduplicate?shared.data:expanded).back();}
            require(checksum==uint64_t(frames)*17,"benchmark checksum");
            return std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count()/frames;};
        run(false);run(true); // Warm storage before measuring.
        auto expanded_ms=run(false),shared_ms=run(true);
        std::cout << "Synthetic queue copy: " << streams << " streams x " << instances << " instances x " << parts << " parts; mean of " << frames
                  << " frames. Expanded=" << expanded_ms << " ms, shared=" << shared_ms << " ms; bytes/frame=" << source.size()*parts << " -> " << source.size() << '\n';
    }
}
