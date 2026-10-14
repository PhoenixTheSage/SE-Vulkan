#define GPU_OPAQUE_COMPUTE
#include "gpu_opaque_reference.hlsli"
groupshared uint scan_values[64];
uint scan_inclusive(uint value,uint lane){
    scan_values[lane]=value;GroupMemoryBarrierWithGroupSync();
    for(uint step=1;step<64;step*=2){uint add=lane>=step?scan_values[lane-step]:0;GroupMemoryBarrierWithGroupSync();scan_values[lane]+=add;GroupMemoryBarrierWithGroupSync();}
    return scan_values[lane];
}
struct Appearance {uint visible;uint2 colour;float4 old_colour;bool override_object;};
Appearance appearance(uint actor,Actor a,uint material){
    Appearance result;result.visible=1;result.colour=uint2(f32tof16(a.colour.x)|(f32tof16(a.colour.y)<<16),f32tof16(a.colour.z)|(f32tof16(a.colour.w)<<16));result.old_colour=0;result.override_object=false;
    if(a.appearance_count){uint first=object_inputs.Load(actor*144+128),low=0,high=a.appearance_count;
        while(low<high){uint mid=low+(high-low)/2;uint id=object_inputs.Load(first+mid*32);if(id<material)low=mid+1;else high=mid;}
        uint index=a.appearance_count-1;
        [branch] if(low<a.appearance_count){if(object_inputs.Load(first+low*32)==material)index=low;}
        uint at=first+index*32,id=object_inputs.Load(at);
        if(id==material||id==0xffffffff){result.visible=object_inputs.Load(at+4);result.colour=object_inputs.Load2(at+8);result.old_colour=asfloat(object_inputs.Load4(at+16));result.override_object=true;}}
    return result;
}
bool part_member(uint actor,Actor a,Part p,View v){
    if((v.flags&32)&&(p.flags&16))return false;
    if(!(v.flags&34)&&(p.flags&32))return false;
    if(!appearance(actor,a,p.material).visible)return false;
    if(p.flags&12){bool all=true;if(a.appearance_count)for(uint i=0;i<p.coverage_count;i++)all&=appearance(actor,a,p.coverage_materials[i]).visible!=0;
        if((p.flags&4)&&!all)return false;if((p.flags&8)&&all)return false;}
    return true;
}
uint2 selected_parts(Actor a,Family f,View v,int lod){
    if(lod<0||lod>(int)f.threshold_count)return 0;
    if(!(control.frame.flags&4))return uint2(f.first_part,f.part_count);
    uint at=((a.family*17+(uint)lod)*2+((v.flags&2)?1:0))*2;
    return uint2(role_parts[at],role_parts[at+1]);
}
uint selected_part(uint2 range,uint index){return (control.frame.flags&4)?role_parts[range.x+index]:range.x+index;}
void scatter_reference(uint actor,Actor a,View view,uint v,Part part,uint p,uint slice,float alpha){
uint g=group_index(p,v,slice),at;InterlockedAdd(groups[g*3+2],1,at);at+=groups[g*3+1];
Reference r=(Reference)0;r.actor=actor;r.view=v;r.material=part.material;r.alpha=alpha;
float3 relative=(float3)(a.translation-control.frame.camera);
for(uint row=0;row<3;row++){for(uint col=0;col<3;col++)r.rows[row*4+col]=a.basis[row*3+col];r.rows[row*4+3]=relative[row];}
references[at]=r;
bool specialized=(control.frame.flags&4)!=0,old=(a.flags&2)!=0,depth=(view.flags&2)!=0;
for(uint row=0;row<3;row++){
    float4 values=float4(r.rows[row*4],r.rows[row*4+1],r.rows[row*4+2],r.rows[row*4+3]);
    if(!specialized||(!old&&!depth))stock_colour.Store4(at*64+row*16,asuint(values));
    if(!specialized||(!old&&depth))stock_depth.Store4(at*48+row*16,asuint(values));
}
Appearance material=appearance(actor,a,part.material);
if(!specialized||(!old&&!depth)){
uint2 key=a.key_colour;key.y=(key.y&65535)|(f32tof16(r.alpha)<<16);stock_colour.Store2(at*64+48,key);stock_colour.Store2(at*64+56,material.colour);
}
if(!specialized||old){
uint dst=(at+1)*128,src=actor*144;
for(uint word=0;word<128;word+=16)stock_objects.Store4(dst+word,object_inputs.Load4(src+word));
for(uint row=0;row<3;row++)stock_objects.Store4(dst+32+row*16,asuint(float4(r.rows[row*4],r.rows[row*4+1],r.rows[row*4+2],r.rows[row*4+3])));
stock_objects.Store(dst+92,part.lod);stock_objects.Store(dst+116,asuint(r.alpha));if(material.override_object)stock_objects.Store4(dst+96,asuint(material.old_colour));
}
}
[numthreads(64,1,1)]
void main(uint3 id:SV_DispatchThreadID,uint3 block:SV_GroupID,uint lane:SV_GroupIndex){
    uint actor=id.x+id.y*(65535u*64u),block_index=block.x+block.y*65535u,total_groups=control.part_count*control.view_count*2,blocks=(total_groups+63)/64,supers=(blocks+63)/64;
    if(control.stage==0){
        if(actor>=control.actor_count)return;Actor a=actors[actor];Family f=families[a.family];
        bool view_masks=(control.frame.flags&8)!=0;uint2 visible_views=0;bool selected_any=false,main=false;
        for(uint v=0;v<control.view_count;v++){[branch] if(view_masks){bool raw=member(a,views[v],v,true),visible=raw&&!(a.hidden_views[v/32]&(1u<<(v%32)));selected_any|=raw;main|=visible&&!(views[v].flags&34);if(visible)visible_views[v/32]|=1u<<(v%32);}else{selected_any|=member(a,views[v],v,true);main|=member(a,views[v],v)&&!(views[v].flags&34);}}
        Selector s=selectors[actor];if(selected_any)s=advance(a,f,s);selectors[actor]=s;
        for(uint v=0;v<control.view_count;v++){
            Selection selected;selected.current=selected.other=-1;selected.alpha=selected.other_alpha=0;View view=views[v];
            bool visible;[branch] if(view_masks)visible=(visible_views[v/32]&(1u<<(v%32)))!=0;else visible=member(a,view,v);
            if(selected_any&&visible){
                bool old=(a.flags&2)!=0,transition=s.transition>0&&s.other>=0;
                int shift=main?view.visible_shift:view.shift;
                selected.current=old?s.current:clamp(max(view.minimum_lod,s.current+shift),0,(int)f.threshold_count);
                if(transition&&(!(view.flags&34)||old)){
                    selected.other=old?s.other:clamp(max(view.minimum_lod,s.other+shift),0,(int)f.threshold_count);
                    selected.alpha=old?s.transition:1-s.transition;selected.other_alpha=old?3-s.transition:2+s.transition;
                }
            }
            selections[actor*control.view_count+v]=selected;
            if(view.flags&128){uint first=(actor*64+view.id)*2;uint4 next=uint4(asuint(selected.current),asuint(selected.other),asuint(selected.alpha),asuint(selected.other_alpha));uint4 meta=uint4(a.generation,selected.current>=0?a.depth_revision:0,view.light+1,1);
                bool changed=any(spot_signatures[first]!=next)||any(spot_signatures[first+1]!=meta)||(view.flags&64);spot_signatures[first]=next;spot_signatures[first+1]=meta;
                if(changed){uint unused;InterlockedOr(counts[control.view_count*control.batch+1+v],1,unused);}}

        }return;
    }
    if(control.stage==9){
        uint work=actor;if(work>=control.actor_count*control.view_count)return;actor=work/control.view_count;uint v=work%control.view_count;Actor a=actors[actor];Family f=families[a.family];Selection selected=selections[work];View view=views[v];if(selected.current<0&&selected.other<0)return;
            if(!(control.frame.flags&4)){
                for(uint p=f.first_part;p<f.first_part+f.part_count;p++){
                    Part part=parts[p];if(((view.flags&2)?!(part.flags&1):!(part.flags&2))||!part_member(actor,a,part,view))continue;
                    if(selected.current==(int)part.lod){uint unused;InterlockedAdd(groups[group_index(p,v,0)*3],1,unused);}
                    if(selected.other==(int)part.lod){uint unused;InterlockedAdd(groups[group_index(p,v,1)*3],1,unused);}
                }return;
            }
            for(uint slice=0;slice<2;slice++){
                int lod=slice==0?selected.current:selected.other;uint2 range=selected_parts(a,f,view,lod);
                for(uint i=0;i<range.y;i++){uint p=selected_part(range,i);Part part=parts[p];
                    if(lod!=(int)part.lod||((view.flags&2)?!(part.flags&1):!(part.flags&2))||!part_member(actor,a,part,view))continue;
                    uint unused;InterlockedAdd(groups[group_index(p,v,slice)*3],1,unused);
                }
            }
        return;
    }
    if(control.stage==1){
        uint count=0;[branch] if(actor<total_groups)count=groups[actor*3];uint inclusive=scan_inclusive(count,lane);
        if(actor<total_groups)groups[actor*3+1]=inclusive-count;
        if(lane==63&&block_index<blocks)scan_scratch[block_index]=inclusive;
        return;
    }
    if(control.stage==8){if(actor>=control.view_count)return;View v=views[actor];if((v.flags&136)==136)for(uint i=0;i<control.view_count;i++)if((views[i].flags&144)==144&&views[i].light==v.light)counts[control.view_count*control.batch+1+actor]|=counts[control.view_count*control.batch+1+i];return;}
    if(control.stage==7){if(actor>=total_groups)return;uint v=(actor/2)%control.view_count;if((views[v].flags&128)&&!counts[control.view_count*control.batch+1+v])groups[actor*3]=0;return;}
    if(control.stage==4){uint count=0;[branch] if(actor<blocks)count=scan_scratch[actor];uint inclusive=scan_inclusive(count,lane);
        if(actor<blocks)scan_scratch[actor]=inclusive-count;
        if(lane==63&&block_index<supers)scan_scratch[blocks+block_index]=inclusive;return;}
    if(control.stage==5){if(actor!=0)return;uint offset=0;for(uint b=0;b<supers;b++){uint count=scan_scratch[blocks+b];scan_scratch[blocks+b]=offset;offset+=count;}
        if(offset>control.reference_capacity)counts[control.view_count*control.batch]=1;return;}
    if(control.stage==6){if(actor>=total_groups)return;uint b=actor/64;groups[actor*3+1]+=scan_scratch[b]+scan_scratch[blocks+b/64];return;}
    if(control.stage==2){
        uint work=actor;if(work>=control.actor_count*control.view_count||counts[control.view_count*control.batch])return;actor=work/control.view_count;uint v=work%control.view_count;
        Actor a=actors[actor];Family f=families[a.family];
        {
            Selection s=selections[work];View view=views[v];if(s.current<0&&s.other<0)return;
            if(!(control.frame.flags&4)){
                for(uint p=f.first_part;p<f.first_part+f.part_count;p++){
                    Part part=parts[p];if(((view.flags&2)?!(part.flags&1):!(part.flags&2))||!part_member(actor,a,part,view))continue;
                    for(uint slice=0;slice<2;slice++)if((slice==0?s.current:s.other)==(int)part.lod)
                        scatter_reference(actor,a,view,v,part,p,slice,slice==0?s.alpha:s.other_alpha);
                }return;
            }
            for(uint slice=0;slice<2;slice++){
                int lod=slice==0?s.current:s.other;uint2 range=selected_parts(a,f,view,lod);
                for(uint i=0;i<range.y;i++){uint p=selected_part(range,i);Part part=parts[p];
                    if(lod!=(int)part.lod||((view.flags&2)?!(part.flags&1):!(part.flags&2))||!part_member(actor,a,part,view))continue;
                    scatter_reference(actor,a,view,v,part,p,slice,slice==0?s.alpha:s.other_alpha);
                }
            }
        }return;
    }
    if(control.stage==3){
        if(actor>=total_groups||counts[control.view_count*control.batch])return;
        uint count=groups[actor*3],first=groups[actor*3+1];if(!count)return;
        uint p=actor/(control.view_count*2),v=(actor/2)%control.view_count;Part part=parts[p];
        bool old=(actors[references[first].actor].flags&2)!=0;uint n=old?count:1,at;
        uint batch_view=part.reserved*control.view_count+v;
        InterlockedAdd(counts[batch_view],n,at);
        if(at+n>part.draw_capacity){uint unused;InterlockedOr(counts[control.view_count*control.batch],2,unused);return;}
        for(uint i=0;i<n;i++){
            uint dst=v*control.draw_capacity+part.draw_first+at+i;Reference r=references[first+i];Actor a=actors[r.actor];
            Indirect arg;arg.index_count=part.index_count;arg.instance_count=old?a.source_count:count;
            arg.first_index=part.first_index;arg.vertex_offset=part.vertex_offset;arg.first_instance=old?a.source_first:first;
            DrawRecord d;d.actor=r.actor;d.part=p;d.reference=first+i;d.old=old;
            arguments[dst]=arg;draw_records[dst]=d;
            stock_draws.Store4(dst*32,uint4(old?(first+i+1)*128:0,part.material,a.source_first,0));stock_draws.Store4(dst*32+16,part.textures);
        }
    }
}
