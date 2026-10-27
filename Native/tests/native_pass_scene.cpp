#include "../src/native_pass_scene.h"
#include <iostream>
#include <cstring>
void require(bool ok,const char* text){if(!ok)throw std::runtime_error(text);}
template<class F>void rejects(F&& fn,const char* text){bool failed=false;try{fn();}catch(const std::runtime_error&){failed=true;}require(failed,text);}
static void run(){
    {
    using namespace native_pass;Scene scene;
    auto family=scene.register_family({10});
    Model model{{Part{1,10,11,12,0,0,3,1,0,false,false,true},Part{1,20,21,22,0,3,3,2,0,false,false,true},Part{1,30,31,32,33,6,3,3,1,true,false,true}}};
    auto level=scene.register_model(model);scene.bind_level(family,0,level);
    Actor a{};a.owner=1;a.family=family;a.group=7;a.selection.translation={0,0,-5};a.selection.bounds={{-1,-1,-6},{1,1,-4}};
    auto first=scene.register_actor(a);a.owner=2;a.group=8;a.key_colour[0]=0x3800;a.selection.translation.x=200;a.selection.bounds.minimum.x=199;a.selection.bounds.maximum.x=201;auto second=scene.register_actor(a);
    Frame frame{};frame.selection.coefficient=1;frame.selection.delta=.1f;frame.selection.global.explicit_lod=true;frame.selection.global.selection=0;
    frame.views={{0,Kind::Colour},{1,Kind::Depth},{2,Kind::Depth},{9,Kind::Forward}};
    scene.prepare(frame);require(scene.query_owners().size()==2&&scene.missing_levels().empty(),"unique query owners; no actor slices leave native preparation");
    scene.publish({});require(scene.render_groups().size()==10,"colour/forward parts and merged solid plus alpha ranges are view-specific");
    require(scene.instance_streams().size()==4,"one base instance stream per model/view is shared across every compatible part");
    size_t shared_bytes=0;for(auto& stream:scene.instance_streams())shared_bytes+=stream->instances.size();require(shared_bytes==2*(64+48+48+64),"geometry ranges do not duplicate transform and appearance upload rows");
    auto changed_appearance=scene.actor_state(first);changed_appearance.appearance={{2,{0x3800,0x3400,0x3c00,0x4000},true}};scene.update_actor(first,changed_appearance);scene.prepare(frame);scene.publish({});
    require(scene.instance_streams().size()==6,"only activated material appearance adds a colour and forward stream; depth stays shared");
    const InstanceStream* base_stream=nullptr;const InstanceStream* material_stream=nullptr;
    for(auto& group:scene.render_groups())if(group.key.view==0){if(group.key.first==0)base_stream=group.stream.get();if(group.key.first==3)material_stream=group.stream.get();}
    require(base_stream&&material_stream&&base_stream!=material_stream,"material override parts bind their own stream while default parts share the base");
    ColourInstance material_row{};std::memcpy(&material_row,material_stream->instances.data(),64);require(material_row.colour[0]==0x3800&&material_row.colour[3]==0x4000,"activated appearance stream preserves tint and emissivity for the selected owner");
    auto pose_only=scene.actor_state(first);pose_only.appearance.clear();pose_only.replace_appearance=false;pose_only.selection.translation.x=1;scene.update_actor(first,pose_only);
    require(scene.actor_state(first).appearance.size()==1&&scene.actor_state(first).appearance[0].colour[3]==0x4000,"pose update keeps independently resident material tint and emissivity");
    rejects([&]{scene.register_actor(pose_only);},"registration cannot preserve nonexistent resident appearance");
    auto invalid_pose=pose_only;invalid_pose.appearance={{2,{0,0,0,0},true}};
    auto untouched=scene.actor_state(first).selection.translation.x;auto changed_pose=pose_only;changed_pose.selection.translation.x=99;
    rejects([&]{scene.update_actors({{first,changed_pose},{second,invalid_pose}});},"invalid appearance update rejects the whole resident batch before mutation");
    require(scene.actor_state(first).selection.translation.x==untouched,"batch rejection preserves the previously committed transform");
    for(auto& group:scene.render_groups())require(group.count()==2,"spatially distant and differently coloured instances share compatible view groups");
    for(auto& group:scene.render_groups())if(group.kind==Kind::Depth&&group.key.first==0)require(group.key.count==6&&group.stride()==48,"dedicated solids use transform-only depth stream");
    for(auto& group:scene.render_groups())if(group.kind==Kind::Colour)require(group.stride()==64,"colour stream uses stock packed key/tint/emissivity");
    scene.prepare(frame);const QueryResult hidden[]{QueryResult{7,uint64_t(1)<<1},QueryResult{8,uint64_t(1)<<2}};scene.publish(hidden);
    for(auto& group:scene.render_groups())if(group.key.view==1)require(group.stream->owners.size()==1&&group.stream->owners[0]==2,"cascade one excludes only its own hidden owner");else if(group.key.view==2)require(group.stream->owners.size()==1&&group.stream->owners[0]==1,"cascade two has independent selected owners");else require(group.count()==2,"shadow occlusion does not remove main/forward owners");
    scene.prepare(frame);const QueryResult main_hidden[]{QueryResult{7,1}};scene.publish(main_hidden);require(scene.main_visible_owners().size()==2,"gameplay receives main frustum visibility identities before occlusion rejection");
    Actor changed{};changed.owner=1;changed.family=family;changed.group=7;changed.selection.translation={0,0,-5};changed.selection.bounds={{-1,-1,-6},{1,1,-4}};changed.appearance={{2,{0x3800,0x3c00,0x3c00,0},false}};scene.update_actor(first,changed);
    scene.prepare(frame);scene.publish({});for(auto& group:scene.render_groups())if(group.kind==Kind::Depth&&group.stream->owners.size()==1&&group.stream->owners[0]==1)require(group.key.first!=3,"material visibility cannot revive the middle solid range");
    frame.selection.global.explicit_lod=false;changed.selection.translation={0,0,-20};changed.selection.bounds={{-1,-1,-21},{1,1,-19}};scene.update_actor(first,changed);scene.prepare(frame);
    require(scene.missing_levels().size()==1&&scene.missing_levels()[0].lod==1,"lazy LOD emits one family-level resource request");
    rejects([&]{scene.update_actor(first,changed);},"committed actor mutation cannot race a pending selector");
    auto far=scene.register_model(model);scene.bind_level(family,1,far);scene.publish({});
    scene.prepare(frame);scene.publish({});bool previous=false,current=false;for(auto& group:scene.render_groups())if(group.kind==Kind::Colour)for(size_t i=0;i<group.stream->owners.size();++i)if(group.stream->owners[i]==1){ColourInstance value{};std::memcpy(&value,group.stream->instances.data()+i*64,64);previous|=value.key[3]>0x4000;current|=value.key[3]>0&&value.key[3]<0x3c00;}
    require(previous&&current,"lazy resolution preserves current/previous stock transition instances");
    frame.views[1].enabled=false;scene.prepare(frame);scene.publish({});for(auto& group:scene.render_groups())require(group.key.view!=1,"unscheduled cascade produces no groups or streams");
    Scene depth_scene;auto df=depth_scene.register_family({});
    Model depth_model{{Part{1,10,11,12,0,0,3,1,0,false,false,true},Part{1,10,11,12,0,3,3,2,0,false,false,true},Part{1,10,11,12,0,6,3,3,0,false,false,true},Part{1,10,11,12,0,9,3,4,0,false,false,false},Part{1,10,11,12,0,12,3,5,0,false,false,true},Part{1,10,11,12,33,15,3,6,1,true,true,true}}};
    auto dm=depth_scene.register_model(depth_model);depth_scene.bind_level(df,0,dm);Actor da{};da.family=df;da.owner=5;da.selection.translation={0,0,-1};da.selection.bounds={{-1,-1,-2},{1,1,0}};auto dh=depth_scene.register_actor(da);Frame dframe{};dframe.views={{1,Kind::Depth}};
    depth_scene.prepare(dframe);depth_scene.publish({});
    require(depth_scene.render_groups().size()==3&&depth_scene.render_groups()[0].key.count==9,"registered dedicated depth merges solids and preserves skipped/alpha boundaries");
    da.appearance={{2,{0x3c00,0x3c00,0x3c00,0x3c00},false}};depth_scene.update_actor(dh,da);depth_scene.prepare(dframe);depth_scene.publish({});
    auto& dg=depth_scene.render_groups();require(dg.size()==4&&dg[0].key.first==0&&dg[0].key.count==3&&dg[1].key.first==6&&dg[1].key.count==3,"hidden middle material splits preprocessed solid segment without including hidden triangles");
    require(dg.back().key.masked&&dg.back().key.alpha==33,"preprocessed alpha segment retains its binding after solid visibility changes");
    da.appearance.clear();depth_scene.update_actor(dh,da);depth_scene.prepare(dframe);depth_scene.publish({});require(depth_scene.render_groups().size()==3&&depth_scene.render_groups()[0].key.count==9,"restored appearance reuses original immutable depth coverage");
    scene.prepare(frame);scene.cancel();scene.prepare(frame);scene.publish({});
    scene.remove_actor(second);rejects([&]{scene.update_actor(second,changed);},"generation-checked actor handle rejects removed owner");
    scene.remove_model(far);scene.prepare(frame);scene.publish({});require(!scene.explicit_fallbacks().empty(),"retired selected geometry returns explicit fallback rather than dereferencing a resource");
    Scene spot_scene;auto sf=spot_scene.register_family({});auto sm=spot_scene.register_model(model);spot_scene.bind_level(sf,0,sm);
    Actor spot_actor{};spot_actor.owner=77;spot_actor.family=sf;spot_actor.selection.bounds={{-1,-1,-2},{1,1,0}};auto sa=spot_scene.register_actor(spot_actor);Frame spot_frame{};View static_spot{15,Kind::Depth};static_spot.only_static=true;View dynamic_spot{16,Kind::Depth};dynamic_spot.only_dynamic=true;spot_frame.views={static_spot,dynamic_spot};
    spot_scene.prepare(spot_frame);spot_scene.publish({});require(spot_scene.static_spot_updates()==(uint64_t(1)<<15),"first native static spotlight membership invalidates retained depth");
    spot_frame.selection.camera.x=4;spot_scene.clear_frame();spot_scene.prepare(spot_frame);spot_scene.publish({});require(spot_scene.static_spot_updates()==0,"camera-relative stream translation does not invalidate static spotlight content");
    spot_actor.dynamic=true;spot_scene.update_actor(sa,spot_actor);spot_scene.prepare(spot_frame);spot_scene.publish({});require(spot_scene.static_spot_updates()==(uint64_t(1)<<15),"moving native caster removes its old static membership");
    for(auto& group:spot_scene.render_groups())require(group.key.view==16,"moving caster is published only into the dynamic spotlight view");
    spot_actor.dynamic=false;spot_scene.update_actor(sa,spot_actor);spot_scene.prepare(spot_frame);spot_scene.publish({});require(spot_scene.static_spot_updates()==(uint64_t(1)<<15),"settled native caster rebuilds its static retained depth membership");
    auto before=spot_scene.actor_state(sa).lod;spot_scene.prepare(spot_frame);spot_scene.cancel();auto after=spot_scene.actor_state(sa).lod;require(before.current==after.current&&before.transition==after.transition,"cancelled prepared selection never advances committed selector");
    spot_scene.invalidate_dependency(0,10);spot_scene.prepare(spot_frame);require(spot_scene.missing_levels().size()==1,"only an invalidated selected immutable binding requests re-registration");spot_scene.publish({});require(spot_scene.explicit_fallbacks().size()==1&&spot_scene.render_groups().empty(),"stale binding uses explicit fallback and never reaches command recording");
    Scene mixed;auto mf=mixed.register_family({10});Model mm=model;mm.glass=true;
    auto ml=mixed.register_model(mm);mixed.bind_level(mf,0,ml);
    Actor ma{};ma.owner=90;ma.family=mf;ma.group=4;ma.selection.translation={0,0,-5};ma.selection.bounds={{-1,-1,-6},{1,1,-4}};
    auto mh=mixed.register_actor(ma);Frame mframe{};mframe.selection.global.explicit_lod=true;mframe.selection.global.selection=0;mframe.views={{0,Kind::Colour},{1,Kind::Depth},{9,Kind::Forward}};
    mixed.prepare(mframe);require(mixed.transparent_requests().empty(),"preparation returns no transparent replay before visibility publication");mixed.publish({});
    auto tickets=mixed.transparent_requests();require(tickets.size()==1&&tickets[0].actor==mh&&tickets[0].owner==90&&tickets[0].lod==0&&tickets[0].alpha==0,"only visible main mixed LOD returns a generation-checked glass ticket");
    require(!mixed.render_groups().empty(),"mixed opaque ranges remain direct native groups");
    mixed.prepare(mframe);const QueryResult glass_hidden[]{QueryResult{4,1}};mixed.publish(glass_hidden);require(mixed.transparent_requests().empty()&&!mixed.render_groups().empty(),"main occlusion suppresses glass without suppressing independent depth and probe groups");
    ma.selection.translation.z=-20;mixed.update_actor(mh,ma);mframe.selection.global.explicit_lod=false;mframe.selection.delta=.1f;mframe.selection.global.minimum_seconds=.25f;mframe.selection.global.smooth_camera_distance=100;
    mixed.prepare(mframe);require(mixed.missing_levels().size()==1,"mixed lazy LOD requests one resource without returning actor view slices");
    Model glass_only{};glass_only.glass=true;auto gm=mixed.register_model(glass_only);mixed.bind_level(mf,1,gm);mixed.publish({});tickets=mixed.transparent_requests();
    require(tickets.size()==2&&tickets[0].lod==1&&tickets[1].lod==0&&tickets[0].alpha>2&&tickets[1].alpha>0&&tickets[1].alpha<1,"glass current and previous use existing glass shader transition encoding after lazy resolution");
    auto committed=mixed.actor_state(mh).lod;mixed.prepare(mframe);mixed.cancel();require(mixed.transparent_requests().empty()&&mixed.actor_state(mh).lod.transition==committed.transition,"cancel removes glass tickets without advancing the resident selector");
    mixed.remove_model(gm);mixed.prepare(mframe);mixed.publish({});require(mixed.explicit_fallbacks().size()==1&&mixed.transparent_requests().empty()&&mixed.render_groups().empty(),"missing mixed resource emits only explicit fallback, never partial opaque or glass coverage");
    mixed.clear_frame();require(mixed.transparent_requests().empty(),"frame clear retires transparent tickets");
    Scene retained;auto retained_family=retained.register_family({});
    Model retained_model{{Part{1,10,11,12,0,0,3,1,0,false,false,true}}};
    auto retained_level=retained.register_model(retained_model);retained.bind_level(retained_family,0,retained_level);
    Actor retained_actor{};retained_actor.owner=3;retained_actor.family=retained_family;retained_actor.selection.translation={0,0,-5};retained_actor.selection.bounds={{-1,-1,-6},{1,1,-4}};
    auto retained_handle=retained.register_actor(retained_actor);
    Frame retained_frame{};retained_frame.views={{0,Kind::Colour}};retained.profiling=true;
    retained.prepare(retained_frame);retained.publish({});
    require(retained.profile.emitted_rows==1&&retained.instance_streams().size()==1,"first publication fills one retained grouped stream");
    auto emitted=retained.profile.emitted_rows;auto bytes=retained.profile.stream_bytes;
    ColourInstance original{};std::memcpy(&original,retained.instance_streams()[0]->instances.data(),64);
    retained_frame.selection.camera.x=4;retained.clear_frame();
    require(retained.render_groups().empty(),"frame clear retires published groups without discarding retained streams");
    retained.prepare(retained_frame);retained.publish({});
    require(retained.profile.emitted_rows==emitted&&retained.profile.stream_bytes==bytes,"camera movement reuses the retained grouped stream");
    ColourInstance shifted{};std::memcpy(&shifted,retained.instance_streams()[0]->instances.data(),64);
    require(shifted.transform.rows[3]==original.transform.rows[3]-4&&retained.render_groups().size()==1,"record consumes the retained group with current camera-relative translation");
    auto posed=retained.actor_state(retained_handle);posed.appearance.clear();posed.replace_appearance=false;posed.selection.translation.x=2;
    retained.update_actor(retained_handle,posed);retained.prepare(retained_frame);retained.publish({});
    require(retained.profile.emitted_rows==emitted+1,"one moved actor rewrites its retained row");
    ColourInstance moved_row{};std::memcpy(&moved_row,retained.instance_streams()[0]->instances.data(),64);
    require(moved_row.transform.rows[3]==-2,"rewritten row uses the committed translation and current camera");
    Actor joined=retained_actor;joined.owner=4;joined.selection.translation.x=8;joined.selection.bounds.minimum.x=7;joined.selection.bounds.maximum.x=9;
    retained.register_actor(joined);auto before_join=retained.profile.emitted_rows;
    retained.prepare(retained_frame);retained.publish({});
    require(retained.profile.emitted_rows==before_join+2&&retained.render_groups()[0].count()==2,"membership change rebuilds the grouped stream once");
    auto stable=retained.profile.emitted_rows;retained.prepare(retained_frame);retained.publish({});
    require(retained.profile.emitted_rows==stable,"stable membership does not fill the grouped stream again");
    Scene recycled;auto recycled_family=recycled.register_family({});auto recycled_model=recycled.register_model(retained_model);recycled.bind_level(recycled_family,0,recycled_model);
    Actor recycled_actor{};recycled_actor.owner=77;recycled_actor.family=recycled_family;recycled_actor.selection.translation={0,0,-5};recycled_actor.selection.bounds={{-1,-1,-6},{1,1,-4}};
    auto retired_actor=recycled.register_actor(recycled_actor);Frame recycled_frame{};View recycled_spot;recycled_spot.id=15;recycled_spot.kind=Kind::Depth;recycled_spot.only_static=true;recycled_frame.views={{0,Kind::Colour},recycled_spot};
    recycled.prepare(recycled_frame);recycled.publish({});require(recycled.static_spot_updates()==(uint64_t(1)<<15),"recycled-owner control establishes static spotlight content");
    recycled.clear_frame();recycled.remove_actor(retired_actor);recycled_actor.selection.translation.x=2;recycled_actor.selection.bounds.minimum.x=1;recycled_actor.selection.bounds.maximum.x=3;recycled_actor.key_colour[0]=0x3800;
    auto replacement_actor=recycled.register_actor(recycled_actor);require(replacement_actor!=retired_actor,"replacement actor has a distinct generation-checked handle");
    recycled.prepare(recycled_frame);recycled.publish({});ColourInstance replacement_row{};std::memcpy(&replacement_row,recycled.instance_streams()[0]->instances.data(),64);
    require(replacement_row.transform.rows[3]==2&&replacement_row.key[0]==0x3800,"recreated owner replaces retained pose and appearance rather than reusing retired bytes");
    require(recycled.static_spot_updates()==(uint64_t(1)<<15),"recreated owner invalidates the prior static spotlight pose");
    }
}
int main(){
    // Every check runs with LOD readiness dedupe and the view cache each off and on; the outputs must not differ.
    try{for(int mode=0;mode<4;++mode){native_pass::Scene::default_lod_dedupe=(mode&1)!=0;native_pass::Scene::default_view_cache=(mode&2)!=0;run();}
        std::cout<<"Native resident per-view producer checks passed\n";}
    catch(const std::exception& error){std::cerr<<"FAIL "<<error.what()<<'\n';return 1;}
}
