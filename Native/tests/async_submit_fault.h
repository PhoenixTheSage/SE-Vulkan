// Test dispatch layer over the real Windows Vulkan loader. Only the armed FIFO
// submit is rejected. Release it when the client actually enters its capture
// timeline wait, including after the dependent frame was enqueued in upload mode.
#pragma once
#include <chrono>
#include <condition_variable>
#include <cwchar>
#include <mutex>

namespace submit_fault {
inline PFN_vkGetInstanceProcAddr instance_proc;
inline PFN_vkGetDeviceProcAddr device_proc;
inline PFN_vkQueueSubmit2 real_submit;
inline PFN_vkWaitSemaphores real_wait;
inline PFN_vkCreateDebugUtilsMessengerEXT create_debug;
inline PFN_vkDebugUtilsMessengerCallbackEXT renderer_debug;
inline std::atomic<unsigned> validation_errors{};
inline std::mutex mutex;
inline std::condition_variable cv;
inline bool armed{},entered{},reading{},wait_seen{},timed_out{};
inline unsigned worker_calls{},after_fault{};
inline DWORD caller;

inline VkResult VKAPI_CALL submit(VkQueue queue,uint32_t count,const VkSubmitInfo2* infos,VkFence fence){
    PWSTR name=nullptr;
    bool worker=SUCCEEDED(GetThreadDescription(GetCurrentThread(),&name))&&name&&std::wcscmp(name,L"Vulkan frame submit")==0;
    LocalFree(name);
    if(worker){
        std::unique_lock lock(mutex);++worker_calls;
        if(armed){
            armed=false;entered=true;cv.notify_all();
            if(!cv.wait_for(lock,std::chrono::seconds(8),[]{return wait_seen;}))timed_out=true;
            return VK_ERROR_OUT_OF_HOST_MEMORY;
        }
        if(entered)++after_fault;
    }
    return real_submit(queue,count,infos,fence);
}
inline VkResult VKAPI_CALL wait(VkDevice device,const VkSemaphoreWaitInfo* info,uint64_t timeout){
    if(GetCurrentThreadId()==caller){
        std::lock_guard lock(mutex);
        if(reading){wait_seen=true;cv.notify_all();}
    }
    return real_wait(device,info,timeout);
}
inline PFN_vkVoidFunction VKAPI_CALL device_dispatch(VkDevice device,const char* name){
    auto fn=device_proc(device,name);
    if(fn&&std::strcmp(name,"vkQueueSubmit2")==0){real_submit=reinterpret_cast<PFN_vkQueueSubmit2>(fn);return reinterpret_cast<PFN_vkVoidFunction>(submit);}
    if(fn&&std::strcmp(name,"vkWaitSemaphores")==0){real_wait=reinterpret_cast<PFN_vkWaitSemaphores>(fn);return reinterpret_cast<PFN_vkVoidFunction>(wait);}
    return fn;
}
inline VkBool32 VKAPI_CALL debug_message(VkDebugUtilsMessageSeverityFlagBitsEXT severity,VkDebugUtilsMessageTypeFlagsEXT type,const VkDebugUtilsMessengerCallbackDataEXT* data,void* user){
    if(severity&VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT)++validation_errors;
    return renderer_debug(severity,type,data,user);
}
inline VkResult VKAPI_CALL debug_messenger(VkInstance instance,const VkDebugUtilsMessengerCreateInfoEXT* info,const VkAllocationCallbacks* allocator,VkDebugUtilsMessengerEXT* handle){
    auto copy=*info;renderer_debug=copy.pfnUserCallback;copy.pfnUserCallback=debug_message;
    return create_debug(instance,&copy,allocator,handle);
}
inline PFN_vkVoidFunction VKAPI_CALL instance_dispatch(VkInstance instance,const char* name){
    auto fn=instance_proc(instance,name);
    if(fn&&std::strcmp(name,"vkGetDeviceProcAddr")==0){device_proc=reinterpret_cast<PFN_vkGetDeviceProcAddr>(fn);return reinterpret_cast<PFN_vkVoidFunction>(device_dispatch);}
    if(fn&&std::strcmp(name,"vkCreateDebugUtilsMessengerEXT")==0){create_debug=reinterpret_cast<PFN_vkCreateDebugUtilsMessengerEXT>(fn);return reinterpret_cast<PFN_vkVoidFunction>(debug_messenger);}
    return fn;
}
inline void install(SevkCreateInfo& info){
    auto loader=GetModuleHandleW(L"vulkan-1.dll");
    require(loader!=nullptr,"real Vulkan loader loaded by probe");
    instance_proc=reinterpret_cast<PFN_vkGetInstanceProcAddr>(GetProcAddress(loader,"vkGetInstanceProcAddr"));
    require(instance_proc!=nullptr,"resolve real Vulkan instance dispatch");
    info.get_instance_proc_addr=reinterpret_cast<void*>(instance_dispatch);caller=GetCurrentThreadId();
}
inline bool run(void* context,bool upload){
    SevkFrameInfo frame{};identity(frame.view_projection);frame.clear_color[3]=1;
    for(int i=0;i<2;++i){ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);}
    ok(sevk_set_async_present(context,1),context); // Drain warm-up and retain the same active mode.
    SevkStats initial{};initial.size=sizeof(initial);ok(sevk_get_stats(context,&initial),context);
    require(initial.validation_enabled&&initial.validation_errors==0&&initial.submitted_frames==2&&initial.presented_frames==2,"two actual clean warm-up presents");
    {std::lock_guard lock(mutex);if(!worker_calls)return false;}
    auto arm=[](){std::lock_guard lock(mutex);armed=true;};
    auto entered_fifo=[](){std::unique_lock lock(mutex);require(cv.wait_for(lock,std::chrono::seconds(4),[]{return entered;}),"fault reached the FIFO submit worker");};
    if(upload){
        uint32_t pixel=0xffffffff;ok(sevk_begin_uploads(context),context);
        ok(sevk_upload_texture(context,201,1,1,0,0,&pixel,sizeof(pixel)),context);
        arm();ok(sevk_end_uploads(context),context);entered_fifo();
    }
    ok(sevk_begin_frame(context,&frame),context);ok(sevk_request_readback(context),context);
    if(!upload)arm();
    ok(sevk_end_frame(context),context);entered_fifo();
    {std::lock_guard lock(mutex);reading=true;}
    auto failed=[&](int result){
        std::string detail=sevk_last_error(context);
        if(result>=0||detail.find("asynchronous frame-queue submit failed (VkResult -1)")==std::string::npos)
            throw std::runtime_error("expected FIFO out-of-host-memory, returned "+std::to_string(result)+": "+detail);
    };
    std::vector<uint8_t> pixels(size_t(initial.width)*initial.height*4);
    failed(sevk_readback(context,pixels.data(),pixels.size()));
    failed(sevk_resize(context,80,80,0));failed(sevk_begin_frame(context,&frame));
    SevkStats final{};final.size=sizeof(final);ok(sevk_get_stats(context,&final),context);
    require(final.submitted_frames==initial.submitted_frames&&final.presented_frames==initial.presented_frames&&final.validation_errors==0&&!final.device_lost,"failed FIFO work leaves success counters and validation intact");
    {std::lock_guard lock(mutex);require(wait_seen&&!timed_out&&after_fault==0,"readback released the injected fault and no later FIFO task reached Vulkan");}
    return true;
}
}
