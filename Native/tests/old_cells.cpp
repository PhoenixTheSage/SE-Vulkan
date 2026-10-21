#include "../src/old_cells.h"
#include <cstdio>
#include <iostream>

static void require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
static old_cells::Cell make_cell(uint32_t owner,double x,uint64_t group=0){
    old_cells::Cell cell;cell.owner=owner;cell.group=group;cell.visible=cell.cast_flag=cell.casts=true;
    cell.bounds={{x-1,-1,-1},{x+1,1,1}};for(int i=0;i<16;i++)cell.world[i]=cell.inverse[i]=i%5==0?1:0;cell.world[12]=x;cell.inverse[12]=-x;
    for(int i=0;i<3;i++){cell.local_minimum[i]=-1;cell.local_maximum[i]=1;}
    old_cells::Part part;part.recipe=1;part.row0=0;part.row1=16;part.row2=32;part.alpha_offset=48;part.constants.assign(64,0);
    cell.thresholds={50};cell.levels={{1,{part}},{2,{part}}};cell.levels[1].parts[0].recipe=2;
    cell.count=2;cell.instances.assign(128,7);return cell;
}
static old_cells::Frame make_frame(){
    old_cells::Frame frame;frame.camera={0,0,0};frame.new_views=true;frame.probe_distance=0;frame.shadow_distance=100;
    // Everything with x<200 is inside the main view.
    frame.main.enabled=true;frame.main.planes[0]={1,0,0,-200};for(int i=1;i<6;i++)frame.main.planes[i]={0,0,0,-1};
    frame.global.updates=true;frame.global.maximum_seconds=1;frame.delta=.1f;return frame;
}
int main(){
    {   // Main view, cull and probe/occlusion flags.
        old_cells::Scene scene;scene.add(make_cell(1,10,7));scene.add(make_cell(2,150,7));scene.add(make_cell(3,300));
        scene.prepare(make_frame(),nullptr);
        require(scene.visible_owners()==std::vector<uint32_t>({1,2}),"main visible owners");
        require(scene.requests().size()==1&&scene.requests()[0].main&&scene.requests()[0].representative==1,"group query aggregation");
        scene.publish({{7,true,0}});
        require(scene.emitted().size()==2,"far probe-only caster was emitted");
        for(auto& d:scene.emitted())require((d.flags&4096)&&(d.flags&16384)&&!(d.flags&512),"occluded/outside-probe flags");
        uint8_t bytes[64];old_cells::Scene::write_constants(scene.emitted()[0],{1,0,0},bytes);float row0[4];std::memcpy(row0,bytes,16);require(row0[0]==1&&row0[3]==9,"camera-relative matrix row");
    }
    {   // Probe-only caster inside shadow distance keeps cascades; size and frustum gates.
        old_cells::Scene scene;auto cell=make_cell(1,250);scene.add(cell);auto frame=make_frame();frame.shadow_distance=300;
        // 0: unfitted; 1: below the small-object threshold; 2: disjoint; 3: containing.
        auto open=[](scene_actor::Frustum& f){f.enabled=true;for(auto& p:f.planes)p={0,0,0,-1};};
        frame.cascades[0]={true,false,false,0,{}};frame.cascades[1]={true,true,true,13,{}};open(frame.cascades[1].frustum);
        frame.cascades[2]={true,false,true,0,{}};open(frame.cascades[2].frustum);frame.cascades[2].frustum.planes[0]={1,0,0,-100};
        frame.cascades[3]={true,false,true,0,{}};open(frame.cascades[3].frustum);
        scene.prepare(frame,nullptr);scene.publish({});
        require(scene.visible_owners().empty()&&scene.emitted().size()==1,"probe-only caster");
        require(scene.emitted()[0].cascades==9u,"cascade size/frustum mask");
    }
    {   // LOD transition draws current and next with complementary alpha.
        old_cells::Scene scene;auto cell=make_cell(1,0);cell.lod_eligible=true;auto handle=scene.add(cell);auto frame=make_frame();
        scene.prepare(frame,nullptr);scene.publish({});require(scene.emitted().size()==1&&scene.representation_changes().size()==1,"initial selection reports a representation");
        auto moved=make_cell(1,60);moved.lod_eligible=true;scene.update(handle,moved);
        scene.prepare(frame,nullptr);scene.publish({});auto& draws=scene.emitted();
        require(draws.size()==2&&draws[0].part->recipe==1&&draws[1].part->recipe==2,"transition draws both levels");
        require(draws[0].alpha>0&&draws[1].alpha==3-draws[0].alpha&&(draws[0].flags&0x1000000u)&&(draws[1].flags&0x1000000u),"transition alpha and dynamic flag");
        // Identical level models draw one mesh without alpha.
        old_cells::Scene shared;auto same=make_cell(1,0);same.lod_eligible=true;same.levels[1].model=1;auto h=shared.add(same);shared.prepare(frame,nullptr);shared.publish({});
        auto far=make_cell(1,60);far.lod_eligible=true;far.levels[1].model=1;shared.update(h,far);shared.prepare(frame,nullptr);shared.publish({});
        require(shared.emitted().size()==1&&shared.emitted()[0].alpha==0,"same-model transition collapses");
    }
    std::cout<<"OLD cells: cull, flags, queries, cascades, constants and LOD transitions passed\n";
}
