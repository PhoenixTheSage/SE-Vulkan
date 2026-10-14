#pragma once
#include "se_critical_profile.h"
#include <array>
#include <chrono>
#include <cstdint>
#ifdef _WIN32
#include <windows.h>
#endif

namespace critical_profile {
enum Kind:uint32_t {QueueOwner,QueueWorker,PassPrepare,PassPublish,WorldPrepare,MainRecord,CascadeOwner,CascadeWorker,CascadeJoin,SpotRecord,ProbeRecord,LightingPostRecord,RecordOwner,CommandApi,DescriptorApi,BarrierApi,SubmitApi,WaitApi,AcquireApi,PresentJoin,PoolApi,FlushApi};
struct Lane {std::array<SevkCriticalRow,SEVK_CRITICAL_ROWS> rows{};uint64_t overhead{};bool sampled{};};
inline thread_local Lane* lane=nullptr;
inline uint64_t clock(){return uint64_t(std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count());}
inline bool cpu(uint64_t& time,uint64_t& cycles){
#ifdef _WIN32
    FILETIME creation{},exit{},kernel{},user{};ULONG64 current{};if(!GetThreadTimes(GetCurrentThread(),&creation,&exit,&kernel,&user)||!QueryThreadCycleTime(GetCurrentThread(),&current))return false;
    time=(uint64_t(kernel.dwHighDateTime)<<32|kernel.dwLowDateTime)+(uint64_t(user.dwHighDateTime)<<32|user.dwLowDateTime);cycles=current;return true;
#else
    (void)time;(void)cycles;return false;
#endif
}
class Bind {
    Lane* previous;
public:
    explicit Bind(Lane* value):previous(lane){lane=value&&value->sampled?value:nullptr;}
    ~Bind(){lane=previous;}
};
class Scope {
    Lane* target{};Kind kind;uint64_t start{},cpu_start{},cycles_start{};bool counted{};
public:
    explicit Scope(Kind value,bool counters=false):kind(value){if(!lane||!lane->sampled)return;target=lane;uint64_t before=clock();if(counters)counted=cpu(cpu_start,cycles_start);start=clock();target->overhead+=start-before;}
    ~Scope(){if(!target)return;uint64_t end=clock();auto& row=target->rows[kind];row.calls++;row.wall_ns+=end-start;if(counted){uint64_t time{},cycles{};if(cpu(time,cycles)&&time>=cpu_start&&cycles>=cycles_start){row.cpu_100ns+=time-cpu_start;row.cycles+=cycles-cycles_start;row.cpu_reads++;}}target->overhead+=clock()-end;}
};
template<class F>decltype(auto) api(Kind kind,F&& call){if(!lane)return call();Scope scope(kind);return call();}
struct State {
    Lane owner;std::array<Lane,16> workers{};bool pending{};
    SevkCriticalProfile totals{sizeof(SevkCriticalProfile),SEVK_ABI_VERSION};
    void begin(uint32_t stride){pending=true;totals.enabled=stride!=0;totals.stride=stride;totals.begun++;bool sample=stride&&totals.begun%stride==0;owner={};owner.sampled=sample;for(auto& worker:workers){worker={};worker.sampled=sample;}lane=sample?&owner:nullptr;}
    void finish(){if(!pending||!totals.enabled){pending=false;lane=nullptr;return;}pending=false;totals.completed++;if(owner.sampled){totals.selected++;auto add=[&](const Lane& source){totals.counter_overhead_ns+=source.overhead;for(uint32_t i=0;i<SEVK_CRITICAL_ROWS;i++){auto& a=totals.rows[i];auto& b=source.rows[i];a.calls+=b.calls;a.wall_ns+=b.wall_ns;a.cpu_100ns+=b.cpu_100ns;a.cycles+=b.cycles;a.cpu_reads+=b.cpu_reads;}};add(owner);for(auto& worker:workers)add(worker);}owner.sampled=false;for(auto& worker:workers)worker.sampled=false;lane=nullptr;}
    void detach(){pending=false;if(lane==&owner)lane=nullptr;}
};
struct Finish {State& state;bool submitted{};~Finish(){if(submitted)state.finish();else state.detach();}};
}
