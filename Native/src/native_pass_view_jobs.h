#pragma once
#include <algorithm>
#include <cstdint>
#include <stdexcept>
#include <atomic>
#include <condition_variable>
#include <exception>
#include <functional>
#include <mutex>
#include <thread>
#include <vector>
#include "job_system.h"

namespace native_pass {
// CPU-only per-view executor on the shared job pool (job_system.h). run()
// joins every task, including after a failure, before returning. No actor
// mutation or Vulkan calls are permitted. Up to four lanes, as before the
// shared pool, so chunking stays the same.
class ViewJobs {
    sevk_jobs::Kind kind;std::atomic<bool> active{};uint32_t lanes;
public:
    explicit ViewJobs(sevk_jobs::Kind job_kind=sevk_jobs::PassView):kind(job_kind),lanes(std::min(4u,sevk_jobs::shared().worker_count()+1)){}
    ViewJobs(const ViewJobs&)=delete;ViewJobs& operator=(const ViewJobs&)=delete;
    uint32_t worker_count()const{return lanes-1;}
    void run(uint32_t jobs,std::function<void(uint32_t)> fn){
        if(active.exchange(true))throw std::runtime_error("invalid view job epoch");
        struct Release{std::atomic<bool>& flag;~Release(){flag=false;}} release{active};
        sevk_jobs::parallel_for(kind,sevk_jobs::Priority::High,lanes,jobs,fn);
    }
};
// One deferred task on the shared pool. wait() joins it (running it here if
// no worker has started it) and rethrows its failure; start() requires the
// previous task to be joined.
class Background {
    sevk_jobs::Kind kind;sevk_jobs::Group group;bool started{};
public:
    explicit Background(sevk_jobs::Kind job_kind=sevk_jobs::PassPack):kind(job_kind){}
    Background(const Background&)=delete;Background& operator=(const Background&)=delete;
    bool pending(){return group.busy();}
    void start(std::function<void()> fn){if(group.busy())throw std::runtime_error("background task already pending");started=true;sevk_jobs::shared().submit(group,kind,sevk_jobs::Priority::Normal,std::move(fn));}
    void wait(){if(started)group.wait();}
};
struct ViewCpuSample {uint64_t cycles{};double cpu_us{};bool available{};};
struct ViewJobProfile {
    uint64_t frames{},jobs{},worker_jobs{};double owner_us{},worker_us{},worker_cpu_us{},join_us{};
    uint64_t owner_cycles{},worker_cycles{};double owner_cpu_us{};bool cpu_available{};
};
}
