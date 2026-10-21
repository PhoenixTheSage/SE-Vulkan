#include "../src/native_pass_scene.h"
#include <chrono>
#include <cstdio>
#include <random>

// Publication benchmark at the private Pertam scale: ~1,700 actors across
// colour, cascade, forward and spot views. The signature covers every
// published group, stream byte, owner, identity and request, so a change to
// publish() must reproduce it exactly.
using namespace native_pass;
static uint64_t mix(uint64_t h,uint64_t v){h^=v;h*=1099511628211ull;return h;}
static uint64_t signature(Scene& scene){
    uint64_t h=14695981039346656037ull;
    for(auto& g:scene.render_groups()){auto& k=g.key;for(uint64_t v:{uint64_t(k.view),uint64_t(k.shader),uint64_t(k.first),uint64_t(k.count),k.mesh,k.colour,k.normal,k.extensions,k.alpha,uint64_t(k.masked),uint64_t(k.double_sided),uint64_t(g.kind),uint64_t(g.main_part),uint64_t(g.stream->index)})h=mix(h,v);}
    for(auto& s:scene.instance_streams()){h=mix(h,s->index);for(uint8_t b:s->instances)h=mix(h,b);for(auto o:s->owners)h=mix(h,o);for(auto i:s->identities)h=mix(h,i);}
    for(auto o:scene.main_visible_owners())h=mix(h,o);for(auto& t:scene.transparent_requests()){h=mix(h,t.actor);h=mix(h,t.owner);h=mix(h,t.lod);h=mix(h,std::bit_cast<uint32_t>(t.alpha));}
    h=mix(h,scene.static_spot_updates());return h;
}
int main(int argc,char** argv){
    bool parallel=argc>1&&std::string(argv[1])=="parallel";
    Scene scene;scene.set_parallel(parallel);std::mt19937 random(0x5EED);std::uniform_real_distribution<double> unit(0,1);
    std::vector<uint64_t> families;
    for(int f=0;f<48;f++){auto family=scene.register_family({30,90});families.push_back(family);
        for(uint32_t lod=0;lod<3;lod++){Model model;uint32_t parts=2+f%5;for(uint32_t p=0;p<parts;p++){Part part{uint64_t(100+f),uint64_t(1000+f*8+p),uint64_t(2000+p),uint64_t(3000+p),p==parts-1?uint64_t(4000+f):0,p*30,30,p+1,p%3,p==parts-1,p%4==1,true,p%3!=2};model.parts.push_back(part);}
            model.glass=f%7==0;scene.bind_level(family,lod,scene.register_model(model));}}
    std::vector<uint64_t> handles;
    for(uint32_t i=0;i<1800;i++){Actor a{};a.owner=i+10;a.family=families[i%families.size()];a.group=1+i/24;double x=unit(random)*400-200,y=unit(random)*60-30,z=-unit(random)*300;
        a.selection.translation={x,y,z};a.selection.bounds={{x-1.5,y-1.5,z-1.5},{x+1.5,y+1.5,z+1.5}};a.dynamic=i%40==0;a.key_colour[0]=uint16_t(0x3800+i%16);
        if(i%11==0)a.appearance={{uint32_t(1+i%3),{0x3800,0x3c00,0x3400,0x3c00},true}};if(i%97==0)a.appearance.push_back({uint32_t(2),{0x3c00,0x3c00,0x3c00,0x3c00},false});
        handles.push_back(scene.register_actor(a));}
    Frame frame{};frame.selection.coefficient=1;frame.selection.delta=1.f/60;frame.selection.global.maximum_seconds=1;frame.selection.global.minimum_seconds=.25f;
    frame.views.push_back({0,Kind::Colour});for(uint32_t c=1;c<=3;c++){View v{c,Kind::Depth};v.minimum_size_squared=c>2?20:0;frame.views.push_back(v);}
    {View v{9,Kind::Forward};v.maximum_distance=150;frame.views.push_back(v);}
    for(uint32_t s=0;s<4;s++){View st{15+s*2,Kind::Depth};st.only_static=true;View dy{16+s*2,Kind::Depth};dy.only_dynamic=true;frame.views.push_back(st);frame.views.push_back(dy);}
    std::vector<QueryResult> results;for(uint64_t g=1;g<=80;g+=5)results.push_back({g,(g%3)?uint64_t(1):uint64_t(0x6)});
    uint64_t total=14695981039346656037ull;double best=1e9,sum=0;const int frames=300;
    for(int n=0;n<frames;n++){
        frame.selection.camera={std::sin(n*.01)*3,0,std::cos(n*.013)*2};
        if(n%50==25){auto a=scene.actor_state(handles[n]);a.selection.translation.x+=.5;scene.update_actor(handles[n],a);}
        auto start=std::chrono::steady_clock::now();scene.prepare(frame);scene.publish(results);auto end=std::chrono::steady_clock::now();
        double us=std::chrono::duration<double,std::micro>(end-start).count();if(n>=20){sum+=us;best=std::min(best,us);}total=mix(total,signature(scene));
    }
    scene.profiling=true;scene.profile={};for(int n=0;n<60;n++){frame.selection.camera={std::sin(n*.01)*3,0,std::cos(n*.013)*2};scene.prepare(frame);scene.publish(results);}
    auto& p=scene.profile;std::printf("profile per frame: prepare=%.1f requests=%.1f publish=%.1f emit=%.1f pack=%.1f spots=%.1f rows=%.1f bytes=%.1f\n",p.prepare_us/60,p.requests_us/60,p.publish_us/60,p.emit_us/60,p.pack_us/60,p.spots_us/60,double(p.emitted_rows)/60,double(p.stream_bytes)/60);
    std::printf("native pass publish %s: groups=%zu streams=%zu mean=%.1fus best=%.1fus signature=%016llx\n",parallel?"parallel":"serial",scene.render_groups().size(),scene.instance_streams().size(),sum/(frames-20),best,(unsigned long long)total);
}
