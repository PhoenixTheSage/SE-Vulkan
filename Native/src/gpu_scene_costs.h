#pragma once
#include <array>
#include <cstdint>
#include <chrono>
#include <cstdlib>
#include <cstring>

// Process-local diagnostic counters. Ordinary comparison runs do not read a
// clock. Unlike strided critical scopes, these include every rare publication.
struct GpuSceneCosts {
    enum Kind {Snapshot,Capacity,Initialize,InitRanges,InitBuffers,InitPipeline,
        StockPublication,DeltaIndices,DeltaInputs,DeltaAppearance,
        FrameValidation,StockGeometry,RecordingMembership,OwnedBindings,Count};
    bool enabled=[](){auto value=std::getenv("SE_VULKAN_CPU_DETAIL");return value&&std::strcmp(value,"1")==0;}();
    std::array<uint64_t,Count*2> values{};
};
struct GpuSceneCostScope {
    GpuSceneCosts* costs;GpuSceneCosts::Kind kind;
    std::chrono::steady_clock::time_point start{};
    GpuSceneCostScope(GpuSceneCosts& c,GpuSceneCosts::Kind k):costs(c.enabled?&c:nullptr),kind(k){if(costs)start=std::chrono::steady_clock::now();}
    ~GpuSceneCostScope(){if(costs){++costs->values[kind*2];costs->values[kind*2+1]+=uint64_t(std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now()-start).count());}}
};
