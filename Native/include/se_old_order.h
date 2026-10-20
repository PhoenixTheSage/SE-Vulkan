#pragma once
#include <stdint.h>
/* Optional stock OLD identity, independent of physical draw/buffer handles. */
typedef struct SevkOldPartOrder {
    uint64_t key;
    uint32_t resource,material,layout,proxy,valid,reserved;
} SevkOldPartOrder;
