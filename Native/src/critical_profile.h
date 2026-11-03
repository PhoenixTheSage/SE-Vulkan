#pragma once
#include "se_critical_profile.h"
#include <array>
#include <chrono>
#include <cstdint>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <stdexcept>
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
// Build and launch are sequential for one ticket, but may overlap the previous
// ticket's recording. Each sampled frame owns its lanes until recording finishes.
struct Frame {
    Lane scene,record;
    std::array<Lane,16> workers{};
    Frame(){scene.sampled=record.sampled=true;for(auto& worker:workers)worker.sampled=true;}
};
class State {
    struct Ticket {uint64_t id{};std::unique_ptr<Frame> sample;};
    struct Attachment {State* state;uint64_t id;Frame* frame;};
    static inline thread_local Attachment attachment{};
    mutable std::mutex mutex;
    std::unordered_map<uint64_t,Ticket> pending;
    Ticket recording;
    SevkCriticalProfile totals{sizeof(SevkCriticalProfile),SEVK_ABI_VERSION};
    void attach(const Ticket& t){attachment={this,t.id,t.sample.get()};lane=t.sample?&t.sample->scene:nullptr;}
public:
    uint64_t begin(uint32_t stride){
        if(stride>1024)throw std::runtime_error("invalid critical sample stride");
        std::lock_guard lock(mutex);
        // Legacy single-thread callers may abandon a frame before BeginFrame.
        if(attachment.state==this)pending.erase(attachment.id);
        detach();totals.stride=stride;totals.enabled=stride!=0;
        if(!stride)return 0;
        uint64_t id=++totals.begun;auto& t=pending[id];t.id=id;
        if(id%stride==0)t.sample=std::make_unique<Frame>();
        attach(t);return id;
    }
    void launch(uint64_t id){
        std::lock_guard lock(mutex);
        auto found=pending.find(id);
        if(found==pending.end())throw std::runtime_error("critical launch ticket not pending");
        attach(found->second);
    }
    void detach(){if(attachment.state==this){attachment={};lane=nullptr;}}
    void discard(uint64_t id){std::lock_guard lock(mutex);if(attachment.state==this&&attachment.id==id)detach();pending.erase(id);}
    Frame* input_frame()const{return attachment.state==this?attachment.frame:nullptr;}
    void open_recording(){
        // BeginFrame has waited for the previous recording. Untagged runtime
        // frames take no lock and never allocate profiling state.
        if(attachment.state!=this)return;
        std::lock_guard lock(mutex);
        auto found=pending.find(attachment.id);
        if(found==pending.end())throw std::runtime_error("critical frame opened without ticket");
        recording=std::move(found->second);pending.erase(found);detach();
    }
    Lane* record_owner(){return recording.sample?&recording.sample->record:nullptr;}
    Lane* record_worker(size_t i){return recording.sample?&recording.sample->workers[i]:nullptr;}
    void finish(bool submitted){
        if(!recording.id)return;
        std::lock_guard lock(mutex);
        if(submitted){
            totals.completed++;
            if(auto* f=recording.sample.get()){
                totals.selected++;
                auto add=[&](const Lane& source){
                    totals.counter_overhead_ns+=source.overhead;
                    for(uint32_t i=0;i<SEVK_CRITICAL_ROWS;i++){
                        auto& a=totals.rows[i];const auto& b=source.rows[i];
                        a.calls+=b.calls;a.wall_ns+=b.wall_ns;a.cpu_100ns+=b.cpu_100ns;a.cycles+=b.cycles;a.cpu_reads+=b.cpu_reads;
                    }
                };
                add(f->scene);add(f->record);for(auto& worker:f->workers)add(worker);
            }
        }
        recording={};
    }
    SevkCriticalProfile snapshot()const{std::lock_guard lock(mutex);return totals;}
};
struct Finish {State& state;bool submitted{};~Finish(){state.finish(submitted);}};
}
