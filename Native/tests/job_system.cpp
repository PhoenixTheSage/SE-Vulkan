#include "../src/job_system.h"
#include "../src/native_pass_view_jobs.h"
#include <iostream>
#include <numeric>

static void require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
int main(){
    {   // Every index runs once; failures join every lane and rethrow.
        std::vector<std::atomic<uint32_t>> hits(1000);
        sevk_jobs::parallel_for(sevk_jobs::PassView,sevk_jobs::Priority::High,4,1000,[&](uint32_t i){hits[i].fetch_add(1);});
        for(auto& h:hits)require(h.load()==1,"parallel_for runs each index once");
        std::atomic<uint32_t> ran{};bool threw=false;
        try{sevk_jobs::parallel_for(sevk_jobs::PassView,sevk_jobs::Priority::High,4,64,[&](uint32_t i){ran.fetch_add(1);if(i==5)throw std::runtime_error("x");});}catch(const std::runtime_error&){threw=true;}
        require(threw&&ran.load()==64,"failure rethrown after every index ran");
    }
    {   // Nested fan-out inside pool jobs finishes: waiters run their own group's jobs.
        auto& pool=sevk_jobs::shared();std::atomic<uint32_t> leaves{};sevk_jobs::Group outer;
        for(uint32_t i=0;i<16;++i)pool.submit(outer,sevk_jobs::PassPack,sevk_jobs::Priority::Normal,[&]{
            sevk_jobs::parallel_for(sevk_jobs::CellView,sevk_jobs::Priority::High,4,32,[&](uint32_t){leaves.fetch_add(1);});});
        outer.wait();require(leaves.load()==16*32,"nested groups complete");
    }
    {   // A group's failure is reported by wait() once; the destructor joins without throwing.
        sevk_jobs::Group group;sevk_jobs::shared().submit(group,sevk_jobs::Upload,sevk_jobs::Priority::Normal,[]{throw std::runtime_error("upload");});
        bool threw=false;try{group.wait();}catch(const std::runtime_error&){threw=true;}require(threw&&!group.busy(),"group failure");
        group.wait();
        {sevk_jobs::Group dropped;sevk_jobs::shared().submit(dropped,sevk_jobs::Upload,sevk_jobs::Priority::Normal,[]{throw std::runtime_error("dropped");});}
    }
    {   // A pool without workers runs everything on the waiter.
        sevk_jobs::System none(0);sevk_jobs::Group group;uint32_t value=0;
        none.submit(group,sevk_jobs::Spot,sevk_jobs::Priority::Normal,[&]{value=7;});group.wait();require(value==7,"zero-worker pool runs on the waiter");
        auto stats=none.snapshot(false);require(stats.workers==0&&stats.kinds[sevk_jobs::Spot].jobs==1&&stats.kinds[sevk_jobs::Spot].helped==1,"zero-worker telemetry");
    }
    {   // High priority jobs start before queued normal ones.
        sevk_jobs::System one(1);std::mutex gate;std::unique_lock hold(gate);std::vector<int> order;std::mutex order_mutex;
        sevk_jobs::Group blocker,normal,high;
        one.submit(blocker,sevk_jobs::Spot,sevk_jobs::Priority::Normal,[&]{std::lock_guard g(gate);});
        // Let the only worker take the blocker, then queue the rest behind it.
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
        one.submit(normal,sevk_jobs::Cascade,sevk_jobs::Priority::Normal,[&]{std::lock_guard g(order_mutex);order.push_back(1);});
        one.submit(high,sevk_jobs::PassView,sevk_jobs::Priority::High,[&]{std::lock_guard g(order_mutex);order.push_back(0);});
        hold.unlock();blocker.wait();
        while(normal.busy()||high.busy())std::this_thread::yield();
        require(order==std::vector<int>({0,1}),"high priority runs first");
        auto stats=one.snapshot(true);require(stats.kinds[sevk_jobs::Cascade].jobs==1&&stats.kinds[sevk_jobs::Cascade].max_wait_ns>0,"wait telemetry");
        require(one.snapshot(false).kinds[sevk_jobs::Cascade].max_wait_ns==0,"reset clears the maximum");
    }
    {   // ViewJobs and Background keep their contracts on the shared pool.
        native_pass::ViewJobs jobs;std::vector<uint32_t> out(100);jobs.run(100,[&](uint32_t i){out[i]=i;});
        require(std::accumulate(out.begin(),out.end(),0u)==4950,"view jobs");
        native_pass::Background background;uint32_t value=0;background.start([&]{value=3;});background.wait();require(value==3&&!background.pending(),"background");
        bool threw=false;background.start([]{throw std::runtime_error("b");});try{background.wait();}catch(const std::runtime_error&){threw=true;}require(threw,"background failure");
    }
    {   // Stage threads run one task at a time and are counted.
        uint32_t before=sevk_jobs::shared().snapshot(false).stage_threads;
        {sevk_jobs::Stage stage;require(sevk_jobs::shared().snapshot(false).stage_threads==before+1,"stage counted");
            int value=0;stage.start([&]{value=1;});stage.start([&]{value+=1;});stage.wait();require(value==2,"stage serializes");}
        require(sevk_jobs::shared().snapshot(false).stage_threads==before,"stage released");
    }
    std::cout<<"Job system: fan-out, nesting, failures, priorities, telemetry and stages passed\n";
}
