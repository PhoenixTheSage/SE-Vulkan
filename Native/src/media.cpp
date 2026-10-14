#include "se_vulkan.h"
#include <mpv/client.h>
#include <mpv/render.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <memory>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>
#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#include <time.h>
#endif

namespace {
using namespace std::chrono_literals;
double player_volume(float volume){return volume<=0?0:100*std::pow(10.,100.*(volume-1.)/60.);}
uint64_t thread_cpu_us(){
#ifdef _WIN32
    FILETIME creation{},exit{},kernel{},user{};
    if(!GetThreadTimes(GetCurrentThread(),&creation,&exit,&kernel,&user))return 0;
    return (((uint64_t(kernel.dwHighDateTime)<<32)|kernel.dwLowDateTime)+((uint64_t(user.dwHighDateTime)<<32)|user.dwLowDateTime))/10;
#else
    timespec value{};if(clock_gettime(CLOCK_THREAD_CPUTIME_ID,&value))return 0;
    return uint64_t(value.tv_sec)*1000000+uint64_t(value.tv_nsec)/1000;
#endif
}
// A service holds its runtime until every player has joined, including retired IDs.
struct Runtime {
    void* library{};
#define MPV_FUNCTION(name) decltype(&mpv_##name) name{}
    MPV_FUNCTION(create); MPV_FUNCTION(initialize); MPV_FUNCTION(set_option_string); MPV_FUNCTION(set_option);
    MPV_FUNCTION(command_async); MPV_FUNCTION(set_property_async); MPV_FUNCTION(get_property);
    MPV_FUNCTION(wait_event); MPV_FUNCTION(terminate_destroy); MPV_FUNCTION(error_string);
    MPV_FUNCTION(render_context_create); MPV_FUNCTION(render_context_set_update_callback);
    MPV_FUNCTION(render_context_update); MPV_FUNCTION(render_context_render);
    MPV_FUNCTION(render_context_free); MPV_FUNCTION(client_api_version);
    MPV_FUNCTION(free);
#undef MPV_FUNCTION
    template<class T> void symbol(T& target, const char* name) {
#ifdef _WIN32
        target = reinterpret_cast<T>(GetProcAddress(static_cast<HMODULE>(library), name));
#else
        target = reinterpret_cast<T>(dlsym(library, name));
#endif
        if (!target) throw std::runtime_error(std::string("Missing libmpv export: ")+name);
    }
    explicit Runtime(const char* directory) {
        if (!directory || !*directory) throw std::runtime_error("Media runtime directory is required");
        auto path=std::filesystem::u8path(directory);
#ifdef _WIN32
        path/="libmpv-2.dll";
        library=LoadLibraryExW(path.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
#else
        path/="libmpv.so.2";
        library=dlopen(path.c_str(),RTLD_NOW|RTLD_LOCAL);
#endif
        if (!library) throw std::runtime_error("Bundled libmpv runtime or one of its dependencies could not be loaded");
        try {
#define LOAD(name) symbol(name,"mpv_" #name)
            LOAD(create);LOAD(initialize);LOAD(set_option_string);LOAD(set_option);LOAD(command_async);
            LOAD(set_property_async);LOAD(get_property);LOAD(wait_event);LOAD(terminate_destroy);
            LOAD(error_string);LOAD(render_context_create);LOAD(render_context_set_update_callback);
            LOAD(render_context_update);LOAD(render_context_render);LOAD(render_context_free);LOAD(client_api_version);
            LOAD(free);
#undef LOAD
            if ((client_api_version()>>16)!=2) throw std::runtime_error("Unsupported libmpv client ABI");
        } catch (...) { unload(); throw; }
    }
    void unload() noexcept {
        if (!library) return;
#ifdef _WIN32
        FreeLibrary(static_cast<HMODULE>(library));
#else
        dlclose(library);
#endif
        library=nullptr;
    }
    ~Runtime(){unload();}
    void check(int result){if(result<0)throw std::runtime_error(error_string(result));}
};

struct Player {
    struct Buffer {
        std::vector<uint8_t> pixels; uint32_t width{},height{}; uint64_t sequence{};
        // libmpv's software API recommends 64-byte pointer/stride alignment.
        // Reserve padding once per slot rather than trigger its copy fallback.
        uint8_t* data(){return reinterpret_cast<uint8_t*>((reinterpret_cast<uintptr_t>(pixels.data())+63)&~uintptr_t(63));}
    };
    std::shared_ptr<Runtime> api;
    std::string path;
    std::atomic<bool> quit{},finished{},start{},render_dirty{true};
    std::atomic<float> volume;
    std::atomic<uint32_t> width{},height{};
    std::atomic<uint64_t> player_dropped{};
    std::mutex mutex, wake_mutex;
    std::condition_variable wake;
    std::array<Buffer,3> buffers;
    int published{-1}, acquired{-1};
    SevkMediaFrame status{};
    std::thread control;

    Player(std::shared_ptr<Runtime> runtime,std::string filename,float gain):api(std::move(runtime)),path(std::move(filename)),volume(gain) {
        status.size=sizeof(status);status.abi_version=SEVK_ABI_VERSION;status.reserved=8;
        control=std::thread([this]{run();});
    }
    ~Player(){stop();if(control.joinable())control.join();}
    void stop(){quit=true;wake.notify_all();}
    void fail(const std::string& message){std::lock_guard lock(mutex);status.state=3;std::snprintf(status.error,sizeof(status.error),"%s",message.c_str());}
    static void updated(void* user){auto& p=*static_cast<Player*>(user);p.render_dirty=true;p.wake.notify_all();}
    void render(mpv_handle* core,std::atomic<int>& ready) noexcept {
        mpv_render_context* context{};
        try {
            mpv_render_param params[]={{MPV_RENDER_PARAM_API_TYPE,const_cast<char*>(MPV_RENDER_API_TYPE_SW)},{MPV_RENDER_PARAM_INVALID,nullptr}};
            api->check(api->render_context_create(&context,core,params));
            api->render_context_set_update_callback(context,updated,this);ready=1;wake.notify_all();
            while(!quit) {
                {std::unique_lock lock(wake_mutex);wake.wait_for(lock,20ms,[&]{return quit.load()||render_dirty.load();});}
                if(quit)break;
                if(!render_dirty.exchange(false))continue;
                uint64_t updates=api->render_context_update(context);
                if(!(updates&MPV_RENDER_UPDATE_FRAME))continue;
                uint32_t w=width.load(),h=height.load();
                if(!w || !h){render_dirty=true;std::this_thread::sleep_for(1ms);continue;}
                if(w>16384 || h>16384 || uint64_t(w)*h>64u*1024*1024)throw std::runtime_error("Media dimensions exceed the streaming budget");
                int slot=0;
                {std::lock_guard lock(mutex);while(slot==published || slot==acquired)++slot;}
                auto& output=buffers[slot];size_t bytes=size_t(w)*h*4;output.pixels.resize(bytes+63);output.width=w;output.height=h;
                int size[]={int(w),int(h)};size_t stride=size_t(w)*4;
                mpv_render_param target[]={{MPV_RENDER_PARAM_SW_SIZE,size},{MPV_RENDER_PARAM_SW_FORMAT,const_cast<char*>("bgr0")},
                    {MPV_RENDER_PARAM_SW_STRIDE,&stride},{MPV_RENDER_PARAM_SW_POINTER,output.data()},{MPV_RENDER_PARAM_INVALID,nullptr}};
                auto before=thread_cpu_us();api->check(api->render_context_render(context,target));
                for(size_t a=3;a<bytes;a+=4)output.data()[a]=255;
                auto elapsed=thread_cpu_us()-before;
                {std::lock_guard lock(mutex);output.sequence=++status.frames;if(published>=0)++status.dropped;published=slot;status.conversion_us+=elapsed;}
            }
        }catch(const std::exception& e){fail(e.what());quit=true;if(ready==0)ready=-1;wake.notify_all();}
        if(context){api->render_context_set_update_callback(context,nullptr,nullptr);api->render_context_free(context);}
    }
    void run() noexcept {
        mpv_handle* core{};std::thread rendering;std::atomic<int> ready{};
        try {
            core=api->create();if(!core)throw std::runtime_error("mpv_create failed");
            auto option=[&](const char* key,const char* value){int result=api->set_option_string(core,key,value);if(result<0)throw std::runtime_error(std::string("libmpv option ")+key+": "+api->error_string(result));};
            // Lua/JavaScript/C plugins are excluded by the pinned build; its
            // load-scripts option consequently does not exist.
            option("config","no");option("input-default-bindings","no");
            option("input-vo-keyboard","no");option("input-terminal","no");option("terminal","no");
            option("osd-level","0");option("sid","no");option("audio-display","no");option("hwdec","no");
            option("vo","libmpv");option("idle","yes");option("pause","yes");option("keep-open","no");
            // Retain mpv's Lanczos/full-chroma/accurate-rounding conversion,
            // but perform it in FFmpeg's threaded frame filter. The legacy
            // software-render scaler otherwise converts 1080p60 serially.
            option("vf","lavfi=[scale=iw:ih:flags=lanczos+accurate_rnd+full_chroma_int+full_chroma_inp:threads=4,format=pix_fmts=bgr0]");
            double initial_volume=player_volume(volume.load());api->check(api->set_option(core,"volume",MPV_FORMAT_DOUBLE,&initial_volume));
            option("video-sync","audio");option("audio-fallback-to-null","yes");
#ifdef _WIN32
            option("ao","wasapi");
#else
            option("ao","pulse,alsa");
#endif
            api->check(api->initialize(core));
            rendering=std::thread([&]{render(core,ready);});
            while(!quit && ready==0){std::unique_lock lock(wake_mutex);wake.wait_for(lock,10ms);}
            if(!quit && ready>0){const char* command[]={"loadfile",path.c_str(),nullptr};api->check(api->command_async(core,1,command));}
            bool playing=false;float applied=std::numeric_limits<float>::quiet_NaN();
            auto opened=std::chrono::steady_clock::now();bool loaded=false;
            while(!quit) {
                auto* event=api->wait_event(core,.02);
                if(event->error<0)throw std::runtime_error(api->error_string(event->error));
                if(event->event_id==MPV_EVENT_FILE_LOADED)loaded=true;
                if(event->event_id==MPV_EVENT_END_FILE){auto* end=static_cast<mpv_event_end_file*>(event->data);if(end && end->error<0)throw std::runtime_error(api->error_string(end->error));std::lock_guard lock(mutex);status.state=2;}
                if(!loaded && std::chrono::steady_clock::now()-opened>15s)throw std::runtime_error("Media opening timed out");
                if(start && !playing){int paused=0;api->check(api->set_property_async(core,2,"pause",MPV_FORMAT_FLAG,&paused));playing=true;}
                float requested=volume.load();
                if(requested!=applied){
                    // DirectShow: dB=100*(v-1); mpv: amplitude=(volume/100)^3.
                    double gain=player_volume(requested);
                    api->check(api->set_property_async(core,3,"volume",MPV_FORMAT_DOUBLE,&gain));applied=requested;
                }
                int64_t w{},h{},samplerate{};double position{},offset{};uint32_t flags=0;
                if(api->get_property(core,"video-params/w",MPV_FORMAT_INT64,&w)>=0 && api->get_property(core,"video-params/h",MPV_FORMAT_INT64,&h)>=0){width=uint32_t(w);height=uint32_t(h);}
                api->get_property(core,"time-pos",MPV_FORMAT_DOUBLE,&position);
                if(api->get_property(core,"avsync",MPV_FORMAT_DOUBLE,&offset)>=0)flags|=2;
                if(api->get_property(core,"audio-out-params/samplerate",MPV_FORMAT_INT64,&samplerate)>=0 && samplerate>0)flags|=1;
                int64_t decoder_drop{},output_drop{};
                api->get_property(core,"decoder-frame-drop-count",MPV_FORMAT_INT64,&decoder_drop);
                api->get_property(core,"frame-drop-count",MPV_FORMAT_INT64,&output_drop);
                player_dropped=uint64_t(std::max<int64_t>(0,decoder_drop))+uint64_t(std::max<int64_t>(0,output_drop));
                char* ao{};if(api->get_property(core,"current-ao",MPV_FORMAT_STRING,&ao)>=0 && ao){if(std::strcmp(ao,"null")!=0)flags|=4;api->free(ao);}
                {std::lock_guard lock(mutex);status.reserved=flags|8;status.position_us=int64_t(position*1e6);status.av_offset_us=int64_t(offset*1e6);if(loaded && playing && status.state==0)status.state=1;}
            }
        }catch(const std::exception& e){fail(e.what());}
        quit=true;wake.notify_all();if(rendering.joinable())rendering.join();
        if(core)api->terminate_destroy(core);finished=true;
    }
    void update(SevkMediaFrame& frame){
        start=true;wake.notify_all();std::lock_guard lock(mutex);
        if(published>=0){acquired=published;published=-1;}
        frame=status;
        frame.dropped+=player_dropped.load();
        if(acquired>=0){auto& b=buffers[acquired];frame.width=b.width;frame.height=b.height;frame.sequence=b.sequence;frame.pixels=b.data();}
    }
};
struct Media {
    std::shared_ptr<Runtime> runtime;
    std::unordered_map<uint32_t,std::unique_ptr<Player>> players;
    std::vector<std::unique_ptr<Player>> retired;
    explicit Media(const char* path):runtime(std::make_shared<Runtime>(path)){}
    ~Media(){for(auto& [id,p]:players)p->stop();for(auto& p:retired)p->stop();players.clear();retired.clear();}
    void collect(){std::erase_if(retired,[](auto& p){return p->finished.load();});}
    void close(uint32_t id){auto i=players.find(id);if(i!=players.end()){i->second->stop();retired.push_back(std::move(i->second));players.erase(i);}collect();}
};
}
int32_t SEVK_CALL sevk_media_create(const char* directory,void** result){if(!result)return -1;*result=nullptr;try{*result=new Media(directory);return 0;}catch(...){return -1;}}
int32_t SEVK_CALL sevk_media_play(void* context,uint32_t id,const char* path,float volume){
    if(!context || !path || !*path || !std::isfinite(volume))return -1;
    try{auto& m=*static_cast<Media*>(context);m.close(id);m.players.emplace(id,std::make_unique<Player>(m.runtime,path,std::clamp(volume,0.f,1.f)));return 0;}catch(...){return -1;}
}
int32_t SEVK_CALL sevk_media_update(void* context,uint32_t id,SevkMediaFrame* frame){
    if(!context || !frame || frame->size!=sizeof(*frame) || frame->abi_version!=SEVK_ABI_VERSION)return -1;
    auto& m=*static_cast<Media*>(context);m.collect();auto i=m.players.find(id);if(i==m.players.end())return 1;i->second->update(*frame);return 0;
}
int32_t SEVK_CALL sevk_media_volume(void* context,uint32_t id,float volume){if(!context || !std::isfinite(volume))return -1;auto& m=*static_cast<Media*>(context);auto i=m.players.find(id);if(i!=m.players.end())i->second->volume=std::clamp(volume,0.f,1.f);return 0;}
int32_t SEVK_CALL sevk_media_close(void* context,uint32_t id){if(!context)return -1;static_cast<Media*>(context)->close(id);return 0;}
void SEVK_CALL sevk_media_destroy(void* context){delete static_cast<Media*>(context);}
