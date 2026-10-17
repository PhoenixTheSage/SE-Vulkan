// Driver NGX, bound the same way the DX11 plugin binds _nvngx.dll:
// GetProcAddress the exports, preload the feature library from the plugin
// directory, and keep NVIDIA headers out of the tree. Evaluate runs on the
// graphics command buffer outside a render pass. Failure leaves the
// Catmull-Rom display pass in place.
struct NgxRequest {
    struct Debug {
        std::string gpu{"not detected yet"};
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
    uint32_t mode{}, preset{11};
    std::string directory;
    std::string owner;
    uint64_t revision{};
    std::string status{"idle"};
    Debug debug;
};
struct NgxExtensions {
    bool queried{};
    std::vector<std::string> instance, device;
};
struct NgxImage {
    VkImageView view;
    VkImage image;
    VkImageSubresourceRange range;
    VkFormat format;
    uint32_t width, height;
};
struct NgxResource {
    NgxImage image;
    int type;
    uint8_t read_write;
    uint8_t pad[3];
};
struct NgxPathList { const wchar_t* const* path; uint32_t length, pad; };
struct NgxLogInfo { void* callback; int level; uint8_t disable; uint8_t pad[3]; };
struct NgxFeatureInfo { NgxPathList paths; void* internal_data; NgxLogInfo log; };
static_assert(sizeof(NgxImage) == 48, "NGX image view layout");
static_assert(sizeof(NgxResource) == 56, "NGX resource layout");
static_assert(sizeof(NgxFeatureInfo) == 40, "NGX feature info layout");

static NgxRequest& ngx_request() { static NgxRequest request; return request; }

static void* ngx_library() {
    static void* module = nullptr;
    static bool tried = false;
    if (tried) return module;
    tried = true;
#if defined(_WIN32)
    std::vector<std::wstring> paths;
    wchar_t sys[MAX_PATH]{};
    if (GetSystemDirectoryW(sys, MAX_PATH)) {
        paths.push_back(std::wstring(sys) + L"\\_nvngx.dll");
        paths.push_back(std::wstring(sys) + L"\\nvngx.dll");
        DWORD bytes = 0;
        const wchar_t* key = L"SOFTWARE\\NVIDIA Corporation\\Global\\NGXCore";
        if (RegGetValueW(HKEY_LOCAL_MACHINE, key, L"FullPath", RRF_RT_REG_SZ, nullptr, nullptr, &bytes) == ERROR_SUCCESS && bytes > sizeof(wchar_t) && bytes < 4096) {
            std::wstring dir(bytes / sizeof(wchar_t), L'\0');
            if (RegGetValueW(HKEY_LOCAL_MACHINE, key, L"FullPath", RRF_RT_REG_SZ, nullptr, dir.data(), &bytes) == ERROR_SUCCESS) {
                while (!dir.empty() && dir.back() == L'\0') dir.pop_back();
                if (!dir.empty()) { paths.push_back(dir + L"\\_nvngx.dll"); paths.push_back(dir + L"\\nvngx.dll"); }
            }
        }
        std::error_code ec;
        for (auto it = std::filesystem::directory_iterator(std::filesystem::path(sys) / "DriverStore" / "FileRepository", ec); !ec && it != std::filesystem::directory_iterator(); it.increment(ec)) {
            if (ec || !it->is_directory()) continue;
            auto name = it->path().filename().wstring();
            if (name.size() < 2 || (name[0] != L'n' && name[0] != L'N') || (name[1] != L'v' && name[1] != L'V')) continue;
            paths.push_back((it->path() / L"_nvngx.dll").wstring());
        }
    }
    for (auto& path : paths) {
        std::error_code ec;
        if (!std::filesystem::exists(path, ec)) continue;
        module = LoadLibraryExW(path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (module) break;
    }
#else
    const char* names[]{"libnvidia-ngx.so.1", "libnvidia-ngx.so"};
    for (auto name : names) { module = dlopen(name, RTLD_NOW | RTLD_LOCAL); if (module) break; }
#endif
    return module;
}

static void* ngx_symbol(const char* name) {
    auto module = ngx_library();
    if (!module || !name) return nullptr;
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(static_cast<HMODULE>(module), name));
#else
    return dlsym(module, name);
#endif
}

static bool ngx_ok(int result) { return static_cast<uint32_t>(result) == 1u; }

static NgxExtensions& ngx_extension_list() {
    static NgxExtensions ext;
    if (ext.queried) return ext;
    ext.queried = true;
    using Req = int (*)(unsigned*, const char***, unsigned*, const char***);
    auto req = reinterpret_cast<Req>(ngx_symbol("NVSDK_NGX_VULKAN_RequiredExtensions"));
    unsigned ic = 0, dc = 0; const char** ie = nullptr; const char** de = nullptr;
    if (!req || !ngx_ok(req(&ic, &ie, &dc, &de))) return ext;
    for (unsigned i = 0; i < ic && ie && ie[i]; ++i) ext.instance.emplace_back(ie[i]);
    for (unsigned i = 0; i < dc && de && de[i]; ++i) ext.device.emplace_back(de[i]);
    return ext;
}

void ngx_add_instance_extensions(std::vector<const char*>& extensions, const std::vector<VkExtensionProperties>& available) {
    auto& wanted = ngx_extension_list().instance;
    for (auto& name : wanted) {
        bool present = false;
        for (auto& ext : available) if (name == ext.extensionName) present = true;
        if (present) extensions.push_back(name.c_str());
    }
}

void ngx_add_device_extensions(std::vector<const char*>& extensions, const std::vector<VkExtensionProperties>& available, VkPhysicalDeviceVulkan12Features& features12) {
    bool address = false;
    for (auto& name : ngx_extension_list().device) {
        bool present = false;
        for (auto& ext : available) if (name == ext.extensionName) present = true;
        if (!present) continue;
        if (name == "VK_KHR_buffer_device_address") address = true;
        extensions.push_back(name.c_str());
    }
    if (!address || features12.bufferDeviceAddress) return;
    VkPhysicalDeviceVulkan12Features query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES};
    VkPhysicalDeviceFeatures2 wrap{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &query};
    vkGetPhysicalDeviceFeatures2(physical, &wrap);
    if (query.bufferDeviceAddress) { features12.bufferDeviceAddress = VK_TRUE; return; }
    extensions.erase(std::remove_if(extensions.begin(), extensions.end(), [](const char* name) { return std::strcmp(name, "VK_KHR_buffer_device_address") == 0; }), extensions.end());
}

static bool ngx_same(const std::string& text, const char* literal) {
    if (!literal) return text.empty();
    size_t length = std::strlen(literal);
    if (text.size() != length) return false;
    for (size_t i = 0; i < length; ++i) {
        char a = text[i], b = literal[i];
        if (a >= 'A' && a <= 'Z') a = char(a - 'A' + 'a');
        if (b >= 'A' && b <= 'Z') b = char(b - 'A' + 'a');
        if (a != b) return false;
    }
    return true;
}

static int ngx_set_owner(const char* id) {
    auto& request = ngx_request();
    std::lock_guard<std::mutex> lock(request.mu);
    request.owner = id ? id : "";
    return 0;
}

static bool ngx_owned() {
    auto& request = ngx_request();
    std::lock_guard<std::mutex> lock(request.mu);
    return ngx_same(request.owner, "se-dlss");
}

static int ngx_configure(uint32_t enabled, uint32_t mode, uint32_t preset, const char* directory) {
    if (enabled && !directory) return -1;
    if (mode > 4) return -1;
    auto& request = ngx_request();
    std::lock_guard<std::mutex> lock(request.mu);
    std::string path = directory ? directory : "";
    uint32_t chosen = preset ? preset : 11u;
    if (request.enabled == (enabled != 0) && request.mode == mode && request.preset == chosen && request.directory == path) return 0;
    request.enabled = enabled != 0;
    request.mode = mode;
    request.preset = chosen;
    request.directory = std::move(path);
    ++request.revision;
    if (!request.enabled) request.status = "off";
    return 0;
}

static int ngx_copy_status(char* text, uint32_t capacity) {
    if (!text || capacity < 2) return -1;
    auto& request = ngx_request();
    std::lock_guard<std::mutex> lock(request.mu);
    std::snprintf(text, capacity, "%s", request.status.c_str());
    return 0;
}

static int ngx_copy_debug(char* text, uint32_t capacity) {
    if (!text || capacity < 2) return -1;
    auto& request = ngx_request();
    std::lock_guard<std::mutex> lock(request.mu);
    auto& d = request.debug;
    std::snprintf(text, capacity,
        "gpu=%s\nvendor=%04X\nready=%d\nsupported=%d\nishdr=%d\nstate=%s\nevals=%u\nfailed=%d\n"
        "in=%ux%u\nout=%ux%u\nhdr=%d\nwant=%d\njitter=%d,%d\nhistory=%d\nmotion=%ux%u\nfeature=%d\n",
        d.gpu.c_str(), d.vendor, d.ready, d.supported, d.ishdr, request.status.c_str(), d.evals, d.failed,
        d.in_w, d.in_h, d.out_w, d.out_h, d.swap_hdr, d.want_hdr, d.jitter_x, d.jitter_y, d.history,
        d.motion_w, d.motion_h, d.feature);
    return 0;
}

void ngx_publish(uint32_t in_w, uint32_t in_h, uint32_t out_w, uint32_t out_h, bool count, bool failed) {
    VkPhysicalDeviceProperties props{};
    if (physical) vkGetPhysicalDeviceProperties(physical, &props);
    auto& request = ngx_request();
    std::lock_guard<std::mutex> lock(request.mu);
    if (props.vendorID) {
        request.debug.gpu = props.deviceName[0] ? props.deviceName : "unknown";
        request.debug.vendor = props.vendorID;
    }
    request.debug.ready = ngx.ready ? 2 : (ngx.feature_library ? 1 : 0);
    request.debug.feature = ngx.feature_library ? 1 : 0;
    request.debug.ishdr = (ngx.feature && (ngx.flags & 1u)) ? 1 : 0;
    request.debug.swap_hdr = hdr_active ? 1 : 0;
    request.debug.want_hdr = (display_flags & 1) ? 1 : 0;
    request.debug.jitter_x = static_cast<int>(std::lround(guest.frame.jitter_x * 1000.f));
    request.debug.jitter_y = static_cast<int>(std::lround(guest.frame.jitter_y * 1000.f));
    request.debug.history = guest.frame.history_valid ? 1 : 0;
    request.debug.motion_w = world.motion.view ? world.motion_w : 0;
    request.debug.motion_h = world.motion.view ? world.motion_h : 0;
    if (in_w && in_h && out_w && out_h) {
        request.debug.in_w = in_w;
        request.debug.in_h = in_h;
        request.debug.out_w = out_w;
        request.debug.out_h = out_h;
    }
    if (ngx.ready) request.debug.supported = 1;
    if (failed) request.debug.failed = 1;
    else if (count) request.debug.failed = 0;
    if (count) ++request.debug.evals;
}

static void ngx_set_status(const std::string& text) {
    auto& request = ngx_request();
    std::lock_guard<std::mutex> lock(request.mu);
    request.status = text;
}

static int ngx_quality(uint32_t mode) {
    switch (mode) {
        case 1: return 1;
        case 2: return 0;
        case 3: return 3;
        case 4: return 5;
        default: return 2;
    }
}

static float ngx_scale(uint32_t mode) {
    switch (mode) {
        case 1: return 0.58f;
        case 2: return 0.5f;
        case 3: return 1.f / 3.f;
        case 4: return 1.f;
        default: return 2.f / 3.f;
    }
}

void ngx_apply_scale() {
    auto& request = ngx_request();
    std::lock_guard<std::mutex> lock(request.mu);
    if (!request.enabled || request.directory.empty()) return;
    float scale = ngx_scale(request.mode);
    if (scale < 0.25f) scale = 0.25f;
    if (scale > 1.f) scale = 1.f;
    render_fraction = scale;
}

bool ngx_active() {
    auto& request = ngx_request();
    std::lock_guard<std::mutex> lock(request.mu);
    return request.enabled && !request.directory.empty();
}

static std::wstring ngx_widen(const std::string& text) {
    std::wstring wide;
#if defined(_WIN32)
    if (text.empty()) return wide;
    int count = MultiByteToWideChar(CP_UTF8, 0, text.c_str(), -1, nullptr, 0);
    if (count <= 1) return wide;
    wide.resize(static_cast<size_t>(count));
    MultiByteToWideChar(CP_UTF8, 0, text.c_str(), -1, wide.data(), count);
    while (!wide.empty() && wide.back() == L'\0') wide.pop_back();
#else
    wide.assign(text.begin(), text.end());
#endif
    return wide;
}

static void* ngx_preload_feature(const std::wstring& directory) {
#if defined(_WIN32)
    auto path = directory + L"\\nvngx_dlss.dll";
    std::error_code ec;
    if (!std::filesystem::exists(path, ec)) return nullptr;
    return LoadLibraryExW(path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
#else
    std::error_code ec;
    std::filesystem::path dir(std::string(directory.begin(), directory.end()));
    for (auto it = std::filesystem::directory_iterator(dir, ec); !ec && it != std::filesystem::directory_iterator(); it.increment(ec)) {
        auto name = it->path().filename().string();
        if (name.rfind("libnvidia-ngx-dlss.so", 0) != 0) continue;
        return dlopen(it->path().c_str(), RTLD_NOW | RTLD_LOCAL);
    }
    return nullptr;
#endif
}

void ngx_release_feature() {
    if (ngx.feature) {
        using Release = int (*)(void*);
        if (auto release = reinterpret_cast<Release>(ngx_symbol("NVSDK_NGX_VULKAN_ReleaseFeature"))) release(ngx.feature);
        ngx.feature = nullptr;
    }
    ngx.in_w = ngx.in_h = ngx.out_w = ngx.out_h = 0;
}

void ngx_release_parameters() {
    using Destroy = int (*)(void*);
    auto destroy = reinterpret_cast<Destroy>(ngx_symbol("NVSDK_NGX_VULKAN_DestroyParameters"));
    if (destroy && ngx.eval_params) destroy(ngx.eval_params);
    if (destroy && ngx.caps) destroy(ngx.caps);
    ngx.eval_params = ngx.caps = nullptr;
}

bool ngx_init(const std::wstring& directory, uint32_t preset) {
    if (ngx.ready && ngx.directory == directory) return true;
    using Shutdown = int (*)(VkDevice);
    if (ngx.ready) {
        ngx_release_feature();
        ngx_release_parameters();
        if (auto shutdown = reinterpret_cast<Shutdown>(ngx_symbol("NVSDK_NGX_VULKAN_Shutdown1"))) shutdown(device);
        ngx.ready = false;
        ngx.caps = ngx.eval_params = nullptr;
    }
    ngx.directory = directory;
    if (!ngx.feature_library) ngx.feature_library = ngx_preload_feature(directory);
    ngx.log_path = directory;
    ngx.path_hold = directory;
    ngx.path_slot = ngx.path_hold.c_str();
    std::memset(ngx.info, 0, sizeof(ngx.info));
    NgxFeatureInfo info{};
    info.paths.path = &ngx.path_slot;
    info.paths.length = 1;
    std::memcpy(ngx.info, &info, sizeof(info));
    using InitExt = int (*)(const char*, int, const char*, const wchar_t*, VkInstance, VkPhysicalDevice, VkDevice, PFN_vkGetInstanceProcAddr, PFN_vkGetDeviceProcAddr, unsigned, const void*);
    using InitShort = int (*)(const char*, int, const char*, const wchar_t*, VkInstance, VkPhysicalDevice, VkDevice, unsigned, const void*);
    int result = 0xBAD00000;
    if (auto init = reinterpret_cast<InitExt>(ngx_symbol("NVSDK_NGX_VULKAN_Init_ProjectID_Ext")))
        result = init("8e4c2a71-6b9d-4f13-9c1a-7f2e5b90d4c3", 0, "1.0.0", ngx.log_path.c_str(), instance, physical, device, vkGetInstanceProcAddr, vkGetDeviceProcAddr, 0x15u, ngx.info);
    else if (auto init = reinterpret_cast<InitShort>(ngx_symbol("NVSDK_NGX_VULKAN_Init_ProjectID")))
        result = init("8e4c2a71-6b9d-4f13-9c1a-7f2e5b90d4c3", 0, "1.0.0", ngx.log_path.c_str(), instance, physical, device, 0x15u, ngx.info);
    else { ngx_publish(0, 0, 0, 0, false, false); ngx_set_status("driver Vulkan NGX exports missing"); return false; }
    if (!ngx_ok(result)) {
        char text[96];
        std::snprintf(text, sizeof(text), "NGX init failed (0x%08X)", static_cast<unsigned>(result));
        ngx_publish(0, 0, 0, 0, false, false);
        ngx_set_status(text);
        return false;
    }
    using GetCaps = int (*)(void**);
    using Alloc = int (*)(void**);
    auto caps_fn = reinterpret_cast<GetCaps>(ngx_symbol("NVSDK_NGX_VULKAN_GetCapabilityParameters"));
    auto alloc = reinterpret_cast<Alloc>(ngx_symbol("NVSDK_NGX_VULKAN_AllocateParameters"));
    void* caps = nullptr;
    auto shutdown = reinterpret_cast<Shutdown>(ngx_symbol("NVSDK_NGX_VULKAN_Shutdown1"));
    if (!caps_fn || !ngx_ok(caps_fn(&caps)) || !caps) { if (shutdown) shutdown(device); ngx_publish(0, 0, 0, 0, false, false); ngx_set_status("NGX capability parameters missing"); return false; }
    ngx.caps = caps;
    void* eval = nullptr;
    if (!alloc || !ngx_ok(alloc(&eval)) || !eval) { ngx_release_parameters(); if (shutdown) shutdown(device); ngx_publish(0, 0, 0, 0, false, false); ngx_set_status("NGX eval parameters missing"); return false; }
    ngx.eval_params = eval;
    auto vt = *reinterpret_cast<void***>(caps);
    auto get_int = reinterpret_cast<int (*)(void*, const char*, int*)>(vt[11]);
    int available = 0;
    if (get_int && ngx_ok(get_int(caps, "SuperSampling.Available", &available)) && available == 0) {
        ngx_release_parameters(); if (shutdown) shutdown(device);
        { auto& request = ngx_request(); std::lock_guard<std::mutex> lock(request.mu); request.debug.supported = 2; }
        ngx_publish(0, 0, 0, 0, false, false);
        ngx_set_status("NGX super sampling unavailable"); return false;
    }
    auto set_uint = reinterpret_cast<void (*)(void*, const char*, unsigned)>(vt[4]);
    if (set_uint) {
        const char* names[]{"DLSS.Hint.Render.Preset.DLAA", "DLSS.Hint.Render.Preset.Quality", "DLSS.Hint.Render.Preset.Balanced", "DLSS.Hint.Render.Preset.Performance", "DLSS.Hint.Render.Preset.UltraPerformance", "DLSS.Hint.Render.Preset.UltraQuality"};
        for (auto name : names) set_uint(caps, name, preset);
    }
    ngx.ready = true;
    ngx_publish(0, 0, 0, 0, false, false);
    ngx_set_status(ngx.feature_library ? "NGX ready" : "NGX ready, feature library not preloaded");
    return true;
}

static NgxResource ngx_resource(VkImageView view, VkImage image, VkFormat format, uint32_t width, uint32_t height, VkImageAspectFlags aspect, bool write) {
    NgxResource resource{};
    resource.image.view = view;
    resource.image.image = image;
    resource.image.range = {aspect, 0, 1, 0, 1};
    resource.image.format = format;
    resource.image.width = width;
    resource.image.height = height;
    resource.type = 0;
    resource.read_write = write ? 1 : 0;
    return resource;
}

bool ngx_evaluate(VkCommandBuffer cmd, uint32_t slot, uint32_t in_w, uint32_t in_h, uint32_t out_w, uint32_t out_h, PostImage* tonemap) {
    // DX11 evaluates the image Anomaly just finished at AfterTonemap.
    // That image is sRGB UNORM. Compose decodes it. Pre-tonemap world.hdr is not this input.
    bool tonemapped = tonemap && tonemap->image.view && tonemap->image.handle &&
        tonemap->width == in_w && tonemap->height == in_h;
    if (!tonemapped) return false;
    VkImageView color_view = tonemap->image.view;
    VkImage color_image = tonemap->image.handle;
    VkFormat color_format = VK_FORMAT_R8G8B8A8_UNORM;
    if (!cmd || !in_w || !in_h || !out_w || !out_h || !world.gbuffer[3].view) return false;
    std::wstring directory;
    uint32_t mode = 0, preset = 11;
    uint64_t revision = 0;
    {
        auto& request = ngx_request();
        std::lock_guard<std::mutex> lock(request.mu);
        if (!request.enabled || request.directory.empty() || !ngx_same(request.owner, "se-dlss")) return false;
        directory = ngx_widen(request.directory);
        mode = request.mode;
        preset = request.preset;
        revision = request.revision;
    }
    if (directory.empty()) return false;
    if (ngx.revision != revision) { ngx.fail_frames = 0; ngx.revision = revision; if (ngx.directory != directory) ngx.ready = false; }
    if (ngx.fail_frames) { --ngx.fail_frames; return false; }
    if (!ngx_init(directory, preset)) { ngx.fail_frames = 60; return false; }
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
    if (!mv_view || !mv_image || !mv_w || !mv_h) { ngx_publish(in_w, in_h, out_w, out_h, false, false); ngx_set_status("NGX waiting for Anomaly velocity"); return false; }
    int quality = ngx_quality(mode);
    // MVLowRes | DepthInverted | AutoExposure. AfterTonemap color is LDR, so IsHDR stays off.
    int flags = 2 | 8 | 64;
    using Create = int (*)(VkCommandBuffer, int, void*, void**);
    using Eval = int (*)(VkCommandBuffer, void*, void*, void*);
    if (!ngx.feature || ngx.quality != static_cast<uint32_t>(quality) || ngx.flags != static_cast<uint32_t>(flags) || ngx.in_w != in_w || ngx.in_h != in_h || ngx.out_w != out_w || ngx.out_h != out_h) {
        ngx_release_feature();
        auto vt = *reinterpret_cast<void***>(ngx.caps);
        auto set_int = reinterpret_cast<void (*)(void*, const char*, int)>(vt[3]);
        auto set_uint = reinterpret_cast<void (*)(void*, const char*, unsigned)>(vt[4]);
        set_uint(ngx.caps, "Width", in_w);
        set_uint(ngx.caps, "Height", in_h);
        set_uint(ngx.caps, "OutWidth", out_w);
        set_uint(ngx.caps, "OutHeight", out_h);
        set_int(ngx.caps, "PerfQualityValue", quality);
        set_int(ngx.caps, "DLSS.Feature.Create.Flags", flags);
        set_int(ngx.caps, "DLSS.Enable.Output.Subrects", 0);
        auto create = reinterpret_cast<Create>(ngx_symbol("NVSDK_NGX_VULKAN_CreateFeature"));
        void* handle = nullptr;
        int result = create ? create(cmd, 1, ngx.caps, &handle) : 0xBAD00000;
        if (!ngx_ok(result) || !handle) {
            char text[96];
            std::snprintf(text, sizeof(text), "NGX CreateFeature failed (0x%08X)", static_cast<unsigned>(result));
            ngx_publish(in_w, in_h, out_w, out_h, false, true);
            ngx_set_status(text);
            ngx.fail_frames = 60;
            return false;
        }
        ngx.feature = handle;
        ngx.quality = static_cast<uint32_t>(quality);
        ngx.flags = static_cast<uint32_t>(flags);
        ngx.in_w = in_w; ngx.in_h = in_h; ngx.out_w = out_w; ngx.out_h = out_h;
    }
    auto& dest = display_state.color[slot & 1];
    bool first = !display_state.visited[slot & 1];
    display_state.visited[slot & 1] = 1;
    barrier(cmd, dest.handle, first ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_ASPECT_COLOR_BIT,
        first ? VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT : VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_2_TRANSFER_BIT,
        first ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT | VK_ACCESS_2_TRANSFER_READ_BIT,
        // NGX also clears output through transfer commands during evaluation.
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_2_TRANSFER_BIT,
        VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT | VK_ACCESS_2_SHADER_STORAGE_READ_BIT | VK_ACCESS_2_TRANSFER_WRITE_BIT);
    if (!ngx.eval_params) { ngx_publish(in_w, in_h, out_w, out_h, false, true); ngx_set_status("NGX eval parameters missing"); ngx.fail_frames = 60; return false; }
    barrier(cmd, color_image, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    void* params = ngx.eval_params;
    auto vt = *reinterpret_cast<void***>(params);
    auto set_void = reinterpret_cast<void (*)(void*, const char*, void*)>(vt[0]);
    auto set_int = reinterpret_cast<void (*)(void*, const char*, int)>(vt[3]);
    auto set_uint = reinterpret_cast<void (*)(void*, const char*, unsigned)>(vt[4]);
    auto set_float = reinterpret_cast<void (*)(void*, const char*, float)>(vt[6]);
    auto reset = reinterpret_cast<void (*)(void*)>(vt[16]);
    reset(params);
    NgxResource color = ngx_resource(color_view, color_image, color_format, in_w, in_h, VK_IMAGE_ASPECT_COLOR_BIT, false);
    NgxResource output = ngx_resource(dest.view, dest.handle, VK_FORMAT_R16G16B16A16_SFLOAT, out_w, out_h, VK_IMAGE_ASPECT_COLOR_BIT, true);
    NgxResource depth = ngx_resource(world.gbuffer[3].view, world.gbuffer[3].handle, VK_FORMAT_D32_SFLOAT, in_w, in_h, VK_IMAGE_ASPECT_DEPTH_BIT, false);
    NgxResource motion = ngx_resource(mv_view, mv_image, VK_FORMAT_R16G16_SFLOAT, mv_w, mv_h, VK_IMAGE_ASPECT_COLOR_BIT, false);
    set_void(params, "Color", &color);
    set_void(params, "Output", &output);
    set_void(params, "Depth", &depth);
    set_void(params, "MotionVectors", &motion);
    VkImageView reactive_view{}; VkImage reactive_image{}; uint32_t reactive_w = 0, reactive_h = 0;
    NgxResource reactive{};
    if (guest_catalog_view(kGuestCatalogReactiveMask, reactive_view, reactive_image, reactive_w, reactive_h) && reactive_w == in_w && reactive_h == in_h) {
        barrier(cmd, reactive_image, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        reactive = ngx_resource(reactive_view, reactive_image, VK_FORMAT_R8G8B8A8_UNORM, reactive_w, reactive_h, VK_IMAGE_ASPECT_COLOR_BIT, false);
        set_void(params, "DLSS.Input.Bias.Current.Color.Mask", &reactive);
    }
    set_float(params, "Jitter.Offset.X", guest.frame.jitter_x);
    set_float(params, "Jitter.Offset.Y", guest.frame.jitter_y);
    set_float(params, "Sharpness", 0.f);
    set_int(params, "Reset", guest.frame.history_valid ? 0 : 1);
    set_float(params, "MV.Scale.X", float(in_w) / float(mv_w));
    set_float(params, "MV.Scale.Y", float(in_h) / float(mv_h));
    set_uint(params, "DLSS.Render.Subrect.Dimensions.Width", in_w);
    set_uint(params, "DLSS.Render.Subrect.Dimensions.Height", in_h);
    auto evaluate = reinterpret_cast<Eval>(ngx_symbol("NVSDK_NGX_VULKAN_EvaluateFeature"));
    int result = evaluate ? evaluate(cmd, ngx.feature, params, nullptr) : 0xBAD00000;
    barrier(cmd, dest.handle, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT | VK_ACCESS_2_TRANSFER_WRITE_BIT,
        VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT | VK_ACCESS_2_TRANSFER_READ_BIT);
    if (!ngx_ok(result)) {
        char text[96];
        std::snprintf(text, sizeof(text), "NGX evaluate failed (0x%08X)", static_cast<unsigned>(result));
        ngx_publish(in_w, in_h, out_w, out_h, false, true);
        ngx_set_status(text);
        ngx.fail_frames = 60;
        return false;
    }
    ngx_publish(in_w, in_h, out_w, out_h, true, false);
    ngx_set_status("NGX evaluating");
    return true;
}

void ngx_from_tonemap(VkCommandBuffer cmd) {
    display_state.upscale_ready = false;
    if (!ngx_active()) return;
    if (!ngx_owned()) { ngx_set_status("NGX waiting for Anomaly claim"); return; }
    auto* tone = post.after_tonemap;
    if (!cmd || !tone || !tone->image.view || !tone->width || !tone->height) return;
    uint32_t out_w = std::max(1u, output_extent.width ? output_extent.width : extent.width);
    uint32_t out_h = std::max(1u, output_extent.height ? output_extent.height : extent.height);
    bool resized = display_state.width != out_w || display_state.height != out_h;
    display_state.color[0] = display_target(display_state.color[0], out_w, out_h);
    display_state.color[1] = display_target(display_state.color[1], out_w, out_h);
    if (resized) {
        display_state.ready = false;
        display_state.visited[0] = display_state.visited[1] = display_state.visited[2] = 0;
    }
    display_state.width = out_w;
    display_state.height = out_h;
    uint32_t write = display_state.index & 1u;
    if (!ngx_evaluate(cmd, write, tone->width, tone->height, out_w, out_h, tone)) return;
    display_state.upscale_view = display_state.color[write].view;
    display_state.upscale_handle = display_state.color[write].handle;
    display_state.upscale_w = out_w;
    display_state.upscale_h = out_h;
    display_state.upscale_ready = true;
}

void cleanup_ngx() noexcept {
    ngx_release_feature();
    // Parameter maps require an initialized NGX instance; release them before
    // Shutdown1 tears down the driver allocator and its device state.
    ngx_release_parameters();
    if (ngx.ready) {
        using Shutdown = int (*)(VkDevice);
        if (auto shutdown = reinterpret_cast<Shutdown>(ngx_symbol("NVSDK_NGX_VULKAN_Shutdown1"))) shutdown(device);
    }
    ngx = {};
}
