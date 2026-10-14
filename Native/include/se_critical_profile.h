#pragma once
#define SEVK_CRITICAL_ROWS 22
/* Diagnostic only. Every stride-th completed frame is sampled. Owner/worker
 * scopes and API scopes overlap. CPU time is quantized Windows thread time;
 * cycles are never converted to milliseconds. API time includes loader,
 * validation layers and driver work, not the driver alone. */
typedef struct SevkCriticalRow {uint64_t calls,wall_ns,cpu_100ns,cycles,cpu_reads;} SevkCriticalRow;
typedef struct SevkCriticalProfile {
    uint32_t size,abi_version,enabled,stride;
    uint64_t begun,completed,selected,counter_overhead_ns;
    SevkCriticalRow rows[SEVK_CRITICAL_ROWS];
} SevkCriticalProfile;
SEVK_API int32_t SEVK_CALL sevk_critical_begin(void* ctx,uint32_t stride);
SEVK_API int32_t SEVK_CALL sevk_critical_profile(void* ctx,SevkCriticalProfile* profile);
