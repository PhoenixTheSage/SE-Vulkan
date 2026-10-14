#include "se_vulkan.h"
#include "../src/native_pass_scene.h"
#include "../src/stock_proxy_lod.h"
#include "../src/gpu_opaque_view_batches.h"
#include <iostream>
#include <vector>
#include <set>
#include <tuple>
#include <cstring>
#include <cstdlib>
uint64_t checks=0;
void require(bool value,const char* message){checks++;if(!value)throw std::runtime_error(message);}
void ok(int32_t value,void* ctx){if(value)throw std::runtime_error(sevk_last_error(ctx));}
void identity(float* p){std::memset(p,0,64);p[0]=p[5]=p[10]=p[15]=1;}
scene_actor::Vec3 vec(const double* p){return {p[0],p[1],p[2]};}
bool member(const SevkOpaqueActor& a,const SevkOpaqueView& v,const SevkOpaqueFrame& f){
    if(!(a.flags&1)||!(v.flags&1)||((v.flags&2)&&!(a.flags&16))||((v.flags&8)&&!(a.flags&8))||((v.flags&16)&&(a.flags&8)))return false;
    scene_actor::Bounds box{vec(a.minimum),vec(a.maximum)};scene_actor::Frustum frustum;frustum.enabled=(v.flags&4)!=0;
    for(uint32_t i=0;i<6;i++)frustum.planes[i]={v.planes[i*4],v.planes[i*4+1],v.planes[i*4+2],v.planes[i*4+3]};
    if(scene_actor::disjoint(box,frustum))return false;
    double size=0,squared=0;for(uint32_t i=0;i<3;i++){double extent=a.maximum[i]-a.minimum[i],d=(a.minimum[i]+a.maximum[i])*.5-f.camera[i];size+=extent*extent;squared+=d*d;}
    if((v.flags&2)&&size<=v.minimum_size_squared)return false;
    return !v.maximum_distance||std::sqrt(squared)<=v.maximum_distance+std::sqrt(size)*.5;
}
float distance(const SevkOpaqueActor& a,const SevkOpaqueFrame& f){
    float squared=0;
    if(a.flags&2){auto p=vec(f.camera);scene_actor::Bounds box{vec(a.minimum),vec(a.maximum)};
        if(a.flags&4){auto* m=a.inverse;p={m[0]*p.x+m[1]*p.y+m[2]*p.z+m[3],m[4]*p.x+m[5]*p.y+m[6]*p.z+m[7],m[8]*p.x+m[9]*p.y+m[10]*p.z+m[11]};box={vec(a.local_minimum),vec(a.local_maximum)};}
        squared=float(resident_proxy::squared_distance(box,p));
    }else{float x=float(a.translation[0]-f.camera[0]),y=float(a.translation[1]-f.camera[1]),z=float(a.translation[2]-f.camera[2]);squared=x*x+y*y+z*z;}
    return (std::sqrt(squared)/((a.flags&2)?1:a.scale)*f.coefficient+f.distance_add)*f.distance_mult;
}
int main(int argc,char** argv){void* ctx=nullptr;try{
    SevkProbe probe{sizeof(probe)};
    if(sevk_probe(&probe)!=0||!probe.validation_available){std::cout<<"GPU opaque verification unavailable: compatible device and active Khronos validation are required\n";return 77;}
#if defined(_WIN32)
    _putenv_s("SE_VULKAN_GPU_OPAQUE","1");
#else
    setenv("SE_VULKAN_GPU_OPAQUE","1",1);
#endif
    SevkCreateInfo create{};create.size=sizeof(create);create.abi_version=SEVK_ABI_VERSION;create.width=create.height=96;create.enable_validation=1;create.shader_directory=argc>1?argv[1]:"shaders";
    ok(sevk_create(&create,&ctx),ctx);SevkGpuOpaqueCapabilities caps{sizeof(caps),SEVK_ABI_VERSION};ok(sevk_get_gpu_opaque_capabilities(ctx,&caps),ctx);
    if(!caps.enabled){std::cout<<"Opaque reference unavailable: missing="<<caps.missing_features<<" limits="<<caps.insufficient_limits<<"\n";sevk_destroy(ctx);return 77;}
    constexpr uint32_t actor_count=4,view_count=3,ref_capacity=64,draw_capacity=24;
    constexpr double origin=3939580.123456789;
    SevkOpaqueActor actors[actor_count]{};
    for(uint32_t a=0;a<actor_count;a++){
        auto& x=actors[a];x.translation[0]=origin+(a==0?-.35:a==1?.35:0);x.translation[1]=a==2?.4:-.2;x.translation[2]=.5;
        for(uint32_t k=0;k<3;k++){x.minimum[k]=x.translation[k]-.1;x.maximum[k]=x.translation[k]+.1;x.local_minimum[k]=-.1;x.local_maximum[k]=.1;}
        x.basis[0]=x.basis[4]=x.basis[8]=1;x.inverse[0]=x.inverse[5]=x.inverse[10]=1;x.inverse[3]=-origin;x.scale=1;x.colour[a]=1;x.colour[3]=1;
        if(a==3){x.colour[0]=x.colour[2]=1;x.translation[1]=.6;x.minimum[1]=.5;x.maximum[1]=.7;}
        x.owner=a+100;x.generation=1;x.family=a>=2?a-1:0;x.flags=1|16|(a>=2?2|4:0)|(a==1?8:0)|(a==3?32:0);x.source_first=a==3?4:2;x.source_count=a>=2?2:1;
    }
    SevkOpaqueFamily families[3]{{1,0,2,1,{.7f}},{1,2,2,1,{.7f}},{1,4,2,1,{.7f}}};
    SevkOpaquePart parts[6]{{0,0,0,3,10,3,1,0},{0,1,3,3,11,3,1,0},{1,0,0,3,12,3,1,0},{1,1,3,3,13,3,1,0},{2,0,0,3,12,3,1,0},{2,1,3,3,13,3,1,0}};
    float vertices[]{-.18f,-.14f,0,.18f,-.14f,0,0,.18f,0, -.14f,-.1f,0,.14f,-.1f,0,0,.14f,0};uint32_t indices[]{0,1,2,3,4,5};
    float source[96]{};for(uint32_t i=0;i<4;i++){source[i*16]=source[i*16+5]=source[i*16+10]=1;source[i*16+3]=i==2?-.12f:i==3?.12f:20;}
    source[4*16+8]=-.28;source[4*16+11]=4;source[5*16+8]=.28;source[5*16+11]=2;
    SevkOpaqueReferenceConfig config{sizeof(config),SEVK_ABI_VERSION,actor_count,3,6,view_count,6,6,6,ref_capacity,draw_capacity,0,actors,families,parts,vertices,indices,reinterpret_cast<uint8_t*>(source)};
    uint32_t grey=0xff404040;ok(sevk_upload_texture(ctx,42,1,1,0,0,&grey,4),ctx);uint64_t textures[14]{};textures[10]=textures[11]=42;config.material_textures=textures;config.material_count=14;
    ok(sevk_opaque_reference_init(ctx,&config),ctx);
    auto invalid_config=config;invalid_config.reference_capacity=1;
    require(sevk_opaque_reference_init(ctx,&invalid_config)<0,"failed candidate registration preserves previous complete scene epoch");
    auto invalid=actors[0];invalid.family=99;require(sevk_opaque_reference_actor(ctx,0,&invalid)<0,"reject invalid commit without changing epoch");
    SevkOpaqueView views[view_count]{};for(auto& v:views){identity(v.projection);v.flags=1;}views[1].flags=1|2;views[2].flags=1|2|8;views[1].projection[10]=views[2].projection[10]=.2f;
    scene_actor::LodState new_states[2];resident_proxy::Lod old_states[2];scene_actor::Vec3 last[actor_count]{};bool have[actor_count]{};
    std::vector<SevkOpaqueSelector> selectors(actor_count);std::vector<SevkOpaqueSelection> selected(actor_count*view_count);
    std::vector<SevkOpaqueReference> refs(ref_capacity);std::vector<SevkOpaqueIndirect> args(draw_capacity*view_count);std::vector<SevkOpaqueDrawRecord> records(draw_capacity*view_count);uint32_t counts[view_count]{},errors{};
    std::set<std::tuple<uint32_t,uint32_t,uint32_t,uint32_t>> expected,actual;
    std::vector<float> retained_depth;double retained_camera[3]{};float retained_projection[16]{};
    SevkFrameInfo output{};identity(output.view_projection);output.clear_color[3]=1;
    for(uint32_t frame_index=0;frame_index<44;frame_index++){
        SevkOpaqueFrame frame{};frame.camera[0]=origin;frame.camera[2]=frame_index>=4&&frame_index<16?-.6:0;
        frame.smooth_camera_distance=10;frame.delta=.06f;frame.coefficient=frame.distance_mult=1;frame.maximum_seconds=1;frame.minimum_seconds=.25;frame.flags=1;frame.epoch=frame_index+1;
        if(frame_index==10){frame.flags|=2;frame.selection=1;}if(frame_index==11)frame.flags=0;if(frame_index==12)frame.smooth_camera_distance=.01;
        views[1].flags=frame_index==15?2:1|2;views[2].flags=frame_index==16?1|2|16:1|2|8;
        views[1].projection[0]=frame_index==14?.9f:frame_index==15?.4f:1;
        if(frame_index>=23&&frame_index<=28){views[1].flags|=4;views[1].planes[0]=1;views[1].planes[3]=-origin;}
        views[2].minimum_size_squared=frame_index==29?.13:0;views[2].maximum_distance=frame_index>=30&&frame_index<34?.1:0;
        if(frame_index==17||frame_index==18){actors[0].flags=frame_index==17?0:1|16;ok(sevk_opaque_reference_actor(ctx,0,&actors[0]),ctx);}
        if(frame_index==19){actors[1].generation++;actors[1].colour[1]=.5f;new_states[1]={};have[1]=false;ok(sevk_opaque_reference_actor(ctx,1,&actors[1]),ctx);}
        if(frame_index==20){ok(sevk_resize(ctx,80,80,0),ctx);}
        // The independent CPU oracle runs only in the test executable.
        expected.clear();std::vector<SevkOpaqueSelection> oracle(actor_count*view_count);
        scene_actor::GlobalLod global{};global.updates=(frame.flags&1)!=0;global.explicit_lod=(frame.flags&2)!=0;global.selection=frame.selection;global.maximum_seconds=frame.maximum_seconds;global.minimum_seconds=frame.minimum_seconds;
        for(uint32_t a=0;a<actor_count;a++){
            bool any=false,main=false;for(uint32_t v=0;v<view_count;v++){bool m=member(actors[a],views[v],frame);any|=m;main|=m&&!(views[v].flags&2);}
            float d=distance(actors[a],frame);auto threshold=std::span<const float>(families[actors[a].family].thresholds,1);
            if(any){if(a<2){auto camera=vec(frame.camera);double dx=last[a].x-camera.x,dy=last[a].y-camera.y,dz=last[a].z-camera.z;bool jump=!have[a]||std::sqrt(dx*dx+dy*dy+dz*dz)>frame.smooth_camera_distance;new_states[a].update(d,threshold,global,frame.delta,jump);}
                else old_states[a-2].advance(d,threshold,global,{views[0].minimum_lod,views[0].shift,views[0].visible_shift},frame.delta);last[a]=vec(frame.camera);have[a]=true;}
            for(uint32_t v=0;v<view_count;v++){
                auto& s=oracle[a*view_count+v];s.current=s.other=-1;if(member(actors[a],views[v],frame)){
                    if(a<2){auto n=new_states[a];int shift=main?views[v].visible_shift:views[v].shift;s.current=std::clamp(std::max(views[v].minimum_lod,n.current+shift),0,1);if(!(views[v].flags&2)&&n.previous>=0&&n.transition>0){s.other=std::clamp(std::max(views[v].minimum_lod,n.previous+shift),0,1);s.alpha=1-n.transition;s.other_alpha=2+n.transition;}}
                    else {auto old_state=old_states[a-2];s.current=old_state.current;if(old_state.transition>0){s.other=old_state.next;s.alpha=old_state.transition;s.other_alpha=3-old_state.transition;}}
                }
                for(uint32_t p=0;p<6;p++)if(parts[p].family==actors[a].family)for(uint32_t slice=0;slice<2;slice++)if((slice==0?s.current:s.other)==int(parts[p].lod)){
                    for(uint32_t instance=0;instance<actors[a].source_count;instance++)expected.emplace(v,a,p,instance);}
            }
        }
        ok(sevk_opaque_reference_frame(ctx,&frame,views,view_count),ctx);ok(sevk_begin_frame(ctx,&output),ctx);ok(sevk_request_readback(ctx),ctx);ok(sevk_end_frame(ctx),ctx);
        ok(sevk_opaque_reference_readback(ctx,selectors.data(),actor_count,selected.data(),uint32_t(selected.size()),refs.data(),ref_capacity,args.data(),records.data(),uint32_t(args.size()),counts,view_count,&errors),ctx);
        require(errors==0,"bounded GPU emission did not overflow");
        auto candidate_families=gpu_opaque::recording_family_batches(parts,config.family_count);
        auto candidate_batches=gpu_opaque::recording_batches(actors,candidate_families,views,1);
        for(uint32_t i=0;i<selected.size();i++){require(selected[i].current==oracle[i].current&&selected[i].other==oracle[i].other,"GPU view LOD disagrees with stock CPU oracle");require(std::fabs(selected[i].alpha-oracle[i].alpha)<1e-6f&&std::fabs(selected[i].other_alpha-oracle[i].other_alpha)<1e-6f,"GPU transition disagrees with stock oracle");}
        actual.clear();for(uint32_t v=0;v<view_count;v++){require(counts[v]<=draw_capacity,"GPU actual draw count bounded");for(uint32_t d=0;d<counts[v];d++){
            auto& arg=args[v*draw_capacity+d];auto& record=records[v*draw_capacity+d];require(arg.index_count==parts[record.part].index_count&&arg.first_index==parts[record.part].first_index,"GPU geometry ranges preserve immutable coverage");
            require(std::find(candidate_batches[v].begin(),candidate_batches[v].end(),parts[record.part].reserved)!=candidate_batches[v].end(),"conservative recording list excluded a GPU-selected draw");
            if(record.old){require(arg.first_instance==actors[record.actor].source_first&&arg.instance_count==actors[record.actor].source_count,"OLD GPU arguments reference original source subrange");for(uint32_t i=0;i<arg.instance_count;i++)actual.emplace(v,record.actor,record.part,i);}
            else for(uint32_t i=0;i<arg.instance_count;i++){auto& ref=refs[arg.first_instance+i];require(ref.view==v&&ref.material==parts[record.part].material,"NEW grouped indexed material/view preserved");actual.emplace(v,ref.actor,record.part,0);}
            auto& ref=refs[record.reference];require(ref.rows[3]==float(actors[ref.actor].translation[0]-frame.camera[0])&&ref.rows[7]==float(actors[ref.actor].translation[1]-frame.camera[1]),"camera subtraction retains world-scale precision");
        }}require(actual==expected,"GPU owner/LOD/range/instance coverage matches CPU oracle");
        uint32_t width=frame_index<20?96:80;std::vector<uint8_t> image(width*width*4);ok(sevk_readback(ctx,image.data(),image.size()),ctx);
        if(frame_index==0){uint32_t red=0,green=0,blue=0,purple=0;for(uint32_t i=0;i<image.size();i+=4){red+=image[i]>=135&&image[i]<=139&&image[i+1]<10;green+=image[i+1]>=135&&image[i+1]<=139&&image[i]<10;blue+=image[i+2]>240&&image[i]<10;purple+=image[i]>240&&image[i+2]>240&&image[i+1]<10;}require(red>20&&green>20&&blue>20&&purple>20,"indirect NEW, generic and rigid-cube rasterization preserves nonuniform indexed materials");}
        if(frame_index==0||frame_index==14||frame_index==15||frame_index==16||frame_index==20){
            std::vector<float> depth_image(width*width);double depth_camera[3]{};float depth_projection[16]{};
            ok(sevk_opaque_reference_depth(ctx,1,depth_image.data(),uint32_t(depth_image.size()),depth_camera,depth_projection),ctx);
            require(std::count_if(depth_image.begin(),depth_image.end(),[](float d){return d>=0&&d<1;})>20,"GPU depth indirect batch has nonempty caster silhouettes");
            if(frame_index==14){retained_depth=depth_image;std::memcpy(retained_camera,depth_camera,sizeof(depth_camera));std::memcpy(retained_projection,depth_projection,sizeof(depth_projection));}
            if(frame_index==15){require(depth_image==retained_depth&&std::memcmp(depth_camera,retained_camera,sizeof(depth_camera))==0&&std::memcmp(depth_projection,retained_projection,sizeof(depth_projection))==0,"unscheduled depth image retains its rendering matrix and camera");require(depth_projection[0]!=views[1].projection[0],"retained depth does not publish an unrendered current matrix");}
            else require(std::memcmp(depth_camera,frame.camera,sizeof(depth_camera))==0&&std::memcmp(depth_projection,views[1].projection,sizeof(depth_projection))==0,"updated depth stores its actual rendering matrix/camera");
        }
        // No frame/readback advances selectors a second time at the same epoch.
        if(frame_index==22){auto before=selectors;ok(sevk_opaque_reference_frame(ctx,&frame,views,view_count),ctx);ok(sevk_begin_frame(ctx,&output),ctx);ok(sevk_end_frame(ctx),ctx);ok(sevk_opaque_reference_readback(ctx,selectors.data(),actor_count,selected.data(),uint32_t(selected.size()),refs.data(),ref_capacity,args.data(),records.data(),uint32_t(args.size()),counts,view_count,&errors),ctx);require(std::memcmp(before.data(),selectors.data(),before.size()*sizeof(SevkOpaqueSelector))==0,"selectors advance once per epoch");}
    }
    for(uint32_t replacement=0;replacement<2;replacement++){
        ok(sevk_begin_uploads(ctx),ctx);grey=replacement==0?0xff808080:0xff404040;ok(sevk_upload_texture(ctx,42,1,1,0,0,&grey,4),ctx);ok(sevk_end_uploads(ctx),ctx);
        SevkOpaqueFrame frame{};frame.camera[0]=origin;frame.epoch=1000+replacement;frame.flags=1;frame.coefficient=frame.distance_mult=1;frame.maximum_seconds=1;frame.minimum_seconds=.25;frame.smooth_camera_distance=10;
        ok(sevk_opaque_reference_frame(ctx,&frame,views,view_count),ctx);ok(sevk_begin_frame(ctx,&output),ctx);ok(sevk_request_readback(ctx),ctx);ok(sevk_end_frame(ctx),ctx);
    }
    std::vector<uint8_t> replaced_image(80*80*4);ok(sevk_readback(ctx,replaced_image.data(),replaced_image.size()),ctx);uint32_t replaced_red=0;
    for(uint32_t i=0;i<replaced_image.size();i+=4)replaced_red+=replaced_image[i]>=135&&replaced_image[i]<=139&&replaced_image[i+1]<10;
    require(replaced_red>20,"fenced descriptor tables refresh replacement textures on both slots without stale material reads");
    // Multiple workgroups must contribute to the SAME compatible NEW group.
    // This catches missing inter-dispatch visibility and compaction races that
    // four-actor fixtures cannot exercise.
    constexpr uint32_t many_count=130,many_refs=many_count*4*view_count;
    std::vector<SevkOpaqueActor> many(many_count);
    for(uint32_t a=0;a<many_count;a++){many[a]=actors[0];many[a].owner=1000+a;many[a].generation=1;many[a].flags=1|16|((a&1)?8:0);
        many[a].translation[0]=origin+(int(a%13)-6)*.1;many[a].translation[1]=(int(a/13)-5)*.1;
        for(uint32_t axis=0;axis<3;axis++){many[a].minimum[axis]=many[a].translation[axis]-.05;many[a].maximum[axis]=many[a].translation[axis]+.05;}}
    auto many_config=config;many_config.actors=many.data();many_config.actor_count=many_count;many_config.reference_capacity=many_refs;ok(sevk_opaque_reference_init(ctx,&many_config),ctx);
    selectors.resize(many_count);selected.resize(many_count*view_count);refs.resize(many_refs);
    views[1].flags=1|2;views[2].flags=1|2|8;views[2].minimum_size_squared=views[2].maximum_distance=0;
    for(uint32_t pass=0;pass<2;pass++){
        if(pass){many[128].translation[0]+=.03125;many[128].minimum[0]+=.03125;many[128].maximum[0]+=.03125;ok(sevk_opaque_reference_actor(ctx,128,&many[128]),ctx);}
        SevkOpaqueFrame frame{};frame.camera[0]=origin;frame.epoch=2000+pass;frame.flags=3;frame.coefficient=frame.distance_mult=1;frame.maximum_seconds=1;frame.minimum_seconds=.25;frame.smooth_camera_distance=10;
        ok(sevk_opaque_reference_frame(ctx,&frame,views,view_count),ctx);ok(sevk_begin_frame(ctx,&output),ctx);ok(sevk_end_frame(ctx),ctx);
        ok(sevk_opaque_reference_readback(ctx,selectors.data(),many_count,selected.data(),uint32_t(selected.size()),refs.data(),many_refs,args.data(),records.data(),uint32_t(args.size()),counts,view_count,&errors),ctx);
        require(!errors,"multiple workgroup bounded emission");
        for(uint32_t v=0;v<view_count;v++){require(counts[v]==1,"compatible NEW actors are one GPU group per view, independent of spatial positions");auto& arg=args[v*draw_capacity];require(arg.instance_count==(v==2?65:many_count),"GPU group instance count includes every contributing workgroup");
            std::set<uint32_t> membership;for(uint32_t i=0;i<arg.instance_count;i++){auto& r=refs[arg.first_instance+i];require(r.actor<many_count&&r.view==v,"compacted view reference within admitted capacity");require(membership.insert(r.actor).second,"each actor emitted once into its view group");require(r.rows[3]==float(many[r.actor].translation[0]-origin),"committed transform updated in the correct fenced slot");}
        }
    }
    // Exercise more than 64 scan blocks and the final partial block. Most views
    // are disabled, so empty groups also carry offsets across superblocks.
    constexpr uint32_t scan_parts=32768,scan_views=64,scan_refs=scan_parts*4,scan_draws=scan_parts*2,scan_batches=320;
    SevkOpaqueFamily scan_family{1,0,scan_parts,0,{}};std::vector<SevkOpaquePart> scan_geometry(scan_parts);
    for(uint32_t i=0;i<scan_parts;i++)scan_geometry[i]={0,0,0,3,10,3,1,i%scan_batches};auto scan_actor=many[0];scan_actor.family=0;
    auto scan_config=config;scan_config.actors=&scan_actor;scan_config.actor_count=1;scan_config.families=&scan_family;scan_config.family_count=1;scan_config.parts=scan_geometry.data();scan_config.part_count=scan_parts;scan_config.view_count=scan_views;scan_config.reference_capacity=scan_refs;scan_config.draw_capacity=scan_draws;scan_config.reserved=scan_batches;
    ok(sevk_opaque_reference_init(ctx,&scan_config),ctx);std::vector<SevkOpaqueView> scan_inputs(scan_views);for(auto& v:scan_inputs)for(uint32_t i=0;i<16;i++)v.projection[i]=(i%5==0)?1:0;scan_inputs[0].flags=1;scan_inputs.back().flags=3;
    SevkOpaqueFrame scan_frame{};scan_frame.camera[0]=origin;scan_frame.epoch=3000;scan_frame.flags=3;scan_frame.coefficient=scan_frame.distance_mult=1;scan_frame.maximum_seconds=1;scan_frame.minimum_seconds=.25;scan_frame.smooth_camera_distance=10;
    ok(sevk_opaque_reference_frame(ctx,&scan_frame,scan_inputs.data(),scan_views),ctx);ok(sevk_begin_frame(ctx,&output),ctx);ok(sevk_end_frame(ctx),ctx);
    selectors.resize(1);selected.resize(scan_views);refs.resize(scan_refs);args.resize(scan_views*scan_draws);records.resize(scan_views*scan_draws);std::vector<uint32_t> scan_counts(scan_views*scan_batches);
    ok(sevk_opaque_reference_readback(ctx,selectors.data(),1,selected.data(),scan_views,refs.data(),scan_refs,args.data(),records.data(),uint32_t(args.size()),scan_counts.data(),scan_views*scan_batches,&errors),ctx);
    require(!errors,"large part/batch topology emits within capacity");
    for(uint32_t v=0;v<scan_views;v++){uint32_t total=0;for(uint32_t b=0;b<scan_batches;b++)total+=scan_counts[b*scan_views+v];require(total==((v==0||v==scan_views-1)?scan_parts:0),"large batch counts preserve active and disabled views");}
    std::set<uint32_t> offsets;for(uint32_t v:{0u,scan_views-1}){uint32_t first=0;for(uint32_t b=0;b<scan_batches;b++){uint32_t parts=scan_parts/scan_batches+(b<scan_parts%scan_batches);require(scan_counts[b*scan_views+v]==parts,"each large topology batch has exact draw count");for(uint32_t i=0;i<parts;i++){auto& arg=args[v*scan_draws+first+i];require(arg.instance_count==1&&arg.first_instance<scan_refs,"hierarchical scan bounded compact reference");require(offsets.insert(arg.first_instance).second&&refs[arg.first_instance].view==v,"hierarchical scan has no overlap across view/part/superblock boundaries");}first+=parts*2;}}
    // Dedicated solid depth segments merge complete appearances and split only
    // the owners whose hidden material boundaries require smaller coverage.
    SevkOpaqueActor coverage_actors[2]{actors[0],actors[1]};for(auto& a:coverage_actors){a.family=0;a.flags=1|16;a.source_first=0;a.source_count=1;}
    SevkOpaqueFamily coverage_family{1,0,3,0,{}};SevkOpaquePart coverage_parts[3]{};
    for(uint32_t p=0;p<3;p++){auto& part=coverage_parts[p];part.family=0;part.lod=0;part.first_index=p==2?3:0;part.index_count=p?3:6;part.material=p==2?11:10;part.flags=1|(p?8:4);part.generation=1;part.coverage_count=2;part.coverage_materials[0]=10;part.coverage_materials[1]=11;}
    auto coverage_config=config;coverage_config.actors=coverage_actors;coverage_config.actor_count=2;coverage_config.families=&coverage_family;coverage_config.family_count=1;coverage_config.parts=coverage_parts;coverage_config.part_count=3;coverage_config.view_count=2;coverage_config.reference_capacity=24;coverage_config.draw_capacity=6;
    ok(sevk_opaque_reference_init(ctx,&coverage_config),ctx);SevkOpaqueAppearance hidden{11,0,{0x3c003c00,0x3c003c00},{1,1,1,1}};SevkOpaqueAppearanceUpdate appearance_update{1,0,1,0};ok(sevk_opaque_reference_appearances(ctx,&appearance_update,1,&hidden,1),ctx);
    auto coverage_frame=scan_frame;coverage_frame.epoch=3100;SevkOpaqueView coverage_views[2]{views[0],views[1]};selectors.resize(2);selected.resize(4);refs.resize(24);args.resize(12);records.resize(12);uint32_t coverage_counts[2]{};
    auto capture_coverage=[&]{ok(sevk_opaque_reference_frame(ctx,&coverage_frame,coverage_views,2),ctx);ok(sevk_begin_frame(ctx,&output),ctx);ok(sevk_end_frame(ctx),ctx);ok(sevk_opaque_reference_readback(ctx,selectors.data(),2,selected.data(),4,refs.data(),24,args.data(),records.data(),12,coverage_counts,2,&errors),ctx);};
    for(uint32_t slot=0;slot<2;slot++){coverage_frame.epoch++;capture_coverage();require(!errors&&coverage_counts[0]==0&&coverage_counts[1]==2,"material visibility splits dedicated solid depth segment");
        std::set<std::pair<uint32_t,uint32_t>> triangles;for(uint32_t n=0;n<coverage_counts[1];n++){auto& arg=args[6+n];auto& record=records[6+n];for(uint32_t r=0;r<arg.instance_count;r++)for(uint32_t t=arg.first_index;t<arg.first_index+arg.index_count;t+=3)triangles.insert({refs[arg.first_instance+r].actor,t});}
        require(triangles==std::set<std::pair<uint32_t,uint32_t>>{{0,0},{0,3},{1,0}},"dedicated depth split preserves exact owner/triangle coverage");}
    hidden.visible=1;ok(sevk_opaque_reference_appearances(ctx,&appearance_update,1,&hidden,1),ctx);coverage_actors[1].hidden_views[0]=1;ok(sevk_opaque_reference_actor(ctx,1,&coverage_actors[1]),ctx);coverage_frame.epoch++;capture_coverage();
    require(!errors&&coverage_counts[1]==1&&args[6].index_count==6&&args[6].instance_count==2,"appearance restoration reunites solid segment across owners");
    require(selected[2].current==-1&&selected[3].current==0,"main occlusion leaves caster membership independent");
    coverage_actors[0].hidden_views[0]=3;ok(sevk_opaque_reference_actor(ctx,0,&coverage_actors[0]),ctx);coverage_frame.epoch++;capture_coverage();require(selectors[0].initialized&&selectors[0].epoch==coverage_frame.epoch&&coverage_counts[1]==1&&args[6].instance_count==1,"all-view occlusion retains once-per-frame selector advancement");
    SevkStats stats{sizeof(stats)};ok(sevk_get_stats(ctx,&stats),ctx);require(stats.validation_enabled&&stats.validation_errors==0,"GPU opaque reference has zero core validation errors");
    sevk_destroy(ctx);ctx=nullptr;std::cout<<"GPU opaque resident selection -> grouped references -> actual indirect counts -> raster: "<<checks<<" checks passed; zero validation errors\n";return 0;
}catch(const std::exception& error){std::cerr<<"Opaque reference failed after "<<checks<<" checks: "<<error.what()<<"\n";if(ctx)sevk_destroy(ctx);return 1;}}

