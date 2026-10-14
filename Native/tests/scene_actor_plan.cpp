#include "../src/scene_actor_registry.h"
#include <iostream>
#include <stdexcept>

void require(bool condition,const char* message){if(!condition)throw std::runtime_error(message);}
int main(){
    using namespace scene_actor;
    const float thresholds[]{10,20};GlobalLod settings{};settings.hysteresis=.1f;
    LodState lod{};lod.update(15,thresholds,settings,.1f,true);
    require(lod.current==1 && lod.previous==-1 && lod.transition==0,"first-visible translation LOD");
    lod.update(21,thresholds,settings,.1f,false);require(lod.current==1 && lod.previous==-1,"hysteresis high boundary");
    lod.update(23,thresholds,settings,.1f,false);require(lod.current==2 && lod.previous==1 && lod.transition>0,"live current/previous transition");
    lod.update(23,thresholds,settings,1,false);require(lod.current==2 && lod.previous==-1 && lod.transition==0,"transition completion");
    settings.updates=false;lod.update(0,thresholds,settings,1,true);require(lod.current==2,"disabled update preserves initialized selector");
    settings.explicit_lod=true;settings.selection=99;lod.update(0,thresholds,settings,1,false);require(lod.current==2 && lod.previous==-1,"explicit override clamps authored range");
    settings={};lod={};lod.update(10,thresholds,settings,0,true);require(lod.current==1,"exact initial threshold includes next level");
    lod.update(10,thresholds,settings,0,false);require(lod.current==1 && lod.previous==-1,"exact hysteresis boundary does not move");
    Frustum cube{};cube.enabled=true;cube.planes={Plane{1,0,0,-1},Plane{-1,0,0,-1},Plane{0,1,0,-1},Plane{0,-1,0,-1},Plane{0,0,1,-1},Plane{0,0,-1,-1}};
    require(!disjoint({{-.5,-.5,-.5},{.5,.5,.5}},cube),"inside box retained");
    require(!disjoint({{1,-.5,-.5},{2,.5,.5}},cube),"touching box retained");
    require(disjoint({{1.00001,-.5,-.5},{2,.5,.5}},cube),"outside positive plane rejected");
    require(disjoint({{-2,-.5,-.5},{-1.00001,.5,.5}},cube),"outside negative plane rejected");
    require(!disjoint({{-100,-100,-100},{100,100,100}},cube),"enclosing box retained");
    Actor actor{};actor.translation={0,0,-5};actor.bounds={{-.5,-.5,-5.5},{.5,.5,-4.5}};
    Frame frame{};frame.probe_distance=100;frame.global.explicit_lod=true;frame.global.selection=0;frame.cascade_count=2;frame.forward_lod.minimum=2;frame.cascade_lod[0].minimum=1;frame.cascade_lod[1].minimum=1;
    auto plan_state=LodState{};auto output=plan(actor,frame,plan_state,thresholds,true,false);
    require(output.selected && output.main && output.probe && output.count==3,"main/depth/forward representation split");
    require(output.slices[0].main && output.slices[0].lod==0 && output.slices[0].cascades==0,"main representation owns only current main pass");
    require(output.slices[1].probe && output.slices[1].lod==2 && output.slices[1].cascades==0,"forward representation owns authored pass LOD");
    require(output.slices[2].cascades==3 && output.slices[2].lod==1 && output.cascade_memberships==2,"equal depth levels merge current cascade masks");
    output=plan(actor,frame,plan_state,thresholds,false,true);require(!output.main && output.probe && output.count==2,"occlusion removes only main consumer");
    actor.skip_forward=true;output=plan(actor,frame,plan_state,thresholds,false,false);require(!output.probe && output.count==2,"skip-forward preserves main and depth");actor.skip_forward=false;
    actor.visible=false;output=plan(actor,frame,plan_state,thresholds,false,false);require(!output.selected && output.count==0,"hierarchy-hidden actor has no views");actor.visible=true;
    actor.skip_main=true;actor.casts=false;frame.probe_distance=0;output=plan(actor,frame,plan_state,thresholds,false,false);
    require(output.selected && output.count==0,"CastShadows outer gate differs from SkipInDepth consumer gate");
    actor.cast_flag=false;output=plan(actor,frame,plan_state,thresholds,false,false);require(!output.selected,"no-view actor outside probe with no cast flag rejected");
    actor={};actor.translation={0,0,-5};actor.bounds={{-.5,-.5,-5.5},{.5,.5,-4.5}};frame.probe_distance=100;
    Spot spot{};spot.direction[2]=-1;spot.range=20;spot.cosine=.5f;const Spot spots[]{spot};frame.spots=spots;frame.spot_lod.minimum=2;
    output=plan(actor,frame,plan_state,thresholds,false,false);require(output.spot && output.spot_lod==2 && output.count==3 && output.slices[1].probe && output.slices[1].spots,"shared forward/spot level merges independent consumers");
    frame.global.explicit_lod=false;frame.global.updates=false;plan_state.initialized=true;plan_state.current=1;plan_state.previous=0;plan_state.transition=.25f;
    output=plan(actor,frame,plan_state,thresholds,false,false);require(output.slices[0].alpha==2.75f && output.slices[1].alpha==.25f && plan_state.transition==.25f,"current transition state survives frozen selector and pass preparation");
    Registry registry;SevkSceneActor record{};record.scale=1;record.flags=1|8|16;record.translation[2]=-5;record.bounds[0]=record.bounds[1]=-.5;record.bounds[2]=-5.5;record.bounds[3]=record.bounds[4]=.5;record.bounds[5]=-4.5;record.lod.previous=-1;
    auto handle=registry.add(record,thresholds,2);SevkSceneFrame snapshot{};snapshot.probe_distance=100;snapshot.shadow_distance=1500;snapshot.coefficient=1;snapshot.delta=.1f;snapshot.global_flags=2;snapshot.global[3]=1;snapshot.global[4]=.25f;snapshot.global[6]=1;snapshot.smooth_distance=100;snapshot.cascade_count=2;
    uint8_t gate{},hidden{};SevkSceneResult planned{};
    registry.cull_frame(snapshot,nullptr,0,&handle,1,&gate);require(gate==3,"resident cull returns current main candidate");
    bool blocked=false;try{registry.remove(handle);}catch(const std::runtime_error&){blocked=true;}require(blocked,"resident mutation prohibited between cull and plan");
    registry.finish(&hidden,&planned,1);require(planned.flags==15 && planned.count==1 && planned.slices[0].cascades==3,"resident frame merges current views");
    blocked=false;try{registry.finish(&hidden,&planned,1);}catch(const std::runtime_error&){blocked=true;}require(blocked,"selector cannot advance twice for one frame");
    record.handle=handle;record.flags=0;record.lod=planned.lod;registry.update(&record,1);registry.cull_frame(snapshot,nullptr,0,&handle,1,&gate);registry.finish(&hidden,&planned,1);require(gate==0 && planned.flags==0,"resident visibility mutation removes all current consumers");
    registry.remove(handle);auto replacement=registry.add(record,thresholds,2);require(replacement!=handle,"recreated actor has a fresh generation");
    blocked=false;try{registry.remove(handle);}catch(const std::runtime_error&){blocked=true;}require(blocked,"stale actor handle rejected after slot reuse");registry.remove(replacement);
    std::cout<<"Actor plan: source LOD boundaries/transitions, double plane tests, independent view ownership passed\n";
}
