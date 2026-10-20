#include "../src/world_indirect_plan.h"
#include <algorithm>
#include <iostream>

static void require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}
template<class F> static void rejects(F action) {
    try { action(); } catch (const std::runtime_error&) { return; }
    throw std::runtime_error("malformed plan accepted");
}
int main() {
    using Packet = WorldIndirectPlan::Packet;
    std::vector<uint8_t> objects(384, 0);
    std::fill(objects.begin(), objects.begin() + 128, 7);
    std::fill(objects.begin() + 128, objects.begin() + 256, 7);
    std::fill(objects.begin() + 256, objects.end(), 8);
    std::vector<Packet> packets{
        {10, 20, 2, 36, 2, 0, 0, 128, true},
        {10, 20, 2, 36, 5, 192, 128, 128, true},
        {10, 20, 2, 36, 1, 512, 256, 128, true}};
    std::vector<uint32_t> selection{0, 1, 2};
    WorldIndirectPlan plan;
    auto build = [&](uint32_t limit = 1024) { plan.build(selection, packets, objects, limit); };
    build();
    require(plan.runs.size() == 2 && plan.runs[0].indirect && plan.runs[0].packet_count == 2 &&
            !plan.runs[1].indirect && plan.runs[1].selection_start == 2, "unequal object bytes combined");
    require(plan.commands.size() == 2 && plan.commands[0].indexCount == 36 &&
            plan.commands[0].instanceCount == 2 && plan.commands[0].firstInstance == 0 &&
            plan.commands[1].instanceCount == 5 && plan.commands[1].firstInstance == 3 &&
            plan.commands[1].firstIndex == 0 && plan.commands[1].vertexOffset == 0,
            "indirect instance range or indexed command changed");
    selection = {1, 0}; build();
    require(plan.commands[0].firstInstance == 3 && plan.commands[1].firstInstance == 0,
            "caller selection order changed");
    selection = {0, 2}; build();
    require(plan.commands.empty() && plan.runs.size() == 2, "filtered selection expanded");
    selection = {0, 1};
    for (int boundary = 0; boundary < 7; ++boundary) {
        Packet original = packets[1];
        if (boundary == 0) ++packets[1].mesh;
        if (boundary == 1) ++packets[1].material;
        if (boundary == 2) ++packets[1].pipeline;
        if (boundary == 3) ++packets[1].index_count;
        if (boundary == 4) packets[1].eligible = false;
        if (boundary == 5) packets[1].raster_state = 0x10000000u;
        if (boundary == 6) packets[1].raster_state = 0x20000000u;
        build(); require(plan.commands.empty() && plan.runs.size() == 2, "binding/eligibility boundary crossed");
        packets[1] = original;
    }
    selection = {0, 1, 0, 1, 0}; build(2);
    require(plan.runs.size() == 3 && plan.commands.size() == 4 &&
            plan.runs[1].command_start == 2 && !plan.runs[2].indirect, "device draw-count limit exceeded");
    build(1); require(plan.commands.empty() && plan.runs.size() == 5, "single-draw fallback changed");
    selection.clear(); build(); require(plan.runs.empty() && plan.commands.empty(), "empty pass kept stale commands");
    selection = {0, 999}; rejects([&] { build(); });
    require(plan.runs.empty() && plan.commands.empty(), "failed validation exposed partial plan");
    selection = {1};
    packets[1].object_offset = UINT32_MAX; rejects([&] { build(); }); packets[1].object_offset = 128;
    packets[1].instance_byte_offset = 1; rejects([&] { build(); }); packets[1].instance_byte_offset = 192;
    packets[1].instance_count = 0; rejects([&] { build(); }); packets[1].instance_count = 5;
    rejects([&] { build(0); });
    packets[0].arena=packets[1].arena=true;packets[0].vertex_buffer=packets[1].vertex_buffer=100;packets[0].index_buffer=packets[1].index_buffer=101;
    packets[1].mesh=999;packets[1].index_count=6;packets[1].first_index=3;packets[1].vertex_offset=3;
    selection={0,1};build();
    require(plan.runs.size()==1 && plan.runs[0].indirect && plan.commands[1].indexCount==6 && plan.commands[1].firstIndex==3 && plan.commands[1].vertexOffset==3,"shared pages did not retain distinct mesh index/base-vertex records");
    ++packets[1].vertex_buffer;build();require(plan.commands.empty(),"different vertex pages combined");
    --packets[1].vertex_buffer;packets[0].old_order=packets[1].old_order={1653665488374690ull,23,34,12,0,1,0};build();require(plan.runs.size()==1&&plan.runs[0].indirect,"matching OLD source identity lost indirect eligibility");
    packets[1].old_order.resource+=128;packets[1].old_order.material+=128;packets[1].old_order.layout+=64;build();require(plan.commands.empty()&&plan.runs.size()==2,"truncated OLD key collision crossed an indirect batch boundary");
    packets[1].old_order=packets[0].old_order;packets[1].old_order.proxy++;build();require(plan.commands.empty(),"distinct OLD source proxies crossed an indirect batch boundary");
    std::cout << "PASS indirect commands, byte-exact bindings, selection identity, device limits and invalid ranges\n";
}
