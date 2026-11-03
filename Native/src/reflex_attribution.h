#pragma once
#include <vulkan/vulkan.h>
#include <atomic>
#include <map>
#include <mutex>
#include <shared_mutex>

namespace sevk_reflex {
// Immutable application input parent plus output identity. The output ordinal
// discriminates repeats; it is never substituted for the latency presentID.
struct Output {
    uint64_t epoch{},parent{},ordinal{},generation{};
    bool operator==(const Output&)const=default;
};
struct Binding {const void* owner{};Output output{};};
inline thread_local Binding output_binding;
inline Output output_for(const void* owner) {return output_binding.owner==owner?output_binding.output:Output{};}
inline void bind_output(const void* owner,Output output) {output_binding={owner,output};}
class OutputScope {
    Binding previous;
public:
    OutputScope(const void* owner,Output output):previous(output_binding){bind_output(owner,output);}
    ~OutputScope(){output_binding=previous;}
    OutputScope(const OutputScope&)=delete;
    OutputScope& operator=(const OutputScope&)=delete;
};

// Physical queue history belongs to the logical device, not a swapchain epoch.
// Calls construct the extension locally on the executing thread. Distinct
// queues keep independent gates, retaining frame/present queue overlap.
class SubmissionAttribution {
    struct Queue {std::mutex gate;uint64_t maximum{},tracked{};};
    mutable std::shared_mutex lifetime;
    std::mutex map_gate;
    std::map<VkQueue,Queue> queues;
    VkDevice device{};
    bool enabled{};
    std::atomic<bool> used{};
public:
    void bind(VkDevice next,uint32_t revision) {
        std::unique_lock lock(lifetime);
        if(device!=next){queues.clear();used=false;device=next;}
        enabled=next&&revision>=3;
    }

    template<class Submit> VkResult submit(VkQueue queue,VkSubmitInfo2 info,uint64_t parent,Submit&& call) {
        std::shared_lock lock(lifetime);
        if(!enabled)return call(info);
        if(!parent&&!used.load(std::memory_order_acquire))return call(info);
        Queue* state;
        {std::lock_guard map(map_gate);state=&queues[queue];}
        std::lock_guard serial(state->gate);
        // Reject a programmer ownership error before passing an illegal ID to
        // the driver. In particular, clearing the tracked ID does not erase the
        // historical monotonicity requirement on this physical queue.
        if(parent&&parent<state->maximum)return VK_ERROR_VALIDATION_FAILED_EXT;
        if(parent){state->maximum=parent;used.store(true,std::memory_order_release);}
        VkLatencySubmissionPresentIdNV latency{VK_STRUCTURE_TYPE_LATENCY_SUBMISSION_PRESENT_ID_NV};
        latency.pNext=info.pNext;latency.presentID=parent;info.pNext=&latency;
        if(parent)state->tracked=parent; // Conservatively clear a failed positive attempt.
        auto result=call(info);
        if(result==VK_SUCCESS)state->tracked=parent; // A failed zero must retain the old tracked ID.
        return result;
    }

    // Caller drains recording/FIFO/present workers and owns every queue first.
    // Include dormant queues: omission would leave device-wide explicit mode
    // sticky after resize, route changes or owner release.
    template<class Submit> VkResult clear(Submit&& call) {
        std::unique_lock lock(lifetime);
        if(!enabled||!used.load())return VK_SUCCESS;
        VkResult first=VK_SUCCESS;
        for(auto& [queue,state]:queues)if(state.tracked) {
            VkLatencySubmissionPresentIdNV latency{VK_STRUCTURE_TYPE_LATENCY_SUBMISSION_PRESENT_ID_NV};
            VkSubmitInfo2 info{VK_STRUCTURE_TYPE_SUBMIT_INFO_2,&latency};
            auto result=call(queue,info);
            if(result==VK_SUCCESS)state.tracked=0;
            else if(first==VK_SUCCESS)first=result;
        }
        return first;
    }
};
}
