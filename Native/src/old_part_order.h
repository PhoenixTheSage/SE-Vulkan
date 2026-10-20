#pragma once
#include "se_old_order.h"
#include <stdexcept>
#include <tuple>
inline auto old_order_tuple(const SevkOldPartOrder& o){return std::tie(o.valid,o.key,o.resource,o.material,o.layout,o.proxy);}
inline bool old_order_equal(const SevkOldPartOrder& a,const SevkOldPartOrder& b){return old_order_tuple(a)==old_order_tuple(b);}
inline void validate_old_order(const SevkOldPartOrder& o){
    if(o.valid>1||o.reserved||(!o.valid&&(o.key||o.resource||o.material||o.layout||o.proxy))||
       (o.valid&&(o.key>>51||((o.key>>14)&63)!=(o.layout&63)||((o.key>>7)&127)!=(o.resource&127)||(o.key&127)!=(o.material&127))))
        throw std::runtime_error("invalid stock OLD identity metadata");
}
static_assert(sizeof(SevkOldPartOrder)==32);
