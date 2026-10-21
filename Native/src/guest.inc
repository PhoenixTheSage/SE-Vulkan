struct GuestPipeline {
    uint64_t executions{}, last_completion{};
    uint32_t last_frame{UINT32_MAX};
    VkPipeline pipeline{}; VkPipelineLayout layout{}; VkDescriptorSetLayout set{};
    VkShaderModule vertex{}, pixel{};
    VkPipeline draw[7][6]{};
    std::vector<std::pair<uint32_t, VkDescriptorType>> bindings;
    uint32_t descriptors{}, owned_bytes{}, extras_bytes{}, uniform_bytes{};
    uint32_t group_x{8}, group_y{8};
    uint32_t group_z{1}, contract{}, constant_bytes[8]{};
    VkFormat output_formats[5]{};
    uint32_t input_dims[48]{}, input_arrayed[48]{}, output_dims[5]{};
    bool bus{}, compute{}, geometry{};
};
struct GuestImage {
    uint32_t specification{};
    Image image{}; uint32_t width{}, height{}, depth{1}, mips{1}, bpp{4}, layers{1};
    std::array<Buffer,3> storage{};
    std::vector<uint8_t> pending;
    uint64_t revision{}, uploaded[3]{}, produced_epoch{};
    bool is_buffer{};
    VkImageAspectFlags aspect{VK_IMAGE_ASPECT_COLOR_BIT};
    VkFormat format{VK_FORMAT_R8G8B8A8_UNORM}; VkImageLayout layout{VK_IMAGE_LAYOUT_UNDEFINED};
};
struct GuestPass {
    uint32_t operation{};
    uint64_t source{};
    float value[4]{};
    uint32_t mark{}, compose{}, width{}, height{}, uniform_bytes{}, flags{};
    uint64_t pipeline{}, storage{}; uint8_t uniforms[256]{}; uint64_t sampled[48]{};
    uint32_t depth{1}; bool extended{};
    uint64_t outputs[5]{};
    std::vector<uint8_t> constants[8];
};
struct GuestBump { Buffer buffer{}; uint32_t used{}; };
struct GuestSnapshot { Image image{};uint32_t width{},height{}; };
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
static_assert(sizeof(SevkGuestPass2) == 568, "extended guest packet is 568 bytes");
struct GuestState {
    bool resources_open{};
    uint32_t depth_count{},depth_ready{};
    bool depth_configured{};
    uint64_t depth_images[3]{};
    float depth_matrices[3][16]{};
    std::vector<GuestPass> passes;
    std::unordered_map<uint64_t, GuestPipeline> pipelines;
    std::unordered_map<uint64_t, GuestImage> images;
    std::vector<std::pair<uint64_t, GuestImage>> retired_images;
    std::vector<std::pair<uint64_t, GuestPipeline>> retired_pipelines;
    uint64_t next_pipeline{1}, replay_count{}, frame_stamp{}, catalog_epoch{1};
    VkDescriptorPool pools[3]{};
    uint64_t pool_stamp[3]{};
    GuestBump bumps[3]{};
    VkDeviceSize uniform_alignment{1};
    VkSampler samplers[6]{};
    Image dummy{};
    GuestSnapshot hdr_snapshot{},upscale_snapshot{};
    std::vector<Image> retired_snapshots[3];
    float prev_vp[16]{}, prev_camera[3]{};
    bool have_prev{};
    SevkGuestFrame frame{};
    float skip_floor{.38f},skip_mul{6.f};
} guest;

void guest_destroy_pipeline(GuestPipeline& pipeline) {
    bool separate = pipeline.pipeline && (!(pipeline.bus && !pipeline.compute) || pipeline.contract == 2);
    if (separate) vkDestroyPipeline(device, pipeline.pipeline, nullptr);
    for (auto& row : pipeline.draw) for (auto pipe : row) if (pipe) vkDestroyPipeline(device, pipe, nullptr);
    if (pipeline.layout) vkDestroyPipelineLayout(device, pipeline.layout, nullptr);
    if (pipeline.set) vkDestroyDescriptorSetLayout(device, pipeline.set, nullptr);
    if (pipeline.vertex) vkDestroyShaderModule(device, pipeline.vertex, nullptr);
    if (pipeline.pixel) vkDestroyShaderModule(device, pipeline.pixel, nullptr);
}
void guest_destroy_image(GuestImage& image) {
    if (image.is_buffer) { for (auto& buffer : image.storage) destroy_buffer(buffer); }
    else destroy_image(image.image);
}
void guest_collect(uint64_t complete) {
    for (auto it=guest.retired_images.begin();it!=guest.retired_images.end();)
        if(it->first<=complete){guest_destroy_image(it->second);it=guest.retired_images.erase(it);}else ++it;
    for (auto it=guest.retired_pipelines.begin();it!=guest.retired_pipelines.end();)
        if(it->first<=complete){guest_destroy_pipeline(it->second);it=guest.retired_pipelines.erase(it);}else ++it;
}
void guest_resource_command(const SevkGuestResourceCommand& command) {
    if (recording && !guest.resources_open) throw std::runtime_error("guest resource command outside allocation window");
    if(recording && (command.operation==1 || command.operation==2))
        for(const auto& pass:guest.passes) {
            bool used=command.operation==2?pass.pipeline==command.image:pass.storage==command.image;
            if(command.operation==1){for(auto id:pass.sampled)used|=id==command.image;for(auto id:pass.outputs)used|=id==command.image;}
            if(used)throw std::runtime_error("retire guest resources before queuing frame commands");
        }
    uint64_t completion=next_completion+(recording?1:0);
    if(command.operation==1){auto it=guest.images.find(command.image);if(it!=guest.images.end()){guest.retired_images.emplace_back(completion,std::move(it->second));guest.images.erase(it);}return;}
    if(command.operation==2){auto it=guest.pipelines.find(command.image);if(it!=guest.pipelines.end()){guest.retired_pipelines.emplace_back(completion,std::move(it->second));guest.pipelines.erase(it);}return;}
    if(command.operation!=3 || command.mark<1 || command.mark>6) throw std::runtime_error("unsupported guest resource command");
    auto it=guest.images.find(command.image);
    if(it==guest.images.end() || it->second.is_buffer || it->second.aspect!=VK_IMAGE_ASPECT_COLOR_BIT) throw std::runtime_error("guest clear requires a color image");
    for(float value:command.value)if(!std::isfinite(value))throw std::runtime_error("nonfinite guest clear");
    GuestPass pass{};pass.operation=3;pass.mark=command.mark;pass.storage=command.image;
    std::memcpy(pass.value,command.value,16);guest.passes.push_back(std::move(pass));
}

void guest_depth_views(const uint64_t* images,const float* matrices,uint32_t count) {
    if(recording || count>3 || (count && (!images || !matrices))) throw std::runtime_error("invalid guest scene-depth view batch");
    for(uint32_t i=0;i<count;i++) {
        auto image=guest.images.find(images[i]);
        if(image==guest.images.end() || image->second.is_buffer || image->second.format!=VK_FORMAT_D32_SFLOAT || image->second.depth!=1 || image->second.layers!=1 || image->second.mips!=1)
            throw std::runtime_error("guest scene-depth target must be single-level D32");
        for(uint32_t j=0;j<i;j++) if(images[i]==images[j]) throw std::runtime_error("duplicate guest scene-depth target");
        for(uint32_t j=0;j<16;j++) if(!std::isfinite(matrices[i*16+j])) throw std::runtime_error("nonfinite guest scene-depth matrix");
    }
    guest.depth_count=count;guest.depth_ready=0;guest.depth_configured=count!=0;
    if(count) { std::memcpy(guest.depth_images,images,count*sizeof(uint64_t));std::memcpy(guest.depth_matrices,matrices,count*64); }
}

void cleanup_guest() {
    for(auto& retired:guest.retired_images)guest_destroy_image(retired.second);
    for(auto& retired:guest.retired_pipelines)guest_destroy_pipeline(retired.second);
    for (auto& item : guest.pipelines) {
        bool separate = item.second.pipeline && (!(item.second.bus && !item.second.compute) || item.second.contract == 2);
        if (separate) vkDestroyPipeline(device, item.second.pipeline, nullptr);
        for (auto& row : item.second.draw) for (auto pipe : row) if (pipe) vkDestroyPipeline(device, pipe, nullptr);
        if (item.second.layout) vkDestroyPipelineLayout(device, item.second.layout, nullptr);
        if (item.second.set) vkDestroyDescriptorSetLayout(device, item.second.set, nullptr);
        if (item.second.vertex) vkDestroyShaderModule(device, item.second.vertex, nullptr);
        if (item.second.pixel) vkDestroyShaderModule(device, item.second.pixel, nullptr);
    }
    for (auto& item : guest.images) { if (item.second.is_buffer) { for (auto& buffer : item.second.storage) destroy_buffer(buffer); } else destroy_image(item.second.image); }
    for (auto pool : guest.pools) if (pool) vkDestroyDescriptorPool(device, pool, nullptr);
    for (auto& bump : guest.bumps) if (bump.buffer.handle) destroy_buffer(bump.buffer);
    for (auto sampler : guest.samplers) if (sampler) vkDestroySampler(device, sampler, nullptr);
    if (guest.dummy.handle) destroy_image(guest.dummy);
    destroy_image(guest.hdr_snapshot.image);destroy_image(guest.upscale_snapshot.image);
    for(auto& retired:guest.retired_snapshots)for(auto& image:retired)destroy_image(image);
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
    if (stored.contract) {
        if (type == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER && binding.binding < 8 && binding.block.size > 0 && binding.block.size <= 4096) {
            stored.constant_bytes[binding.binding] = binding.block.size; return true;
        }
        if (type == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE && binding.binding >= 128 && binding.binding < 176) {
            stored.input_dims[binding.binding - 128] = binding.image.dim;
            stored.input_arrayed[binding.binding - 128] = binding.image.arrayed;
            return binding.image.dim == SpvDim2D || binding.image.dim == SpvDim3D;
        }
        if (type == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER) return binding.binding >= 128 && binding.binding < 176;
        if (type == VK_DESCRIPTOR_TYPE_SAMPLER) return binding.binding >= 256 && binding.binding < 262;
        if (compute && type == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE && binding.binding >= 384 && binding.binding < 389) {
            VkFormat format = binding.image.image_format == SpvImageFormatRgba16f ? VK_FORMAT_R16G16B16A16_SFLOAT
                : binding.image.image_format == SpvImageFormatR16f ? VK_FORMAT_R16_SFLOAT
                : binding.image.image_format == SpvImageFormatR32f ? VK_FORMAT_R32_SFLOAT
                : binding.image.image_format == SpvImageFormatRgba32f ? VK_FORMAT_R32G32B32A32_SFLOAT
                : binding.image.image_format == SpvImageFormatRg16f ? VK_FORMAT_R16G16_SFLOAT
                : binding.image.image_format == SpvImageFormatRgba8 ? VK_FORMAT_R8G8B8A8_UNORM : VK_FORMAT_UNDEFINED;
            stored.output_formats[binding.binding - 384] = format;
            stored.output_dims[binding.binding - 384] = binding.image.dim;
            return format != VK_FORMAT_UNDEFINED && !binding.image.arrayed && (binding.image.dim == SpvDim2D || binding.image.dim == SpvDim3D);
        }
        return false;
    }
    if (type == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER) {
        // Packs share the same stock Frame.hlsli as the world renderer (1280 B).
        // The smaller b0 layouts remain reserved for Anomaly's owned passes.
        if (binding.binding == 0 && (binding.block.size <= 256 || binding.block.size == 1280)) { stored.owned_bytes = binding.block.size; return true; }
        if (binding.binding == 6 && binding.block.size <= 320) { stored.extras_bytes = binding.block.size; return true; }
        if (binding.binding == 7 && binding.block.size <= 256) { stored.uniform_bytes = binding.block.size; return true; }
        return false;
    }
    if (type == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE) return binding.binding >= 128 && binding.binding <= 141;
    if (type == VK_DESCRIPTOR_TYPE_SAMPLER) return binding.binding >= 256 && binding.binding <= 258;
    if (type == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER) return binding.binding == 128 || binding.binding == 129 || (binding.binding >= 135 && binding.binding <= 141);
    return compute && type == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE && binding.binding == 384;
}

void guest_ensure_samplers() {
    if (guest.samplers[0]) return;
    VkPhysicalDeviceProperties properties{};
    vkGetPhysicalDeviceProperties(physical, &properties);
    guest.uniform_alignment = std::max<VkDeviceSize>(1, properties.limits.minUniformBufferOffsetAlignment);
    auto make = [&](uint32_t index, VkFilter filter, VkSamplerAddressMode address) {
        VkSamplerCreateInfo info{ VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO };
        info.magFilter = info.minFilter = filter;
        info.mipmapMode = filter == VK_FILTER_LINEAR ? VK_SAMPLER_MIPMAP_MODE_LINEAR : VK_SAMPLER_MIPMAP_MODE_NEAREST;
        info.addressModeU = info.addressModeV = info.addressModeW = address; info.maxLod = 16;
        if (index == 5) { info.compareEnable = VK_TRUE; info.compareOp = VK_COMPARE_OP_LESS_OR_EQUAL; }
        check(vkCreateSampler(device, &info, nullptr, &guest.samplers[index]), "create guest sampler");
    };
    make(0, VK_FILTER_NEAREST, VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE);
    make(1, VK_FILTER_LINEAR, VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE);
    make(2, VK_FILTER_LINEAR, VK_SAMPLER_ADDRESS_MODE_REPEAT);
    make(5, VK_FILTER_LINEAR, VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE);
}

uint64_t guest_create_pipeline(const void* vs, uint64_t vs_bytes, const char* vs_entry, const void* ps, uint64_t ps_bytes, const char* ps_entry, uint32_t contract = 0) {
    if (recording && !guest.resources_open) throw std::runtime_error("guest pipeline during world recording");
    if (!vs_entry || !ps_entry) throw std::runtime_error("guest entry point missing");
    bool compute = std::strcmp(vs_entry, "compute") == 0;
    if (contract > 2 || (compute && contract == 2)) throw std::runtime_error("invalid extended guest pipeline contract");
    SpvReflectShaderModule vr{}, pr{}; VkShaderModule vertex{}, pixel{}; VkPipelineLayout layout{}; VkDescriptorSetLayout set{};
    GuestPipeline stored{};
    try {
        if (!compute) vertex = guest_shader(vs, vs_bytes, VK_SHADER_STAGE_VERTEX_BIT, vr);
        pixel = guest_shader(ps, ps_bytes, compute ? VK_SHADER_STAGE_COMPUTE_BIT : VK_SHADER_STAGE_FRAGMENT_BIT, pr);
        uint32_t descriptors = (compute ? 0 : vr.descriptor_binding_count) + pr.descriptor_binding_count;
        stored.vertex = vertex; stored.pixel = pixel; stored.descriptors = descriptors; stored.compute = compute;
        stored.contract = contract;
        stored.geometry = !compute && std::strcmp(vs_entry, "geometry_vertex") == 0;
        if (compute) {
            const auto* entry = spvReflectGetEntryPoint(&pr, ps_entry);
            if (!entry || !entry->local_size.x || !entry->local_size.y || !entry->local_size.z || (!contract && entry->local_size.z != 1))
                throw std::runtime_error("guest compute workgroup is unsupported");
            stored.group_x = entry->local_size.x; stored.group_y = entry->local_size.y;
            stored.group_z = entry->local_size.z;
        }
        {
            // Resource-free pixel programs still need target-format and compose
            // variants. An empty descriptor layout uses the same validated bus.
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
                    if (type == VK_DESCRIPTOR_TYPE_SAMPLER) {
                        uint32_t index = binding.binding - 256;
                        if (contract) { const uint32_t map[]{1,2,0,1,1,5}; index = map[index]; }
                        sampler = &guest.samplers[index];
                    }
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
            } else if (contract == 2) {
                VkFormat formats[]{VK_FORMAT_R16G16B16A16_SFLOAT, VK_FORMAT_R16G16B16A16_SFLOAT};
                stored.pipeline = world_graphics(vertex, vs_entry, pixel, ps_entry, layout, {}, {}, formats, 2, false);
            } else {
                VkFormat formats[]{ VK_FORMAT_R8G8B8A8_UNORM, VK_FORMAT_R16G16B16A16_SFLOAT, VK_FORMAT_R32_SFLOAT, VK_FORMAT_R16G16_SFLOAT, VK_FORMAT_R32G32B32A32_SFLOAT, VK_FORMAT_B10G11R11_UFLOAT_PACK32, VK_FORMAT_R16_SFLOAT };
                for (int format = 0; format < 7; ++format)
                    for (int mode = 0; mode < (format < 2 || format==5 ? 5 : 1); ++mode) {
                        bool depth = mode == 4, additive = mode == 1;
                        uint32_t blend = mode == 2 ? 1u : mode == 3 ? 6u : 0u;
                        stored.draw[format][mode] = world_graphics(vertex, vs_entry, pixel, ps_entry, layout, {}, {}, &formats[format], 1, depth, false, additive, false, blend, depth);
                    }
                if (stored.geometry)
                    for (int format : { 2, 4 })
                        stored.draw[format][5] = world_graphics(vertex, vs_entry, pixel, ps_entry, layout, {}, {}, &formats[format], 1, false, false, false, false, 7);
                stored.pipeline = stored.draw[1][0];
            }
        }
        uint64_t id = guest.next_pipeline++;
        guest.pipelines.emplace(id, stored);
        spvReflectDestroyShaderModule(&vr); spvReflectDestroyShaderModule(&pr);
        return id;
    } catch (...) {
        spvReflectDestroyShaderModule(&vr); spvReflectDestroyShaderModule(&pr);
        bool separate = stored.pipeline && (!(stored.bus && !stored.compute) || stored.contract == 2);
        if (separate) vkDestroyPipeline(device, stored.pipeline, nullptr);
        for (auto& row : stored.draw) for (auto pipe : row) if (pipe) vkDestroyPipeline(device, pipe, nullptr);
        if (layout) vkDestroyPipelineLayout(device, layout, nullptr);
        if (set) vkDestroyDescriptorSetLayout(device, set, nullptr);
        if (vertex) vkDestroyShaderModule(device, vertex, nullptr);
        if (pixel) vkDestroyShaderModule(device, pixel, nullptr);
        throw;
    }
}

void guest_create_image(uint64_t id, uint32_t width, uint32_t height, uint32_t depth, uint32_t format) {
    if (!id || (recording && !guest.resources_open)) throw std::runtime_error("invalid guest image phase");
    uint32_t kind = format & 0xffffu, mips = std::max(1u, format >> 16);
    if (kind > 8 || (kind == 7 && (depth > 1 || mips != 1))) throw std::runtime_error("guest image format is outside the bus");
    width = std::max(1u, width); height = std::max(1u, height); depth = std::max(1u, depth);
    auto found = guest.images.find(id);
    if (found != guest.images.end()) {
        const auto& old=found->second;
        if(old.width==(kind==4?std::max(16u,width):width) && old.height==height &&
            (kind==5?old.layers:old.depth)==depth && old.specification==format)return;
    }
    GuestImage image{};
    image.specification=format;
    image.width = width; image.height = height; image.depth = depth; image.mips = mips;
    if (kind == 4) {
        image.is_buffer = true; image.width = std::max(16u, width); image.bpp = 1; image.layers = 1;
        try { for (auto& buffer : image.storage) buffer = make_buffer(image.width, VK_BUFFER_USAGE_STORAGE_BUFFER_BIT, true); }
        catch (...) { for (auto& buffer : image.storage) destroy_buffer(buffer); throw; }
        image.pending.resize(image.width); image.revision = 1;
        if(found!=guest.images.end()){guest.retired_images.emplace_back(next_completion+(recording?1:0),std::move(found->second));guest.images.erase(found);}
        guest.images.emplace(id, std::move(image)); return;
    }
    image.layers = kind == 5 ? depth : 1;
    if (kind == 5) { image.depth = 1; depth = 1; }
    image.format = kind == 8 ? VK_FORMAT_R16_SFLOAT : kind == 7 ? VK_FORMAT_D32_SFLOAT : kind == 6 ? VK_FORMAT_R32G32B32A32_SFLOAT : kind == 1 ? VK_FORMAT_R16G16B16A16_SFLOAT : kind == 2 ? VK_FORMAT_R32_SFLOAT : kind == 3 ? VK_FORMAT_R16G16_SFLOAT : VK_FORMAT_R8G8B8A8_UNORM;
    if (kind == 7) image.aspect = VK_IMAGE_ASPECT_DEPTH_BIT;
    image.bpp = kind == 8 ? 2u : kind == 6 ? 16u : kind == 1 ? 8u : 4u;
    VkImageUsageFlags usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_STORAGE_BIT;
    if (depth == 1) usage |= VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    if (kind == 7) usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT;
    if (depth == 1)
        image.image = make_image(width, height, image.format, usage, image.aspect, mips, std::max(1u, image.layers), image.layers > 1);
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
    if(found!=guest.images.end()){guest.retired_images.emplace_back(next_completion+(recording?1:0),std::move(found->second));guest.images.erase(found);}
    guest.images.emplace(id, std::move(image));
}

void guest_update_image(uint64_t id, const void* data, uint64_t bytes) {
    auto found = guest.images.find(id);
    if (found == guest.images.end() || !data) throw std::runtime_error("guest image update missing");
    auto& image = found->second;
    if (image.is_buffer) {
        if (bytes > image.width) throw std::runtime_error("guest buffer update short");
        // Copy now, upload only into the fence-protected frame slot at replay.
        // Earlier submissions retain their own mapped buffer and descriptors.
        std::memcpy(image.pending.data(), data, static_cast<size_t>(bytes)); ++image.revision;
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
            barrier(cmd, image.image.handle, image.layout, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, image.aspect,
                VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, image.layout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, image.mips, image.layers);
            std::vector<VkBufferImageCopy> copies(image.mips);
            uint64_t offset = 0;
            for (uint32_t mip = 0; mip < image.mips; ++mip) {
                uint32_t mw = std::max(1u, image.width >> mip), mh = std::max(1u, image.height >> mip), md = std::max(1u, image.depth >> mip);
                copies[mip].bufferOffset = offset; copies[mip].imageSubresource = { image.aspect, mip, 0, image.layers };
                copies[mip].imageExtent = { mw, mh, image.depth == 1 ? 1u : md };
                offset += uint64_t(mw) * mh * md * image.layers * image.bpp;
            }
            vkCmdCopyBufferToImage(cmd, staging.handle, image.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, uint32_t(copies.size()), copies.data());
            barrier(cmd, image.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, image.aspect,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, image.mips, image.layers);
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
        std::memcpy(pass.sampled, passes[i].sampled, sizeof(passes[i].sampled));
        guest.passes.push_back(pass);
    }
}

void guest_submit2(const SevkGuestPass2* passes, uint32_t count) {
    if ((count && !passes) || count > 256) throw std::runtime_error("invalid extended guest passes");
    std::vector<GuestPass> pending;
    for (uint32_t i = 0; i < count; ++i) {
        const auto& input = passes[i];
        if (input.size != sizeof(SevkGuestPass2) || input.abi_version != 2 || input.reserved[0] || input.reserved[1])
            throw std::runtime_error("extended guest ABI mismatch");
        if (input.mark < 1 || input.mark > 6 || input.compose > 6 || (input.flags & ~(16u|1024u|2048u)) || (input.flags&3072u)==3072u || !input.width || !input.height || !input.depth || input.width > 16384 || input.height > 16384 || input.depth > 2048)
            throw std::runtime_error("invalid extended guest dispatch");
        auto pipeline = guest.pipelines.find(input.pipeline);
        if (pipeline == guest.pipelines.end() || !pipeline->second.contract) throw std::runtime_error("extended guest pipeline missing");
        GuestPass pass{}; pass.extended = true; pass.mark = input.mark; pass.compose = input.compose;
        pass.width = input.width; pass.height = input.height; pass.depth = input.depth; pass.flags = input.flags;
        pass.pipeline = input.pipeline; pass.storage = input.storage[0];
        std::memcpy(pass.sampled, input.sampled, sizeof(pass.sampled));
        std::memcpy(pass.outputs, input.storage, sizeof(pass.outputs));
        for (uint32_t b = 0; b < 8; ++b) {
            uint32_t bytes = input.constant_bytes[b], required = pipeline->second.constant_bytes[b];
            if (bytes > 4096 || bytes < required || (bytes && !input.constants[b])) throw std::runtime_error("extended guest constants incomplete");
            if (bytes) {
                const auto* data = static_cast<const uint8_t*>(input.constants[b]);
                pass.constants[b].assign(data, data + bytes);
            }
        }
        guest_validate_extended(pass, pipeline->second);
        pending.push_back(std::move(pass));
    }
    // Allocate before committing so even a queue allocation failure cannot
    // append a valid prefix of an otherwise rejected volume transaction.
    static_assert(std::is_nothrow_move_constructible_v<GuestPass>);
    guest.passes.reserve(guest.passes.size() + pending.size());
    for (auto& pass : pending) guest.passes.push_back(std::move(pass));
}

static constexpr uint64_t kGuestHdr = 0xFFFF000000000001ull;
static constexpr uint64_t kGuestDepth = 0xFFFF000000000002ull;
static constexpr uint64_t kGuestG0 = 0xFFFF000000000003ull;
static constexpr uint64_t kGuestG1 = 0xFFFF000000000004ull;
static constexpr uint64_t kGuestG2 = 0xFFFF000000000005ull;
static constexpr uint64_t kGuestUpscale = 0xFFFF000000000006ull;
static constexpr uint64_t kGuestDisplay = 0xFFFF000000000007ull;
static constexpr uint64_t kGuestMotion = 0xFFFF000000000008ull;
static constexpr uint64_t kGuestAttach0 = 0xFFFF000000000010ull;
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
    // Guest callbacks run before EndFrame temporarily replaces extent with the
    // world render size. Report that same size now, including DRS and logical
    // viewport dimensions, rather than allocating producers at presentation size.
    auto output=present_extent.width?present_extent:extent;
    uint32_t render_w=extent.width,render_h=extent.height;
    if(world.current().queued) {
        render_w=std::max(1u,uint32_t(std::lround((sprite_w?sprite_w:output.width)*render_fraction)));
        render_h=std::max(1u,uint32_t(std::lround((sprite_h?sprite_h:output.height)*render_fraction)));
    }
    float width = float(std::max(1u, render_w)), height = float(std::max(1u, render_h));
    extras.render[0] = extras.scene[0] = width; extras.render[1] = extras.scene[1] = height;
    extras.inv_render[0] = extras.inv_scene[0] = 1.f / width; extras.inv_render[1] = extras.inv_scene[1] = 1.f / height;
    extras.history_valid = guest.have_prev ? 1u : 0u; extras.has_velocity = extras.history_valid;
    extras.frame_index = uint32_t(stats.submitted_frames); extras.safety = 1.f;
    std::memcpy(extras.unjittered, current, 64);
    std::memcpy(extras.prev, guest.have_prev ? guest.prev_vp : current, 64);
    extras.camera[0] = extras.camera[5] = extras.camera[10] = 1.f;
    extras.camera[3] = submitted_frame.camera_position[0]; extras.camera[7] = submitted_frame.camera_position[1]; extras.camera[11] = submitted_frame.camera_position[2];
    // QueueWorld's "projection" is the combined view-projection used for
    // depth reconstruction. Its diagonal changes sign with camera rotation.
    // Stock Frame.hlsli stores the actual projection as its second matrix.
    if (world.current().frame.size() >= 128) {
        float projection[16]{}; std::memcpy(projection, world.current().frame.data() + 64, 64);
        extras.proj_scale[0] = projection[0]; extras.proj_scale[1] = projection[5];
    }
    std::memcpy(extras.sun_color, submitted_frame.sunlight_color, 12);
    extras.sun_diffuse = submitted_frame.sunlight_color[3] > 0.f ? submitted_frame.sunlight_color[3] : 1.f;
    float sun_length=0;for(uint32_t i=0;i<3;i++)sun_length+=submitted_frame.sunlight_direction[i]*submitted_frame.sunlight_direction[i];
    if(!std::isfinite(sun_length) || sun_length<1e-8f)extras.sun_toward[1]=1.f;
    else for(uint32_t i=0;i<3;i++)extras.sun_toward[i]=-submitted_frame.sunlight_direction[i]/std::sqrt(sun_length);
    std::memcpy(extras.sky_ambient, submitted_frame.ambient_color, 12);
    extras.sky_luma = extras.sky_ambient[0] * 0.2126f + extras.sky_ambient[1] * 0.7152f + extras.sky_ambient[2] * 0.0722f;
    SevkGuestFrame frame{}; frame.size = sizeof(frame); frame.abi_version = SEVK_ABI_VERSION;
    frame.width = render_w; frame.height = render_h; frame.frame_index = extras.frame_index; frame.history_valid = extras.history_valid;
    frame.jitter_x = extras.jitter[0]; frame.jitter_y = extras.jitter[1]; frame.safety_scale = extras.safety; frame.sun_diffuse = extras.sun_diffuse;
    frame.sky_luma = extras.sky_luma; frame.proj_scale_x = extras.proj_scale[0]; frame.proj_scale_y = extras.proj_scale[1];
    std::memcpy(frame.sun_color, extras.sun_color, 12); std::memcpy(frame.sun_toward, extras.sun_toward, 12); std::memcpy(frame.sky_ambient, extras.sky_ambient, 12);
    frame.camera_delta[0] = submitted_frame.camera_position[0] - guest.prev_camera[0];
    frame.camera_delta[1] = submitted_frame.camera_position[1] - guest.prev_camera[1];
    frame.camera_delta[2] = submitted_frame.camera_position[2] - guest.prev_camera[2];
    std::memcpy(frame.view_proj, extras.unjittered, 64); std::memcpy(frame.prev_view_proj, extras.prev, 64);
    frame.camera_to_world[0] = frame.camera_to_world[5] = frame.camera_to_world[10] = frame.camera_to_world[15] = 1.f;
    // Stock Frame.hlsli places inv_view_matrix after four 64-byte matrices.
    // It is view-at-zero on this renderer; preserve its rotation, then supply
    // the absolute camera translation independently below.
    if (world.current().frame.size() >= 320) {
        float rotation[16]{}; std::memcpy(rotation, world.current().frame.data() + 256, 64);
        float length = rotation[0]*rotation[0] + rotation[1]*rotation[1] + rotation[2]*rotation[2];
        if (std::isfinite(length) && length > 0.5f && length < 1.5f)
            for (uint32_t row = 0; row < 3; ++row)
                for (uint32_t col = 0; col < 3; ++col) frame.camera_to_world[row*4+col] = rotation[row*4+col];
    }
    frame.camera_to_world[12] = submitted_frame.camera_position[0]; frame.camera_to_world[13] = submitted_frame.camera_position[1]; frame.camera_to_world[14] = submitted_frame.camera_position[2];
    frame.far_plane = view_far > 1.f ? view_far : 1000000.f;
    frame.output_width = float(std::max(1u,output.width));
    frame.output_height = float(std::max(1u,output.height));
    frame.service_flags = display_flags | (hdr_active ? 8u : 0u);
    guest.frame = frame;
    std::memcpy(guest.prev_vp, current, 64); std::memcpy(guest.prev_camera, submitted_frame.camera_position, 12); guest.have_prev = true; guest.frame_stamp = stamp;
    guest.frame.pad0 = std::bit_cast<float>(extras.frame_index);
}

void guest_frame_environment(const SevkGuestEnvironment& input) {
    if(!recording || !guest.resources_open)throw std::runtime_error("guest environment requires the open frame callback");
    guest_refresh_frame();
    if(input.frame_index!=guest.frame.frame_index)throw std::runtime_error("guest environment frame index mismatch");
    float values[20];std::memcpy(values,input.sun_color,sizeof(values));
    for(float value:values)if(!std::isfinite(value))throw std::runtime_error("nonfinite guest environment");
    if(input.safety_scale<0 || input.safety_scale>1 || input.sun_diffuse<0 || input.planet_air_top<0 || input.visual_atmo_ceil<0)
        throw std::runtime_error("invalid guest environment range");
    std::memcpy(guest.frame.sun_color,input.sun_color,12);guest.frame.sun_diffuse=input.sun_diffuse;
    std::memcpy(guest.frame.sun_toward,input.sun_toward,12);guest.frame.sky_luma=input.sky_luma;
    std::memcpy(guest.frame.sky_ambient,input.sky_ambient,12);guest.frame.safety_scale=input.safety_scale;
    guest.frame.air_top=input.planet_air_top;guest.frame.visual_ceil=input.visual_atmo_ceil;
    guest.frame.skip_floor=input.skip_floor;guest.frame.skip_mul=input.skip_mul;
    std::memcpy(guest.frame.camera_delta,input.camera_delta,12);
    guest.skip_floor=input.skip_floor;guest.skip_mul=input.skip_mul;
}

int guest_pool_index() { return recording ? int(slot_index % 2) : 2; }

VkDescriptorPool guest_pool(int index) {
    uint64_t stamp = recording ? stats.submitted_frames + 1 : guest.replay_count;
    if (!guest.pools[index]) {
        std::array<VkDescriptorPoolSize, 4> sizes{{ { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 4096 }, { VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 16384 }, { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 256 }, { VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 2048 } }};
        VkDescriptorPoolCreateInfo info{ VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO };
        info.maxSets = 1024; info.poolSizeCount = 4; info.pPoolSizes = sizes.data();
        check(vkCreateDescriptorPool(device, &info, nullptr, &guest.pools[index]), "create guest descriptor pool");
    }
    if (guest.pool_stamp[index] != stamp) {
        check(vkResetDescriptorPool(device, guest.pools[index], 0), "reset guest descriptor pool");
        // The renderer has waited this frame slot's fence (or completed the
        // previous synchronous replay). Earlier descriptors may still refer to
        // a snapshot replaced later in the same command buffer.
        for(auto& image:guest.retired_snapshots[index])destroy_image(image);
        guest.retired_snapshots[index].clear();
        guest.bumps[index].used = 0; guest.pool_stamp[index] = stamp;
    }
    return guest.pools[index];
}

uint32_t guest_uniform(int pool, const void* data, uint32_t bytes) {
    auto& bump = guest.bumps[pool];
    if (!bump.buffer.handle) bump.buffer = make_buffer(4u * 1024u * 1024u, VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT, true);
    uint32_t align = uint32_t(guest.uniform_alignment), start = (bump.used + align - 1u) & ~(align - 1u);
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
    if (found == guest.images.end() || found->second.is_buffer || found->second.depth != 1 || !found->second.image.view || found->second.produced_epoch!=guest.catalog_epoch)
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
    if(id>=kGuestCatalogVelocity && id<=kGuestCatalogReactiveMask){auto found=guest.images.find(id);if(found!=guest.images.end())return found->second.image.view;}
    if (id >= kGuestAttach0 && id < kGuestAttach0 + 3) { auto& target = world.attach[id - kGuestAttach0]; return target.image.view ? target.image.view : guest_dummy(); }
    // Preserve old attachment aliases only when no pinned catalog image owns
    // that ID. Updated hosts use the disjoint 0x10..0x12 range above.
    if(id>=0xFFFF000000000009ull && id<=0xFFFF00000000000Bull){auto& target=world.attach[id-0xFFFF000000000009ull];return target.image.view?target.image.view:guest_dummy();}
    if (id == kGuestDisplay) {
        if (swapchain && image_index < swap_views.size()) return swap_views[image_index];
        return offscreen.view ? offscreen.view : guest_dummy();
    }
    auto found = guest.images.find(id);
    return found == guest.images.end() ? guest_dummy() : found->second.image.view;
}

bool guest_snapshot_size(GuestSnapshot& snapshot,uint32_t width,uint32_t height) {
    bool fresh=!snapshot.image.view || snapshot.width!=width || snapshot.height!=height;
    if (fresh) {
        auto image=make_image(width,height,VK_FORMAT_R16G16B16A16_SFLOAT,VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT,VK_IMAGE_ASPECT_COLOR_BIT);
        if(snapshot.image.handle) {
            try{guest.retired_snapshots[guest_pool_index()].push_back(snapshot.image);}
            catch(...){destroy_image(image);throw;}
        }
        snapshot={image,width,height};
    }
    return fresh;
}

VkImageView guest_copy_hdr(VkCommandBuffer cmd, VkImageLayout layout) {
    if (!world.hdr.view || !world.width || !world.height) return VK_NULL_HANDLE;
    auto& snapshot=guest.hdr_snapshot;
    bool fresh=guest_snapshot_size(snapshot,world.width,world.height);
    VkPipelineStageFlags2 source = layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT : (VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT);
    VkAccessFlags2 access = layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    barrier(cmd, world.hdr.handle, layout, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        source, access, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT);
    barrier(cmd, snapshot.image.handle, fresh?VK_IMAGE_LAYOUT_UNDEFINED:VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        fresh?VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT:(VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT), fresh?0:VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT);
    if(world.hdr_format==VK_FORMAT_R16G16B16A16_SFLOAT){
        VkImageCopy copy{}; copy.srcSubresource = copy.dstSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 }; copy.extent = { world.width, world.height, 1 };
        vkCmdCopyImage(cmd, world.hdr.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, snapshot.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
    }else{
        VkImageBlit copy{};copy.srcSubresource=copy.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};copy.srcOffsets[1]=copy.dstOffsets[1]={int(world.width),int(world.height),1};
        vkCmdBlitImage(cmd,world.hdr.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,snapshot.image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&copy,VK_FILTER_NEAREST);
    }
    barrier(cmd, snapshot.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    barrier(cmd, world.hdr.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, layout, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT, source, access);
    return snapshot.image.view;
}

VkImageView guest_copy_source(VkCommandBuffer cmd, VkImage image, VkImageLayout layout, uint32_t width, uint32_t height) {
    if (!cmd || !image || !width || !height) return VK_NULL_HANDLE;
    auto& snapshot=guest.upscale_snapshot;
    bool fresh=guest_snapshot_size(snapshot,width,height);
    VkPipelineStageFlags2 source = layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT : (VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT);
    VkAccessFlags2 access = layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    barrier(cmd, image, layout, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT, source, access, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT);
    barrier(cmd, snapshot.image.handle, fresh ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        fresh ? VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT : (VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT), fresh ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT);
    VkImageBlit blit{};
    blit.srcSubresource = blit.dstSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 };
    blit.srcOffsets[1] = blit.dstOffsets[1] = { int(width), int(height), 1 };
    vkCmdBlitImage(cmd, image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, snapshot.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &blit, VK_FILTER_NEAREST);
    barrier(cmd, snapshot.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    barrier(cmd, image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, layout, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT, source, access);
    return snapshot.image.view;
}

void guest_initialize_sampled(VkCommandBuffer cmd,GuestImage& image) {
    if(image.is_buffer || image.layout!=VK_IMAGE_LAYOUT_UNDEFINED)return;
    // A disabled history branch still declares sampled descriptors. Initialize
    // fresh histories deterministically and establish their advertised layout
    // before the first dispatch/draw, across every mip and array layer.
    auto stages=VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    barrier(cmd,image.image.handle,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,image.aspect,
        VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT,0,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,image.mips,image.layers);
    VkImageSubresourceRange range{image.aspect,0,image.mips,0,image.layers};
    if(image.aspect==VK_IMAGE_ASPECT_DEPTH_BIT) {
        VkClearDepthStencilValue clear{1.f,0};vkCmdClearDepthStencilImage(cmd,image.image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,&clear,1,&range);
    }else {
        VkClearColorValue clear{};vkCmdClearColorImage(cmd,image.image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,&clear,1,&range);
    }
    barrier(cmd,image.image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,image.aspect,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,stages,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,image.mips,image.layers);
    image.layout=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
}

float guest_unpack(uint64_t packed, bool high) {
    uint32_t bits = high ? uint32_t(packed >> 32) : uint32_t(packed);
    float value = 0;
    std::memcpy(&value, &bits, 4);
    return value;
}

void guest_fill_owned(const GuestPass& pass, GuestPipeline& pipeline, uint8_t* owned) {
    if (pass.flags & 32) {
        if (pipeline.owned_bytes > 256) throw std::runtime_error("guest custom constants exceed packet");
        std::memcpy(owned, pass.uniforms, std::min(pass.uniform_bytes, pipeline.owned_bytes));
        return;
    }
    if (pipeline.owned_bytes == 1280) {
        std::memcpy(owned, world.current().frame.data(), 1280);
        return;
    }
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

void guest_validate_extended(const GuestPass& pass, const GuestPipeline& pipeline) {
    std::vector<uint64_t> outputs;
    auto output = [&](uint64_t id) {
        if (!id || std::find(outputs.begin(), outputs.end(), id) != outputs.end()) throw std::runtime_error("extended guest output missing or duplicated");
        auto image = guest.images.find(id);
        if (id == kGuestHdr && !pipeline.compute && pipeline.contract == 1) {
            if (!world.hdr.view) throw std::runtime_error("extended guest HDR target unavailable");
        } else if (image == guest.images.end() || image->second.is_buffer || image->second.aspect != VK_IMAGE_ASPECT_COLOR_BIT || image->second.mips != 1 || image->second.layers != 1)
            throw std::runtime_error("extended guest output image unavailable");
        outputs.push_back(id);
    };
    if (!pipeline.compute) {
        output(pass.outputs[0]);
        if (pipeline.contract == 2) {
            output(pass.outputs[1]);
            const auto& a = guest.images.at(pass.outputs[0]); const auto& b = guest.images.at(pass.outputs[1]);
            if (a.format != VK_FORMAT_R16G16B16A16_SFLOAT || b.format != a.format || a.depth != 1 || b.depth != 1 || a.width != b.width || a.height != b.height)
                throw std::runtime_error("extended guest MRT dimensions or format mismatch");
        }
    }
    for (const auto& binding : pipeline.bindings) if (binding.second == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE) {
        uint32_t index = binding.first - 384;
        output(pass.outputs[index]);
        if (guest.images.at(pass.outputs[index]).format != pipeline.output_formats[index])
            throw std::runtime_error("extended guest storage image format mismatch");
        if ((guest.images.at(pass.outputs[index]).depth > 1) != (pipeline.output_dims[index] == SpvDim3D))
            throw std::runtime_error("extended guest storage image dimension mismatch");
    }
    for (const auto& binding : pipeline.bindings) {
        if (binding.second != VK_DESCRIPTOR_TYPE_STORAGE_BUFFER && binding.second != VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE) continue;
        uint64_t id = pass.sampled[binding.first - 128];
        if (std::find(outputs.begin(), outputs.end(), id) != outputs.end()) throw std::runtime_error("extended guest input aliases output");
        auto image = guest.images.find(id);
        bool buffer = binding.second == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
        bool sentinel = !buffer && ((id >= kGuestHdr && id <= kGuestCatalogReactiveMask) || (id>=kGuestAttach0 && id<kGuestAttach0+3));
        if (!sentinel && (image == guest.images.end() || image->second.is_buffer != buffer))
            throw std::runtime_error("extended guest required input t" + std::to_string(binding.first - 128) + " unavailable");
        if (!buffer) {
            uint32_t slot = binding.first - 128;
            bool volume = !sentinel && image->second.depth > 1;
            bool arrayed = !sentinel && image->second.layers > 1;
            if (volume != (pipeline.input_dims[slot] == SpvDim3D) || arrayed != (pipeline.input_arrayed[slot] != 0))
                throw std::runtime_error("extended guest sampled image dimension mismatch");
        }
    }
}

bool guest_draw_bus(VkCommandBuffer cmd, const GuestPass& pass, GuestPipeline& pipeline, uint32_t mark) {
    if (pass.extended != (pipeline.contract != 0)) return false;
    std::vector<GuestImage*> compute_outputs;
    for (const auto& binding : pipeline.bindings) if (binding.second == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE) {
        uint64_t id = pass.extended ? pass.outputs[binding.first - 384] : pass.storage;
        auto image = guest.images.find(id);
        if (image == guest.images.end() || image->second.is_buffer || image->second.aspect != VK_IMAGE_ASPECT_COLOR_BIT || image->second.mips != 1) return false;
        for (const auto* output : compute_outputs) if (output == &image->second) return false;
        for (uint64_t sampled : pass.sampled) if (sampled == id) return false;
        compute_outputs.push_back(&image->second);
    }
    if (pipeline.contract && !pipeline.compute) {
        uint32_t count = pipeline.contract == 2 ? 2u : 1u;
        for (uint32_t i = 0; i < count; ++i) {
            if (!pass.outputs[i]) return false;
            for (uint64_t sampled : pass.sampled) if (sampled == pass.outputs[i]) return false;
            if (i && pass.outputs[i] == pass.outputs[0]) return false;
        }
    }
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
    extras.history_valid = guest.frame.history_valid; extras.has_velocity = extras.history_valid; extras.frame_index = guest.frame.frame_index; extras.safety = guest.frame.safety_scale;
    extras.air_top=guest.frame.air_top;extras.visual_ceil=guest.frame.visual_ceil;extras.skip_floor=guest.skip_floor;extras.skip_mul=guest.skip_mul;
    std::memcpy(extras.unjittered, guest.frame.view_proj, 64); std::memcpy(extras.prev, guest.frame.prev_view_proj, 64);
    std::memcpy(extras.camera, guest.frame.camera_to_world, 48);
    extras.camera[3] = guest.frame.camera_to_world[12]; extras.camera[7] = guest.frame.camera_to_world[13]; extras.camera[11] = guest.frame.camera_to_world[14];
    extras.proj_scale[0] = guest.frame.proj_scale_x; extras.proj_scale[1] = guest.frame.proj_scale_y;
    std::memcpy(extras.sun_color, guest.frame.sun_color, 12); extras.sun_diffuse = guest.frame.sun_diffuse;
    std::memcpy(extras.sun_toward, guest.frame.sun_toward, 12); extras.sky_luma = guest.frame.sky_luma;
    std::memcpy(extras.sky_ambient, guest.frame.sky_ambient, 12);
    uint8_t owned[1280]{};
    guest_fill_owned(pass, pipeline, owned);
    if ((pass.flags & (1 | 32)) == 0 && pipeline.owned_bytes >= 208 && pipeline.owned_bytes <= 256) {
        std::memcpy(owned + 192, extras.render, 8); std::memcpy(owned + 200, extras.inv_render, 8);
    }
    uint8_t celestial_view[96]{};
    bool celestial = (pass.flags & 1) && pipeline.extras_bytes > 0 && pipeline.extras_bytes <= 96;
    if (celestial) guest_fill_celestial_view(pass, celestial_view);
    bool to_hdr = pass.storage == kGuestHdr;
    bool sample_hdr = false;
    for (auto binding : pipeline.bindings)
        if (binding.second == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE && binding.first >= 128 && binding.first < 176 && pass.sampled[binding.first - 128] == kGuestHdr)
            sample_hdr = true;
    // Offscreen consumers need the same snapshot/layout contract as HDR writers.
    VkImageLayout hdr_layout = mark >= 4 ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    VkImageView hdr_snapshot=sample_hdr?guest_copy_hdr(cmd,hdr_layout):VK_NULL_HANDLE;
    bool draw_tone = to_hdr && mark == 5 && post.after_tonemap && post.after_tonemap->image.view;
    bool draw_dlss = to_hdr && mark >= 6 && display_state.upscale_ready && display_state.upscale_view;
    bool sample_upscale = false;
    for (auto binding : pipeline.bindings)
            if (binding.second == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE && binding.first >= 128 && binding.first < 176 && pass.sampled[binding.first - 128] == kGuestUpscale)
                sample_upscale = true;
    VkImageView upscale_snapshot=VK_NULL_HANDLE;
    if (sample_upscale && mark>=6 && display_state.upscale_ready && display_state.upscale_view)
        upscale_snapshot=guest_copy_source(cmd, display_state.upscale_handle, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, display_state.upscale_w, display_state.upscale_h);
    else if (sample_upscale && post.after_tonemap && post.after_tonemap->image.view)
        upscale_snapshot=guest_copy_source(cmd, post.after_tonemap->image.handle, post.after_tonemap->layout ? post.after_tonemap->layout : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, post.after_tonemap->width, post.after_tonemap->height);
    else if(sample_upscale)upscale_snapshot=guest_copy_hdr(cmd,hdr_layout);
    std::vector<VkDescriptorBufferInfo> buffers;
    std::vector<VkDescriptorImageInfo> images;
    struct GuestWrite { uint32_t binding; VkDescriptorType type; int buffer{-1}, image{-1}; };
    std::vector<GuestWrite> planned;
    for (auto binding : pipeline.bindings) {
        GuestWrite planned_write{ binding.first, binding.second, -1, -1 };
        if (binding.second == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER) {
            const void* data = pass.extended ? (const void*)pass.constants[binding.first].data() : binding.first == 7 ? (const void*)pass.uniforms : binding.first == 0 ? (const void*)owned : celestial ? (const void*)celestial_view : (const void*)&extras;
            uint32_t bytes = pass.extended ? pipeline.constant_bytes[binding.first] : binding.first == 7 ? (pipeline.uniform_bytes ? pipeline.uniform_bytes : 256u) : binding.first == 0 ? std::max(16u, pipeline.owned_bytes) : (pipeline.extras_bytes ? pipeline.extras_bytes : 320u);
            uint32_t offset = guest_uniform(pool, data, bytes);
            if (offset == ~0u) return false;
            planned_write.buffer = int(buffers.size());
            buffers.push_back({ guest.bumps[pool].buffer.handle, offset, bytes });
        } else if (binding.second == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE) {
            uint32_t slot = binding.first - 128;
            uint64_t id = slot < 48 ? pass.sampled[slot] : 0;
            if (pass.extended && (!id || (id < kGuestHdr && (guest.images.find(id) == guest.images.end() || guest.images.at(id).is_buffer)))) return false;
            bool hdr = false; VkImageView view = id ? guest_source(id, &hdr) : guest_dummy();
            auto source=guest.images.find(id);
            if(source!=guest.images.end() && !source->second.is_buffer && view==source->second.image.view)guest_initialize_sampled(cmd,source->second);
            if (hdr && hdr_snapshot) view = hdr_snapshot;
            if (id == kGuestUpscale && upscale_snapshot) view = upscale_snapshot;
            VkImageLayout layout = id == kGuestDepth && (pass.flags & 1) ? VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            planned_write.image = int(images.size());
            images.push_back({ nullptr, view, layout });
        } else if (binding.second == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER) {
            uint32_t slot = binding.first >= 128 ? binding.first - 128 : 0;
            uint64_t id = slot < 48 ? pass.sampled[slot] : 0;
            // The fullscreen contract exposes the same 48-byte point-light
            // records and tiled lists used by stock deferred lighting. Borrow
            // this frame slot's read-only buffers; the world owns retirement.
            if (!pass.extended && id == 0 && mark < 5 && (slot == 10 || slot == 11)) {
                auto& world_slot = world.slots[slot_index];
                const auto& source = slot == 10 ? world_slot.point_lights
                    : light_tiles.requested ? world_slot.light_tile_gpu : world_slot.tiles;
                if (!source.handle || !source.bytes) return false;
                planned_write.buffer = int(buffers.size());
                buffers.push_back({ source.handle, 0, source.bytes });
                planned.push_back(planned_write);
                continue;
            }
            auto image = guest.images.find(id);
            if (image == guest.images.end() || !image->second.is_buffer) return false;
            auto& source = image->second;
            auto& buffer = source.storage[pool];
            if (source.uploaded[pool] != source.revision) {
                std::memcpy(buffer.mapped, source.pending.data(), source.width);
                check(vmaFlushAllocation(allocator, buffer.memory, 0, source.width), "flush guest frame buffer");
                source.uploaded[pool] = source.revision;
            }
            planned_write.buffer = int(buffers.size());
            buffers.push_back({ buffer.handle, 0, buffer.bytes });
        } else if (binding.second == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE) {
            auto image = guest.images.find(pass.extended ? pass.outputs[binding.first - 384] : pass.storage);
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
        const auto sampled_stages = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
        for (auto* image : compute_outputs) {
        if (pass.flags & 16) {
            barrier(cmd, image->image.handle, image->layout, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                sampled_stages, image->layout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT | VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT);
            VkClearColorValue zero{}; VkImageSubresourceRange range{ VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
            vkCmdClearColorImage(cmd, image->image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &zero, 1, &range);
            barrier(cmd, image->image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT,
                VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_READ_BIT | VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
        } else
            barrier(cmd, image->image.handle, image->layout, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_ASPECT_COLOR_BIT,
                sampled_stages, image->layout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT | VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,
                VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_READ_BIT | VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT, image->mips);
        }
        vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipeline.pipeline);
        vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipeline.layout, 0, 1, &set, 0, nullptr);
        vkCmdDispatch(cmd, std::max(1u, (std::max(1u, pass.width) + pipeline.group_x - 1) / pipeline.group_x),
            std::max(1u, (std::max(1u, pass.height) + pipeline.group_y - 1) / pipeline.group_y),
            std::max(1u, (pass.depth + pipeline.group_z - 1) / pipeline.group_z));
        for (auto* image : compute_outputs) {
            barrier(cmd, image->image.handle, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT, sampled_stages, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, image->mips);
            image->layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;image->produced_epoch=guest.catalog_epoch;
        }
        return true;
    }
    bool depth = (pass.flags & 1) != 0, geometry = (pass.flags & 64) != 0;
    if (geometry && !pipeline.geometry) return false;
    int mode = geometry ? 5 : depth ? 4 : pass.compose == 1 ? 2 : pass.compose == 2 ? 3 : (pass.compose == 0 || pass.compose == 6) ? 1 : 0;
    GuestImage* target = nullptr;
    PostImage* tracked = nullptr;
    VkImageView color = world.hdr.view; uint32_t tw = extent.width, th = extent.height; bool hdr_target = true;
    if (!to_hdr) {
        auto image = guest.images.find(pass.storage);
        if (image == guest.images.end() || image->second.depth != 1 || image->second.aspect != VK_IMAGE_ASPECT_COLOR_BIT) return false;
        target = &image->second; color = target->image.view; tw = target->width; th = target->height; hdr_target = false;
    } else if (draw_tone) {
        tracked = post.after_tonemap; color = tracked->image.view; tw = tracked->width; th = tracked->height; hdr_target = false;
    } else if (draw_dlss) {
        color = display_state.upscale_view; tw = display_state.upscale_w; th = display_state.upscale_h; hdr_target = false;
    }
    if (!color) return false;
    int format = hdr_target && world.hdr_format==VK_FORMAT_B10G11R11_UFLOAT_PACK32 ? 5 : draw_tone ? 0 : draw_dlss ? 1 : target && target->format == VK_FORMAT_R16_SFLOAT ? 6 : target && target->format == VK_FORMAT_R32G32B32A32_SFLOAT ? 4 : hdr_target || (target && target->format == VK_FORMAT_R16G16B16A16_SFLOAT) ? 1 : target && target->format == VK_FORMAT_R32_SFLOAT ? 2 : target && target->format == VK_FORMAT_R16G16_SFLOAT ? 3 : 0;
    VkPipeline pipe = pipeline.contract == 2 ? pipeline.pipeline : pipeline.draw[format][mode];
    if (!pipe) return false;
    GuestImage* second = nullptr;
    if (pipeline.contract == 2) {
        auto image = guest.images.find(pass.outputs[1]);
        if (!target || target->format != VK_FORMAT_R16G16B16A16_SFLOAT || image == guest.images.end()) return false;
        second = &image->second;
        if (second->is_buffer || second->depth != 1 || second->width != tw || second->height != th || second->format != target->format) return false;
    }
    VkImageLayout incoming = draw_tone ? (tracked->layout ? tracked->layout : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL) : draw_dlss ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : hdr_target ? (mark >= 4 ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL) : (target->layout == VK_IMAGE_LAYOUT_UNDEFINED ? VK_IMAGE_LAYOUT_UNDEFINED : target->layout);
    VkImage handle = draw_tone ? tracked->image.handle : draw_dlss ? display_state.upscale_handle : hdr_target ? world.hdr.handle : target->image.handle;
    // LOAD also reads the existing HDR attachment, including when the layout
    // already matches the producer's color-attachment layout.
    barrier(cmd, handle, incoming, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        incoming == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT : VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,
        incoming == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : incoming == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    if (second) barrier(cmd, second->image.handle, second->layout, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT,
        second->layout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    bool restore_depth = false;
    VkRenderingAttachmentInfo depth_info{ VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO };
    if (depth && world.gbuffer[3].view) {
        barrier(cmd, world.gbuffer[3].handle, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_DEPTH_BIT,
            VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,
            VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        depth_info.imageView = world.gbuffer[3].view; depth_info.imageLayout = VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL;
        // Celestial draws only test/sample opaque depth. Later atmosphere,
        // transparency and temporal passes still require its exact contents.
        depth_info.loadOp = VK_ATTACHMENT_LOAD_OP_LOAD; depth_info.storeOp = VK_ATTACHMENT_STORE_OP_NONE; restore_depth = true;
    }
    VkRenderingAttachmentInfo attachment{ VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO };
    attachment.imageView = color; attachment.imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    attachment.loadOp = (pass.flags & 256) ? VK_ATTACHMENT_LOAD_OP_CLEAR : geometry || (pass.flags & 128) || hdr_target || draw_tone || draw_dlss || pass.compose == 3 ? VK_ATTACHMENT_LOAD_OP_LOAD : VK_ATTACHMENT_LOAD_OP_CLEAR;
    attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE; attachment.clearValue.color = { { 0, 0, 0, 0 } };
    if (pass.flags & 256) attachment.clearValue.color = { { 100000.f, 100000.f, 100000.f, 100000.f } };
    VkRenderingInfo rendering{ VK_STRUCTURE_TYPE_RENDERING_INFO }; rendering.renderArea = { { 0, 0 }, { tw, th } };
    rendering.layerCount = 1; rendering.colorAttachmentCount = 1; rendering.pColorAttachments = &attachment;
    VkRenderingAttachmentInfo mrt[2]{attachment, attachment};
    if (second) { mrt[1].imageView = second->image.view; rendering.colorAttachmentCount = 2; rendering.pColorAttachments = mrt; }
    if (restore_depth) rendering.pDepthAttachment = &depth_info;
    VkViewport viewport{ 0, 0, float(tw), float(th), 0, 1 }; VkRect2D scissor{ { 0, 0 }, { tw, th } };
    uint32_t vertex_count = 3;
    if (geometry || (pass.flags & 128)) {
        if (pass.uniform_bytes < 160) throw std::runtime_error("guest geometry viewport constants missing");
        float rectangle[4]{}; std::memcpy(rectangle, pass.uniforms + 128, 16);
        for (float value : rectangle) if (!std::isfinite(value) || value < 0) throw std::runtime_error("invalid guest geometry viewport");
        if (rectangle[2] < 1 || rectangle[3] < 1 || rectangle[0] + rectangle[2] > tw || rectangle[1] + rectangle[3] > th)
            throw std::runtime_error("guest geometry viewport outside image");
        viewport = { rectangle[0], rectangle[1], rectangle[2], rectangle[3], 0, 1 };
        scissor = { { int32_t(rectangle[0]), int32_t(rectangle[1]) }, { uint32_t(rectangle[2]), uint32_t(rectangle[3]) } };
        if (geometry) {
            std::memcpy(&vertex_count, pass.uniforms + 144, 4);
            if (vertex_count > 6000000 || vertex_count % 3) throw std::runtime_error("invalid guest triangle count");
        }
    }
    vkCmdBeginRendering(cmd, &rendering);
    vkCmdSetViewport(cmd, 0, 1, &viewport); vkCmdSetScissor(cmd, 0, 1, &scissor);
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, pipe);
    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline.layout, 0, 1, &set, 0, nullptr);
    vkCmdDraw(cmd, vertex_count, 1, 0, 0);
    vkCmdEndRendering(cmd);
    VkImageLayout outgoing = hdr_target && mark >= 4 ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : hdr_target ? VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    if (outgoing != VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)
        barrier(cmd, handle, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, outgoing, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    if (second) {
        barrier(cmd, second->image.handle, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,
            VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        second->layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;second->produced_epoch=guest.catalog_epoch;
    }
    if (!hdr_target && target) {target->layout = outgoing;target->produced_epoch=guest.catalog_epoch;}
    if (tracked) tracked->layout = outgoing;
    if (restore_depth)
        barrier(cmd, world.gbuffer[3].handle, VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_DEPTH_BIT,
            VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,
            VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
    return true;
}

// The host requests an HDR pyramid with packet flag 8. Keep copy, mip
// generation and visibility on the same graphics queue as the consuming pass.
bool guest_generate_mips(VkCommandBuffer cmd, const GuestPass& pass, uint32_t mark) {
    auto found = guest.images.find(pass.storage);
    if (found == guest.images.end()) return false;
    auto& target = found->second;
    if (target.is_buffer || target.depth != 1 || target.layers != 1 || target.mips == 0) return false;
    VkImage source{}; VkImageLayout source_layout{};
    uint32_t sw{}, sh{};
    if (pass.sampled[0] == kGuestHdr) {
        source = world.hdr.handle; sw = extent.width; sh = extent.height;
        source_layout = mark >= 4 ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    } else {
        auto input = guest.images.find(pass.sampled[0]);
        if (input == guest.images.end() || input->second.is_buffer || input->second.depth != 1 || pass.sampled[0] == pass.storage) return false;
        source = input->second.image.handle; sw = input->second.width; sh = input->second.height;
        source_layout = input->second.layout;
    }
    if (!source || source_layout == VK_IMAGE_LAYOUT_UNDEFINED || sw != target.width || sh != target.height) return false;
    VkFormatProperties properties{};
    vkGetPhysicalDeviceFormatProperties(physical, target.format, &properties);
    constexpr auto required = VK_FORMAT_FEATURE_BLIT_SRC_BIT | VK_FORMAT_FEATURE_BLIT_DST_BIT | VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT;
    if ((properties.optimalTilingFeatures & required) != required) throw std::runtime_error("guest lit mips format cannot linear blit");
    const auto shader_stages = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    auto source_stage = source_layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT : shader_stages;
    auto source_access = source_layout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL ? VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    barrier(cmd, source, source_layout, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        source_stage, source_access, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT);
    barrier(cmd, target.image.handle, target.layout, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_ASPECT_COLOR_BIT,
        shader_stages, target.layout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, target.mips);
    VkImageBlit blit{};
    blit.srcSubresource = blit.dstSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 };
    blit.srcOffsets[1] = blit.dstOffsets[1] = { int(sw), int(sh), 1 };
    vkCmdBlitImage(cmd, source, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, target.image.handle, VK_IMAGE_LAYOUT_GENERAL, 1, &blit, VK_FILTER_NEAREST);
    for (uint32_t mip = 1; mip < target.mips; ++mip) {
        VkImageMemoryBarrier2 dependency{ VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2 };
        dependency.srcStageMask = dependency.dstStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
        dependency.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT; dependency.dstAccessMask = VK_ACCESS_2_TRANSFER_READ_BIT;
        dependency.oldLayout = dependency.newLayout = VK_IMAGE_LAYOUT_GENERAL;
        dependency.srcQueueFamilyIndex = dependency.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        dependency.image = target.image.handle; dependency.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, mip - 1, 1, 0, 1 };
        VkDependencyInfo info{ VK_STRUCTURE_TYPE_DEPENDENCY_INFO }; info.imageMemoryBarrierCount = 1; info.pImageMemoryBarriers = &dependency;
        vkCmdPipelineBarrier2(cmd, &info);
        blit.srcSubresource.mipLevel = mip - 1; blit.dstSubresource.mipLevel = mip;
        blit.srcOffsets[1] = { int(std::max(1u, sw >> (mip - 1))), int(std::max(1u, sh >> (mip - 1))), 1 };
        blit.dstOffsets[1] = { int(std::max(1u, sw >> mip)), int(std::max(1u, sh >> mip)), 1 };
        vkCmdBlitImage(cmd, target.image.handle, VK_IMAGE_LAYOUT_GENERAL, target.image.handle, VK_IMAGE_LAYOUT_GENERAL, 1, &blit, VK_FILTER_LINEAR);
    }
    barrier(cmd, target.image.handle, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT | VK_ACCESS_2_TRANSFER_READ_BIT,
        shader_stages, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, target.mips);
    target.layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    barrier(cmd, source, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, source_layout, VK_IMAGE_ASPECT_COLOR_BIT,
        VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT, source_stage, source_access);
    return true;
}

void guest_replay(VkCommandBuffer cmd, uint32_t mark) {
    ++guest.replay_count;
    guest_refresh_frame();
    if (guest.passes.empty()) return;
    for(uint32_t phase=0;phase<3;phase++)for (const auto& pass : guest.passes) {
        uint32_t selected=pass.extended && (pass.flags&1024u)?0u:pass.extended && (pass.flags&2048u)?2u:1u;
        if(selected!=phase)continue;
        if (pass.mark != mark) continue;
        if(pass.operation==3){
            auto found=guest.images.find(pass.storage);if(found==guest.images.end())continue;
            auto& image=found->second;
            barrier(cmd,image.image.handle,image.layout,VK_IMAGE_LAYOUT_GENERAL,image.aspect,
                VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,image.mips,image.layers);
            VkClearColorValue value{};std::memcpy(value.float32,pass.value,16);
            VkImageSubresourceRange range{image.aspect,0,image.mips,0,image.layers};
            vkCmdClearColorImage(cmd,image.image.handle,VK_IMAGE_LAYOUT_GENERAL,&value,1,&range);
            barrier(cmd,image.image.handle,VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,image.aspect,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,
                VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_MEMORY_READ_BIT,image.mips,image.layers);
            image.layout=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;image.produced_epoch=guest.catalog_epoch;continue;
        }
        if(!pass.pipeline)continue;
        const char* trace=std::getenv("SE_VULKAN_GUEST_TRACE");
        if(trace && std::strcmp(trace,"1")==0){std::fprintf(stderr,"guest mark=%u phase=%u pipe=%llu target=%llu %ux%ux%u\n",mark,phase,(unsigned long long)pass.pipeline,(unsigned long long)pass.storage,pass.width,pass.height,pass.depth);std::fflush(stderr);}
        if (pass.flags & 8) { guest_generate_mips(cmd, pass, mark); continue; }
        auto pipeline = guest.pipelines.find(pass.pipeline);
        if (pipeline == guest.pipelines.end()) continue;
        if (pipeline->second.bus) {
            bool recorded = guest_draw_bus(cmd, pass, pipeline->second, mark);
            if (pass.extended && !recorded) throw std::runtime_error("extended guest replay prerequisites changed");
            if(recorded){++pipeline->second.executions;pipeline->second.last_frame=guest.frame.frame_index;pipeline->second.last_completion=next_completion+1;}
            continue;
        }
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
        ++pipeline->second.executions;pipeline->second.last_frame=guest.frame.frame_index;pipeline->second.last_completion=next_completion+1;
    }
}

void guest_readback(uint64_t id, void* rgba, uint64_t bytes) {
    GuestImage borrowed{};GuestImage* selected=nullptr;
    if(id==kGuestHdr){
        if(recording || !world.initialized || !world.hdr.view)throw std::runtime_error("HDR readback requires a completed world frame");
        borrowed.image=world.hdr;borrowed.width=world.width;borrowed.height=world.height;borrowed.bpp=world.hdr_format==VK_FORMAT_B10G11R11_UFLOAT_PACK32?4:8;
        borrowed.format=world.hdr_format;borrowed.layout=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;selected=&borrowed;
    }else if(id==kGuestUpscale){
        if(recording || !display_state.upscale_ready || !display_state.upscale_view)throw std::runtime_error("upscale readback requires a completed output frame");
        borrowed.image.handle=display_state.upscale_handle;borrowed.image.view=display_state.upscale_view;
        borrowed.width=display_state.upscale_w;borrowed.height=display_state.upscale_h;borrowed.bpp=8;
        borrowed.format=VK_FORMAT_R16G16B16A16_SFLOAT;borrowed.layout=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;selected=&borrowed;
    }else {auto found=guest.images.find(id);if(found!=guest.images.end())selected=&found->second;}
    if(!selected || !rgba || selected->is_buffer)throw std::runtime_error("guest readback missing");
    auto& image=*selected;
    uint64_t expect = uint64_t(image.width) * image.height * image.depth * image.layers * image.bpp;
    bool packed=image.format==VK_FORMAT_B10G11R11_UFLOAT_PACK32;
    if (bytes < expect*(packed?2:1)) throw std::runtime_error("guest readback short");
    Buffer staging = make_buffer(expect, VK_BUFFER_USAGE_TRANSFER_DST_BIT, true, true);
    try {
        immediate([&](VkCommandBuffer cmd) {
            barrier(cmd, image.image.handle, image.layout == VK_IMAGE_LAYOUT_UNDEFINED ? VK_IMAGE_LAYOUT_UNDEFINED : image.layout,
                VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, image.aspect,
                VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, image.layout == VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT, 1, image.layers);
            VkBufferImageCopy copy{}; copy.imageSubresource = { image.aspect, 0, 0, image.layers }; copy.imageExtent = { image.width, image.height, image.depth };
            vkCmdCopyImageToBuffer(cmd, image.image.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, staging.handle, 1, &copy);
            barrier(cmd, image.image.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, image.aspect,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT, 1, image.layers);
        });
        image.layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        check(vmaInvalidateAllocation(allocator, staging.memory, 0, expect), "invalidate guest readback");
        if(packed){for(uint64_t i=0;i<expect/4;++i){uint32_t value;std::memcpy(&value,static_cast<const uint8_t*>(staging.mapped)+i*4,4);uint16_t pixel[]{uint16_t((value&0x7ffu)<<4),uint16_t(((value>>11)&0x7ffu)<<4),uint16_t(((value>>22)&0x3ffu)<<5),0x3c00u};std::memcpy(static_cast<uint8_t*>(rgba)+i*8,pixel,8);}}
        else std::memcpy(rgba, staging.mapped, static_cast<size_t>(expect));
    } catch (...) { destroy_buffer(staging); throw; }
    destroy_buffer(staging);
}
