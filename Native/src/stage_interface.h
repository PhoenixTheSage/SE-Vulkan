#pragma once
// Vulkan "Interface Matching" for user-defined stage variables. Every consumer input needs a producer output at the
// same Location and Component, declared with an equivalent type; with maintenance4 the output vector may carry more
// components of the same component type. Interpolation decorations (Flat, NoPerspective, Centroid, Sample) are
// exempt, and outputs the next stage does not read are legal. Owners are refused here, before activation, rather
// than relying on validation layers.
#include <spirv_reflect.h>
#include <algorithm>
#include <cstdint>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>

struct StageSlot {uint32_t location{},component{},components{},width{},kind{},columns{},array{1};};
inline uint32_t stage_slot_kind(const SpvReflectInterfaceVariable& v){
    auto flags=v.type_description?v.type_description->type_flags:0;
    return (flags&SPV_REFLECT_TYPE_FLAG_FLOAT)?0u:(flags&SPV_REFLECT_TYPE_FLAG_INT)?(v.numeric.scalar.signedness?1u:2u):3u;
}
inline void stage_interface_slots(const SpvReflectInterfaceVariable& v,std::vector<StageSlot>& slots,const std::string& role){
    if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)return;
    if(v.member_count){for(uint32_t i=0;i<v.member_count;++i)stage_interface_slots(v.members[i],slots,role);return;}
    if(v.location==UINT32_MAX)return;
    StageSlot s{v.location,v.component==UINT32_MAX?0u:v.component,std::max(1u,v.numeric.vector.component_count),v.numeric.scalar.width,stage_slot_kind(v),v.numeric.matrix.column_count,1};
    for(uint32_t d=0;d<v.array.dims_count;++d)s.array*=v.array.dims[d];
    if(s.kind>2||(s.width!=16&&s.width!=32&&s.width!=64))throw std::runtime_error(role+" stage interface has an unsupported variable type");
    // 16/32-bit scalars and vectors must stay within one Location; a 64-bit component takes two Component slots.
    if(s.width!=64&&s.component+s.components>4)throw std::runtime_error(role+" stage interface spans a Location");
    slots.push_back(s);
}
// A variable covers [location, location+locations) x the Component mask below.
inline uint32_t stage_slot_locations(const StageSlot& s){uint32_t per=s.width==64&&s.components>2?2:1;return std::max(1u,s.columns)*s.array*per;}
inline uint32_t stage_slot_mask(const StageSlot& s){uint32_t used=std::min(4u,s.components*(s.width==64?2:1));return ((1u<<used)-1u)<<s.component;}
inline void validate_stage_interface(const std::vector<StageSlot>& outputs,const SpvReflectShaderModule& consumer,bool widening,const std::string& role){
    std::map<uint32_t,uint32_t> occupied;
    for(const auto& o:outputs)for(uint32_t l=0;l<stage_slot_locations(o);++l){auto& mask=occupied[o.location+l];if(mask&stage_slot_mask(o))throw std::runtime_error(role+" stage interface has overlapping output Component slots");mask|=stage_slot_mask(o);}
    std::vector<StageSlot> inputs;for(uint32_t i=0;i<consumer.input_variable_count;++i)stage_interface_slots(*consumer.input_variables[i],inputs,role);
    for(const auto& in:inputs){
        auto match=std::find_if(outputs.begin(),outputs.end(),[&](const StageSlot& o){return o.location==in.location&&o.component==in.component;});
        if(match==outputs.end())throw std::runtime_error(role+" stage input at Location "+std::to_string(in.location)+" Component "+std::to_string(in.component)+" has no matching output");
        bool same_shape=match->kind==in.kind&&match->width==in.width&&match->columns==in.columns&&match->array==in.array;
        bool wider=widening&&!in.columns&&in.array==1&&match->components>in.components;
        if(!same_shape||(match->components!=in.components&&!wider))throw std::runtime_error(role+" stage interface type mismatch at Location "+std::to_string(in.location));
    }
}
inline std::vector<StageSlot> stage_outputs(const SpvReflectShaderModule& producer,const std::string& role){
    std::vector<StageSlot> outputs;for(uint32_t i=0;i<producer.output_variable_count;++i)stage_interface_slots(*producer.output_variables[i],outputs,role);return outputs;
}
