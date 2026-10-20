#include "../src/gpu_opaque_registry.h"
#include <iostream>
using namespace gpu_scene;
int main(){try{
    uint32_t checks=0;auto require=[&](bool ok,const char* label){if(!ok)throw std::runtime_error(label);++checks;};
    auto rejects=[&](auto fn,const char* label){bool rejected=false;try{fn();}catch(const std::runtime_error&){rejected=true;}require(rejected,label);};
    Registry registry;
    {
        Registry ordered;
        Model model;model.old=true;Part p{{100,1,10,20,0,3,0},{1,0,32,64,0,{1,2,3,4}}};
        p.old_order={1653665488374690ull,23,34,12,0,1,0};model.parts={p,p,p};
        model.parts[1].old_order.resource+=128;model.parts[1].old_order.material+=128;model.parts[1].old_order.layout+=64;
        model.parts[2].old_order.proxy=1;
        for(auto& part:model.parts)validate_old_order(part.old_order);
        auto handle=ordered.register_model(model);uint64_t levels[]{handle};Actor entry;entry.family=ordered.register_family({},levels);entry.input.owner=1;entry.input.generation=1;entry.input.flags=1|2;entry.input.scale=1;
        Source stream;stream.bytes.resize(64);entry.source=ordered.register_source(std::move(stream));ordered.register_actor(entry);
        auto snapshot=ordered.snapshot();require(snapshot.batches.size()==3,"OLD truncated-key collisions or source proxies were merged on shared pages");
        for(auto& key:snapshot.batches)require(key.old_order.valid&&key.old_order.key==1653665488374690ull,"snapshot lost immutable OLD key metadata");
        auto changed=std::make_shared<Model>(model);Family a,b;a.old=b.old=true;a.levels.push_back(changed);b.levels.push_back(std::make_shared<Model>(*changed));
        auto other=std::make_shared<Model>(*changed);other->parts[0].old_order.proxy++;b.levels[0]=other;require(!compatible_bindings(a,b),"OLD ordering change admitted as texture-only binding patch");
        auto bad=model;bad.parts[0].old_order.reserved=1;rejects([&]{ordered.register_model(bad);},"reserved OLD metadata admitted");
        bad=model;bad.parts[0].old_order.key^=1;rejects([&]{ordered.register_model(bad);},"OLD packed/full material identity mismatch admitted");
        bad=model;bad.old=false;rejects([&]{ordered.register_model(bad);},"NEW model admitted OLD identity metadata");
    }
    Model near;near.parts.push_back({{100,1,10,20,0,3,0},{1,0,4,8,0,{1,2,3,4}}});
    Model far=near;far.parts[0].geometry.mesh=101;far.parts[0].geometry.first=3;far.parts[0].material.textures={5,6,7,8};
    auto nm=registry.register_model(near),fm=registry.register_model(far);uint64_t levels[]{nm,fm};
    rejects([&]{registry.register_family({10},std::span(levels,1));},"incomplete authored family admitted");
    auto family=registry.register_family({10},levels);Actor actor;actor.family=family;actor.input.owner=1000;actor.input.generation=1;actor.input.flags=1|16;actor.input.source_count=1;actor.input.scale=1;
    auto a=registry.register_actor(actor);actor.input.owner=1001;auto b=registry.register_actor(actor);auto first=registry.snapshot();
    require(first.actors.size()==2&&first.families.size()==1,"resident owners/families are complete");
    require(first.batches.size()==2,"NEW LOD part identity was lost in shared page batches");
    auto upload=registry.upload();require(upload.batches.size()==2&&upload.parts.size()==4&&upload.actors.size()==2,"upload retains ordered NEW parts and dedicated depth geometry");require(upload.handles.size()==2&&upload.objects.size()==256&&upload.actors[0].family==upload.actors[1].family,"upload preserves resident owner/object/family identities");
    {
        Registry ordered;Model group=near;auto cutout=near.parts[0];cutout.geometry.first=3;cutout.material.colour_pipeline=14;cutout.material.forward_pipeline=UINT32_MAX;cutout.depth=cutout.forward=false;group.parts.push_back(cutout);
        auto registeredFirst=ordered.register_model(near),registeredLast=ordered.register_model(group);uint64_t aLevels[]{registeredLast},bLevels[]{registeredFirst};auto aFamily=ordered.register_family({},aLevels),bFamily=ordered.register_family({},bLevels);
        Actor entry=actor;entry.family=aFamily;entry.input.owner=5000;ordered.register_actor(entry);entry.family=bFamily;entry.input.owner=5001;ordered.register_actor(entry);auto grouped=ordered.upload();
        require(grouped.batches.size()==3&&grouped.batches[0].main_pipeline==0&&grouped.batches[1].main_pipeline==14&&grouped.batches[2].main_pipeline==0,"shared pages split NEW groups by shader instead of first-seen parts");
        require(grouped.actors[0].family==0&&grouped.actors[1].family==1,"NEW family order follows allocation rather than committed encounter order");
        Model oldGroup=group;oldGroup.old=true;Registry oldRegistry;auto oldModel=oldRegistry.register_model(oldGroup);uint64_t oldLevels[]{oldModel};entry.family=oldRegistry.register_family({},oldLevels);Source oldSource;oldSource.bytes.resize(64);entry.source=oldRegistry.register_source(std::move(oldSource));entry.input.owner=6000;entry.input.flags|=2;oldRegistry.register_actor(entry);auto oldUpload=oldRegistry.upload();
        require(oldUpload.batches.size()==2&&oldUpload.source.batches[0].group==0&&oldUpload.source.batches[1].group==0,"OLD page/shader batching acquired NEW group constraints");
    }
    auto bindingFamily=*first.families[0];auto bindingModel=std::make_shared<Model>(*bindingFamily.levels[0]);bindingModel->parts[0].material.textures={50,51,52,53};bindingModel->depth[0].range.material.textures={50,51,52,53};bindingFamily.levels[0]=bindingModel;
    require(compatible_bindings(*first.families[0],bindingFamily),"texture-only binding cannot retain geometry");
    require(binding_textures(*first.families[0]).size()==upload.families[0].part_count,"binding table differs from publication part layout");
    auto changedBinding=bindingFamily;changedBinding.thresholds[0]=11;require(!compatible_bindings(*first.families[0],changedBinding),"LOD change treated as binding-only");
    auto changedModel=std::make_shared<Model>(*bindingModel);changedBinding=bindingFamily;changedBinding.levels[0]=changedModel;
    changedModel->parts[0].geometry.generation++;require(!compatible_bindings(*first.families[0],changedBinding),"geometry generation change treated as binding-only");changedModel->parts[0]=bindingModel->parts[0];
    changedModel->parts[0].material.depth_pipeline++;require(!compatible_bindings(*first.families[0],changedBinding),"pipeline change treated as binding-only");changedModel->parts[0]=bindingModel->parts[0];
    changedModel->parts[0].depth=false;require(!compatible_bindings(*first.families[0],changedBinding),"depth coverage change treated as binding-only");
    require(registry.family_in_use(first.families[0]),"shared owner family is not protected");
    // Unused registrations and retired external handles cannot change the
    // topology reachable from retained actors (including failed admissions).
    auto activeTopology=registry.topology();auto unusedModel=registry.register_model(near);uint64_t unusedLevels[]{unusedModel};auto unusedFamily=registry.register_family({},unusedLevels);Source unusedSource;unusedSource.bytes.resize(64);auto unused=registry.register_source(std::move(unusedSource));require(registry.topology()==activeTopology,"unused resource registration invalidated the published scene");registry.retire_source(unused);registry.retire_family(unusedFamily);registry.retire_model(unusedModel);require(registry.topology()==activeTopology,"unused resource retirement invalidated the published scene");
    auto before=registry.revision();Actor changed=registry.actor(a).input;changed.input.translation[0]=42;changed.appearances.push_back({1,0,{0,0,0,0}});std::pair<uint64_t,Actor> update{a,changed};registry.update(std::span(&update,1));
    require(registry.topology()==first.topology,"pose/appearance commit rebuilt immutable topology");
    auto dirty=registry.changed(before);require(dirty.size()==1&&dirty[0].input.input.translation[0]==42&&dirty[0].input.appearances[0].visible==0,"changed source inputs lost sparse commit");
    Actor wrong=changed;wrong.input.owner=999;std::pair<uint64_t,Actor> invalid[]{update,{b,wrong}};auto oldRevision=registry.revision();
    rejects([&]{registry.update(invalid);},"reassigned batch owner admitted");require(registry.revision()==oldRevision,"failed batch partially mutated registry");
    auto invalidBounds=changed;invalidBounds.input.maximum[0]=1e13;update={a,invalidBounds};rejects([&]{registry.update(std::span(&update,1));},"committed bounds exceed GPU precision domain");require(registry.revision()==oldRevision,"invalid committed pose partially mutated registry");
    registry.retire_model(nm);registry.retire_model(fm);registry.retire_family(family);
    require(first.actors[0].family->levels[0]->parts[0].geometry.mesh==100,"retirement destroyed published immutable source lease");
    rejects([&]{registry.register_actor(actor);},"retired family handle admitted");
    registry.remove_actor(a);actor.input.owner=1002;
    auto replacementModel=registry.register_model(near);uint64_t replacementLevels[]{replacementModel};actor.family=registry.register_family({},replacementLevels);auto replacement=registry.register_actor(actor);
    rejects([&]{registry.actor(a);},"reused actor slot accepts stale handle");require(a!=replacement,"actor slot lost generation identity");
    auto retained=registry.snapshot();require(retained.actors.size()==2,"replacement lost live owner");
    auto gpuState=SevkOpaqueSelector{};gpuState.current=1;gpuState.other=0;gpuState.transition=.25f;registry.selector_handoff(b,gpuState);changed=registry.actor(b).input;changed.input.translation[1]=5;changed.selector.current=99;update={b,changed};
    // Retiring an external family handle does not revoke its retained actor
    // lease. Pose commits keep it; new admissions still reject that stale handle.
    registry.update(std::span(&update,1));require(registry.actor(b).input.selector.current==1&&registry.actor(b).input.selector.transition==.25f,"pose commit imported stale managed selector");
    Model old=near;old.old=true;auto oldModel=registry.register_model(old);uint64_t oldLevels[]{oldModel};auto oldFamily=registry.register_family({},oldLevels);Source source;source.bytes.resize(128);auto src=registry.register_source(std::move(source));Actor oldActor;oldActor.input.owner=2000;oldActor.input.generation=1;oldActor.input.flags=1|2;oldActor.input.scale=1;oldActor.family=oldFamily;oldActor.source=src;oldActor.source_first=1;oldActor.source_count=2;
    rejects([&]{registry.register_actor(oldActor);},"OLD source subrange outside resource admitted");oldActor.source_count=1;auto oa=registry.register_actor(oldActor);auto leased=registry.snapshot();registry.retire_source(src);
    require(registry.actor(oa).source->bytes.size()==128&&leased.sources.size()==1,"OLD source retirement destroys existing immutable lease");
    registry.acknowledge(registry.revision());require(registry.changed(0).empty(),"acknowledged commit replayed");
    Model colourOnly=near;for(auto& p:colourOnly.parts){p.forward=false;p.material.forward_pipeline=UINT32_MAX;}auto colourHandle=registry.register_model(colourOnly);uint64_t colourLevels[]{colourHandle};Actor colourActor=actor;colourActor.input.owner=3000;colourActor.family=registry.register_family({},colourLevels);registry.register_actor(colourActor);auto colourUpload=registry.upload();require(std::any_of(colourUpload.batches.begin(),colourUpload.batches.end(),[](auto& b){return b.forward_pipeline==UINT32_MAX;}),"colour-only source loses explicit forward exclusion");
    auto large=registry.actor(b).input;for(uint32_t i=10;i<100;i++)large.appearances.push_back({i,1});update={b,large};registry.update(std::span(&update,1));auto appRevision=registry.actor(b).appearance_revision;
    require(registry.actor(b).input.appearances.size()>32,"variable resident appearance span was rejected");
    auto preserved=registry.actor(b).input;preserved.replace_appearance=false;preserved.appearances.clear();preserved.input.translation[0]+=1;update={b,preserved};registry.update(std::span(&update,1));
    require(registry.actor(b).appearance_revision==appRevision&&registry.actor(b).input.appearances.size()==large.appearances.size(),"pose preservation replaced appearance or advanced its version");
    gpu_opaque::AppearanceArena arena;arena.reset(2);std::array<uint8_t,128> object{};object[71]=42;arena.object(0,object.data());
    gpu_opaque::AppearanceArena::Update spans[]{ {0,std::vector<SevkOpaqueAppearance>(80)}, {1,std::vector<SevkOpaqueAppearance>(2)} };
    arena.commit(spans);auto second=arena.ranges[1].first;auto oldVersion=arena.versions[0];spans[0].entries.resize(160);arena.commit(std::span(spans,1));
    require(arena.ranges[0].count==160&&arena.ranges[1].first==second&&arena.bytes[71]==42,"span growth corrupted another actor or base object");
    spans[0].entries.resize(1);arena.commit(std::span(spans,1));require(arena.ranges[0].count==1&&arena.versions[0]>oldVersion,"span shrink did not publish its new count/version");
    oldVersion=arena.versions[0];arena.object(0,object.data());arena.commit(std::span(spans,1));require(arena.versions[0]==oldVersion,"unchanged appearance/object commit invalidated its span");
    spans[1].entries.resize(gpu_opaque::appearance_limit+1);auto beforeArena=arena.bytes;rejects([&]{arena.commit(spans);},"oversized appearance batch admitted");require(arena.bytes==beforeArena,"rejected appearance batch partially mutated the arena");
    std::cout<<checks<<" resident registry checks passed\n";
}catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}}
