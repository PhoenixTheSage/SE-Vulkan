#include "../src/world_object_records.h"
#include <iostream>
void require(bool b,const char* text){if(!b)throw std::runtime_error(text);}
template<class F>void rejects(F f){try{f();}catch(const std::runtime_error&){return;}throw std::runtime_error("invalid record accepted");}
int main(){
    WorldObjectRecords s;s.alignment=16;s.budget=64;uint8_t a[17]{1},b[17]{2};
    s.begin();auto first=s.select(1,a,17);auto version=s.records.at(1).revision;
    a[0]=9;require(s.data[first]==1,"source bytes are owned");
    rejects([&]{s.select(1,a,17);});a[0]=1;
    require(s.select(1,a,17)==first && s.selected.size()==1 && s.changed==1,"duplicate selection is unique");
    s.begin();require(s.select(1,a,17)==first && !s.changed,"unchanged record retains offset and revision");
    a[0]=4;s.begin();s.select(1,a,17);require(s.records.at(1).revision>version && s.changed==1,"changed bytes revise in place");
    rejects([&]{s.select(1,a,16);});s.select(2,b,17);
    rejects([&]{s.select(3,a,17);});
    s.begin();s.begin();s.begin();auto at=s.select(3,b,17);
    require(s.records.size()==1 && s.live==17 && s.data.size()==64,"budget pressure reclaims old records and reuses ranges");
    require(s.records.at(3).revision>version,"reused offset has a new global revision");
    require(at%16==0,"record offsets aligned");rejects([&]{s.select(0,a,17);});rejects([&]{s.select(4,nullptr,17);});
    std::cout<<"retained object ownership, changes, duplicates, reclamation and budget passed\n";
}
