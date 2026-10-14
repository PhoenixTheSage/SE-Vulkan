#include "se_vulkan.h"
#include <array>
#include <chrono>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <thread>
#include <vector>
static void ok(int result,void* context){if(result)throw std::runtime_error(sevk_last_error(context));}
static void require(bool condition,const char* message){if(!condition)throw std::runtime_error(message);}
int main(int argc,char** argv){
    void* context{};void* media{};
    try{
        SevkProbe probe{};probe.size=sizeof(probe);probe.abi_version=1;
        if(sevk_probe(&probe)<0 || !probe.compatible_device_count)return 77;
        require(argc>1,"shader path required");
        SevkCreateInfo create{};create.size=sizeof(create);create.abi_version=1;create.width=create.height=32;create.shader_directory=argv[1];create.enable_validation=1;
        ok(sevk_create(&create,&context),context);
        SevkFrameInfo frame{};frame.view_projection[0]=frame.view_projection[5]=frame.view_projection[10]=frame.view_projection[15]=1;frame.clear_color[3]=1;
        SevkSprite sprite{};sprite.texture_id=71;sprite.destination[2]=sprite.destination[3]=32;sprite.uv[2]=sprite.uv[3]=1;
        sprite.color[0]=sprite.color[1]=sprite.color[2]=sprite.color[3]=1;sprite.clip[2]=sprite.clip[3]=-1;sprite.flags=2;
        std::vector<unsigned char> pixels(32*32*4),result(pixels.size());
        for(int f=0;f<100;++f){
            for(size_t i=0;i<pixels.size();i+=4){pixels[i]=(f%2)?255:0;pixels[i+1]=(f%2)?0:255;pixels[i+2]=0;pixels[i+3]=255;}
            ok(sevk_begin_uploads(context),context);ok(sevk_update_stream_texture(context,71,32,32,1,pixels.data(),pixels.size()),context);ok(sevk_end_uploads(context),context);
            ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&sprite,1),context);ok(sevk_request_readback(context),context);ok(sevk_end_frame(context),context);
            if(f%13==0){ok(sevk_readback(context,result.data(),result.size()),context);size_t center=(16*32+16)*4;require(result[center+2]==pixels[center] && result[center+1]==pixels[center+1],"BGRA streamed frame reaches correctly ordered GPU pixels");}
            SevkStats stats{};stats.size=sizeof(stats);stats.abi_version=1;ok(sevk_get_stats(context,&stats),context);require(stats.textures==2,"streaming reuses texture allocation (plus renderer white texture)");require(stats.validation_errors==0,"streaming synchronization validation clean");
        }
        // Reallocate dimensions while older submissions can still reference the old image.
        pixels.resize(16*16*4,255);ok(sevk_begin_uploads(context),context);ok(sevk_update_stream_texture(context,71,16,16,0,pixels.data(),pixels.size()),context);ok(sevk_end_uploads(context),context);
        ok(sevk_resize(context,48,48,0),context);
        sprite.destination[2]=sprite.destination[3]=48;result.resize(48*48*4);
        ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&sprite,1),context);ok(sevk_request_readback(context),context);ok(sevk_end_frame(context),context);
        ok(sevk_remove_resource(context,0,71),context);
        ok(sevk_readback(context,result.data(),result.size()),context);
        size_t resized_center=(24*48+24)*4;require(result[resized_center]==255 && result[resized_center+1]==0 && result[resized_center+2]==0 && result[resized_center+3]==255,"replacement format reaches resized readback before retirement");
        SevkStats final{};final.size=sizeof(final);final.abi_version=1;ok(sevk_get_stats(context,&final),context);require(final.validation_errors==0 && final.textures==1,"resize/close with frames in flight remains clean");
        if(argc>3){
            require(sevk_media_create(argv[2],&media)==0,"load decoder for actual frame-to-GPU test");
            require(sevk_media_play(media,7,argv[3],0)==0,"open generated GPU fixture");
            uint64_t sequence=0,first_hash=0;unsigned decoded=0;bool changed=false,ended=false;
            auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(15);
            while(std::chrono::steady_clock::now()<deadline){
                SevkMediaFrame decoded_frame{};decoded_frame.size=sizeof(decoded_frame);decoded_frame.abi_version=1;
                require(sevk_media_update(media,7,&decoded_frame)==0 && decoded_frame.state!=3,"decode GPU fixture");
                if(decoded_frame.sequence!=sequence){
                    sequence=decoded_frame.sequence;require(decoded_frame.width==160 && decoded_frame.height==96,"decoded GPU fixture dimensions");
                    ok(sevk_begin_uploads(context),context);ok(sevk_update_stream_texture(context,72,160,96,(decoded_frame.reserved&8)?1:0,decoded_frame.pixels,160*96*4),context);ok(sevk_end_uploads(context),context);
                    sprite.texture_id=72;ok(sevk_begin_frame(context,&frame),context);ok(sevk_draw_sprites(context,&sprite,1),context);ok(sevk_request_readback(context),context);ok(sevk_end_frame(context),context);ok(sevk_readback(context,result.data(),result.size()),context);
                    for(size_t alpha=3;alpha<result.size();alpha+=4)require(result[alpha]==255,"decoded GPU alpha remains opaque");
                    uint64_t hash=0;for(auto pixel:result)hash=hash*33+pixel;
                    if(!decoded){first_hash=hash;require(result[(42*48+6)*4]>result[(14*48+6)*4]+80,"decoded orientation survives GPU composition");}
                    else changed|=hash!=first_hash;
                    ++decoded;
                }
                if(decoded_frame.state==2){ended=true;break;}
                std::this_thread::sleep_for(std::chrono::milliseconds(5));
            }
            require(ended && decoded>=3 && changed,"changing decoded WMV frames reach GPU readback and EOF");
            sevk_media_destroy(media);media=nullptr;ok(sevk_remove_resource(context,0,72),context);
            ok(sevk_get_stats(context,&final),context);require(final.validation_errors==0,"decoded frame upload/composition validation clean");
            std::cout<<"PASS "<<decoded<<" decoded WMV frames reach changing GPU pixels with top-down orientation, opaque alpha and clean validation\n";
        }
        sevk_destroy(context);context=nullptr;
        std::cout<<"PASS streaming media GPU pixels, stable image count, in-flight replacement, resize and retirement\n";return 0;
    }catch(const std::exception& e){if(media)sevk_media_destroy(media);if(context)sevk_destroy(context);std::cerr<<e.what()<<'\n';return 1;}
}
