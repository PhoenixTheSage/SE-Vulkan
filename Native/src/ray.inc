VkBufferUsageFlags ray_geometry_usage(VkBufferUsageFlags usage) {
    if (ray.supported && (usage & (VK_BUFFER_USAGE_VERTEX_BUFFER_BIT | VK_BUFFER_USAGE_INDEX_BUFFER_BIT)))
        usage |= VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT | VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT_KHR;
    return usage;
}
VkDeviceAddress ray_address(VkBuffer buffer, VkDeviceSize offset = 0) {
    VkBufferDeviceAddressInfo info{ VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO }; info.buffer = buffer;
    return vkGetBufferDeviceAddress(device, &info) + offset;
}
void ray_retire(uint64_t completion, VkAccelerationStructureKHR handle, Buffer storage) {
    if (!handle && !storage.handle) return;
    ray.retired.push_back({ completion, handle, storage });
}
void ray_collect(uint64_t complete) {
    if (!vkDestroyAccelerationStructureKHR) { ray.retired.clear(); return; }
    for (auto it = ray.retired.begin(); it != ray.retired.end();) {
        if (it->completion <= complete) {
            if (it->handle) vkDestroyAccelerationStructureKHR(device, it->handle, nullptr);
            destroy_buffer(it->storage);
            it = ray.retired.erase(it);
        } else ++it;
    }
}
Buffer ray_device_buffer(Buffer& current, uint64_t& completion, VkDeviceSize bytes, VkBufferUsageFlags usage, bool host) {
    if (current.handle && current.bytes >= bytes && completion <= stats.completed_timeline) { completion = next_completion + 1; return current; }
    if (current.handle) ray_retire(completion, {}, current);
    current = {};
    current = make_buffer(bytes, usage, host);
    completion = next_completion + 1;
    return current;
}
bool ray_invert(const float* m, float* o) {
    float s0 = m[0] * m[5] - m[4] * m[1], s1 = m[0] * m[6] - m[4] * m[2], s2 = m[0] * m[7] - m[4] * m[3];
    float s3 = m[1] * m[6] - m[5] * m[2], s4 = m[1] * m[7] - m[5] * m[3], s5 = m[2] * m[7] - m[6] * m[3];
    float c5 = m[10] * m[15] - m[14] * m[11], c4 = m[9] * m[15] - m[13] * m[11], c3 = m[9] * m[14] - m[13] * m[10];
    float c2 = m[8] * m[15] - m[12] * m[11], c1 = m[8] * m[14] - m[12] * m[10], c0 = m[8] * m[13] - m[12] * m[9];
    float det = s0 * c5 - s1 * c4 + s2 * c3 + s3 * c2 - s4 * c1 + s5 * c0;
    if (!(std::fabs(det) > 1e-12f)) return false;
    float inv = 1.f / det;
    o[0] = (m[5] * c5 - m[6] * c4 + m[7] * c3) * inv; o[1] = (-m[1] * c5 + m[2] * c4 - m[3] * c3) * inv;
    o[2] = (m[13] * s5 - m[14] * s4 + m[15] * s3) * inv; o[3] = (-m[9] * s5 + m[10] * s4 - m[11] * s3) * inv;
    o[4] = (-m[4] * c5 + m[6] * c2 - m[7] * c1) * inv; o[5] = (m[0] * c5 - m[2] * c2 + m[3] * c1) * inv;
    o[6] = (-m[12] * s5 + m[14] * s2 - m[15] * s1) * inv; o[7] = (m[8] * s5 - m[10] * s2 + m[11] * s1) * inv;
    o[8] = (m[4] * c4 - m[5] * c2 + m[7] * c0) * inv; o[9] = (-m[0] * c4 + m[1] * c2 - m[3] * c0) * inv;
    o[10] = (m[12] * s4 - m[13] * s2 + m[15] * s0) * inv; o[11] = (-m[8] * s4 + m[9] * s2 - m[11] * s0) * inv;
    o[12] = (-m[4] * c3 + m[5] * c1 - m[6] * c0) * inv; o[13] = (m[0] * c3 - m[1] * c1 + m[2] * c0) * inv;
    o[14] = (-m[12] * s3 + m[13] * s1 - m[14] * s0) * inv; o[15] = (m[8] * s3 - m[9] * s1 + m[10] * s0) * inv;
    return true;
}
bool ray_matrix(const float* rows, VkTransformMatrixKHR& out) {
    for (int i = 0; i < 12; ++i) if (!std::isfinite(rows[i])) return false;
    out.matrix[0][0] = rows[0]; out.matrix[0][1] = rows[4]; out.matrix[0][2] = rows[8]; out.matrix[0][3] = rows[3];
    out.matrix[1][0] = rows[1]; out.matrix[1][1] = rows[5]; out.matrix[1][2] = rows[9]; out.matrix[1][3] = rows[7];
    out.matrix[2][0] = rows[2]; out.matrix[2][1] = rows[6]; out.matrix[2][2] = rows[10]; out.matrix[2][3] = rows[11];
    return true;
}
void ray_prepare_shader() {
    if (ray.shader_attempted || !ray.supported) return;
    ray.shader_attempted = true;
    std::ifstream in(std::filesystem::path(shader_directory) / "ray_query_cs.spv", std::ios::binary);
    if (!in) return;
    std::vector<char> bytes((std::istreambuf_iterator<char>(in)), {});
    if (bytes.size() < 20 || bytes.size() % 4) return;
    VkShaderModuleCreateInfo module{ VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO }; module.codeSize = bytes.size(); module.pCode = reinterpret_cast<const uint32_t*>(bytes.data());
    check(vkCreateShaderModule(device, &module, nullptr, &ray.module), "create ray query shader");
    std::array<VkDescriptorSetLayoutBinding, 4> bindings{ {
        { 0, VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
        { 1, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
        { 2, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr },
        { 3, VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr } } };
    VkDescriptorSetLayoutCreateInfo layout{ VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO }; layout.bindingCount = 4; layout.pBindings = bindings.data();
    check(vkCreateDescriptorSetLayout(device, &layout, nullptr, &ray.layout), "create ray descriptor layout");
    VkPipelineLayoutCreateInfo pipeline_layout{ VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO }; pipeline_layout.setLayoutCount = 1; pipeline_layout.pSetLayouts = &ray.layout;
    check(vkCreatePipelineLayout(device, &pipeline_layout, nullptr, &ray.pipeline_layout), "create ray pipeline layout");
    VkPipelineShaderStageCreateInfo stage{ VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO }; stage.stage = VK_SHADER_STAGE_COMPUTE_BIT; stage.module = ray.module; stage.pName = "main";
    VkComputePipelineCreateInfo pipeline{ VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO }; pipeline.stage = stage; pipeline.layout = ray.pipeline_layout;
    check(vkCreateComputePipelines(device, pipeline_cache, 1, &pipeline, nullptr, &ray.pipeline), "create ray query pipeline");
    std::array<VkDescriptorPoolSize, 4> sizes{ { { VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR, 1 }, { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 1 }, { VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1 }, { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1 } } };
    VkDescriptorPoolCreateInfo pool{ VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO }; pool.maxSets = 1; pool.poolSizeCount = 4; pool.pPoolSizes = sizes.data();
    check(vkCreateDescriptorPool(device, &pool, nullptr, &ray.pool), "create ray descriptor pool");
    VkDescriptorSetAllocateInfo sets{ VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO }; sets.descriptorPool = ray.pool; sets.descriptorSetCount = 1; sets.pSetLayouts = &ray.layout;
    check(vkAllocateDescriptorSets(device, &sets, &ray.set), "allocate ray descriptor");
    ray.shader_ready = 1;
}
VkAccelerationStructureKHR ray_blas(uint64_t id, Mesh& mesh, VkCommandBuffer cmd, VkDeviceSize& scratch_need) {
    if (!mesh.ray_stamp || mesh.vertex_stride < 12 || mesh.vertex_stride % 4 || !mesh.index_count || mesh.index_count % 3 || mesh.vertices.bytes < mesh.vertex_stride) return {};
    uint32_t vertices = uint32_t(mesh.vertices.bytes / mesh.vertex_stride), primitives = mesh.index_count / 3;
    auto found = ray.blas.find(id);
    if (found != ray.blas.end() && found->second.stamp == mesh.ray_stamp && found->second.vertex == mesh.vertices.handle && found->second.vertex_offset == mesh.vertex_offset && found->second.index == mesh.indices.handle && found->second.index_offset == mesh.index_offset) return found->second.handle;
    VkAccelerationStructureGeometryKHR geometry{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR };
    geometry.geometryType = VK_GEOMETRY_TYPE_TRIANGLES_KHR; geometry.flags = VK_GEOMETRY_OPAQUE_BIT_KHR;
    geometry.geometry.triangles = { VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_TRIANGLES_DATA_KHR };
    geometry.geometry.triangles.vertexFormat = VK_FORMAT_R32G32B32_SFLOAT;
    geometry.geometry.triangles.vertexData.deviceAddress = ray_address(mesh.vertices.handle, mesh.vertex_offset);
    geometry.geometry.triangles.vertexStride = mesh.vertex_stride; geometry.geometry.triangles.maxVertex = vertices - 1;
    geometry.geometry.triangles.indexType = VK_INDEX_TYPE_UINT32;
    geometry.geometry.triangles.indexData.deviceAddress = ray_address(mesh.indices.handle, mesh.index_offset);
    VkAccelerationStructureBuildGeometryInfoKHR build{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_GEOMETRY_INFO_KHR };
    build.type = VK_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL_KHR; build.flags = VK_BUILD_ACCELERATION_STRUCTURE_PREFER_FAST_TRACE_BIT_KHR;
    build.mode = VK_BUILD_ACCELERATION_STRUCTURE_MODE_BUILD_KHR; build.geometryCount = 1; build.pGeometries = &geometry;
    VkAccelerationStructureBuildSizesInfoKHR sizes{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_SIZES_INFO_KHR };
    vkGetAccelerationStructureBuildSizesKHR(device, VK_ACCELERATION_STRUCTURE_BUILD_TYPE_DEVICE_KHR, &build, &primitives, &sizes);
    scratch_need = std::max(scratch_need, sizes.buildScratchSize);
    Buffer storage = make_buffer(sizes.accelerationStructureSize, VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_STORAGE_BIT_KHR | VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT, false);
    VkAccelerationStructureCreateInfoKHR create{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_CREATE_INFO_KHR };
    create.buffer = storage.handle; create.size = sizes.accelerationStructureSize; create.type = VK_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL_KHR;
    VkAccelerationStructureKHR handle{};
    try { check(vkCreateAccelerationStructureKHR(device, &create, nullptr, &handle), "create BLAS"); }
    catch (...) { destroy_buffer(storage); throw; }
    build.dstAccelerationStructure = handle; build.scratchData.deviceAddress = 0;
    if (found != ray.blas.end()) ray_retire(next_completion, found->second.handle, found->second.storage);
    RayBlas entry{}; entry.stamp = mesh.ray_stamp; entry.primitives = primitives; entry.vertex = mesh.vertices.handle; entry.index = mesh.indices.handle;
    entry.vertex_offset = mesh.vertex_offset; entry.index_offset = mesh.index_offset; entry.handle = handle; entry.storage = storage;
    ray.blas[id] = entry; ++ray.builds;
    return handle;
}
void trace_hardware_rays(VkCommandBuffer cmd) {
    SevkRayOptions options{};
    { std::lock_guard<std::mutex> lock(ray.mutex); options = ray.options; }
    if (!options.enable || !ray.supported || !ray.hdr_storage || !world.hdr.handle || !world.gbuffer[3].handle) return;
    try { ray_prepare_shader(); } catch (...) { ray.shader_ready = 0; }
    if (!ray.shader_ready) return;
    const uint8_t* objects = world.retained_objects ? world.object_records.data.data() : world.current().draw_objects.data();
    uint64_t object_bytes = world.retained_objects ? world.object_records.data.size() : world.current().draw_objects.size();
    struct Pending { uint64_t mesh; VkTransformMatrixKHR transform; };
    std::vector<Pending> pending; pending.reserve(256);
    uint32_t triangles = 0;
    auto push = [&](uint64_t id, Mesh& mesh, const float* rows) {
        VkTransformMatrixKHR transform{}; if (!ray_matrix(rows, transform)) return;
        uint32_t primitives = mesh.index_count / 3; if (!primitives) return;
        if (triangles > ray.max_primitives || pending.size() >= ray.max_instances) { triangles += primitives; pending.push_back({ id, transform }); return; }
        triangles += primitives; pending.push_back({ id, transform });
    };
    for (uint32_t index : world.current().main_selection) {
        auto& draw = world.current().draws[index]; if (!draw.geometry || draw.packet.pipeline >= 20 || draw.packet.pipeline >= world.pipelines.size()) continue;
        uint32_t type = world.pipelines[draw.packet.pipeline].type; if (type == 1 || type >= 3) continue;
        Mesh& mesh = *draw.geometry;
        if (type == 0) {
            if (draw.object_n < 48 || uint64_t(draw.object_at) + 48 > object_bytes) continue;
            push(draw.packet.mesh, mesh, reinterpret_cast<const float*>(objects + draw.object_at));
        } else if (draw.instance_n >= 64 && uint64_t(draw.instance_at) + draw.instance_n <= world.current().instances.data.size()) {
            for (uint32_t i = 0; i < draw.instance_n / 64; ++i) push(draw.packet.mesh, mesh, reinterpret_cast<const float*>(world.current().instances.data.data() + draw.instance_at + i * 64));
        }
    }
    if (!sevk_ray_dispatch_allowed(options.enable, 1, uint32_t(pending.size()), triangles, ray.max_instances, ray.max_primitives)) {
        if (pending.size() > ray.max_instances || triangles > ray.max_primitives) { std::lock_guard<std::mutex> lock(ray.mutex); ++ray.refused; }
        return;
    }
    VkMemoryBarrier2 geometry_barrier{ VK_STRUCTURE_TYPE_MEMORY_BARRIER_2 };
    geometry_barrier.srcStageMask = VK_PIPELINE_STAGE_2_VERTEX_INPUT_BIT | VK_PIPELINE_STAGE_2_TRANSFER_BIT;
    geometry_barrier.srcAccessMask = VK_ACCESS_2_VERTEX_ATTRIBUTE_READ_BIT | VK_ACCESS_2_INDEX_READ_BIT | VK_ACCESS_2_TRANSFER_WRITE_BIT;
    geometry_barrier.dstStageMask = VK_PIPELINE_STAGE_2_ACCELERATION_STRUCTURE_BUILD_BIT_KHR;
    geometry_barrier.dstAccessMask = VK_ACCESS_2_ACCELERATION_STRUCTURE_READ_BIT_KHR;
    VkDependencyInfo geometry_dependency{ VK_STRUCTURE_TYPE_DEPENDENCY_INFO }; geometry_dependency.memoryBarrierCount = 1; geometry_dependency.pMemoryBarriers = &geometry_barrier;
    vkCmdPipelineBarrier2(cmd, &geometry_dependency);
    std::vector<VkAccelerationStructureBuildGeometryInfoKHR> builds; std::vector<VkAccelerationStructureGeometryKHR> geometries; std::vector<VkAccelerationStructureBuildRangeInfoKHR> ranges;
    VkDeviceSize scratch_need = 0;
    for (auto& item : pending) {
        auto mesh = world.meshes.find(item.mesh); if (mesh == world.meshes.end()) continue;
        auto before = ray.blas.find(item.mesh);
        bool fresh = before == ray.blas.end() || before->second.stamp != mesh->second.ray_stamp;
        auto handle = ray_blas(item.mesh, mesh->second, cmd, scratch_need);
        if (!handle || !fresh) continue;
        VkAccelerationStructureGeometryKHR geometry{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR };
        geometry.geometryType = VK_GEOMETRY_TYPE_TRIANGLES_KHR; geometry.flags = VK_GEOMETRY_OPAQUE_BIT_KHR;
        geometry.geometry.triangles = { VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_TRIANGLES_DATA_KHR };
        geometry.geometry.triangles.vertexFormat = VK_FORMAT_R32G32B32_SFLOAT;
        geometry.geometry.triangles.vertexData.deviceAddress = ray_address(mesh->second.vertices.handle, mesh->second.vertex_offset);
        geometry.geometry.triangles.vertexStride = mesh->second.vertex_stride; geometry.geometry.triangles.maxVertex = uint32_t(mesh->second.vertices.bytes / mesh->second.vertex_stride) - 1;
        geometry.geometry.triangles.indexType = VK_INDEX_TYPE_UINT32;
        geometry.geometry.triangles.indexData.deviceAddress = ray_address(mesh->second.indices.handle, mesh->second.index_offset);
        geometries.push_back(geometry);
        VkAccelerationStructureBuildGeometryInfoKHR build{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_GEOMETRY_INFO_KHR };
        build.type = VK_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL_KHR; build.flags = VK_BUILD_ACCELERATION_STRUCTURE_PREFER_FAST_TRACE_BIT_KHR;
        build.mode = VK_BUILD_ACCELERATION_STRUCTURE_MODE_BUILD_KHR; build.dstAccelerationStructure = handle;
        builds.push_back(build);
        ranges.push_back({ mesh->second.index_count / 3, 0, 0, 0 });
    }
    if (scratch_need) {
        auto scratch = ray_device_buffer(ray.scratch, ray.scratch_completion, (scratch_need + 255) & ~VkDeviceSize(255), VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT, false);
        VkDeviceAddress scratch_address = ray_address(scratch.handle);
        for (auto& build : builds) build.scratchData.deviceAddress = scratch_address;
    }
    for (uint32_t i = 0; i < builds.size(); ++i) {
        builds[i].geometryCount = 1; builds[i].pGeometries = &geometries[i];
        const VkAccelerationStructureBuildRangeInfoKHR* range = &ranges[i];
        vkCmdBuildAccelerationStructuresKHR(cmd, 1, &builds[i], &range);
        VkMemoryBarrier2 step{ VK_STRUCTURE_TYPE_MEMORY_BARRIER_2 };
        step.srcStageMask = VK_PIPELINE_STAGE_2_ACCELERATION_STRUCTURE_BUILD_BIT_KHR; step.srcAccessMask = VK_ACCESS_2_ACCELERATION_STRUCTURE_WRITE_BIT_KHR;
        step.dstStageMask = VK_PIPELINE_STAGE_2_ACCELERATION_STRUCTURE_BUILD_BIT_KHR; step.dstAccessMask = VK_ACCESS_2_ACCELERATION_STRUCTURE_READ_BIT_KHR;
        VkDependencyInfo dependency{ VK_STRUCTURE_TYPE_DEPENDENCY_INFO }; dependency.memoryBarrierCount = 1; dependency.pMemoryBarriers = &step;
        vkCmdPipelineBarrier2(cmd, &dependency);
    }
    std::vector<VkAccelerationStructureInstanceKHR> instances; instances.reserve(pending.size());
    for (auto& item : pending) {
        auto blas = ray.blas.find(item.mesh); if (blas == ray.blas.end() || !blas->second.handle) continue;
        VkAccelerationStructureDeviceAddressInfoKHR address{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_DEVICE_ADDRESS_INFO_KHR }; address.accelerationStructure = blas->second.handle;
        VkAccelerationStructureInstanceKHR instance{}; instance.transform = item.transform; instance.mask = 0xFF;
        instance.flags = VK_GEOMETRY_INSTANCE_TRIANGLE_FACING_CULL_DISABLE_BIT_KHR;
        instance.accelerationStructureReference = vkGetAccelerationStructureDeviceAddressKHR(device, &address);
        instances.push_back(instance);
    }
    if (instances.empty()) return;
    auto instance_buffer = ray_device_buffer(ray.instances, ray.instance_completion, instances.size() * sizeof(VkAccelerationStructureInstanceKHR), VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT_KHR | VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT, true);
    std::memcpy(instance_buffer.mapped, instances.data(), instances.size() * sizeof(VkAccelerationStructureInstanceKHR));
    check(vmaFlushAllocation(allocator, instance_buffer.memory, 0, VK_WHOLE_SIZE), "flush ray instances");
    VkMemoryBarrier2 host_barrier{ VK_STRUCTURE_TYPE_MEMORY_BARRIER_2 };
    host_barrier.srcStageMask = VK_PIPELINE_STAGE_2_HOST_BIT; host_barrier.srcAccessMask = VK_ACCESS_2_HOST_WRITE_BIT;
    host_barrier.dstStageMask = VK_PIPELINE_STAGE_2_ACCELERATION_STRUCTURE_BUILD_BIT_KHR; host_barrier.dstAccessMask = VK_ACCESS_2_ACCELERATION_STRUCTURE_READ_BIT_KHR;
    VkDependencyInfo host_dependency{ VK_STRUCTURE_TYPE_DEPENDENCY_INFO }; host_dependency.memoryBarrierCount = 1; host_dependency.pMemoryBarriers = &host_barrier;
    vkCmdPipelineBarrier2(cmd, &host_dependency);
    if (ray.tlas && ray.tlas_completion <= stats.completed_timeline) { vkDestroyAccelerationStructureKHR(device, ray.tlas, nullptr); ray.tlas = {}; }
    else if (ray.tlas) { ray_retire(ray.tlas_completion, ray.tlas, ray.tlas_storage); ray.tlas = {}; ray.tlas_storage = {}; }
    VkAccelerationStructureGeometryKHR tlas_geometry{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR };
    tlas_geometry.geometryType = VK_GEOMETRY_TYPE_INSTANCES_KHR;
    tlas_geometry.geometry.instances = { VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_INSTANCES_DATA_KHR };
    tlas_geometry.geometry.instances.arrayOfPointers = VK_FALSE; tlas_geometry.geometry.instances.data.deviceAddress = ray_address(instance_buffer.handle);
    uint32_t instance_count = uint32_t(instances.size());
    VkAccelerationStructureBuildGeometryInfoKHR tlas_build{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_GEOMETRY_INFO_KHR };
    tlas_build.type = VK_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL_KHR; tlas_build.flags = VK_BUILD_ACCELERATION_STRUCTURE_PREFER_FAST_TRACE_BIT_KHR;
    tlas_build.mode = VK_BUILD_ACCELERATION_STRUCTURE_MODE_BUILD_KHR; tlas_build.geometryCount = 1; tlas_build.pGeometries = &tlas_geometry;
    VkAccelerationStructureBuildSizesInfoKHR tlas_sizes{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_SIZES_INFO_KHR };
    vkGetAccelerationStructureBuildSizesKHR(device, VK_ACCELERATION_STRUCTURE_BUILD_TYPE_DEVICE_KHR, &tlas_build, &instance_count, &tlas_sizes);
    if (!ray.tlas_storage.handle || ray.tlas_storage.bytes < tlas_sizes.accelerationStructureSize) {
        if (ray.tlas_storage.handle) ray_retire(ray.tlas_completion, {}, ray.tlas_storage);
        ray.tlas_storage = make_buffer(tlas_sizes.accelerationStructureSize, VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_STORAGE_BIT_KHR | VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT, false);
    }
    VkAccelerationStructureCreateInfoKHR tlas_info{ VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_CREATE_INFO_KHR };
    tlas_info.buffer = ray.tlas_storage.handle; tlas_info.size = tlas_sizes.accelerationStructureSize; tlas_info.type = VK_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL_KHR;
    check(vkCreateAccelerationStructureKHR(device, &tlas_info, nullptr, &ray.tlas), "create TLAS");
    auto tlas_scratch = ray_device_buffer(ray.scratch, ray.scratch_completion, std::max(scratch_need, tlas_sizes.buildScratchSize + 255) & ~VkDeviceSize(255), VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT, false);
    tlas_build.dstAccelerationStructure = ray.tlas; tlas_build.scratchData.deviceAddress = ray_address(tlas_scratch.handle);
    VkAccelerationStructureBuildRangeInfoKHR tlas_range{ instance_count, 0, 0, 0 }; const VkAccelerationStructureBuildRangeInfoKHR* tlas_ranges = &tlas_range;
    vkCmdBuildAccelerationStructuresKHR(cmd, 1, &tlas_build, &tlas_ranges);
    ray.tlas_completion = next_completion + 1;
    VkMemoryBarrier2 built{ VK_STRUCTURE_TYPE_MEMORY_BARRIER_2 };
    built.srcStageMask = VK_PIPELINE_STAGE_2_ACCELERATION_STRUCTURE_BUILD_BIT_KHR; built.srcAccessMask = VK_ACCESS_2_ACCELERATION_STRUCTURE_WRITE_BIT_KHR;
    built.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT; built.dstAccessMask = VK_ACCESS_2_ACCELERATION_STRUCTURE_READ_BIT_KHR;
    VkDependencyInfo built_dependency{ VK_STRUCTURE_TYPE_DEPENDENCY_INFO }; built_dependency.memoryBarrierCount = 1; built_dependency.pMemoryBarriers = &built;
    vkCmdPipelineBarrier2(cmd, &built_dependency);
    struct RayParams { float inv_view[16], inv_proj[16], sun_direction[4], sun_color[4], settings[4]; uint32_t width, height, light_count, reserved; float light_pos[8][4], light_color[8][4]; };
    static_assert(sizeof(RayParams) == 448, "ray uniform layout");
    RayParams params{};
    if (!ray_invert(reinterpret_cast<const float*>(world.current().frame.data()), params.inv_view)) return;
    std::memcpy(params.inv_proj, world.current().inverse.data(), sizeof(params.inv_proj));
    std::memcpy(params.sun_direction, submitted_frame.sunlight_direction, 16);
    std::memcpy(params.sun_color, submitted_frame.sunlight_color, 16);
    params.settings[0] = options.shadow_bias; params.settings[1] = options.exposure; params.settings[2] = options.solar_radians; params.settings[3] = options.shadows ? 1.f : 0.f;
    params.width = extent.width; params.height = extent.height;
    for (uint32_t i = 0; i < light_count && params.light_count < 8; ++i) {
        if (lights[i].flags & 1) continue;
        for (int c = 0; c < 3; ++c) { params.light_pos[params.light_count][c] = lights[i].position[c]; params.light_color[params.light_count][c] = lights[i].color[c] * lights[i].intensity; }
        params.light_pos[params.light_count][3] = lights[i].range; ++params.light_count;
    }
    auto uniforms = ray_device_buffer(ray.uniforms, ray.uniform_completion, sizeof(params), VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT, true);
    std::memcpy(uniforms.mapped, &params, sizeof(params));
    check(vmaFlushAllocation(allocator, uniforms.memory, 0, VK_WHOLE_SIZE), "flush ray uniforms");
    VkWriteDescriptorSetAccelerationStructureKHR written_as{ VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET_ACCELERATION_STRUCTURE_KHR }; written_as.accelerationStructureCount = 1; written_as.pAccelerationStructures = &ray.tlas;
    VkDescriptorBufferInfo uniform_info{ uniforms.handle, 0, sizeof(params) };
    VkDescriptorImageInfo depth_info{ VK_NULL_HANDLE, world.gbuffer[3].view, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL };
    VkDescriptorImageInfo hdr_info{ VK_NULL_HANDLE, world.hdr.view, VK_IMAGE_LAYOUT_GENERAL };
    std::array<VkWriteDescriptorSet, 4> writes{};
    for (auto& write : writes) { write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET; write.dstSet = ray.set; write.descriptorCount = 1; }
    writes[0].pNext = &written_as; writes[0].dstBinding = 0; writes[0].descriptorType = VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR;
    writes[1].dstBinding = 1; writes[1].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER; writes[1].pBufferInfo = &uniform_info;
    writes[2].dstBinding = 2; writes[2].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE; writes[2].pImageInfo = &depth_info;
    writes[3].dstBinding = 3; writes[3].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE; writes[3].pImageInfo = &hdr_info;
    vkUpdateDescriptorSets(device, 4, writes.data(), 0, nullptr);
    barrier(cmd, world.hdr.handle, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_ASPECT_COLOR_BIT, VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT, VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_READ_BIT | VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT);
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, ray.pipeline);
    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, ray.pipeline_layout, 0, 1, &ray.set, 0, nullptr);
    vkCmdDispatch(cmd, (extent.width + 7) / 8, (extent.height + 7) / 8, 1);
    barrier(cmd, world.hdr.handle, VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT, VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT, VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT, VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
    { std::lock_guard<std::mutex> lock(ray.mutex); ++ray.dispatches; }
}
int get_ray_capabilities(SevkRayCapabilities* caps) {
    if (!caps || caps->size != sizeof(SevkRayCapabilities) || caps->abi_version != SEVK_ABI_VERSION) throw std::runtime_error("ray capability ABI mismatch");
    SevkRayCapabilities out{ sizeof(out), SEVK_ABI_VERSION };
    std::lock_guard<std::mutex> lock(ray.mutex);
    out.supported = ray.supported ? 1u : 0u; out.ray_query = out.supported; out.max_primitives = ray.max_primitives; out.max_instances = ray.max_instances;
    out.shader_ready = ray.shader_ready; out.refused = ray.refused; out.builds = ray.builds; out.dispatches = ray.dispatches;
    *caps = out; return 0;
}
int set_ray_options(const SevkRayOptions& options) {
    if (options.size != sizeof(SevkRayOptions) || options.abi_version != SEVK_ABI_VERSION) throw std::runtime_error("ray option ABI mismatch");
    std::lock_guard<std::mutex> lock(ray.mutex); ray.options = options; return 0;
}
void cleanup_ray() noexcept {
    if (!device) return;
    if (ray.tlas && vkDestroyAccelerationStructureKHR) vkDestroyAccelerationStructureKHR(device, ray.tlas, nullptr);
    for (auto& item : ray.blas) if (item.second.handle && vkDestroyAccelerationStructureKHR) vkDestroyAccelerationStructureKHR(device, item.second.handle, nullptr);
    for (auto& item : ray.retired) { if (item.handle && vkDestroyAccelerationStructureKHR) vkDestroyAccelerationStructureKHR(device, item.handle, nullptr); destroy_buffer(item.storage); }
    ray.blas.clear(); ray.retired.clear(); ray.tlas = {};
    destroy_buffer(ray.tlas_storage); destroy_buffer(ray.scratch); destroy_buffer(ray.instances); destroy_buffer(ray.uniforms);
    if (ray.pipeline) vkDestroyPipeline(device, ray.pipeline, nullptr);
    if (ray.pipeline_layout) vkDestroyPipelineLayout(device, ray.pipeline_layout, nullptr);
    if (ray.layout) vkDestroyDescriptorSetLayout(device, ray.layout, nullptr);
    if (ray.pool) vkDestroyDescriptorPool(device, ray.pool, nullptr);
    if (ray.module) vkDestroyShaderModule(device, ray.module, nullptr);
    ray.pipeline = {}; ray.pipeline_layout = {}; ray.layout = {}; ray.pool = {}; ray.set = {}; ray.module = {}; ray.shader_ready = 0;
}
