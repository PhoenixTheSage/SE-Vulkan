#pragma once
#include <cstdint>
#include <cstring>
#include <span>
#include <stdexcept>
#include <vector>
#include "old_part_order.h"

// CPU-owned preparation for one caller-selected pass. Selection order and draw
// identities survive batching; the GPU bridge supplies authenticated eligibility.
struct WorldIndirectPlan {
    struct Packet {
        uint64_t mesh{}, material{};
        uint32_t pipeline{}, index_count{}, instance_count{}, instance_byte_offset{};
        uint32_t object_offset{}, object_bytes{};
        bool eligible{};
        uint64_t vertex_buffer{},index_buffer{};
        uint32_t first_index{};int32_t vertex_offset{};bool arena{};
        uint32_t raster_state{},main_order{},main_part{};
        SevkOldPartOrder old_order{};
    };
    // VkDrawIndexedIndirectCommand layout, without requiring Vulkan in host tests.
    struct Command {
        uint32_t indexCount, instanceCount, firstIndex;
        int32_t vertexOffset;
        uint32_t firstInstance;
    };
    static_assert(sizeof(Command) == 20);
    struct Run {
        uint32_t selection_start{}, packet_count{}, command_start{};
        bool indirect{};
    };
    std::vector<Command> commands;
    std::vector<Run> runs;

    void build(std::span<const uint32_t> selection, std::span<const Packet> packets,
               std::span<const uint8_t> objects, uint32_t max_draw_count) {
        commands.clear(); runs.clear();
        if (!max_draw_count || selection.size() > UINT32_MAX)
            throw std::runtime_error("invalid indirect plan limit");
        // Validate the entire input before exposing a partial execution plan.
        for (uint32_t id : selection) {
            if (id >= packets.size()) throw std::runtime_error("indirect selection out of range");
            const auto& p = packets[id];
            if (!p.index_count || !p.instance_count || !p.object_bytes ||
                uint64_t(p.object_offset) + p.object_bytes > objects.size() ||
                (p.eligible && p.instance_byte_offset % 64))
                throw std::runtime_error("invalid indirect packet range");
        }
        auto compatible = [&](const Packet& a, const Packet& b) {
            bool geometry=a.arena==b.arena && (a.arena ? a.vertex_buffer==b.vertex_buffer && a.index_buffer==b.index_buffer : a.mesh==b.mesh && a.index_count==b.index_count);
            return a.eligible && b.eligible && geometry && a.material == b.material &&
                a.pipeline == b.pipeline && a.raster_state == b.raster_state && a.main_order==b.main_order && a.main_part==b.main_part && old_order_equal(a.old_order,b.old_order) &&
                a.object_bytes == b.object_bytes &&
                std::memcmp(objects.data() + a.object_offset, objects.data() + b.object_offset,
                            a.object_bytes) == 0;
        };
        for (uint32_t at = 0; at < selection.size();) {
            uint32_t count = 1;
            const auto& first = packets[selection[at]];
            while (count < max_draw_count && uint64_t(at) + count < selection.size() &&
                   compatible(first, packets[selection[at + count]])) ++count;
            Run run{at, count, static_cast<uint32_t>(commands.size()), count > 1};
            if (run.indirect) for (uint32_t n = 0; n < count; ++n) {
                const auto& p = packets[selection[at + n]];
                commands.push_back({p.index_count, p.instance_count, p.first_index, p.vertex_offset, p.instance_byte_offset / 64});
            }
            runs.push_back(run);
            at += count;
        }
    }
};
