#pragma once
#include <vulkan/vulkan.h>
#include <atomic>
#include <algorithm>
#include <array>
#include <cstring>
#include <limits>
#include <mutex>
#include <shared_mutex>
#include <span>
#include "reflex_attribution.h"

// Official VK_NV_low_latency2 binding. Anomaly owns policy and frame identity;
// this renderer owns the device, swapchain and latency semaphore. This binding
// alone does not establish correct game-loop placement or measured latency.
namespace sevk_reflex {
struct DevicePlan {
    uint32_t version{};
    bool enabled{};
    bool explicit_attribution() const { return enabled && version >= 3; }
};
inline DevicePlan device_plan(std::span<const VkExtensionProperties> extensions,
                             bool surface, bool timeline, bool present_id_feature) {
    DevicePlan result;
    bool present_id=false;
    for(const auto& e:extensions) {
        if(std::strcmp(e.extensionName,VK_NV_LOW_LATENCY_2_EXTENSION_NAME)==0)result.version=e.specVersion;
        if(std::strcmp(e.extensionName,VK_KHR_PRESENT_ID_EXTENSION_NAME)==0)present_id=true;
    }
    // Mode/sleep/marker entry points are available from revision 1. Timing
    // queries need their separate revision-2 ABI. Explicit
    // VkLatencySubmissionPresentIdNV needs driver revision 3.
    result.enabled=surface&&timeline&&present_id&&present_id_feature&&result.version>=1;
    return result;
}

struct Functions {
    PFN_vkSetLatencySleepModeNV set_mode{};
    PFN_vkLatencySleepNV sleep{};
    PFN_vkSetLatencyMarkerNV marker{};
    PFN_vkCreateSemaphore create_semaphore{};
    PFN_vkDestroySemaphore destroy_semaphore{};
    PFN_vkWaitSemaphores wait_semaphores{};
    PFN_vkGetLatencyTimingsNV timings{}; // Optional; never gates mode/sleep/markers.
    bool complete() const { return set_mode&&sleep&&marker&&create_semaphore&&destroy_semaphore&&wait_semaphores; }
};
struct State {
    bool bound{};
    uint32_t version{},configured_mode=UINT32_MAX;
    VkResult result=VK_SUCCESS;
    uint64_t epoch{},sleeps{},markers{};
};

class Session {
    static inline std::atomic<uint64_t> next_epoch{};
    mutable std::shared_mutex lifetime;
    std::mutex sleep_gate;
    std::mutex host_gate;
    std::atomic<bool> validation_host_calls{};
    VkDevice device{};
    VkSwapchainKHR swapchain{};
    VkSemaphore semaphore{};
    Functions functions{};
    uint64_t value{},epoch{};
    uint32_t version{},configured_mode=UINT32_MAX;
    std::atomic<uint64_t> sleeps{},markers{};
    std::atomic<VkResult> last_result{VK_SUCCESS};

    void reset_locked() {
        if(device&&swapchain&&configured_mode!=UINT32_MAX&&functions.set_mode) {
            // Explicit Off is equivalent to the spec's optional null reset,
            // and does not require a driver to handle a null mode-info pointer.
            VkLatencySleepModeInfoNV off{VK_STRUCTURE_TYPE_LATENCY_SLEEP_MODE_INFO_NV};
            last_result=host_call([&]{return functions.set_mode(device,swapchain,&off);});
        }
        // A shared Sleep lease includes its semaphore wait, so exclusive reset
        // cannot destroy a semaphore while vkLatencySleepNV is still signaling it.
        if(semaphore&&functions.destroy_semaphore)functions.destroy_semaphore(device,semaphore,nullptr);
        device={};swapchain={};semaphore={};functions={};value=0;version=0;configured_mode=UINT32_MAX;
        // Process-unique even if a new Renderer reuses an old context address.
        sleeps=0;markers=0;epoch=next_epoch.fetch_add(1,std::memory_order_relaxed)+1;
    }
public:
    Session()=default;
    Session(const Session&)=delete;
    Session& operator=(const Session&)=delete;
    // Renderer must call reset before destroying its swapchain/device.
    ~Session(){reset();}

    // Serialize individual swapchain host entry points for VVL 1.4.321's
    // conservative read/write tracking of latency markers versus acquire/
    // present. The spec permits their overlap. Never hold this over a semaphore
    // wait, command recording, GPU submission or an asynchronous handoff.
    template<class Fn> auto host_call(Fn&& fn) {
        std::unique_lock guard(host_gate,std::defer_lock);
        if(validation_host_calls.load(std::memory_order_relaxed))guard.lock();
        return fn();
    }
    void validation_host_gate(bool enabled){validation_host_calls.store(enabled,std::memory_order_relaxed);}

    bool bind(VkDevice dev,VkSwapchainKHR chain,uint32_t revision,Functions api) {
        std::unique_lock lock(lifetime);
        reset_locked();
        if(!dev||!chain||revision<1||!api.complete()){last_result=VK_ERROR_EXTENSION_NOT_PRESENT;return false;}
        device=dev;swapchain=chain;version=revision;functions=api;last_result=VK_SUCCESS;
        return true;
    }
    void reset(){std::unique_lock lock(lifetime);reset_locked();}
    VkResult retire(uint64_t expected_epoch) {
        std::unique_lock lock(lifetime);
        if(!expected_epoch||expected_epoch!=epoch)return VK_ERROR_OUT_OF_DATE_KHR;
        if(!device)return VK_ERROR_EXTENSION_NOT_PRESENT;
        VkLatencySleepModeInfoNV off{VK_STRUCTURE_TYPE_LATENCY_SLEEP_MODE_INFO_NV};
        auto result=host_call([&]{return functions.set_mode(device,swapchain,&off);});
        last_result=result;if(result!=VK_SUCCESS)return result;
        if(semaphore)functions.destroy_semaphore(device,semaphore,nullptr);
        semaphore={};value=0;configured_mode=UINT32_MAX;sleeps=0;markers=0;
        epoch=next_epoch.fetch_add(1,std::memory_order_relaxed)+1;
        return VK_SUCCESS;
    }

    VkResult configure(uint32_t mode,uint64_t expected_epoch=0) {
        std::unique_lock lock(lifetime);
        if(mode>2)return VK_ERROR_INITIALIZATION_FAILED;
        if(!device)return VK_ERROR_EXTENSION_NOT_PRESENT;
        if(expected_epoch&&expected_epoch!=epoch)return VK_ERROR_OUT_OF_DATE_KHR;
        VkLatencySleepModeInfoNV info{VK_STRUCTURE_TYPE_LATENCY_SLEEP_MODE_INFO_NV};
        info.lowLatencyMode=mode!=0;info.lowLatencyBoost=mode==2;
        // Zero interval preserves the host/FG pacing policy; this is not an FPS limiter.
        const auto result=host_call([&]{return functions.set_mode(device,swapchain,&info);});
        if(result==VK_SUCCESS)configured_mode=mode;
        last_result=result;return result;
    }

    VkResult sleep(uint64_t expected_epoch=0) {
        std::shared_lock lock(lifetime);
        if(!device)return VK_ERROR_EXTENSION_NOT_PRESENT;
        if(expected_epoch&&expected_epoch!=epoch)return VK_ERROR_OUT_OF_DATE_KHR;
        if(configured_mode==UINT32_MAX)return VK_ERROR_INITIALIZATION_FAILED;
        std::lock_guard serial(sleep_gate);
        if(value==std::numeric_limits<uint64_t>::max())return VK_ERROR_TOO_MANY_OBJECTS;
        if(!semaphore) {
            VkSemaphoreTypeCreateInfo type{VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO};type.semaphoreType=VK_SEMAPHORE_TYPE_TIMELINE;
            VkSemaphoreCreateInfo create{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO,&type};
            VkSemaphore created{};
            auto result=functions.create_semaphore(device,&create,nullptr,&created);
            if(result!=VK_SUCCESS){last_result=result;return result;}
            semaphore=created;
        }
        VkLatencySleepInfoNV info{VK_STRUCTURE_TYPE_LATENCY_SLEEP_INFO_NV};info.signalSemaphore=semaphore;info.value=++value;
        auto result=host_call([&]{return functions.sleep(device,swapchain,&info);});
        if(result==VK_SUCCESS) {
            VkSemaphoreWaitInfo wait{VK_STRUCTURE_TYPE_SEMAPHORE_WAIT_INFO};wait.semaphoreCount=1;wait.pSemaphores=&semaphore;wait.pValues=&value;
            result=functions.wait_semaphores(device,&wait,UINT64_MAX);
        }
        if(result==VK_SUCCESS)++sleeps;
        last_result=result;return result;
    }

    VkResult mark(uint64_t frame_id,uint32_t marker,uint64_t expected_epoch=0) {
        std::shared_lock lock(lifetime);
        if(!frame_id||marker>11)return VK_ERROR_INITIALIZATION_FAILED;
        if(!device)return VK_ERROR_EXTENSION_NOT_PRESENT;
        if(expected_epoch&&expected_epoch!=epoch)return VK_ERROR_OUT_OF_DATE_KHR;
        VkSetLatencyMarkerInfoNV info{VK_STRUCTURE_TYPE_SET_LATENCY_MARKER_INFO_NV};
        info.presentID=frame_id;info.marker=static_cast<VkLatencyMarkerNV>(marker);
        host_call([&]{functions.marker(device,swapchain,&info);});++markers;
        return VK_SUCCESS; // Vulkan marker entry point is void, not an acknowledgement.
    }

    VkResult timings(uint64_t expected_epoch,std::array<VkLatencyTimingsFrameReportNV,64>& reports,uint32_t& count) {
        count=0;reports={};
        std::shared_lock lock(lifetime);
        if(!expected_epoch||expected_epoch!=epoch)return VK_ERROR_OUT_OF_DATE_KHR;
        if(!device||version<2||!functions.timings)return VK_ERROR_EXTENSION_NOT_PRESENT;
        for(auto& report:reports)report.sType=VK_STRUCTURE_TYPE_LATENCY_TIMINGS_FRAME_REPORT_NV;
        VkGetLatencyMarkerInfoNV info{VK_STRUCTURE_TYPE_GET_LATENCY_MARKER_INFO_NV};
        info.timingCount=uint32_t(reports.size());info.pTimings=reports.data();
        host_call([&]{functions.timings(device,swapchain,&info);});
        count=std::min(info.timingCount,uint32_t(reports.size()));
        return VK_SUCCESS; // Void query: execution, not driver mode acknowledgement.
    }

    State state() const {
        std::shared_lock lock(lifetime);
        return {device!=VK_NULL_HANDLE,version,configured_mode,last_result.load(),epoch,sleeps.load(),markers.load()};
    }
};
// One execution-thread CPU marker region; explicit submission attribution is
// independent and is applied where each queue API call actually executes.
class MarkerRegion {
    Session& session;
    Output output;
    uint32_t end;
    bool open{};
public:
    MarkerRegion(Session& owner,Output token,uint32_t begin,uint32_t finish):session(owner),output(token),end(finish) {
        if(output.parent)open=session.mark(output.parent,begin,output.epoch)==VK_SUCCESS;
    }
    void close(){if(open){open=false;session.mark(output.parent,end,output.epoch);}}
    ~MarkerRegion(){close();}
    MarkerRegion(const MarkerRegion&)=delete;
    MarkerRegion& operator=(const MarkerRegion&)=delete;
};
}
