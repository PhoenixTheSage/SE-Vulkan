#include "../src/resident_proxy_scene.h"
#include <iostream>
void require(bool ok,const char* text){if(!ok)throw std::runtime_error(text);}
template<class F>void rejects(F&& fn,const char* text){bool failed=false;try{fn();}catch(const std::runtime_error&){failed=true;}require(failed,text);}
int main(){native_pass::ViewJobs jobs;
    {
        resident_proxy::Scene scene;auto family=scene.register_family({10});auto near=scene.register_model();scene.bind_level(family,0,near);
        resident_proxy::Actor actor;actor.family=family;actor.owner=1;actor.group=7;actor.stream=42;actor.first=4;actor.count=8;actor.selection.bounds={{-1,-1,-6},{1,1,-4}};actor.local_bounds={{-1,-1,-1},{1,1,1}};actor.parented=true;actor.inverse[11]=5;
        auto a=scene.register_actor(actor);actor.owner=2;actor.group=8;actor.first=12;actor.count=5;actor.inverse[3]=-200;auto b=scene.register_actor(actor);
        native_pass::Frame frame;frame.views={{0,native_pass::Kind::Colour},{1,native_pass::Kind::Depth},{9,native_pass::Kind::Forward}};frame.selection.coefficient=1;
        scene.prepare(frame,jobs);require(scene.missing_levels().size()==1&&scene.missing_levels()[0].lod==1,"OLD parent-local selector returns one lazy model request");
        auto far=scene.register_model();scene.bind_level(family,1,far);scene.publish({},jobs);
        for(auto& view:scene.view_references()){require(view.proxies.size()==2,"OLD selected proxy identities stay view-specific");auto& x=view.proxies[0];auto& y=view.proxies[1];require(x.stream==42&&y.stream==42&&x.first==4&&x.count==8&&y.first==12&&y.count==5,"OLD views preserve source buffer subranges without instance scatter");require(x.model==near&&y.model==far,"OLD uses current stock selector rather than NEW per-view shifts");}
        scene.prepare(frame,jobs);native_pass::QueryResult hidden[]{ {7,1} };scene.publish(hidden,jobs);require(scene.view_references()[0].proxies.size()==1&&scene.view_references()[1].proxies.size()==2,"OLD main occlusion cannot suppress shadow ownership");
        actor=scene.actor_state(a);actor.inverse[3]=-20;scene.update_actor(a,actor);scene.prepare(frame,jobs);scene.publish({},jobs);require(scene.view_references()[0].proxies.size()==3&&scene.view_references()[0].proxies[0].alpha>0&&scene.view_references()[0].proxies[1].alpha>2,"OLD transitions retain current/next proxy float alpha contract");
        auto before=scene.actor_state(a).lod;scene.prepare(frame,jobs);scene.cancel();require(scene.actor_state(a).lod.transition==before.transition,"cancelled OLD frame does not advance selector");
        scene.remove_model(near);scene.prepare(frame,jobs);scene.publish({},jobs);require(!scene.explicit_fallbacks().empty(),"retired OLD geometry emits explicit fallback before reference publication");
        scene.remove_actor(b);rejects([&]{scene.actor_state(b);},"OLD actor handles reject retired generations");
    }
    {
        resident_proxy::Scene scene;auto family=scene.register_family({10});auto near=scene.register_model(),far=scene.register_model();scene.bind_level(family,0,near);scene.bind_level(family,1,far);
        resident_proxy::Actor actor;actor.family=family;actor.owner=4;actor.selection.visible=actor.selection.cast_flag=actor.selection.casts=true;actor.selection.bounds={{-1,-1,-6},{1,1,-4}};auto handle=scene.register_actor(actor);
        native_pass::Frame frame;frame.selection.coefficient=1;frame.selection.global.maximum_seconds=1;frame.selection.delta=.1f;frame.views={{0,native_pass::Kind::Colour},{15,native_pass::Kind::Depth},{16,native_pass::Kind::Depth},{5,native_pass::Kind::Depth}};frame.views[1].only_static=true;frame.views[2].only_dynamic=true;
        scene.prepare(frame,jobs);scene.publish({},jobs);require(scene.view_references()[1].proxies.size()==1&&scene.view_references()[2].proxies.empty()&&(scene.static_updates()&(1ull<<15)),"static OLD proxy initializes retained spotlight depth");
        scene.prepare(frame,jobs);scene.publish({},jobs);require(scene.static_updates()==0,"steady OLD proxy does not invalidate its static spotlight image");
        actor=scene.actor_state(handle);actor.selection.bounds={{-1,-1,-26},{1,1,-24}};actor.limit_far=true;scene.update_actor(handle,actor);scene.prepare(frame,jobs);// Publication callback validates bindings before selector commit.
        rejects([&]{scene.publish({},jobs,[](const auto&){throw std::runtime_error("injected GPU binding validation");});},"failed resource validation rejects the whole proxy publication");scene.cancel();require(scene.actor_state(handle).lod.transition==0,"failed publication leaves OLD selector unchanged");
        scene.prepare(frame,jobs);scene.publish({},jobs);require(scene.view_references()[1].proxies.empty()&&scene.view_references()[2].proxies.size()==2&&(scene.static_updates()&(1ull<<15)),"OLD transition removes static ownership and retains both dynamic depth proxies");require(scene.view_references()[3].proxies.empty(),"stock OLD far-cascade exclusion is independent of spot ownership");
        std::array<float,16> projection{};projection[0]=projection[5]=1;projection[11]=-1;actor=scene.actor_state(handle);actor.size_policy=true;actor.selection.bounds={{-1,-1,-10001},{1,1,-9999}};scene.update_actor(handle,actor);scene.set_size_policy(projection,1920,1080,true);scene.prepare(frame,jobs);scene.publish({},jobs);for(auto& view:scene.view_references())require(view.proxies.empty(),"stock projected-size pruning excludes the OLD actor from every normal view");
    }
std::cout<<"Resident OLD proxy reference checks passed\n";
}
