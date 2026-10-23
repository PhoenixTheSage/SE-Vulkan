// FidelityFX Vulkan upscaler, loaded the same way DX11 loads amd_frs.dll.
// SDK 1.1.4's default provider is FSR 3.1.4. FSR 4 is not used; SDK 2.x dropped Vulkan.
struct FsrVersionQuery { uint64_t type; FsrHeader* next; uint64_t version_id; const char* version_name; };
struct FsrRequest {
    struct Debug {
        std::string gpu{"not detected yet"};
        std::string version{"FSR 3.1.4"};
        std::string path;
        uint32_t vendor{};
        int ready{};
        int supported{};
        int ishdr{};
        uint32_t evals{};
        int failed{};
        uint32_t in_w{}, in_h{}, out_w{}, out_h{};
        int swap_hdr{};
        int want_hdr{};
        int jitter_x{}, jitter_y{};
        int history{};
        uint32_t motion_w{}, motion_h{};
        int feature{};
    };
    std::mutex mu;
    bool enabled{};
    uint32_t mode{};
    float sharpness{0.5f};
    std::string directory, status{"off"};
    uint64_t revision{};
    Debug debug;
};
static FsrRequest& fsr_request() { static FsrRequest request; return request; }

static void fsr_set_status(const std::string& text) {
    auto& request = fsr_request();
    std::lock_guard<std::mutex> lock(request.mu);
    request.status = text;
}

static int fsr_configure(uint32_t enabled, uint32_t mode, float sharpness, const char* directory) {
    if (enabled && !directory) return -1;
    if (mode > 4 || !std::isfinite(sharpness)) return -1;
    if (sharpness < 0.f) sharpness = 0.f;
    if (sharpness > 1.f) sharpness = 1.f;
    auto& request = fsr_request();
    std::lock_guard<std::mutex> lock(request.mu);
    std::string path = directory ? directory : "";
    if (request.enabled == (enabled != 0) && request.mode == mode && request.sharpness == sharpness && request.directory == path) return 0;
    request.enabled = enabled != 0;
    request.mode = mode;
    request.sharpness = sharpness;
    request.directory = std::move(path);
    ++request.revision;
    if (!request.enabled) request.status = "off";
    return 0;
}

static int fsr_copy_status(char* text, uint32_t capacity) {
    if (!text || capacity < 2) return -1;
    auto& request = fsr_request();
    std::lock_guard<std::mutex> lock(request.mu);
    std::snprintf(text, capacity, "%s", request.status.c_str());
    return 0;
}

static int fsr_copy_debug(char* text, uint32_t capacity) {
    if (!text || capacity < 2) return -1;
    auto& request = fsr_request();
    std::lock_guard<std::mutex> lock(request.mu);
    auto& d = request.debug;
    std::snprintf(text, capacity,
        "gpu=%s\nvendor=%04X\nready=%d\nsupported=%d\nishdr=%d\nstate=%s\nversion=%s\nevals=%u\nfailed=%d\npath=%s\n"
        "in=%ux%u\nout=%ux%u\nhdr=%d\nwant=%d\njitter=%d,%d\nhistory=%d\nmotion=%ux%u\nfeature=%d\n",
        d.gpu.c_str(), d.vendor, d.ready, d.supported, d.ishdr, request.status.c_str(), d.version.c_str(),
        d.evals, d.failed, d.path.c_str(), d.in_w, d.in_h, d.out_w, d.out_h, d.swap_hdr, d.want_hdr,
        d.jitter_x, d.jitter_y, d.history, d.motion_w, d.motion_h, d.feature);
    return 0;
}

void fsr_publish(uint32_t in_w, uint32_t in_h, uint32_t out_w, uint32_t out_h, bool count, bool failed, const char* path) {
    VkPhysicalDeviceProperties props{};
    if (physical) vkGetPhysicalDeviceProperties(physical, &props);
    auto& request = fsr_request();
    std::lock_guard<std::mutex> lock(request.mu);
    if (props.vendorID) {
        request.debug.gpu = props.deviceName[0] ? props.deviceName : "unknown";
        request.debug.vendor = props.vendorID;
    }
    request.debug.ready = fsr.ready ? 2 : (fsr.library ? 1 : 0);
    request.debug.feature = fsr.library ? 1 : 0;
    request.debug.swap_hdr = hdr_active ? 1 : 0;
    request.debug.want_hdr = (display_flags & 1) ? 1 : 0;
    request.debug.jitter_x = static_cast<int>(std::lround(jitter.pixel[0] * 1000.f));
    request.debug.jitter_y = static_cast<int>(std::lround(jitter.pixel[1] * 1000.f));
    request.debug.history = guest.frame.history_valid ? 1 : 0;
    request.debug.motion_w = world.motion.handle ? world.motion_w : 0;
    request.debug.motion_h = world.motion.handle ? world.motion_h : 0;
    if (in_w && in_h && out_w && out_h) {
        request.debug.in_w = in_w;
        request.debug.in_h = in_h;
        request.debug.out_w = out_w;
        request.debug.out_h = out_h;
    }
    if (!fsr.version.empty()) request.debug.version = fsr.version;
    if (path) request.debug.path = path;
    if (path && path[0]) request.debug.ishdr = 1;
    if (fsr.ready) request.debug.supported = 1;
    if (failed) request.debug.failed = 1;
    else if (count) request.debug.failed = 0;
    if (count) ++request.debug.evals;
}

static float fsr_scale(uint32_t mode) {
    switch (mode) {
        case 1: return 1.f / 1.7f;
        case 2: return 0.5f;
        case 3: return 1.f / 3.f;
        case 4: return 1.f;
        default: return 1.f / 1.5f;
    }
}

void fsr_apply_scale() {
    if (ngx_active()) return;
    auto& request = fsr_request();
    std::lock_guard<std::mutex> lock(request.mu);
    if (!request.enabled || request.directory.empty()) return;
    float scale = fsr_scale(request.mode);
    if (scale < 0.25f) scale = 0.25f;
    if (scale > 1.f) scale = 1.f;
    render_fraction = scale;
}

bool fsr_active() {
    auto& request = fsr_request();
    std::lock_guard<std::mutex> lock(request.mu);
    return request.enabled && !request.directory.empty();
}

static void* fsr_load_library(const std::string& directory) {
#if defined(_WIN32)
    std::wstring path = ngx_widen(directory);
    if (!path.empty() && path.back() != L'\\' && path.back() != L'/') path.push_back(L'\\');
    path += L"amd_fidelityfx_vk.dll";
    return LoadLibraryExW(path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
#else
    std::string path = directory;
    if (!path.empty() && path.back() != '/') path.push_back('/');
    void* library = dlopen((path + "libamd_fidelityfx_vk.so").c_str(), RTLD_NOW | RTLD_LOCAL);
    if (!library) library = dlopen((path + "amd_fidelityfx_vk.so").c_str(), RTLD_NOW | RTLD_LOCAL);
    return library;
#endif
}

void* fsr_symbol(const char* name) {
    if (!fsr.library) return nullptr;
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(static_cast<HMODULE>(fsr.library), name));
#else
    return dlsym(fsr.library, name);
#endif
}

// FFX API surface format ids for the formats Anomaly catalog images can take; 0 means FSR cannot read it.
static uint32_t fsr_surface_format(VkFormat format) {
    switch (format) {
    case VK_FORMAT_R32G32B32A32_SFLOAT: return 3;
    case VK_FORMAT_R16G16B16A16_SFLOAT: return 4;
    case VK_FORMAT_R8G8B8A8_UNORM: return 10;
    case VK_FORMAT_B10G11R11_UFLOAT_PACK32: return 16;
    case VK_FORMAT_R16G16_SFLOAT: return 18;
    case VK_FORMAT_R16_SFLOAT: return 21;
    case VK_FORMAT_R8_UNORM: return 25;
    case VK_FORMAT_R32_SFLOAT: return 28;
    default: return 0;
    }
}

static FsrResource fsr_image(VkImage image, uint32_t format, uint32_t width, uint32_t height, uint32_t usage, uint32_t state) {
    FsrResource resource{};
    resource.resource = reinterpret_cast<void*>(image);
    resource.type = 2;
    resource.format = format;
    resource.width = width;
    resource.height = height;
    resource.depth = 1;
    resource.mip_count = 1;
    resource.usage = usage;
    resource.state = state;
    return resource;
}

bool fsr_evaluate(VkCommandBuffer cmd, uint32_t slot, uint32_t in_w, uint32_t in_h, uint32_t out_w, uint32_t out_h) {
    if (!cmd || !in_w || !in_h || !out_w || !out_h || !world.hdr.handle || !world.gbuffer[3].handle) return false;
    std::string directory;
    uint32_t mode = 0;
    float sharpness = 0.f;
    uint64_t revision = 0;
    {
        auto& request = fsr_request();
        std::lock_guard<std::mutex> lock(request.mu);
        if (!request.enabled || request.directory.empty()) return false;
        directory = request.directory;
        mode = request.mode;
        sharpness = request.sharpness;
        revision = request.revision;
    }
    if (fsr.revision != revision) { fsr.fail_frames = 0; fsr.revision = revision; if (fsr.directory != directory) fsr.ready = false; }
    if (fsr.fail_frames) { --fsr.fail_frames; return false; }
    VkImageView mv_view = world.motion.view;
    VkImage mv_image = world.motion.handle;
    uint32_t mv_w = world.motion_w, mv_h = world.motion_h;
    VkImageView catalog_view{}; VkImage catalog_image{}; uint32_t catalog_w = 0, catalog_h = 0;
    if (guest_anomaly_velocity(catalog_view, catalog_image, catalog_w, catalog_h)) {
        mv_view = catalog_view; mv_image = catalog_image; mv_w = catalog_w; mv_h = catalog_h;
        barrier(cmd, mv_image, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    }
    if (!mv_image || !mv_w || !mv_h) { fsr_publish(in_w, in_h, out_w, out_h, false, false, nullptr); fsr_set_status("FSR waiting for Anomaly velocity"); return false; }
    using Create = uint32_t (*)(void**, void*, const void*);
    using Destroy = uint32_t (*)(void**, const void*);
    using Dispatch = uint32_t (*)(void*, const void*);
    using Query = uint32_t (*)(void*, void*);
    if (!fsr.library) {
        fsr.library = fsr_load_library(directory);
        fsr.directory = directory;
        fsr.create = fsr_symbol("ffxCreateContext");
        fsr.destroy = fsr_symbol("ffxDestroyContext");
        fsr.dispatch = fsr_symbol("ffxDispatch");
        fsr.query = fsr_symbol("ffxQuery");
    }
    if (!fsr.library || !fsr.create || !fsr.dispatch) {
        { auto& request = fsr_request(); std::lock_guard<std::mutex> lock(request.mu); request.debug.supported = 2; }
        fsr_publish(in_w, in_h, out_w, out_h, false, false, nullptr);
        fsr_set_status("FSR Vulkan library missing");
        fsr.fail_frames = 60;
        return false;
    }
    auto create = reinterpret_cast<Create>(fsr.create);
    auto destroy = reinterpret_cast<Destroy>(fsr.destroy);
    auto dispatch = reinterpret_cast<Dispatch>(fsr.dispatch);
    if (!fsr.ready || fsr.out_w != out_w || fsr.out_h != out_h) {
        if (fsr.context && destroy) destroy(&fsr.context, nullptr);
        fsr.context = nullptr;
        fsr.ready = false;
        fsr.backend = {};
        fsr.backend.type = 3;
        fsr.backend.device = device;
        fsr.backend.physical = physical;
        fsr.backend.get_device_proc = vkGetDeviceProcAddr;
        fsr.created = {};
        fsr.created.type = 0x00010000ull;
        fsr.created.next = reinterpret_cast<FsrHeader*>(&fsr.backend);
        fsr.created.flags = 1u | 8u | 16u | 32u | 64u;
        fsr.created.max_render_w = out_w;
        fsr.created.max_render_h = out_h;
        fsr.created.max_upscale_w = out_w;
        fsr.created.max_upscale_h = out_h;
        void* context = nullptr;
        uint32_t result = create(&context, &fsr.created, nullptr);
        if (result != 0 || !context) {
            char text[96];
            std::snprintf(text, sizeof(text), "FSR context failed (%u)", result);
            { auto& request = fsr_request(); std::lock_guard<std::mutex> lock(request.mu); request.debug.supported = 2; }
            fsr_publish(in_w, in_h, out_w, out_h, false, true, nullptr);
            fsr_set_status(text);
            fsr.fail_frames = 60;
            return false;
        }
        fsr.context = context;
        fsr.ready = true;
        fsr.out_w = out_w;
        fsr.out_h = out_h;
        fsr.version = "FSR 3.1.4";
        if (fsr.query) {
            FsrVersionQuery query{};
            query.type = 6;
            if (reinterpret_cast<Query>(fsr.query)(fsr.context, &query) == 0 && query.version_name && query.version_name[0])
                fsr.version = query.version_name;
        }
    }
    auto& dest = display_state.color[slot & 1];
    bool first = !display_state.visited[slot & 1];
    display_state.visited[slot & 1] = 1;
    barrier(cmd, dest.handle, first ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_ASPECT_COLOR_BIT,
        first ? VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT : VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,
        first ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT | VK_ACCESS_2_SHADER_STORAGE_READ_BIT);
    // Vertical FOV from the frame block's unjittered projection (Frame.hlsli offset 64); the world
    // projection array is the view-projection, whose M22 changes with camera rotation.
    float fov = 1.047f;
    if (world.current().frame.size() >= 128) {
        float y;
        std::memcpy(&y, world.current().frame.data() + 64 + 5 * sizeof(float), sizeof(float));
        if (std::fabs(y) > 0.01f) fov = 2.f * std::atan(1.f / std::fabs(y));
    }
    auto now = std::chrono::steady_clock::now();
    float frame_ms = 16.6f;
    if (fsr.have_time) {
        frame_ms = std::chrono::duration<float, std::milli>(now - fsr.last_time).count();
        if (!(frame_ms > 0.f) || frame_ms > 250.f) frame_ms = 16.6f;
    }
    fsr.last_time = now;
    fsr.have_time = true;
    FsrDispatch work{};
    work.type = 0x00010001ull;
    work.command = cmd;
    work.color = fsr_image(world.hdr.handle, world.hdr_format==VK_FORMAT_B10G11R11_UFLOAT_PACK32?16u:4u, in_w, in_h, 0, 12);
    work.depth = fsr_image(world.gbuffer[3].handle, 28, in_w, in_h, 4, 12);
    work.motion = fsr_image(mv_image, 18, mv_w, mv_h, 0, 12);
    work.output = fsr_image(dest.handle, 4, out_w, out_h, 2, 2);
    // Anomaly's reactiveMask feeds FSR's reactive input when it matches the render size, as SE-FRS does on DX11.
    VkImageView reactive_view{}; VkImage reactive_image{}; uint32_t reactive_w = 0, reactive_h = 0, reactive_format = 0;
    if (guest_catalog_view(kGuestCatalogReactiveMask, reactive_view, reactive_image, reactive_w, reactive_h) && reactive_w == in_w && reactive_h == in_h)
        reactive_format = fsr_surface_format(guest.images[kGuestCatalogReactiveMask].format);
    if (reactive_format) {
        barrier(cmd, reactive_image, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        work.reactive = fsr_image(reactive_image, reactive_format, in_w, in_h, 0, 12);
    }
    work.jitter_x = jitter.pixel[0];
    work.jitter_y = jitter.pixel[1];
    work.mv_x = float(in_w) / float(mv_w);
    work.mv_y = float(in_h) / float(mv_h);
    work.render_w = in_w;
    work.render_h = in_h;
    work.upscale_w = out_w;
    work.upscale_h = out_h;
    work.sharpen = sharpness > 0.f ? 1 : 0;
    work.sharpness = sharpness;
    work.frame_ms = frame_ms;
    work.pre_exposure = 1.f;
    work.reset = guest.frame.history_valid ? 0 : 1;
    work.near_plane = 0.05f;
    work.far_plane = guest.frame.far_plane > 1.f ? guest.frame.far_plane : view_far;
    work.fov = fov;
    work.meters = 1.f;
    uint32_t result = dispatch(fsr.context, &work);
    barrier(cmd, dest.handle, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,
        VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT | VK_ACCESS_2_TRANSFER_READ_BIT);
    if (result != 0) {
        char text[96];
        std::snprintf(text, sizeof(text), "FSR evaluate failed (%u)", result);
        fsr_publish(in_w, in_h, out_w, out_h, false, true, "HDR hdrColor");
        fsr_set_status(text);
        fsr.fail_frames = 60;
        return false;
    }
    fsr_publish(in_w, in_h, out_w, out_h, true, false, "HDR hdrColor");
    fsr_set_status(fsr.version + (reactive_format ? " evaluating (reactive anomaly)" : " evaluating"));
    return true;
}

void cleanup_fsr() noexcept {
    if (fsr.context && fsr.destroy) reinterpret_cast<uint32_t (*)(void**, const void*)>(fsr.destroy)(&fsr.context, nullptr);
    fsr.context = nullptr;
    fsr.ready = false;
    fsr.have_time = false;
}
