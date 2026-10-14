struct GuestPipeline {
    VkPipeline pipeline{}; VkPipelineLayout layout{}; VkDescriptorSetLayout set{};
    VkShaderModule vertex{}, pixel{};
    VkPipeline draw[4][5]{};
    std::vector<std::pair<uint32_t, VkDescriptorType>> bindings;
    uint32_t descriptors{}, owned_bytes{}, extras_bytes{}, uniform_bytes{};
    bool bus{}, compute{};
};
struct GuestImage {
    Image image{}; uint32_t width{}, height{}, depth{1}, mips{1}, bpp{4}, layers{1};
    Buffer storage{}; bool is_buffer{};
    VkFormat format{VK_FORMAT_R8G8B8A8_UNORM}; VkImageLayout layout{VK_IMAGE_LAYOUT_UNDEFINED};
};
struct GuestPass {
    uint32_t mark{}, compose{}, width{}, height{}, uniform_bytes{}, flags{};
    uint64_t pipeline{}, storage{}; uint8_t uniforms[256]{}; uint64_t sampled[14]{};
};
struct GuestBump { Buffer buffer{}; uint32_t used{}; };
struct GuestExtras {
    float render[2], inv_render[2];
    uint32_t has_velocity, history_valid, attach_count, frame_index;
    float jitter[2], safety, safety_pad;
    float unjittered[16], prev[16], camera[12], proj_scale[2], camera_pad[2], scene[2], inv_scene[2];
    float sun_color[3], sun_diffuse, sun_toward[3], sky_luma, sky_ambient[3], sky_pad;
    float air_top, visual_ceil, skip_floor, skip_mul;
};
static_assert(sizeof(GuestExtras) == 320, "fullscreen extras are 320 bytes");
static_assert(sizeof(SevkGuestFrame) == 344, "guest frame is 344 bytes");
struct GuestState {
    std::vector<GuestPass> passes;
    std::unordered_map<uint64_t, GuestPipeline> pipelines;
    std::unordered_map<uint64_t, GuestImage> images;
    uint64_t next_pipeline{1}, replay_count{}, frame_stamp{};
    VkDescriptorPool pools[3]{};
    uint64_t pool_stamp[3]{};
    GuestBump bumps[3]{};
    VkSampler samplers[3]{};
    Image dummy{}, scratch{};
    uint32_t scratch_w{}, scratch_h{};
    float prev_vp[16]{}, prev_camera[3]{};
    bool have_prev{};
    SevkGuestFrame frame{};
} guest;

void cleanup_guest() {
    for (auto& item : guest.pipelines) {
        bool separate = item.second.pipeline && !(item.second.bus && !item.second.compute);
        if (separate) vkDestroyPipeline(device, item.second.pipeline, nullptr);
        for (auto& row : item.second.draw) for (auto pipe : row) if (pipe) vkDestroyPipeline(device, pipe, nullptr);
        if (item.second.layout) vkDestroyPipelineLayout(device, item.second.layout, nullptr);
        if (item.second.set) vkDestroyDescriptorSetLayout(device, item.second.set, nullptr);
        if (item.second.vertex) vkDestroyShaderModule(device, item.second.vertex, nullptr);
        if (item.second.pixel) vkDestroyShaderModule(device, item.second.pixel, nullptr);
    }
    for (auto& item : guest.images) { if (item.second.is_buffer) destroy_buffer(item.second.storage); else destroy_image(item.second.image); }
    for (auto pool : guest.pools) if (pool) vkDestroyDescriptorPool(device, pool, nullptr);
    for (auto& bump : guest.bumps) if (bump.buffer.handle) destroy_buffer(bump.buffer);
    for (auto sampler : guest.samplers) if (sampler) vkDestroySampler(device, sampler, nullptr);
    if (guest.dummy.handle) destroy_image(guest.dummy);
    if (guest.scratch.handle) destroy_image(guest.scratch);
    guest = {};
}

VkShaderModule guest_shader(const void* data, uint64_t bytes, VkShaderStageFlagBits stage, SpvReflectShaderModule& reflected) {
    if (!data || bytes < 20 || bytes % 4 || bytes > 16 * 1024 * 1024) throw std::runtime_error("invalid guest SPIR-V size");
    auto* words = static_cast<const uint32_t*>(data);
    for (uint64_t i = 5; i < bytes / 4;) {
        uint32_t n = words[i] >> 16, op = words[i] & 65535;
        if (!n || n > bytes / 4 - i) throw std::runtime_error("malformed guest SPIR-V");
        if (op == 10) throw std::runtime_error("guest shader requests a SPIR-V extension");
        i += n;
    }
    if (spvReflectCreateShaderModule(bytes, data, &reflected) != SPV_REFLECT_RESULT_SUCCESS) throw std::runtime_error("guest reflection failed");
    if (uint32_t(reflected.shader_stage) != uint32_t(stage)) { spvReflectDestroyShaderModule(&reflected); throw std::runtime_error("guest shader stage mismatch"); }
    VkShaderModuleCreateInfo info{ VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO }; info.codeSize = bytes; info.pCode = words;
    VkShaderModule module{};
    try { check(vkCreateShaderModule(device, &info, nullptr, &module), "create guest shader"); }
    catch (...) { spvReflectDestroyShaderModule(&reflected); throw; }
    return module;
}

bool guest_binding_allowed(const SpvReflectDescriptorBinding& binding, bool compute, GuestPipeline& stored) {
    if (binding.set || binding.count != 1) return false;
    auto type = VkDescriptorType(binding.descriptor_type);
    if (type == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER) {
        if (binding.binding == 0 && binding.block.size <= 256) { stored.owned_bytes = binding.block.size; return true; }
        if (binding.binding == 6 && binding.block.size <= 320) { stored.extras_bytes = binding.block.size; return true; }
        if (binding.binding == 7 && binding.block.size <= 256) { stored.uniform_bytes = binding.block.size; return true; }
        return false;
    }
    if (type == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE) return binding.binding >= 128 && binding.binding <= 141;
    if (type == VK_DESCRIPTOR_TYPE_SAMPLER) return binding.binding >= 256 && binding.binding <= 258;
    if (type == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER) return binding.binding == 129 || binding.binding == 138 || binding.binding == 139;
    return compute && type == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE && binding.binding == 384;
}

void guest_ensure_samplers() {
    if (guest.samplers[0]) return;
    auto make = [&](uint32_t index, VkFilter filter, VkSamplerAddressMode address) {
        VkSamplerCreateInfo info{ VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO };
        info.magFilter = info.minFilter = filter; info.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
        info.addressModeU = info.addressModeV = info.addressModeW = address; info.maxLod = 16;
        check(vkCreateSampler(device, &info, nullptr, &guest.samplers[index]), "create guest sampler");
    };
    make(0, VK_FILTER_NEAREST, VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE);
    make(1, VK_FILTER_LINEAR, VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE);
    make(2, VK_FILTER_LINEAR, VK_SAMPLER_ADDRESS_MODE_REPEAT);
}

uint64_t guest_create_pipeline(const void* vs, uint64_t vs_bytes, const char* vs_entry, const void* ps, uint64_t ps_bytes, const char* ps_entry) {
    if (recording) throw std::runtime_error("guest pipeline during recording");
    if (!vs_entry || !ps_entry) throw std::runtime_error("guest entry point missing");
    bool compute = std::strcmp(vs_entry, "compute") == 0;
    SpvReflectShaderModule vr{}, pr{}; VkShaderModule vertex{}, pixel{}; VkPipelineLayout layout{}; VkDescriptorSetLayout set{};
    try {
        if (!compute) vertex = guest_shader(vs, vs_bytes, VK_SHADER_STAGE_VERTEX_BIT, vr);
        pixel = guest_shader(ps, ps_bytes, compute ? VK_SHADER_STAGE_COMPUTE_BIT : VK_SHADER_STAGE_FRAGMENT_BIT, pr);
        uint32_t descriptors = (compute ? 0 : vr.descriptor_binding_count) + pr.descriptor_binding_count;
        GuestPipeline stored{}; stored.vertex = vertex; stored.pixel = pixel; stored.descriptors = descriptors; stored.compute = compute;
        if (descriptors == 0 && !compute) {
            VkPipelineLayoutCreateInfo layout_info{ VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO };
            check(vkCreatePipelineLayout(device, &layout_info, nullptr, &layout), "create guest pipeline layout");
            stored.layout = layout;
            VkFormat format = VK_FORMAT_R8G8B8A8_UNORM;
            stored.pipeline = world_graphics(vertex, vs_entry, pixel, ps_entry, stored.layout, {}, {}, &format, 1, false);
        } else {
            guest_ensure_samplers();
            std::vector<VkDescriptorSetLayoutBinding> bindings;
            auto take = [&](SpvReflectShaderModule& module, VkShaderStageFlags stage) {
                for (uint32_t i = 0; i < module.descriptor_binding_count; ++i) {
                    auto& binding = module.descriptor_bindings[i];
                    if (!guest_binding_allowed(binding, compute, stored)) return false;
                    bool duplicate = false;
                    for (auto& existing : bindings) duplicate |= existing.binding == binding.binding;
                    if (duplicate) continue;
                    VkSampler* sampler = nullptr;
                    auto type = VkDescriptorType(binding.descriptor_type);
                    if (type == VK_DESCRIPTOR_TYPE_SAMPLER) sampler = &guest.samplers[binding.binding - 256];
                    else stored.bindings.push_back({ binding.binding, type });
                    bindings.push_back({ binding.binding, type, 1, stage, sampler });
                }
                return true;
            };
            if ((!compute && !take(vr, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT)) || !take(pr, compute ? VK_SHADER_STAGE_COMPUTE_BIT : VK_SHADER_STAGE_FRAGMENT_BIT)) {
                if (vertex) vkDestroyShaderModule(device, vertex, nullptr);
                if (pixel) vkDestroyShaderModule(device, pixel, nullptr);
                spvReflectDestroyShaderModule(&vr); spvReflectDestroyShaderModule(&pr);
                return 0;
            }
            VkDescriptorSetLayoutCreateInfo set_info{ VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO };
            set_info.bindingCount = uint32_t(bindings.size()); set_info.pBindings = bindings.data();
            check(vkCreateDescriptorSetLayout(device, &set_info, nullptr, &set), "create guest bus layout");
            stored.set = set;
            VkPipelineLayoutCreateInfo layout_info{ VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO };
            layout_info.setLayoutCount = 1; layout_info.pSetLayouts = &set;
            check(vkCreatePipelineLayout(device, &layout_info, nullptr, &layout), "create guest bus pipeline layout");
            stored.layout = layout; stored.bus = true;
            if (compute) {
                VkComputePipelineCreateInfo info{ VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO };
                info.stage = { VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO, nullptr, 0, VK_SHADER_STAGE_COMPUTE_BIT, pixel, ps_entry, nullptr };
                info.layout = layout;
                check(vkCreateComputePipelines(device, pipeline_cache, 1, &info, nullptr, &stored.pipeline), "create guest compute pipeline");
            } else {
                VkFormat formats[]{ VK_FORMAT_R8G8B8A8_UNORM, VK_FORMAT_R16G16B16A16_SFLOAT, VK_FORMAT_R32_SFLOAT, VK_FORMAT_R16G16_SFLOAT };
                for (int format = 0; format < 4; ++format)
                    for (int mode = 0; mode < (format < 2 ? 5 : 1); ++mode) {
                        bool depth = mode == 4, additive = mode == 1;
                        uint32_t blend = mode == 2 ? 1u : mode == 3 ? 6u : 0u;
                        stored.draw[format][mode] = world_graphics(vertex, vs_entry, pixel, ps_entry, layout, {}, {}, &formats[format], 1, depth, false, additive, false, blend);
                    }
                stored.pipeline = stored.draw[1][0];
            }
        }
        uint64_t id = guest.next_pipeline++;
        guest.pipelines.emplace(id, stored);
        spvReflectDestroyShaderModule(&vr); spvReflectDestroyShaderModule(&pr);
        return id;
    } catch (...) {
        spvReflectDestroyShaderModule(&vr); spvReflectDestroyShaderModule(&pr);
        if (layout) vkDestroyPipelineLayout(device, layout, nullptr);
        if (set) vkDestroyDescriptorSetLayout(device, set, nullptr);
        if (vertex) vkDestroyShaderModule(device, vertex, nullptr);
        if (pixel) vkDestroyShaderModule(device, pixel, nullptr);
        throw;
    }
}

void guest_create_image(uint64_t id, uint32_t width, uint32_t height, uint32_t depth, uint32_t format) {
    if (!id || recording) throw std::runtime_error("invalid guest image");
    uint32_t kind = format & 0xffffu, mips = std::max(1u, format >> 16);
    if (kind > 5) throw std::runtime_error("guest image format is outside the bus");
    width = std::max(1u, width); height = std::max(1u, height); depth = std::max(1u, depth);
    auto found = guest.images.find(id);
    if (found != guest.images.end()) { destroy_image(found->second.image); guest.images.erase(found); }
    GuestImage image{};
    image.width = width; image.height = height; image.depth = depth; image.mips = mips;
    if (kind == 4) {
        image.is_buffer = true; image.width = std::max(16u, width); image.bpp = 1; image.layers = 1;
        image.storage = make_buffer(image.width, VK_BUFFER_USAGE_STORAGE_BUFFER_BIT, true);
        guest.images.emplace(id, image); return;
    }
    image.layers = kind == 5 ? depth : 1;
    if (kind == 5) { image.depth = 1; depth = 1; }
    image.format = kind == 1 ? VK_FORMAT_R16G16B16A16_SFLOAT : kind == 2 ? VK_FORMAT_R32_SFLOAT : kind == 3 ? VK_FORMAT_R16G16_SFLOAT : VK_FORMAT_R8G8B8A8_UNORM;
    image.bpp = kind == 1 ? 8u : 4u;
    VkImageUsageFlags usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_STORAGE_BIT;
    if (depth == 1) usage |= VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    if (depth == 1)
        image.image = make_image(width, height, image.format, usage, VK_IMAGE_ASPECT_COLOR_BIT, mips, std::max(1u, image.layers), image.layers > 1);
    else {
        Image result{};
        VkImageCreateInfo info{ VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO }; info.imageType = VK_IMAGE_TYPE_3D; info.format = image.format;
        info.extent = { width, height, depth }; info.mipLevels = mips; info.arrayLayers = 1; info.samples = VK_SAMPLE_COUNT_1_BIT;
        info.tiling = VK_IMAGE_TILING_OPTIMAL; info.usage = usage;
        info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        VmaAllocationCreateInfo memory{}; memory.usage = VMA_MEMORY_USAGE_AUTO_PREFER_DEVICE;
        check(vmaCreateImage(allocator, &info, &memory, &result.handle, &result.memory, nullptr), "allocate guest volume");
        VkImageViewCreateInfo view{ VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO }; view.image = result.handle; view.viewType = VK_IMAGE_VIEW_TYPE_3D;
        view.format = info.format; view.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, mips, 0, 1 };
        try { check(vkCreateImageView(device, &view, nullptr, &result.view), "create guest volume view"); }
        catch (...) { vmaDestroyImage(allocator, result.handle, result.memory); throw; }
        image.image = result;
    }
    guest.images.emplace(id, image);
}

void guest_update_image(uint64_t id, const void* data, uint64_t bytes) {
    auto found = guest.images.find(id);
    if (found == guest.images.end() || !data) throw std::runtime_error("guest image update missing");
    auto& image = found->second;
    if (image.is_buffer) {
        if (bytes > image.storage.bytes) throw std::runtime_error("guest buffer update short");
        std::memcpy(image.storage.mapped, data, static_cast<size_t>(bytes));
        check(vmaFlushAllocation(allocator, image.storage.memory, 0, bytes), "flush guest buffer");
        return;
    }
    uint64_t expect = 0;
    for (uint32_t mip = 0; mip < image.mips; ++mip)
        expect += uint64_t(std::max(1u, image.width >> mip)) * std::max(1u, image.height >> mip) * std::max(1u, image.depth >> mip) * image.layers * image.bpp;
    if (bytes < expect) throw std::runtime_error("guest image update short");
    Buffer staging = make_buffer(expect, VK_BUFFER_USAGE_TRANSFER_SRC_BIT, true);
    std::memcpy(staging.mapped, data, static_cast<size_t>(expect));
    check(vmaFlushAllocation(allocator, staging.memory, 0, expect), "flush guest image");
    try {
        immediate([&](VkCommandBuffer cmd) {
            barrier(cmd, image.image.handle, image.layout, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT, 0, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, image.mips, image.layers);
            std::vector<VkBufferImageCopy> copies(image.mips);
            uint64_t offset = 0;
            for (uint32_t mip = 0; mip < image.mips; ++mip) {
                uint32_t mw = std::max(1u, image.width >> mip), mh = std::max(1u, image.height >> mip), md = std::max(1u, image.depth >> mip);
                copies[mip].bufferOffset = offset; copies[mip].imageSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, mip, 0, image.layers };
                copies[mip].imageExtent = { mw, mh, image.depth == 1 ? 1u : md };
                offset += uint64_t(mw) * mh * md * image.layers * image.bpp;
            }
            vkCmdCopyBufferToImage(cmd, staging.handle, image.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, uint32_t(copies.size()), copies.data());
            barrier(cmd, image.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, image.mips, image.layers);
        });
        image.layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        if(batching_uploads){upload_slots[upload_index].pending.push_back(staging);staging={};}
    } catch (...) { destroy_buffer(staging); throw; }
    destroy_buffer(staging);
}

void guest_submit(const SevkGuestPass* passes, uint32_t count) {
    if (count && !passes) throw std::runtime_error("null guest passes");
    if (count > 256) throw std::runtime_error("too many guest passes");
    for (uint32_t i = 0; i < count; ++i) {
        if (passes[i].size != sizeof(SevkGuestPass) || passes[i].abi_version != SEVK_ABI_VERSION) throw std::runtime_error("guest pass ABI mismatch");
        if (passes[i].mark < 1 || passes[i].mark > 6) throw std::runtime_error("guest mark out of range");
        GuestPass pass{};
        pass.mark = passes[i].mark; pass.compose = passes[i].compose; pass.width = passes[i].width; pass.height = passes[i].height;
        pass.pipeline = passes[i].pipeline; pass.storage = passes[i].storage; pass.uniform_bytes = std::min(passes[i].uniform_bytes, 256u); pass.flags = passes[i].reserved;
        std::memcpy(pass.uniforms, passes[i].uniforms, pass.uniform_bytes);
        std::memcpy(pass.sampled, passes[i].sampled, sizeof(pass.sampled));
        guest.passes.push_back(pass);
    }
}

static constexpr uint64_t kGuestHdr = 0xFFFF000000000001ull;
static constexpr uint64_t kGuestDepth = 0xFFFF000000000002ull;
static constexpr uint64_t kGuestG0 = 0xFFFF000000000003ull;
static constexpr uint64_t kGuestG1 = 0xFFFF000000000004ull;
static constexpr uint64_t kGuestG2 = 0xFFFF000000000005ull;
static constexpr uint64_t kGuestUpscale = 0xFFFF000000000006ull;
static constexpr uint64_t kGuestDisplay = 0xFFFF000000000007ull;
static constexpr uint64_t kGuestMotion = 0xFFFF000000000008ull;
static constexpr uint64_t kGuestAttach0 = 0xFFFF000000000009ull;
// Anomaly catalog images. CameraVelocity writes RG16F convention-15 motion.
// volumeReactive is the shared-volume reject mask when a producer publishes it.
static constexpr uint64_t kGuestCatalogVelocity = 0xFFFF00000000000Aull;
static constexpr uint64_t kGuestCatalogVolumeReactive = 0xFFFF00000000000Bull;
static constexpr uint64_t kGuestCatalogReactiveMask = 0xFFFF00000000000Cull;

void guest_refresh_frame() {
    uint64_t stamp = stats.submitted_frames + (recording ? 1 : 0);
    if (guest.frame_stamp == stamp && guest.frame.size == sizeof(SevkGuestFrame)) return;
    float current[16]{};
    if (world.current().projection.size() >= 16) std::memcpy(current, submitted_frame.view_projection, 64);
    GuestExtras extras{};
    float width = float(std::max(1u, extent.width)), height = float(std::max(1u, extent.height));
    extras.render[0] = extras.scene[0] = width; extras.render[1] = extras.scene[1] = height;
    extras.inv_render[0] = extras.inv_scene[0] = 1.f / width; extras.inv_render[1] = extras.inv_scene[1] = 1.f / height;
    extras.history_valid = guest.have_prev ? 1u : 0u; extras.has_velocity = extras.history_valid;
    extras.frame_index = uint32_t(stats.submitted_frames); extras.safety = 1.f;
    std::memcpy(extras.unjittered, current, 64);
    std::memcpy(extras.prev, guest.have_prev ? guest.prev_vp : current, 64);
    extras.camera[0] = extras.camera[5] = extras.camera[10] = 1.f;
    extras.camera[3] = submitted_frame.camera_position[0]; extras.camera[7] = submitted_frame.camera_position[1]; extras.camera[11] = submitted_frame.camera_position[2];
    if (world.current().projection.size() >= 16) { extras.proj_scale[0] = world.current().projection[0]; extras.proj_scale[1] = world.current().projection[5]; }
    std::memcpy(extras.sun_color, submitted_frame.sunlight_color, 12);
    extras.sun_diffuse = submitted_frame.sunlight_color[3] > 0.f ? submitted_frame.sunlight_color[3] : 1.f;
    std::memcpy(extras.sun_toward, submitted_frame.sunlight_direction, 12);
    std::memcpy(extras.sky_ambient, submitted_frame.ambient_color, 12);
    extras.sky_luma = extras.sky_ambient[0] * 0.2126f + extras.sky_ambient[1] * 0.7152f + extras.sky_ambient[2] * 0.0722f;
    SevkGuestFrame frame{}; frame.size = sizeof(frame); frame.abi_version = SEVK_ABI_VERSION;
    frame.width = extent.width; frame.height = extent.height; frame.frame_index = extras.frame_index; frame.history_valid = extras.history_valid;
    frame.jitter_x = extras.jitter[0]; frame.jitter_y = extras.jitter[1]; frame.safety_scale = extras.safety; frame.sun_diffuse = extras.sun_diffuse;
    frame.sky_luma = extras.sky_luma; frame.proj_scale_x = extras.proj_scale[0]; frame.proj_scale_y = extras.proj_scale[1];
    std::memcpy(frame.sun_color, extras.sun_color, 12); std::memcpy(frame.sun_toward, extras.sun_toward, 12); std::memcpy(frame.sky_ambient, extras.sky_ambient, 12);
    frame.camera_delta[0] = submitted_frame.camera_position[0] - guest.prev_camera[0];
    frame.camera_delta[1] = submitted_frame.camera_position[1] - guest.prev_camera[1];
    frame.camera_delta[2] = submitted_frame.camera_position[2] - guest.prev_camera[2];
    std::memcpy(frame.view_proj, extras.unjittered, 64); std::memcpy(frame.prev_view_proj, extras.prev, 64);
    frame.camera_to_world[0] = frame.camera_to_world[5] = frame.camera_to_world[10] = frame.camera_to_world[15] = 1.f;
    frame.camera_to_world[12] = submitted_frame.camera_position[0]; frame.camera_to_world[13] = submitted_frame.camera_position[1]; frame.camera_to_world[14] = submitted_frame.camera_position[2];
    frame.far_plane = view_far > 1.f ? view_far : 1000000.f;
    frame.output_width = resolution_stats.output_width ? float(resolution_stats.output_width) : float(std::max(1u, extent.width));
    frame.output_height = resolution_stats.output_height ? float(resolution_stats.output_height) : float(std::max(1u, extent.height));
    frame.service_flags = display_flags | (hdr_active ? 8u : 0u);
    guest.frame = frame;
    std::memcpy(guest.prev_vp, current, 64); std::memcpy(guest.prev_camera, submitted_frame.camera_position, 12); guest.have_prev = true; guest.frame_stamp = stamp;
    guest.frame.pad0 = std::bit_cast<float>(extras.frame_index);
}

int guest_pool_index() { return recording ? int(slot_index % 2) : 2; }

VkDescriptorPool guest_pool(int index) {
    uint64_t stamp = recording ? stats.submitted_frames + 1 : guest.replay_count;
    if (!guest.pools[index]) {
        std::array<VkDescriptorPoolSize, 4> sizes{{ { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 2048 }, { VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 8192 }, { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 256 }, { VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 256 } }};
        VkDescriptorPoolCreateInfo info{ VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO };
        info.maxSets = 512; info.poolSizeCount = 4; info.pPoolSizes = sizes.data();
        check(vkCreateDescriptorPool(device, &info, nullptr, &guest.pools[index]), "create guest descriptor pool");
    }
    if (guest.pool_stamp[index] != stamp) {
        check(vkResetDescriptorPool(device, guest.pools[index], 0), "reset guest descriptor pool");
        guest.bumps[index].used = 0; guest.pool_stamp[index] = stamp;
    }
    return guest.pools[index];
}

uint32_t guest_uniform(int pool, const void* data, uint32_t bytes) {
    auto& bump = guest.bumps[pool];
    if (!bump.buffer.handle) bump.buffer = make_buffer(4u * 1024u * 1024u, VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT, true);
    uint32_t align = uint32_t(std::max<VkDeviceSize>(1, world.alignment)), start = (bump.used + align - 1u) & ~(align - 1u);
    if (start + bytes > 4u * 1024u * 1024u) return ~0u;
    std::memcpy(static_cast<uint8_t*>(bump.buffer.mapped) + start, data, bytes);
    check(vmaFlushAllocation(allocator, bump.buffer.memory, start, bytes), "flush guest uniform");
    bump.used = start + bytes; return start;
}

VkImageView guest_dummy() {
    if (guest.dummy.view) return guest.dummy.view;
    guest.dummy = make_image(1, 1, VK_FORMAT_R8G8B8A8_UNORM, VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT, VK_IMAGE_ASPECT_COLOR_BIT);
    immediate([&](VkCommandBuffer cmd) {
        barrier(cmd, guest.dummy.handle, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT, 0, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    });
    return guest.dummy.view;
}

bool guest_catalog_view(uint64_t id, VkImageView& view, VkImage& image, uint32_t& width, uint32_t& height) {
    auto found = guest.images.find(id);
    if (found == guest.images.end() || found->second.is_buffer || found->second.depth != 1 || !found->second.image.view)
        return false;
    view = found->second.image.view;
    image = found->second.image.handle;
    width = found->second.width;
    height = found->second.height;
    return width > 0 && height > 0;
}

bool guest_anomaly_velocity(VkImageView& view, VkImage& image, uint32_t& width, uint32_t& height) {
    if (guest.frame.size != sizeof(SevkGuestFrame) || guest.frame.history_valid == 0)
        return false;
    return guest_catalog_view(kGuestCatalogVelocity, view, image, width, height);
}

VkImageView guest_source(uint64_t id, bool* hdr) {
    if (hdr) *hdr = false;
    if (id == kGuestHdr) { if (hdr) *hdr = true; return world.hdr.view ? world.hdr.view : guest_dummy(); }
    if (id == kGuestDepth) return world.gbuffer[3].view ? world.gbuffer[3].view : guest_dummy();
    if (id == kGuestG0) return world.gbuffer[0].view ? world.gbuffer[0].view : guest_dummy();
    if (id == kGuestG1) return world.gbuffer[1].view ? world.gbuffer[1].view : guest_dummy();
    if (id == kGuestG2) return world.gbuffer[2].view ? world.gbuffer[2].view : guest_dummy();
    if (id == kGuestUpscale) {
        if (display_state.upscale_ready && display_state.upscale_view) return display_state.upscale_view;
        if (post.after_tonemap && post.after_tonemap->image.view) return post.after_tonemap->image.view;
        return post.output && post.output->image.view ? post.output->image.view : (world.hdr.view ? world.hdr.view : guest_dummy());
    }
    if (id == kGuestMotion) return world.motion.view ? world.motion.view : guest_dummy();
    if (id >= kGuestAttach0 && id < kGuestAttach0 + 3) { auto& target = world.attach[id - kGuestAttach0]; return target.image.view ? target.image.view : guest_dummy(); }
    if (id == kGuestDisplay) {
        if (swapchain && image_index < swap_views.size()) return swap_views[image_index];
        return offscreen.view ? offscreen.view : guest_dummy();
    }
    auto found = guest.images.find(id);
    return found == guest.images.end() ? guest_dummy() : found->second.image.view;
}

void guest_copy_hdr(VkCommandBuffer cmd, VkImageLayout layout) {
    if (!world.hdr.view || !extent.width || !extent.height) return;
    if (!guest.scratch.view || guest.scratch_w != extent.width || guest.scratch_h != extent.height) {
        if (guest.scratch.view) destroy_image(guest.scratch);
        guest.scratch = make_image(extent.width, extent.height, VK_FORMAT_R16G16B16A16_SFLOAT, VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT, VK_IMAGE_ASPECT_COLOR_BIT);
        guest.scratch_w = extent.width; guest.scratch_h = extent.height;
    }
    VkPipelineStageFlags2 source = layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT : VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
    VkAccessFlags2 access = layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    barrier(cmd, world.hdr.handle, layout, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        source, access, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT);
    barrier(cmd, guest.scratch.handle, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT, 0, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT);
    VkImageCopy copy{}; copy.srcSubresource = copy.dstSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 }; copy.extent = { extent.width, extent.height, 1 };
    vkCmdCopyImage(cmd, world.hdr.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, guest.scratch.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
    barrier(cmd, guest.scratch.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    barrier(cmd, world.hdr.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, layout, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT, source, access);
}

void guest_copy_source(VkCommandBuffer cmd, VkImage image, VkImageLayout layout, uint32_t width, uint32_t height) {
    if (!cmd || !image || !width || !height) return;
    bool fresh = !guest.scratch.view || guest.scratch_w != width || guest.scratch_h != height;
    if (fresh) {
        if (guest.scratch.view) destroy_image(guest.scratch);
        guest.scratch = make_image(width, height, VK_FORMAT_R16G16B16A16_SFLOAT, VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT, VK_IMAGE_ASPECT_COLOR_BIT);
        guest.scratch_w = width; guest.scratch_h = height;
    }
    VkPipelineStageFlags2 source = layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT : (VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT);
    VkAccessFlags2 access = layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    barrier(cmd, image, layout, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT, source, access, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT);
    barrier(cmd, guest.scratch.handle, fresh ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        fresh ? VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT : VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, fresh ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT);
    VkImageBlit blit{};
    blit.srcSubresource = blit.dstSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 };
    blit.srcOffsets[1] = blit.dstOffsets[1] = { int(width), int(height), 1 };
    vkCmdBlitImage(cmd, image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, guest.scratch.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &blit, VK_FILTER_NEAREST);
    barrier(cmd, guest.scratch.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    barrier(cmd, image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, layout, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT, source, access);
}

float guest_unpack(uint64_t packed, bool high) {
    uint32_t bits = high ? uint32_t(packed >> 32) : uint32_t(packed);
    float value = 0;
    std::memcpy(&value, &bits, 4);
    return value;
}

void guest_fill_owned(const GuestPass& pass, GuestPipeline& pipeline, uint8_t* owned) {
    if ((pass.flags & 4) && pipeline.owned_bytes >= 32) {
        float origin[4]{ guest_unpack(pass.sampled[8], false), guest_unpack(pass.sampled[8], true), guest_unpack(pass.sampled[9], false), 0 };
        float motion[4]{ guest_unpack(pass.sampled[9], true), guest_unpack(pass.sampled[10], false), guest_unpack(pass.sampled[10], true), 0 };
        std::memcpy(owned, origin, 16); std::memcpy(owned + 16, motion, 16);
        return;
    }
    if ((pass.flags & 1) && pipeline.owned_bytes >= 144) {
        float light[12]{};
        light[0] = guest.frame.sun_toward[0]; light[1] = guest.frame.sun_toward[1]; light[2] = guest.frame.sun_toward[2]; light[3] = guest.frame.sun_diffuse;
        std::memcpy(light + 4, guest.frame.sun_color, 12); light[7] = 1.f;
        std::memcpy(light + 8, guest.frame.sun_color, 12); light[11] = 1.f;
        std::memcpy(owned, light, 48);
        float time = float(guest.frame.frame_index);
        std::memcpy(owned + 64, &time, 4);
        float view[16]{};
        view[0] = view[5] = view[10] = view[15] = 1.f;
        if (world.current().inverse.size() >= 16) std::memcpy(view, world.current().inverse.data(), 64);
        std::memcpy(owned + 80, view, 64);
        return;
    }
    if (pipeline.owned_bytes >= 16 && world.current().projection.size() >= 16) {
        float proj33 = world.current().projection[10], proj43 = world.current().projection[14];
        std::memcpy(owned, &proj33, 4); std::memcpy(owned + 4, &proj43, 4);
    }
    if (pipeline.owned_bytes >= 208) {
        std::memcpy(owned, world.current().inverse.data(), 64);
        std::memcpy(owned + 64, guest.frame.view_proj, 64);
        std::memcpy(owned + 128, guest.frame.prev_view_proj, 64);
    }
}

void guest_fill_celestial_view(const GuestPass& pass, uint8_t* bytes) {
    float rows[12]{ 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
    if (world.current().inverse.size() >= 16) {
        const float* m = world.current().inverse.data();
        rows[0] = m[0]; rows[1] = m[4]; rows[2] = m[8];
        rows[4] = m[1]; rows[5] = m[5]; rows[6] = m[9];
        rows[8] = m[2]; rows[9] = m[6]; rows[10] = m[10];
    }
    std::memcpy(bytes, rows, 48);
    float view[4]{ float(std::max(1u, pass.width)), float(std::max(1u, pass.height)), guest.frame.proj_scale_x, guest.frame.proj_scale_y };
    std::memcpy(bytes + 48, view, 16);
    float count = 0;
    auto data = guest.images.find(pass.sampled[1]);
    if (data != guest.images.end() && data->second.is_buffer && data->second.width >= 16) count = float(data->second.width / 16u);
    float info[4]{ count, 0, 0, 0 };
    std::memcpy(bytes + 64, info, 16);
}

bool guest_draw_bus(VkCommandBuffer cmd, const GuestPass& pass, GuestPipeline& pipeline, uint32_t mark) {
    int pool = guest_pool_index();
    VkDescriptorSetAllocateInfo allocate{ VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO };
    allocate.descriptorPool = guest_pool(pool); allocate.descriptorSetCount = 1; allocate.pSetLayouts = &pipeline.set;
    VkDescriptorSet set{};
    if (vkAllocateDescriptorSets(device, &allocate, &set) != VK_SUCCESS) return false;
    GuestExtras extras{};
    guest_refresh_frame();
    float width = float(std::max(1u, pass.width ? pass.width : extent.width)), height = float(std::max(1u, pass.height ? pass.height : extent.height));
    float scene_w = float(std::max(1u, guest.frame.width ? guest.frame.width : extent.width));
    float scene_h = float(std::max(1u, guest.frame.height ? guest.frame.height : extent.height));
    extras.render[0] = width; extras.render[1] = height; extras.scene[0] = scene_w; extras.scene[1] = scene_h;
    extras.inv_render[0] = 1.f / width; extras.inv_render[1] = 1.f / height; extras.inv_scene[0] = 1.f / scene_w; extras.inv_scene[1] = 1.f / scene_h;
    extras.camera_pad[0] = guest.frame.far_plane > 1.f ? guest.frame.far_plane : 1000000.f;
    extras.history_valid = guest.frame.history_valid; extras.has_velocity = extras.history_valid; extras.frame_index = guest.frame.frame_index; extras.safety = guest.frame.safety_scale > 0 ? guest.frame.safety_scale : 1.f;
    std::memcpy(extras.unjittered, guest.frame.view_proj, 64); std::memcpy(extras.prev, guest.frame.prev_view_proj, 64);
    extras.camera[0] = extras.camera[5] = extras.camera[10] = 1.f;
    extras.camera[3] = guest.frame.camera_to_world[12]; extras.camera[7] = guest.frame.camera_to_world[13]; extras.camera[11] = guest.frame.camera_to_world[14];
    extras.proj_scale[0] = guest.frame.proj_scale_x; extras.proj_scale[1] = guest.frame.proj_scale_y;
    std::memcpy(extras.sun_color, guest.frame.sun_color, 12); extras.sun_diffuse = guest.frame.sun_diffuse;
    std::memcpy(extras.sun_toward, guest.frame.sun_toward, 12); extras.sky_luma = guest.frame.sky_luma;
    std::memcpy(extras.sky_ambient, guest.frame.sky_ambient, 12);
    uint8_t owned[256]{};
    guest_fill_owned(pass, pipeline, owned);
    if ((pass.flags & 1) == 0 && pipeline.owned_bytes >= 208) {
        std::memcpy(owned + 192, extras.render, 8); std::memcpy(owned + 200, extras.inv_render, 8);
    }
    uint8_t celestial_view[96]{};
    bool celestial = (pass.flags & 1) && pipeline.extras_bytes > 0 && pipeline.extras_bytes <= 96;
    if (celestial) guest_fill_celestial_view(pass, celestial_view);
    bool to_hdr = pass.storage == kGuestHdr;
    bool sample_hdr = to_hdr;
    for (auto binding : pipeline.bindings)
        if (binding.second == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE && binding.first >= 128 && binding.first <= 141 && pass.sampled[binding.first - 128] == kGuestHdr)
            sample_hdr = true;
    sample_hdr = sample_hdr && to_hdr;
    VkImageLayout hdr_layout = mark >= 4 ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    if (sample_hdr && world.hdr.view) guest_copy_hdr(cmd, hdr_layout);
    bool draw_tone = to_hdr && mark == 5 && post.after_tonemap && post.after_tonemap->image.view;
    bool draw_dlss = to_hdr && mark >= 6 && display_state.upscale_ready && display_state.upscale_view;
    bool sample_upscale = false;
    if (draw_tone || draw_dlss)
        for (auto binding : pipeline.bindings)
            if (binding.second == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE && binding.first >= 128 && binding.first <= 141 && pass.sampled[binding.first - 128] == kGuestUpscale)
                sample_upscale = true;
    if (sample_upscale && draw_dlss) guest_copy_source(cmd, display_state.upscale_handle, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, display_state.upscale_w, display_state.upscale_h);
    else if (sample_upscale) guest_copy_source(cmd, post.after_tonemap->image.handle, post.after_tonemap->layout ? post.after_tonemap->layout : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, post.after_tonemap->width, post.after_tonemap->height);
    std::vector<VkDescriptorBufferInfo> buffers;
    std::vector<VkDescriptorImageInfo> images;
    struct GuestWrite { uint32_t binding; VkDescriptorType type; int buffer{-1}, image{-1}; };
    std::vector<GuestWrite> planned;
    for (auto binding : pipeline.bindings) {
        GuestWrite planned_write{ binding.first, binding.second, -1, -1 };
        if (binding.second == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER) {
            const void* data = binding.first == 7 ? (const void*)pass.uniforms : binding.first == 0 ? (const void*)owned : celestial ? (const void*)celestial_view : (const void*)&extras;
            uint32_t bytes = binding.first == 7 ? (pipeline.uniform_bytes ? pipeline.uniform_bytes : 256u) : binding.first == 0 ? std::max(16u, pipeline.owned_bytes) : (pipeline.extras_bytes ? pipeline.extras_bytes : 320u);
            uint32_t offset = guest_uniform(pool, data, bytes);
            if (offset == ~0u) return false;
            planned_write.buffer = int(buffers.size());
            buffers.push_back({ guest.bumps[pool].buffer.handle, offset, bytes });
        } else if (binding.second == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE) {
            uint32_t slot = binding.first - 128;
            uint64_t id = slot < 14 ? pass.sampled[slot] : 0;
            bool hdr = false; VkImageView view = id ? guest_source(id, &hdr) : guest_dummy();
            if (hdr && sample_hdr) view = guest.scratch.view ? guest.scratch.view : view;
            if (sample_upscale && id == kGuestUpscale && guest.scratch.view) view = guest.scratch.view;
            VkImageLayout layout = id == kGuestDepth ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            planned_write.image = int(images.size());
            images.push_back({ nullptr, view, layout });
        } else if (binding.second == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER) {
            uint32_t slot = binding.first >= 128 ? binding.first - 128 : 0;
            uint64_t id = slot < 14 ? pass.sampled[slot] : 0;
            auto image = guest.images.find(id);
            if (image == guest.images.end() || !image->second.is_buffer) return false;
            planned_write.buffer = int(buffers.size());
            buffers.push_back({ image->second.storage.handle, 0, image->second.storage.bytes });
        } else if (binding.second == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE) {
            auto image = guest.images.find(pass.storage);
            if (image == guest.images.end()) return false;
            planned_write.image = int(images.size());
            images.push_back({ nullptr, image->second.image.view, VK_IMAGE_LAYOUT_GENERAL });
        } else continue;
        planned.push_back(planned_write);
    }
    std::vector<VkWriteDescriptorSet> writes(planned.size());
    for (size_t i = 0; i < planned.size(); ++i) {
        writes[i] = { VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET }; writes[i].dstSet = set; writes[i].dstBinding = planned[i].binding; writes[i].descriptorCount = 1; writes[i].descriptorType = planned[i].type;
        if (planned[i].buffer >= 0) writes[i].pBufferInfo = &buffers[planned[i].buffer];
        if (planned[i].image >= 0) writes[i].pImageInfo = &images[planned[i].image];
    }
    if (!writes.empty()) vkUpdateDescriptorSets(device, uint32_t(writes.size()), writes.data(), 0, nullptr);
    if (pipeline.compute) {
        auto image = guest.images.find(pass.storage);
        if (image != guest.images.end())
            barrier(cmd, image->second.image.handle, image->second.layout, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT, image->second.mips);
        vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipeline.pipeline);
        vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipeline.layout, 0, 1, &set, 0, nullptr);
        vkCmdDispatch(cmd, std::max(1u, (std::max(1u, pass.width) + 7) / 8), std::max(1u, (std::max(1u, pass.height) + 7) / 8), 1);
        if (image != guest.images.end()) {
            barrier(cmd, image->second.image.handle, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, image->second.mips);
            image->second.layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        }
        return true;
    }
    bool depth = (pass.flags & 1) != 0;
    int mode = depth ? 4 : pass.compose == 1 ? 2 : pass.compose == 2 ? 3 : (pass.compose == 0 || pass.compose == 6) ? 1 : 0;
    GuestImage* target = nullptr;
    PostImage* tracked = nullptr;
    VkImageView color = world.hdr.view; uint32_t tw = extent.width, th = extent.height; bool hdr_target = true;
    if (!to_hdr) {
        auto image = guest.images.find(pass.storage);
        if (image == guest.images.end() || image->second.depth != 1) return false;
        target = &image->second; color = target->image.view; tw = target->width; th = target->height; hdr_target = false;
    } else if (draw_tone) {
        tracked = post.after_tonemap; color = tracked->image.view; tw = tracked->width; th = tracked->height; hdr_target = false;
    } else if (draw_dlss) {
        color = display_state.upscale_view; tw = display_state.upscale_w; th = display_state.upscale_h; hdr_target = false;
    }
    if (!color) return false;
    int format = draw_tone ? 0 : draw_dlss ? 1 : hdr_target || (target && target->format == VK_FORMAT_R16G16B16A16_SFLOAT) ? 1 : target && target->format == VK_FORMAT_R32_SFLOAT ? 2 : target && target->format == VK_FORMAT_R16G16_SFLOAT ? 3 : 0;
    VkPipeline pipe = pipeline.draw[format][mode];
    if (!pipe) return false;
    VkImageLayout incoming = draw_tone ? (tracked->layout ? tracked->layout : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL) : draw_dlss ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : hdr_target ? (mark >= 4 ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL) : (target->layout == VK_IMAGE_LAYOUT_UNDEFINED ? VK_IMAGE_LAYOUT_UNDEFINED : target->layout);
    VkImage handle = draw_tone ? tracked->image.handle : draw_dlss ? display_state.upscale_handle : hdr_target ? world.hdr.handle : target->image.handle;
    if (incoming != VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)
        barrier(cmd, handle, incoming, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, incoming == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    bool restore_depth = false;
    VkRenderingAttachmentInfo depth_info{ VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO };
    if (depth && world.gbuffer[3].view) {
        barrier(cmd, world.gbuffer[3].handle, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_DEPTH_BIT,
            VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT, VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT);
        depth_info.imageView = world.gbuffer[3].view; depth_info.imageLayout = VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL;
        depth_info.loadOp = VK_ATTACHMENT_LOAD_OP_LOAD; depth_info.storeOp = VK_ATTACHMENT_STORE_OP_DONT_CARE; restore_depth = true;
    }
    VkRenderingAttachmentInfo attachment{ VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO };
    attachment.imageView = color; attachment.imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    attachment.loadOp = hdr_target || draw_tone || draw_dlss || pass.compose == 3 ? VK_ATTACHMENT_LOAD_OP_LOAD : VK_ATTACHMENT_LOAD_OP_CLEAR;
    attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE; attachment.clearValue.color = { { 0, 0, 0, 0 } };
    VkRenderingInfo rendering{ VK_STRUCTURE_TYPE_RENDERING_INFO }; rendering.renderArea = { { 0, 0 }, { tw, th } };
    rendering.layerCount = 1; rendering.colorAttachmentCount = 1; rendering.pColorAttachments = &attachment;
    if (restore_depth) rendering.pDepthAttachment = &depth_info;
    vkCmdBeginRendering(cmd, &rendering);
    VkViewport viewport{ 0, 0, float(tw), float(th), 0, 1 }; VkRect2D scissor{ { 0, 0 }, { tw, th } };
    vkCmdSetViewport(cmd, 0, 1, &viewport); vkCmdSetScissor(cmd, 0, 1, &scissor);
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, pipe);
    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline.layout, 0, 1, &set, 0, nullptr);
    vkCmdDraw(cmd, 3, 1, 0, 0);
    vkCmdEndRendering(cmd);
    VkImageLayout outgoing = hdr_target && mark >= 4 ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : hdr_target ? VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    if (outgoing != VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)
        barrier(cmd, handle, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, outgoing, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    if (!hdr_target && target) target->layout = outgoing;
    if (tracked) tracked->layout = outgoing;
    if (restore_depth)
        barrier(cmd, world.gbuffer[3].handle, VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_DEPTH_BIT,
            VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT, VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    return true;
}

void guest_replay(VkCommandBuffer cmd, uint32_t mark) {
    ++guest.replay_count;
    guest_refresh_frame();
    if (guest.passes.empty()) return;
    for (const auto& pass : guest.passes) {
        if (pass.mark != mark || !pass.pipeline) continue;
        auto pipeline = guest.pipelines.find(pass.pipeline);
        if (pipeline == guest.pipelines.end()) continue;
        if (pipeline->second.bus) { guest_draw_bus(cmd, pass, pipeline->second, mark); continue; }
        auto image = guest.images.find(pass.storage);
        if (image == guest.images.end()) continue;
        if (pipeline->second.descriptors != 0 || !pipeline->second.pipeline || image->second.depth != 1) continue;
        auto& target = image->second;
        barrier(cmd, target.image.handle, target.layout == VK_IMAGE_LAYOUT_UNDEFINED ? VK_IMAGE_LAYOUT_UNDEFINED : target.layout,
            VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, target.layout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
        VkRenderingAttachmentInfo color{ VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO };
        color.imageView = target.image.view; color.imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        color.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR; color.storeOp = VK_ATTACHMENT_STORE_OP_STORE; color.clearValue.color = { { 0, 0, 0, 0 } };
        VkRenderingInfo rendering{ VK_STRUCTURE_TYPE_RENDERING_INFO }; rendering.renderArea = { { 0, 0 }, { target.width, target.height } };
        rendering.layerCount = 1; rendering.colorAttachmentCount = 1; rendering.pColorAttachments = &color;
        vkCmdBeginRendering(cmd, &rendering);
        VkViewport viewport{ 0, 0, float(target.width), float(target.height), 0, 1 }; VkRect2D scissor{ { 0, 0 }, { target.width, target.height } };
        vkCmdSetViewport(cmd, 0, 1, &viewport); vkCmdSetScissor(cmd, 0, 1, &scissor);
        vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline->second.pipeline);
        vkCmdDraw(cmd, 3, 1, 0, 0);
        vkCmdEndRendering(cmd);
        barrier(cmd, target.image.handle, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        target.layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    }
}

void guest_readback(uint64_t id, void* rgba, uint64_t bytes) {
    auto found = guest.images.find(id);
    if (found == guest.images.end() || !rgba || found->second.depth != 1) throw std::runtime_error("guest readback missing");
    auto& image = found->second;
    uint64_t expect = uint64_t(image.width) * image.height * 4ull;
    if (bytes < expect) throw std::runtime_error("guest readback short");
    Buffer staging = make_buffer(expect, VK_BUFFER_USAGE_TRANSFER_DST_BIT, true, true);
    try {
        immediate([&](VkCommandBuffer cmd) {
            barrier(cmd, image.image.handle, image.layout == VK_IMAGE_LAYOUT_UNDEFINED ? VK_IMAGE_LAYOUT_UNDEFINED : image.layout,
                VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, image.layout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT);
            VkBufferImageCopy copy{}; copy.imageSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 }; copy.imageExtent = { image.width, image.height, 1 };
            vkCmdCopyImageToBuffer(cmd, image.image.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, staging.handle, 1, &copy);
            barrier(cmd, image.image.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        });
        image.layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        check(vmaInvalidateAllocation(allocator, staging.memory, 0, expect), "invalidate guest readback");
        std::memcpy(rgba, staging.mapped, static_cast<size_t>(expect));
    } catch (...) { destroy_buffer(staging); throw; }
    destroy_buffer(staging);
}
