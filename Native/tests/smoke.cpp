#include "se_vulkan.h"
#include "presentation_capture.h"
#include <array>
#include <cmath>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <vector>
#include <atomic>
#include <thread>
#include <vulkan/vulkan.h>
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif
void require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
void ok(int result, void* ctx) { if (result != 0) throw std::runtime_error(sevk_last_error(ctx)); }
void identity(float* matrix) { std::memset(matrix, 0, 64); matrix[0] = matrix[5] = matrix[10] = matrix[15] = 1; }
VKAPI_ATTR VkResult VKAPI_CALL old_loader_version(uint32_t* version) { *version=VK_API_VERSION_1_3;return VK_SUCCESS; }
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL old_loader_proc(VkInstance,const char* name) {
    return std::strcmp(name,"vkEnumerateInstanceVersion")==0?reinterpret_cast<PFN_vkVoidFunction>(old_loader_version):nullptr;
}
uint32_t mock_api=VK_API_VERSION_1_4, mock_missing=0, mock_device_creations=0, mock_feature_queries=0, mock_destructions=0;
VKAPI_ATTR VkResult VKAPI_CALL mock_loader_version(uint32_t* version) { *version=VK_API_VERSION_1_4;return VK_SUCCESS; }
VKAPI_ATTR VkResult VKAPI_CALL mock_instance(const VkInstanceCreateInfo* info,const VkAllocationCallbacks*,VkInstance* instance) {
    require(info->pApplicationInfo->apiVersion==VK_API_VERSION_1_4,"bootstrap requests API 1.4");*instance=reinterpret_cast<VkInstance>(uintptr_t(1));return VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL mock_instance_extensions(const char*,uint32_t* count,VkExtensionProperties*) { *count=0;return VK_SUCCESS; }
VKAPI_ATTR void VKAPI_CALL mock_destroy_instance(VkInstance,const VkAllocationCallbacks*) { ++mock_destructions; }
VKAPI_ATTR VkResult VKAPI_CALL mock_devices(VkInstance,uint32_t* count,VkPhysicalDevice* devices) { *count=1;if(devices)devices[0]=reinterpret_cast<VkPhysicalDevice>(uintptr_t(2));return VK_SUCCESS; }
VKAPI_ATTR void VKAPI_CALL mock_properties(VkPhysicalDevice,VkPhysicalDeviceProperties* properties) { *properties={};properties->apiVersion=mock_api; }
VKAPI_ATTR void VKAPI_CALL mock_properties2(VkPhysicalDevice,VkPhysicalDeviceProperties2* properties) {
    properties->properties={}; // All optional attachment-local-read properties remain absent.
}
VKAPI_ATTR void VKAPI_CALL mock_features(VkPhysicalDevice,VkPhysicalDeviceFeatures2* features) {
    ++mock_feature_queries;features->features.depthClamp=(mock_missing&16384)?VK_FALSE:VK_TRUE;features->features.robustBufferAccess=(mock_missing&8192)?VK_FALSE:VK_TRUE;features->features.independentBlend=(mock_missing&4096)?VK_FALSE:VK_TRUE;features->features.textureCompressionBC=(mock_missing&16)?VK_FALSE:VK_TRUE;features->features.geometryShader=(mock_missing&1024)?VK_FALSE:VK_TRUE;features->features.shaderStorageImageExtendedFormats=(mock_missing&2048)?VK_FALSE:VK_TRUE;
    for(auto* next=static_cast<VkBaseOutStructure*>(features->pNext);next;next=next->pNext) {
        if(next->sType==VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES) {
            auto* value=reinterpret_cast<VkPhysicalDeviceVulkan12Features*>(next);value->timelineSemaphore=(mock_missing&4)?VK_FALSE:VK_TRUE;value->scalarBlockLayout=(mock_missing&8)?VK_FALSE:VK_TRUE;
        } else if(next->sType==VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES) {
            auto* value=reinterpret_cast<VkPhysicalDeviceVulkan13Features*>(next);value->dynamicRendering=(mock_missing&1)?VK_FALSE:VK_TRUE;value->synchronization2=(mock_missing&2)?VK_FALSE:VK_TRUE;value->shaderDemoteToHelperInvocation=(mock_missing&32)?VK_FALSE:VK_TRUE;
        }
        // API 1.4 host image copy/local-read feature fields are deliberately absent.
    }
}
VKAPI_ATTR void VKAPI_CALL mock_queues(VkPhysicalDevice,uint32_t* count,VkQueueFamilyProperties* queues) { *count=1;if(queues) { queues[0]={};queues[0].queueFlags=VK_QUEUE_GRAPHICS_BIT;queues[0].queueCount=1; } }
VKAPI_ATTR VkResult VKAPI_CALL mock_device(VkPhysicalDevice,const VkDeviceCreateInfo* info,const VkAllocationCallbacks*,VkDevice*) {
    const auto* features=static_cast<const VkPhysicalDeviceFeatures2*>(info->pNext);
    require(features&&features->features.textureCompressionBC,"used BC feature explicitly enabled");
    uint32_t enabled=16|(features->features.depthClamp?16384:0)|(features->features.robustBufferAccess?8192:0)|(features->features.independentBlend?4096:0)|(features->features.geometryShader?1024:0)|(features->features.shaderStorageImageExtendedFormats?2048:0);
    for(auto* next=static_cast<const VkBaseInStructure*>(features->pNext);next;next=next->pNext) {
        if(next->sType==VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES) {
            const auto* value=reinterpret_cast<const VkPhysicalDeviceVulkan12Features*>(next);enabled|=(value->timelineSemaphore?4:0)|(value->scalarBlockLayout?8:0);
        } else if(next->sType==VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES) {
            const auto* value=reinterpret_cast<const VkPhysicalDeviceVulkan13Features*>(next);enabled|=(value->dynamicRendering?1:0)|(value->synchronization2?2:0)|(value->shaderDemoteToHelperInvocation?32:0);
        } else if(next->sType==VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_4_FEATURES) {
            const auto* value=reinterpret_cast<const VkPhysicalDeviceVulkan14Features*>(next);require(!value->hostImageCopy&&!value->dynamicRenderingLocalRead,"unused optional features stay disabled");
        }
    }
    require(enabled==31807,"all used features explicitly enabled");
    ++mock_device_creations;return VK_ERROR_INITIALIZATION_FAILED; // Stop before creating GPU resources.
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL mock_proc(VkInstance,const char* name) {
    if(std::strcmp(name,"vkEnumerateInstanceVersion")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_loader_version);
    if(std::strcmp(name,"vkEnumerateInstanceExtensionProperties")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_instance_extensions);
    if(std::strcmp(name,"vkCreateInstance")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_instance);
    if(std::strcmp(name,"vkDestroyInstance")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_destroy_instance);
    if(std::strcmp(name,"vkEnumeratePhysicalDevices")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_devices);
    if(std::strcmp(name,"vkGetPhysicalDeviceProperties")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_properties);
    if(std::strcmp(name,"vkGetPhysicalDeviceProperties2")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_properties2);
    if(std::strcmp(name,"vkGetPhysicalDeviceFeatures2")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_features);
    if(std::strcmp(name,"vkGetPhysicalDeviceQueueFamilyProperties")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_queues);
    if(std::strcmp(name,"vkCreateDevice")==0)return reinterpret_cast<PFN_vkVoidFunction>(mock_device);
    return nullptr;
}
int main(int argc, char** argv) {
    void* context = nullptr;
    try {
        require(sevk_api_version() == SEVK_ABI_VERSION, "ABI version");
        require(sizeof(SevkCreateInfo) == 96 && sizeof(SevkProbe) == 1312 && sizeof(SevkStats) == 872 && sizeof(SevkPerformanceStats) == 84, "ABI packing");
        require(sizeof(SevkGuestPass)==416 && sizeof(SevkGuestPass2)==568 && sizeof(SevkGuestFrame)==344,
            "existing guest packet layouts remain compatible");
        require(sizeof(SevkGuestResourceCommand)==48 && sizeof(SevkGuestExecutionStatus)==48,"additive guest packet packing");
        require(sizeof(SevkGuestEnvironment)==96,"shared frame environment packing");
        SevkGuestResourceCommand bad_guest{};
        require(sevk_guest_resource_command(nullptr,&bad_guest)<0,"invalid guest command header refused");
        SevkGuestExecutionStatus bad_status{};
        require(sevk_guest_execution_status(nullptr,&bad_status)<0,"invalid guest status header refused");
        SevkCreateInfo bad{}; require(sevk_create(&bad, &context) < 0 && !context, "invalid ABI must fail");
        require(sevk_begin_frame(nullptr, nullptr) < 0, "null context must fail");
        SevkCreateInfo old{};old.size=sizeof(old);old.abi_version=SEVK_ABI_VERSION;old.shader_directory="unused";old.get_instance_proc_addr=reinterpret_cast<void*>(old_loader_proc);
        require(sevk_create(&old,&context)<0 && !context,"old Vulkan loader rejected before instance/device creation");
        require(std::strstr(sevk_last_error(nullptr),"1.4 loader")!=nullptr,"old loader actionable reason");
        SevkCreateInfo mocked=old;mocked.get_instance_proc_addr=reinterpret_cast<void*>(mock_proc);
        mock_api=VK_API_VERSION_1_3;
        require(sevk_create(&mocked,&context)<0&&!context&&mock_feature_queries==0&&mock_device_creations==0&&mock_destructions==1,
            "older physical device rejected before querying 1.4 features or device creation; instance cleaned up");
        mock_api=VK_API_VERSION_1_4;
        for(uint32_t missing:{1u,2u,4u,8u,16u,32u,1024u,2048u,4096u,8192u,16384u}) {
            mock_missing=missing;uint32_t before=mock_destructions;
            require(sevk_create(&mocked,&context)<0&&!context&&mock_device_creations==0&&mock_destructions==before+1,
                "each missing required feature rejected before device creation; instance cleaned up");
        }
        mock_missing=0;
        require(sevk_create(&mocked,&context)<0&&!context&&mock_device_creations==1&&std::strstr(sevk_last_error(nullptr),"create Vulkan device"),
            "absent optional host-copy/local-read capabilities still reach device creation");
        const uint16_t hdr_texels[]={0x3800,0x3c00,0xbc00,0x3800,0x0001,0x7c00,0x7e00,0x3c00};
        uint8_t sdr_texels[8]{};sevk::capture_scrgb_rgba8(hdr_texels,sdr_texels,2);
        require(sdr_texels[0]==188&&sdr_texels[1]==255&&sdr_texels[2]==0&&sdr_texels[3]==128&&sdr_texels[4]==0&&sdr_texels[5]==255&&sdr_texels[6]==0&&sdr_texels[7]==255,"HDR PNG preview encodes linear RGB and preserves linear alpha with bounded special values");
        {   SevkJobStats jobs{sizeof(SevkJobStats),SEVK_ABI_VERSION};require(sizeof(jobs)==512,"job stats ABI packing");
            require(sevk_get_job_stats(&jobs,1)==0&&jobs.kind_count==7,"job stats need no context");
            SevkJobStats wrong{8,SEVK_ABI_VERSION};require(sevk_get_job_stats(&wrong,0)<0,"job stats reject a wrong size");}
        if (argc > 1 && std::strcmp(argv[1], "--abi") == 0) { std::cout << "ABI contracts passed\n"; return 0; }
        SevkProbe probe{}; probe.size = sizeof(probe);
        if (sevk_probe(&probe) != 0) { std::cout << "GPU test unavailable: " << probe.detail << '\n'; return 77; }
        require(probe.device_api >= ((1u << 22) | (4u << 12)), "must select Vulkan 1.4 device");
        std::cout << "GPU: " << probe.device_name << "; loader " << probe.loader_api << "; validation available " << probe.validation_available << '\n';
        SevkCreateInfo info{}; info.size = sizeof(info); info.abi_version = SEVK_ABI_VERSION;
        info.width = info.height = 64; info.shader_directory = argc > 1 ? argv[1] : "shaders"; info.enable_validation = 1;
#if defined(_WIN32)
        if(argc>2 && std::strcmp(argv[2],"--window")==0) {
            WNDCLASSW cls{};cls.lpfnWndProc=DefWindowProcW;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=L"SeVulkanSmoke";
            require(RegisterClassW(&cls)!=0,"register hidden presentation window");
            RECT rect{0,0,64,64};AdjustWindowRect(&rect,WS_OVERLAPPEDWINDOW,FALSE);
            HWND window=CreateWindowW(cls.lpszClassName,L"SE-Vulkan hidden smoke",WS_OVERLAPPEDWINDOW,0,0,rect.right-rect.left,rect.bottom-rect.top,nullptr,nullptr,cls.hInstance,nullptr);
            require(window!=nullptr,"create hidden presentation window");info.platform=1;info.window=window;
            ok(sevk_create(&info,&context),context);
            SevkPresentationStats presentation{sizeof(SevkPresentationStats),SEVK_ABI_VERSION};
            ok(sevk_get_presentation_stats(context,&presentation),context);
            require(sizeof(presentation)==32 && presentation.swapchain==1 && presentation.surface_ready==1 && presentation.vsync==0 && presentation.mode<=VK_PRESENT_MODE_FIFO_RELAXED_KHR && presentation.images>=2 && presentation.generation>0,"actual HWND presentation state");
            auto initial_generation=presentation.generation;
            ok(sevk_resize(context,64,64,1),context);ok(sevk_get_presentation_stats(context,&presentation),context);
            require(presentation.vsync==1 && presentation.mode==VK_PRESENT_MODE_FIFO_KHR && presentation.generation>initial_generation,"VSync resize reports created FIFO swapchain");
            ok(sevk_resize(context,64,64,0),context);ok(sevk_get_presentation_stats(context,&presentation),context);
            require(presentation.vsync==0,"uncapped resize reports applied VSync state");
            SevkPresentFlowStats flow{sizeof(SevkPresentFlowStats),SEVK_ABI_VERSION};require(sizeof(flow)==64,"present flow ABI packing");
            ok(sevk_get_present_flow_stats(context,&flow),context);
            if(!std::getenv("SE_VULKAN_ASYNC_PRESENT"))require(flow.enabled==1,"async presentation enabled by default");
            else if(std::string(std::getenv("SE_VULKAN_ASYNC_PRESENT"))=="0")require(flow.enabled==0,"explicit synchronous presentation override honored");
            ok(sevk_set_async_present(context,0),context);
            SevkFrameInfo frame{};identity(frame.view_projection);frame.clear_color[0]=1;frame.clear_color[3]=1;
            ok(sevk_set_display_flags(context,1),context);
            ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);
            SevkStats stats{};stats.size=sizeof(stats);ok(sevk_get_stats(context,&stats),context);
            require(stats.presented_frames==1&&stats.validation_errors==0,"first-frame display recreate avoids a global device idle");
            ok(sevk_request_readback(context),context);
            ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);
            std::vector<uint8_t> hdr_pixels(uint64_t(stats.width)*stats.height*4);
            ok(sevk_readback(context,hdr_pixels.data(),hdr_pixels.size()),context);
            ok(sevk_get_stats(context,&stats),context);
            require(hdr_pixels[0]>240&&hdr_pixels[1]<10&&stats.validation_errors==0,"HDR-capable presentation capture returns RGBA8 with valid buffer bounds");
            ok(sevk_set_display_flags(context,0),context);
            for(unsigned i=0;i<5;++i) {
                SevkSprite gray{};gray.destination[0]=gray.destination[1]=4;gray.destination[2]=gray.destination[3]=16;gray.uv[2]=gray.uv[3]=1;
                gray.color[0]=gray.color[1]=gray.color[2]=.25f;gray.color[3]=1;gray.clip[2]=-1;
                ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&gray,1),context);ok(sevk_request_readback(context),context);ok(sevk_end_frame(context),context);
                stats.size=sizeof(stats);ok(sevk_get_stats(context,&stats),context);
                std::vector<uint8_t> pixels(stats.width*stats.height*4);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
                require(pixels[0]>240 && pixels[1]<10 && pixels[2]<10,"presentation capture must return RGBA8");
                require(pixels[(10*stats.width+10)*4]>=136 && pixels[(10*stats.width+10)*4]<=139,"HWND midgray linear tint encoded once");
            }
            std::cout<<"HWND surface extent "<<stats.width<<'x'<<stats.height<<'\n';
            require(stats.presented_frames==7 && stats.validation_errors==0,"presented frame/validation counters");
            ok(sevk_set_async_present(context,1),context);
            for(unsigned i=0;i<20;++i){ok(sevk_begin_frame(context,&frame),context);ok(sevk_request_readback(context),context);ok(sevk_end_frame(context),context);std::vector<uint8_t> pixels(stats.width*stats.height*4);ok(sevk_readback(context,pixels.data(),pixels.size()),context);require(pixels[0]>240&&pixels[1]<10,"async present preserves queued capture bytes");if(i==8){ok(sevk_resize(context,0,0,0),context);ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);ok(sevk_resize(context,64,64,0),context);}}
            ok(sevk_set_async_present(context,0),context);ok(sevk_get_present_flow_stats(context,&flow),context);
            require(flow.completed_calls==28&&flow.pending==0&&flow.enabled==0&&flow.total_interval_ns>0&&flow.total_call_ns>0,"completed presentation calls counted after explicit worker join");
            stats.size=sizeof(stats);ok(sevk_get_stats(context,&stats),context);require(stats.presented_frames==28&&stats.validation_errors==0,"async present resize/suspend-restore/mode switch has clean validation and actual presentation coverage");
            // Display bit 64 (low latency) leaves the decoupled present for the direct one and returns when cleared.
            ok(sevk_set_async_present(context,1),context);
            for(uint32_t flags: {64u,0u,64u,0u}){
                ok(sevk_set_display_flags(context,flags),context);
                for(unsigned i=0;i<3;++i){ok(sevk_begin_frame(context,&frame),context);ok(sevk_request_readback(context),context);ok(sevk_end_frame(context),context);
                    std::vector<uint8_t> pixels(stats.width*stats.height*4);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
                    require(pixels[0]>240&&pixels[1]<10,"low-latency present switch keeps the composed frame");}
            }
            ok(sevk_set_async_present(context,0),context);
            stats.size=sizeof(stats);ok(sevk_get_stats(context,&stats),context);require(stats.presented_frames==40&&stats.validation_errors==0,"low-latency present switch presents every frame with clean validation");
            require(GetModuleHandleW(L"d3d11.dll")==nullptr,"native presentation must not load D3D11");
            sevk_destroy(context);context=nullptr;DestroyWindow(window);UnregisterClassW(cls.lpszClassName,cls.hInstance);
            std::cout<<"Native HWND presentation, pre-present capture, and no-D3D11 smoke passed\n";return 0;
        }
#endif
        ok(sevk_create(&info, &context), context);
        uint32_t rw=0,rh=0;float fraction=0;
        auto resolve=[&](uint32_t w,uint32_t h){ok(sevk_resolve_render_resolution(context,w,h,&rw,&rh,&fraction),context);};
        ok(sevk_set_render_fraction(context,.75f),context);resolve(1919,1079);
        require(rw==1439 && rh==809 && fraction==.75f,"DRS resolution resolves before frame constants with native rounding");
        for(uint32_t mode: {0u,1u,2u,3u,4u}){
            ok(sevk_set_render_fraction(context,1),context);
            ok(sevk_ngx_configure(1,mode,0,"resolution-test-only"),context);resolve(1920,1080);
            const uint32_t widths[]{1280,1114,960,640,1920},heights[]{720,626,540,360,1080};
            require(rw==widths[mode] && rh==heights[mode],"NGX mode overrides DRS before frame constants, including DLAA");
        }
        ok(sevk_ngx_configure(0,0,0,nullptr),context);
        ok(sevk_set_render_fraction(context,1),context);resolve(1920,1080);
        require(rw==1920 && rh==1080 && fraction==1,"disabling NGX restores requested resolution without a stale frame");
        ok(sevk_fsr_configure(1,1,.5f,"resolution-test-only"),context);resolve(1920,1080);
        require(rw==1129 && rh==635,"FSR resolves its own Balanced scale before constants");
        ok(sevk_ngx_configure(1,1,0,"resolution-test-only"),context);resolve(1920,1080);
        require(rw==1114 && rh==626,"NGX retains precedence over FSR in resolution planning");
        ok(sevk_ngx_configure(0,0,0,nullptr),context);ok(sevk_fsr_configure(0,0,.5f,nullptr),context);
        ok(sevk_set_render_fraction(context,1),context);
        require(sevk_resolve_render_resolution(context,0,1080,&rw,&rh,&fraction)<0,"zero logical render extent rejected");
        SevkGpuOpaqueCapabilities opaque_caps{sizeof(opaque_caps),SEVK_ABI_VERSION};
        ok(sevk_get_gpu_opaque_capabilities(context,&opaque_caps),context);
        require(sizeof(opaque_caps)==80,"GPU opaque caps ABI packing");
        std::cout<<"GPU opaque {\"supported\":"<<opaque_caps.supported<<",\"enabled\":"<<opaque_caps.enabled
            <<",\"features\":"<<opaque_caps.available_features<<",\"missing\":"<<opaque_caps.missing_features
            <<",\"insufficient_limits\":"<<opaque_caps.insufficient_limits<<",\"draw_count\":"<<opaque_caps.max_draw_count
            <<",\"storage_range\":"<<opaque_caps.max_storage_range<<",\"stage_storage\":"<<opaque_caps.max_stage_storage
            <<",\"set_storage\":"<<opaque_caps.max_set_storage<<",\"stage_textures\":"<<opaque_caps.max_stage_textures
            <<",\"set_textures\":"<<opaque_caps.max_set_textures<<",\"group_invocations\":"<<opaque_caps.max_group_invocations
            <<",\"group_x\":"<<opaque_caps.max_group_x<<",\"stage_resources\":"<<opaque_caps.max_stage_resources<<"}\n";
        if(argc>2&&std::strcmp(argv[2],"--opaque-caps")==0){
            SevkStats caps_stats{sizeof(caps_stats)};ok(sevk_get_stats(context,&caps_stats),context);
            require(caps_stats.validation_enabled&&caps_stats.validation_errors==0,"caps admission validation clean");
            sevk_destroy(context);context=nullptr;return 0;
        }
        SevkRayCapabilities ray_caps{sizeof(ray_caps), SEVK_ABI_VERSION};
        require(sizeof(SevkRayCapabilities)==48 && sizeof(SevkRayOptions)==32, "ray ABI sizes");
        ok(sevk_get_ray_capabilities(context, &ray_caps), context);
        require(ray_caps.abi_version==SEVK_ABI_VERSION && ray_caps.dispatches==0, "ray capabilities start idle");
        if (!ray_caps.supported) require(ray_caps.ray_query==0, "unsupported device reports no ray query");
        SevkRayOptions ray_options{sizeof(ray_options), SEVK_ABI_VERSION};
        ok(sevk_set_ray_options(context, &ray_options), context);
        SevkPresentationStats offscreen_presentation{sizeof(SevkPresentationStats),SEVK_ABI_VERSION};
        ok(sevk_get_presentation_stats(context,&offscreen_presentation),context);
        require(offscreen_presentation.swapchain==0 && offscreen_presentation.mode==UINT32_MAX && offscreen_presentation.images==0 && offscreen_presentation.generation==0,"offscreen state does not claim a valid swapchain mode");
        const std::array<uint8_t,16> texels{255,0,0,255,255,0,0,255,255,0,0,255,255,0,0,255};
        ok(sevk_upload_texture(context, 1, 2, 2, 0, 0, texels.data(), texels.size()), context);
        require(sevk_upload_texture(context, 2, 2, 2, 0, 0, texels.data(), 3) < 0, "bad texture length rejected");
        const SevkVertex vertices[] = { {{-.7f,-.7f,.5f},{0,0,1},{0,0}}, {{.7f,-.7f,.5f},{0,0,1},{1,0}}, {{0,.7f,.5f},{0,0,1},{.5f,1}} };
        const uint32_t indices[] = {0,1,2};
        SevkStats before_upload{};before_upload.size=sizeof(before_upload);ok(sevk_get_stats(context,&before_upload),context);
        ok(sevk_upload_mesh(context, 1, vertices, 3, indices, 3), context);
        SevkStats after_upload{};after_upload.size=sizeof(after_upload);ok(sevk_get_stats(context,&after_upload),context);
        require(after_upload.completed_timeline==before_upload.completed_timeline+1 && after_upload.uploaded_bytes==before_upload.uploaded_bytes+sizeof(vertices)+sizeof(indices),"mesh vertices/indices copied in one submission with exact byte accounting");
        uint32_t bad_indices[] = {0,1,9}; require(sevk_upload_mesh(context, 2, vertices, 3, bad_indices, 3) < 0, "bad mesh indices rejected");
        SevkFrameInfo frame{}; identity(frame.view_projection); frame.clear_color[3] = 1;
        frame.ambient_color[0] = frame.ambient_color[1] = frame.ambient_color[2] = 1;
        SevkDrawMesh mesh{}; mesh.mesh_id = 1; identity(mesh.world); mesh.tint[2] = mesh.tint[3] = 1; mesh.flags = 2;
        SevkSprite sprite{}; sprite.texture_id = 1; sprite.destination[0] = sprite.destination[1] = 4;
        sprite.destination[2] = sprite.destination[3] = 16; sprite.uv[2] = sprite.uv[3] = 1;
        for (float& c : sprite.color) c = 1; sprite.clip[2] = -1;
        require(sevk_resize_output(context,64,64,32,0,0)<0,"invalid logical presentation extent rejected");
        ok(sevk_resize_output(context,64,64,32,32,0),context);
        SevkSprite full=sprite;full.destination[0]=full.destination[1]=0;full.destination[2]=full.destination[3]=32;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&full,1),context);ok(sevk_end_frame(context),context);
        std::vector<uint8_t> scaled_pixels(64*64*4);ok(sevk_readback(context,scaled_pixels.data(),scaled_pixels.size()),context);
        require(scaled_pixels[(60*64+60)*4]>240,"logical game extent scales across the physical output extent");
        ok(sevk_resize(context,64,64,0),context);
        const float clear_lcd[4]={0,1,0,1};
        ok(sevk_render_texture(context,3,32,32,clear_lcd,&sprite,1),context);
        SevkSprite lcd{}; lcd.texture_id=3; lcd.destination[0]=36; lcd.destination[1]=4; lcd.destination[2]=lcd.destination[3]=24;
        lcd.uv[2]=lcd.uv[3]=1; for(float& c:lcd.color)c=1; lcd.clip[2]=-1;
        for (unsigned i = 0; i < 5; ++i) {
            ok(sevk_begin_frame(context, &frame), context);
            ok(sevk_draw_mesh(context, &mesh), context); ok(sevk_draw_sprites(context, &sprite, 1), context); ok(sevk_end_frame(context), context);
        }
        std::vector<uint8_t> pixels(64*64*4); ok(sevk_readback(context, pixels.data(), pixels.size()), context);
        auto pixel = [&](uint32_t x,uint32_t y,uint32_t channel) { return pixels[(y*64+x)*4+channel]; };
        require(pixel(10,10,0) > 240 && pixel(10,10,1) < 10, "textured sprite must write red pixels");
        require(pixel(32,32,2) > 240 && pixel(32,32,0) < 10, "indexed mesh must write blue pixels");
        require(pixel(60,60,0) < 10 && pixel(60,60,2) < 10, "clear must preserve background");
        ok(sevk_begin_frame(context,&frame),context); ok(sevk_draw_sprites(context,&lcd,1),context); ok(sevk_end_frame(context),context);
        ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(40,8,0)>200 && pixel(40,8,1)<20,"offscreen sprite texture sampled on screen");
        require(pixel(57,25,1)>240 && pixel(57,25,0)<20,"offscreen clear texture sampled on screen");
        SevkSprite aspect_sprite=sprite;
        aspect_sprite.destination[0]=4;aspect_sprite.destination[1]=2;aspect_sprite.destination[2]=8;aspect_sprite.destination[3]=4;
        aspect_sprite.clip[0]=6;aspect_sprite.clip[1]=3;aspect_sprite.clip[2]=4;aspect_sprite.clip[3]=2;
        require(sevk_render_texture_aspect(context,12,32,32,clear_lcd,&aspect_sprite,1,0,8)<0,"invalid logical viewport rejected");
        ok(sevk_render_texture_aspect(context,12,32,32,clear_lcd,&aspect_sprite,1,16,8),context);
        SevkSprite aspect_lcd=lcd;aspect_lcd.texture_id=12;aspect_lcd.destination[0]=aspect_lcd.destination[1]=0;aspect_lcd.destination[2]=aspect_lcd.destination[3]=32;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&aspect_lcd,1),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(16,16,0)>240 && pixel(10,16,1)>240 && pixel(22,16,1)>240 && pixel(16,22,1)>240,"LCD logical viewport scales both geometry and clip endpoints independently");
        aspect_sprite.destination[0]=6;aspect_sprite.destination[1]=3;aspect_sprite.destination[2]=4;aspect_sprite.destination[3]=2;
        aspect_sprite.origin[0]=2;aspect_sprite.origin[1]=1;aspect_sprite.angle_radians=1.57079632679f;aspect_sprite.clip[2]=-1;
        ok(sevk_render_texture_aspect(context,12,32,32,clear_lcd,&aspect_sprite,1,16,8),context);
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&aspect_lcd,1),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(16,10,0)>240 && pixel(11,16,1)>240,"LCD rotation occurs in logical coordinates before nonuniform viewport scaling");
        std::vector<uint8_t> mip_bytes;
        for(uint32_t mip=0;mip<4;++mip) for(uint32_t i=0;i<(8u>>mip)*(8u>>mip);++i) {
            mip_bytes.push_back(mip==0 || mip==3?255:0);mip_bytes.push_back(mip==1 || mip==3?255:0);mip_bytes.push_back(mip>=2?255:0);mip_bytes.push_back(255);
        }
        ok(sevk_upload_texture_mips(context,10,8,8,0,0,mip_bytes.data(),mip_bytes.size(),4),context);
        require(sevk_upload_texture_mips(context,11,8,8,0,0,mip_bytes.data(),mip_bytes.size()-1,4)<0,"truncated mip chain rejected");
        require(sevk_upload_texture_mips(context,11,8,8,0,0,mip_bytes.data(),mip_bytes.size(),5)<0,"overlong mip count rejected");
        SevkSprite minified=sprite;minified.texture_id=10;minified.destination[2]=minified.destination[3]=2;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&minified,1),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(4,4,2)>240 && pixel(4,4,0)<10 && pixel(4,4,1)<10,"sprite minification selects uploaded mip two instead of base level");
        const uint8_t clamp_texels[]={255,0,0,255,0,255,0,255};ok(sevk_upload_texture(context,11,2,1,0,0,clamp_texels,sizeof(clamp_texels)),context);
        SevkSprite clamped=sprite;clamped.texture_id=11;clamped.uv[0]=1.25f;clamped.uv[1]=.5f;clamped.uv[2]=clamped.uv[3]=0;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&clamped,1),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(10,10,1)>240 && pixel(10,10,0)<10,"GUI sampler clamps atlas edges instead of wrapping");
        frame.ambient_color[0]=frame.ambient_color[1]=frame.ambient_color[2]=0;
        SevkLight light{}; light.position[2]=1; light.range=10; light.color[0]=light.color[1]=light.color[2]=1; light.intensity=1;
        ok(sevk_set_lights(context,&light,1),context); mesh.flags=0;
        ok(sevk_begin_frame(context,&frame),context); ok(sevk_draw_mesh(context,&mesh),context); ok(sevk_end_frame(context),context);
        ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(32,32,2)>180,"local point light contributes without ambient/sun");
        const float stock_falloff=100,stock_diffuse=3.14159265359f;
        light.flags=2;std::memcpy(&light.reserved[0],&stock_falloff,4);std::memcpy(&light.reserved[1],&stock_diffuse,4);
        ok(sevk_set_lights(context,&light,1),context);ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_mesh(context,&mesh),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(32,32,2)>=223 && pixel(32,32,2)<=228,"stock light metadata applies range/falloff and normalized diffuse factor");
        ok(sevk_set_lights(context,nullptr,0),context);
        const uint8_t tangent_texel[]={255,128,128,255};ok(sevk_upload_texture(context,12,1,1,0,0,tangent_texel,sizeof(tangent_texel)),context);
        SevkDrawMesh normal_mesh=mesh;normal_mesh.normal_texture_id=12;normal_mesh.tint[0]=normal_mesh.tint[1]=normal_mesh.tint[2]=1;
        frame.sunlight_direction[0]=-1;frame.sunlight_color[0]=frame.sunlight_color[1]=frame.sunlight_color[2]=1;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_mesh(context,&normal_mesh),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(32,32,0)>220,"derivative tangent normal follows stock inverted-X convention");
        frame.sunlight_direction[0]=0;
        const SevkVertex receiver_vertices[]={{{-.9f,-.9f,.5f},{0,0,-1},{0,0}},{{.9f,-.9f,.5f},{0,0,-1},{1,0}},{{0,.9f,.5f},{0,0,-1},{.5f,1}}};
        ok(sevk_upload_mesh(context,2,receiver_vertices,3,indices,3),context);ok(sevk_set_lights(context,nullptr,0),context);
        frame.sunlight_direction[2]=1;frame.sunlight_color[0]=frame.sunlight_color[1]=frame.sunlight_color[2]=1;
        mesh.mesh_id=2;mesh.flags=0;
        SevkDrawMesh blocker=mesh;blocker.world[0]=blocker.world[5]=.2f;blocker.world[14]=-.25f;blocker.tint[0]=1;blocker.tint[2]=0;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_mesh(context,&mesh),context);ok(sevk_draw_mesh(context,&blocker),context);ok(sevk_end_frame(context),context);
        ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(32,32,2)>220 && pixel(32,32,0)<10,"reversed depth keeps near blue receiver ahead of farther red mesh drawn last");
        float matrices[64]{};for(unsigned i=0;i<4;++i) identity(matrices+i*16);const float splits[]={10,20,30,40};
        ok(sevk_set_shadow_cascades(context,matrices,4,splits),context);
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_mesh(context,&mesh),context);ok(sevk_draw_mesh(context,&blocker),context);
        ok(sevk_request_readback(context),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(32,32,2)<20,"cascaded depth shadow occludes receiver");
        require(pixel(32,48,2)>220,"receiver outside shadow remains sunlit");
        ok(sevk_set_shadow_cascades(context,nullptr,0,nullptr),context);
        const uint8_t gray_texel[]={128,128,128,255};ok(sevk_upload_texture(context,4,1,1,0,1,gray_texel,sizeof(gray_texel)),context);
        SevkDrawMesh gray_mesh=mesh;gray_mesh.texture_id=4;gray_mesh.flags=2;
        gray_mesh.tint[0]=gray_mesh.tint[1]=gray_mesh.tint[2]=gray_mesh.tint[3]=1;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_mesh(context,&gray_mesh),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(32,32,0)>=126 && pixel(32,32,0)<=130,"sRGB texture view decoded and display encoded exactly once");
        const float gray_clear[]={.25f,.25f,.25f,1};ok(sevk_render_texture(context,5,16,16,gray_clear,nullptr,0),context);
        SevkSprite gray_lcd=sprite;gray_lcd.texture_id=5;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&gray_lcd,1),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(10,10,0)>=136 && pixel(10,10,0)<=139,"sRGB offscreen LCD preserves linear midgray without double encoding");
        const uint8_t alpha_texel[]={255,0,0,128}, premul_texel[]={188,0,0,128};
        ok(sevk_upload_texture(context,7,1,1,0,1,alpha_texel,sizeof(alpha_texel)),context);
        ok(sevk_upload_texture(context,8,1,1,0,1,premul_texel,sizeof(premul_texel)),context);
        std::array<SevkSprite,3> edges{sprite,sprite,sprite};
        for(unsigned i=0;i<edges.size();++i) {edges[i].texture_id=7;edges[i].destination[0]=4+20.0f*i;edges[i].color[3]=.5f;}
        edges[1].texture_id=8;edges[1].flags=2;edges[2].flags=4;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,edges.data(),static_cast<uint32_t>(edges.size())),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(10,10,0)>=136 && pixel(10,10,0)<=139,"default ABI retains straight-alpha coverage in linear attachment");
        require(pixel(30,10,0)>=136 && pixel(30,10,0)<=139,"premultiplied sRGB edge and tint alpha multiply once with ONE blending");
        require(pixel(50,10,0)>=136 && pixel(50,10,0)<=139,"straight sprite converted to premultiplied output matches edge coverage");
        const float black_lcd[4]={0,0,0,1};edges[1].destination[0]=4;
        ok(sevk_render_texture(context,9,24,24,black_lcd,&edges[1],1),context);
        SevkSprite edge_lcd=sprite;edge_lcd.texture_id=9;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&edge_lcd,1),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(10,10,0)>=136 && pixel(10,10,0)<=139,"premultiplied LCD uses linear blending and a single sRGB encoding");
        SevkFrameInfo edge_frame=frame;edge_frame.clear_color[0]=.2f;edge_frame.clear_color[1]=.3f;edge_frame.clear_color[2]=.4f;
        for(unsigned i=0;i<edges.size();++i) {edges[i].destination[0]=4+20.0f*i;edges[i].color[0]=.4f;edges[i].color[1]=.6f;edges[i].color[2]=.2f;}
        ok(sevk_begin_frame(context,&edge_frame),context);ok(sevk_draw_sprites(context,edges.data(),static_cast<uint32_t>(edges.size())),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        auto linear=[](float c) {return c<=.04045f?c/12.92f:std::pow((c+.055f)/1.055f,2.4f);};
        auto encoded=[](float c) {return int(std::round(255*(c<=.0031308f?12.92f*c:1.055f*std::pow(c,1/2.4f)-.055f)));};
        for(unsigned i=0;i<edges.size();++i) for(unsigned c=0;c<3;++c) {
            float texel=i==1?linear(premul_texel[c]/255.0f):linear(alpha_texel[c]/255.0f);
            float alpha=(128/255.0f)*.5f;
            float source=texel*edges[i].color[c]*(i==1?.5f:alpha);
            int expected=encoded(source+edge_frame.clear_color[c]*(1-alpha));
            require(std::abs(int(pixel(10+20*i,10,c))-expected)<=2,"midtone tint/nonblack background uses linear premultiplied compositing");
        }
        const uint8_t transparent_texel[]={255,0,0,0};ok(sevk_upload_texture(context,6,1,1,0,0,transparent_texel,sizeof(transparent_texel)),context);
        SevkDrawMesh cutout=gray_mesh;cutout.texture_id=6;cutout.flags=3;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_mesh(context,&cutout),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(32,32,0)<10,"cutout samples base alpha when no separate alpha texture exists");
        cutout.flags=2;cutout.texture_id=0;cutout.tint[0]=cutout.tint[1]=0;cutout.tint[2]=1;cutout.dither=.5f;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_mesh(context,&cutout),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        uint32_t covered=0;for(uint32_t y=28;y<36;++y) for(uint32_t x=28;x<36;++x) if(pixel(x,y,2)>240) ++covered;
        require(covered==32,"construction dithering produces expected 50 percent coverage");
        SevkDrawMesh foreground_mesh=mesh;foreground_mesh.flags=2;SevkDrawMesh background_mesh=foreground_mesh;background_mesh.world[14]=-.25f;background_mesh.tint[0]=1;background_mesh.tint[2]=0;background_mesh.flags=2|8;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_mesh(context,&foreground_mesh),context);ok(sevk_draw_mesh(context,&background_mesh),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(32,32,2)>240 && pixel(32,32,0)<10,"additive bottom respects opaque depth");
        background_mesh.flags|=2048;
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_mesh(context,&foreground_mesh),context);ok(sevk_draw_mesh(context,&background_mesh),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,pixels.data(),pixels.size()),context);
        require(pixel(32,32,2)>240 && pixel(32,32,0)>240,"additive top bypasses opaque depth");
        ok(sevk_resize(context,0,0,1),context); require(sevk_begin_frame(context,&frame) == 1, "minimized surface suspended");
        ok(sevk_resize(context,32,32,1),context); ok(sevk_begin_frame(context,&frame),context); ok(sevk_end_frame(context),context);
        ok(sevk_remove_resource(context,0,1),context); ok(sevk_remove_resource(context,1,1),context);
        SevkStats stats{}; stats.size = sizeof(stats); ok(sevk_get_stats(context,&stats),context);
        std::cout << "Submitted " << stats.submitted_frames << " frames, mesh " << stats.mesh_draws << ", sprite " << stats.sprite_draws << ", validation errors " << stats.validation_errors << '\n';
        if (stats.validation_errors) std::cerr << stats.detail << '\n';
        require(stats.submitted_frames == 26 && stats.mesh_draws == 19 && stats.sprite_draws == 23 && stats.validation_errors == 0, "render counters/validation");
        ok(sevk_get_ray_capabilities(context, &ray_caps), context);
        require(ray_caps.dispatches==0, "enable 0 does not dispatch");
        ok(sevk_guest_create_image(context,9001,2,2,1,0),context);
        ok(sevk_begin_frame(context,&frame),context);
        ok(sevk_guest_create_image(context,9002,2,2,1,0),context);
        ok(sevk_guest_update_image(context,9002,texels.data(),texels.size()),context);
        ok(sevk_guest_create_image(context,9002,1,1,1,0),context);
        ok(sevk_end_frame(context),context);
        ok(sevk_begin_uploads(context),context);
        ok(sevk_guest_update_image(context,9001,texels.data(),texels.size()),context);
        ok(sevk_end_uploads(context),context);
        std::atomic<uint32_t> guest_failures{};
        std::thread publisher([&]{for(uint32_t i=0;i<64;++i)if(sevk_guest_update_image(context,9001,texels.data(),texels.size())!=0)++guest_failures;});
        try{for(uint32_t i=0;i<64;++i){ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);}}
        catch(...){publisher.join();throw;}
        publisher.join();ok(sevk_get_stats(context,&stats),context);
        require(guest_failures==0&&stats.validation_errors==0,"game-thread guest uploads serialize with render submissions and retain batched staging");
        sevk_destroy(context); std::cout << "Native textured draw/readback/resize/lifetime tests passed\n"; return 0;
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; sevk_destroy(context); return 1; }
}
