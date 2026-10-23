// Vulkan interface matching for owner stage pairs: legal widening, packing and interpolation, and the
// mismatches that must be refused before activation.
#include "../src/stage_interface.h"
#include <cstdio>
#include <deque>

namespace {
std::deque<SpvReflectTypeDescription> types;
std::deque<SpvReflectInterfaceVariable> storage;
struct Var {uint32_t location,component,count,width=32,kind=0;bool flat=false,builtin=false;uint32_t array=0;};
SpvReflectInterfaceVariable* make(const Var& v){
    auto& t=types.emplace_back();t.type_flags=v.kind==0?SPV_REFLECT_TYPE_FLAG_FLOAT:SPV_REFLECT_TYPE_FLAG_INT;
    auto& r=storage.emplace_back();r.location=v.location;r.component=v.component;r.numeric.vector.component_count=v.count>1?v.count:0;r.numeric.scalar.width=v.width;r.numeric.scalar.signedness=v.kind==1;
    r.type_description=&t;if(v.flat)r.decoration_flags|=SPV_REFLECT_DECORATION_FLAT;if(v.builtin){r.decoration_flags|=SPV_REFLECT_DECORATION_BUILT_IN;r.built_in=0;}
    if(v.array){r.array.dims_count=1;r.array.dims[0]=v.array;}return &r;}
struct Module {std::vector<SpvReflectInterfaceVariable*> in,out;SpvReflectShaderModule m{};
    SpvReflectShaderModule& get(){m.input_variable_count=uint32_t(in.size());m.input_variables=in.data();m.output_variable_count=uint32_t(out.size());m.output_variables=out.data();return m;}};
int failures=0;
void expect(bool legal,std::vector<Var> outputs,std::vector<Var> inputs,bool widening,const char* text){
    Module producer,consumer;for(auto& v:outputs)producer.out.push_back(make(v));for(auto& v:inputs)consumer.in.push_back(make(v));
    bool ok=true;std::string error;try{validate_stage_interface(stage_outputs(producer.get(),"test"),consumer.get(),widening,"test");}catch(const std::exception& e){ok=false;error=e.what();}
    if(ok!=legal){++failures;std::printf("FAIL %s (%s)\n",text,error.c_str());}else std::printf("PASS %s%s%s\n",text,error.empty()?"":": ",error.c_str());
}
}

int main(){
    expect(true,{{0,0,2}},{{0,0,2}},false,"identical float2 link");
    expect(true,{{0,0,4},{1,0,2}},{{0,0,4}},false,"unread producer outputs are legal");
    expect(true,{{0,0,2,32,0,false,true},{0,0,2}},{{0,0,2,32,0,false,true},{0,0,2}},false,"built-ins do not take part");
    expect(true,{{0,0,2}},{{0,0,2,32,0,true}},false,"interpolation decorations are exempt (smooth output, flat input)");
    expect(true,{{3,0,1,32,2,true}},{{3,0,1,32,2,false}},false,"interpolation decorations are exempt (flat output, unflagged input)");
    expect(true,{{0,0,4}},{{0,0,2}},true,"maintenance4: a wider output vector feeds a narrower input");
    expect(false,{{0,0,4}},{{0,0,2}},false,"without maintenance4 vector sizes must match");
    expect(false,{{0,0,2}},{{0,0,4}},true,"an input never widens past its output");
    expect(true,{{0,0,2},{0,2,2}},{{0,2,2},{0,0,1}},true,"Component-packed outputs match by Location and Component");
    expect(false,{{0,0,2}},{{0,1,1}},true,"an input must start at an output's Component");
    expect(false,{{0,0,2}},{{1,0,2}},true,"every input needs a matching output");
    expect(false,{{0,0,2}},{{0,0,2,32,1}},true,"component type must match (float vs int)");
    expect(false,{{0,0,2,32,1}},{{0,0,2,32,2}},true,"signedness must match");
    expect(false,{{0,0,2,16}},{{0,0,2,32}},true,"component width must match");
    expect(false,{{0,0,4},{0,2,2}},{{0,0,4}},true,"overlapping output Component slots are refused");
    expect(false,{{0,3,2}},{{0,3,2}},true,"a 32-bit vector may not span Locations");
    expect(true,{{2,0,4,32,0,false,false,2}},{{2,0,4,32,0,false,false,2}},true,"matching arrays");
    expect(false,{{2,0,4,32,0,false,false,2}},{{2,0,4,32,0,false,false,3}},true,"array lengths must match");
    expect(false,{{2,0,4,32,0,false,false,2},{3,0,1}},{{2,0,4,32,0,false,false,2}},true,"an array's later Locations overlap following outputs");
    if(failures)std::printf("%d stage interface case(s) failed\n",failures);else std::printf("PASS all stage interface cases\n");
    return failures?1:0;
}
