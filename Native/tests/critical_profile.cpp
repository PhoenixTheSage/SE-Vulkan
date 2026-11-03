#include "se_vulkan.h"
#include "../src/critical_profile.h"
#include <future>
#include <iostream>
#include <thread>

using namespace critical_profile;

static void require(bool ok,const char* message){if(!ok)throw std::runtime_error(message);}

int main(){
    State state;
    auto first=state.begin(1);
    {Scope build(PassPrepare);}
    state.detach();
    // Stage B opens frame 1 on its launch thread.
    std::thread launch([&]{state.launch(first);{Scope queued(QueueOwner);}state.open_recording();});
    launch.join();
    std::promise<void> recording_started,allow_submit;
    auto started=recording_started.get_future(),submit=allow_submit.get_future();
    std::thread recording([&]{
        Finish finish{state};
        Bind owner(state.record_owner());Scope record(RecordOwner);
        recording_started.set_value();submit.wait();
        api(CommandApi,[]{});
        std::thread helper([&]{Bind worker(state.record_worker(8));Scope work(SpotRecord);});
        helper.join();finish.submitted=true;
    });
    started.wait();
    auto second=state.begin(1);
    {Scope build(PassPublish);}
    state.detach();
    allow_submit.set_value();recording.join();
    auto after_first=state.snapshot();
    require(after_first.begun==2&&after_first.selected==1&&after_first.completed==1,"overlap changed submitted frame count");
    for(auto k:{PassPrepare,QueueOwner,RecordOwner,CommandApi,SpotRecord})
        require(after_first.rows[k].calls==1,"lost build, launch, recording or worker lane");
    require(after_first.rows[PassPublish].calls==0,"future build leaked into prior recording");
    state.launch(second);state.open_recording();
    {Finish finish{state};Bind owner(state.record_owner());Scope record(RecordOwner);finish.submitted=true;}
    auto final=state.snapshot();
    require(final.rows[PassPublish].calls==1&&final.rows[RecordOwner].calls==2,"second submitted frame lost its own profile");
    auto skipped=state.begin(1);state.detach();state.launch(skipped);state.discard(skipped);
    state.open_recording();state.finish(true);
    require(state.snapshot().completed==2&&!lane,"unopened launch counted or retained its lane");
    std::cout<<"Critical profile preserves build, launch, worker and recording lanes across overlapping frames.\n";
}
