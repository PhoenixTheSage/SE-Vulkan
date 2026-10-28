#pragma once
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdlib>
#include <deque>
#include <exception>
#include <functional>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#if defined(_M_X64)||defined(__x86_64__)
#include <immintrin.h>
#endif

// One process-wide worker pool for the renderer's CPU fan-out, in place of
// per-subsystem threads and per-frame std::async. Fixed worker count
// (SE_VULKAN_JOB_THREADS), two priorities, and per-kind telemetry of queue
// wait and run time. A waiter runs its own group's queued jobs instead of
// blocking; it never takes another group's job, so a waiter holding a lock
// cannot run work that needs that lock. Jobs must not block on anything but
// their own nested groups. Blocking pipeline stages (recording, present) use
// Stage threads, outside the cap.
#ifdef _WIN32
extern "C" __declspec(dllimport) int __stdcall SetThreadPriority(void* thread,int priority);
#endif
namespace sevk_jobs {
// OS priority for pool workers (SE_VULKAN_JOB_PRIORITY): unset or "normal"
// keeps the default; "above" and "highest" raise it. SE runs DX11's render
// jobs on its Highest-priority VeryHigh worker array.
inline int worker_os_priority(){const char* value=std::getenv("SE_VULKAN_JOB_PRIORITY");if(!value)return 0;std::string v(value);return v=="highest"?2:v=="above"?1:0;}
inline void raise_worker_priority(std::thread& thread,int priority){
#ifdef _WIN32
    if(priority>0)SetThreadPriority(thread.native_handle(),priority);
#else
    (void)thread;(void)priority;
#endif
}
enum class Priority:uint8_t {High,Normal};
// Frame split (SE_VULKAN_FRAME_SPLIT=1): the native recording paces the frame and overlaps the next frame's Build,
// whose publication fan-out would otherwise outrank the recording's own Normal fan-out (cascades, spots, uploads, the
// pass packing it consumes). Under the split, jobs submitted from the pacing stage (the recording thread, and jobs
// it fans out) run High and every other job runs Normal, in submission order. Opt-in (SE_VULKAN_RECORD_JOBS_FIRST=1):
// the 2026-10-28 A/B measured no gain, since the recording's lap growth was not job starvation. Off the split,
// priorities are unchanged.
inline bool pacing_priorities(){
    static const bool enabled=[]{const char* split=std::getenv("SE_VULKAN_FRAME_SPLIT");const char* first=std::getenv("SE_VULKAN_RECORD_JOBS_FIRST");
        return split&&std::string(split)=="1"&&first&&std::string(first)=="1";}();
    return enabled;}
inline thread_local bool tl_pacing_stage=false;
// Telemetry slots; SevkJobStats reports them in this order.
enum Kind:uint32_t {PassView,PassPack,CellView,QueueValidate,Upload,Cascade,Spot,KindCount};
constexpr uint32_t KindSlots=12;
struct KindStats {uint64_t jobs{},helped{},wait_ns{},max_wait_ns{},run_ns{};};
struct Snapshot {uint32_t workers{},peak_queued{},stage_threads{};std::array<KindStats,KindSlots> kinds{};};

inline void cpu_relax(){
#if defined(_M_X64)||defined(__x86_64__)
    _mm_pause();
#else
    std::this_thread::yield();
#endif
}
// Spin before sleeping (SE_VULKAN_JOB_SPIN_US, default 0). Measured 2026-10-26: a 30 us spin left queue waits
// unchanged (fan-outs arrive milliseconds apart, after the window), so it only burned CPU; kept for experiments.
inline uint64_t spin_ns_setting(){const char* v=std::getenv("SE_VULKAN_JOB_SPIN_US");long us=v?std::strtol(v,nullptr,10):0;return uint64_t(us<0?0:us>1000?1000:us)*1000;}
inline uint64_t now_ns(){return uint64_t(std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count());}

class System;
// Completion counter for a set of jobs. wait() rethrows the first failure;
// the destructor joins without throwing, as std::async futures do.
class Group {
    friend class System;
    System* owner{};std::atomic<uint32_t> pending{};std::exception_ptr failure;
public:
    Group()=default;Group(const Group&)=delete;Group& operator=(const Group&)=delete;
    ~Group();
    bool busy()const{return pending.load(std::memory_order_acquire)!=0;}
    void wait();
    void join()noexcept;
};

class System {
    struct Task {std::function<void()> fn;Group* group;Kind kind;uint64_t queued_ns;bool pacing{};};
    std::mutex mutex;std::condition_variable work_ready,done;
    std::deque<Task> queues[2];std::vector<std::thread> threads;bool stopping{};
    std::atomic<uint32_t> queued{};uint32_t sleepers{};uint64_t spin_ns=spin_ns_setting();
    std::mutex stats_mutex;Snapshot stats;std::atomic<uint32_t> stages{};
    void record(Kind kind,uint64_t queued,uint64_t start,uint64_t end,bool helped){
        std::lock_guard lock(stats_mutex);auto& k=stats.kinds[kind<KindSlots?kind:KindSlots-1];++k.jobs;if(helped)++k.helped;
        uint64_t wait=start-queued;k.wait_ns+=wait;k.max_wait_ns=std::max(k.max_wait_ns,wait);k.run_ns+=end-start;}
    void execute(Task& task,bool helped){
        uint64_t start=now_ns();
        // A pacing job's own fan-out inherits its priority (restored after, since a waiter may run this job).
        bool outer=tl_pacing_stage;tl_pacing_stage=task.pacing||outer;
        try{task.fn();}catch(...){std::lock_guard lock(mutex);if(!task.group->failure)task.group->failure=std::current_exception();}
        tl_pacing_stage=outer;
        record(task.kind,task.queued_ns,start,now_ns(),helped);task.fn={};
        {std::lock_guard lock(mutex);task.group->pending.fetch_sub(1,std::memory_order_acq_rel);}done.notify_all();
    }
    bool take(Task& out){for(auto& queue:queues)if(!queue.empty()){out=std::move(queue.front());queue.pop_front();queued.fetch_sub(1,std::memory_order_relaxed);return true;}return false;}
    bool take_own(Group& group,Task& out){
        for(auto& queue:queues)for(auto it=queue.begin();it!=queue.end();++it)if(it->group==&group){out=std::move(*it);queue.erase(it);queued.fetch_sub(1,std::memory_order_relaxed);return true;}
        return false;}
    void worker(){for(;;){Task task;bool got=false;
        if(spin_ns){uint64_t until=now_ns()+spin_ns;while(now_ns()<until){if(queued.load(std::memory_order_acquire)){std::unique_lock lock(mutex);if(take(task)){got=true;break;}if(stopping)return;}cpu_relax();}}
        if(!got){std::unique_lock lock(mutex);++sleepers;work_ready.wait(lock,[&]{return stopping||!queues[0].empty()||!queues[1].empty();});--sleepers;if(!take(task))return;}
        execute(task,false);}}
public:
    explicit System(uint32_t workers){stats.workers=workers;
        try{int os=worker_os_priority();for(uint32_t i=0;i<workers;++i){threads.emplace_back([this]{worker();});raise_worker_priority(threads.back(),os);}}catch(...){stop();throw;}}
    System(const System&)=delete;System& operator=(const System&)=delete;
    ~System(){stop();}
    void stop(){{std::lock_guard lock(mutex);stopping=true;}work_ready.notify_all();for(auto& thread:threads)if(thread.joinable())thread.join();threads.clear();}
    uint32_t worker_count()const{return uint32_t(threads.size());}
    void submit(Group& group,Kind kind,Priority priority,std::function<void()> fn){
        bool wake=false;
        {std::lock_guard lock(mutex);if(stopping)throw std::runtime_error("job system stopped");
            if(group.owner&&group.owner!=this)throw std::runtime_error("job group belongs to another system");group.owner=this;
            bool pacing=tl_pacing_stage;if(pacing_priorities())priority=pacing?Priority::High:Priority::Normal;
            group.pending.fetch_add(1,std::memory_order_acq_rel);queues[priority==Priority::High?0:1].push_back({std::move(fn),&group,kind,now_ns(),pacing});queued.fetch_add(1,std::memory_order_release);
            wake=sleepers>0;uint32_t depth=uint32_t(queues[0].size()+queues[1].size());std::lock_guard s(stats_mutex);stats.peak_queued=std::max(stats.peak_queued,depth);}
        if(wake)work_ready.notify_one();
    }
    // Runs the group's queued jobs on this thread, then waits for the ones
    // other workers took.
    void help(Group& group){
        for(;;){Task task;bool own;
            {std::unique_lock lock(mutex);own=take_own(group,task);}
            if(!own&&spin_ns){uint64_t until=now_ns()+spin_ns;while(group.pending.load(std::memory_order_acquire)!=0&&now_ns()<until)cpu_relax();if(group.pending.load(std::memory_order_acquire)==0)return;}
            if(!own){std::unique_lock lock(mutex);if(!take_own(group,task)){done.wait(lock,[&]{return group.pending.load(std::memory_order_acquire)==0||std::any_of(std::begin(queues),std::end(queues),[&](const auto& q){return std::any_of(q.begin(),q.end(),[&](const Task& t){return t.group==&group;});});});
                if(group.pending.load(std::memory_order_acquire)==0)return;continue;}}
            execute(task,true);}
    }
    Snapshot snapshot(bool reset_max){std::lock_guard lock(stats_mutex);auto copy=stats;copy.workers=worker_count();copy.stage_threads=stages.load();
        if(reset_max){stats.peak_queued=0;for(auto& k:stats.kinds)k.max_wait_ns=0;}return copy;}
    void stage_started(){stages.fetch_add(1);}void stage_stopped(){stages.fetch_sub(1);}
};

inline void Group::wait(){if(!owner)return;owner->help(*this);std::exception_ptr error;{error=failure;failure=nullptr;}if(error)std::rethrow_exception(error);}
inline void Group::join()noexcept{if(owner)owner->help(*this);failure=nullptr;}
inline Group::~Group(){join();}

// Default: half the logical processors less one, 1..7, leaving the rest to
// the game's simulation, Havok and the render and recording threads.
inline uint32_t default_workers(){
    if(const char* value=std::getenv("SE_VULKAN_JOB_THREADS")){char* end{};long n=std::strtol(value,&end,10);if(end!=value&&n>=0)return uint32_t(std::min(n,64l));}
    uint32_t logical=std::max(1u,std::thread::hardware_concurrency());return std::clamp(logical/2u,2u,8u)-1u;
}
// Never destroyed: joining workers from a DLL's static destructors would run
// under the loader lock, which exiting threads also need.
inline System& shared(){static System* system=new System(default_workers());return *system;}

// A parallel-for over [0,count): up to lanes-1 pool jobs plus the caller
// drain one shared index. Joins every lane, including after a failure, and
// rethrows the first failure.
inline void parallel_for(Kind kind,Priority priority,uint32_t lanes,uint32_t count,const std::function<void(uint32_t)>& fn){
    std::atomic<uint32_t> next{};std::mutex failure_mutex;std::exception_ptr failure;
    auto drain=[&]{for(;;){uint32_t index=next.fetch_add(1,std::memory_order_relaxed);if(index>=count)return;
        try{fn(index);}catch(...){std::lock_guard lock(failure_mutex);if(!failure)failure=std::current_exception();}}};
    {Group group;uint32_t helpers=count?std::min(lanes?lanes-1:0u,count-1):0u;
        for(uint32_t i=0;i<helpers;++i)shared().submit(group,kind,priority,drain);
        drain();group.join();}
    if(failure)std::rethrow_exception(failure);
}

// A dedicated thread for one blocking pipeline stage at a time (recording,
// present). start() waits for the previous task to finish; wait() joins and
// rethrows its failure.
// A stage spins briefly (SE_VULKAN_STAGE_SPIN_US; 300 µs under SE_VULKAN_FRAME_SPLIT=1, otherwise 0) before blocking,
// so a start that follows within the window skips the kernel wake. Under the split the recording stage paces the
// frame and is relaunched within about a millisecond of finishing.
inline uint64_t stage_spin_ns(){
    static const uint64_t ns=[]{if(const char* v=std::getenv("SE_VULKAN_STAGE_SPIN_US")){char* end{};long n=std::strtol(v,&end,10);if(end!=v&&n>=0)return uint64_t(std::min(n,5000l))*1000;}
        const char* split=std::getenv("SE_VULKAN_FRAME_SPLIT");return split&&std::string(split)=="1"?uint64_t(300000):uint64_t(0);}();
    return ns;}
class Stage {
    std::mutex mutex;std::condition_variable ready,completed;std::thread thread;
    std::function<void()> work;bool stopping{},queued{},busy{};std::exception_ptr failure;std::atomic<bool> signal{false};
    void worker(){for(;;){std::function<void()> task;
        if(uint64_t spin=stage_spin_ns()){uint64_t until=now_ns()+spin;while(!signal.load(std::memory_order_acquire)&&now_ns()<until)cpu_relax();}
        {std::unique_lock lock(mutex);ready.wait(lock,[&]{return stopping||queued;});if(stopping&&!queued)return;task=std::move(work);work={};queued=false;signal.store(false,std::memory_order_relaxed);}
        std::exception_ptr error;try{task();}catch(...){error=std::current_exception();}
        {std::lock_guard lock(mutex);failure=error;busy=false;}completed.notify_all();}}
public:
    Stage(){thread=std::thread([this]{worker();});shared().stage_started();}
    Stage(const Stage&)=delete;Stage& operator=(const Stage&)=delete;
    ~Stage(){{std::lock_guard lock(mutex);stopping=true;}ready.notify_all();if(thread.joinable())thread.join();shared().stage_stopped();}
    bool pending(){std::lock_guard lock(mutex);return busy;}
    void start(std::function<void()> fn){
        {std::unique_lock lock(mutex);completed.wait(lock,[&]{return !busy;});if(stopping)throw std::runtime_error("stage stopped");
            work=std::move(fn);queued=busy=true;failure=nullptr;signal.store(true,std::memory_order_release);}
        ready.notify_one();}
    void wait(){std::exception_ptr error;{std::unique_lock lock(mutex);completed.wait(lock,[&]{return !busy;});error=failure;failure=nullptr;}if(error)std::rethrow_exception(error);}
};
}
