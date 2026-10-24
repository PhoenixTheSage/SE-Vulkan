// HDR display contract (Codex audit P4): display detection/state, tearing and low-latency presentation, and the
// raw FP16 presentation capture behind HDR screenshots. Mirrors HdrRender's DX11 companion: DXGI Output6 for the
// window's monitor, AllowTearing only without vsync, and MaximumFrameLatency 1 for low-latency mode.
void* native_window{};
SevkDisplayInfo display_info_cache{};uint32_t display_info_generation=UINT32_MAX;
#if defined(_WIN32)
bool query_dxgi_output(SevkDisplayInfo& info) {
    if (!native_window) return false;
    HMONITOR monitor = MonitorFromWindow(static_cast<HWND>(native_window), MONITOR_DEFAULTTONEAREST);
    HMODULE dxgi = LoadLibraryW(L"dxgi.dll"); if (!dxgi) return false;
    using CreateFactory = HRESULT(WINAPI*)(REFIID, void**);
    auto create = reinterpret_cast<CreateFactory>(GetProcAddress(dxgi, "CreateDXGIFactory1"));
    IDXGIFactory1* factory = nullptr; if (!create || FAILED(create(__uuidof(IDXGIFactory1), reinterpret_cast<void**>(&factory)))) return false;
    bool found = false;
    for (UINT a = 0; !found; ++a) {
        IDXGIAdapter1* adapter = nullptr; if (factory->EnumAdapters1(a, &adapter) == DXGI_ERROR_NOT_FOUND) break;
        for (UINT o = 0; !found; ++o) {
            IDXGIOutput* output = nullptr; if (adapter->EnumOutputs(o, &output) == DXGI_ERROR_NOT_FOUND) break;
            IDXGIOutput6* output6 = nullptr;
            if (SUCCEEDED(output->QueryInterface(__uuidof(IDXGIOutput6), reinterpret_cast<void**>(&output6)))) {
                DXGI_OUTPUT_DESC1 desc{};
                if (SUCCEEDED(output6->GetDesc1(&desc)) && desc.Monitor == monitor) {
                    found = true; info.display_known = 1; info.display_hdr10 = desc.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ? 1u : 0u;
                    info.bits_per_color = desc.BitsPerColor; info.max_luminance = desc.MaxLuminance; info.max_full_frame_luminance = desc.MaxFullFrameLuminance; info.min_luminance = desc.MinLuminance;
                }
                output6->Release();
            }
            output->Release();
        }
        adapter->Release();
    }
    factory->Release();
    return found;
}
#endif
SevkDisplayInfo display_info() {
    SevkDisplayInfo info{}; info.size = sizeof(info); info.abi_version = SEVK_ABI_VERSION;
    info.hdr_active = hdr_active ? 1u : 0u; info.tenant_graded = post.display_graded ? 1u : 0u;
    info.present_mode = swapchain ? uint32_t(actual_present_mode) : UINT32_MAX;
    info.tearing_active = swapchain && actual_present_mode == VK_PRESENT_MODE_IMMEDIATE_KHR ? 1u : 0u;
    info.low_latency = (display_flags & 64) ? 1u : 0u;
    if (surface) {
        uint32_t count = 0;
        if (vkGetPhysicalDeviceSurfaceFormatsKHR(physical, surface, &count, nullptr) == VK_SUCCESS && count) {
            std::vector<VkSurfaceFormatKHR> formats(count); vkGetPhysicalDeviceSurfaceFormatsKHR(physical, surface, &count, formats.data());
            for (const auto& f : formats) if (f.format == VK_FORMAT_R16G16B16A16_SFLOAT && f.colorSpace == VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT) info.hdr_capable = 1;
        }
        count = 0;
        if (vkGetPhysicalDeviceSurfacePresentModesKHR(physical, surface, &count, nullptr) == VK_SUCCESS && count) {
            std::vector<VkPresentModeKHR> modes(count); vkGetPhysicalDeviceSurfacePresentModesKHR(physical, surface, &count, modes.data());
            info.tearing_supported = std::find(modes.begin(), modes.end(), VK_PRESENT_MODE_IMMEDIATE_KHR) != modes.end() ? 1u : 0u;
        }
    }
    // DXGI output enumeration is not free; refresh it with each swapchain generation.
    if (display_info_generation != swapchain_generation) {
        display_info_cache = SevkDisplayInfo{};
#if defined(_WIN32)
        query_dxgi_output(display_info_cache);
#endif
        display_info_generation = swapchain_generation;
    }
    info.display_known = display_info_cache.display_known; info.display_hdr10 = display_info_cache.display_hdr10; info.bits_per_color = display_info_cache.bits_per_color;
    info.max_luminance = display_info_cache.max_luminance; info.max_full_frame_luminance = display_info_cache.max_full_frame_luminance; info.min_luminance = display_info_cache.min_luminance;
    return info;
}
// Raw FP16 scRGB presentation capture for HDR screenshots (EXR plus a paper-white SDR conversion managed-side).
void readback_hdr(void* data, uint64_t bytes) {
    if (recording || !last_image_valid || !readback_supported || !data || color_format != VK_FORMAT_R16G16B16A16_SFLOAT || bytes != uint64_t(extent.width) * extent.height * 8)
        throw std::runtime_error("HDR readback requires a completed FP16 frame and an exact RGBA16F buffer");
    if (!capture_valid) throw std::runtime_error("HDR readback requires a requested presentation capture");
    wait(capture_completion); check(vmaInvalidateAllocation(allocator, capture.memory, 0, capture.bytes), "invalidate HDR presentation capture");
    std::memcpy(data, capture.mapped, static_cast<size_t>(bytes)); capture_valid = false;
}
