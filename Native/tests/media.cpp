#include "se_vulkan.h"
#include <chrono>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <thread>
using namespace std::chrono_literals;
static void require(bool condition,const char* message){if(!condition)throw std::runtime_error(message);}
static SevkMediaFrame update(void* media,uint32_t id){SevkMediaFrame f{};f.size=sizeof(f);f.abi_version=SEVK_ABI_VERSION;require(sevk_media_update(media,id,&f)==0,"update registered player");return f;}
int main(int argc,char** argv){
    void* media{};
    try{
        require(sevk_media_create("/no/such/runtime",&media)<0 && media==nullptr,"missing runtime fails safely");
        require(sevk_media_create(nullptr,nullptr)<0,"null media output rejected");
        if(argc<3){std::cout<<"PASS media ABI guards; runtime fixture requires directory and media file\n";return 0;}
        require(sevk_media_create(argv[1],&media)==0,"load bundled runtime");
        require(sevk_media_play(media,7,argv[2],.5f)==0,"async open");
        require(sevk_media_volume(media,7,std::numeric_limits<float>::quiet_NaN())<0,"nonfinite volume rejected");
        require(sevk_media_volume(media,7,.2f)==0,"volume while opening");
        uint64_t sequence=0,observed=0;bool ended=false;
        bool fixture=argc>3 && std::strcmp(argv[3],"--fixture")==0;
        auto timeout=std::chrono::steady_clock::now()+15s;
        while(std::chrono::steady_clock::now()<timeout){
            auto f=update(media,7);
            if(f.state==3)throw std::runtime_error(f.error);
            if(f.sequence!=sequence){require(f.sequence>sequence,"frame generations monotonic");require(f.width>0 && f.height>0 && f.pixels,"frame dimensions/pointer");
                const auto* rgba=static_cast<const unsigned char*>(f.pixels);for(size_t a=3;a<size_t(f.width)*f.height*4;a+=4)require(rgba[a]==255,"opaque video alpha");
                if(observed==0){
                    if(fixture){
                        require(f.width==160 && f.height==96,"generated fixture dimensions");
                        bool bgra=(f.reserved&8)!=0;size_t red=bgra?2:0,blue=bgra?0:2;
                        auto pixel=[&](size_t x,size_t y,size_t channel){return rgba[(y*f.width+x)*4+channel];};
                        require(pixel(20,85,red)>180 && pixel(20,85,blue)<80,"fixture red channel");
                        require(pixel(145,80,blue)>180 && pixel(145,80,red)<80,"fixture cyan channel");
                        require(pixel(20,85,red)>pixel(20,28,red)+80,"fixture is top-down, not vertically flipped");
                    }
                    // Workers may replace the latest unpublished slot, but
                    // cannot overwrite a frame pinned by the C ABI consumer.
                    size_t bytes=size_t(f.width)*f.height*4;uint64_t before=0,after=0;
                    for(size_t i=0;i<bytes;++i)before=before*33+rgba[i];
                    std::this_thread::sleep_for(250ms);
                    for(size_t i=0;i<bytes;++i)after=after*33+rgba[i];
                    require(before==after,"retained frame remains stable while worker publishes");
                }
                sequence=f.sequence;++observed;}
            if(f.state==2){ended=true;break;}std::this_thread::sleep_for(5ms);
        }
        require(ended && observed>=3,"moving fixture progresses to natural EOF");
        require(sevk_media_close(media,7)==0,"close completed player");SevkMediaFrame missing{};missing.size=sizeof(missing);missing.abi_version=1;
        require(sevk_media_update(media,7,&missing)==1,"closed ID absent");
        for(int i=0;i<20;++i){require(sevk_media_play(media,7,argv[2],0)==0,"replace/reopen");if(i%2==0)sevk_media_close(media,7);}
        sevk_media_close(media,7);
        require(sevk_media_play(media,8,"/no/such/video.wmv",1)==0,"invalid file opens asynchronously");
        timeout=std::chrono::steady_clock::now()+5s;bool failed=false;
        while(std::chrono::steady_clock::now()<timeout){auto f=update(media,8);if(f.state==3){require(*f.error,"decode failure has diagnostics");failed=true;break;}std::this_thread::sleep_for(5ms);}
        require(failed,"missing media reaches terminal failure");
        sevk_media_destroy(media);media=nullptr;std::cout<<"PASS native media playback, EOF, volume, cancellation, replacement, alpha and failure ("<<observed<<" frames)\n";return 0;
    }catch(const std::exception& e){if(media)sevk_media_destroy(media);std::cerr<<e.what()<<'\n';return 1;}
}
