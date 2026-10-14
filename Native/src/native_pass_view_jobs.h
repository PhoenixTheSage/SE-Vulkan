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

namespace native_pass {
// CPU-only per-view executor. run() joins every task, including after a
// failure, before returning. No actor mutation or Vulkan calls are permitted.
class ViewJobs {
    std::mutex mutex;std::condition_variable ready,completed;
    std::vector<std::thread> threads;std::function<void(uint32_t)> work;
    std::atomic<uint32_t> next{};uint32_t count{},lanes_remaining{};
    uint64_t epoch{};bool stopping{},active{};std::exception_ptr failure;
    void drain(){
        for(;;){uint32_t index=next.fetch_add(1,std::memory_order_relaxed);if(index>=count)return;
            try{work(index);}catch(...){std::lock_guard lock(mutex);if(!failure)failure=std::current_exception();}
        }
    }
    void finish_lane(){std::lock_guard lock(mutex);if(--lanes_remaining==0)completed.notify_one();}
    void worker(){uint64_t seen=0;for(;;){
        {std::unique_lock lock(mutex);ready.wait(lock,[&]{return stopping||epoch!=seen;});if(stopping)return;seen=epoch;}
        drain();finish_lane();
    }}
public:
    ViewJobs(){uint32_t lanes=std::min(4u,std::max(1u,std::thread::hardware_concurrency()));
        try{for(uint32_t i=1;i<lanes;++i)threads.emplace_back([this]{worker();});}
        catch(...){stop();throw;}
    }
    ViewJobs(const ViewJobs&)=delete;ViewJobs& operator=(const ViewJobs&)=delete;
    ~ViewJobs(){stop();}
    void stop(){ {std::lock_guard lock(mutex);stopping=true;}ready.notify_all();for(auto& thread:threads)if(thread.joinable())thread.join();threads.clear(); }
    uint32_t worker_count()const{return uint32_t(threads.size());}
    void run(uint32_t jobs,std::function<void(uint32_t)> fn){
        {std::lock_guard lock(mutex);if(active||stopping)throw std::runtime_error("invalid view job epoch");
            active=true;failure=nullptr;count=jobs;next=0;lanes_remaining=uint32_t(threads.size())+1;work=std::move(fn);++epoch;}
        ready.notify_all();drain();finish_lane();
        // A lane must leave drain(), even when it got no tasks. Callback
        // completion alone cannot protect count/next/work from the next epoch.
        std::exception_ptr error;{std::unique_lock lock(mutex);completed.wait(lock,[&]{return lanes_remaining==0;});error=failure;work={};active=false;}
        if(error)std::rethrow_exception(error);
    }
};
struct ViewCpuSample {uint64_t cycles{};double cpu_us{};bool available{};};
struct ViewJobProfile {
    uint64_t frames{},jobs{},worker_jobs{};double owner_us{},worker_us{},worker_cpu_us{},join_us{};
    uint64_t owner_cycles{},worker_cycles{};double owner_cpu_us{};bool cpu_available{};
};
}
