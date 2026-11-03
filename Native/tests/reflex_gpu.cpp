#include "se_vulkan.h"
#include <iostream>
#include <stdexcept>
#include <string>
#include <future>
#include <chrono>
#if defined(_WIN32)
#include <windows.h>
#include <dbghelp.h>
#include <array>
#endif

namespace {
#if defined(_WIN32)
LONG WINAPI crash_report(EXCEPTION_POINTERS* exception){
    std::cerr<<"Reflex native exception 0x"<<std::hex<<exception->ExceptionRecord->ExceptionCode<<" address="<<exception->ExceptionRecord->ExceptionAddress<<std::dec<<'\n';
    HANDLE process=GetCurrentProcess();SymInitialize(process,nullptr,TRUE);
    CONTEXT context=*exception->ContextRecord;
    STACKFRAME64 frame{};frame.AddrPC.Offset=context.Rip;frame.AddrPC.Mode=AddrModeFlat;
    frame.AddrStack.Offset=context.Rsp;frame.AddrStack.Mode=AddrModeFlat;frame.AddrFrame.Offset=context.Rbp;frame.AddrFrame.Mode=AddrModeFlat;
    for(int i=0;i<24&&StackWalk64(IMAGE_FILE_MACHINE_AMD64,process,GetCurrentThread(),&frame,&context,nullptr,SymFunctionTableAccess64,SymGetModuleBase64,nullptr);++i){
        alignas(SYMBOL_INFO) std::array<char,sizeof(SYMBOL_INFO)+MAX_SYM_NAME> storage{};
        auto* symbol=reinterpret_cast<SYMBOL_INFO*>(storage.data());symbol->SizeOfStruct=sizeof(SYMBOL_INFO);symbol->MaxNameLen=MAX_SYM_NAME;
        DWORD64 displacement=0;IMAGEHLP_MODULE64 module{};module.SizeOfStruct=sizeof(module);
        SymGetModuleInfo64(process,frame.AddrPC.Offset,&module);
        std::cerr<<module.ModuleName<<"!";
        if(SymFromAddr(process,frame.AddrPC.Offset,&displacement,symbol))std::cerr<<symbol->Name<<"+0x"<<std::hex<<displacement<<std::dec;
        else std::cerr<<"0x"<<std::hex<<frame.AddrPC.Offset<<std::dec;
        std::cerr<<'\n';
    }
    HANDLE output=CreateFileW(L"reflex-vulkan-crash.dmp",GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(output!=INVALID_HANDLE_VALUE){MINIDUMP_EXCEPTION_INFORMATION info{GetCurrentThreadId(),exception,FALSE};MiniDumpWriteDump(process,GetCurrentProcessId(),output,MiniDumpWithThreadInfo,&info,nullptr,nullptr);CloseHandle(output);}
    return EXCEPTION_EXECUTE_HANDLER;
}
#endif
void require(bool value,const char* text){if(!value)throw std::runtime_error(text);}
void ok(int32_t result,void* context){if(result!=0)throw std::runtime_error("Reflex native result "+std::to_string(result)+": "+sevk_last_error(context));}
SevkReflexStatus status(void* context){SevkReflexStatus value{sizeof(value),SEVK_ABI_VERSION};ok(sevk_reflex_status(context,&value),context);return value;}
}
int main(int argc,char** argv){
#if !defined(_WIN32)
    std::cout<<"Hidden HWND Reflex smoke requires Windows; no Vulkan support conclusion.\n";return 77;
#else
    SetUnhandledExceptionFilter(crash_report);
    void* context=nullptr;HWND window=nullptr;
    auto cleanup=[&]{if(context){sevk_destroy(context);context=nullptr;}if(window){DestroyWindow(window);window=nullptr;}};
    try{
        SevkProbe probe{};probe.size=sizeof(probe);
        ok(sevk_probe(&probe),nullptr);
        std::cout<<"GPU: "<<probe.device_name<<" validation available="<<probe.validation_available<<std::endl;
        if(argc>2&&std::string(argv[2])=="--require-validation")require(probe.validation_available!=0,"Reflex validation gate requires the private Khronos layer");
        bool asynchronous=argc>3&&std::string(argv[3])=="--async";
        window=CreateWindowExW(0,L"STATIC",L"SE-Vulkan Reflex binding smoke",WS_POPUP,0,0,64,64,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        require(window!=nullptr,"hidden HWND creation");
        SevkCreateInfo create{};create.size=sizeof(create);create.abi_version=SEVK_ABI_VERSION;
        bool validation=!(argc>2&&std::string(argv[2])=="--no-validation");
        create.platform=1;create.window=window;create.width=create.height=64;create.enable_validation=validation?1u:0u;
        create.shader_directory=argc>1?argv[1]:"shaders";
        ok(sevk_create(&create,&context),context);
        auto initial=status(context);
        std::cout<<"Vulkan Reflex extension enabled="<<initial.extension_enabled<<" revision="<<initial.extension_version<<" bound="<<initial.swapchain_bound<<'\n';
        require(initial.configured_mode==UINT32_MAX&&initial.sleep_count==0,"binding must not apply a user mode implicitly");
        if(!initial.swapchain_bound){cleanup();std::cout<<"Reflex binding unavailable on this device/surface.\n";return 77;}
        if(initial.extension_version<3){cleanup();std::cout<<"Explicit Reflex attribution smoke requires revision 3; ordinary binding support remains separate.\n";return 77;}
        // Synchronous presentation is a smoke-harness policy. Game async paths
        // remain unchanged and need their own frame-attribution integration.
        ok(sevk_set_async_present(context,asynchronous?1u:0u),context);
        SevkFrameInfo frame{};for(int i=0;i<16;++i)frame.view_projection[i]=(i%5==0)?1.f:0.f;frame.clear_color[3]=1;
        uint64_t frames=0;
        for(uint32_t mode:{0u,1u,2u,0u}){
            ok(sevk_reflex_configure(context,initial.epoch,mode),context);
            require(status(context).configured_mode==mode,"accepted native mode reported separately");
            for(int i=0;i<2;++i){
                ++frames;
                ok(sevk_reflex_sleep(context,initial.epoch),context);
                ok(sevk_reflex_marker(context,initial.epoch,frames,0),context);
                ok(sevk_reflex_marker(context,initial.epoch,frames,1),context);
                SevkReflexOutput output{sizeof(output),SEVK_ABI_VERSION,initial.epoch,frames,0,1};
                ok(sevk_reflex_bind_output(context,&output),context);
                ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);
                output.output_ordinal=1;ok(sevk_reflex_bind_output(context,&output),context);
                ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);
            }
            // Same policy call drains outstanding FIFO/copy/present tasks. It
            // does not wait between individual input-bearing frames or repeats.
            ok(sevk_set_async_present(context,asynchronous?1u:0u),context);
            std::cout<<"accepted mode="<<mode<<" sleep calls="<<status(context).sleep_count<<'\n';
        }
        auto before_resize=status(context);
        require(before_resize.sleep_count==frames&&before_resize.marker_count==frames*10,"base/repeat recording and actual-present marker counters without extra sleep");
        SevkReflexLatency timing{sizeof(timing),SEVK_ABI_VERSION};
        ok(sevk_reflex_latency(context,initial.epoch,&timing),context);
        require(timing.epoch==initial.epoch&&timing.report_count<=64,"driver timing summary bounded and epoch matched");
        std::cout<<"Timing query reports="<<timing.report_count<<" latest="<<timing.frame_id<<" (hidden smoke may have none)\n";
        SevkReflexOutput unbound{sizeof(unbound),SEVK_ABI_VERSION};ok(sevk_reflex_bind_output(context,&unbound),context);
        std::promise<void> stale_ready,release_stale;auto ready=stale_ready.get_future();auto released=release_stale.get_future();
        auto stale_worker=std::async(std::launch::async,[&]{
            SevkReflexOutput old{sizeof(old),SEVK_ABI_VERSION,initial.epoch,frames+1,0,1};
            ok(sevk_reflex_bind_output(context,&old),context);stale_ready.set_value();
            require(released.wait_for(std::chrono::seconds(5))==std::future_status::ready,"stale producer release timeout");
            std::array<uint8_t,16> pixels{};
            ok(sevk_begin_uploads(context),context);
            ok(sevk_upload_texture(context,1001,2,2,0,1,pixels.data(),pixels.size()),context);
            ok(sevk_reflex_bind_output(context,&unbound),context);
            ok(sevk_upload_texture(context,1002,2,2,0,1,pixels.data(),pixels.size()),context);
            ok(sevk_end_uploads(context),context);
        });
        require(ready.wait_for(std::chrono::seconds(5))==std::future_status::ready,"producer installed old-epoch thread binding");
        ok(sevk_reflex_retire(context,initial.epoch),context);
        require(sevk_reflex_latency(context,initial.epoch,&timing)<0&&timing.report_count==0&&timing.frame_id==0,"retired timing query rejects and clears stale reports");
        release_stale.set_value();stale_worker.get();
        auto retired=status(context);
        require(retired.swapchain_bound&&retired.epoch>initial.epoch&&retired.configured_mode==UINT32_MAX&&retired.marker_count==0,"owner retirement changes epoch without recreating swapchain");
        ok(sevk_reflex_configure(context,retired.epoch,0),context);
        SevkReflexOutput fresh{sizeof(fresh),SEVK_ABI_VERSION,retired.epoch,frames+1,0,2};
        ok(sevk_reflex_bind_output(context,&fresh),context);
        auto stale=fresh;stale.epoch=initial.epoch;
        require(sevk_reflex_bind_output(context,&stale)<0,"retired token bind is refused");
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);ok(sevk_set_async_present(context,asynchronous?1u:0u),context);
        require(status(context).marker_count==0,"refused rebind clears previously installed thread token");
        ok(sevk_reflex_bind_output(context,&fresh),context);
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);ok(sevk_set_async_present(context,asynchronous?1u:0u),context);
        require(status(context).marker_count==4,"new owner emits record and actual present markers after retirement");
        ok(sevk_reflex_bind_output(context,&unbound),context);
        ok(sevk_resize(context,80,72,0),context);
        auto resized=status(context);
        require(resized.swapchain_bound&&resized.epoch>before_resize.epoch&&resized.configured_mode==UINT32_MAX&&resized.sleep_count==0,"resize retires old semaphore and mode");
        require(sevk_reflex_sleep(context,resized.epoch)<0,"new swapchain requires configuration");
        require(sevk_reflex_configure(context,before_resize.epoch,1)<0&&sevk_reflex_sleep(context,before_resize.epoch)<0&&sevk_reflex_marker(context,before_resize.epoch,99,0)<0,"retired epoch commands refused");
        require(sevk_reflex_configure(context,0,1)<0,"missing epoch refused");
        ok(sevk_reflex_configure(context,resized.epoch,0),context);
        ok(sevk_reflex_sleep(context,resized.epoch),context);
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_end_frame(context),context);
        SevkStats stats{};stats.size=sizeof(stats);ok(sevk_get_stats(context,&stats),context);
        require(stats.validation_errors==0,"Vulkan validation must remain clean");
        require(stats.validation_enabled==(validation?1u:0u),"validation and production driver-call lanes stay distinct");
        std::cout<<"Reflex Vulkan binding smoke passed; reported errors="<<stats.validation_errors<<" validation available="<<probe.validation_available<<". This does not validate game-loop markers, async attribution, FG or latency reduction.\n";
        cleanup();return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';cleanup();return 1;}
#endif
}
