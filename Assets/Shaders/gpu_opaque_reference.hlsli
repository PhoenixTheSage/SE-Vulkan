// Explicit scalar layout shared with se_gpu_opaque_reference.h. This is a
// numerical/raster reference, not an authenticated stock shading replacement.
struct Actor {
    double3 translation,minimum,maximum,local_minimum,local_maximum;
    double inverse[12];float basis[9];float scale;float4 colour;
    uint owner,generation,family,flags,source_first,source_count;
    uint2 key_colour;
    uint appearance_count;uint2 hidden_views;uint depth_revision;
};
struct Family {uint generation,first_part,part_count,threshold_count;float thresholds[16];};
struct Part {uint family,lod,first_index,index_count,material,flags,generation,reserved;uint4 textures;int vertex_offset;uint padding;uint draw_first,draw_capacity;uint coverage_count,coverage_materials[32];};
struct View {double4 planes[6];double minimum_size_squared,maximum_distance;row_major float4x4 projection;int minimum_lod,shift,visible_shift;uint flags,id,light;};
struct Frame {double3 camera;double smooth_camera_distance;float delta,coefficient,distance_add,distance_mult;
    float hysteresis,dead_zone,dead_zone_mult,maximum_seconds,minimum_seconds;uint flags,selection,epoch;};
struct Selector {int current,other;float transition,start;uint initialized,border,generation,epoch;double3 last_camera;};
struct Selection {int current,other;float alpha,other_alpha;};
struct Reference {uint actor,view,material,reserved;float rows[12];float alpha;uint3 pad;};
struct DrawRecord {uint actor,part,reference,old;};
struct Indirect {uint index_count,instance_count,first_index;int vertex_offset;uint first_instance;};
struct Control {Frame frame;uint actor_count,family_count,part_count,view_count,reference_capacity,draw_capacity,stage,batch,distance_fade,padding;};
[[vk::push_constant]] ConstantBuffer<Control> control;
[[vk::binding(0,0)]] StructuredBuffer<Actor> actors;
[[vk::binding(1,0)]] StructuredBuffer<Family> families;
[[vk::binding(2,0)]] StructuredBuffer<Part> parts;
[[vk::binding(3,0)]] StructuredBuffer<View> views;
#ifdef GPU_OPAQUE_COMPUTE
#define OPAQUE_BUFFER RWStructuredBuffer
#else
#define OPAQUE_BUFFER StructuredBuffer
#endif
[[vk::binding(4,0)]] OPAQUE_BUFFER<Selector> selectors;
[[vk::binding(5,0)]] OPAQUE_BUFFER<Selection> selections;
[[vk::binding(6,0)]] OPAQUE_BUFFER<uint> groups; // count, offset, cursor
[[vk::binding(7,0)]] OPAQUE_BUFFER<Reference> references;
[[vk::binding(8,0)]] OPAQUE_BUFFER<Indirect> arguments;
[[vk::binding(9,0)]] OPAQUE_BUFFER<DrawRecord> draw_records;
[[vk::binding(10,0)]] OPAQUE_BUFFER<uint> counts; // views then error flags
[[vk::binding(11,0)]] ByteAddressBuffer source_instances;
#ifdef GPU_OPAQUE_COMPUTE
[[vk::binding(14,0)]] RWByteAddressBuffer stock_objects;
[[vk::binding(15,0)]] RWByteAddressBuffer stock_colour;
[[vk::binding(16,0)]] RWByteAddressBuffer stock_depth;
[[vk::binding(17,0)]] ByteAddressBuffer object_inputs;
[[vk::binding(18,0)]] RWByteAddressBuffer stock_draws;
[[vk::binding(19,0)]] RWStructuredBuffer<uint> scan_scratch;
[[vk::binding(20,0)]] RWStructuredBuffer<uint4> spot_signatures;
[[vk::binding(21,0)]] StructuredBuffer<uint> role_parts;
#endif
uint group_index(uint part,uint view,uint old_slice){return (part*control.view_count+view)*2+old_slice;}
double ddot(double3 a,double3 b){precise double value=a.x*b.x+a.y*b.y+a.z*b.z;return value;}
double dsqrt(double value){
    if(value<=0)return 0;
    uint low,high;asuint(value,low,high);int correction=0;
    if((high&0x7ff00000)==0){value*=4503599627370496.0;correction=-52;asuint(value,low,high);}
    int exponent=(int)((high>>20)&2047)-1023+correction;
    double mantissa=asdouble(low,(high&0x000fffff)|(1023u<<20));
    if(exponent&1){mantissa*=2;exponent--;}
    precise double estimate=(double)sqrt((float)mantissa);
    for(uint i=0;i<4;i++)estimate=(estimate+mantissa/estimate)*.5;
    return estimate*asdouble(0,(uint)(exponent/2+1023)<<20);
}
double squared_distance(double3 low,double3 high,double3 cameraPoint){double3 d=max(max(low-cameraPoint,cameraPoint-high),double3(0,0,0));return ddot(d,d);}
bool member(Actor a,View v,uint view,bool ignore_occlusion=false){
    if(!(a.flags&1)||!(v.flags&1)||((v.flags&2)&&!(a.flags&16)))return false;
    if(!ignore_occlusion&&(a.hidden_views[view/32]&(1u<<(view%32))))return false;
    if(((v.flags&32)&&(a.flags&128))||(!(v.flags&34)&&(a.flags&64)))return false;
    if(((v.flags&8)&&!(a.flags&8))||((v.flags&16)&&(a.flags&8)))return false;
    if(v.flags&4)for(uint p=0;p<6;p++){
        precise double3 corner=double3(v.planes[p].x>=0?a.minimum.x:a.maximum.x,v.planes[p].y>=0?a.minimum.y:a.maximum.y,v.planes[p].z>=0?a.minimum.z:a.maximum.z);
        precise double d=v.planes[p].x*corner.x+v.planes[p].y*corner.y+v.planes[p].z*corner.z+v.planes[p].w;
        // Outward roundoff envelope preserves coverage near world-space planes.
        double envelope=(abs(v.planes[p].x*corner.x)+abs(v.planes[p].y*corner.y)+abs(v.planes[p].z*corner.z)+abs(v.planes[p].w))*1.7763568394002505e-15;
        if(d>envelope)return false;
    }
    double3 extent=a.maximum-a.minimum;double size=ddot(extent,extent);
    if((v.flags&2)&&size<=v.minimum_size_squared)return false;
    if(v.maximum_distance>0){double3 centre=(a.maximum+a.minimum)*.5-control.frame.camera;
        double distance=dsqrt(ddot(centre,centre)),limit=v.maximum_distance+dsqrt(size)*.5;
        if(distance>limit+(abs(distance)+abs(limit))*1.7763568394002505e-15)return false;}
    return true;
}
float distance_for(Actor a){
    if(a.flags&2){double3 cameraPoint=control.frame.camera;double squared;
        if(a.flags&4){precise double3 local=double3(a.inverse[0]*cameraPoint.x+a.inverse[1]*cameraPoint.y+a.inverse[2]*cameraPoint.z+a.inverse[3],a.inverse[4]*cameraPoint.x+a.inverse[5]*cameraPoint.y+a.inverse[6]*cameraPoint.z+a.inverse[7],a.inverse[8]*cameraPoint.x+a.inverse[9]*cameraPoint.y+a.inverse[10]*cameraPoint.z+a.inverse[11]);squared=squared_distance(a.local_minimum,a.local_maximum,local);}
        else squared=squared_distance(a.minimum,a.maximum,cameraPoint);
        return (sqrt((float)squared)*control.frame.coefficient+control.frame.distance_add)*control.frame.distance_mult;
    }
    float3 relative=(float3)(a.translation-control.frame.camera);
    return (sqrt(dot(relative,relative))/(a.scale>0?a.scale:1)*control.frame.coefficient+control.frame.distance_add)*control.frame.distance_mult;
}
int choose(float d,Family f){int result=0;while(result<(int)f.threshold_count&&d>=f.thresholds[result])result++;return result;}
Selector advance(Actor a,Family f,Selector s){
    if(s.generation!=a.generation){s=(Selector)0;s.other=-1;s.generation=a.generation;}
    if(s.initialized&&s.epoch==control.frame.epoch)return s;
    float distance=distance_for(a);int chosen=choose(distance,f);bool old=(a.flags&2)!=0;
    double3 jump=s.last_camera-control.frame.camera;bool immediate=!s.initialized||(!old&&ddot(jump,jump)>control.frame.smooth_camera_distance*control.frame.smooth_camera_distance);
    if(old){View main=views[0];chosen=clamp(max(main.minimum_lod,chosen+main.shift),0,(int)f.threshold_count);
        if(f.threshold_count==0||!(control.frame.flags&1))return s;
        if(control.frame.flags&2){s.current=s.other=min(control.frame.selection,f.threshold_count);s.transition=0;s.initialized=1;}
        else if(!s.initialized){s.current=s.other=chosen;s.transition=0;s.initialized=1;}
        else if(s.transition!=0){float width=max(4*exp2((float)s.other),4);float progress=max(s.transition+control.frame.delta/control.frame.maximum_seconds,clamp(abs(distance-s.start)/width/8,0,1));float step=min(abs(s.transition-progress),.25);if(!isfinite(step))step=1;s.transition+=step;if(s.transition>=1){s.current=s.other;s.transition=0;}}
        else if(s.border){float threshold=s.current==0?0:f.thresholds[s.current-1];if(abs(distance-threshold)>threshold*.1)s.border=0;}
        else if(chosen!=s.current){s.other=chosen;s.transition=.001;s.start=distance;s.border=1;}
    }else{
        if(control.frame.flags&2){s.current=min(control.frame.selection,f.threshold_count);s.other=-1;s.transition=0;s.initialized=1;}
        else if(!(control.frame.flags&1)&&s.initialized){}
        else if(immediate){s.current=chosen;s.other=-1;s.transition=0;s.initialized=1;}
        else {
            if(s.transition==0){chosen=s.current;
                while(chosen>0&&distance<f.thresholds[chosen-1]*(1-control.frame.hysteresis))chosen--;
                while(chosen<(int)f.threshold_count&&distance>f.thresholds[chosen]*(1+control.frame.hysteresis))chosen++;
                if(chosen!=s.current){s.other=s.current;s.current=chosen;s.start=distance;}
            }
            if(s.other>=0){float moved=abs(s.start-distance),dead=max(control.frame.dead_zone,control.frame.dead_zone_mult*moved);
                float duration=moved<=dead?control.frame.maximum_seconds:control.frame.minimum_seconds;
                s.transition+=duration>0?max(0,control.frame.delta)/duration:1;if(s.transition>=1){s.transition=0;s.other=-1;}}
        }
    }
    s.last_camera=control.frame.camera;s.epoch=control.frame.epoch;return s;
}
