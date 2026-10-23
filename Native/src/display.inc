struct DisplayState {
    VkDescriptorSetLayout layout{}, warp_layout{};
    VkPipelineLayout pipeline_layout{}, warp_pipeline_layout{};
    VkPipeline upscale{}, interpolate{};
    Image color[2]{}, interpolant{}, zero{}, depth_copy[2]{};
    Buffer constants[2]{};
    VkSemaphore acquire{}, present{};
    uint32_t width{}, height{}, index{}, depth_w{}, depth_h{}, depth_index{};
    uint8_t visited[3]{}, depth_visited[2]{};
    bool ready{}, interpolant_ready{}, zero_ready{}, depth_ready{}, upscale_ready{};
    VkImageView compose{}, upscale_view{};
    VkImage upscale_handle{};
    uint32_t upscale_w{}, upscale_h{};
};
DisplayState display_state;

void display_write_storage(VkDescriptorSet set, uint32_t binding, VkImageView view) {
    VkDescriptorImageInfo info{VK_NULL_HANDLE, view, VK_IMAGE_LAYOUT_GENERAL};
    VkWriteDescriptorSet write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};
    write.dstSet = set; write.dstBinding = binding; write.descriptorCount = 1;
    write.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE; write.pImageInfo = &info;
    vkUpdateDescriptorSets(device, 1, &write, 0, nullptr);
}

void cleanup_display() noexcept {
    if (display_state.upscale) vkDestroyPipeline(device, display_state.upscale, nullptr);
    if (display_state.interpolate) vkDestroyPipeline(device, display_state.interpolate, nullptr);
    if (display_state.warp_pipeline_layout) vkDestroyPipelineLayout(device, display_state.warp_pipeline_layout, nullptr);
    if (display_state.warp_layout) vkDestroyDescriptorSetLayout(device, display_state.warp_layout, nullptr);
    if (display_state.pipeline_layout) vkDestroyPipelineLayout(device, display_state.pipeline_layout, nullptr);
    if (display_state.layout) vkDestroyDescriptorSetLayout(device, display_state.layout, nullptr);
    if (display_state.acquire) vkDestroySemaphore(device, display_state.acquire, nullptr);
    if (display_state.present) vkDestroySemaphore(device, display_state.present, nullptr);
    for (auto& image : display_state.color) destroy_image(image);
    for (auto& image : display_state.depth_copy) destroy_image(image);
    destroy_image(display_state.interpolant); destroy_image(display_state.zero);
    destroy_buffer(display_state.constants[0]); destroy_buffer(display_state.constants[1]);
    display_state = {};
}

VkPipeline display_load(const char* name, VkPipelineLayout layout, bool warp) {
    auto path = std::filesystem::path(shader_directory) / name;
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file || file.tellg() < 20 || file.tellg() > 1024 * 1024 || file.tellg() % 4) throw std::runtime_error(std::string(name) + " missing");
    std::vector<uint32_t> code(static_cast<size_t>(file.tellg()) / 4);
    file.seekg(0); file.read(reinterpret_cast<char*>(code.data()), code.size() * 4);
    SpvReflectShaderModule reflected{};
    auto module = world_shader(code.data(), code.size() * 4, VK_SHADER_STAGE_COMPUTE_BIT, reflected);
    bool valid = reflected.descriptor_binding_count == (warp ? 9u : 6u) && !reflected.push_constant_block_count;
    for (uint32_t i = 0; i < reflected.descriptor_binding_count; ++i) {
        auto& binding = reflected.descriptor_bindings[i];
        valid &= binding.set == 0 && binding.count == 1;
        bool sampled = binding.descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE
            && (binding.binding == 1 || binding.binding == 2 || binding.binding == 3 || (warp && binding.binding >= 6 && binding.binding <= 8));
        valid &= (binding.binding == 0 && binding.descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER && binding.block.size == 32)
            || sampled
            || (binding.binding == 4 && binding.descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER)
            || (binding.binding == 5 && binding.descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_STORAGE_IMAGE);
    }
    spvReflectDestroyShaderModule(&reflected);
    if (!valid) { vkDestroyShaderModule(device, module, nullptr); throw std::runtime_error(std::string(name) + " interface changed"); }
    VkComputePipelineCreateInfo info{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};
    info.stage = {VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO, nullptr, 0, VK_SHADER_STAGE_COMPUTE_BIT, module, "main", nullptr};
    info.layout = layout;
    VkPipeline pipeline{};
    auto result = vkCreateComputePipelines(device, pipeline_cache, 1, &info, nullptr, &pipeline);
    vkDestroyShaderModule(device, module, nullptr);
    check(result, "display compute pipeline");
    return pipeline;
}

void init_display() {
    if (display_state.upscale) return;
    VkDescriptorSetLayoutBinding bindings[]{
        {0, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {1, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {2, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {3, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {4, VK_DESCRIPTOR_TYPE_SAMPLER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {5, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
    };
    VkDescriptorSetLayoutCreateInfo layout{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};
    layout.bindingCount = 6; layout.pBindings = bindings;
    check(vkCreateDescriptorSetLayout(device, &layout, nullptr, &display_state.layout), "display descriptors");
    display_state.pipeline_layout = world_pipeline_layout(display_state.layout);
    display_state.upscale = display_load("display_upscale_cs.spv", display_state.pipeline_layout, false);
    VkDescriptorSetLayoutBinding warp_bindings[]{
        {0, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {1, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {2, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {3, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {4, VK_DESCRIPTOR_TYPE_SAMPLER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {5, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {6, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {7, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {8, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
    };
    VkDescriptorSetLayoutCreateInfo warp_layout{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};
    warp_layout.bindingCount = 9; warp_layout.pBindings = warp_bindings;
    check(vkCreateDescriptorSetLayout(device, &warp_layout, nullptr, &display_state.warp_layout), "framegen descriptors");
    display_state.warp_pipeline_layout = world_pipeline_layout(display_state.warp_layout);
    display_state.interpolate = display_load("display_interpolate_cs.spv", display_state.warp_pipeline_layout, true);
    display_state.constants[0] = make_buffer(32, VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT, true);
    display_state.constants[1] = make_buffer(32, VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT, true);
    guest_ensure_samplers();
}

Image display_target(Image image, uint32_t width, uint32_t height) {
    if (image.view && display_state.width == width && display_state.height == height) return image;
    if (image.handle) retire_image(image);
    return make_image(width, height, VK_FORMAT_R16G16B16A16_SFLOAT,
        VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT,
        VK_IMAGE_ASPECT_COLOR_BIT);
}

void display_dispatch(VkCommandBuffer cmd, VkPipeline pipeline, Image& dest, int dest_slot, VkImageView current, VkImage source, VkImageView history, VkImageView motion, float history_weight, Buffer& constants, uint32_t in_w, uint32_t in_h, uint32_t out_w, uint32_t out_h) {
    float values[]{float(in_w), float(in_h), float(out_w), float(out_h), history_weight, 0.f, guest.frame.jitter_x, guest.frame.jitter_y};
    std::memcpy(constants.mapped, values, sizeof(values));
    check(vmaFlushAllocation(allocator, constants.memory, 0, 32), "flush display constants");
    barrier(cmd, source, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    bool first = dest_slot < 0 || dest_slot > 2 || !display_state.visited[dest_slot];
    VkImageLayout previous = first ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    if (dest_slot >= 0 && dest_slot < 3) display_state.visited[dest_slot] = 1;
    barrier(cmd, dest.handle, previous, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_ASPECT_COLOR_BIT,
        previous == VK_IMAGE_LAYOUT_UNDEFINED ? VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT : VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,
        previous == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
    auto set = world_set(world.slots[slot_index], display_state.layout);
    world_write_buffer(set, 0, constants, 32, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
    world_write_image(set, 1, current);
    world_write_image(set, 2, history ? history : current);
    world_write_image(set, 3, motion ? motion : display_state.zero.view);
    world_write_image(set, 4, {}, guest.samplers[1], true);
    display_write_storage(set, 5, dest.view);
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipeline);
    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, display_state.pipeline_layout, 0, 1, &set, 0, nullptr);
    vkCmdDispatch(cmd, (out_w + 7) / 8, (out_h + 7) / 8, 1);
    barrier(cmd, dest.handle, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,
        VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT | VK_ACCESS_2_TRANSFER_READ_BIT);
}

struct FgLive {
    std::mutex mu;
    SevkFrameGenStatus status{};
    uint32_t generate_count{}, skip_count{}, game_presents{}, display_presents{}, used_velocity{};
    float game_fps{}, display_fps{}, scale_x{1.f}, scale_y{1.f};
    std::chrono::steady_clock::time_point window{}, last_game{}, refresh_probe{};
    bool have_game{}, refresh_capped{}, was_capped{}, last_failed{}, probed{};
    int refresh_hz{};
    char path[64]{};
    char error[192]{};
    char evidence[192]{};
};

static FgLive& fg_live() { static FgLive live; return live; }

static void fg_roll_fps(FgLive& live, std::chrono::steady_clock::time_point now) {
    if (live.window.time_since_epoch().count() == 0) { live.window = now; return; }
    auto elapsed = now - live.window;
    if (elapsed < std::chrono::milliseconds(500)) return;
    float seconds = std::chrono::duration<float>(elapsed).count();
    if (seconds > 0.f) {
        live.game_fps = live.game_presents / seconds;
        live.display_fps = live.display_presents / seconds;
    }
    live.game_presents = live.display_presents = 0;
    live.window = now;
}

static void fg_probe_refresh(FgLive& live) {
    auto now = std::chrono::steady_clock::now();
    if (live.probed && now - live.refresh_probe < std::chrono::seconds(2)) return;
    live.refresh_probe = now;
    live.probed = true;
    int hz = 0;
#ifdef _WIN32
    DEVMODEW mode{};
    mode.dmSize = sizeof(mode);
    if (EnumDisplaySettingsW(nullptr, ENUM_CURRENT_SETTINGS, &mode) && mode.dmDisplayFrequency >= 30 && mode.dmDisplayFrequency <= 500)
        hz = int(mode.dmDisplayFrequency);
#endif
    if (hz >= 30) live.refresh_hz = hz;
}

void fg_assign(const char* path, const char* error, float scale_x, float scale_y, bool velocity, const char* source = nullptr, const char* reactive = "none") {
    auto& live = fg_live();
    std::lock_guard<std::mutex> lock(live.mu);
    std::snprintf(live.path, sizeof(live.path), "%s", path ? path : "");
    std::snprintf(live.error, sizeof(live.error), "%s", error ? error : "");
    live.scale_x = scale_x;
    live.scale_y = scale_y;
    live.used_velocity = velocity ? 1u : 0u;
    const char* named = source && source[0] ? source : (velocity ? "renderer/object-motion" : "none");
    std::snprintf(live.evidence, sizeof(live.evidence),
        "Present source=%s scale=%.3f,%.3f scene=preHUD reset=%s volumeReactive=%s",
        named, scale_x, scale_y, live.path, reactive && reactive[0] ? reactive : "none");
}

void fg_count_skip() { auto& live = fg_live(); std::lock_guard<std::mutex> lock(live.mu); ++live.skip_count; }
void fg_count_generate() { auto& live = fg_live(); std::lock_guard<std::mutex> lock(live.mu); ++live.generate_count; live.last_failed = false; }

bool fg_refresh_blocked() {
    auto& live = fg_live();
    std::lock_guard<std::mutex> lock(live.mu);
    fg_probe_refresh(live);
    bool capped = false;
    if (live.refresh_hz >= 30 && live.have_game) {
        double period = 1.0 / live.refresh_hz;
        double ratio = std::chrono::duration<double>(std::chrono::steady_clock::now() - live.last_game).count() / period;
        capped = live.refresh_capped ? ratio < 1.25 : ratio < 1.12;
    }
    live.was_capped = live.refresh_capped;
    live.refresh_capped = capped;
    return capped;
}

bool fg_consume_uncap() {
    auto& live = fg_live();
    std::lock_guard<std::mutex> lock(live.mu);
    if (!live.was_capped || live.refresh_capped) return false;
    live.was_capped = false;
    return true;
}

void fg_note_game() {
    if (!swapchain) return;
    auto& live = fg_live();
    std::lock_guard<std::mutex> lock(live.mu);
    auto now = std::chrono::steady_clock::now();
    live.last_game = now;
    live.have_game = true;
    ++live.game_presents;
    ++live.display_presents;
    fg_roll_fps(live, now);
}

void fg_presented() {
    auto& live = fg_live();
    std::lock_guard<std::mutex> lock(live.mu);
    std::snprintf(live.path, sizeof(live.path), "interpolated-after-present");
    live.error[0] = 0;
    live.last_failed = false;
    ++live.display_presents;
    fg_roll_fps(live, std::chrono::steady_clock::now());
}

void fg_present_failed(const char* path, const char* error) {
    auto& live = fg_live();
    std::lock_guard<std::mutex> lock(live.mu);
    std::snprintf(live.path, sizeof(live.path), "%s", path ? path : "fail");
    std::snprintf(live.error, sizeof(live.error), "%s", error ? error : "");
    live.last_failed = true;
}

void fg_publish() {
    auto& live = fg_live();
    std::lock_guard<std::mutex> lock(live.mu);
    SevkFrameGenStatus status{};
    status.size = sizeof(status);
    status.abi_version = SEVK_ABI_VERSION;
    status.enabled = (display_flags & 2) ? 1u : 0u;
    status.loaded = display_state.interpolate ? 1u : 0u;
    status.ready = display_state.interpolate && display_state.ready && display_state.depth_ready ? 1u : 0u;
    status.eligible = (!fg_denied && (!swapchain || swap_transfer)) ? 1u : 0u;
    status.vsync = vsync ? 1u : 0u;
    status.refresh_hz = uint32_t(std::max(0, live.refresh_hz));
    status.width = display_state.width ? display_state.width : extent.width;
    status.height = display_state.height ? display_state.height : extent.height;
    status.generate_count = live.generate_count;
    status.skip_count = live.skip_count;
    status.used_velocity = live.used_velocity;
    status.last_failed = live.last_failed ? 1u : 0u;
    status.game_fps = live.game_fps;
    status.display_fps = live.display_fps;
    status.mv_scale_x = live.scale_x;
    status.mv_scale_y = live.scale_y;
    std::snprintf(status.path, sizeof(status.path), "%s", live.path[0] ? live.path : "off");
    std::snprintf(status.error, sizeof(status.error), "%s", live.error);
    std::snprintf(status.device, sizeof(status.device), "%s", stats.device_name);
    std::snprintf(status.evidence, sizeof(status.evidence), "%s", live.evidence[0] ? live.evidence : "Motion vectors: no evaluate attempted");
    live.status = status;
}

static int fg_copy_status(SevkFrameGenStatus* out) {
    if (!out || out->size != sizeof(SevkFrameGenStatus) || out->abi_version != SEVK_ABI_VERSION) return -1;
    auto& live = fg_live();
    std::lock_guard<std::mutex> lock(live.mu);
    uint32_t size = out->size;
    *out = live.status;
    out->size = size;
    out->abi_version = SEVK_ABI_VERSION;
    return 0;
}

void display_store_depth(VkCommandBuffer cmd, Image& dest, bool known) {
    auto& src = world.gbuffer[3];
    barrier(cmd, src.handle, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_DEPTH_BIT,
        VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT);
    barrier(cmd, dest.handle, known ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        known ? VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT : VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT, known ? VK_ACCESS_2_SHADER_SAMPLED_READ_BIT : 0,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT);
    VkImageCopy region{};
    region.srcSubresource = {VK_IMAGE_ASPECT_DEPTH_BIT, 0, 0, 1};
    region.dstSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1};
    region.extent = {display_state.depth_w, display_state.depth_h, 1};
    vkCmdCopyImage(cmd, src.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, dest.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);
    barrier(cmd, src.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_DEPTH_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT,
        VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    barrier(cmd, dest.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
}

void display_warp(VkCommandBuffer cmd, int write, VkImageView current, VkImage current_image, VkImageView history, VkImageView motion, VkImageView depth, VkImageView previous_depth, float scale_x, float scale_y, uint32_t out_w, uint32_t out_h, VkImageView reactive, uint32_t has_reactive) {
    struct Constants { uint32_t width, height; float inv_width, inv_height, scale_x, scale_y; uint32_t inverted, reactive; };
    static_assert(sizeof(Constants) == 32, "framegen constants are 32 bytes");
    Constants values{out_w, out_h, 1.f / float(out_w), 1.f / float(out_h), scale_x, scale_y, 1u, has_reactive};
    auto& constants = display_state.constants[1];
    std::memcpy(constants.mapped, &values, sizeof(values));
    check(vmaFlushAllocation(allocator, constants.memory, 0, 32), "flush framegen constants");
    auto& dest = display_state.interpolant;
    barrier(cmd, current_image, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    bool first = !display_state.visited[2];
    display_state.visited[2] = 1;
    barrier(cmd, dest.handle, first ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_ASPECT_COLOR_BIT,
        first ? VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT : VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,
        first ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
    auto set = world_set(world.slots[slot_index], display_state.warp_layout);
    world_write_buffer(set, 0, constants, 32, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
    world_write_image(set, 1, current);
    world_write_image(set, 2, history);
    world_write_image(set, 3, motion);
    world_write_image(set, 4, {}, guest.samplers[1], true);
    display_write_storage(set, 5, dest.view);
    world_write_image(set, 6, depth);
    world_write_image(set, 7, previous_depth);
    world_write_image(set, 8, has_reactive && reactive ? reactive : display_state.zero.view);
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, display_state.interpolate);
    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, display_state.warp_pipeline_layout, 0, 1, &set, 0, nullptr);
    vkCmdDispatch(cmd, (out_w + 7) / 8, (out_h + 7) / 8, 1);
    // The present pass samples this as a texture. Tonemap bytes stay encoded
    // until that pass decodes them; a transfer blit would encode them again.
    barrier(cmd, dest.handle, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,
        VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT | VK_ACCESS_2_TRANSFER_READ_BIT);
    (void)write;
}

void present_generated_color(VkCommandBuffer cmd, uint32_t index) {
    // Same grade as the real swapchain pass. Stock tonemap stores encoded sRGB
    // in UNORM; post.compose decodes it and the sRGB attachment encodes once.
    // A raw blit of those bytes onto an sRGB image encodes them a second time
    // and blows the highlights out. Sprites are drawn after the grade, the
    // same order as the real frame, so HUD and menus are on the generated image.
    VkImage image = swap_images[index];
    bool known = index < image_initialized.size() && image_initialized[index];
    barrier(cmd, image, known ? VK_IMAGE_LAYOUT_PRESENT_SRC_KHR : VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        known ? VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT : VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT, known ? VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT : 0,
        VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    barrier(cmd, depth.handle, VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL, VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL, VK_IMAGE_ASPECT_DEPTH_BIT,
        VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT, VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,
        VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,
        VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);
    VkRenderingAttachmentInfo color{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};
    color.imageView = swap_views[index];
    color.imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    color.loadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    color.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    VkRenderingAttachmentInfo depth_attachment{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};
    depth_attachment.imageView = depth.view;
    depth_attachment.imageLayout = VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;
    depth_attachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
    depth_attachment.storeOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    depth_attachment.clearValue.depthStencil = {0, 0};
    VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};
    rendering.renderArea = {{0, 0}, extent};
    rendering.layerCount = 1;
    rendering.colorAttachmentCount = 1;
    rendering.pColorAttachments = &color;
    rendering.pDepthAttachment = &depth_attachment;
    vkCmdBeginRendering(cmd, &rendering);
    auto& wslot = world.slots[slot_index];
    float dimensions[]{float(world.width ? world.width : extent.width), float(world.height ? world.height : extent.height),
        float(extent.width), float(extent.height), hdr_active ? 1.f : 0.f, 0, 0, 0};
    std::memcpy(wslot.dimensions.mapped, dimensions, 32);
    check(vmaFlushAllocation(allocator, wslot.dimensions.memory, 0, 32), "flush generated frame dimensions");
    auto set = world_light_set(wslot);
    world_write_image(set, 152, display_state.interpolant.view);
    VkPipeline grade = post.output && post.compose ? post.compose : world.compose;
    if (grade) world_fullscreen(cmd, grade, set, world.light_pipeline_layout);
    if(transparency.output_depth_ready)render_output_billboards(cmd,wslot);
    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline_layout, 0, 1, &slots[slot_index].descriptor, 0, nullptr);
    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline_layout, 2, 1, &shadow_descriptor, 0, nullptr);
    emit_sprites(cmd, extent, frame_sprites.data(), static_cast<uint32_t>(frame_sprites.size()),
        sprite_pipeline, additive_pipeline, sprite_premul_pipeline, sprite_premul_additive_pipeline, 0, 0, true);
    vkCmdEndRendering(cmd);
    barrier(cmd, image, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_LAYOUT_PRESENT_SRC_KHR, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT, VK_PIPELINE_STAGE_2_NONE, 0);
}

void display_try_warp(VkCommandBuffer cmd, uint32_t write, VkImageView motion, uint32_t in_w, uint32_t in_h, uint32_t out_w, uint32_t out_h) {
    bool motion_ok = world.motion.view && world.motion_w == in_w && world.motion_h == in_h;
    float scale_x = in_w ? float(out_w) / float(in_w) : 1.f;
    float scale_y = in_h ? float(out_h) / float(in_h) : 1.f;
    VkImageView anomaly_view{}, reactive_view{};
    VkImage anomaly_image{}, reactive_image{};
    uint32_t anomaly_w = 0, anomaly_h = 0, reactive_w = 0, reactive_h = 0;
    bool anomaly = guest_catalog_view(kGuestCatalogVelocity, anomaly_view, anomaly_image, anomaly_w, anomaly_h);
    bool reactive = guest_catalog_view(kGuestCatalogVolumeReactive, reactive_view, reactive_image, reactive_w, reactive_h);
    const char* source = anomaly ? "Anomaly/velocity" : (motion_ok ? "renderer/object-motion" : "none");
    const char* reactive_name = reactive ? "current" : "none";
    if (anomaly) {
        motion = anomaly_view;
        motion_ok = true;
        scale_x = float(out_w) / float(anomaly_w);
        scale_y = float(out_h) / float(anomaly_h);
    }
    if (vsync) {
        display_state.depth_ready = false;
        fg_assign("vsync", "Turn VSync off. Extra Present with VSync on waits a refresh and halves FPS.", scale_x, scale_y, motion_ok, source, reactive_name);
        return;
    }
    if (fg_denied || actual_present_mode != VK_PRESENT_MODE_IMMEDIATE_KHR) {
        display_state.depth_ready = false;
        fg_assign("no-immediate", "Immediate present is unavailable, so the extra frame would queue.", scale_x, scale_y, motion_ok, source, reactive_name);
        return;
    }
    if (!swap_transfer) {
        display_state.depth_ready = false;
        fg_assign("no-swapchain", "Swapchain cannot copy the interpolant.", scale_x, scale_y, motion_ok, source, reactive_name);
        return;
    }
    if (fg_refresh_blocked()) {
        display_state.depth_ready = false;
        fg_assign("refresh-cap", "", scale_x, scale_y, motion_ok, source, reactive_name);
        return;
    }
    if (fg_consume_uncap()) {
        display_state.depth_ready = false;
        fg_assign("refresh-uncap", "", scale_x, scale_y, motion_ok, source, reactive_name);
        return;
    }
    if (!world.gbuffer[3].handle) {
        fg_assign("no-scene", "", scale_x, scale_y, false, source, reactive_name);
        return;
    }
    if (display_state.depth_w != in_w || display_state.depth_h != in_h || !display_state.depth_copy[0].view) {
        for (auto& image : display_state.depth_copy) if (image.handle) retire_image(image);
        display_state.depth_copy[0] = make_image(in_w, in_h, VK_FORMAT_R32_SFLOAT, VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT, VK_IMAGE_ASPECT_COLOR_BIT);
        display_state.depth_copy[1] = make_image(in_w, in_h, VK_FORMAT_R32_SFLOAT, VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT, VK_IMAGE_ASPECT_COLOR_BIT);
        display_state.depth_w = in_w;
        display_state.depth_h = in_h;
        display_state.depth_ready = false;
        display_state.depth_visited[0] = display_state.depth_visited[1] = 0;
        display_state.depth_index = 0;
    }
    uint32_t depth_write = display_state.depth_index & 1u;
    display_store_depth(cmd, display_state.depth_copy[depth_write], display_state.depth_visited[depth_write] != 0);
    display_state.depth_visited[depth_write] = 1;
    bool history = display_state.ready && display_state.depth_ready && motion_ok && (!anomaly || guest.frame.history_valid != 0);
    VkImageView current_depth = display_state.depth_copy[depth_write].view;
    VkImageView previous_depth = display_state.depth_copy[depth_write ^ 1u].view;
    display_state.depth_index ^= 1u;
    display_state.depth_ready = true;
    if (!history) {
        fg_count_skip();
        const char* why = anomaly && guest.frame.history_valid == 0
            ? "Anomaly velocity history is invalid."
            : (motion_ok ? "" : "Object motion is not ready; presenting real frames only.");
        fg_assign("seed", why, scale_x, scale_y, motion_ok, source, reactive_name);
        return;
    }
    auto sample_catalog = [&](VkImage image) {
        barrier(cmd, image, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    };
    if (anomaly) sample_catalog(anomaly_image);
    if (reactive) sample_catalog(reactive_image);
    display_warp(cmd, int(write), display_state.color[write].view, display_state.color[write].handle, display_state.color[write ^ 1u].view, motion, current_depth, previous_depth, scale_x, scale_y, out_w, out_h, reactive ? reactive_view : display_state.zero.view, reactive ? 1u : 0u);
    display_state.interpolant_ready = true;
    fg_count_generate();
    fg_assign("interpolated", "", scale_x, scale_y, true, source, reactive_name);
}

VkImageView prepare_display(VkCommandBuffer cmd) {
    display_state.interpolant_ready = false;
    display_state.compose = {};
    bool upscale = (display_flags & 4) != 0;
    bool generated = (display_flags & 2) != 0;
    bool ngx_on = ngx_active();
    bool fsr_on = fsr_active();
    bool guest_output=std::any_of(guest.passes.begin(),guest.passes.end(),[](const auto& pass){
        if(pass.mark!=6 || !pass.pipeline || (pass.flags&16384u))return false;
        if(pass.storage==kGuestHdr)return true;
        return std::any_of(std::begin(pass.sampled),std::end(pass.sampled),[](auto image){return image==kGuestUpscale;});
    });
    if (!upscale && !generated && !ngx_on && !fsr_on && !guest_output) return {};
    VkImageView source_view = post.output && post.output->image.view ? post.output->image.view : world.hdr.view;
    VkImage source = post.output && post.output->image.view ? post.output->image.handle : world.hdr.handle;
    if (!source_view || !source) {
        if (generated) fg_assign("no-scene", "", 1.f, 1.f, false);
        return {};
    }
    uint32_t out_w = std::max(1u, extent.width), out_h = std::max(1u, extent.height);
    uint32_t in_w = std::max(1u, world.width ? world.width : extent.width), in_h = std::max(1u, world.height ? world.height : extent.height);
    init_display();
    bool resized = display_state.width != out_w || display_state.height != out_h;
    display_state.color[0] = display_target(display_state.color[0], out_w, out_h);
    display_state.color[1] = display_target(display_state.color[1], out_w, out_h);
    display_state.interpolant = display_target(display_state.interpolant, out_w, out_h);
    if (!display_state.zero.view) display_state.zero = make_image(1, 1, VK_FORMAT_R16G16_SFLOAT, VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT, VK_IMAGE_ASPECT_COLOR_BIT);
    if (!display_state.zero_ready) {
        barrier(cmd, display_state.zero.handle, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT, 0, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT);
        VkClearColorValue black{}; VkImageSubresourceRange range{VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        vkCmdClearColorImage(cmd, display_state.zero.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &black, 1, &range);
        barrier(cmd, display_state.zero.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        display_state.zero_ready = true;
    }
    if (resized) { display_state.ready = false; display_state.width = out_w; display_state.height = out_h; display_state.visited[0] = display_state.visited[1] = display_state.visited[2] = 0; }
    uint32_t write = display_state.index & 1;
    bool motion_ok = world.motion.view && world.motion_w == in_w && world.motion_h == in_h;
    VkImageView motion = motion_ok ? world.motion.view : display_state.zero.view;
    bool ngx = display_state.upscale_ready;
    bool fsr_ok = !ngx && fsr_on && fsr_evaluate(cmd, write, in_w, in_h, out_w, out_h);
    if (!ngx && !fsr_ok && !upscale && guest_output) {
        // Ordinary AfterUpscale packs get the same bilinear output that the
        // stock compose samples. Do not enable a temporal/filtering algorithm.
        auto& target=display_state.color[write];
        bool fresh=!display_state.visited[write];
        barrier(cmd,source,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
        barrier(cmd,target.handle,fresh?VK_IMAGE_LAYOUT_UNDEFINED:VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,fresh?0:VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT);
        VkImageBlit blit{};blit.srcSubresource=blit.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};
        blit.srcOffsets[1]={int(in_w),int(in_h),1};blit.dstOffsets[1]={int(out_w),int(out_h),1};
        vkCmdBlitImage(cmd,source,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,target.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&blit,VK_FILTER_LINEAR);
        barrier(cmd,source,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT);
        barrier(cmd,target.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT);
        display_state.visited[write]=1;
    }
    else if (!ngx && !fsr_ok) display_dispatch(cmd, display_state.upscale, display_state.color[write], int(write), source_view, source,
        display_state.ready ? display_state.color[write ^ 1].view : display_state.zero.view, motion,
        upscale && display_state.ready ? 0.9f : 0.f, display_state.constants[0], in_w, in_h, out_w, out_h);
    if (generated) display_try_warp(cmd, write, motion_ok ? motion : display_state.zero.view, in_w, in_h, out_w, out_h);
    display_state.index ^= 1;
    display_state.ready = true;
    display_state.compose = (upscale || ngx || fsr_ok || guest_output) ? display_state.color[write].view : VkImageView{};
    if(display_state.compose){display_state.upscale_ready=true;display_state.upscale_view=display_state.color[write].view;
        display_state.upscale_handle=display_state.color[write].handle;display_state.upscale_w=out_w;display_state.upscale_h=out_h;}
    return display_state.compose;
}

void present_generated_frame() {
    if (!display_state.interpolant_ready) return;
    if (!swapchain || actual_present_mode != VK_PRESENT_MODE_IMMEDIATE_KHR || !swap_transfer) {
        display_state.interpolant_ready = false;
        fg_present_failed("no-swapchain", "Extra present was not submitted.");
        return;
    }
    join_present(false);
    wait(slots[slot_index].completion);
    if (!display_state.acquire) {
        VkSemaphoreCreateInfo info{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO};
        check(vkCreateSemaphore(device, &info, nullptr, &display_state.acquire), "display acquire semaphore");
        check(vkCreateSemaphore(device, &info, nullptr, &display_state.present), "display present semaphore");
    }
    uint32_t index = 0;
    VkResult acquired = vkAcquireNextImageKHR(device, swapchain, 0, display_state.acquire, VK_NULL_HANDLE, &index);
    if (acquired != VK_SUCCESS && acquired != VK_SUBOPTIMAL_KHR) {
        fg_present_failed("no-swapchain", "Extra present could not acquire a swap image.");
        return;
    }
    VkCommandBufferAllocateInfo allocate{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
    allocate.commandPool = upload_pool; allocate.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; allocate.commandBufferCount = 1;
    VkCommandBuffer cmd{}; check(vkAllocateCommandBuffers(device, &allocate, &cmd), "display present command");
    try {
        VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO}; begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        check(vkBeginCommandBuffer(cmd, &begin), "begin display present");
        present_generated_color(cmd, index);
        check(vkEndCommandBuffer(cmd), "end display present");
        VkCommandBufferSubmitInfo command{VK_STRUCTURE_TYPE_COMMAND_BUFFER_SUBMIT_INFO}; command.commandBuffer = cmd;
        VkSemaphoreSubmitInfo acquired_wait{VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO}; acquired_wait.semaphore = display_state.acquire; acquired_wait.stageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
        uint64_t completion = ++next_completion;
        VkSemaphoreSubmitInfo signals[2]{};
        signals[0].sType = signals[1].sType = VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
        signals[0].semaphore = timeline; signals[0].value = completion; signals[0].stageMask = VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
        signals[1].semaphore = display_state.present; signals[1].stageMask = VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
        VkSubmitInfo2 submit{VK_STRUCTURE_TYPE_SUBMIT_INFO_2};
        submit.commandBufferInfoCount = 1; submit.pCommandBufferInfos = &command;
        submit.waitSemaphoreInfoCount = 1; submit.pWaitSemaphoreInfos = &acquired_wait;
        submit.signalSemaphoreInfoCount = 2; submit.pSignalSemaphoreInfos = signals;
        join_present(); check(vkQueueSubmit2(queue, 1, &submit, VK_NULL_HANDLE), "submit generated frame");
        VkPresentInfoKHR present_info{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
        present_info.waitSemaphoreCount = 1; present_info.pWaitSemaphores = &display_state.present;
        present_info.swapchainCount = 1; present_info.pSwapchains = &swapchain; present_info.pImageIndices = &index;
        VkResult presented = vkQueuePresentKHR(queue, &present_info);
        wait(completion);
        if (index < image_initialized.size()) image_initialized[index] = true;
        if (presented == VK_SUCCESS || presented == VK_SUBOPTIMAL_KHR) fg_presented();
        else fg_present_failed("no-swapchain", "Extra present was rejected.");
    } catch (...) { vkFreeCommandBuffers(device, upload_pool, 1, &cmd); display_state.interpolant_ready = false; fg_present_failed("fail", "Extra present failed."); return; }
    vkFreeCommandBuffers(device, upload_pool, 1, &cmd);
}
