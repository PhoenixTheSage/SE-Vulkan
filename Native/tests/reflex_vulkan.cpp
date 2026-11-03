#include "../src/reflex_vulkan.h"
#include <array>
#include <chrono>
#include <condition_variable>
#include <future>
#include <iostream>
#include <stdexcept>
#include <type_traits>

namespace {
int checks{};
void require(bool value,const char* message){if(!value)throw std::runtime_error(message);++checks;}
template<class T>T handle(uintptr_t value){if constexpr(std::is_pointer_v<T>)return reinterpret_cast<T>(value);else return T(value);}
VkDevice dev=handle<VkDevice>(1);VkSwapchainKHR chain=handle<VkSwapchainKHR>(2);VkSemaphore sem=handle<VkSemaphore>(3);
std::atomic<int> creates{},destroys{},marks{},waits{},sets{},disables{};
std::atomic<uint64_t> last_value{};
std::atomic<bool> expecting_reset{};
int timing_queries{};
VKAPI_ATTR void VKAPI_CALL timings(VkDevice device,VkSwapchainKHR swap,VkGetLatencyMarkerInfoNV* info){
    if(device!=dev||swap!=chain||info->sType!=VK_STRUCTURE_TYPE_GET_LATENCY_MARKER_INFO_NV||info->pNext||info->timingCount!=64||!info->pTimings)
        throw std::runtime_error("wrong latency query packet");
    for(uint32_t i=0;i<64;++i){const auto& f=info->pTimings[i];
        if(f.sType!=VK_STRUCTURE_TYPE_LATENCY_TIMINGS_FRAME_REPORT_NV||f.pNext||f.presentID||f.simStartTimeUs||f.gpuRenderEndTimeUs)
            throw std::runtime_error("latency outputs must be initialized with correct sType and zero data");}
    ++timing_queries;info->pTimings[0].presentID=41;info->pTimings[0].simStartTimeUs=100;
    info->pTimings[1].presentID=42;info->pTimings[1].gpuRenderEndTimeUs=200;info->timingCount=2;
}
VkResult set_result=VK_SUCCESS,create_result=VK_SUCCESS,sleep_result=VK_SUCCESS;
uint32_t expected_mode{};uint64_t expected_frame=42;uint32_t expected_marker=2;
std::mutex gate;std::condition_variable cv;bool block_wait{},inside_wait{},release_wait{};
bool block_off{},inside_off{},release_off{};

VKAPI_ATTR VkResult VKAPI_CALL set_mode(VkDevice device,VkSwapchainKHR swap,const VkLatencySleepModeInfoNV* info){
    if(device!=dev||swap!=chain)throw std::runtime_error("wrong mode device/swapchain");
    if(expecting_reset){
        if(!info||info->sType!=VK_STRUCTURE_TYPE_LATENCY_SLEEP_MODE_INFO_NV||info->pNext||info->lowLatencyMode||info->lowLatencyBoost||info->minimumIntervalUs)
            throw std::runtime_error("reset requires explicit fully disabled mode packet");
        ++disables;
        {std::unique_lock lock(gate);if(block_off){inside_off=true;cv.notify_all();if(!cv.wait_for(lock,std::chrono::seconds(5),[]{return release_off;}))throw std::runtime_error("Off fixture timed out");}}
        return set_result;
    }
    if(!info)throw std::runtime_error("mode-info pointer must be explicit");
    if(info->sType!=VK_STRUCTURE_TYPE_LATENCY_SLEEP_MODE_INFO_NV||info->pNext||
       info->lowLatencyMode!=(expected_mode!=0)||info->lowLatencyBoost!=(expected_mode==2)||info->minimumIntervalUs)
        throw std::runtime_error("wrong mode packet");
    ++sets;return set_result;
}
VKAPI_ATTR VkResult VKAPI_CALL create_sem(VkDevice device,const VkSemaphoreCreateInfo* info,const VkAllocationCallbacks*,VkSemaphore* out){
    const auto* type=static_cast<const VkSemaphoreTypeCreateInfo*>(info->pNext);
    if(device!=dev||!type||type->sType!=VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO||type->semaphoreType!=VK_SEMAPHORE_TYPE_TIMELINE||type->initialValue)
        throw std::runtime_error("sleep needs fresh timeline semaphore");
    ++creates;*out=sem;return create_result;
}
VKAPI_ATTR void VKAPI_CALL destroy_sem(VkDevice device,VkSemaphore semaphore,const VkAllocationCallbacks*){
    if(device!=dev||semaphore!=sem)throw std::runtime_error("wrong semaphore destroyed");++destroys;
}
VKAPI_ATTR VkResult VKAPI_CALL sleep(VkDevice device,VkSwapchainKHR swap,const VkLatencySleepInfoNV* info){
    if(device!=dev||swap!=chain||info->sType!=VK_STRUCTURE_TYPE_LATENCY_SLEEP_INFO_NV||info->pNext||info->signalSemaphore!=sem||info->value<=last_value.load())
        throw std::runtime_error("sleep must use increasing timeline values");
    last_value=info->value;return sleep_result;
}
VKAPI_ATTR VkResult VKAPI_CALL wait(VkDevice device,const VkSemaphoreWaitInfo* info,uint64_t timeout){
    if(device!=dev||info->semaphoreCount!=1||info->pSemaphores[0]!=sem||info->pValues[0]!=last_value||timeout!=UINT64_MAX)
        throw std::runtime_error("sleep wait does not match signal");
    ++waits;
    std::unique_lock lock(gate);inside_wait=true;cv.notify_all();
    if(block_wait&&!cv.wait_for(lock,std::chrono::seconds(5),[]{return release_wait;}))throw std::runtime_error("wait test timed out");
    return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL marker(VkDevice device,VkSwapchainKHR swap,const VkSetLatencyMarkerInfoNV* info){
    if(device!=dev||swap!=chain||info->sType!=VK_STRUCTURE_TYPE_SET_LATENCY_MARKER_INFO_NV||info->pNext||info->presentID!=expected_frame||uint32_t(info->marker)!=expected_marker)
        throw std::runtime_error("wrong marker identity or enum");
    ++marks;
}
sevk_reflex::Functions functions(){return {set_mode,sleep,marker,create_sem,destroy_sem,wait,timings};}
}

void attribution_checks(){
    using namespace sevk_reflex;
    const void* owner=reinterpret_cast<void*>(10);const void* other=reinterpret_cast<void*>(11);
    Output a{7,41,0,2},b{7,42,3,2};
    bind_output(owner,a);
    require(output_for(owner)==a&&output_for(other)==Output{},"output scope respects backend owner");
    {OutputScope scope(other,b);require(output_for(other)==b&&output_for(owner)==Output{},"nested output binding isolates other backend");}
    require(output_for(owner)==a,"scope restores prior owner and complete tuple");
    auto worker=std::async(std::launch::async,[=]{require(output_for(owner)==Output{},"worker never inherits mutable thread context");OutputScope scope(owner,a);return output_for(owner);});
    require(worker.get()==a,"worker consumes a value-captured output token");bind_output(owner,{});
    SubmissionAttribution tags;auto q1=handle<VkQueue>(21),q2=handle<VkQueue>(22);
    VkSubmitInfo2 info{VK_STRUCTURE_TYPE_SUBMIT_INFO_2};int sentinel=3;info.pNext=&sentinel;
    int calls=0;uint64_t expected=41;
    auto submit=[&](const VkSubmitInfo2& actual){++calls;auto* tag=static_cast<const VkLatencySubmissionPresentIdNV*>(actual.pNext);
        require(tag&&tag->sType==VK_STRUCTURE_TYPE_LATENCY_SUBMISSION_PRESENT_ID_NV&&tag->presentID==expected&&tag->pNext==&sentinel,"executing submit owns local tag and preserves existing chain");return VK_SUCCESS;};
    tags.bind(dev,2);
    require(tags.submit(q1,info,41,[&](const VkSubmitInfo2& actual){return actual.pNext==&sentinel?VK_SUCCESS:VK_ERROR_UNKNOWN;})==VK_SUCCESS,"revision2 leaves submission unchanged");
    tags.bind(dev,3);require(tags.submit(q1,info,41,submit)==VK_SUCCESS,"first queue accepts parent");
    expected=42;require(tags.submit(q1,info,42,submit)==VK_SUCCESS&&tags.submit(q1,info,42,submit)==VK_SUCCESS,"queue permits increasing parent and repeated output");
    int before=calls;require(tags.submit(q1,info,41,submit)==VK_ERROR_VALIDATION_FAILED_EXT&&calls==before,"decreasing queue ID is refused before driver dispatch");
    expected=40;require(tags.submit(q2,info,40,submit)==VK_SUCCESS,"separate physical queue has independent ID history");
    int clears=0;auto clear=[&](VkQueue queue,const VkSubmitInfo2& actual){auto* tag=static_cast<const VkLatencySubmissionPresentIdNV*>(actual.pNext);
        require((queue==q1||queue==q2)&&tag&&tag->presentID==0&&!tag->pNext&&actual.commandBufferInfoCount==0,"queue clearing is an explicit empty zero-ID submission");++clears;return VK_SUCCESS;};
    require(tags.clear(clear)==VK_SUCCESS&&clears==2,"clear includes both active and dormant tagged queues");
    require(tags.clear(clear)==VK_SUCCESS&&clears==2,"already cleared queues need no repeated clear");
    tags.bind(dev,3);require(tags.submit(q1,info,41,submit)==VK_ERROR_VALIDATION_FAILED_EXT,"same-device rebind preserves monotonic history after clear");
    expected=0;require(tags.submit(q1,info,0,submit)==VK_SUCCESS,"untagged work explicitly clears prior queue context");
    tags.bind(handle<VkDevice>(99),3);expected=1;require(tags.submit(q1,info,1,submit)==VK_SUCCESS,"new logical device starts independent queue history");
    expected=2;require(tags.submit(q1,info,2,[&](const VkSubmitInfo2&){return VK_ERROR_DEVICE_LOST;})==VK_ERROR_DEVICE_LOST,"native submit failures propagate");
    require(tags.clear(clear)==VK_SUCCESS&&clears==3,"failed attempted attribution still retires conservatively");
    expected=8;require(tags.submit(q1,info,8,submit)==VK_SUCCESS,"sticky-state regression starts with successful positive tag");
    require(tags.submit(q1,info,0,[](const VkSubmitInfo2&){return VK_ERROR_OUT_OF_HOST_MEMORY;})==VK_ERROR_OUT_OF_HOST_MEMORY,"failed zero submission propagates native error");
    require(tags.clear(clear)==VK_SUCCESS&&clears==4,"failed zero retains sticky state for subsequent explicit clear");
}

int main(){try{
    attribution_checks();
    {
        sevk_reflex::Session query;
        std::array<VkLatencyTimingsFrameReportNV,64> reports{};uint32_t count=99;
        require(query.timings(0,reports,count)==VK_ERROR_OUT_OF_DATE_KHR&&count==0,"missing timing epoch rejected and output cleared");
        require(query.bind(dev,chain,1,functions()),"revision1 ordinary binding remains available");
        require(query.timings(query.state().epoch,reports,count)==VK_ERROR_EXTENSION_NOT_PRESENT&&timing_queries==0,"revision1 never calls revision2 timing ABI");
        auto optional=functions();optional.timings=nullptr;
        require(query.bind(dev,chain,3,optional)&&query.timings(query.state().epoch,reports,count)==VK_ERROR_EXTENSION_NOT_PRESENT,"missing optional timing entry point does not disable binding");
        require(query.bind(dev,chain,3,functions()),"timing-capable binding installed");
        auto epoch=query.state().epoch;
        require(query.timings(epoch,reports,count)==VK_SUCCESS&&count==2&&reports[0].presentID==41&&reports[1].presentID==42&&reports[1].gpuRenderEndTimeUs==200,"initialized timing query preserves returned ordering and timestamps");
        query.reset();
        require(query.timings(epoch,reports,count)==VK_ERROR_OUT_OF_DATE_KHR&&count==0&&reports[1].presentID==0&&timing_queries==1,"retired timing epoch cannot call driver or return old data");
    }
    std::array<VkExtensionProperties,2> extensions{};
    std::strcpy(extensions[0].extensionName,VK_NV_LOW_LATENCY_2_EXTENSION_NAME);extensions[0].specVersion=2;
    std::strcpy(extensions[1].extensionName,VK_KHR_PRESENT_ID_EXTENSION_NAME);extensions[1].specVersion=1;
    auto plan=sevk_reflex::device_plan(extensions,true,true,true);
    require(plan.enabled&&!plan.explicit_attribution(),"revision2 supports implicit, not explicit attribution");
    extensions[0].specVersion=3;plan=sevk_reflex::device_plan(extensions,true,true,true);
    require(plan.enabled&&plan.explicit_attribution(),"driver revision3 permits explicit attribution");
    require(!sevk_reflex::device_plan(extensions,false,true,true).enabled,"headless device is not a Reflex swapchain");
    require(!sevk_reflex::device_plan(extensions,true,false,true).enabled,"timeline support required");
    require(!sevk_reflex::device_plan(extensions,true,true,false).enabled,"present ID feature required");
    require(!sevk_reflex::device_plan(std::span(extensions).first(1),true,true,true).enabled,"present ID extension required");
    extensions[0].specVersion=1;
    require(sevk_reflex::device_plan(extensions,true,true,true).enabled,"revision1 supports mode/sleep/markers without timings queries");

    sevk_reflex::Session session;session.validation_host_gate(true);
    require(!session.state().bound&&session.state().configured_mode==UINT32_MAX,"new session has no applied mode");
    require(session.configure(1)==VK_ERROR_EXTENSION_NOT_PRESENT,"unbound configure refused");
    require(session.sleep()==VK_ERROR_EXTENSION_NOT_PRESENT,"unbound sleep refused");
    require(!session.bind(dev,chain,0,functions()),"missing extension revision refused");
    require(!session.bind(dev,chain,2,{}),"incomplete entry points refused");
    require(session.bind(dev,chain,2,functions()),"complete revision2 binding");
    require(session.sleep()==VK_ERROR_INITIALIZATION_FAILED&&creates==0,"sleep waits for explicit host configuration");
    for(uint32_t mode=0;mode<3;++mode){expected_mode=mode;require(session.configure(mode)==VK_SUCCESS&&session.state().configured_mode==mode,"accepted mode retained");}
    require(session.configure(3)==VK_ERROR_INITIALIZATION_FAILED&&sets==3,"invalid mode does not reach driver");
    set_result=VK_ERROR_UNKNOWN;
    require(session.configure(2)==VK_ERROR_UNKNOWN&&session.state().configured_mode==2,"configuration error preserves last accepted mode");
    set_result=VK_SUCCESS;
    require(session.mark(0,2)==VK_ERROR_INITIALIZATION_FAILED&&session.mark(42,12)==VK_ERROR_INITIALIZATION_FAILED&&marks==0,"invalid frame and Vulkan marker refused");
    require(session.mark(expected_frame,expected_marker)==VK_SUCCESS&&marks==1,"native marker packet carries exact identity");
    create_result=VK_ERROR_OUT_OF_HOST_MEMORY;
    require(session.sleep()==VK_ERROR_OUT_OF_HOST_MEMORY&&waits==0,"semaphore creation failure never sleeps or waits");
    create_result=VK_SUCCESS;
    require(session.sleep()==VK_SUCCESS&&creates==2&&waits==1,"failed semaphore output not reused");
    require(session.sleep()==VK_SUCCESS&&creates==2&&waits==2&&session.state().sleeps==2,"timeline semaphore reused with increasing value");
    sleep_result=VK_ERROR_UNKNOWN;
    require(session.sleep()==VK_ERROR_UNKNOWN&&waits==2&&session.state().sleeps==2,"failed sleep never waits on unsignaled value");
    sleep_result=VK_SUCCESS;

    {std::lock_guard lock(gate);block_wait=true;inside_wait=false;release_wait=false;}
    auto sleeper=std::async(std::launch::async,[&]{return session.sleep();});
    {std::unique_lock lock(gate);require(cv.wait_for(lock,std::chrono::seconds(5),[]{return inside_wait;}),"sleep reached native semaphore wait");}
    require(session.mark(expected_frame,expected_marker)==VK_SUCCESS,"render marker can progress during pre-input sleep");
    expecting_reset=true;
    auto reset=std::async(std::launch::async,[&]{session.reset();});
    const bool reset_waited=reset.wait_for(std::chrono::milliseconds(30))==std::future_status::timeout&&destroys==0;
    {std::lock_guard lock(gate);release_wait=true;}cv.notify_all();
    require(sleeper.get()==VK_SUCCESS,"sleep completes before retirement");reset.get();
    require(reset_waited&&destroys==1&&disables==1,"retirement waits for sleep before disabling and destroying");
    require(!session.state().bound&&session.state().sleeps==0&&session.state().markers==0,"retired session clears device counters");
    session.reset();require(destroys==1&&disables==1,"retirement is idempotent");
    const auto old_epoch=session.state().epoch;last_value=0;
    require(session.bind(dev,chain,3,functions())&&session.state().epoch>old_epoch,"new swapchain epoch never reuses retired identity");
    require(session.state().configured_mode==UINT32_MAX,"new epoch does not imply old mode applied");
    const auto old_sets=sets.load(),old_marks=marks.load(),old_creates=creates.load();
    require(session.configure(2,old_epoch)==VK_ERROR_OUT_OF_DATE_KHR,"retired configure epoch refused");
    require(session.sleep(old_epoch)==VK_ERROR_OUT_OF_DATE_KHR,"retired sleep epoch refused");
    require(session.mark(expected_frame,expected_marker,old_epoch)==VK_ERROR_OUT_OF_DATE_KHR,"retired marker epoch refused");
    require(sets==old_sets&&marks==old_marks&&creates==old_creates,"retired commands never reach Vulkan driver");
    auto prior_incarnation=session.state().epoch;
    {sevk_reflex::Session replacement;require(replacement.bind(dev,chain,3,functions())&&replacement.state().epoch>prior_incarnation,"new context incarnation cannot reuse an old swapchain epoch");}
    std::promise<void> entering;auto entered=entering.get_future();std::future<VkResult> blocked_marker;
    session.validation_host_gate(true);
    session.host_call([&]{
        blocked_marker=std::async(std::launch::async,[&]{entering.set_value();return session.mark(expected_frame,expected_marker);});
        require(entered.wait_for(std::chrono::seconds(5))==std::future_status::ready,"marker thread reached swapchain host entry");
        require(blocked_marker.wait_for(std::chrono::milliseconds(30))==std::future_status::timeout&&marks==old_marks,"driver marker waits for an active swapchain host call");
    });
    require(blocked_marker.get()==VK_SUCCESS&&marks==old_marks+1,"marker progresses immediately after swapchain host call returns");
    session.validation_host_gate(false);
    session.host_call([&]{auto marker=std::async(std::launch::async,[&]{return session.mark(expected_frame,expected_marker);});
        require(marker.wait_for(std::chrono::seconds(5))==std::future_status::ready&&marker.get()==VK_SUCCESS,"production host calls do not exclude legal concurrent markers");});
    const auto retiring_epoch=session.state().epoch;
    set_result=VK_ERROR_UNKNOWN;
    require(session.retire(retiring_epoch)==VK_ERROR_UNKNOWN&&session.state().epoch==retiring_epoch&&session.state().bound,"failed checked Off cannot claim epoch retirement");
    set_result=VK_SUCCESS;
    {std::lock_guard lock(gate);block_off=true;inside_off=false;release_off=false;}
    auto retiring=std::async(std::launch::async,[&]{return session.retire(retiring_epoch);});
    {std::unique_lock lock(gate);require(cv.wait_for(lock,std::chrono::seconds(5),[]{return inside_off;}),"retirement entered its single checked Off call");}
    auto old_configure=std::async(std::launch::async,[&]{return session.configure(1,retiring_epoch);});
    const bool configure_waited=old_configure.wait_for(std::chrono::milliseconds(30))==std::future_status::timeout;
    {std::lock_guard lock(gate);release_off=true;}cv.notify_all();
    require(retiring.get()==VK_SUCCESS&&configure_waited&&old_configure.get()==VK_ERROR_OUT_OF_DATE_KHR,"atomic retirement excludes and rejects intervening old-epoch configure");
    require(session.state().bound&&session.state().epoch>retiring_epoch&&session.state().configured_mode==UINT32_MAX&&session.state().markers==0,"retirement retains borrowed device with fresh unconfigured epoch and counters");
    std::cout<<"Passed "<<checks<<" Vulkan Reflex binding checks; no game latency claim.\n";
    return 0;
}catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
