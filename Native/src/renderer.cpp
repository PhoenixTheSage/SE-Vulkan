#include <unordered_set>
#include <deque>
#include "se_vulkan.h"
#include "presentation_capture.h"
#include "ray_policy.h"
#include "ray_transform.h"
#include "gpu_opaque_admission.h"
#include "world_instance_arena.h"
#include "world_indirect_plan.h"
#include "world_object_records.h"
#include "world_texture_validation_cache.h"
#include "spot_bounds.h"
#include "native_pass_scene.h"
#include "old_cells.h"
#include "gpu_opaque_registry.h"
#include "gpu_appearance_arena.h"
#include "gpu_scene_costs.h"
#include "gpu_opaque_view_batches.h"
#include <volk.h>
#if defined(_WIN32)
#include <windows.h>
#include <dbghelp.h>
#include <dxgi1_6.h>
#pragma comment(lib,"dbghelp.lib")
#endif
#define VMA_IMPLEMENTATION
#include <vk_mem_alloc.h>
#include <spirv_reflect.h>
#include "stage_interface.h"
#include <algorithm>
#include <array>
#include <bit>
#include <cstring>
#include <cstdlib>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <string>
#include <chrono>
#include <unordered_map>
#include <vector>
#include <future>
#include <functional>
#include <condition_variable>
#include <thread>
#include <memory>
#include <optional>
#include <source_location>
#include "critical_profile.h"
#if defined(_WIN32)
#include <windows.h>
#else
#include <dlfcn.h>
#endif
// Diagnostic wrappers preserve the exact Vulkan calls and return values.
// Only sampled frames bind a thread-local collector; recording workers own
// separate lanes and are joined before the owner reduces their counters.
#define vkCmdBindPipeline(...) critical_profile::api(critical_profile::CommandApi,[&]{return ::vkCmdBindPipeline(__VA_ARGS__);})
#define vkCmdBindDescriptorSets(...) critical_profile::api(critical_profile::CommandApi,[&]{return ::vkCmdBindDescriptorSets(__VA_ARGS__);})
#define vkCmdBindVertexBuffers(...) critical_profile::api(critical_profile::CommandApi,[&]{return ::vkCmdBindVertexBuffers(__VA_ARGS__);})
#define vkCmdBindIndexBuffer(...) critical_profile::api(critical_profile::CommandApi,[&]{return ::vkCmdBindIndexBuffer(__VA_ARGS__);})
#define vkCmdDrawIndexed(...) critical_profile::api(critical_profile::CommandApi,[&]{return ::vkCmdDrawIndexed(__VA_ARGS__);})
#define vkCmdDrawIndexedIndirect(...) critical_profile::api(critical_profile::CommandApi,[&]{return ::vkCmdDrawIndexedIndirect(__VA_ARGS__);})
#define vkCmdDrawIndexedIndirectCount(...) critical_profile::api(critical_profile::CommandApi,[&]{return ::vkCmdDrawIndexedIndirectCount(__VA_ARGS__);})
#define vkCmdDraw(...) critical_profile::api(critical_profile::CommandApi,[&]{return ::vkCmdDraw(__VA_ARGS__);})
#define vkCmdDispatch(...) critical_profile::api(critical_profile::CommandApi,[&]{return ::vkCmdDispatch(__VA_ARGS__);})
#define vkCmdPipelineBarrier2(...) critical_profile::api(critical_profile::BarrierApi,[&]{return ::vkCmdPipelineBarrier2(__VA_ARGS__);})
#define vkUpdateDescriptorSets(...) critical_profile::api(critical_profile::DescriptorApi,[&]{return ::vkUpdateDescriptorSets(__VA_ARGS__);})
#define vkAllocateDescriptorSets(...) critical_profile::api(critical_profile::DescriptorApi,[&]{return ::vkAllocateDescriptorSets(__VA_ARGS__);})
#define vkFreeDescriptorSets(...) critical_profile::api(critical_profile::DescriptorApi,[&]{return ::vkFreeDescriptorSets(__VA_ARGS__);})
#define vkResetDescriptorPool(...) critical_profile::api(critical_profile::PoolApi,[&]{return ::vkResetDescriptorPool(__VA_ARGS__);})
#define vkResetCommandPool(...) critical_profile::api(critical_profile::PoolApi,[&]{return ::vkResetCommandPool(__VA_ARGS__);})
#define vkResetCommandBuffer(...) critical_profile::api(critical_profile::PoolApi,[&]{return ::vkResetCommandBuffer(__VA_ARGS__);})
#define vkQueueSubmit2(...) critical_profile::api(critical_profile::SubmitApi,[&]{return ::vkQueueSubmit2(__VA_ARGS__);})
#define vkWaitSemaphores(...) critical_profile::api(critical_profile::WaitApi,[&]{return ::vkWaitSemaphores(__VA_ARGS__);})
#define vkAcquireNextImageKHR(...) critical_profile::api(critical_profile::AcquireApi,[&]{return ::vkAcquireNextImageKHR(__VA_ARGS__);})
#define vmaFlushAllocation(...) critical_profile::api(critical_profile::FlushApi,[&]{return ::vmaFlushAllocation(__VA_ARGS__);})

namespace {
constexpr VkFormat stock_probe_format=VK_FORMAT_B10G11R11_UFLOAT_PACK32;
thread_local std::string global_error;
void check(VkResult result, const char* operation) {
    if (result != VK_SUCCESS) throw std::runtime_error(std::string(operation) + " failed (VkResult " + std::to_string(result) + ")");
}
template<size_t N> void copy_text(char (&to)[N], const std::string& text) {
    std::memset(to, 0, N); std::memcpy(to, text.data(), std::min(N - 1, text.size()));
}
constexpr uint32_t minimum_api = VK_API_VERSION_1_4;
constexpr uint32_t required_features = 1 | 2 | 4 | 8 | 16 | 32 | 1024 | 2048 | 4096 | 8192 | 16384; // depth clamp for stock billboard rasterization; dynamic rendering, sync2, timeline, scalar, BC, shader demote, geometry, extended storage formats
bool features_supported(VkPhysicalDevice device, uint32_t& flags) {
    VkPhysicalDeviceVulkan14Features f14{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_4_FEATURES };
    VkPhysicalDeviceVulkan13Features f13{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES, &f14 };
    VkPhysicalDeviceVulkan12Features f12{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES, &f13 };
    VkPhysicalDeviceFeatures2 f{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &f12 };
    vkGetPhysicalDeviceFeatures2(device, &f);
    flags = (f13.dynamicRendering ? 1 : 0) | (f13.synchronization2 ? 2 : 0) |
        (f12.timelineSemaphore ? 4 : 0) | (f12.scalarBlockLayout ? 8 : 0) | (f.features.textureCompressionBC ? 16 : 0) | (f13.shaderDemoteToHelperInvocation?32:0) | (f.features.geometryShader?1024:0) | (f.features.shaderStorageImageExtendedFormats?2048:0) | (f.features.independentBlend?4096:0) | (f.features.robustBufferAccess?8192:0) | (f.features.depthClamp?16384:0);
    VkPhysicalDeviceVulkan14Properties p14{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_4_PROPERTIES };
    VkPhysicalDeviceProperties2 properties{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2, &p14 };
    vkGetPhysicalDeviceProperties2(device, &properties);
    flags |= (f14.hostImageCopy ? 64 : 0) | (f14.dynamicRenderingLocalRead ? 128 : 0) |
        (p14.dynamicRenderingLocalReadDepthStencilAttachments ? 256 : 0) |
        (p14.dynamicRenderingLocalReadMultisampledAttachments ? 512 : 0);
    return (flags & required_features) == required_features;
}
bool layer_available(const char* name) {
    uint32_t count = 0; check(vkEnumerateInstanceLayerProperties(&count, nullptr), "enumerate layers");
    std::vector<VkLayerProperties> layers(count); check(vkEnumerateInstanceLayerProperties(&count, layers.data()), "enumerate layers");
    return std::any_of(layers.begin(), layers.end(), [name](const auto& l) { return std::strcmp(l.layerName, name) == 0; });
}
void load_loader(void* address = nullptr) {
    if (address) volkInitializeCustom(reinterpret_cast<PFN_vkGetInstanceProcAddr>(address));
    else check(volkInitialize(), "load Vulkan loader");
    uint32_t version = VK_API_VERSION_1_0;
    if (vkEnumerateInstanceVersion) check(vkEnumerateInstanceVersion(&version), "query loader API");
    if (version < minimum_api) throw std::runtime_error("Vulkan 1.4 loader required; update the graphics driver");
}
VkInstance create_instance(const std::vector<const char*>& extensions, bool validation, void* callback_data,
    PFN_vkDebugUtilsMessengerCallbackEXT callback) {
    VkApplicationInfo application{ VK_STRUCTURE_TYPE_APPLICATION_INFO };
    application.pApplicationName = "SE-Vulkan"; application.applicationVersion = VK_MAKE_VERSION(0, 1, 0);
    application.pEngineName = "SE-Vulkan native"; application.engineVersion = SEVK_ABI_VERSION; application.apiVersion = minimum_api;
    const char* layer = "VK_LAYER_KHRONOS_validation";
    VkDebugUtilsMessengerCreateInfoEXT debug{ VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT };
    debug.messageSeverity = VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
    debug.messageType = VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
    debug.pfnUserCallback = callback; debug.pUserData = callback_data;
    VkInstanceCreateInfo info{ VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO };
    info.pApplicationInfo = &application; info.enabledExtensionCount = static_cast<uint32_t>(extensions.size()); info.ppEnabledExtensionNames = extensions.data();
    if (validation) { info.enabledLayerCount = 1; info.ppEnabledLayerNames = &layer; info.pNext = &debug; }
    VkInstance instance{}; check(vkCreateInstance(&info, nullptr, &instance), "create Vulkan 1.4 instance");
    volkLoadInstance(instance); return instance;
}
struct Buffer { VkBuffer handle{}; VmaAllocation memory{}; void* mapped{}; VkDeviceSize bytes{}; VkBufferUsageFlags usage{}; };
thread_local const Buffer* active_buffer_destroy{};
thread_local std::source_location active_buffer_destroy_source;
struct Image { VkImage handle{}; VmaAllocation memory{}; VkImageView view{}; };
struct Texture { Image image{}; VkDescriptorSet descriptor{}, sprite_descriptor{}; VkImageView attachment{}; uint32_t stream_width{},stream_height{},stream_format{}; };
struct Mesh { Buffer vertices{}, indices{}; uint32_t index_count{}, vertex_stride{};
    VkDeviceSize vertex_offset{},index_offset{};uint32_t vertex_page{},index_page{};
    VmaVirtualAllocation vertex_allocation{},index_allocation{};
    std::shared_ptr<Mesh> geometry_owner;
    uint64_t ray_stamp{};
};
struct GeometryPage {Buffer buffer{};VmaVirtualBlock block{};uint32_t unit{};};
struct MaterialKey { std::array<uint64_t,4> ids{}; bool operator==(const MaterialKey&) const = default; };
struct MaterialHash { size_t operator()(const MaterialKey& key) const noexcept { size_t h=0; for(uint64_t id:key.ids) h^=std::hash<uint64_t>{}(id)+0x9e3779b9+(h<<6)+(h>>2);return h; } };
struct Slot { Buffer lines{};
    VkCommandPool pool{}; VkCommandBuffer command{}; VkSemaphore acquired{};
    Buffer uniform{}; VkDescriptorSet descriptor{}; uint64_t completion{};
    std::vector<VkDescriptorSet> draw_descriptors;
    std::unordered_map<MaterialKey,VkDescriptorSet,MaterialHash> materials;
};
struct Retired { uint64_t completion{}; Texture texture{}; Mesh mesh{}; };
struct UploadSlot { VkCommandPool pool{}; VkCommandBuffer command{}; uint64_t completion{}; std::vector<Buffer> pending, reusable; uint64_t cached_bytes{}; };
struct SpritePush { float rect[4], uv[4], color[4], screen[2], angle; uint32_t flags; float origin[2], pad[2]; float hdr_ui[4]; };
struct MeshPush { float world[16], tint[4], hsv[4], dither, emissivity; uint32_t flags, pad; };
struct NativeFrame { SevkFrameInfo frame; uint32_t light_count, reserved[3]; SevkLight lights[64]; float shadow_matrices[4][16], shadow_splits[4]; uint32_t shadow_count, shadow_reserved[3]; };
struct StockGBuffer {
    VkDescriptorSetLayout layout{}; VkPipelineLayout pipeline_layout{}; VkPipeline pipeline{}; VkDescriptorPool pool{};
    std::array<Image,4> images{}; Image dither{}; std::unordered_map<uint64_t,Mesh> meshes;
    Buffer frame{}, projection{}, objects{}; VkDeviceSize object_stride{}; uint32_t object_capacity{};
    SevkGBufferStats stats{}; bool initialized{};
};
struct WorldPipeline { VkPipeline handle{}; uint32_t object_bytes{},type{}; bool indirect_safe{};VkPipeline stock_cascade{},stock_spot{}; uint32_t row0=0xFFFFFFFFu,row1=0xFFFFFFFFu,row2=0xFFFFFFFFu; bool velocity{}; };
struct PassGpuState {
    native_pass::Scene scene;bool ready{};
    struct Pipeline{VkPipeline back{},no_cull{};bool velocity{};};std::array<Pipeline,28> pipelines{};
    std::array<Buffer,2> instances{},objects{},history_instances{};std::array<uint8_t,128> placeholder{};
    std::unordered_map<uint64_t,std::array<float,12>> history_rows;std::vector<uint32_t> history_offsets;uint64_t history_frame=UINT64_MAX;
    std::vector<uint32_t> offsets;std::vector<VkDescriptorSet> materials;std::array<uint32_t,64> spot_views{};uint64_t static_dirty{};
};
struct AtlasTransient { uint64_t completion{};std::vector<VkDescriptorSet> sets;std::vector<VkImageView> views; };
struct OpaqueReferenceState {
    struct Depth {Image image;uint32_t width{},height{};bool valid{};double camera[3]{};float projection[16]{};};
    std::vector<Depth> depths;
    struct Slot {Buffer actors,views,selections,groups,references,args,draws,counts,stock_objects,stock_colour,stock_depth,stock_draws,scan,object_inputs,parts;VkDescriptorSet set{};std::vector<uint64_t> versions,appearance_versions;std::vector<std::pair<uint32_t,uint32_t>> pending_parts;};
    std::array<Slot,2> slots;VkQueryPool compute_timing{};bool compute_timing_valid[2]{};
    Buffer families,parts,sources,vertices,indices,selectors,spot_signatures,role_parts;
    VkDescriptorSetLayout descriptor_layout{};VkPipelineLayout layout{};VkDescriptorPool pool{};
    VkPipeline compute{},graphics{},depth_pipeline{};std::array<VkShaderModule,4> modules{};
    struct Range {uint32_t first,capacity;};std::vector<Range> ranges;
    std::vector<SevkOpaqueActor> actors;std::vector<SevkOpaqueFamily> family_inputs;std::vector<SevkOpaquePart> part_inputs,gpu_part_inputs;
    uint64_t reference_colour_bound{},reference_depth_bound{},reference_forward_bound{};std::vector<uint8_t> source_inputs;
    gpu_opaque::AppearanceArena object_inputs;
    std::vector<uint64_t> material_ids;std::array<bool,2> materials_dirty{};
    std::vector<uint64_t> versions;std::vector<SevkOpaqueView> views;
    SevkOpaqueFrame frame{};uint32_t part_count{},view_count{},view_capacity{},reference_capacity{},draw_capacity{},source_count{},last_slot{};
    uint64_t revision{};uint32_t batch_count=1;bool initialized{},frame_ready{},published{};
    bool view_batches{},role_streams{},stock_geometry{};std::vector<std::vector<uint32_t>> recording_batches;
    std::vector<gpu_opaque::FamilyBatches> recording_families;
};
struct OpaqueStockState {
    struct Pipeline {VkPipeline back{},no_cull{};VkPipelineLayout layout{};bool velocity{};};
    std::array<Pipeline,28> newer;std::array<Pipeline,2656> older;
    VkDescriptorSetLayout indexed_layout{};VkDescriptorPool pool{};
    std::array<std::array<VkDescriptorSet,2>,2> indexed_sets{};
    std::array<bool,2> materials_dirty{true,true};
    struct Batch {SevkOpaqueStockBatch input;VkBuffer vertices{},indices{};};
    struct Geometry {uint64_t id,stamp;VkBuffer vertices,indices;VkDeviceSize vertex_offset,index_offset;};
    std::vector<Batch> batches;std::vector<Geometry> geometry;
    bool ready{};
};
struct IndexedMotionState {
    struct Actor {std::array<float,12> rows{};uint32_t source_first{},source_count{},type{};float fade{};bool active{};};
    std::unordered_map<uint64_t,Actor> actors;
    std::shared_ptr<const std::vector<uint8_t>> sources,uploaded_sources[2];
    Buffer previous_rows[2]{},previous_sources[2]{};VkPipeline pipelines[5]{};
    uint64_t frame=UINT64_MAX,source_epoch=UINT64_MAX;
};
struct GpuResidentState {uint64_t publications{},frames{};gpu_scene::Registry registry;gpu_scene::Upload upload;uint64_t topology{},revision{};bool published{};std::array<uint32_t,64> view_map{};std::vector<SevkPassQuery> queries;std::unordered_map<uint64_t,std::vector<uint32_t>> group_actors;
    std::array<uint64_t,5> topology_counts{};
    std::unordered_map<uint64_t,uint32_t> actor_indices;
    std::map<std::shared_ptr<const gpu_scene::Family>,uint32_t> family_indices;
    std::map<std::shared_ptr<const gpu_scene::Source>,uint32_t> source_offsets;
};
struct VoxelMaterial { Buffer constants; uint64_t arrays[3]{},random{}; };
struct WorldRecipe;
struct WorldDraw {WorldRecipe* recipe{};uint32_t recipe_generation{}; SevkWorldDraw packet{}; uint32_t object_at{}, object_n{}, instance_at{}, instance_n{}, gpu_object_at{},main_order{},main_part{},owner{};SevkOldPartOrder old_order{}; Mesh* geometry{}; VkDescriptorSet material{}; };
struct WorldSetKey {
    uint64_t a,b,c,d,e,f; uint32_t bytes, kind;
    bool operator==(const WorldSetKey& other) const { return a==other.a && b==other.b && c==other.c && d==other.d && e==other.e && f==other.f && bytes==other.bytes && kind==other.kind; }
};
struct WorldSetHash { size_t operator()(const WorldSetKey& key) const noexcept { size_t h=0; auto mix=[&](uint64_t value){ h^=std::hash<uint64_t>{}(value)+0x9e3779b9+(h<<6)+(h>>2); }; mix(key.a);mix(key.b);mix(key.c);mix(key.d);mix(key.e);mix(key.f);mix((uint64_t(key.bytes)<<32)|key.kind); return h; } };
struct WorldSlot { uint64_t material_epoch=1;Buffer frame,projections,objects,instances,point_lights,tiles,spots,glue,dimensions,history_projection,history_instances;uint64_t instance_bytes_used{},history_instance_bytes{};
    // Host copies of the bytes uploaded to frame/projection/object/instance
    // memory. That memory may be uncached device-local BAR; CPU readers
    // (motion history, spot fade hashing) must never read it back.
    std::vector<uint8_t> host_frame,host_objects,host_instances;std::array<uint8_t,64> host_projection{};bool host_projection_valid{};
    uint32_t tile_list_tiles{},tile_list_points{},tile_list_capacity{}; VkDescriptorPool pool{}, material_pool{}; std::unordered_map<WorldSetKey,VkDescriptorSet,WorldSetHash> material_cache,probe_materials; std::unordered_set<uint64_t> dirty_textures,dirty_arrays,dirty_voxels; bool material_stale=false;std::vector<uint64_t> object_versions;
    Buffer light_tile_gpu,light_tile_readback,light_tile_constants;uint64_t tile_frame{},tile_epoch{};uint32_t tile_count{},tile_points{};bool tile_pending{};
};
struct AtmosphereLut { Image image; std::array<uint8_t,48> parameters{}; bool initialized{},dirty{}; };
struct EnvironmentState {
    VkShaderModule stages[10]{}; VkDescriptorSetLayout layouts[4]{}; VkPipelineLayout pipeline_layouts[4]{}; VkPipeline pipelines[4]{},other_hdr[4]{};
    Buffer constants[2]{}; std::unordered_map<uint64_t,AtmosphereLut> luts; std::vector<SevkEnvironmentDraw> draws;
    VkDescriptorSetLayout foliage_layout{};VkPipelineLayout foliage_pipeline_layout{};VkPipeline foliage_pipelines[2]{};
    Buffer foliage_objects[2]{},foliage_materials[2]{}; std::vector<SevkFoliageDraw> foliage_draws;std::array<uint8_t,4096> foliage_table{};
    SevkEnvironmentStats stats{sizeof(SevkEnvironmentStats),SEVK_ABI_VERSION};
};
struct RecipeDependency {uint32_t kind;uint64_t id;bool operator==(const RecipeDependency& b)const{return kind==b.kind&&id==b.id;}};
struct RecipeDependencyHash {size_t operator()(const RecipeDependency& k)const noexcept{return std::hash<uint64_t>{}(k.id)^(size_t(k.kind)*0x9e3779b9u);}};
struct WorldRecipe {uint32_t index=UINT32_MAX;std::vector<RecipeDependency> dependencies;SevkWorldDraw packet{};Mesh* geometry{};uint32_t generation{};bool alive{},valid{};uint64_t arrays[3]{},random{};std::array<VkDescriptorSet,2> materials{};std::array<uint64_t,2> material_epochs{};};
struct ModelPart {WorldRecipe* recipe{};uint32_t generation{};std::vector<uint8_t> constants;uint32_t view_flags{};SevkOldPartOrder old_order{};};
struct ModelDefinition {uint32_t generation{};std::vector<ModelPart> parts;};
struct ModelGroupKey {uint64_t model;uint32_t flags,cascades,cell;int32_t x,y,z;uint32_t main_order{};bool operator==(const ModelGroupKey&)const=default;};
struct ModelGroupHash {size_t operator()(const ModelGroupKey& k)const noexcept{size_t h=std::hash<uint64_t>{}(k.model);for(auto v:{k.flags,k.cascades,k.cell,uint32_t(k.x),uint32_t(k.y),uint32_t(k.z),k.main_order})h^=size_t(v)+0x9e3779b9+(h<<6)+(h>>2);return h;}};
struct ModelGroup {ModelGroupKey key{};uint64_t frame{};uint32_t count{},order{};float origin[3]{},radius{};std::vector<uint8_t> instances;};
struct ModelState {std::deque<ModelDefinition> definitions;uint32_t generation{};uint64_t frame{},resident_bytes{};std::unordered_map<ModelGroupKey,uint32_t,ModelGroupHash> lookup;std::deque<ModelGroup> groups;std::vector<uint32_t> active;std::vector<WorldDraw> merged;SevkModelGroupStats stats{sizeof(SevkModelGroupStats),SEVK_ABI_VERSION};};
struct RayBlas { uint64_t stamp{}; uint32_t primitives{}; VkBuffer vertex{}, index{}; VkDeviceSize vertex_offset{}, index_offset{}; VkAccelerationStructureKHR handle{}; Buffer storage{}; };
struct RayRetired { uint64_t completion{}; VkAccelerationStructureKHR handle{}; Buffer storage{}; };
struct RayState {
    bool supported{}, hdr_storage{}, visibility_storage{}, shader_attempted{};
    VkDeviceSize scratch_alignment=256;
    uint32_t max_primitives{}, max_instances{}, refused{}, shader_ready{};
    uint64_t builds{}, dispatches{}, geometry_stamp{1}, scratch_completion{}, instance_completion{}, uniform_completion{}, tlas_completion{};
    std::mutex mutex;
    SevkRayOptions options{};
    std::unordered_map<uint64_t, RayBlas> blas;
    std::vector<RayRetired> retired;
    VkAccelerationStructureKHR tlas{};
    Buffer tlas_storage{}, scratch{}, instances{}, uniforms{};
    VkDescriptorSetLayout layout{}; VkPipelineLayout pipeline_layout{}; VkPipeline pipeline{};
    VkDescriptorPool pool{}; VkDescriptorSet sets[2]{}; VkShaderModule module{};
};
struct LightTileState {
    bool requested{};
    VkDescriptorSetLayout layout{};
    VkPipelineLayout pipeline_layout{};
    VkPipeline pipeline{};
    uint64_t epoch=1;
    uint32_t points{};
    SevkLightTileStats stats{sizeof(SevkLightTileStats),SEVK_ABI_VERSION};
};
// One published submission stays immutable while the next queue fills the other.
// Queue still refuses to run during recording, so the published slot cannot move under a recorder.
struct WorldSubmission {
    std::unordered_map<uint32_t,uint32_t> main_owner_orders;
    uint32_t queue_us{};
    bool queued{};
    std::vector<WorldDraw> draws;
    std::vector<uint8_t> draw_objects;
    WorldInstanceArena instances;
    alignas(16) std::array<uint8_t,1280> frame{};
    std::array<float,16> projection{},inverse{};
    std::vector<uint32_t> object_offsets,instance_offsets,order,main_selection;
    std::array<std::vector<uint32_t>,8> cascade_casters;
    std::vector<VkDescriptorSet> materials;
    std::vector<WorldIndirectPlan::Packet> indirect_packets;
    std::vector<WorldIndirectPlan::Command> indirect_commands;
    std::array<WorldIndirectPlan,9> indirect_plans;
    std::array<uint32_t,9> indirect_bases{};
    SevkQueueWorkStats queue_work{sizeof(SevkQueueWorkStats),SEVK_ABI_VERSION};
    SevkQueueValidationStats validation_stats{sizeof(SevkQueueValidationStats),SEVK_ABI_VERSION};
    SevkRecipeStats recipe_stats{sizeof(SevkRecipeStats),SEVK_ABI_VERSION};
};
struct WorldState {ModelState models;
    bool lighting_diagnostics{};Image lighting_stages[3]{},lighting_terms[7]{};VkPipeline lighting_terms_pipeline{};VkSampler lighting_terms_sampler{};uint32_t lighting_stage_width{},lighting_stage_height{},lighting_stage_valid{};VkFormat lighting_stage_format{};
    std::deque<WorldRecipe> recipes;std::vector<uint32_t> free_recipes;std::unordered_map<RecipeDependency,std::unordered_set<uint32_t>,RecipeDependencyHash> recipe_dependencies;uint32_t recipe_live{},recipe_registered{},recipe_retired{};
    bool parallel_cascades{};
    bool parallel_queue{};
    VkCommandPool cascade_pools[2][8]{};VkCommandBuffer cascade_commands[2][8]{};
    VkDescriptorPool cascade_descriptor_pools[2][8]{};
    // Spot-shadow scope contents recorded by worker k into its own pool.
    bool parallel_spots{};uint32_t spot_tasks{};
    VkCommandPool spot_pools[2][8]{};std::vector<VkCommandBuffer> spot_commands[2][8];uint32_t spot_used[2][8]{};
    // Object/instance upload copies overlap material lookup and the draw sort.
    bool parallel_upload{};
    uint32_t active_submission{};
    std::array<WorldSubmission,2> submissions{};
    WorldSubmission& current(){return submissions[active_submission];}
    const WorldSubmission& current() const{return submissions[active_submission];}
    uint32_t cascade_tasks{},cascade_task_commands{};
    WorldObjectRecords object_records;bool retained_objects{};uint64_t object_uploaded_bytes{};
    bool geometry_arena_requested{};
    std::vector<GeometryPage> vertex_pages,index_pages;
    bool indirect_requested{},indirect_supported{};uint32_t indirect_limit=1;
    SevkIndirectStats indirect_stats{sizeof(SevkIndirectStats),SEVK_ABI_VERSION};
    Buffer indirect_buffers[2];
    bool texture_validation_cache{};
    std::unordered_set<uint64_t> shadow_alpha_textures;
    VkDescriptorSetLayout material_layout{},light_layout{},spot_layout{}; VkPipelineLayout material_pipeline_layout{},light_pipeline_layout{},spot_pipeline_layout{};
    uint32_t distance_fade_at=UINT32_MAX;
    std::array<WorldPipeline,3408> pipelines{}; std::array<VkPipeline,4> lights{},other_hdr_lights{}; VkPipeline compose{},resolve{};
    VkFormat hdr_format=VK_FORMAT_R16G16B16A16_SFLOAT;
    VkDescriptorSetLayout copy_layout{};VkPipelineLayout copy_pipeline_layout{};VkPipeline copy_pipelines[2]{};std::vector<AtlasTransient> atlas_transients;
    VkShaderModule fullscreen{}; Buffer quad{}; Image dither{},ambient{},black{};
    std::vector<VkVertexInputAttributeDescription> fullscreen_attributes; VkFormat compose_format{};
    std::array<WorldSlot,2> slots{}; std::array<Image,4> gbuffer{}; Image hdr{},shadows{},motion{};
    uint32_t motion_w{},motion_h{}; bool motion_sampled{},motion_have_signature{}; uint64_t motion_signature{};
    VkPipeline motion_pipe[4]{}; VkPipelineLayout motion_layout{}; VkDescriptorSetLayout motion_set_layout{};
    VkDescriptorPool motion_pool{}; VkDescriptorSet motion_sets[2]{};
    struct AttachTarget { Image image{};uint64_t produced_epoch{}; uint32_t format=0xFFFFFFFFu,w{},h{}; bool sampled{},live{},declared{}; VkPipeline pipe[4]{},indexed_pipe[5]{}; };
    AttachTarget attach[3]{}; VkPipelineLayout attach_layout{}; VkDescriptorSetLayout attach_set_layout{}; VkDescriptorPool attach_pool{}; VkDescriptorSet attach_sets[2]{};
    VkPipelineLayout attach_indexed_layout{};VkDescriptorSetLayout attach_indexed_inputs{};VkDescriptorPool attach_indexed_pool{};VkDescriptorSet attach_indexed_sets[2]{},motion_indexed_sets[2]{};
    std::unordered_map<uint64_t,Mesh> meshes;std::unordered_map<uint64_t,Image> cubes,arrays;std::unordered_map<uint64_t,VoxelMaterial> voxel_materials;
    uint64_t voxel_frames{},voxel_draws{};
    uint64_t skybox{},probe_skybox{};uint32_t width{},height{};VkDeviceSize alignment{};
    bool initialized{};
};
static_assert(sizeof(SevkGBufferVertex)==48 && sizeof(SevkGBufferDraw)==168 && sizeof(SevkGBufferStats)==48);
static_assert(sizeof(SevkVertex) == 32 && sizeof(SpritePush) == 96 && sizeof(MeshPush) == 112);
static_assert(sizeof(SevkSprite) == 88 && sizeof(SevkFrameInfo) == 144);
static_assert(sizeof(SevkDrawMesh) == 152 && sizeof(SevkLight) == 64);

struct TransparentDraw {SevkTransparentDraw packet{};std::vector<uint8_t> object,instances;};
struct OutputBillboardProgram {VkShaderModule vs{},ps{};std::vector<VkVertexInputAttributeDescription> attributes;VkPipeline pipeline{},ldr_hdr{};VkFormat format{};VkPipelineLayout layout{};};
struct TransparencyState { VkDescriptorSetLayout layout{},resolve_layout{};VkPipelineLayout pipeline_layout{},resolve_pipeline_layout{};WorldPipeline glass[10]{},glass_depth[10]{},glass_culled[10]{},glass_depth_culled[10]{},glass_depth_old[10]{};Image glass_layers[2]{};VkSampler point_sampler{};bool layers_initialized[2]{};VkPipeline billboards[32]{},other_hdr_billboards[32]{},top_billboards[32]{},other_hdr_top_billboards[32]{},ldr_billboards[32]{};OutputBillboardProgram output_billboards[32]{};Image output_depth{};uint32_t output_width{},output_height{};bool output_depth_ready{};float custom_projections[512]{};VkImageView ldr_view{};VkImage ldr_image{};VkDescriptorSetLayout billboard_layout{};VkPipelineLayout billboard_pipeline_layout{};Buffer billboard_vertices[2]{},billboard_data[2]{},billboard_projections[2]{},billboard_indices{};std::vector<SevkBillboardDraw> billboard_draws;VkPipeline resolve{},other_hdr_resolve{};Image accum{},coverage{},csm{};Buffer objects[2]{},materials[2]{},instances[2]{},shadow_constants[2]{};std::vector<TransparentDraw> draws;uint32_t width{},height{};bool initialized{};SevkTransparencyStats stats{sizeof(SevkTransparencyStats),SEVK_ABI_VERSION};};
struct ParticleStage {VkShaderModule module{};VkDescriptorSetLayout layout{};VkPipelineLayout pipeline_layout{};VkPipeline pipeline{};std::vector<std::pair<uint32_t,VkDescriptorType>> bindings;};
struct ParticleState {ParticleStage stages[10]{};Buffer particle{},dead{},alive[2]{},counters[4]{},draw_args{},sim_args{},active{};VkBufferView draw_view{},sim_view{};Buffer emitters[2]{},emit_constants[2]{},readback[2]{},indices{};std::vector<uint8_t> packet;uint32_t count{},index{};uint64_t atlas{};uint64_t generation_token[2]{},generation_completion[2]{};uint32_t generation_frame[2]{},generation_sim[2]{};bool generation_pending[2]{},generation_draw[2]{};bool initialized{},reset{},queued{},readback_valid[2]{};SevkParticleStats stats{sizeof(SevkParticleStats),SEVK_ABI_VERSION};};
struct FlareState {VkDescriptorSetLayout layout{};VkPipelineLayout pipeline_layout{};VkPipeline pipeline{};Buffer queries[2]{},results[2]{},constants[2]{},readback[2]{};std::vector<SevkFlareQuery> packet;std::vector<SevkFlareRasterQuery> raster_packet;std::vector<SevkFlareResult> completed;float projection[16]{};uint32_t counts[2]{};bool valid[2]{};} ;
struct DecalBatch {uint32_t role{};uint64_t textures[4]{};std::vector<uint32_t> draws;};
struct DecalState {VkPipeline pipelines[9]{};VkDescriptorSetLayout layout{};VkPipelineLayout pipeline_layout{};Buffer constants[2]{},indices{};Image normals{};uint32_t width{},height{};bool initialized{};std::vector<SevkDecalDraw> draws;std::vector<DecalBatch> batches;};
struct PostImage {Image image;uint32_t width{},height{};VkImageLayout layout{};VkFormat format{};};
struct NativeCatalogResource {uint32_t binding{},dim{},arrayed{},depth{},number{};VkDescriptorType type{};bool owner_input{};};
struct PostStage {uint64_t generation{},executions{},last_completion{};uint32_t last_frame{};VkDescriptorSetLayout layout{};VkPipelineLayout pipeline_layout{};VkPipeline pipeline{},other_hdr{};std::vector<std::pair<uint32_t,VkDescriptorType>> bindings;std::vector<NativeCatalogResource> catalog;uint32_t storage_format{};bool compute{},probe_full_view{},track_geometry{true};WorldPipeline geometry{};std::vector<VkPipeline> pipelines;std::vector<VkShaderModule> modules;std::vector<VkVertexInputAttributeDescription> attributes;};
struct ShadowUsageView {Buffer gpu,readback,screen;bool pending{};};
struct ShadowUsageState {PostStage stage;ShadowUsageView views[2][19];uint32_t pixels[19][8]{};uint64_t completion[19]{},latest{};bool enabled{};};
struct PostCandidate {uint64_t expected{};VkFormat format{};std::vector<std::pair<uint32_t,PostStage>> stages;};
struct PostState {VkImageView tone_bloom{};bool grading{},display_graded{};PostImage tone_hdr{},fxaa_hdr{},chromatic_hdr{};uint64_t revision{},next_generation{},pending{};std::map<uint64_t,PostCandidate> candidates;std::vector<std::pair<uint64_t,PostStage>> retired;std::vector<PostStage> geometry,cells,gpu_new,gpu_old;PostStage oit{},stages[144]{},lighting[4]{},atmosphere[3]{},foliage[5]{},decals[10]{},particles[10]{},transparent[40]{},billboards[65]{};PostImage shadow_blur{},exposure[2]{},half{},bloom_down[11]{},bloom_up[10]{},tone{},fxaa{},chromatic{},ao{},ao_blur{};PostImage* output{};PostImage* after_tonemap{};Buffer constants[2]{},histogram{};VkSampler point{};VkPipeline compose{};VkFormat compose_format{};uint32_t width{},height{},exposure_index{};SevkPostprocessConfig config{};SevkPostprocessStats stats{sizeof(SevkPostprocessStats),SEVK_ABI_VERSION};};
struct GpuOwnerRow {uint32_t module{},first{},capacity{},frame{};uint64_t generation{};uint32_t condition=UINT32_MAX;};
struct GpuOwnerSlot {Buffer ranges{},work{},readback{};std::vector<GpuOwnerRow> rows;uint64_t completion{};bool pending{};};
struct GpuOwnerState {PostStage reduction{};GpuOwnerSlot slots[2];std::vector<std::pair<uint64_t,GpuOwnerSlot>> retired;bool enabled{};};
struct GBufferVelocityConstants {float view_proj[16],prev_view_proj[16],size[2],inv_size[2];uint32_t prev_count,has_history,has_prev_world,bone_count;float rows[3][4];uint32_t instance_base,probe_mode;float padding[2],camera_delta[3],tail;};
static_assert(sizeof(GBufferVelocityConstants)==240,"AnomalyVelocity cbuffer layout");
struct GBufferVelocityState {VkDescriptorSetLayout empty{},layout{};VkPipelineLayout pipeline_layout{};Buffer constants[2]{},probe{},prev_world{},prev_bones{};VkDescriptorSet set{},gpu_set{};uint32_t stride{},cursor{},capacity{},gpu_offset{},attachments{};bool frame{},history{},indexed_prepared{},indexed_ready{},camera_valid{};double camera[3]{};float camera_delta[3]{};};
struct HbaoState {SevkHbaoConfig config{};PostImage depth{},normal{},aoz[2]{};Image quarter_depth{},quarter_ao{};VkImageView depth_views[16]{},ao_views[16]{};Buffer constants[2]{};VkSampler border{};uint32_t width{},height{};bool initialized{},jitter_provided{};float jitter[64]{};};
struct ProbeCube {Image image;std::vector<VkImageView> views;};
struct ProbeState {uint64_t atmosphere_id{};float atmosphere_intensity{},atmosphere_desaturation{},sun_direction[3]{};Buffer atmosphere[2][6]{},atmosphere_view[2][6]{};VkDescriptorSetLayout material_layout{};VkPipelineLayout material_pipeline_layout{};PostStage stages[4]{};ProbeCube original[2],filtered[2],final[2];Image depth[6]{};Buffer frame[2][6]{},projection[2][6]{},forward_projection[2][6]{},constants[2][6]{};SevkProbeConfig config{};SevkProbeStats stats{sizeof(SevkProbeStats),SEVK_ABI_VERSION};uint32_t resolution{},mips{},skip_levels{},face{},stage{},work_face{},blend_kind{};bool initialized{},ready{};};
struct HighlightState {VkDescriptorSetLayout layout{};VkPipelineLayout pipeline_layout{};VkPipeline compose{},compose_hdr{};PostImage raw{},overlap{},blur[2]{},composed{};Buffer colors[2]{},constants[2]{};std::vector<SevkHighlightDraw> draws;SevkHighlightStats stats{sizeof(SevkHighlightStats),SEVK_ABI_VERSION};};
struct SpotState {uint64_t fade_static[64]{},fade_dynamic[64]{},fade_valid{};VkDescriptorSetLayout copy_set_layout{};VkPipelineLayout copy_layout{};VkDescriptorPool copy_pool{};VkPipeline copy_pipeline{};VkDescriptorSet copy_sets[2][64]{};std::array<std::vector<uint32_t>,64> static_casters,dynamic_casters;std::vector<SevkSpot> config;std::unordered_set<uint64_t> exclusions;Image images[64]{},frozen[64]{};uint32_t sizes[64]{},frozen_sizes[64]{},resolution{512};uint8_t frozen_ready[64]{};VkImageLayout frozen_layout[64]{};uint64_t static_dirty{~0ull},dynamic_dirty{};SevkSpotStats stats{sizeof(SevkSpotStats),SEVK_ABI_VERSION};};
struct OcclusionState {VkDescriptorSetLayout layout{};VkPipelineLayout pipeline_layout{};VkPipeline pipeline{};VkQueryPool pool{};Buffer boxes[2]{},projections[2]{};std::vector<SevkOcclusionDraw> packet,submitted[2];std::vector<SevkOcclusionResult> completed;uint64_t token{},tokens[2]{},completed_token{};bool valid[2]{};SevkOcclusionStats stats{sizeof(SevkOcclusionStats),SEVK_ABI_VERSION};};
struct FsrHeader { uint64_t type; FsrHeader* next; };
struct FsrBackend {
    uint64_t type; FsrHeader* next;
    VkDevice device; VkPhysicalDevice physical; PFN_vkGetDeviceProcAddr get_device_proc;
};
struct FsrCreate {
    uint64_t type; FsrHeader* next;
    uint32_t flags, pad;
    uint32_t max_render_w, max_render_h, max_upscale_w, max_upscale_h;
    void* message;
};
struct FsrResource {
    void* resource;
    uint32_t type, format, width, height, depth, mip_count, flags, usage, state, pad;
};
struct FsrDispatch {
    uint64_t type; FsrHeader* next; void* command;
    FsrResource color, depth, motion, exposure, reactive, transparency, output;
    float jitter_x, jitter_y, mv_x, mv_y;
    uint32_t render_w, render_h, upscale_w, upscale_h;
    uint8_t sharpen; uint8_t pad0[3];
    float sharpness, frame_ms, pre_exposure;
    uint8_t reset; uint8_t pad1[3];
    float near_plane, far_plane, fov, meters;
    uint32_t flags;
};
static_assert(sizeof(FsrBackend) == 40, "FSR Vulkan backend layout");
static_assert(sizeof(FsrCreate) == 48, "FSR create layout");
static_assert(sizeof(FsrResource) == 48 && offsetof(FsrResource, state) == 40, "FSR resource layout");
static_assert(sizeof(FsrDispatch) == 432 && offsetof(FsrDispatch, flags) == 428, "FSR dispatch layout");

struct Renderer {uint32_t recipe_generation=1;
    OcclusionState occlusion;
    std::array<OcclusionState,8> shadow_occlusion;VkPipeline shadow_occlusion_pipeline{};
    SpotState spots;
    HighlightState highlights;
    VkQueryPool timing_pool{};uint32_t timestamp_bits{};float timestamp_period{},render_fraction{1},view_far{1000000};bool timing_valid[2]{};
    struct NgxRuntime {
        bool ready{};
        uint64_t revision{};
        uint32_t quality{}, flags{}, in_w{}, in_h{}, out_w{}, out_h{}, fail_frames{};
        void* feature{}, *caps{}, *eval_params{}, *feature_library{};
        const wchar_t* path_slot{};
        alignas(8) unsigned char info[64]{};
        std::wstring directory, log_path, path_hold;
    };
    NgxRuntime ngx;
    struct FsrRuntime {
        bool ready{}, have_time{};
        uint64_t revision{};
        uint32_t out_w{}, out_h{}, fail_frames{};
        void* library{}, *context{}, *create{}, *destroy{}, *dispatch{}, *query{};
        FsrBackend backend{};
        FsrCreate created{};
        std::string directory, version;
        std::chrono::steady_clock::time_point last_time{};
    };
    FsrRuntime fsr;
    // HDR UI transform for the presented HUD: scene paper white x UI gain (scRGB), shoulder, peak (scRGB). Zero disables.
    float hdr_ui[3]{};
    bool hdr_display_forced{}; // diagnostics: display tenancy without an HDR swapchain
    bool tearing_denied{},tearing_armed{};
    uint32_t display_flags{};bool hdr_active{},hdr_denied{},fg_denied{},swap_transfer{},null_descriptors{};
    std::array<uint64_t,11> opaque_compute_times{};
    SevkResolutionStats resolution_stats{sizeof(SevkResolutionStats),SEVK_ABI_VERSION};
    std::vector<std::pair<uint64_t,VkImageView>> retired_views;
    void retire_image(Image& image){if(image.handle){Texture t{};t.image=image;retired.push_back({next_completion,t,{}});image={};}}
    void retire_view(VkImageView& view){if(view){retired_views.push_back({next_completion,view});view={};}}
    void sample_timing(uint32_t index){if(!timing_pool || !timing_valid[index])return;uint64_t values[4]{};
        auto result=vkGetQueryPoolResults(device,timing_pool,index*2,2,sizeof(values),values,16,VK_QUERY_RESULT_64_BIT|VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);
        if(result==VK_NOT_READY)return;check(result,"read completed Vulkan timestamps");if(!values[1] || !values[3])return;
        uint64_t mask=timestamp_bits==64?~uint64_t(0):(uint64_t(1)<<timestamp_bits)-1;
        resolution_stats.gpu_ms=float((values[2]-values[0])&mask)*timestamp_period/1000000.f;++resolution_stats.samples;timing_valid[index]=false;
    }
    PostStage stock_shadow;
    ShadowUsageState shadow_usage;
    ProbeState probes;
    HbaoState hbao;
    PostState post;GpuOwnerState gpu_owners;std::mutex gpu_owner_mutex;GBufferVelocityState gbuffer_velocity;bool gbuffer_velocity_supported{};
    // maintenance4 interface widening, and the stock fullscreen VS outputs that fullscreen owners must consume.
    bool interface_widening{};std::vector<StageSlot> fullscreen_outputs;
    // Conditional spot scope of the thread recording it; cascade/pass workers record concurrently.
    inline static thread_local uint32_t gpu_owner_condition=UINT32_MAX;
    FlareState flares;
    DecalState decals;
    ParticleState particles;
    TransparencyState transparency;
    VkInstance instance{}; VkDebugUtilsMessengerEXT debug{}; VkPhysicalDevice physical{}; VkDevice device{};
    VkQueue queue{}; uint32_t queue_family{}; VmaAllocator allocator{};
    std::mutex api_mutex;
    // Pipelined recording: end_frame(N) runs on a worker holding api_mutex
    // while managed frame N+1 continues. Ordinary calls wait for it and then
    // replay deferred calls in issue order; whitelisted void calls queue with
    // copied arguments; a few reads answer from the launch snapshot.
    std::condition_variable_any record_cv;bool record_pending{},record_running{},record_failed{};std::string record_error;std::future<void> record_job;
    std::mutex deferred_mutex;bool record_in_flight{};std::vector<std::function<int32_t(Renderer&)>> deferred;
    struct ReadSnapshot {SevkWorkStats work{};SevkResolutionStats resolution{};SevkShadowFeedback shadow{};uint32_t cache_mask{},cache_count{};bool cache_valid{};std::vector<SevkOcclusionResult> occlusion;uint64_t occlusion_token{};std::vector<SevkFlareResult> flares;float render_fraction=1;};
    std::mutex snapshot_mutex;ReadSnapshot snapshot;uint64_t pipelined_frames{},pipelined_mask_mismatches{},pipelined_waits{},pipelined_wait_us{};
    // Wait census (only with SE_VULKAN_PIPELINE_REPORT): native frame address -> waits, microseconds.
    bool pipeline_census=std::getenv("SE_VULKAN_PIPELINE_REPORT")!=nullptr;std::map<void*,std::pair<uint64_t,uint64_t>> pipelined_wait_sites;
    std::map<void*,std::pair<uint64_t,uint64_t>> packing_wait_sites;uint64_t packing_blocked[2]{},packing_blocked_us[2]{};
    VkSurfaceKHR surface{}; VkSwapchainKHR swapchain{};
    VkPresentModeKHR actual_present_mode=VK_PRESENT_MODE_FIFO_KHR;uint32_t swapchain_generation{};
    bool exclusive_extension{}, exclusive_requested{}, exclusive_swapchain{}, exclusive_held{}, exclusive_denied{}; void* exclusive_monitor{};
    VkFormat color_format{ VK_FORMAT_R8G8B8A8_SRGB }; VkExtent2D extent{}, output_extent{}, present_extent{}; uint32_t sprite_w{}, sprite_h{};
    std::vector<VkImage> swap_images; std::vector<VkImageView> swap_views;
    std::vector<VkSemaphore> present_semaphores; std::vector<bool> image_initialized;
    struct PresentResult {VkResult result;uint64_t begin_ns,end_ns;};
    std::future<PresentResult> pending_present;
    bool async_present=!std::getenv("SE_VULKAN_ASYNC_PRESENT") || std::strcmp(std::getenv("SE_VULKAN_ASYNC_PRESENT"),"0")!=0;
    SevkPresentFlowStats present_flow{sizeof(SevkPresentFlowStats),SEVK_ABI_VERSION};
    bool capture_present_samples=std::getenv("SE_VULKAN_PRESENT_SAMPLES") && std::strcmp(std::getenv("SE_VULKAN_PRESENT_SAMPLES"),"1")==0;std::deque<SevkPresentSample> present_samples;
    static uint64_t present_clock(){return uint64_t(std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count());}
    void accept_present(PresentResult value,bool propagate=true){
        if(capture_present_samples){present_samples.push_back({present_flow.completed_calls+1,value.end_ns,present_flow.last_completed_ns?value.end_ns-present_flow.last_completed_ns:0,value.end_ns-value.begin_ns});if(present_samples.size()>4096)present_samples.pop_front();}
        present_flow.last_call_us=uint32_t((value.end_ns-value.begin_ns)/1000);present_flow.total_call_ns+=value.end_ns-value.begin_ns;
        if(present_flow.last_completed_ns)present_flow.total_interval_ns+=value.end_ns-present_flow.last_completed_ns;
        present_flow.last_completed_ns=value.end_ns;present_flow.completed_calls++;present_flow.result=uint32_t(value.result);
        if(value.result==VK_ERROR_FULL_SCREEN_EXCLUSIVE_MODE_LOST_EXT){exclusive_held=false;return;}
        if(value.result==VK_ERROR_OUT_OF_DATE_KHR){surface_dirty=true;}
        else if(value.result!=VK_SUCCESS && value.result!=VK_SUBOPTIMAL_KHR && propagate)check_gpu(value.result,"present rendered frame");
        if(value.result==VK_SUCCESS||value.result==VK_SUBOPTIMAL_KHR)++stats.presented_frames;
    }
    void join_present(bool propagate=true){
        critical_profile::Scope critical_join(critical_profile::PresentJoin);
        if(!pending_present.valid())return;
        auto begin=present_clock();auto result=pending_present.get();present_flow.last_join_us=uint32_t((present_clock()-begin)/1000);accept_present(result,propagate);
    }
    void poll_present(){if(pending_present.valid()&&pending_present.wait_for(std::chrono::seconds(0))==std::future_status::ready)join_present();}
    Image offscreen{}, depth{}; bool offscreen_initialized{}, depth_initialized{};
    VkDescriptorPool descriptors{}; VkDescriptorSetLayout frame_layout{}, texture_layout{}, shadow_layout{};
    VkPipelineLayout pipeline_layout{}; VkPipeline sprite_pipeline{}, additive_pipeline{}, mesh_pipeline{}, transparent_pipeline{}, mesh_additive_pipeline{};
    VkPipeline lcd_pipeline{}, lcd_additive_pipeline{}, shadow_pipeline{}, skybox_pipeline{}, mesh_additive_top_pipeline{};
    // DebugDrawLine3D replay (stock MyLinesRenderer): [0] depth-read, [1] IgnoreDepth.
    VkPipeline line_pipelines[2]{}; std::vector<SevkLineVertex> frame_lines[2]; uint64_t debug_line_vertices{};
    VkPipeline sprite_premul_pipeline{}, sprite_premul_additive_pipeline{}, lcd_premul_pipeline{}, lcd_premul_additive_pipeline{};
    std::array<Image,8> shadow_images{}; VkDescriptorSet shadow_descriptor{}; VkSampler shadow_sampler{};
    float shadow_matrices[8][16]{}, shadow_splits[8]{}, saved_shadow_matrices[8][16]{}; uint32_t shadow_count{}, saved_shadow_count{}, shadow_resolution{1024},requested_shadow_resolution{1024};float shadow_bias{.001f};bool shadow_blur_enabled{}, saved_shadows{};uint64_t saved_caster_signature{};
    bool scheduled_shadows{};uint32_t shadow_update_mask{255},shadow_valid_mask{255},shadow_image_mask{};
    std::array<uint8_t,1200> shadow_pass_constants{};bool shadow_pass_constants_provided{};
    bool shadow_depth_bias_clamp{};
    std::vector<SevkDrawMesh> frame_meshes; std::vector<SevkSprite> frame_sprites;
    VkPipelineCache pipeline_cache{}; VkSampler sampler{}, sprite_sampler{};
    uint32_t anisotropy_samples{1}; float anisotropy_limit{1};
    VkSemaphore timeline{}; VkCommandPool upload_pool{};
    std::array<Slot, 2> slots{}; uint32_t slot_index{}, image_index{}; uint64_t next_completion{};
    bool recording{}, suspended{}, surface_dirty{}, vsync{}, validation{}, readback_supported{}, device_lost{};
    bool last_image_valid{}; uint32_t last_image_index{};
    Buffer capture{}; bool capture_requested{}, capture_valid{}; uint64_t capture_completion{};
    std::unordered_map<uint64_t, Texture> textures;
    std::unordered_map<uint64_t, Mesh> meshes;
    std::vector<Retired> retired;
    SevkGpuOpaqueCapabilities opaque_caps{sizeof(SevkGpuOpaqueCapabilities),SEVK_ABI_VERSION};
    OpaqueReferenceState opaque_reference;
    IndexedMotionState indexed_motion;uint64_t opaque_source_epoch{};
    OpaqueStockState opaque_stock;
    GpuResidentState gpu_resident;GpuSceneCosts gpu_scene_costs;std::mutex world_descriptor_mutex;
    bool gpu_resident_deltas=[](){auto value=std::getenv("SE_VULKAN_GPU_OPAQUE_RESIDENT_DELTAS");return value&&std::strcmp(value,"1")==0;}();
    bool gpu_owned_inputs=[](){auto value=std::getenv("SE_VULKAN_GPU_OPAQUE_OWNED_INPUTS");return value&&std::strcmp(value,"1")==0;}();
    bool opaque_conditional{};
    SevkStats stats{}; std::string error, shader_directory, cache_directory, recent_validation, last_validation_error;
    // Stock point quality reaches 4096. Spotlight slots retain their separate
    // 64-entry mask/index contract; scene submission places spots first.
    std::array<SevkLight, 4096+64> lights{}; uint32_t light_count{};
    RayState ray; SevkFrameInfo submitted_frame{};
    critical_profile::State critical;
    StockGBuffer stock;
    WorldState world;
    LightTileState light_tiles;
    PassGpuState pass_scene;
    struct CellState {old_cells::Scene scene;std::unique_ptr<native_pass::ViewJobs> jobs;SevkCellStats stats{sizeof(SevkCellStats)};} cells;
    bool deferred_cell_emission{},cells_emit_pending{};
    EnvironmentState environment;
    std::array<UploadSlot,2> upload_slots{}; uint32_t upload_index{};
    bool batching_uploads{}; VkCommandBuffer batch_command{}; uint64_t upload_fence{};
    struct GpuMark { const char* name; uint64_t value; };
    std::array<GpuMark,8192> gpu_marks{}; uint32_t gpu_mark_head{}; bool gpu_checkpoints{}, gpu_fault{};
    void gpu_mark(VkCommandBuffer cmd,const char* name,uint64_t value){if(!gpu_checkpoints || !cmd)return;auto& m=gpu_marks[gpu_mark_head++%gpu_marks.size()];m={name,value};vkCmdSetCheckpointNV(cmd,&m);}
    bool profile_markers{}, profile_label_open{};
    void frame_mark(const char* name){auto cmd=slots[slot_index].command;gpu_mark(cmd,name,next_completion+1);
        if(profile_markers && vkCmdBeginDebugUtilsLabelEXT && vkCmdEndDebugUtilsLabelEXT){
            if(profile_label_open)vkCmdEndDebugUtilsLabelEXT(cmd);
            VkDebugUtilsLabelEXT label{VK_STRUCTURE_TYPE_DEBUG_UTILS_LABEL_EXT};label.pLabelName=name;
            vkCmdBeginDebugUtilsLabelEXT(cmd,&label);profile_label_open=true;
        }
    }
    std::string gpu_fault_report(){
        std::string r;uint64_t complete=0;if(device)vkGetSemaphoreCounterValue(device,timeline,&complete);
        r+="; submitted timeline "+std::to_string(next_completion)+", completed "+std::to_string(complete);
        if(!gpu_checkpoints)r+="; GPU checkpoints unavailable";
        else{uint32_t n=0;vkGetQueueCheckpointDataNV(queue,&n,nullptr);std::vector<VkCheckpointDataNV> data(n,VkCheckpointDataNV{VK_STRUCTURE_TYPE_CHECKPOINT_DATA_NV});if(n)vkGetQueueCheckpointDataNV(queue,&n,data.data());
            if(!n)r+="; no GPU checkpoints reported";
            for(uint32_t i=0;i<n;++i){auto* m=static_cast<const GpuMark*>(data[i].pCheckpointMarker);const char* stage=data[i].stage==VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT?"started":data[i].stage==VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT?"finished":"stage";
                r+="; GPU last "+std::string(stage)+": "+(m && m->name?m->name:"?")+" (timeline "+std::to_string(m?m->value:0)+")";}}
        if(gpu_fault){VkDeviceFaultCountsEXT counts{VK_STRUCTURE_TYPE_DEVICE_FAULT_COUNTS_EXT};if(vkGetDeviceFaultInfoEXT(device,&counts,nullptr)==VK_SUCCESS){
            std::vector<VkDeviceFaultAddressInfoEXT> addresses(counts.addressInfoCount);std::vector<VkDeviceFaultVendorInfoEXT> vendor(counts.vendorInfoCount);counts.vendorBinarySize=0;
            VkDeviceFaultInfoEXT info{VK_STRUCTURE_TYPE_DEVICE_FAULT_INFO_EXT};info.pAddressInfos=addresses.empty()?nullptr:addresses.data();info.pVendorInfos=vendor.empty()?nullptr:vendor.data();
            auto result=vkGetDeviceFaultInfoEXT(device,&counts,&info);if(result==VK_SUCCESS || result==VK_INCOMPLETE){r+="; device fault: "+std::string(info.description);
                static const char* kinds[]{"none","invalid read","invalid write","invalid execute","unknown instruction","invalid instruction","instruction fault"};
                for(uint32_t i=0;i<counts.addressInfoCount;++i){char text[96];std::snprintf(text,sizeof(text)," [%s 0x%llx +/-0x%llx]",addresses[i].addressType<7?kinds[addresses[i].addressType]:"address",(unsigned long long)addresses[i].reportedAddress,(unsigned long long)addresses[i].addressPrecision);r+=text;}
                for(uint32_t i=0;i<counts.vendorInfoCount;++i)r+=" [vendor "+std::string(vendor[i].description)+" code "+std::to_string(vendor[i].vendorFaultCode)+" data "+std::to_string(vendor[i].vendorFaultData)+"]";}}
            else r+="; device fault info unavailable";}
        else r+="; VK_EXT_device_fault unavailable";
        return r;
    }
    void check_gpu(VkResult result,const char* operation){if(result==VK_ERROR_DEVICE_LOST){device_lost=true;stats.device_lost=1;throw std::runtime_error(std::string(operation)+" failed (VkResult -4, device lost)"+gpu_fault_report());}check(result,operation);}
    SevkWorkStats work{sizeof(SevkWorkStats),SEVK_ABI_VERSION};
    SevkPerformanceStats performance{sizeof(SevkPerformanceStats),SEVK_ABI_VERSION};
    ~Renderer() { cleanup(); }
    static VKAPI_ATTR VkBool32 VKAPI_CALL on_debug(VkDebugUtilsMessageSeverityFlagBitsEXT severity,
        VkDebugUtilsMessageTypeFlagsEXT, const VkDebugUtilsMessengerCallbackDataEXT* data, void* user) {
        auto* r = static_cast<Renderer*>(user);
        if (r) {
            if (severity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT) {
                ++r->stats.validation_errors;
                r->last_validation_error=data && data->pMessage?std::string(data->pMessage).substr(0,500):"Vulkan validation error";
                if(r->stats.validation_errors<=16 && data && data->pMessage) std::fprintf(stderr,"SE-Vulkan validation: %s\n",data->pMessage);
                if(active_buffer_destroy)std::fprintf(stderr,"SE-Vulkan buffer destruction owner: %s:%u (%s), bytes=%llu usage=0x%x\n",active_buffer_destroy_source.file_name(),active_buffer_destroy_source.line(),active_buffer_destroy_source.function_name(),static_cast<unsigned long long>(active_buffer_destroy->bytes),active_buffer_destroy->usage);
            }
            r->recent_validation = data && data->pMessage ? std::string(data->pMessage).substr(0, 500) : "Vulkan validation message";
        }
        return VK_FALSE;
    }
    void destroy_buffer(Buffer& b,std::source_location source=std::source_location::current()) {
        if(b.handle && batch_command) upload_slots[upload_index].pending.push_back(b);
        else if (b.handle) {
            active_buffer_destroy=&b;active_buffer_destroy_source=source;
            vmaDestroyBuffer(allocator, b.handle, b.memory);
            active_buffer_destroy=nullptr;
        }
        b = {};
    }
    void destroy_image(Image& image) {
        if (image.view) vkDestroyImageView(device, image.view, nullptr);
        if (image.handle) vmaDestroyImage(allocator, image.handle, image.memory);
        image = {};
    }
    void destroy_texture(Texture& t) {
        if (t.descriptor) vkFreeDescriptorSets(device, descriptors, 1, &t.descriptor);
        if (t.sprite_descriptor) vkFreeDescriptorSets(device, descriptors, 1, &t.sprite_descriptor);
        if (t.attachment) vkDestroyImageView(device,t.attachment,nullptr);
        destroy_image(t.image); t = {};
    }
    void release_geometry(std::vector<GeometryPage>& pages,uint32_t page,VmaVirtualAllocation allocation){
        auto& p=pages.at(page);vmaVirtualFree(p.block,allocation);VmaStatistics stats{};vmaGetVirtualBlockStatistics(p.block,&stats);
        if(!stats.allocationCount){destroy_buffer(p.buffer);vmaDestroyVirtualBlock(p.block);p={};}
    }
    void destroy_mesh(Mesh& m) {
        if(m.geometry_owner){auto owner=std::move(m.geometry_owner);m={};if(owner.use_count()==1)destroy_mesh(*owner);return;}
        if(m.vertex_allocation)release_geometry(world.vertex_pages,m.vertex_page,m.vertex_allocation);else destroy_buffer(m.vertices);
        if(m.index_allocation)release_geometry(world.index_pages,m.index_page,m.index_allocation);else destroy_buffer(m.indices);
        m={};
    }
    void wait(uint64_t value) {
        if(batch_command) flush_uploads();
        if (!value) return;
        VkSemaphoreWaitInfo w{ VK_STRUCTURE_TYPE_SEMAPHORE_WAIT_INFO }; w.semaphoreCount = 1; w.pSemaphores = &timeline; w.pValues = &value;
        check_gpu(vkWaitSemaphores(device, &w, UINT64_MAX), "wait GPU timeline");
    }
    // Shadow masks end_frame leaves for the next frame: a pending resolution
    // change clears the cache, then render_world adds this frame's updates.
    // Unscheduled shadows depend on the frame's casters and are not predicted.
    bool predict_shadow_masks(uint32_t& cache,uint32_t& update)const{
        uint32_t mask=shadow_resolution==requested_shadow_resolution?shadow_image_mask:0u,all=(1u<<shadow_count)-1;
        update=shadow_update_mask;if(!scheduled_shadows)return false;update|=all&~mask;cache=(mask|update)&all;return true;
    }
    SevkShadowFeedback shadow_feedback(){
        SevkShadowFeedback out{sizeof(SevkShadowFeedback),SEVK_ABI_VERSION};uint64_t complete=0;check_gpu(vkGetSemaphoreCounterValue(device,timeline,&complete),"shadow feedback completion");
        for(uint32_t i=0;i<2;++i)if(slots[i].completion<=complete)sample_shadow_usage(i);out.completion=shadow_usage.latest;for(auto& view:shadow_usage.pixels)for(uint32_t c=0;c<8;++c)out.pixels[c]+=view[c];
        out.update_mask=shadow_update_mask;out.valid_mask=shadow_valid_mask;return out;
    }
    void publish_launch_snapshot(){
        collect();std::lock_guard lock(snapshot_mutex);auto& s=snapshot;s.resolution=resolution_stats;uint32_t cache=0,update=0;s.cache_valid=predict_shadow_masks(cache,update);
        s.shadow=shadow_feedback();s.shadow.update_mask=update;s.cache_mask=cache;s.cache_count=shadow_count;
        s.occlusion=occlusion.completed;s.occlusion_token=occlusion.completed_token;s.flares=flares.completed;s.render_fraction=render_fraction;
    }
    static bool upscaler_requested(){{auto& n=ngx_request();std::lock_guard l(n.mu);if(n.enabled&&!n.directory.empty())return true;}{auto& f=fsr_request();std::lock_guard l(f.mu);if(f.enabled&&!f.directory.empty())return true;}return false;}
    // Ordinary calls: recording finished, then deferred calls in issue order.
    void await_recording(std::unique_lock<std::mutex>& lock){record_cv.wait(lock,[&]{return !record_pending&&!record_running;});if(record_failed){record_failed=false;throw std::runtime_error(record_error);}}
    void flush_deferred(){std::vector<std::function<int32_t(Renderer&)>> work;{std::lock_guard q(deferred_mutex);work.swap(deferred);}for(auto& fn:work)if(fn(*this))throw std::runtime_error("deferred native call failed");}
    // Starts end_frame on a worker; the caller holds api_mutex, so the worker
    // runs only after this call returns, and before any later ordinary call.
    int end_frame_async(){
        if(!recording||!world.current().queued)return end_frame();
        publish_launch_snapshot();uint32_t predicted=snapshot.cache_mask;bool checked=snapshot.cache_valid;
        {std::lock_guard q(deferred_mutex);record_in_flight=true;}record_pending=true;
        record_job=std::async(std::launch::async,[this,predicted,checked]{
            std::unique_lock lock(api_mutex);record_pending=false;record_running=true;
            try{end_frame();++pipelined_frames;if(checked&&(shadow_image_mask&((1u<<shadow_count)-1))!=predicted)++pipelined_mask_mismatches;}
            catch(const std::exception& e){record_error=e.what();record_failed=true;}catch(...){record_error="unknown native Vulkan error";record_failed=true;}
            {std::lock_guard s(snapshot_mutex);snapshot.work=work;}
            {std::lock_guard q(deferred_mutex);record_in_flight=false;}record_running=false;lock.unlock();record_cv.notify_all();});
        return 0;
    }
    void collect() {
        uint64_t complete = 0; check_gpu(vkGetSemaphoreCounterValue(device, timeline, &complete), "query GPU timeline");
        ray_collect(complete);
        guest_collect(complete);
        tenant_collect(complete);
        gpu_owner_resolve_generations(complete);post_collect(complete);
        stats.completed_timeline = complete;
        for(uint32_t i=0;i<2;++i)if(slots[i].completion<=complete){sample_timing(i);sample_occlusion(i);sample_shadow_usage(i);if(world.slots[i].material_stale)release_cached_materials(world.slots[i]);else release_dirty_materials(world.slots[i]);}
        for(auto it=retired_views.begin();it!=retired_views.end();){if(it->first<=complete){vkDestroyImageView(device,it->second,nullptr);it=retired_views.erase(it);}else ++it;}
        for(auto it=world.atlas_transients.begin();it!=world.atlas_transients.end();){if(it->completion<=complete){for(auto v:it->views)vkDestroyImageView(device,v,nullptr);for(auto set:it->sets)check(vkFreeDescriptorSets(device,descriptors,1,&set),"free completed atlas descriptors");it=world.atlas_transients.erase(it);}else ++it;}
        for (auto it = retired.begin(); it != retired.end();) {
            if (it->completion <= complete) { destroy_texture(it->texture); destroy_mesh(it->mesh); it = retired.erase(it); }
            else ++it;
        }
    }
    Buffer make_buffer(VkDeviceSize bytes, VkBufferUsageFlags usage, bool host, bool cpu_read=false,bool device_local=false) {
        if(batching_uploads && host && usage==VK_BUFFER_USAGE_TRANSFER_SRC_BIT) {
            if(batch_command){uint64_t pending=0;for(const auto& b:upload_slots[upload_index].pending)if(b.usage==VK_BUFFER_USAGE_TRANSFER_SRC_BIT)pending+=b.bytes;
                if(pending+bytes>64*1024*1024)flush_uploads();}
            ensure_upload_command();auto& slot=upload_slots[upload_index];
            auto found=std::find_if(slot.reusable.begin(),slot.reusable.end(),[bytes](const auto& b){return b.bytes>=bytes;});
            if(found!=slot.reusable.end()){Buffer result=*found;slot.cached_bytes-=result.bytes;slot.reusable.erase(found);return result;}
        }
        Buffer result;
        VkBufferCreateInfo info{ VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO }; info.size = bytes; info.usage = usage; info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        VmaAllocationCreateInfo memory{}; memory.usage = VMA_MEMORY_USAGE_AUTO;
        if (host){memory.flags=(cpu_read?VMA_ALLOCATION_CREATE_HOST_ACCESS_RANDOM_BIT:VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT)|VMA_ALLOCATION_CREATE_MAPPED_BIT;
            if(cpu_read)memory.requiredFlags=VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_CACHED_BIT;}
        if(device_local){memory.usage=VMA_MEMORY_USAGE_AUTO_PREFER_DEVICE;memory.requiredFlags=VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT|(host?VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT:0);memory.flags=host?(VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT|VMA_ALLOCATION_CREATE_MAPPED_BIT):0;}
        VmaAllocationInfo allocation{};
        check(vmaCreateBuffer(allocator, &info, &memory, &result.handle, &result.memory, &allocation), "allocate buffer");
        result.mapped = allocation.pMappedData; result.bytes=bytes;result.usage=usage;return result;
    }
    Image make_image(uint32_t width, uint32_t height, VkFormat format, VkImageUsageFlags usage, VkImageAspectFlags aspect, uint32_t mip_count = 1,uint32_t layers=1,bool array_view=false) {
        Image result;
        VkImageCreateInfo image{ VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO }; image.imageType = VK_IMAGE_TYPE_2D; image.format = format;
        image.extent = { width, height, 1 }; image.mipLevels = mip_count; image.arrayLayers = layers; image.samples = VK_SAMPLE_COUNT_1_BIT;
        if(format==VK_FORMAT_R8G8B8A8_UNORM)image.flags|=VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT;
        if(layers==6 && !array_view)image.flags|=VK_IMAGE_CREATE_CUBE_COMPATIBLE_BIT;
        image.tiling = VK_IMAGE_TILING_OPTIMAL; image.usage = usage; image.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        VmaAllocationCreateInfo memory{}; memory.usage = VMA_MEMORY_USAGE_AUTO_PREFER_DEVICE;
        check(vmaCreateImage(allocator, &image, &memory, &result.handle, &result.memory, nullptr), "allocate image");
        VkImageViewCreateInfo view{ VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO }; view.image = result.handle; view.viewType = array_view?VK_IMAGE_VIEW_TYPE_2D_ARRAY:(layers==6?VK_IMAGE_VIEW_TYPE_CUBE:VK_IMAGE_VIEW_TYPE_2D);
        view.format = format; view.subresourceRange = { aspect, 0, mip_count, 0, layers };
        try { check(vkCreateImageView(device, &view, nullptr, &result.view), "create image view"); }
        catch (...) { vmaDestroyImage(allocator, result.handle, result.memory); throw; }
        return result;
    }
    VkDescriptorSet allocate_set(VkDescriptorSetLayout layout) {
        VkDescriptorSetAllocateInfo info{ VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO }; info.descriptorPool = descriptors;
        info.descriptorSetCount = 1; info.pSetLayouts = &layout;
        VkDescriptorSet result{}; check(vkAllocateDescriptorSets(device, &info, &result), "allocate descriptor set"); return result;
    }
    static void barrier(VkCommandBuffer cmd, VkImage image, VkImageLayout old_layout, VkImageLayout new_layout,
        VkImageAspectFlags aspect, VkPipelineStageFlags2 source, VkAccessFlags2 source_access,
        VkPipelineStageFlags2 destination, VkAccessFlags2 destination_access, uint32_t mip_count = 1,uint32_t layers=1) {
        VkImageMemoryBarrier2 b{ VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2 }; b.srcStageMask = source; b.srcAccessMask = source_access;
        b.dstStageMask = destination; b.dstAccessMask = destination_access; b.oldLayout = old_layout; b.newLayout = new_layout;
        b.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED; b.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED; b.image = image;
        b.subresourceRange = { aspect, 0, mip_count, 0, layers };
        VkDependencyInfo dependency{ VK_STRUCTURE_TYPE_DEPENDENCY_INFO }; dependency.imageMemoryBarrierCount = 1; dependency.pImageMemoryBarriers = &b;
        vkCmdPipelineBarrier2(cmd, &dependency);
    }
    template<class Record> void immediate(Record&& record) {
        if(batching_uploads){ensure_upload_command();record(batch_command);return;}
        VkCommandBufferAllocateInfo allocate{ VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO }; allocate.commandPool = upload_pool;
        allocate.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; allocate.commandBufferCount = 1;
        VkCommandBuffer cmd{}; check(vkAllocateCommandBuffers(device, &allocate, &cmd), "allocate upload command");
        try {
            VkCommandBufferBeginInfo begin{ VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO }; begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
            check(vkBeginCommandBuffer(cmd, &begin), "begin upload"); record(cmd); check(vkEndCommandBuffer(cmd), "end upload");
            VkCommandBufferSubmitInfo command{ VK_STRUCTURE_TYPE_COMMAND_BUFFER_SUBMIT_INFO }; command.commandBuffer = cmd;
            uint64_t completion = ++next_completion;
            VkSemaphoreSubmitInfo signal{ VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO }; signal.semaphore = timeline; signal.value = completion; signal.stageMask = VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
            VkSubmitInfo2 submit{ VK_STRUCTURE_TYPE_SUBMIT_INFO_2 }; submit.commandBufferInfoCount = 1; submit.pCommandBufferInfos = &command;
            submit.signalSemaphoreInfoCount = 1; submit.pSignalSemaphoreInfos = &signal;
            join_present();check(vkQueueSubmit2(queue, 1, &submit, VK_NULL_HANDLE), "submit upload"); wait(completion);
        } catch (...) { vkFreeCommandBuffers(device, upload_pool, 1, &cmd); throw; }
        vkFreeCommandBuffers(device, upload_pool, 1, &cmd);
    }
    #include "upload_batches.inc"
    void init(const SevkCreateInfo& info) {
        if (!info.shader_directory || !*info.shader_directory) throw std::runtime_error("shader_directory is required");
        shader_directory = info.shader_directory; cache_directory = info.cache_directory ? info.cache_directory : "";
        anisotropy_samples = info.reserved==4||info.reserved==8||info.reserved==16 ? info.reserved : 1u;
        const char* tile_env=std::getenv("SE_VULKAN_LIGHT_TILES");light_tiles.requested=tile_env && std::strcmp(tile_env,"1")==0;
        const char* parallel_env=std::getenv("SE_VULKAN_PARALLEL_CASCADES");world.parallel_cascades=parallel_env?std::strcmp(parallel_env,"1")==0:std::thread::hardware_concurrency()>=4;
        const char* spots_env=std::getenv("SE_VULKAN_PARALLEL_SPOTS");world.parallel_spots=spots_env?std::strcmp(spots_env,"1")==0:std::thread::hardware_concurrency()>=4;
        const char* pass_env=std::getenv("SE_VULKAN_PARALLEL_PASS");pass_scene.scene.set_parallel(pass_env?std::strcmp(pass_env,"1")==0:std::thread::hardware_concurrency()>=4);
        const char* cell_emit_env=std::getenv("SE_VULKAN_DEFERRED_CELL_EMISSION");deferred_cell_emission=!cell_emit_env||std::strcmp(cell_emit_env,"0")!=0;
        const char* selection_env=std::getenv("SE_VULKAN_PARALLEL_SELECTION");pass_scene.scene.parallel_selection=!selection_env||std::strcmp(selection_env,"0")!=0;
        const char* packing_env=std::getenv("SE_VULKAN_ASYNC_PASS_PACKING");pass_scene.scene.set_async_packing(packing_env?std::strcmp(packing_env,"0")!=0:std::thread::hardware_concurrency()>=4);
        // The worker holds api_mutex while recording, so record_running identifies its joins.
        if(pipeline_census)pass_scene.scene.on_blocked=[this](double us){int lane=record_running?1:0;++packing_blocked[lane];packing_blocked_us[lane]+=uint64_t(us);
#if defined(_WIN32)
            if(!lane){void* frames[12]{};USHORT count=CaptureStackBackTrace(0,12,frames,nullptr);for(USHORT i=0;i<count;++i){auto& site=packing_wait_sites[frames[i]];++site.first;site.second+=uint64_t(us);}}
#endif
        };
        const char* upload_env=std::getenv("SE_VULKAN_PARALLEL_UPLOAD");world.parallel_upload=upload_env?std::strcmp(upload_env,"1")==0:std::thread::hardware_concurrency()>=4;
        const char* queue_env=std::getenv("SE_VULKAN_PARALLEL_QUEUE");world.parallel_queue=queue_env?std::strcmp(queue_env,"1")==0:std::thread::hardware_concurrency()>=4;
        const char* arena_env=std::getenv("SE_VULKAN_GEOMETRY_ARENA");world.geometry_arena_requested=arena_env && std::strcmp(arena_env,"1")==0;
        if (info.platform > 2 || (info.platform == 1 && !info.window) || (info.platform == 2 && (!info.create_surface || !info.get_instance_proc_addr)))
            throw std::runtime_error("invalid platform window/surface callback");
        load_loader(info.get_instance_proc_addr);
        validation = info.enable_validation && layer_available("VK_LAYER_KHRONOS_validation");
        std::vector<const char*> extensions;
        for (uint32_t i = 0; i < info.instance_extension_count; ++i) {
            if (!info.instance_extensions || !info.instance_extensions[i]) throw std::runtime_error("invalid instance extension list");
            extensions.push_back(info.instance_extensions[i]);
        }
        if (info.platform == 1) {
#if defined(_WIN32)
            extensions.push_back(VK_KHR_SURFACE_EXTENSION_NAME); extensions.push_back(VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
#else
            throw std::runtime_error("Win32 presentation unavailable on this platform");
#endif
        }
        const char* marker_env=std::getenv("SE_VULKAN_PROFILE_MARKERS");profile_markers=marker_env && std::strcmp(marker_env,"1")==0;
        const char* validation_cache_env=std::getenv("SE_VULKAN_TEXTURE_VALIDATION_CACHE");world.texture_validation_cache=!validation_cache_env || std::strcmp(validation_cache_env,"0")!=0;
        if (validation || profile_markers) extensions.push_back(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
        uint32_t instance_extension_count=0;vkEnumerateInstanceExtensionProperties(nullptr,&instance_extension_count,nullptr);
        std::vector<VkExtensionProperties> instance_extensions(instance_extension_count);
        if(instance_extension_count)vkEnumerateInstanceExtensionProperties(nullptr,&instance_extension_count,instance_extensions.data());
        bool surface_capabilities2=false;
        for(const auto& extension:instance_extensions) {
            if(info.platform!=0&&std::strcmp(extension.extensionName,VK_EXT_SWAPCHAIN_COLOR_SPACE_EXTENSION_NAME)==0)extensions.push_back(VK_EXT_SWAPCHAIN_COLOR_SPACE_EXTENSION_NAME);
#if defined(_WIN32)
            if(info.platform==1&&std::strcmp(extension.extensionName,VK_KHR_GET_SURFACE_CAPABILITIES_2_EXTENSION_NAME)==0){extensions.push_back(VK_KHR_GET_SURFACE_CAPABILITIES_2_EXTENSION_NAME);surface_capabilities2=true;}
#endif
        }
        ngx_add_instance_extensions(extensions, instance_extensions);
        std::sort(extensions.begin(), extensions.end(), [](const char* a, const char* b) { return std::strcmp(a, b) < 0; });
        extensions.erase(std::unique(extensions.begin(), extensions.end(), [](const char* a, const char* b) { return std::strcmp(a, b) == 0; }), extensions.end());
        instance = create_instance(extensions, validation, this, on_debug);
        if (validation) {
            VkDebugUtilsMessengerCreateInfoEXT create{ VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT };
            create.messageSeverity = VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
            create.messageType = VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
            create.pfnUserCallback = on_debug; create.pUserData = this;
            check(vkCreateDebugUtilsMessengerEXT(instance, &create, nullptr, &debug), "create validation messenger");
        }
        if (info.platform == 1) {
#if defined(_WIN32)
            VkWin32SurfaceCreateInfoKHR create{ VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR };
            create.hinstance = GetModuleHandleW(nullptr); create.hwnd = static_cast<HWND>(info.window); native_window = info.window;
            check(vkCreateWin32SurfaceKHR(instance, &create, nullptr, &surface), "create HWND surface");
#endif
        } else if (info.platform == 2) {
            surface = reinterpret_cast<VkSurfaceKHR>(info.create_surface(instance, info.window, info.surface_user));
            if (!surface) throw std::runtime_error("SDL3 Vulkan surface callback failed");
        }
        uint32_t count = 0; check(vkEnumeratePhysicalDevices(instance, &count, nullptr), "enumerate GPU count");
        std::vector<VkPhysicalDevice> devices(count); check(vkEnumeratePhysicalDevices(instance, &count, devices.data()), "enumerate GPUs");
        std::vector<uint32_t> order;
        if (info.adapter_index < count) order.push_back(info.adapter_index);
        for (uint32_t i = 0; i < count; ++i) if (i != info.adapter_index) order.push_back(i);
        for (uint32_t index : order) {
            VkPhysicalDeviceProperties properties{}; vkGetPhysicalDeviceProperties(devices[index], &properties);
            uint32_t flags{}; if (properties.apiVersion < minimum_api || !features_supported(devices[index], flags)) continue;
            uint32_t queues = 0; vkGetPhysicalDeviceQueueFamilyProperties(devices[index], &queues, nullptr);
            std::vector<VkQueueFamilyProperties> families(queues); vkGetPhysicalDeviceQueueFamilyProperties(devices[index], &queues, families.data());
            for (uint32_t q = 0; q < queues; ++q) {
                VkBool32 present = VK_TRUE;
                if (surface) check(vkGetPhysicalDeviceSurfaceSupportKHR(devices[index], q, surface, &present), "query presentation queue");
                if ((families[q].queueFlags & VK_QUEUE_GRAPHICS_BIT) && present) { physical = devices[index]; queue_family = q; timestamp_bits=families[q].timestampValidBits; break; }
            }
            if (physical) { stats.api_version = properties.apiVersion; copy_text(stats.device_name, properties.deviceName); break; }
        }
        if (!physical) throw std::runtime_error("No Vulkan 1.4 graphics/presentation device with dynamic rendering, sync2, timeline, scalar layout, BC textures");
        VkPhysicalDeviceVulkan12Features opaque12{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES};
        VkPhysicalDeviceVulkan11Features opaque11{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES,&opaque12};
        VkPhysicalDeviceFeatures2 optional{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,&opaque11};vkGetPhysicalDeviceFeatures2(physical,&optional);
        VkPhysicalDeviceProperties indirect_properties{};vkGetPhysicalDeviceProperties(physical,&indirect_properties);
        uint32_t queue_count=0;vkGetPhysicalDeviceQueueFamilyProperties(physical,&queue_count,nullptr);
        std::vector<VkQueueFamilyProperties> opaque_queues(queue_count);vkGetPhysicalDeviceQueueFamilyProperties(physical,&queue_count,opaque_queues.data());
        opaque_caps.available_features=(optional.features.multiDrawIndirect?SEVK_OPAQUE_MULTI_DRAW:0)
            |(opaque12.drawIndirectCount?SEVK_OPAQUE_INDIRECT_COUNT:0)
            |(optional.features.drawIndirectFirstInstance?SEVK_OPAQUE_FIRST_INSTANCE:0)
            |(opaque11.shaderDrawParameters?SEVK_OPAQUE_DRAW_PARAMETERS:0)
            |(optional.features.shaderFloat64?SEVK_OPAQUE_FLOAT64:0)
            |(opaque12.runtimeDescriptorArray?SEVK_OPAQUE_RUNTIME_ARRAY:0)
            |(opaque12.shaderSampledImageArrayNonUniformIndexing?SEVK_OPAQUE_NONUNIFORM_TEXTURE:0)
            |((opaque_queues[queue_family].queueFlags&VK_QUEUE_COMPUTE_BIT)?SEVK_OPAQUE_COMPUTE_QUEUE:0);
        const auto& ol=indirect_properties.limits;
        opaque_caps.max_draw_count=ol.maxDrawIndirectCount;opaque_caps.max_storage_range=ol.maxStorageBufferRange;
        opaque_caps.max_stage_storage=ol.maxPerStageDescriptorStorageBuffers;opaque_caps.max_set_storage=ol.maxDescriptorSetStorageBuffers;
        opaque_caps.max_stage_textures=ol.maxPerStageDescriptorSampledImages;opaque_caps.max_set_textures=ol.maxDescriptorSetSampledImages;
        opaque_caps.max_group_invocations=ol.maxComputeWorkGroupInvocations;opaque_caps.max_group_x=ol.maxComputeWorkGroupSize[0];
        opaque_caps.max_stage_resources=ol.maxPerStageResources;
        const char* opaque_env=std::getenv("SE_VULKAN_GPU_OPAQUE");opaque_caps.requested=opaque_env&&std::strcmp(opaque_env,"1")==0;
        gpu_opaque::admit(opaque_caps);
        world.indirect_supported=optional.features.multiDrawIndirect && optional.features.drawIndirectFirstInstance;
        world.indirect_limit=indirect_properties.limits.maxDrawIndirectCount;
        const char* indirect_env=std::getenv("SE_VULKAN_WORLD_INDIRECT");world.indirect_requested=indirect_env && std::strcmp(indirect_env,"1")==0;
        VkPhysicalDeviceVulkan13Features f13{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES }; f13.dynamicRendering = VK_TRUE; f13.synchronization2 = VK_TRUE; f13.shaderDemoteToHelperInvocation=VK_TRUE;
        {VkPhysicalDeviceVulkan13Features supported{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES};VkPhysicalDeviceFeatures2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,&supported};vkGetPhysicalDeviceFeatures2(physical,&query);f13.maintenance4=supported.maintenance4;interface_widening=supported.maintenance4!=0;}
        VkPhysicalDeviceVulkan12Features f12{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES, &f13 }; f12.timelineSemaphore = VK_TRUE; f12.scalarBlockLayout = VK_TRUE;
        VkPhysicalDeviceVulkan11Features f11{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES,&f12};
        VkPhysicalDeviceFeatures2 features{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &f11 }; features.features.robustBufferAccess=VK_TRUE;features.features.depthClamp=VK_TRUE;features.features.independentBlend=VK_TRUE; features.features.textureCompressionBC = VK_TRUE; features.features.geometryShader=VK_TRUE; features.features.shaderStorageImageExtendedFormats=VK_TRUE;
        // Tenant storage images are typed Unknown so DX11-compatible UAV formats bind (tenant.inc).
        tenant.without_format=optional.features.shaderStorageImageReadWithoutFormat && optional.features.shaderStorageImageWriteWithoutFormat;
        features.features.shaderStorageImageReadWithoutFormat=features.features.shaderStorageImageWriteWithoutFormat=tenant.without_format?VK_TRUE:VK_FALSE;
        if(opaque_caps.enabled){features.features.shaderFloat64=VK_TRUE;f11.shaderDrawParameters=VK_TRUE;
            f12.drawIndirectCount=VK_TRUE;f12.runtimeDescriptorArray=VK_TRUE;f12.shaderSampledImageArrayNonUniformIndexing=VK_TRUE;}
        float priority = 1;
        features.features.multiDrawIndirect=world.indirect_supported;features.features.drawIndirectFirstInstance=world.indirect_supported;
        if(optional.features.samplerAnisotropy){features.features.samplerAnisotropy=VK_TRUE;anisotropy_limit=indirect_properties.limits.maxSamplerAnisotropy;}
        shadow_depth_bias_clamp=optional.features.depthBiasClamp;features.features.depthBiasClamp=shadow_depth_bias_clamp;
        VkDeviceQueueCreateInfo q{ VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO }; q.queueFamilyIndex = queue_family; q.queueCount = 1; q.pQueuePriorities = &priority;
        std::vector<VkExtensionProperties> available;
        if(vkEnumerateDeviceExtensionProperties){uint32_t available_count=0;if(vkEnumerateDeviceExtensionProperties(physical,nullptr,&available_count,nullptr)==VK_SUCCESS){available.resize(available_count);if(vkEnumerateDeviceExtensionProperties(physical,nullptr,&available_count,available.data())!=VK_SUCCESS)available.clear();else available.resize(available_count);}}
        auto has=[&](const char* name){for(auto& e:available)if(std::strcmp(e.extensionName,name)==0)return true;return false;};
        std::vector<const char*> device_extensions;if(surface)device_extensions.push_back(VK_KHR_SWAPCHAIN_EXTENSION_NAME);
#if defined(_WIN32)
        exclusive_extension=surface&&surface_capabilities2&&has(VK_EXT_FULL_SCREEN_EXCLUSIVE_EXTENSION_NAME);if(exclusive_extension)device_extensions.push_back(VK_EXT_FULL_SCREEN_EXCLUSIVE_EXTENSION_NAME);
#endif
        VkPhysicalDeviceConditionalRenderingFeaturesEXT conditional{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_CONDITIONAL_RENDERING_FEATURES_EXT};
        if(opaque_caps.enabled&&has(VK_EXT_CONDITIONAL_RENDERING_EXTENSION_NAME)){VkPhysicalDeviceFeatures2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,&conditional};vkGetPhysicalDeviceFeatures2(physical,&query);opaque_conditional=conditional.conditionalRendering!=0;}
        if(opaque_conditional){conditional.inheritedConditionalRendering=VK_FALSE;device_extensions.push_back(VK_EXT_CONDITIONAL_RENDERING_EXTENSION_NAME);}
        void* chain=nullptr;
        VkPhysicalDeviceRobustness2FeaturesEXT nulls{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ROBUSTNESS_2_FEATURES_EXT};
        const char* robustness=has(VK_EXT_ROBUSTNESS_2_EXTENSION_NAME)?VK_EXT_ROBUSTNESS_2_EXTENSION_NAME:has("VK_KHR_robustness2")?"VK_KHR_robustness2":nullptr;
        if(robustness){VkPhysicalDeviceFeatures2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,&nulls};vkGetPhysicalDeviceFeatures2(physical,&query);
            null_descriptors=nulls.nullDescriptor!=0;nulls.robustBufferAccess2=nulls.robustImageAccess2=VK_FALSE;
            if(null_descriptors){device_extensions.push_back(robustness);nulls.pNext=chain;chain=&nulls;}}
        // Owner GBuffer pipelines add Target3 while stock pipelines keep three attachments.
        VkPhysicalDeviceDynamicRenderingUnusedAttachmentsFeaturesEXT unused_attachments{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_UNUSED_ATTACHMENTS_FEATURES_EXT};
        if(has(VK_EXT_DYNAMIC_RENDERING_UNUSED_ATTACHMENTS_EXTENSION_NAME)){VkPhysicalDeviceFeatures2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,&unused_attachments};vkGetPhysicalDeviceFeatures2(physical,&query);
            gbuffer_velocity_supported=unused_attachments.dynamicRenderingUnusedAttachments!=0;
            if(gbuffer_velocity_supported){device_extensions.push_back(VK_EXT_DYNAMIC_RENDERING_UNUSED_ATTACHMENTS_EXTENSION_NAME);unused_attachments.pNext=chain;chain=&unused_attachments;}}

        gpu_checkpoints=has(VK_NV_DEVICE_DIAGNOSTIC_CHECKPOINTS_EXTENSION_NAME);if(gpu_checkpoints)device_extensions.push_back(VK_NV_DEVICE_DIAGNOSTIC_CHECKPOINTS_EXTENSION_NAME);
        VkDeviceDiagnosticsConfigCreateInfoNV diagnostics{VK_STRUCTURE_TYPE_DEVICE_DIAGNOSTICS_CONFIG_CREATE_INFO_NV};diagnostics.flags=VK_DEVICE_DIAGNOSTICS_CONFIG_ENABLE_RESOURCE_TRACKING_BIT_NV;
        if(has(VK_NV_DEVICE_DIAGNOSTICS_CONFIG_EXTENSION_NAME)){VkPhysicalDeviceDiagnosticsConfigFeaturesNV supported{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DIAGNOSTICS_CONFIG_FEATURES_NV};VkPhysicalDeviceFeatures2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,&supported};vkGetPhysicalDeviceFeatures2(physical,&query);
            if(supported.diagnosticsConfig){device_extensions.push_back(VK_NV_DEVICE_DIAGNOSTICS_CONFIG_EXTENSION_NAME);diagnostics.pNext=chain;chain=&diagnostics;}}
        VkPhysicalDeviceDiagnosticsConfigFeaturesNV diagnostics_feature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DIAGNOSTICS_CONFIG_FEATURES_NV};
        if(chain==&diagnostics){diagnostics_feature.diagnosticsConfig=VK_TRUE;diagnostics_feature.pNext=chain;chain=&diagnostics_feature;}
        VkPhysicalDeviceFaultFeaturesEXT fault{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FAULT_FEATURES_EXT};
        if(has(VK_EXT_DEVICE_FAULT_EXTENSION_NAME)){VkPhysicalDeviceFaultFeaturesEXT supported{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FAULT_FEATURES_EXT};VkPhysicalDeviceFeatures2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,&supported};vkGetPhysicalDeviceFeatures2(physical,&query);
            if(supported.deviceFault){gpu_fault=true;fault.deviceFault=VK_TRUE;fault.deviceFaultVendorBinary=VK_FALSE;fault.pNext=chain;chain=&fault;device_extensions.push_back(VK_EXT_DEVICE_FAULT_EXTENSION_NAME);}}
        VkPhysicalDeviceRayQueryFeaturesKHR ray_query_feature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_RAY_QUERY_FEATURES_KHR};
        VkPhysicalDeviceAccelerationStructureFeaturesKHR acceleration_feature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ACCELERATION_STRUCTURE_FEATURES_KHR,&ray_query_feature};
        VkPhysicalDeviceVulkan12Features address_query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES,&acceleration_feature};
        bool ray_extensions=has(VK_KHR_ACCELERATION_STRUCTURE_EXTENSION_NAME)&&has(VK_KHR_RAY_QUERY_EXTENSION_NAME)&&has(VK_KHR_DEFERRED_HOST_OPERATIONS_EXTENSION_NAME);
        if(ray_extensions){VkPhysicalDeviceFeatures2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,&address_query};vkGetPhysicalDeviceFeatures2(physical,&query);
            if(ray_query_feature.rayQuery && acceleration_feature.accelerationStructure && address_query.bufferDeviceAddress && optional.features.shaderInt64){
                f12.bufferDeviceAddress=VK_TRUE;features.features.shaderInt64=VK_TRUE;ray_query_feature.rayQuery=VK_TRUE;acceleration_feature.accelerationStructure=VK_TRUE;
                ray_query_feature.pNext=chain;acceleration_feature.pNext=&ray_query_feature;chain=&acceleration_feature;ray.supported=true;
                device_extensions.push_back(VK_KHR_ACCELERATION_STRUCTURE_EXTENSION_NAME);device_extensions.push_back(VK_KHR_RAY_QUERY_EXTENSION_NAME);device_extensions.push_back(VK_KHR_DEFERRED_HOST_OPERATIONS_EXTENSION_NAME);
                VkPhysicalDeviceAccelerationStructurePropertiesKHR limits{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ACCELERATION_STRUCTURE_PROPERTIES_KHR};VkPhysicalDeviceProperties2 properties{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2,&limits};vkGetPhysicalDeviceProperties2(physical,&properties);
                ray.max_primitives=limits.maxPrimitiveCount<2000000ull?uint32_t(limits.maxPrimitiveCount):2000000u;ray.max_instances=limits.maxInstanceCount<8192ull?uint32_t(limits.maxInstanceCount):8192u;
                ray.scratch_alignment=std::max(1u,limits.minAccelerationStructureScratchOffsetAlignment);
                VkFormatProperties hdr{};vkGetPhysicalDeviceFormatProperties(physical,VK_FORMAT_R16G16B16A16_SFLOAT,&hdr);ray.hdr_storage=(hdr.optimalTilingFeatures&VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT)!=0;
                VkFormatProperties visibility{};vkGetPhysicalDeviceFormatProperties(physical,VK_FORMAT_R8_UNORM,&visibility);ray.visibility_storage=(visibility.optimalTilingFeatures&VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT)!=0;}}
        if(opaque_conditional){conditional.pNext=chain;chain=&conditional;}f13.pNext=chain;
        ngx_add_device_extensions(device_extensions, available, f12);
        VkDeviceCreateInfo create{ VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO, &features }; create.queueCreateInfoCount = 1; create.pQueueCreateInfos = &q;
        create.enabledExtensionCount=uint32_t(device_extensions.size());create.ppEnabledExtensionNames=device_extensions.empty()?nullptr:device_extensions.data();
        check(vkCreateDevice(physical, &create, nullptr, &device), "create Vulkan device"); volkLoadDevice(device); vkGetDeviceQueue(device, queue_family, 0, &queue);
#if defined(_WIN32)
        if(exclusive_extension && !vkAcquireFullScreenExclusiveModeEXT)exclusive_extension=false;
#endif
        if(gpu_checkpoints && !vkCmdSetCheckpointNV)gpu_checkpoints=false;if(gpu_fault && !vkGetDeviceFaultInfoEXT)gpu_fault=false;
        if(ray.supported && (!vkCreateAccelerationStructureKHR || !vkCmdBuildAccelerationStructuresKHR || !vkGetAccelerationStructureBuildSizesKHR || !vkGetAccelerationStructureDeviceAddressKHR || !vkGetBufferDeviceAddress))ray.supported=false;
        VmaVulkanFunctions functions{}; functions.vkGetInstanceProcAddr = vkGetInstanceProcAddr; functions.vkGetDeviceProcAddr = vkGetDeviceProcAddr;
        VmaAllocatorCreateInfo vma{}; vma.physicalDevice = physical; vma.device = device; vma.instance = instance;
        vma.vulkanApiVersion = minimum_api; vma.pVulkanFunctions = &functions;
        if(ray.supported)vma.flags|=VMA_ALLOCATOR_CREATE_BUFFER_DEVICE_ADDRESS_BIT;
        check(vmaCreateAllocator(&vma, &allocator), "create Vulkan 1.4 memory allocator");
        VkSemaphoreTypeCreateInfo type{ VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO }; type.semaphoreType = VK_SEMAPHORE_TYPE_TIMELINE;
        VkSemaphoreCreateInfo semaphore{ VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO, &type }; check(vkCreateSemaphore(device, &semaphore, nullptr, &timeline), "create timeline");
        VkCommandPoolCreateInfo pool{ VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO }; pool.queueFamilyIndex = queue_family; pool.flags = VK_COMMAND_POOL_CREATE_TRANSIENT_BIT;
        check(vkCreateCommandPool(device, &pool, nullptr, &upload_pool), "create upload pool");
        init_descriptors(); init_slots(); init_pipeline_cache(); init_shadows();
        if(timestamp_bits){VkPhysicalDeviceProperties properties{};vkGetPhysicalDeviceProperties(physical,&properties);timestamp_period=properties.limits.timestampPeriod;
            VkQueryPoolCreateInfo qp{VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO};qp.queryType=VK_QUERY_TYPE_TIMESTAMP;qp.queryCount=4;check(vkCreateQueryPool(device,&qp,nullptr,&timing_pool),"create world timing queries");resolution_stats.timing_supported=1;}
        resize(info.width, info.height, info.vsync != 0);
        const uint32_t white = 0xffffffff; upload_texture(0, 1, 1, 0, false, &white, sizeof(white), true);
        stats.size = sizeof(stats); stats.abi_version = SEVK_ABI_VERSION; stats.validation_enabled = validation;
    }
    void apply_anisotropy(VkSamplerCreateInfo& sample) {
        sample.anisotropyEnable = VK_FALSE; sample.maxAnisotropy = 1.f;
        uint32_t samples = anisotropy_samples==4||anisotropy_samples==8||anisotropy_samples==16 ? anisotropy_samples : 1u;
        if (samples>=2 && anisotropy_limit>=2.f) { sample.anisotropyEnable = VK_TRUE; sample.maxAnisotropy = std::min(float(samples), anisotropy_limit); }
    }
    void set_anisotropy(uint32_t samples) {
        if (recording) throw std::runtime_error("anisotropy cannot change during recording");
        uint32_t normalized = samples==4||samples==8||samples==16 ? samples : 1u;
        if (normalized==anisotropy_samples) return;
        anisotropy_samples = normalized;
        if (!sampler) return;
        join_present(false); check(vkDeviceWaitIdle(device), "wait before material sampler replacement");
        VkSamplerCreateInfo sample{ VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO }; sample.magFilter = VK_FILTER_LINEAR; sample.minFilter = VK_FILTER_LINEAR;
        sample.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR; sample.addressModeU = sample.addressModeV = sample.addressModeW = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        sample.maxLod = VK_LOD_CLAMP_NONE; apply_anisotropy(sample);
        VkSampler next{}; check(vkCreateSampler(device, &sample, nullptr, &next), "replace texture sampler");
        VkSampler previous = sampler; sampler = next;
        for (auto& item : textures) if (item.second.descriptor) {
            VkDescriptorImageInfo info{sampler, VK_NULL_HANDLE, VK_IMAGE_LAYOUT_UNDEFINED};
            VkWriteDescriptorSet write{ VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET }; write.dstSet = item.second.descriptor; write.dstBinding = 1;
            write.descriptorCount = 1; write.descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER; write.pImageInfo = &info;
            vkUpdateDescriptorSets(device, 1, &write, 0, nullptr);
        }
        for (auto& slot : world.slots) { release_cached_materials(slot); slot.probe_materials.clear(); }
        if (opaque_reference.initialized) for (auto& slot : opaque_reference.slots) if (slot.set) {
            VkDescriptorImageInfo info{sampler, VK_NULL_HANDLE, VK_IMAGE_LAYOUT_UNDEFINED};
            VkWriteDescriptorSet write{ VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET }; write.dstSet = slot.set; write.dstBinding = 13;
            write.descriptorCount = 1; write.descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER; write.pImageInfo = &info;
            vkUpdateDescriptorSets(device, 1, &write, 0, nullptr);
        }
        vkDestroySampler(device, previous, nullptr);
    }
    void init_descriptors() {
        VkDescriptorSetLayoutBinding frame{}; frame.binding = 0; frame.descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        frame.descriptorCount = 1; frame.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        VkDescriptorSetLayoutCreateInfo layout{ VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO }; layout.bindingCount = 1; layout.pBindings = &frame;
        check(vkCreateDescriptorSetLayout(device, &layout, nullptr, &frame_layout), "create frame layout");
        std::array<VkDescriptorSetLayoutBinding, 5> texture{};
        texture[0] = { 0, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr };
        texture[1] = { 1, VK_DESCRIPTOR_TYPE_SAMPLER, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr };
        for (uint32_t i = 2; i < 5; ++i) texture[i] = { i,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,1,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr };
        layout.bindingCount = 5; layout.pBindings = texture.data();
        check(vkCreateDescriptorSetLayout(device, &layout, nullptr, &texture_layout), "create texture layout");
        std::array<VkDescriptorPoolSize, 3> sizes{ VkDescriptorPoolSize{ VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 2 },
            VkDescriptorPoolSize{ VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 131072 }, VkDescriptorPoolSize{ VK_DESCRIPTOR_TYPE_SAMPLER, 32768 } };
        VkDescriptorPoolCreateInfo pool{ VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO }; pool.flags = VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT;
        pool.maxSets = 32770; pool.poolSizeCount = static_cast<uint32_t>(sizes.size()); pool.pPoolSizes = sizes.data();
        check(vkCreateDescriptorPool(device, &pool, nullptr, &descriptors), "create resource descriptors");
        std::array<VkDescriptorSetLayoutBinding,2> shadow_bindings{ VkDescriptorSetLayoutBinding{0,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,4,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr},
            VkDescriptorSetLayoutBinding{1,VK_DESCRIPTOR_TYPE_SAMPLER,1,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr} };
        layout.bindingCount=2; layout.pBindings=shadow_bindings.data();
        check(vkCreateDescriptorSetLayout(device,&layout,nullptr,&shadow_layout),"create shadow layout");
        std::array<VkDescriptorSetLayout, 3> layouts{ frame_layout, texture_layout,shadow_layout };
        VkPushConstantRange push{ VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(MeshPush) };
        VkPipelineLayoutCreateInfo pipeline{ VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO }; pipeline.setLayoutCount = 3; pipeline.pSetLayouts = layouts.data();
        pipeline.pushConstantRangeCount = 1; pipeline.pPushConstantRanges = &push;
        check(vkCreatePipelineLayout(device, &pipeline, nullptr, &pipeline_layout), "create pipeline layout");
        VkSamplerCreateInfo sample{ VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO }; sample.magFilter = VK_FILTER_LINEAR; sample.minFilter = VK_FILTER_LINEAR;
        sample.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR; sample.addressModeU = sample.addressModeV = sample.addressModeW = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        sample.maxLod = VK_LOD_CLAMP_NONE; apply_anisotropy(sample);
        check(vkCreateSampler(device, &sample, nullptr, &sampler), "create texture sampler");
        sample.anisotropyEnable = VK_FALSE; sample.maxAnisotropy = 1.f;
        sample.addressModeU = sample.addressModeV = sample.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        check(vkCreateSampler(device, &sample, nullptr, &sprite_sampler), "create stock linear clamp sprite sampler");
    }
    VkSampler standard_sampler(uint32_t binding) const {
        // Common.hlsli / MySamplerStateManager.StandardSamplers: only
        // TextureSampler wraps and uses the user's anisotropy setting.
        if(binding==259)return sampler;
        if(binding==256 || binding==258)return sprite_sampler;
        if(binding==257 && post.point)return post.point;
        if(binding==271)return shadow_sampler;
        throw std::runtime_error("unsupported stock sampler binding");
    }
    void init_slots() {
        for (auto& slot : slots) {
            VkCommandPoolCreateInfo pool{ VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO }; pool.queueFamilyIndex = queue_family; pool.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
            check(vkCreateCommandPool(device, &pool, nullptr, &slot.pool), "create frame pool");
            VkCommandBufferAllocateInfo command{ VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO }; command.commandPool = slot.pool;
            command.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; command.commandBufferCount = 1;
            check(vkAllocateCommandBuffers(device, &command, &slot.command), "create frame command");
            VkSemaphoreCreateInfo semaphore{ VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO }; check(vkCreateSemaphore(device, &semaphore, nullptr, &slot.acquired), "create acquisition semaphore");
            slot.uniform = make_buffer(sizeof(NativeFrame), VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT, true);
            slot.descriptor = allocate_set(frame_layout);
            VkDescriptorBufferInfo buffer{ slot.uniform.handle, 0, sizeof(NativeFrame) };
            VkWriteDescriptorSet write{ VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET }; write.dstSet = slot.descriptor; write.dstBinding = 0;
            write.descriptorCount = 1; write.descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER; write.pBufferInfo = &buffer;
            vkUpdateDescriptorSets(device, 1, &write, 0, nullptr);
        }
    }
    void init_pipeline_cache() {
        std::vector<char> data;
        if (!cache_directory.empty()) {
            std::ifstream input(std::filesystem::path(cache_directory) / "pipeline-v1.cache", std::ios::binary | std::ios::ate);
            if (input && input.tellg() > 0 && input.tellg() < 64 * 1024 * 1024) {
                data.resize(static_cast<size_t>(input.tellg())); input.seekg(0); input.read(data.data(), data.size());
            }
        }
        // A cache is driver-owned binary data. Reject foreign/truncated headers
        // before handing any persisted bytes to the Vulkan driver.
        if(!data.empty()) {
            VkPhysicalDeviceProperties props{};vkGetPhysicalDeviceProperties(physical,&props);
            uint32_t header[4]{}; if(data.size()>=32) std::memcpy(header,data.data(),sizeof(header));
            if(data.size()<32 || header[0]!=32 || header[1]!=VK_PIPELINE_CACHE_HEADER_VERSION_ONE || header[2]!=props.vendorID || header[3]!=props.deviceID ||
                std::memcmp(data.data()+16,props.pipelineCacheUUID,VK_UUID_SIZE)!=0) data.clear();
        }
        VkPipelineCacheCreateInfo cache{ VK_STRUCTURE_TYPE_PIPELINE_CACHE_CREATE_INFO }; cache.initialDataSize = data.size(); cache.pInitialData = data.data();
        VkResult result = vkCreatePipelineCache(device, &cache, nullptr, &pipeline_cache);
        if (result != VK_SUCCESS && !data.empty()) { cache.initialDataSize = 0; cache.pInitialData = nullptr; result = vkCreatePipelineCache(device, &cache, nullptr, &pipeline_cache); }
        check(result, "create pipeline cache");
    }
    VkShaderModule shader(const char* name) {
        auto path = std::filesystem::path(shader_directory) / (std::string(name) + ".spv");
        std::ifstream input(path, std::ios::binary | std::ios::ate);
        if (!input || input.tellg() < 20 || input.tellg() > 16 * 1024 * 1024 || input.tellg() % 4 != 0)
            throw std::runtime_error("Missing/invalid compiled shader: " + path.string());
        std::vector<uint32_t> words(static_cast<size_t>(input.tellg()) / 4); input.seekg(0); input.read(reinterpret_cast<char*>(words.data()), words.size() * 4);
        SpvReflectShaderModule reflected{};
        if (spvReflectCreateShaderModule(words.size() * 4, words.data(), &reflected) != SPV_REFLECT_RESULT_SUCCESS)
            throw std::runtime_error("Shader reflection failed: " + path.string());
        bool valid = true;
        for (uint32_t i = 0; i < reflected.descriptor_binding_count; ++i) {
            const auto& binding = reflected.descriptor_bindings[i];
            if (!((binding.set == 0 && binding.binding == 0 && binding.descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER) ||
                (binding.set == 1 && binding.binding != 1 && binding.binding < 5 && binding.descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE) ||
                (binding.set == 1 && binding.binding == 1 && binding.descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER) ||
                (binding.set == 2 && binding.binding == 0 && binding.descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE && binding.count == 4) ||
                (binding.set == 2 && binding.binding == 1 && binding.descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER))) valid = false;
        }
        for (uint32_t i = 0; i < reflected.push_constant_block_count; ++i) if (reflected.push_constant_blocks[i].size > sizeof(MeshPush)) valid = false;
        spvReflectDestroyShaderModule(&reflected);
        if (!valid) throw std::runtime_error("Shader interface exceeds ABI layout: " + path.string());
        VkShaderModuleCreateInfo info{ VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO }; info.codeSize = words.size() * 4; info.pCode = words.data();
        VkShaderModule result{}; check(vkCreateShaderModule(device, &info, nullptr, &result), "create SPIR-V shader"); return result;
    }
    VkPipeline make_pipeline(bool mesh, bool additive, bool transparent = false, bool target_depth = true, VkFormat target_format = VK_FORMAT_UNDEFINED, bool skybox = false, bool shadow = false, bool no_depth_test = false, bool premultiplied = false, bool write_alpha = true, bool lines = false) {
        VkShaderModule vertex = shader(lines?"line_vs":shadow?"shadow_vs":(mesh ? "mesh_vs" : "sprite_vs")), fragment{};
        try {
            fragment = shader(lines?"line_ps":shadow?"shadow_ps":(mesh ? "mesh_ps" : "sprite_ps"));
            std::array<VkPipelineShaderStageCreateInfo, 2> stages{};
            stages[0] = { VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO, nullptr, 0, VK_SHADER_STAGE_VERTEX_BIT, vertex, "main", nullptr };
            stages[1] = { VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO, nullptr, 0, VK_SHADER_STAGE_FRAGMENT_BIT, fragment, "main", nullptr };
            VkFormat render_format = target_format == VK_FORMAT_UNDEFINED ? color_format : target_format;
            bool hardware_linear=render_format==VK_FORMAT_R8G8B8A8_SRGB || render_format==VK_FORMAT_B8G8R8A8_SRGB || (hdr_active && render_format==VK_FORMAT_R16G16B16A16_SFLOAT);
            uint32_t encode_display=hardware_linear?0u:1u;
            VkSpecializationMapEntry encode_entry{0,0,sizeof(uint32_t)};
            VkSpecializationInfo encode_info{1,&encode_entry,sizeof(uint32_t),&encode_display};
            if(!shadow) stages[1].pSpecializationInfo=&encode_info;
            VkVertexInputBindingDescription binding{ 0, sizeof(SevkVertex), VK_VERTEX_INPUT_RATE_VERTEX };
            std::array<VkVertexInputAttributeDescription, 3> attributes{ VkVertexInputAttributeDescription{ 0,0,VK_FORMAT_R32G32B32_SFLOAT,0 },
                VkVertexInputAttributeDescription{ 1,0,VK_FORMAT_R32G32B32_SFLOAT,12 }, VkVertexInputAttributeDescription{ 2,0,VK_FORMAT_R32G32_SFLOAT,24 } };
            VkPipelineVertexInputStateCreateInfo input{ VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO };
            if (mesh) { input.vertexBindingDescriptionCount = 1; input.pVertexBindingDescriptions = &binding; input.vertexAttributeDescriptionCount = 3; input.pVertexAttributeDescriptions = attributes.data(); }
            VkVertexInputBindingDescription line_binding{ 0, sizeof(SevkLineVertex), VK_VERTEX_INPUT_RATE_VERTEX };
            std::array<VkVertexInputAttributeDescription, 2> line_attributes{ VkVertexInputAttributeDescription{ 0,0,VK_FORMAT_R32G32B32_SFLOAT,0 }, VkVertexInputAttributeDescription{ 1,0,VK_FORMAT_R8G8B8A8_UNORM,12 } };
            if (lines) { input.vertexBindingDescriptionCount = 1; input.pVertexBindingDescriptions = &line_binding; input.vertexAttributeDescriptionCount = 2; input.pVertexAttributeDescriptions = line_attributes.data(); }
            VkPipelineInputAssemblyStateCreateInfo assembly{ VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO }; assembly.topology = lines ? VK_PRIMITIVE_TOPOLOGY_LINE_LIST : VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
            VkPipelineViewportStateCreateInfo viewport{ VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO }; viewport.viewportCount = viewport.scissorCount = 1;
            VkPipelineRasterizationStateCreateInfo raster{ VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO }; raster.polygonMode = VK_POLYGON_MODE_FILL;
            raster.cullMode = VK_CULL_MODE_NONE; raster.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE; raster.lineWidth = 1;
            raster.depthBiasEnable=shadow; raster.depthBiasConstantFactor=shadow?1.5f:0; raster.depthBiasSlopeFactor=shadow?2.f:0;
            VkPipelineMultisampleStateCreateInfo samples{ VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO }; samples.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;
            VkPipelineDepthStencilStateCreateInfo depth_state{ VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO };
            depth_state.depthTestEnable = (mesh || lines) && !no_depth_test; depth_state.depthWriteEnable = mesh && !transparent && !skybox && !no_depth_test; depth_state.depthCompareOp = shadow?VK_COMPARE_OP_LESS_OR_EQUAL:VK_COMPARE_OP_GREATER_OR_EQUAL;
            VkPipelineColorBlendAttachmentState attachment{}; attachment.colorWriteMask = write_alpha ? 0xf : 0x7;
            if (!mesh || transparent) {
                attachment.blendEnable = VK_TRUE; attachment.srcColorBlendFactor = mesh || premultiplied?VK_BLEND_FACTOR_ONE:VK_BLEND_FACTOR_SRC_ALPHA;
                attachment.dstColorBlendFactor = additive ? VK_BLEND_FACTOR_ONE : VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA; attachment.colorBlendOp = VK_BLEND_OP_ADD;
                attachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE; attachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA; attachment.alphaBlendOp = VK_BLEND_OP_ADD;
            }
            VkPipelineColorBlendStateCreateInfo blend{ VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO }; blend.attachmentCount = shadow?0:1; blend.pAttachments = &attachment;
            std::array<VkDynamicState, 2> dynamics{ VK_DYNAMIC_STATE_VIEWPORT, VK_DYNAMIC_STATE_SCISSOR };
            VkPipelineDynamicStateCreateInfo dynamic{ VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO }; dynamic.dynamicStateCount = 2; dynamic.pDynamicStates = dynamics.data();
            VkPipelineRenderingCreateInfo rendering{ VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO }; rendering.colorAttachmentCount = shadow?0:1; rendering.pColorAttachmentFormats = &render_format;
            rendering.depthAttachmentFormat = target_depth ? VK_FORMAT_D32_SFLOAT : VK_FORMAT_UNDEFINED;
            VkGraphicsPipelineCreateInfo pipeline{ VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO, &rendering }; pipeline.stageCount = 2; pipeline.pStages = stages.data();
            pipeline.pVertexInputState = &input; pipeline.pInputAssemblyState = &assembly; pipeline.pViewportState = &viewport; pipeline.pRasterizationState = &raster;
            pipeline.pMultisampleState = &samples; pipeline.pDepthStencilState = &depth_state; pipeline.pColorBlendState = &blend; pipeline.pDynamicState = &dynamic; pipeline.layout = pipeline_layout;
            VkPipeline result{}; check(vkCreateGraphicsPipelines(device, pipeline_cache, 1, &pipeline, nullptr, &result), "create dynamic rendering pipeline");
            vkDestroyShaderModule(device, fragment, nullptr); vkDestroyShaderModule(device, vertex, nullptr); return result;
        } catch (...) { if (fragment) vkDestroyShaderModule(device, fragment, nullptr); vkDestroyShaderModule(device, vertex, nullptr); throw; }
    }
    void init_shadows() {
        VkFormatProperties depth_properties{};vkGetPhysicalDeviceFormatProperties(physical,VK_FORMAT_D32_SFLOAT,&depth_properties);
        if((depth_properties.optimalTilingFeatures&(VK_FORMAT_FEATURE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT)) !=
            (VK_FORMAT_FEATURE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT)) throw std::runtime_error("D32 sampled shadow depth unsupported");
        for(auto& image:shadow_images) image=make_image(1024,1024,VK_FORMAT_D32_SFLOAT,
            VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT,VK_IMAGE_ASPECT_DEPTH_BIT);
        // Stock Shadowmap is comparison-linear with clamp addressing. Vulkan
        // depth-comparison filtering permits linear even without the ordinary
        // non-comparison linear-filter format bit; do not downgrade PCF to point.
        VkSamplerCreateInfo sampler_info{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO}; sampler_info.magFilter=sampler_info.minFilter=VK_FILTER_LINEAR;
        sampler_info.mipmapMode=VK_SAMPLER_MIPMAP_MODE_LINEAR; sampler_info.addressModeU=sampler_info.addressModeV=sampler_info.addressModeW=VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        sampler_info.borderColor=VK_BORDER_COLOR_FLOAT_OPAQUE_WHITE; sampler_info.compareEnable=VK_TRUE; sampler_info.compareOp=VK_COMPARE_OP_LESS_OR_EQUAL;
        check(vkCreateSampler(device,&sampler_info,nullptr,&shadow_sampler),"create PCF shadow sampler");
        immediate([&](VkCommandBuffer command) {
            for(auto& image:shadow_images) {
                barrier(command,image.handle,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,
                    VK_PIPELINE_STAGE_2_NONE,0,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT);
                VkClearDepthStencilValue clear{1,0}; VkImageSubresourceRange range{VK_IMAGE_ASPECT_DEPTH_BIT,0,1,0,1};
                vkCmdClearDepthStencilImage(command,image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,&clear,1,&range);
                barrier(command,image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,
                    VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
            }
        });
        shadow_descriptor=allocate_set(shadow_layout);
        std::array<VkDescriptorImageInfo,4> images{};
        for(uint32_t i=0;i<4;++i) images[i]={VK_NULL_HANDLE,shadow_images[i].view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
        VkDescriptorImageInfo sampler_descriptor{shadow_sampler,VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};
        std::array<VkWriteDescriptorSet,2> writes{};
        for(auto& write:writes) {write.sType=VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;write.dstSet=shadow_descriptor;}
        writes[0].dstBinding=0;writes[0].descriptorCount=4;writes[0].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;writes[0].pImageInfo=images.data();
        writes[1].dstBinding=1;writes[1].descriptorCount=1;writes[1].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLER;writes[1].pImageInfo=&sampler_descriptor;
        vkUpdateDescriptorSets(device,2,writes.data(),0,nullptr);
        shadow_pipeline=make_pipeline(true,false,false,true,VK_FORMAT_UNDEFINED,false,true);
    }
    void render_shadows() {
        if(!shadow_count) return;
        // Menu/preview draws share the cascade images with the world. Their
        // contents no longer belong to either world shadow-cache policy.
        saved_shadows=false;shadow_image_mask&=~((1u<<std::min(4u,shadow_count))-1u);
        auto& slot=slots[slot_index];
        vkCmdBindDescriptorSets(slot.command,VK_PIPELINE_BIND_POINT_GRAPHICS,pipeline_layout,0,1,&slot.descriptor,0,nullptr);
        for(uint32_t cascade=0;cascade<std::min(4u,shadow_count);++cascade) {
            auto& image=shadow_images[cascade];
            barrier(slot.command,image.handle,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,
                VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
                VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);
            VkRenderingAttachmentInfo attachment{VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO};attachment.imageView=image.view;attachment.imageLayout=VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;
            attachment.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR;attachment.storeOp=VK_ATTACHMENT_STORE_OP_STORE;attachment.clearValue.depthStencil={1,0};
            VkRenderingInfo rendering{VK_STRUCTURE_TYPE_RENDERING_INFO};rendering.renderArea={{0,0},{shadow_resolution,shadow_resolution}};rendering.layerCount=1;rendering.pDepthAttachment=&attachment;
            vkCmdBeginRendering(slot.command,&rendering);
            for(const auto& draw:frame_meshes) if((draw.flags&(2|4|8|256|512))==0) emit_mesh(draw,static_cast<int>(cascade));
            vkCmdEndRendering(slot.command);
            barrier(slot.command,image.handle,VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_DEPTH_BIT,
                VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,
                VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
        }
    }
    void release_exclusive() {
#if defined(_WIN32)
        if (exclusive_held && swapchain && vkReleaseFullScreenExclusiveModeEXT) {
            VkResult result = vkReleaseFullScreenExclusiveModeEXT(device, swapchain);
            if (result != VK_SUCCESS && result != VK_ERROR_FULL_SCREEN_EXCLUSIVE_MODE_LOST_EXT && result != VK_ERROR_SURFACE_LOST_KHR)
                exclusive_held = false;
        }
#endif
        exclusive_held = false;
    }
    void try_acquire_exclusive(bool deny_on_failure) {
#if defined(_WIN32)
        if (!exclusive_swapchain || !exclusive_requested || !exclusive_extension || exclusive_held || exclusive_denied || !swapchain || !exclusive_monitor || !vkAcquireFullScreenExclusiveModeEXT) return;
        VkResult result = vkAcquireFullScreenExclusiveModeEXT(device, swapchain);
        if (result == VK_SUCCESS) { exclusive_held = true; return; }
        exclusive_held = false;
        if (deny_on_failure && result != VK_ERROR_FULL_SCREEN_EXCLUSIVE_MODE_LOST_EXT) exclusive_denied = true;
#else
        (void)deny_on_failure;
#endif
    }
    void destroy_targets() {
        join_present(false);
        release_exclusive();
        destroy_buffer(capture); capture_valid=false; capture_completion=0;
        destroy_image(offscreen); destroy_image(depth);
        for (auto view : swap_views) vkDestroyImageView(device, view, nullptr);
        for (auto semaphore : present_semaphores) vkDestroySemaphore(device, semaphore, nullptr);
        swap_views.clear(); swap_images.clear(); present_semaphores.clear(); image_initialized.clear();
        if (swapchain) vkDestroySwapchainKHR(device, swapchain, nullptr); swapchain = {}; exclusive_swapchain = false;
        offscreen_initialized = depth_initialized = last_image_valid = false;
    }
    void resize_output(uint32_t width, uint32_t height, uint32_t logical_width, uint32_t logical_height, bool sync) {
        if (recording) throw std::runtime_error("resize during an open frame");
        if (width > 16384 || height > 16384 || logical_width > 16384 || logical_height > 16384 ||
            ((width == 0 || height == 0) != (logical_width == 0 || logical_height == 0))) throw std::runtime_error("invalid render extent");
        join_present(false);flush_uploads();wait(next_completion);
        // The render timeline does not cover the presentation semaphore wait.
        if(swapchain)check(vkQueueWaitIdle(queue),"wait for presentation resize");
        collect();destroy_targets();vsync = sync;
        sprite_w = logical_width; sprite_h = logical_height;
        if (!width || !height) { suspended = true; extent = { width, height }; stats.surface_ready = 0; return; }
        VkFormat previous = color_format;
        if (surface) {
            VkSurfaceCapabilitiesKHR caps{}; check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physical, surface, &caps), "query surface capabilities");
            if (caps.currentExtent.width != UINT32_MAX) extent = caps.currentExtent;
            else extent = { std::clamp(width, caps.minImageExtent.width, caps.maxImageExtent.width), std::clamp(height, caps.minImageExtent.height, caps.maxImageExtent.height) };
            present_extent = extent;
            if (!extent.width || !extent.height) { suspended = true; stats.surface_ready = 0; return; }
            uint32_t count = 0; check(vkGetPhysicalDeviceSurfaceFormatsKHR(physical, surface, &count, nullptr), "query surface formats");
            std::vector<VkSurfaceFormatKHR> formats(count); check(vkGetPhysicalDeviceSurfaceFormatsKHR(physical, surface, &count, formats.data()), "query surface formats");
            hdr_active = false;
            auto chosen = formats.end();
            if ((display_flags & 1) && !hdr_denied) chosen = std::find_if(formats.begin(), formats.end(), [](const auto& f) { return f.format == VK_FORMAT_R16G16B16A16_SFLOAT && f.colorSpace == VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT; });
            if (chosen != formats.end()) hdr_active = true;
            else chosen = std::find_if(formats.begin(), formats.end(), [](const auto& f) { return f.format == VK_FORMAT_B8G8R8A8_SRGB && f.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR; });
            if (chosen == formats.end()) chosen = std::find_if(formats.begin(), formats.end(), [](const auto& f) { return f.format == VK_FORMAT_R8G8B8A8_SRGB && f.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR; });
            if (chosen == formats.end()) throw std::runtime_error("Surface lacks RGBA8/BGRA8 sRGB attachment required for linear UI blending");
            if ((display_flags & 1) && !hdr_active) hdr_denied = true;
            if (!(display_flags & 1)) hdr_denied = false;
            color_format = chosen->format;
            VkPresentModeKHR mode = VK_PRESENT_MODE_FIFO_KHR;
            if (!vsync) {
                count = 0; check(vkGetPhysicalDeviceSurfacePresentModesKHR(physical, surface, &count, nullptr), "query present modes");
                std::vector<VkPresentModeKHR> modes(count); check(vkGetPhysicalDeviceSurfacePresentModesKHR(physical, surface, &count, modes.data()), "query present modes");
                bool generated = (display_flags & 2) != 0;
                // Display bit 32 (HdrRender AllowTearing) also prefers IMMEDIATE; DX11 tears only without vsync too.
                bool tearing = (display_flags & 32) != 0;
                if ((generated || tearing) && std::find(modes.begin(), modes.end(), VK_PRESENT_MODE_IMMEDIATE_KHR) != modes.end()) mode = VK_PRESENT_MODE_IMMEDIATE_KHR;
                else if (std::find(modes.begin(), modes.end(), VK_PRESENT_MODE_MAILBOX_KHR) != modes.end()) mode = VK_PRESENT_MODE_MAILBOX_KHR;
                else if (std::find(modes.begin(), modes.end(), VK_PRESENT_MODE_IMMEDIATE_KHR) != modes.end()) mode = VK_PRESENT_MODE_IMMEDIATE_KHR;
                if (generated && mode != VK_PRESENT_MODE_IMMEDIATE_KHR) fg_denied = true;
                if (!generated) fg_denied = false;
                tearing_denied = tearing && mode != VK_PRESENT_MODE_IMMEDIATE_KHR; tearing_armed = tearing && mode == VK_PRESENT_MODE_IMMEDIATE_KHR;
            }
            uint32_t images = caps.minImageCount + 1; if (caps.maxImageCount) images = std::min(images, caps.maxImageCount);
            VkSwapchainCreateInfoKHR swap{ VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR }; swap.surface = surface; swap.minImageCount = images;
            swap.imageFormat = color_format; swap.imageColorSpace = chosen->colorSpace; swap.imageExtent = extent; swap.imageArrayLayers = 1;
            swap.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT; readback_supported = (caps.supportedUsageFlags & VK_IMAGE_USAGE_TRANSFER_SRC_BIT) != 0;
            swap_transfer = (caps.supportedUsageFlags & (VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT)) == (VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT);
            if (readback_supported) swap.imageUsage |= VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
            if (swap_transfer) swap.imageUsage |= VK_IMAGE_USAGE_TRANSFER_DST_BIT;
            swap.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE; swap.preTransform = caps.currentTransform;
            swap.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
            if (!(caps.supportedCompositeAlpha & swap.compositeAlpha)) {
                for (uint32_t alpha : { 1u, 2u, 4u, 8u }) if (caps.supportedCompositeAlpha & alpha) { swap.compositeAlpha = static_cast<VkCompositeAlphaFlagBitsKHR>(alpha); break; }
            }
            swap.presentMode = mode; swap.clipped = VK_TRUE;
#if defined(_WIN32)
            VkSurfaceFullScreenExclusiveWin32InfoEXT exclusive_win32{ VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_WIN32_INFO_EXT };
            VkSurfaceFullScreenExclusiveInfoEXT exclusive_info{ VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_INFO_EXT };
            bool exclusive_chain = false;
            exclusive_denied = false; exclusive_held = false;
            if (exclusive_requested && exclusive_extension && exclusive_monitor && vkGetPhysicalDeviceSurfaceCapabilities2KHR) {
                exclusive_win32.hmonitor = static_cast<HMONITOR>(exclusive_monitor);
                exclusive_info.pNext = &exclusive_win32;
                exclusive_info.fullScreenExclusive = VK_FULL_SCREEN_EXCLUSIVE_APPLICATION_CONTROLLED_EXT;
                VkPhysicalDeviceSurfaceInfo2KHR surface_info{ VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SURFACE_INFO_2_KHR, &exclusive_info };
                surface_info.surface = surface;
                VkSurfaceCapabilitiesFullScreenExclusiveEXT exclusive_caps{ VK_STRUCTURE_TYPE_SURFACE_CAPABILITIES_FULL_SCREEN_EXCLUSIVE_EXT };
                VkSurfaceCapabilities2KHR caps2{ VK_STRUCTURE_TYPE_SURFACE_CAPABILITIES_2_KHR, &exclusive_caps };
                if (vkGetPhysicalDeviceSurfaceCapabilities2KHR(physical, &surface_info, &caps2) == VK_SUCCESS && exclusive_caps.fullScreenExclusiveSupported) {
                    swap.pNext = &exclusive_info; exclusive_chain = true;
                }
            }
#endif
            check(vkCreateSwapchainKHR(device, &swap, nullptr, &swapchain), "create swapchain");
#if defined(_WIN32)
            exclusive_swapchain = exclusive_chain;
            if (exclusive_chain) try_acquire_exclusive(true);
#endif
            actual_present_mode=mode;++swapchain_generation;
            count = 0; check(vkGetSwapchainImagesKHR(device, swapchain, &count, nullptr), "query swap images"); swap_images.resize(count);
            check(vkGetSwapchainImagesKHR(device, swapchain, &count, swap_images.data()), "query swap images");
            for (VkImage image : swap_images) {
                VkImageViewCreateInfo view{ VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO }; view.image = image; view.viewType = VK_IMAGE_VIEW_TYPE_2D;
                view.format = color_format; view.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1 };
                VkImageView result{}; check(vkCreateImageView(device, &view, nullptr, &result), "create swap view"); swap_views.push_back(result);
                VkSemaphoreCreateInfo semaphore{ VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO }; VkSemaphore signal{};
                check(vkCreateSemaphore(device, &semaphore, nullptr, &signal), "create presentation semaphore"); present_semaphores.push_back(signal);
            }
            image_initialized.resize(count, false);
        } else {
            color_format = VK_FORMAT_R8G8B8A8_SRGB; extent = present_extent = { width, height }; readback_supported = true;
            offscreen = make_image(width, height, color_format, VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT, VK_IMAGE_ASPECT_COLOR_BIT);
        }
        depth = make_image(extent.width, extent.height, VK_FORMAT_D32_SFLOAT, VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT, VK_IMAGE_ASPECT_DEPTH_BIT);
        if (!sprite_pipeline || previous != color_format) {
            if (sprite_pipeline) vkDestroyPipeline(device, sprite_pipeline, nullptr);
            if (additive_pipeline) vkDestroyPipeline(device, additive_pipeline, nullptr);
            if (sprite_premul_pipeline) vkDestroyPipeline(device, sprite_premul_pipeline, nullptr);
            if (sprite_premul_additive_pipeline) vkDestroyPipeline(device, sprite_premul_additive_pipeline, nullptr);
            if (mesh_pipeline) vkDestroyPipeline(device, mesh_pipeline, nullptr);
            if (transparent_pipeline) vkDestroyPipeline(device, transparent_pipeline, nullptr);
            if (mesh_additive_pipeline) vkDestroyPipeline(device, mesh_additive_pipeline, nullptr);
            if (skybox_pipeline) vkDestroyPipeline(device,skybox_pipeline,nullptr);
            if (mesh_additive_top_pipeline) vkDestroyPipeline(device,mesh_additive_top_pipeline,nullptr);
            sprite_pipeline = additive_pipeline = mesh_pipeline = transparent_pipeline = mesh_additive_pipeline = skybox_pipeline = mesh_additive_top_pipeline = {};
            sprite_premul_pipeline=sprite_premul_additive_pipeline={};
            sprite_pipeline = make_pipeline(false, false); additive_pipeline = make_pipeline(false, true); mesh_pipeline = make_pipeline(true, false);
            sprite_premul_pipeline = make_pipeline(false,false,false,true,VK_FORMAT_UNDEFINED,false,false,false,true);
            sprite_premul_additive_pipeline = make_pipeline(false,true,false,true,VK_FORMAT_UNDEFINED,false,false,false,true);
            transparent_pipeline = make_pipeline(true, false, true); mesh_additive_pipeline = make_pipeline(true, true, true);
            skybox_pipeline=make_pipeline(true,false,false,true,VK_FORMAT_UNDEFINED,true);
            mesh_additive_top_pipeline=make_pipeline(true,true,true,true,VK_FORMAT_UNDEFINED,false,false,true);
            for (auto& line : line_pipelines) { if (line) vkDestroyPipeline(device, line, nullptr); line = {}; }
            line_pipelines[0] = make_pipeline(false,false,false,true,VK_FORMAT_UNDEFINED,false,false,false,true,true,true);
            line_pipelines[1] = make_pipeline(false,false,false,true,VK_FORMAT_UNDEFINED,false,false,true,true,true,true);
        }
        if (!lcd_pipeline) {
            // Stock offscreen sprites preserve destination alpha. In a world
            // ColorMetal texture that channel is metalness, not sprite opacity.
            lcd_pipeline = make_pipeline(false,false,false,false,VK_FORMAT_R8G8B8A8_SRGB,false,false,false,false,false); lcd_additive_pipeline = make_pipeline(false,true,false,false,VK_FORMAT_R8G8B8A8_SRGB,false,false,false,false,false);
            lcd_premul_pipeline=make_pipeline(false,false,false,false,VK_FORMAT_R8G8B8A8_SRGB,false,false,false,true,false);
            lcd_premul_additive_pipeline=make_pipeline(false,true,false,false,VK_FORMAT_R8G8B8A8_SRGB,false,false,false,true,false);
        }
        suspended = false; stats.width = extent.width; stats.height = extent.height; stats.surface_ready = 1;
    }
    void resize(uint32_t width, uint32_t height, bool sync) { resize_output(width,height,width,height,sync); }
    void texture_descriptors(Texture& texture) {
        VkDescriptorImageInfo image{ VK_NULL_HANDLE,texture.image.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL };
        std::array<VkWriteDescriptorSet,5> writes{};
        for (uint32_t i=0;i<5;++i) {
            writes[i].sType=VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET; writes[i].dstBinding=i; writes[i].descriptorCount=1;
            writes[i].descriptorType=i==1?VK_DESCRIPTOR_TYPE_SAMPLER:VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
            writes[i].pImageInfo=&image;
        }
        for(uint32_t variant=0;variant<2;++variant) {
            auto& descriptor=variant?texture.sprite_descriptor:texture.descriptor;
            descriptor=allocate_set(texture_layout);
            VkDescriptorImageInfo sample{variant?sprite_sampler:sampler,VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};
            for(auto& write:writes) write.dstSet=descriptor;
            writes[1].pImageInfo=&sample;
            vkUpdateDescriptorSets(device,static_cast<uint32_t>(writes.size()),writes.data(),0,nullptr);
        }
    }
    void upload_texture(uint64_t id, uint32_t width, uint32_t height, uint32_t format, bool srgb, const void* data, uint64_t bytes, bool internal = false, uint32_t mip_count = 1) {
        if (recording) throw std::runtime_error("resource upload must precede begin_frame");
        if ((!id && !internal) || !data || !width || !height || width > 16384 || height > 16384 || format > 9 || (format==9 && srgb)) throw std::runtime_error("invalid texture arguments");
        static const VkFormat linear[] = { VK_FORMAT_R8G8B8A8_UNORM,VK_FORMAT_B8G8R8A8_UNORM,VK_FORMAT_BC1_RGBA_UNORM_BLOCK,VK_FORMAT_BC2_UNORM_BLOCK,
            VK_FORMAT_BC3_UNORM_BLOCK,VK_FORMAT_BC4_UNORM_BLOCK,VK_FORMAT_BC5_UNORM_BLOCK,VK_FORMAT_BC6H_UFLOAT_BLOCK,VK_FORMAT_BC7_UNORM_BLOCK,VK_FORMAT_R16G16_SFLOAT };
        static const VkFormat gamma[] = { VK_FORMAT_R8G8B8A8_SRGB,VK_FORMAT_B8G8R8A8_SRGB,VK_FORMAT_BC1_RGBA_SRGB_BLOCK,VK_FORMAT_BC2_SRGB_BLOCK,
            VK_FORMAT_BC3_SRGB_BLOCK,VK_FORMAT_BC4_UNORM_BLOCK,VK_FORMAT_BC5_UNORM_BLOCK,VK_FORMAT_BC6H_UFLOAT_BLOCK,VK_FORMAT_BC7_SRGB_BLOCK,VK_FORMAT_R16G16_SFLOAT };
        uint32_t maximum_mips=1;
        for(uint32_t size=std::max(width,height);size>1;size/=2) ++maximum_mips;
        if(!mip_count || mip_count>maximum_mips) throw std::runtime_error("invalid texture mip count");
        uint64_t expected=0;uint32_t mip_width=width,mip_height=height;
        std::vector<VkBufferImageCopy> copies(mip_count);
        for(uint32_t mip=0;mip<mip_count;++mip) {
            auto& copy=copies[mip];copy.bufferOffset=expected;copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,mip,0,1};copy.imageExtent={mip_width,mip_height,1};
            expected+=(format<2 || format==9)?uint64_t(mip_width)*mip_height*4:uint64_t((mip_width+3)/4)*((mip_height+3)/4)*((format==2 || format==5)?8:16);
            mip_width=std::max(1u,mip_width/2);mip_height=std::max(1u,mip_height/2);
        }
        if (bytes != expected) throw std::runtime_error("Texture byte count does not match format/extent");
        VkFormat selected = srgb ? gamma[format] : linear[format]; VkFormatProperties properties{}; vkGetPhysicalDeviceFormatProperties(physical, selected, &properties);
        if (!(properties.optimalTilingFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT)) throw std::runtime_error("Texture format unsupported by device");
        Buffer staging = make_buffer(bytes, VK_BUFFER_USAGE_TRANSFER_SRC_BIT, true); Texture texture{};
        try {
            std::memcpy(staging.mapped, data, static_cast<size_t>(bytes)); check(vmaFlushAllocation(allocator, staging.memory, 0, bytes), "flush texture staging");
            texture.image = make_image(width, height, selected, VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT, VK_IMAGE_ASPECT_COLOR_BIT,mip_count);
            immediate([&](VkCommandBuffer cmd) {
                barrier(cmd, texture.image.handle, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                    VK_PIPELINE_STAGE_2_NONE, 0, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT,mip_count);
                vkCmdCopyBufferToImage(cmd, staging.handle, texture.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, mip_count, copies.data());
                barrier(cmd, texture.image.handle, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                    VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,mip_count);
            });
            texture_descriptors(texture);
            if (auto old = textures.find(id); old != textures.end()) { retired.push_back({ next_completion,old->second,{} }); old->second = texture; recycle_cached_texture(id); }
            else textures.emplace(id, texture);
            stats.uploaded_bytes += bytes; stats.textures = textures.size();
        } catch (...) { if(batch_command)wait(next_completion);destroy_buffer(staging); destroy_texture(texture); throw; }
        destroy_buffer(staging); collect();
    }
    void update_stream_texture(uint64_t id,uint32_t width,uint32_t height,uint32_t format,const void* rgba,uint64_t bytes) {
        if(recording || !id || !rgba || !width || !height || format>1 || width>16384 || height>16384 || bytes!=uint64_t(width)*height*4)
            throw std::runtime_error("invalid streaming texture update");
        auto found=textures.find(id);
        if(found==textures.end() || found->second.stream_width!=width || found->second.stream_height!=height || found->second.stream_format!=format){
            upload_texture(id,width,height,format,true,rgba,bytes);auto& texture=textures.at(id);texture.stream_width=width;texture.stream_height=height;texture.stream_format=format;return;
        }
        Buffer staging=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_SRC_BIT,true);
        try {
            std::memcpy(staging.mapped,rgba,size_t(bytes));check(vmaFlushAllocation(allocator,staging.memory,0,bytes),"flush media staging");
            auto image=found->second.image.handle;
            immediate([&](VkCommandBuffer cmd){
                // Upload and sprite draws use the same graphics queue. This includes
                // reads from older submitted frames and LCD/offscreen consumers.
                barrier(cmd,image,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
                    VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT);
                VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};copy.imageExtent={width,height,1};
                vkCmdCopyBufferToImage(cmd,staging.handle,image,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&copy);
                barrier(cmd,image,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
                    VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
            });stats.uploaded_bytes+=bytes;
        }catch(...){if(batch_command)wait(next_completion);destroy_buffer(staging);throw;}
        destroy_buffer(staging);
    }
    void upload_mesh(uint64_t id, const SevkVertex* vertices, uint32_t vertex_count, const uint32_t* indices, uint32_t index_count) {
        if (recording) throw std::runtime_error("resource upload must precede begin_frame");
        if (!id || !vertices || !indices || !vertex_count || !index_count || index_count % 3 || vertex_count > 16 * 1024 * 1024 || index_count > 64 * 1024 * 1024)
            throw std::runtime_error("invalid mesh arguments");
        for (uint32_t i = 0; i < index_count; ++i) if (indices[i] >= vertex_count) throw std::runtime_error("mesh index exceeds vertex count");
        Mesh mesh{}; Buffer staging{};
        try {
            const uint64_t vertex_bytes=uint64_t(vertex_count)*sizeof(SevkVertex), index_bytes=uint64_t(index_count)*sizeof(uint32_t);
            staging=make_buffer(vertex_bytes+index_bytes,VK_BUFFER_USAGE_TRANSFER_SRC_BIT,true);
            std::memcpy(staging.mapped,vertices,static_cast<size_t>(vertex_bytes));
            std::memcpy(static_cast<uint8_t*>(staging.mapped)+vertex_bytes,indices,static_cast<size_t>(index_bytes));
            check(vmaFlushAllocation(allocator,staging.memory,0,vertex_bytes+index_bytes),"flush mesh staging");
            mesh.vertices=make_buffer(vertex_bytes,ray_geometry_usage(VK_BUFFER_USAGE_VERTEX_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT),false);
            mesh.indices=make_buffer(index_bytes,ray_geometry_usage(VK_BUFFER_USAGE_INDEX_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT),false);
            immediate([&](VkCommandBuffer command) {
                const VkBufferCopy vertices_copy{0,0,vertex_bytes}, indices_copy{vertex_bytes,0,index_bytes};
                vkCmdCopyBuffer(command,staging.handle,mesh.vertices.handle,1,&vertices_copy);
                vkCmdCopyBuffer(command,staging.handle,mesh.indices.handle,1,&indices_copy);
                std::array<VkBufferMemoryBarrier2,2> barriers{};
                for(auto& barrier:barriers) {
                    barrier.sType=VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;
                    barrier.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;barrier.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;
                    barrier.dstStageMask=VK_PIPELINE_STAGE_2_VERTEX_INPUT_BIT;
                    barrier.srcQueueFamilyIndex=barrier.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;barrier.size=VK_WHOLE_SIZE;
                }
                barriers[0].buffer=mesh.vertices.handle;barriers[0].dstAccessMask=VK_ACCESS_2_VERTEX_ATTRIBUTE_READ_BIT;
                barriers[1].buffer=mesh.indices.handle;barriers[1].dstAccessMask=VK_ACCESS_2_INDEX_READ_BIT;
                VkDependencyInfo dependency{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};dependency.bufferMemoryBarrierCount=2;dependency.pBufferMemoryBarriers=barriers.data();
                vkCmdPipelineBarrier2(command,&dependency);
            });
            destroy_buffer(staging);stats.uploaded_bytes+=vertex_bytes+index_bytes;mesh.index_count=index_count;mesh.ray_stamp=++ray.geometry_stamp;
            if (auto old = meshes.find(id); old != meshes.end()) { retired.push_back({ next_completion,{},old->second }); old->second = mesh; }
            else meshes.emplace(id, mesh);
            stats.meshes = meshes.size();
        } catch (...) { destroy_buffer(staging);destroy_mesh(mesh); throw; }
        collect();
    }
    // Win32 currentExtent is the client size. A settings change can resize the HWND
    // after the swapchain was created, and present then keeps the old image in the
    // left of the window. Recreate to the live extent without stopping presentation.
    void match_surface_extent() {
        if (!surface || recording) return;
        VkSurfaceCapabilitiesKHR caps{};
        if (vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physical, surface, &caps) != VK_SUCCESS) return;
        if (caps.currentExtent.width == 0xFFFFFFFFu) return;
        if (!caps.currentExtent.width || !caps.currentExtent.height) return;
        if (swapchain && !suspended && caps.currentExtent.width == extent.width && caps.currentExtent.height == extent.height) return;
        uint32_t logical_w = sprite_w ? sprite_w : caps.currentExtent.width, logical_h = sprite_h ? sprite_h : caps.currentExtent.height;
        resize_output(caps.currentExtent.width, caps.currentExtent.height, logical_w, logical_h, vsync);
    }
    int begin_frame(const SevkFrameInfo& frame) {
        submitted_frame=frame;jitter_begin_frame();
        guest.depth_ready=0;++guest.catalog_epoch;
        join_present();
        match_surface_extent();
        if (recording) throw std::runtime_error("frame already begun");
        bool want_hdr=(display_flags&1)&&!hdr_denied&&!hdr_active;
        bool drop_hdr=!(display_flags&1)&&hdr_active;
        bool want_immediate=(((display_flags&2)&&!fg_denied)||((display_flags&32)&&!tearing_denied))&&!vsync&&actual_present_mode!=VK_PRESENT_MODE_IMMEDIATE_KHR;
        // Releasing tearing returns to the ordinary no-vsync mode (MAILBOX when available).
        want_immediate|=tearing_armed&&!(display_flags&(2|32))&&!vsync;if(!(display_flags&32))tearing_denied=false;
        // Recreate for HDR or immediate present without replacing the selected
        // resolution. Passing the window extent here made borderless render at
        // the desktop size while the game still laid the HUD out at the mode.
        if(swapchain&&extent.width&&extent.height&&(want_hdr||drop_hdr||want_immediate)){uint32_t logical_w=sprite_w?sprite_w:extent.width,logical_h=sprite_h?sprite_h:extent.height;resize_output(extent.width,extent.height,logical_w,logical_h,vsync);}
        if(surface_dirty){surface_dirty=false;uint32_t logical_w=sprite_w?sprite_w:extent.width,logical_h=sprite_h?sprite_h:extent.height;if(extent.width&&extent.height)resize_output(extent.width,extent.height,logical_w,logical_h,vsync);}
        end_uploads();
        if (suspended) return 1;
        performance=SevkPerformanceStats{sizeof(SevkPerformanceStats),SEVK_ABI_VERSION};
        performance.queue_us=world.current().queue_us;performance.logical_draws=performance.submitted_draws=uint32_t(world.current().draws.size()+(cells_emit_pending&&cells.scene.ready()?cells.scene.emitted().size():0));
        auto& slot = slots[slot_index];
        auto slot_stamp=std::chrono::steady_clock::now();
        auto slot_take=[&](){auto now=std::chrono::steady_clock::now();uint32_t elapsed=(uint32_t)std::chrono::duration_cast<std::chrono::microseconds>(now-slot_stamp).count();slot_stamp=now;return elapsed;};
        wait(slot.completion);
        // Display bit 64 (HdrRender LowLatencyMode): DX11 sets MaximumFrameLatency 1, so also drain the previous frame.
        if(display_flags&64)wait(slots[(slot_index+1)%slots.size()].completion);
        work.wait_us=slot_take(); collect();sample_light_tiles(slot_index);
        if (!slot.draw_descriptors.empty()) {
            check(vkFreeDescriptorSets(device, descriptors, static_cast<uint32_t>(slot.draw_descriptors.size()), slot.draw_descriptors.data()), "recycle draw descriptors");
            slot.draw_descriptors.clear();
            slot.materials.clear();
        }
        if (swapchain) {
            try_acquire_exclusive(false);
            VkResult result = vkAcquireNextImageKHR(device, swapchain, UINT64_MAX, slot.acquired, VK_NULL_HANDLE, &image_index);
            if (result == VK_ERROR_FULL_SCREEN_EXCLUSIVE_MODE_LOST_EXT) {
                exclusive_held = false; try_acquire_exclusive(false);
                if (exclusive_held) result = vkAcquireNextImageKHR(device, swapchain, UINT64_MAX, slot.acquired, VK_NULL_HANDLE, &image_index);
                else return 1;
            }
            work.acquire_us=slot_take();
            if (result == VK_ERROR_OUT_OF_DATE_KHR) {
                uint32_t logical_w=sprite_w?sprite_w:extent.width,logical_h=sprite_h?sprite_h:extent.height;
                if(extent.width&&extent.height)resize_output(extent.width,extent.height,logical_w,logical_h,vsync);
                if(suspended||!swapchain)return 1;
                result = vkAcquireNextImageKHR(device, swapchain, UINT64_MAX, slot.acquired, VK_NULL_HANDLE, &image_index);
                if(result==VK_ERROR_OUT_OF_DATE_KHR)return 1;
            }
            if (result != VK_SUCCESS && result != VK_SUBOPTIMAL_KHR) check(result, "acquire swap image");
        } else work.acquire_us=slot_take();
        check(vkResetCommandPool(device, slot.pool, 0), "reset frame commands"); work.reset_us=slot_take();
        NativeFrame constants{}; constants.frame = frame; constants.light_count = std::min(64u,light_count);
        // The preview shader has its own fixed ABI. Stock world lighting reads
        // the complete storage buffer built by world_light_data instead.
        std::memcpy(constants.lights, lights.data(), constants.light_count * sizeof(SevkLight));
        std::memcpy(constants.shadow_matrices,shadow_matrices,sizeof(constants.shadow_matrices)); std::memcpy(constants.shadow_splits,shadow_splits,sizeof(constants.shadow_splits)); constants.shadow_count=std::min(4u,shadow_count);
        std::memcpy(slot.uniform.mapped, &constants, sizeof(constants)); check(vmaFlushAllocation(allocator, slot.uniform.memory, 0, sizeof(constants)), "flush frame constants");
        VkCommandBufferBeginInfo begin{ VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO }; begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        check(vkBeginCommandBuffer(slot.command, &begin), "begin frame command"); frame_mark("frame begin");
        frame_meshes.clear(); frame_sprites.clear(); frame_lines[0].clear(); frame_lines[1].clear(); recording=true; guest.resources_open=true; return 0;
    }
    void begin_main_rendering(const SevkFrameInfo& frame,bool load=false) {
        auto& slot=slots[slot_index];
        VkExtent2D target = present_extent.width ? present_extent : extent;
        VkImage image = swapchain ? swap_images[image_index] : offscreen.handle;
        bool initialized = load || (swapchain ? image_initialized[image_index] : offscreen_initialized);
        barrier(slot.command, image, load ? VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL : initialized ? (swapchain ? VK_IMAGE_LAYOUT_PRESENT_SRC_KHR : VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL) : VK_IMAGE_LAYOUT_UNDEFINED,
            VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
            swapchain ? VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT : (initialized ? VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT : VK_PIPELINE_STAGE_2_NONE), initialized ? VK_ACCESS_2_MEMORY_WRITE_BIT : 0,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
        barrier(slot.command, depth.handle, (load||depth_initialized) ? VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL : VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,
            VK_IMAGE_ASPECT_DEPTH_BIT, (load||depth_initialized) ? VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT : VK_PIPELINE_STAGE_2_NONE,
            (load||depth_initialized) ? VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT|VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT : 0,
            VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT, VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT);
        VkRenderingAttachmentInfo color{ VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO }; color.imageView = swapchain ? swap_views[image_index] : offscreen.view;
        color.imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL; color.loadOp = load ? VK_ATTACHMENT_LOAD_OP_LOAD : VK_ATTACHMENT_LOAD_OP_CLEAR; color.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
        std::memcpy(color.clearValue.color.float32, frame.clear_color, 4 * sizeof(float));
        VkRenderingAttachmentInfo d{ VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO }; d.imageView = depth.view; d.imageLayout = VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;
        d.loadOp = load ? VK_ATTACHMENT_LOAD_OP_LOAD : VK_ATTACHMENT_LOAD_OP_CLEAR; d.storeOp = has_output_billboards() ? VK_ATTACHMENT_STORE_OP_STORE : VK_ATTACHMENT_STORE_OP_DONT_CARE; d.clearValue.depthStencil = { 0,0 };
        VkRenderingInfo rendering{ VK_STRUCTURE_TYPE_RENDERING_INFO }; rendering.renderArea = { {0,0},target }; rendering.layerCount = 1;
        rendering.colorAttachmentCount = 1; rendering.pColorAttachments = &color; rendering.pDepthAttachment = &d;
        vkCmdBeginRendering(slot.command, &rendering);
        vkCmdBindDescriptorSets(slot.command, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline_layout, 0, 1, &slot.descriptor, 0, nullptr);
        vkCmdBindDescriptorSets(slot.command,VK_PIPELINE_BIND_POINT_GRAPHICS,pipeline_layout,2,1,&shadow_descriptor,0,nullptr);
    }
    Texture& texture(uint64_t id) { auto i = textures.find(id); return i == textures.end() ? textures.at(0) : i->second; }
    void emit_mesh(const SevkDrawMesh& draw, int cascade=-1) {
        auto found = meshes.find(draw.mesh_id); if (found == meshes.end()) throw std::runtime_error("draw references missing mesh " + std::to_string(draw.mesh_id));
        auto& slot = slots[slot_index]; auto& mesh = found->second;
        MaterialKey key{{draw.texture_id,draw.normal_texture_id,draw.extensions_texture_id,draw.alpha_texture_id}};
        auto cached=slot.materials.find(key);VkDescriptorSet material{};
        if(cached!=slot.materials.end()) material=cached->second;
        else {
        material = allocate_set(texture_layout); slot.draw_descriptors.push_back(material); slot.materials.emplace(key,material);
        std::array<VkDescriptorImageInfo, 5> image_info{};
        image_info[0] = { VK_NULL_HANDLE,texture(draw.texture_id).image.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL };
        image_info[1] = { sampler,VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED };
        image_info[2] = { VK_NULL_HANDLE,texture(draw.normal_texture_id).image.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL };
        image_info[3] = { VK_NULL_HANDLE,texture(draw.extensions_texture_id).image.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL };
        image_info[4] = { VK_NULL_HANDLE,texture(draw.alpha_texture_id).image.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL };
        std::array<VkWriteDescriptorSet, 5> writes{};
        for (uint32_t i = 0; i < 5; ++i) {
            writes[i].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET; writes[i].dstSet = material; writes[i].dstBinding = i; writes[i].descriptorCount = 1;
            writes[i].descriptorType = i == 1 ? VK_DESCRIPTOR_TYPE_SAMPLER : VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE; writes[i].pImageInfo = &image_info[i];
        }
        vkUpdateDescriptorSets(device, static_cast<uint32_t>(writes.size()), writes.data(), 0, nullptr);
        }
        vkCmdBindPipeline(slot.command, VK_PIPELINE_BIND_POINT_GRAPHICS, cascade>=0?shadow_pipeline:(draw.flags&256?skybox_pipeline:(draw.flags&2048?mesh_additive_top_pipeline:(draw.flags & 8 ? mesh_additive_pipeline : (draw.flags & 4 ? transparent_pipeline : mesh_pipeline)))));
        VkExtent2D target=cascade>=0?VkExtent2D{shadow_resolution,shadow_resolution}:(present_extent.width?present_extent:extent);
        VkViewport viewport{ 0,float(target.height),float(target.width),-float(target.height),0,1 }; vkCmdSetViewport(slot.command, 0, 1, &viewport);
        VkRect2D scissor{ {0,0},target }; vkCmdSetScissor(slot.command, 0, 1, &scissor);
        vkCmdBindDescriptorSets(slot.command, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline_layout, 1, 1, &material, 0, nullptr);
        MeshPush push{}; std::memcpy(push.world, draw.world, sizeof(push.world)); std::memcpy(push.tint, draw.tint, sizeof(push.tint));
        std::memcpy(push.hsv, draw.color_mask_hsv, sizeof(push.hsv)); push.dither = draw.dither; push.emissivity = draw.emissivity; push.flags = draw.flags;
        if (draw.normal_texture_id) push.flags |= 32;
        if (draw.extensions_texture_id) push.flags |= 64;
        if (draw.alpha_texture_id) push.flags |= 128;
        push.pad=cascade<0?0:static_cast<uint32_t>(cascade);
        vkCmdPushConstants(slot.command, pipeline_layout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(push), &push);
        VkDeviceSize offset = 0; vkCmdBindVertexBuffers(slot.command, 0, 1, &mesh.vertices.handle, &offset); vkCmdBindIndexBuffer(slot.command, mesh.indices.handle, 0, VK_INDEX_TYPE_UINT32);
        vkCmdDrawIndexed(slot.command, mesh.index_count, 1, 0, 0, 0); if(cascade<0) ++stats.mesh_draws;
    }
    void draw_mesh(const SevkDrawMesh& draw) {
        if(!recording) throw std::runtime_error("mesh draw outside frame");
        if(!meshes.count(draw.mesh_id)) throw std::runtime_error("draw references missing mesh "+std::to_string(draw.mesh_id));
        frame_meshes.push_back(draw);
    }
    void emit_sprites(VkCommandBuffer command, VkExtent2D target, const SevkSprite* sprites, uint32_t count, VkPipeline standard, VkPipeline additive, VkPipeline premul, VkPipeline premul_additive, float logical_width = 0, float logical_height = 0, bool display_ui = false) {
        if (logical_width == 0) logical_width = float(target.width);
        if (logical_height == 0) logical_height = float(target.height);
        const double scale_x = double(target.width) / logical_width, scale_y = double(target.height) / logical_height;
        VkViewport viewport{ 0,0,float(target.width),float(target.height),0,1 }; vkCmdSetViewport(command, 0, 1, &viewport);
        for (uint32_t i = 0; i < count; ++i) {
            const auto& s = sprites[i]; if (s.destination[2] <= 0 || s.destination[3] <= 0) continue;
            VkRect2D scissor{ {0,0},target };
            if (s.clip[2] >= 0) {
                int64_t left = int64_t(std::clamp(double(s.clip[0]) * scale_x, 0.0, double(target.width)));
                int64_t top = int64_t(std::clamp(double(s.clip[1]) * scale_y, 0.0, double(target.height)));
                int64_t right = int64_t(std::clamp(double(int64_t(s.clip[0]) + s.clip[2]) * scale_x, double(left), double(target.width)));
                int64_t bottom = int64_t(std::clamp(double(int64_t(s.clip[1]) + s.clip[3]) * scale_y, double(top), double(target.height)));
                if (right <= left || bottom <= top) continue;
                scissor = { {int32_t(left),int32_t(top)},{uint32_t(right-left),uint32_t(bottom-top)} };
            }
            vkCmdSetScissor(command, 0, 1, &scissor);
            vkCmdBindPipeline(command, VK_PIPELINE_BIND_POINT_GRAPHICS, s.flags & 6 ? (s.flags & 1 ? premul_additive : premul) : (s.flags & 1 ? additive : standard));
            auto& image = texture(s.texture_id);
            vkCmdBindDescriptorSets(command, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline_layout, 1, 1, &image.sprite_descriptor, 0, nullptr);
            SpritePush push{}; std::memcpy(push.rect, s.destination, sizeof(push.rect)); std::memcpy(push.uv, s.uv, sizeof(push.uv)); std::memcpy(push.color, s.color, sizeof(push.color));
            push.screen[0] = logical_width; push.screen[1] = logical_height; push.angle = s.angle_radians; push.flags=s.flags; std::memcpy(push.origin, s.origin, sizeof(push.origin));
            if (display_ui && hdr_active && hdr_ui[0] > 0.f) { push.hdr_ui[0] = hdr_ui[0]; push.hdr_ui[1] = hdr_ui[1]; push.hdr_ui[2] = hdr_ui[2]; push.hdr_ui[3] = 1.f; }
            vkCmdPushConstants(command, pipeline_layout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(push), &push);
            vkCmdDraw(command, 6, 1, 0, 0); ++stats.sprite_draws;
        }
    }
    // Stock MyLinesRenderer.Draw after the game scene: premultiplied blend, scene depth for DepthRead batches.
    void draw_lines(const SevkLineVertex* vertices, uint32_t count, uint32_t flags) {
        if (!recording) throw std::runtime_error("line draw outside frame");
        if ((!vertices && count) || (count & 1) || flags > 1) throw std::runtime_error("invalid line batch");
        auto& target = frame_lines[(flags & 1) ? 0 : 1];
        if (target.size() + count > 200000) throw std::runtime_error("line batch exceeds the stock debug-draw capacity");
        if (count) target.insert(target.end(), vertices, vertices + count);
    }
    void emit_lines(VkCommandBuffer command) {
        size_t total = frame_lines[0].size() + frame_lines[1].size(); if (!total || !line_pipelines[0]) return;
        auto& buffer = slots[slot_index].lines; world_buffer(buffer, total * sizeof(SevkLineVertex), VK_BUFFER_USAGE_VERTEX_BUFFER_BIT);
        std::memcpy(buffer.mapped, frame_lines[0].data(), frame_lines[0].size() * sizeof(SevkLineVertex));
        std::memcpy(static_cast<uint8_t*>(buffer.mapped) + frame_lines[0].size() * sizeof(SevkLineVertex), frame_lines[1].data(), frame_lines[1].size() * sizeof(SevkLineVertex));
        check(vmaFlushAllocation(allocator, buffer.memory, 0, VK_WHOLE_SIZE), "flush debug lines");
        VkExtent2D target = present_extent.width ? present_extent : extent;
        VkViewport viewport{ 0,float(target.height),float(target.width),-float(target.height),0,1 }; vkCmdSetViewport(command, 0, 1, &viewport);
        VkRect2D scissor{ {0,0},target }; vkCmdSetScissor(command, 0, 1, &scissor);
        vkCmdBindDescriptorSets(command, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline_layout, 0, 1, &slots[slot_index].descriptor, 0, nullptr);
        VkDeviceSize offset = 0; vkCmdBindVertexBuffers(command, 0, 1, &buffer.handle, &offset);
        uint32_t first = 0;
        for (uint32_t i = 0; i < 2; ++i) { uint32_t count = uint32_t(frame_lines[i].size()); if (!count) continue;
            vkCmdBindPipeline(command, VK_PIPELINE_BIND_POINT_GRAPHICS, line_pipelines[i]); vkCmdDraw(command, count, 1, first, 0); first += count; debug_line_vertices += count; }
    }
    void draw_sprites(const SevkSprite* sprites, uint32_t count) {
        if (!recording) throw std::runtime_error("sprite draw outside frame");
        if(count) frame_sprites.insert(frame_sprites.end(),sprites,sprites+count);
    }
    void render_texture(uint64_t id, uint32_t width, uint32_t height, const float* clear, const SevkSprite* sprites, uint32_t count, float logical_width, float logical_height, bool generate_mips=false) {
        if (recording || !id || !width || !height || width > 16384 || height > 16384 || !clear || (!sprites && count) || count > 1000000)
            throw std::runtime_error("invalid offscreen render arguments");
        if (!std::isfinite(logical_width) || !std::isfinite(logical_height) || logical_width < 1 || logical_height < 1 || logical_width > 1000000 || logical_height > 1000000)
            throw std::runtime_error("invalid offscreen logical viewport");
        for (uint32_t i = 0; i < count; ++i) if (sprites[i].texture_id == id) throw std::runtime_error("offscreen feedback loop unsupported");
        Texture result{};
        try {
            uint32_t levels=generate_mips?std::bit_width(std::max(width,height)):1;
            auto usage=VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
            if(levels>1)usage|=VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
            result.image = make_image(width,height,VK_FORMAT_R8G8B8A8_SRGB,usage,VK_IMAGE_ASPECT_COLOR_BIT,levels);
            if(levels>1){
                VkImageViewCreateInfo view{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};view.image=result.image.handle;view.viewType=VK_IMAGE_VIEW_TYPE_2D;view.format=VK_FORMAT_R8G8B8A8_SRGB;view.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
                check(vkCreateImageView(device,&view,nullptr,&result.attachment),"create LCD base-mip attachment");
            }
            immediate([&](VkCommandBuffer command) {
                barrier(command,result.image.handle,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
                    VK_PIPELINE_STAGE_2_NONE,0,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
                VkRenderingAttachmentInfo attachment{ VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO }; attachment.imageView = result.attachment?result.attachment:result.image.view;
                attachment.imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL; attachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR; attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
                std::memcpy(attachment.clearValue.color.float32,clear,4*sizeof(float));
                VkRenderingInfo rendering{ VK_STRUCTURE_TYPE_RENDERING_INFO }; rendering.renderArea = {{0,0},{width,height}}; rendering.layerCount = 1;
                rendering.colorAttachmentCount = 1; rendering.pColorAttachments = &attachment;
                vkCmdBeginRendering(command,&rendering); emit_sprites(command,{width,height},sprites,count,lcd_pipeline,lcd_additive_pipeline,lcd_premul_pipeline,lcd_premul_additive_pipeline,logical_width,logical_height); vkCmdEndRendering(command);
                if(levels>1){
                    barrier(command,result.image.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
                    for(uint32_t m=1;m<levels;++m){
                        VkImageMemoryBarrier2 b{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2};b.srcStageMask=VK_PIPELINE_STAGE_2_NONE;b.dstStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;b.dstAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;b.oldLayout=VK_IMAGE_LAYOUT_UNDEFINED;b.newLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;b.image=result.image.handle;b.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,m,1,0,1};VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.imageMemoryBarrierCount=1;d.pImageMemoryBarriers=&b;vkCmdPipelineBarrier2(command,&d);
                        VkImageBlit blit{};blit.srcSubresource={VK_IMAGE_ASPECT_COLOR_BIT,m-1,0,1};blit.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,m,0,1};blit.srcOffsets[1]={int32_t(std::max(1u,width>>(m-1))),int32_t(std::max(1u,height>>(m-1))),1};blit.dstOffsets[1]={int32_t(std::max(1u,width>>m)),int32_t(std::max(1u,height>>m)),1};vkCmdBlitImage(command,result.image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,result.image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&blit,VK_FILTER_LINEAR);
                        b.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;b.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;b.dstAccessMask=VK_ACCESS_2_TRANSFER_READ_BIT;b.oldLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;b.newLayout=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;vkCmdPipelineBarrier2(command,&d);
                    }
                    barrier(command,result.image.handle,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT|VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,levels);
                }else barrier(command,result.image.handle,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
                    VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT);
            });
            texture_descriptors(result);
            // Material sets cache this image view. The replacement is retired with the
            // upload, which finishes before the next frame, so the old view must not be reused.
            if (auto old=textures.find(id);old!=textures.end()) { retired.push_back({next_completion,old->second,{}}); old->second=result; recycle_cached_texture(id); }
            else textures.emplace(id,result);
            stats.textures=textures.size(); collect();
        } catch (...) { if(batch_command)wait(next_completion);destroy_texture(result); throw; }
    }
    int end_frame() {
        critical_profile::Finish critical_finish{critical};
        critical_profile::Scope critical_record(critical_profile::RecordOwner,true);
        if (!recording) { if (suspended) return 1; throw std::runtime_error("end_frame without begin_frame"); }
        settle_cells();
        // Host guest callbacks run after BeginFrame and before EndFrame.
        // No guest resource has been referenced by this command buffer yet.
        // Close that allocation window before recording the world/guest draws.
        guest.resources_open=false;
        auto& slot = slots[slot_index];
        auto perf_stamp=std::chrono::steady_clock::now();
        auto perf_take=[&](){auto now=std::chrono::steady_clock::now();auto us=uint32_t(std::chrono::duration_cast<std::chrono::microseconds>(now-perf_stamp).count());perf_stamp=now;return us;};
        // Full GPU frame timing starts before resident preparation, not after
        // its dispatches/barriers; otherwise preparation stalls are invisible.
        if(world.current().queued&&timing_pool){vkCmdResetQueryPool(slot.command,timing_pool,slot_index*2,2);vkCmdWriteTimestamp2(slot.command,VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT,timing_pool,slot_index*2);}
        if(guest.has_global_clear)guest_replay(slot.command,0);
        if(opaque_stock.ready&&!world.current().queued)prepare_opaque_reference(slot.command);
        ngx_apply_scale();
        fsr_apply_scale();
        output_extent = present_extent.width ? present_extent : extent;
        display_state.upscale_ready = false;
        post.after_tonemap = nullptr; post.display_graded = false;
        // The world may render below the selected mode. The swapchain image stays
        // present_extent, and the following pass stretches the world across it.
        if(world.current().queued){auto saved_output=extent;uint32_t logical_w=sprite_w?sprite_w:output_extent.width,logical_h=sprite_h?sprite_h:output_extent.height;extent={std::max(1u,uint32_t(std::lround(logical_w*render_fraction))),std::max(1u,uint32_t(std::lround(logical_h*render_fraction)))};
            resolution_stats.width=extent.width;resolution_stats.height=extent.height;resolution_stats.output_width=output_extent.width;resolution_stats.output_height=output_extent.height;resolution_stats.fraction=render_fraction;
            try{render_world();}catch(...){extent=saved_output;throw;}extent=saved_output;
        }else {frame_mark("menu shadows");render_shadows();}
        guest.depth_configured=false;
        if(world.current().queued)prepare_display(slot.command);
        if(!opaque_stock.ready)prepare_opaque_reference(slot.command);
        record_opaque_reference_depths(slot.command);
        extent = output_extent;
        // Guest passes own dynamic-rendering scopes. AfterUpscale must finish
        // before the swapchain scope used by composition and sprites begins.
        if(world.current().queued)guest_replay(slot.command,6);
        perf_take();frame_mark("swapchain pass"); begin_main_rendering(*static_cast<SevkFrameInfo*>(slot.uniform.mapped));
        if(world.current().queued) compose_world();
        record_opaque_reference(slot.command);
        frame_mark("meshes");for(const auto& draw:frame_meshes) emit_mesh(draw);
        frame_mark("debug lines");emit_lines(slot.command);
        if(world.current().queued && has_output_billboards()){prepare_output_billboard_depth(slot.command);begin_main_rendering(*static_cast<SevkFrameInfo*>(slot.uniform.mapped),true);render_output_billboards(slot.command,world.slots[slot_index]);}
        performance.compose_us=perf_take();frame_mark("sprites");emit_sprites(slot.command,output_extent,frame_sprites.data(),static_cast<uint32_t>(frame_sprites.size()),sprite_pipeline,additive_pipeline,sprite_premul_pipeline,sprite_premul_additive_pipeline,sprite_w?float(sprite_w):float(output_extent.width),sprite_h?float(sprite_h):float(output_extent.height),true);
        performance.sprite_us=perf_take();
        vkCmdEndRendering(slot.command);
        if(capture_requested) {
            if(!readback_supported) throw std::runtime_error("surface does not support transfer-source capture");
            wait(capture_completion);
            if(!capture.handle) capture=make_buffer(uint64_t(extent.width)*extent.height*(color_format==VK_FORMAT_R16G16B16A16_SFLOAT?8:4),VK_BUFFER_USAGE_TRANSFER_DST_BIT,true);
            VkImage image=swapchain?swap_images[image_index]:offscreen.handle;
            barrier(slot.command,image,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT);
            VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};copy.imageExtent={extent.width,extent.height,1};
            vkCmdCopyImageToBuffer(slot.command,image,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,capture.handle,1,&copy);
            barrier(slot.command,image,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,
                VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
            VkBufferMemoryBarrier2 host{VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2};host.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;host.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;
            host.dstStageMask=VK_PIPELINE_STAGE_2_HOST_BIT;host.dstAccessMask=VK_ACCESS_2_HOST_READ_BIT;host.buffer=capture.handle;host.size=VK_WHOLE_SIZE;
            host.srcQueueFamilyIndex=host.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;
            VkDependencyInfo dependency{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};dependency.bufferMemoryBarrierCount=1;dependency.pBufferMemoryBarriers=&host;
            vkCmdPipelineBarrier2(slot.command,&dependency);
        }
        if (swapchain) barrier(slot.command, swap_images[image_index], VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_LAYOUT_PRESENT_SRC_KHR, VK_IMAGE_ASPECT_COLOR_BIT,
            VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT, VK_PIPELINE_STAGE_2_NONE, 0);
        if(world.current().queued&&timing_pool){vkCmdWriteTimestamp2(slot.command,VK_PIPELINE_STAGE_2_BOTTOM_OF_PIPE_BIT,timing_pool,slot_index*2+1);timing_valid[slot_index]=true;}
        frame_mark("frame end");if(profile_label_open){vkCmdEndDebugUtilsLabelEXT(slot.command);profile_label_open=false;}
        check(vkEndCommandBuffer(slot.command), "end frame command"); recording = false;
        guest.passes.clear();guest.has_early_owned=guest.has_global_clear=false;tenant_end_frame(); // Frame packets never replay implicitly in a later frame.
        performance.finish_us=perf_take();
        VkCommandBufferSubmitInfo command{ VK_STRUCTURE_TYPE_COMMAND_BUFFER_SUBMIT_INFO }; command.commandBuffer = slot.command;
        VkSemaphoreSubmitInfo acquired{ VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO }; acquired.semaphore = slot.acquired; acquired.stageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
        std::array<VkSemaphoreSubmitInfo, 2> signals{};
        for (auto& signal : signals) signal.sType = VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
        slot.completion = ++next_completion; signals[0].semaphore = timeline; signals[0].value = slot.completion; signals[0].stageMask = VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
        // Separate submits are not ordered. The frame samples images whose layout
        // transition is in the upload batch, so it waits on that timeline value.
        std::array<VkSemaphoreSubmitInfo,2> waits{}; uint32_t wait_count=0;
        if(swapchain)waits[wait_count++]=acquired;
        uint64_t upload_wait=0;if(upload_fence && upload_fence<slot.completion){upload_wait=upload_fence;waits[wait_count].sType=VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;waits[wait_count].semaphore=timeline;waits[wait_count].value=upload_wait;waits[wait_count].stageMask=VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;++wait_count;}
        VkSubmitInfo2 submit{ VK_STRUCTURE_TYPE_SUBMIT_INFO_2 }; submit.commandBufferInfoCount = 1; submit.pCommandBufferInfos = &command;
        submit.signalSemaphoreInfoCount = 1; submit.pSignalSemaphoreInfos = signals.data();
        if(wait_count){submit.waitSemaphoreInfoCount=wait_count;submit.pWaitSemaphoreInfos=waits.data();}
        if (swapchain) { signals[1].semaphore = present_semaphores[image_index];
            signals[1].stageMask = VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT; submit.signalSemaphoreInfoCount = 2; }
        join_present();VkResult result = vkQueueSubmit2(queue, 1, &submit, VK_NULL_HANDLE);
        performance.submit_us=perf_take();
        check_gpu(result, "submit rendered frame"); if(upload_wait)upload_fence=0; ++stats.submitted_frames;critical_finish.submitted=true;
        if(capture_requested) {capture_completion=slot.completion;capture_valid=true;capture_requested=false;}
        depth_initialized = true; last_image_valid = true; last_image_index = image_index;
        if (swapchain) {
            image_initialized[image_index] = true;
            // One immutable task owns the queue until joined before any other
            // queue operation. Per-image semaphore reuse still follows acquire.
            auto submit_present=[present_queue=queue,chain=swapchain,index=image_index,semaphore=present_semaphores[image_index]](){
                VkPresentInfoKHR present{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};present.waitSemaphoreCount=1;present.pWaitSemaphores=&semaphore;present.swapchainCount=1;present.pSwapchains=&chain;present.pImageIndices=&index;
                uint64_t begin=present_clock();VkResult status=vkQueuePresentKHR(present_queue,&present);return PresentResult{status,begin,present_clock()};
            };
            if(async_present)pending_present=std::async(std::launch::async,submit_present);else accept_present(submit_present());
        } else offscreen_initialized = true;
        performance.present_us=perf_take();
        present_generated_frame();
        fg_note_game();
        fg_publish();
        slot_index = (slot_index + 1) % slots.size(); return suspended ? 1 : 0;
    }
    void remove(uint32_t kind, uint64_t id) {
        if (recording) throw std::runtime_error("resource removal must precede begin_frame");
        if (!id || kind > 7) throw std::runtime_error("invalid resource kind/id");
        invalidate_world_recipes(kind,id,kind==3);
        if (kind == 0) { auto it = textures.find(id); if (it != textures.end()) { retired.push_back({ next_completion,it->second,{} }); textures.erase(it); recycle_cached_texture(id); } }
        else if(kind == 1) { auto it = meshes.find(id); if (it != meshes.end()) { retired.push_back({ next_completion,{},it->second }); meshes.erase(it); } }
        else if(kind==2) { auto it = stock.meshes.find(id); if (it != stock.meshes.end()) { retired.push_back({ next_completion,{},it->second }); stock.meshes.erase(it); } }
        else if(kind==3) { auto it=world.meshes.find(id);if(it!=world.meshes.end()){retired.push_back({next_completion,{},it->second});world.meshes.erase(it);} }
        else if(kind==5){auto it=world.arrays.find(id);if(it!=world.arrays.end()){Texture t{};t.image=it->second;retired.push_back({next_completion,t,{}});world.arrays.erase(it);recycle_cached_array(id);}}
        else if(kind==7){auto it=environment.luts.find(id);if(it!=environment.luts.end()){Texture t{};t.image=it->second.image;retired.push_back({next_completion,t,{}});environment.luts.erase(it);}}
        else if(kind==6){auto it=world.voxel_materials.find(id);if(it!=world.voxel_materials.end()){Mesh m{};m.vertices=it->second.constants;retired.push_back({next_completion,{},m});world.voxel_materials.erase(it);recycle_cached_voxel(id);}}
        else {auto it=world.cubes.find(id);if(it!=world.cubes.end()){Texture t{};t.image=it->second;retired.push_back({next_completion,t,{}});world.cubes.erase(it);if(world.skybox==id)world.skybox=0;if(world.probe_skybox==id)world.probe_skybox=0;}}
        stats.textures = textures.size(); stats.meshes = meshes.size(); collect();
    }
    void readback(void* data, uint64_t bytes) {
        if (recording || !last_image_valid || !readback_supported || !data || bytes != uint64_t(extent.width) * extent.height * 4)
            throw std::runtime_error("readback requires a completed frame and exact RGBA8 buffer for "+std::to_string(extent.width)+"x"+std::to_string(extent.height));
        if(capture_valid) {
            wait(capture_completion);check(vmaInvalidateAllocation(allocator,capture.memory,0,capture.bytes),"invalidate presentation capture");
            if(color_format==VK_FORMAT_R16G16B16A16_SFLOAT)sevk::capture_scrgb_rgba8(static_cast<const uint16_t*>(capture.mapped),static_cast<uint8_t*>(data),bytes/4);
            else std::memcpy(data,capture.mapped,static_cast<size_t>(bytes));
            if(color_format==VK_FORMAT_B8G8R8A8_UNORM || color_format==VK_FORMAT_B8G8R8A8_SRGB) {auto* pixels=static_cast<uint8_t*>(data);for(uint64_t i=0;i<bytes;i+=4) std::swap(pixels[i],pixels[i+2]);}
            capture_valid=false;return;
        }
        // Presentation owns acquired swapchain images after end_frame. Readback is
        // deliberately restricted to the offscreen path until capture-before-present exists.
        if (swapchain) throw std::runtime_error("swapchain capture pending; offscreen readback available");
        wait(next_completion);
        Buffer staging = make_buffer(bytes, VK_BUFFER_USAGE_TRANSFER_DST_BIT, true);
        try {
            immediate([&](VkCommandBuffer cmd) {
                barrier(cmd, offscreen.handle, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                    VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT);
                VkBufferImageCopy copy{}; copy.imageSubresource = { VK_IMAGE_ASPECT_COLOR_BIT,0,0,1 }; copy.imageExtent = { extent.width,extent.height,1 };
                vkCmdCopyImageToBuffer(cmd, offscreen.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, staging.handle, 1, &copy);
                barrier(cmd, offscreen.handle, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT,
                    VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT, VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT);
            });
            check(vmaInvalidateAllocation(allocator, staging.memory, 0, bytes), "invalidate readback"); std::memcpy(data, staging.mapped, static_cast<size_t>(bytes));
        } catch (...) { destroy_buffer(staging); throw; }
        destroy_buffer(staging);
    }
    #include "stock_gbuffer.inc"
    #include "world_pipeline.inc"
    #include "guest.inc"
    #include "tenant.inc"
    #include "stage_bindings.inc"
    #include "display.inc"
    #include "jitter.inc"
    #include "display_info.inc"
    #include "ngx.inc"
    #include "fsr.inc"
    #include "voxel_resources.inc"
    #include "environment.inc"
    #include "flares.inc"
#include "decals.inc"
#include "particles.inc"
#include "transparency.inc"
#include "native_pass_render.inc"
#include "object_motion.inc"
#include "gbuffer_attach.inc"
#include "indexed_attach.inc"
#include "indexed_motion.inc"
#include "world_render.inc"
#include "world_recipes.inc"
#include "old_cells.inc"
#include "world_models.inc"
#include "light_tiles.inc"
#include "lighting_generations.inc"
#include "environment_generations.inc"
#include "foliage_generations.inc"
#include "decal_generations.inc"
#include "transparent_generations.inc"
#include "billboard_generations.inc"
#include "probe_generations.inc"
#include "particle_generations.inc"
#include "shadow_generations.inc"
#include "oit_generations.inc"
#include "geometry_generations.inc"
#include "extended_geometry_generations.inc"
#include "gbuffer_velocity.inc"
#include "gpu_owner_production.inc"
#include "postprocess.inc"
#include "hbao.inc"
#include "probes.inc"
#include "stock_shadows.inc"
#include "shadow_usage.inc"
#include "shadow_readback.inc"
#include "highlights.inc"
#include "spots.inc"
#include "occlusion.inc"
#include "ray.inc"
#include "gpu_opaque_reference.inc"
#include "gpu_opaque_stock.inc"
#include "gpu_opaque_resident.inc"
    void cleanup() noexcept {
        critical.detach();
        try{join_present(false);}catch(...){}
        try{end_uploads();}catch(...){}
        if (device) {
            vkDeviceWaitIdle(device);
            cleanup_ngx();
            cleanup_fsr();
            for(auto& item:retired){destroy_texture(item.texture);destroy_mesh(item.mesh);}retired.clear();
            for(auto& item:retired_views)vkDestroyImageView(device,item.second,nullptr);retired_views.clear();
            if(timing_pool)vkDestroyQueryPool(device,timing_pool,nullptr);
            cleanup_stock();
            cleanup_occlusion();cleanup_spot_copy();for(auto& image:spots.images)destroy_image(image);for(auto& image:spots.frozen)destroy_image(image);cleanup_highlights();cleanup_jitter();cleanup_shadow_usage();cleanup_stock_shadows();cleanup_probes();cleanup_hbao();cleanup_postprocess();cleanup_flares();cleanup_display();cleanup_decals();cleanup_particles();cleanup_transparency();cleanup_environment();
            cleanup_guest();
            cleanup_tenant();
            cleanup_ray();
            cleanup_opaque_reference();
            cleanup_opaque_stock();
            cleanup_world();
            cleanup_uploads();
            if (pipeline_cache && !cache_directory.empty() && !device_lost) {
                try {
                    size_t size = 0; if (vkGetPipelineCacheData(device, pipeline_cache, &size, nullptr) == VK_SUCCESS && size < 64 * 1024 * 1024) {
                        std::vector<char> bytes(size); if (vkGetPipelineCacheData(device, pipeline_cache, &size, bytes.data()) == VK_SUCCESS) {
                            std::filesystem::create_directories(cache_directory);
                            auto path = std::filesystem::path(cache_directory) / "pipeline-v1.cache", temp = path; temp += ".tmp";
                            { std::ofstream stream(temp, std::ios::binary); stream.write(bytes.data(), size); }
                            std::error_code ignored; std::filesystem::remove(path, ignored); std::filesystem::rename(temp, path, ignored);
                        }
                    }
                } catch (...) {}
            }
            for (auto& item : textures) destroy_texture(item.second);
            for (auto& item : meshes) destroy_mesh(item.second);
            for (auto& item : retired) { destroy_texture(item.texture); destroy_mesh(item.mesh); }
            if (allocator) destroy_targets();
            for (auto& line : line_pipelines) if (line) vkDestroyPipeline(device, line, nullptr);
            for (auto& slot : slots) { if (allocator) { destroy_buffer(slot.uniform); destroy_buffer(slot.lines); } if (slot.acquired) vkDestroySemaphore(device, slot.acquired, nullptr); if (slot.pool) vkDestroyCommandPool(device, slot.pool, nullptr); }
            if (sprite_pipeline) vkDestroyPipeline(device, sprite_pipeline, nullptr);
            if (additive_pipeline) vkDestroyPipeline(device, additive_pipeline, nullptr);
            if (sprite_premul_pipeline) vkDestroyPipeline(device, sprite_premul_pipeline, nullptr);
            if (sprite_premul_additive_pipeline) vkDestroyPipeline(device, sprite_premul_additive_pipeline, nullptr);
            if (mesh_pipeline) vkDestroyPipeline(device, mesh_pipeline, nullptr);
            if (transparent_pipeline) vkDestroyPipeline(device, transparent_pipeline, nullptr);
            if (mesh_additive_pipeline) vkDestroyPipeline(device, mesh_additive_pipeline, nullptr);
            if (lcd_pipeline) vkDestroyPipeline(device, lcd_pipeline, nullptr);
            if (lcd_additive_pipeline) vkDestroyPipeline(device, lcd_additive_pipeline, nullptr);
            if (lcd_premul_pipeline) vkDestroyPipeline(device, lcd_premul_pipeline, nullptr);
            if (lcd_premul_additive_pipeline) vkDestroyPipeline(device, lcd_premul_additive_pipeline, nullptr);
            if (shadow_pipeline) vkDestroyPipeline(device,shadow_pipeline,nullptr);
            if (skybox_pipeline) vkDestroyPipeline(device,skybox_pipeline,nullptr);
            if (mesh_additive_top_pipeline) vkDestroyPipeline(device,mesh_additive_top_pipeline,nullptr);
            if (allocator) for(auto& image:shadow_images) destroy_image(image);
            if (shadow_sampler) vkDestroySampler(device,shadow_sampler,nullptr);
            if (pipeline_layout) vkDestroyPipelineLayout(device, pipeline_layout, nullptr);
            if (pipeline_cache) vkDestroyPipelineCache(device, pipeline_cache, nullptr);
            if (sampler) vkDestroySampler(device, sampler, nullptr);
            if (sprite_sampler) vkDestroySampler(device, sprite_sampler, nullptr);
            if (descriptors) vkDestroyDescriptorPool(device, descriptors, nullptr);
            if (frame_layout) vkDestroyDescriptorSetLayout(device, frame_layout, nullptr);
            if (texture_layout) vkDestroyDescriptorSetLayout(device, texture_layout, nullptr);
            if (shadow_layout) vkDestroyDescriptorSetLayout(device,shadow_layout,nullptr);
            if (upload_pool) vkDestroyCommandPool(device, upload_pool, nullptr);
            if (timeline) vkDestroySemaphore(device, timeline, nullptr);
            if (allocator) vmaDestroyAllocator(allocator);
            vkDestroyDevice(device, nullptr);
        }
        if (surface) vkDestroySurfaceKHR(instance, surface, nullptr);
        if (debug) vkDestroyDebugUtilsMessengerEXT(instance, debug, nullptr);
        if (instance) vkDestroyInstance(instance, nullptr);
    }
};
template<class Fn> int32_t call(void* context, Fn&& fn) {
    if (!context) { global_error = "null Vulkan context"; return -1; }
    auto* renderer = static_cast<Renderer*>(context);
    // Guest publications can arrive on the game thread while the render thread
    // records/submits. Timeline allocation, pools and queue access share one owner.
    // A pipelined end_frame completes, and deferred calls replay, before any call.
    std::unique_lock lock(renderer->api_mutex);
    try { if(renderer->record_pending||renderer->record_running){auto start=std::chrono::steady_clock::now();renderer->await_recording(lock);uint64_t us=uint64_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-start).count());renderer->pipelined_wait_us+=us;++renderer->pipelined_waits;
#if defined(_WIN32)
        if(renderer->pipeline_census){void* frames[3]{};CaptureStackBackTrace(0,3,frames,nullptr);for(void* frame:frames){auto& site=renderer->pipelined_wait_sites[frame];++site.first;site.second+=us;}}
#endif
        } renderer->await_recording(lock); renderer->flush_deferred(); return fn(*renderer); }
    catch (const std::exception& e) { renderer->error = e.what(); global_error = renderer->error; return -1; }
    catch (...) { renderer->error = "unknown native Vulkan error"; global_error = renderer->error; return -1; }
}
}

// Void calls whose arguments are copied: queued in issue order while a
// pipelined end_frame records, otherwise executed as ordinary calls.
template<class Fn> int32_t call_deferrable(void* context, Fn fn) {
    if (!context) { global_error = "null Vulkan context"; return -1; }
    auto* renderer = static_cast<Renderer*>(context);
    {std::lock_guard queue(renderer->deferred_mutex);if(renderer->record_in_flight){renderer->deferred.emplace_back(std::move(fn));return 0;}}
    return call(context,fn);
}
// Reads answered from the launch snapshot while recording; a snapshot
// function returning nullopt falls back to the ordinary (waiting) call.
template<class Snapshot,class Fn> int32_t call_snapshot(void* context,Snapshot&& snapshot,Fn&& fn){
    if (!context) { global_error = "null Vulkan context"; return -1; }
    auto* renderer = static_cast<Renderer*>(context);
    {std::lock_guard queue(renderer->deferred_mutex);if(renderer->record_in_flight){std::lock_guard read(renderer->snapshot_mutex);std::optional<int32_t> result=snapshot(*renderer);if(result)return *result;}}
    return call(context,fn);
}
template<class T> struct Copied {std::vector<T> values;bool null=true;const T* get()const{return null?nullptr:values.data();}};
template<class T> Copied<T> copy_in(const T* data,uint64_t count){Copied<T> copy;copy.null=!data;if(data&&count)copy.values.assign(data,data+count);return copy;}
inline Copied<uint8_t> copy_bytes(const void* data,uint64_t bytes){return copy_in(static_cast<const uint8_t*>(data),bytes);}

uint32_t SEVK_CALL sevk_api_version() { return SEVK_ABI_VERSION; }
int32_t SEVK_CALL sevk_probe(SevkProbe* probe) {
    if (!probe || probe->size != sizeof(SevkProbe)) { global_error = "probe ABI size mismatch"; return -1; }
    VkInstance instance{};
    try {
        std::memset(probe, 0, sizeof(*probe)); probe->size = sizeof(*probe); probe->abi_version = SEVK_ABI_VERSION;
        load_loader(); check(vkEnumerateInstanceVersion(&probe->loader_api), "query loader API");
        probe->validation_available = layer_available("VK_LAYER_KHRONOS_validation");
        instance = create_instance({}, false, nullptr, nullptr);
        check(vkEnumeratePhysicalDevices(instance, &probe->device_count, nullptr), "enumerate probe GPUs");
        std::vector<VkPhysicalDevice> devices(probe->device_count); uint32_t count = probe->device_count;
        check(vkEnumeratePhysicalDevices(instance, &count, devices.data()), "enumerate probe GPUs");
        for (auto device : devices) {
            VkPhysicalDeviceProperties properties{}; vkGetPhysicalDeviceProperties(device, &properties); uint32_t flags{};
            bool supported = properties.apiVersion >= minimum_api && features_supported(device, flags);
            if (supported) ++probe->compatible_device_count;
            if (!probe->device_api || (supported && probe->compatible_device_count == 1)) { probe->device_api = properties.apiVersion; probe->features = flags; copy_text(probe->device_name, properties.deviceName); }
        }
        vkDestroyInstance(instance, nullptr); instance = {};
        copy_text(probe->detail, probe->compatible_device_count ? "Vulkan 1.4 required features available; surface compatibility checked during create" : "No compatible Vulkan 1.4 device");
        return probe->compatible_device_count ? 0 : -2;
    } catch (const std::exception& e) { if (instance) vkDestroyInstance(instance, nullptr); global_error = e.what(); copy_text(probe->detail, global_error); return -1; }
}
void* active_context;
int32_t SEVK_CALL sevk_create(const SevkCreateInfo* info, void** context) {
    if (!context) { global_error = "null output context"; return -1; } *context = nullptr;
    if (!info || info->size != sizeof(SevkCreateInfo) || info->abi_version != SEVK_ABI_VERSION) { global_error = "create ABI version/size mismatch"; return -1; }
    auto* renderer = new Renderer();
    try { renderer->init(*info); *context = renderer; active_context = renderer; return 0; }
    catch (const std::exception& e) { global_error = e.what(); delete renderer; return -1; }
    catch (...) { global_error = "unknown error creating Vulkan context"; delete renderer; return -1; }
}
int32_t SEVK_CALL sevk_set_texture_anisotropy(void* context, uint32_t samples) { return call(context, [&](Renderer& r) { r.set_anisotropy(samples); return 0; }); }
int32_t SEVK_CALL sevk_resize(void* context, uint32_t width, uint32_t height, uint32_t vsync) { return call(context, [&](Renderer& r) { r.resize(width,height,vsync != 0); return 0; }); }
int32_t SEVK_CALL sevk_resize_output(void* context,uint32_t width,uint32_t height,uint32_t logical_width,uint32_t logical_height,uint32_t vsync) { return call(context,[&](Renderer& r) { r.resize_output(width,height,logical_width,logical_height,vsync!=0);return 0; }); }
int32_t SEVK_CALL sevk_set_exclusive_fullscreen(void* context, uint32_t enabled, void* monitor) { return call(context, [&](Renderer& r) {
    r.exclusive_requested = enabled != 0 && monitor != nullptr; r.exclusive_monitor = r.exclusive_requested ? monitor : nullptr; r.exclusive_denied = false; return 0; }); }
int32_t SEVK_CALL sevk_get_exclusive_fullscreen(void* context, uint32_t* acquired) { return call(context, [&](Renderer& r) { if (!acquired) throw std::runtime_error("exclusive fullscreen output is required"); *acquired = r.exclusive_held ? 1u : 0u; return 0; }); }
int32_t SEVK_CALL sevk_get_gpu_opaque_capabilities(void* context,SevkGpuOpaqueCapabilities* caps){
    return call(context,[&](Renderer& r){if(!caps||caps->size!=sizeof(*caps)||caps->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("GPU opaque caps ABI mismatch");*caps=r.opaque_caps;return 0;});
}
int32_t SEVK_CALL sevk_opaque_reference_init(void* context,const SevkOpaqueReferenceConfig* config){return call(context,[&](Renderer& r){if(!config)throw std::runtime_error("null opaque reference configuration");r.init_opaque_reference(*config);return 0;});}
int32_t SEVK_CALL sevk_opaque_reference_actor(void* context,uint32_t index,const SevkOpaqueActor* actor){return call(context,[&](Renderer& r){if(!actor)throw std::runtime_error("null opaque reference actor");r.update_opaque_reference_actor(index,*actor);return 0;});}
int32_t SEVK_CALL sevk_opaque_reference_actors(void* context,const uint32_t* indices,const SevkOpaqueActor* actors,uint32_t count,const void* objects,uint32_t bytes){return call(context,[&](Renderer& r){r.update_opaque_reference_actors(indices,actors,count,objects,bytes);return 0;});}
int32_t SEVK_CALL sevk_opaque_reference_appearances(void* context,const SevkOpaqueAppearanceUpdate* updates,uint32_t count,const SevkOpaqueAppearance* entries,uint32_t total){return call(context,[&](Renderer& r){r.update_opaque_reference_appearances(updates,count,entries,total);return 0;});}
int32_t SEVK_CALL sevk_opaque_reference_frame(void* context,const SevkOpaqueFrame* frame,const SevkOpaqueView* views,uint32_t count){return call(context,[&](Renderer& r){if(!frame)throw std::runtime_error("null opaque reference frame");r.queue_opaque_reference_frame(*frame,views,count);return 0;});}
int32_t SEVK_CALL sevk_opaque_reference_depth(void* context,uint32_t view,float* pixels,uint32_t count,double* camera,float* projection){return call(context,[&](Renderer& r){r.readback_opaque_reference_depth(view,pixels,count,camera,projection);return 0;});}
int32_t SEVK_CALL sevk_opaque_stock_pipeline(void* context,uint32_t id,uint32_t old,const void* vs,uint64_t vb,const void* ps,uint64_t pb){return call(context,[&](Renderer& r){r.init_opaque_stock_pipeline(id,old,vs,vb,ps,pb);return 0;});}
#include "gpu_opaque_resident_exports.inc"
int32_t SEVK_CALL sevk_opaque_stock_inputs(void* context,uint64_t mesh,const void* objects,uint32_t bytes,uint32_t old,uint32_t main,uint32_t depth){return call(context,[&](Renderer& r){r.set_opaque_stock_inputs(mesh,objects,bytes,old,main,depth);return 0;});}
int32_t SEVK_CALL sevk_opaque_stock_batches(void* context,const SevkOpaqueStockBatch* batches,uint32_t count,const uint64_t* meshes,uint32_t parts,const void* objects,uint32_t bytes){return call(context,[&](Renderer& r){r.set_opaque_stock_batches(batches,count,meshes,parts,objects,bytes);return 0;});}
int32_t SEVK_CALL sevk_opaque_reference_readback(void* context,SevkOpaqueSelector* selectors,uint32_t actor_count,SevkOpaqueSelection* selections,uint32_t selection_count,SevkOpaqueReference* refs,uint32_t ref_capacity,SevkOpaqueIndirect* args,SevkOpaqueDrawRecord* draws,uint32_t draw_capacity,uint32_t* counts,uint32_t view_count,uint32_t* errors){
    return call(context,[&](Renderer& r){r.readback_opaque_reference(selectors,actor_count,selections,selection_count,refs,ref_capacity,args,draws,draw_capacity,counts,view_count,errors);return 0;});
}
int32_t SEVK_CALL sevk_upload_texture(void* context, uint64_t id, uint32_t width, uint32_t height, uint32_t format, uint32_t srgb, const void* data, uint64_t bytes) {
    return call(context, [&](Renderer& r) { r.upload_texture(id,width,height,format,srgb != 0,data,bytes); return 0; });
}
int32_t SEVK_CALL sevk_upload_mesh(void* context, uint64_t id, const SevkVertex* vertices, uint32_t vertex_count, const uint32_t* indices, uint32_t index_count) {
    return call(context, [&](Renderer& r) { r.upload_mesh(id,vertices,vertex_count,indices,index_count); return 0; });
}
int32_t SEVK_CALL sevk_update_stream_texture(void* context,uint64_t id,uint32_t width,uint32_t height,uint32_t format,const void* data,uint64_t bytes) {
    return call(context,[&](Renderer& r){r.update_stream_texture(id,width,height,format,data,bytes);return 0;});
}
int32_t SEVK_CALL sevk_upload_texture_mips(void* context, uint64_t id, uint32_t width, uint32_t height, uint32_t format, uint32_t srgb, const void* data, uint64_t bytes, uint32_t mip_count) {
    return call(context, [&](Renderer& r) { r.upload_texture(id,width,height,format,srgb!=0,data,bytes,false,mip_count); return 0; });
}
int32_t SEVK_CALL sevk_set_lights(void* context, const SevkLight* lights, uint32_t count) {
    return call_deferrable(context,[lights_copy=copy_in(lights,count),count](Renderer& r){auto* lights=lights_copy.get(); if (r.recording || count>r.lights.size() || (!lights && count)) throw std::runtime_error("invalid local light batch");
        if(count>64 && (!r.world.lights[0]||!r.world.lights[1]||!r.world.lights[2]))throw std::runtime_error("large local light batches require stock world lighting");
        uint32_t points=0;for(uint32_t i=0;i<count;++i){if(lights[i].flags&1){if(i>=64)throw std::runtime_error("spotlight identity exceeds the 64-slot shadow mask");}else if(++points>4096)throw std::runtime_error("stock point-light capacity exceeded");}
        if (count) std::memcpy(r.lights.data(),lights,count*sizeof(SevkLight)); r.light_count=count; return 0; });
}
int32_t SEVK_CALL sevk_set_shadow_cascades(void* context,const float* matrices,uint32_t count,const float* splits) {
    return call_deferrable(context,[matrices_copy=copy_in(matrices,uint64_t(count)*16),splits_copy=copy_in(splits,count),count](Renderer& r){auto* matrices=matrices_copy.get();auto* splits=splits_copy.get();if(r.recording || count>8 || (count && (!matrices || !splits))) throw std::runtime_error("invalid shadow cascade batch");
        for(uint32_t i=0;i<count;++i) if(!std::isfinite(splits[i]) || splits[i]<=0 || (i && splits[i]<=splits[i-1])) throw std::runtime_error("shadow splits must be positive and increasing");
        for(uint32_t i=0;i<count*16;++i)if(!std::isfinite(matrices[i]))throw std::runtime_error("nonfinite shadow matrix");
        if(count) {std::memcpy(r.shadow_matrices,matrices,count*16*sizeof(float));std::memcpy(r.shadow_splits,splits,count*sizeof(float));}else r.shadow_pass_constants_provided=false;r.shadow_count=count;return 0;});
}
int32_t SEVK_CALL sevk_set_shadow_pass_constants(void* ctx,const void* data,uint32_t bytes){return call_deferrable(ctx,[data_copy=copy_bytes(data,bytes),bytes](Renderer& r){const void* data=data_copy.get();if(r.recording || (bytes!=0 && bytes!=1200) || (!data&&bytes))throw std::runtime_error("invalid stock shadow constant buffer");if(bytes){uint32_t count;std::memcpy(&count,static_cast<const uint8_t*>(data)+1192,4);if(count!=r.shadow_count)throw std::runtime_error("shadow constant count mismatch");std::memcpy(r.shadow_pass_constants.data(),data,bytes);}r.shadow_pass_constants_provided=bytes!=0;return 0;});}
int32_t SEVK_CALL sevk_set_shadow_schedule(void* context,uint32_t enabled,uint32_t update_mask,uint32_t valid_mask){
    return call_deferrable(context,[enabled,update_mask,valid_mask](Renderer& r){uint32_t active=(1u<<r.shadow_count)-1;if(r.recording || enabled>3 || ((update_mask|valid_mask)&~active))throw std::runtime_error("invalid shadow scheduling mask");r.scheduled_shadows=(enabled&1)!=0;r.shadow_usage.enabled=(enabled&2)!=0;r.shadow_update_mask=update_mask;r.shadow_valid_mask=valid_mask;return 0;});
}
int32_t SEVK_CALL sevk_render_texture(void* context, uint64_t id, uint32_t width, uint32_t height, const float* clear, const SevkSprite* sprites, uint32_t count) {
    return call(context,[&](Renderer& r) { r.render_texture(id,width,height,clear,sprites,count,float(width),float(height)); return 0; });
}
int32_t SEVK_CALL sevk_render_texture_aspect(void* context, uint64_t id, uint32_t width, uint32_t height, const float* clear, const SevkSprite* sprites, uint32_t count, float logical_width, float logical_height) {
    return call(context,[&](Renderer& r) { r.render_texture(id,width,height,clear,sprites,count,logical_width,logical_height); return 0; });
}
int32_t SEVK_CALL sevk_begin_frame(void* context, const SevkFrameInfo* frame) { return call(context, [&](Renderer& r) { if (!frame) throw std::runtime_error("null frame"); return r.begin_frame(*frame); }); }
int32_t SEVK_CALL sevk_render_texture_mips(void* context,uint64_t id,uint32_t width,uint32_t height,const float* clear,const SevkSprite* sprites,uint32_t count,float logical_width,float logical_height,uint32_t generate_mips){return call(context,[&](Renderer& r){if(generate_mips>1)throw std::runtime_error("invalid LCD mip option");r.render_texture(id,width,height,clear,sprites,count,logical_width,logical_height,generate_mips!=0);return 0;});}
int32_t SEVK_CALL sevk_draw_mesh(void* context, const SevkDrawMesh* draw) { return call(context, [&](Renderer& r) { if (!draw) throw std::runtime_error("null draw"); r.draw_mesh(*draw); return 0; }); }
int32_t SEVK_CALL sevk_draw_lines(void* context, const SevkLineVertex* vertices, uint32_t count, uint32_t flags) { return call(context, [&](Renderer& r) { r.draw_lines(vertices, count, flags); return 0; }); }
int32_t SEVK_CALL sevk_draw_sprites(void* context, const SevkSprite* sprites, uint32_t count) { return call(context, [&](Renderer& r) { if ((!sprites && count) || count > 1000000) throw std::runtime_error("invalid sprite batch"); r.draw_sprites(sprites,count); return 0; }); }
int32_t SEVK_CALL sevk_end_frame(void* context) { return call(context, [&](Renderer& r) { return r.end_frame(); }); }
// Records frame N on a worker while managed frame N+1 runs; see call_deferrable.
int32_t SEVK_CALL sevk_end_frame_async(void* context) { return call(context, [&](Renderer& r) { return r.end_frame_async(); }); }
int32_t SEVK_CALL sevk_remove_resource(void* context, uint32_t kind, uint64_t id) { return call(context, [&](Renderer& r) { r.remove(kind,id); return 0; }); }
int32_t SEVK_CALL sevk_get_stats(void* context, SevkStats* stats) { return call(context, [&](Renderer& r) {
    if (!stats || stats->size != sizeof(SevkStats)) throw std::runtime_error("stats ABI size mismatch");
    r.collect();r.stats.meshes=r.meshes.size()+r.world.meshes.size()+r.stock.meshes.size();r.stats.textures=r.textures.size()+r.world.cubes.size()+r.world.arrays.size(); *stats = r.stats; copy_text(stats->detail, !r.last_validation_error.empty()?r.last_validation_error:(r.error.empty() ? r.recent_validation : r.error)); return 0;
}); }
int32_t SEVK_CALL sevk_readback(void* context, void* rgba8, uint64_t bytes) { return call(context, [&](Renderer& r) { r.readback(rgba8,bytes); return 0; }); }
int32_t SEVK_CALL sevk_request_readback(void* context) {return call(context,[&](Renderer& r) {if(!r.readback_supported) throw std::runtime_error("capture unsupported by presentation surface");r.capture_requested=true;return 0;});}
void SEVK_CALL sevk_destroy(void* context) { if (active_context == context) active_context = nullptr; if(context){auto* r=static_cast<Renderer*>(context);if(r->record_job.valid())r->record_job.wait();
    // SE_VULKAN_PIPELINE_REPORT=<file>: pipelined frames, cascade-mask prediction misses and waits,
    // plus native pass packing joins that blocked, split into render thread and recording worker.
    if(const char* path=std::getenv("SE_VULKAN_PIPELINE_REPORT"))if(FILE* f=std::fopen(path,"w")){std::fprintf(f,"{\"PipelinedFrames\":%llu,\"MaskMismatches\":%llu,\"Waits\":%llu,\"WaitUs\":%llu,\"PackingBlocked\":%llu,\"PackingBlockedUs\":%llu,\"PackingWorkerBlocked\":%llu,\"PackingWorkerBlockedUs\":%llu",(unsigned long long)r->pipelined_frames,(unsigned long long)r->pipelined_mask_mismatches,(unsigned long long)r->pipelined_waits,(unsigned long long)r->pipelined_wait_us,(unsigned long long)r->packing_blocked[0],(unsigned long long)r->packing_blocked_us[0],(unsigned long long)r->packing_blocked[1],(unsigned long long)r->packing_blocked_us[1]);
#if defined(_WIN32)
        // Each wait records its innermost frames; only frames resolving to an export are listed.
        HANDLE process=GetCurrentProcess();bool symbols=SymInitialize(process,nullptr,TRUE);
        auto sites=[&](const char* label,const std::map<void*,std::pair<uint64_t,uint64_t>>& addresses){std::map<std::string,std::pair<uint64_t,uint64_t>> exports;
            if(symbols){alignas(SYMBOL_INFO) char buffer[sizeof(SYMBOL_INFO)+256]{};auto* info=reinterpret_cast<SYMBOL_INFO*>(buffer);
                for(auto& [address,site]:addresses){info->SizeOfStruct=sizeof(SYMBOL_INFO);info->MaxNameLen=255;DWORD64 offset=0;
                    if(SymFromAddr(process,DWORD64(address),&offset,info)&&std::strncmp(info->Name,"sevk_",5)==0){auto& e=exports[info->Name];e.first+=site.first;e.second+=site.second;}}}
            std::fprintf(f,",\"%s\":[",label);bool first=true;for(auto& [name,site]:exports){std::fprintf(f,"%s{\"Export\":\"%s\",\"Waits\":%llu,\"WaitUs\":%llu}",first?"":",",name.c_str(),(unsigned long long)site.first,(unsigned long long)site.second);first=false;}std::fprintf(f,"]");};
        sites("Sites",r->pipelined_wait_sites);sites("PackingSites",r->packing_wait_sites);
        if(symbols)SymCleanup(process);
#endif
        std::fprintf(f,"}\n");std::fclose(f);}} delete static_cast<Renderer*>(context); }
const char* SEVK_CALL sevk_last_error(void* context) { return context ? static_cast<Renderer*>(context)->error.c_str() : global_error.c_str(); }
int32_t SEVK_CALL sevk_init_gbuffer(void* ctx,const void* vs,uint64_t vb,const void* ps,uint64_t pb) {return call(ctx,[&](Renderer& r){r.init_stock(vs,vb,ps,pb);return 0;});}
int32_t SEVK_CALL sevk_upload_gbuffer_mesh(void* ctx,uint64_t id,const SevkGBufferVertex* v,uint32_t vc,const uint32_t* i,uint32_t ic) {return call(ctx,[&](Renderer& r){r.upload_stock(id,v,vc,i,ic);return 0;});}
int32_t SEVK_CALL sevk_render_gbuffer(void* ctx,uint32_t w,uint32_t h,const void* frame,uint32_t fb,const float* proj,const SevkGBufferDraw* draws,uint32_t count) {return call(ctx,[&](Renderer& r){r.render_stock(w,h,frame,fb,proj,draws,count);return 0;});}
int32_t SEVK_CALL sevk_readback_gbuffer(void* ctx,uint32_t attachment,void* pixels,uint64_t bytes) {return call(ctx,[&](Renderer& r){r.read_stock(attachment,pixels,bytes);return 0;});}
int32_t SEVK_CALL sevk_get_gbuffer_stats(void* ctx,SevkGBufferStats* stats) {return call(ctx,[&](Renderer& r){if(!stats || stats->size!=sizeof(SevkGBufferStats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("stock stats ABI mismatch");*stats=r.stock.stats;stats->size=sizeof(*stats);stats->abi_version=SEVK_ABI_VERSION;return 0;});}
int32_t SEVK_CALL sevk_begin_uploads(void* ctx){return call_deferrable(ctx,[](Renderer& r){r.begin_uploads();return 0;});}
int32_t SEVK_CALL sevk_end_uploads(void* ctx){return call(ctx,[&](Renderer& r){r.end_uploads();return 0;});}
int32_t SEVK_CALL sevk_get_work_stats(void* ctx,SevkWorkStats* stats){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)return -1;return call_snapshot(ctx,[&](Renderer& r)->std::optional<int32_t>{*stats=r.snapshot.work;return 0;},[&](Renderer& r){*stats=r.work;return 0;});}
int32_t SEVK_CALL sevk_get_performance_stats(void* ctx,SevkPerformanceStats* stats){return call(ctx,[&](Renderer& r){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("performance stats ABI mismatch");*stats=r.performance;return 0;});}
int32_t SEVK_CALL sevk_set_texture_validation_cache(void* ctx,uint32_t enabled){return call(ctx,[&](Renderer& r){if(r.recording)throw std::runtime_error("queue policy cannot change during recording");r.world.texture_validation_cache=enabled!=0;return 0;});}
int32_t SEVK_CALL sevk_set_async_policy(void* ctx,uint32_t packing,uint32_t cells){return call(ctx,[&](Renderer& r){if(r.recording||packing>1||cells>1)throw std::runtime_error("invalid async policy");r.pass_scene.scene.set_async_packing(packing!=0);r.settle_cells();r.deferred_cell_emission=cells!=0;return 0;});}
int32_t SEVK_CALL sevk_set_parallel_world_queue(void* ctx,uint32_t enabled){return call(ctx,[&](Renderer& r){if(r.recording)throw std::runtime_error("queue policy cannot change during recording");r.world.parallel_queue=enabled!=0;return 0;});}
int32_t SEVK_CALL sevk_register_world_recipe(void* ctx,const SevkWorldRecipe* recipe,uint64_t* handle){return call(ctx,[&](Renderer& r){if(!recipe || !handle)throw std::runtime_error("missing recipe/handle");*handle=r.register_world_recipe(*recipe);return 0;});}
int32_t SEVK_CALL sevk_queue_world_recipes(void* ctx,const void* frame,uint32_t fb,const float* projection,const float* inverse,const SevkRecipeDraw* draws,uint32_t count,const uint8_t* objects,uint32_t object_bytes,const uint8_t* instances,uint32_t instance_bytes){return call(ctx,[&](Renderer& r){r.queue_world_recipes(frame,fb,projection,inverse,draws,count,objects,object_bytes,instances,instance_bytes);return 0;});}
int32_t SEVK_CALL sevk_register_model(void* ctx,const SevkModelPart* parts,uint32_t count,const uint8_t* objects,uint32_t bytes,uint64_t* handle){return call(ctx,[&](Renderer& r){if(!handle)throw std::runtime_error("model handle output missing");*handle=r.register_model(parts,count,objects,bytes);return 0;});}
int32_t SEVK_CALL sevk_register_model_ordered(void* ctx,const SevkModelPart* parts,const SevkOldPartOrder* orders,uint32_t count,const uint8_t* objects,uint32_t bytes,uint64_t* handle){return call(ctx,[&](Renderer& r){if(!handle||!orders)throw std::runtime_error("ordered model input missing");*handle=r.register_model(parts,count,objects,bytes,orders);return 0;});}
int32_t SEVK_CALL sevk_clear_models(void* ctx){return call(ctx,[&](Renderer& r){r.clear_models();return 0;});}
int32_t SEVK_CALL sevk_append_models(void* ctx,const SevkModelInstance* instances,uint32_t count,const uint32_t* order,uint32_t legacy_count,uint32_t* remap){return call(ctx,[&](Renderer& r){r.append_models(instances,count,order,legacy_count,remap);return 0;});}
int32_t SEVK_CALL sevk_world_main_order(void* ctx,const SevkMainDrawOrder* orders,uint32_t count){return call(ctx,[&](Renderer& r){r.set_world_main_order(orders,count);return 0;});}
int32_t SEVK_CALL sevk_world_old_order(void* ctx,const SevkOldPartOrder* orders,uint32_t count){return call(ctx,[&](Renderer& r){r.set_world_old_order(orders,count);return 0;});}
int32_t SEVK_CALL sevk_world_draw_owners(void* ctx,const uint32_t* owners,uint32_t count){return call(ctx,[&](Renderer& r){r.set_world_draw_owners(owners,count);return 0;});}
int32_t SEVK_CALL sevk_world_main_owners(void* ctx,const SevkMainOwnerOrder* owners,uint32_t count){return call(ctx,[&](Renderer& r){r.set_world_main_owners(owners,count);return 0;});}
int32_t SEVK_CALL sevk_get_model_group_stats(void* ctx,SevkModelGroupStats* stats){return call(ctx,[&](Renderer& r){if(!stats||stats->size!=sizeof(*stats)||stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("model group stats ABI mismatch");*stats=r.world.models.stats;return 0;});}
int32_t SEVK_CALL sevk_get_recipe_stats(void* ctx,SevkRecipeStats* stats){return call(ctx,[&](Renderer& r){r.settle_cells();if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("recipe stats ABI mismatch");*stats=r.world.current().recipe_stats;return 0;});}
int32_t SEVK_CALL sevk_get_queue_work_stats(void* ctx,SevkQueueWorkStats* stats){return call(ctx,[&](Renderer& r){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("queue work stats ABI mismatch");*stats=r.world.current().queue_work;return 0;});}
int32_t SEVK_CALL sevk_set_world_indirect(void* ctx,uint32_t enabled){return call(ctx,[&](Renderer& r){if(r.recording)throw std::runtime_error("indirect policy cannot change during recording");r.world.indirect_requested=enabled!=0;r.saved_shadows=false;return 0;});}
int32_t SEVK_CALL sevk_set_hdr_quality(void* ctx,uint32_t high_quality){return call(ctx,[&](Renderer& r){r.set_hdr_quality(high_quality);return 0;});}
int32_t SEVK_CALL sevk_set_geometry_arena(void* ctx,uint32_t enabled){return call(ctx,[&](Renderer& r){if(r.recording)throw std::runtime_error("geometry policy cannot change during recording");r.world.geometry_arena_requested=enabled!=0;return 0;});}
int32_t SEVK_CALL sevk_get_geometry_arena_stats(void* ctx,SevkGeometryArenaStats* stats){return call(ctx,[&](Renderer& r){
    if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("geometry arena stats ABI mismatch");
    *stats=SevkGeometryArenaStats{sizeof(*stats),SEVK_ABI_VERSION,r.world.geometry_arena_requested?1u:0u};
    auto read=[](const std::vector<GeometryPage>& pages,uint32_t& count,uint64_t& live,uint64_t& capacity){for(const auto& p:pages)if(p.block){++count;VmaStatistics s{};vmaGetVirtualBlockStatistics(p.block,&s);live+=s.allocationBytes*p.unit;capacity+=p.buffer.bytes;}};
    read(r.world.vertex_pages,stats->vertex_pages,stats->vertex_live,stats->vertex_capacity);read(r.world.index_pages,stats->index_pages,stats->index_live,stats->index_capacity);return 0;});}
int32_t SEVK_CALL sevk_get_indirect_stats(void* ctx,SevkIndirectStats* stats){return call(ctx,[&](Renderer& r){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("indirect stats ABI mismatch");*stats=r.world.indirect_stats;stats->supported=r.world.indirect_supported;stats->enabled=r.world.indirect_supported&&r.world.indirect_requested;return 0;});}
int32_t SEVK_CALL sevk_get_queue_validation_stats(void* ctx,SevkQueueValidationStats* stats){return call(ctx,[&](Renderer& r){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("queue validation stats ABI mismatch");*stats=r.world.current().validation_stats;return 0;});}
int32_t SEVK_CALL sevk_get_presentation_stats(void* ctx,SevkPresentationStats* stats){return call(ctx,[&](Renderer& r){
    if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("presentation stats ABI mismatch");
    *stats=SevkPresentationStats{sizeof(*stats),SEVK_ABI_VERSION,r.swapchain?1u:0u,r.stats.surface_ready,r.vsync?1u:0u,
        r.swapchain?uint32_t(r.actual_present_mode):UINT32_MAX,uint32_t(r.swap_images.size()),r.swapchain_generation};return 0;});}
int32_t SEVK_CALL sevk_set_async_present(void* ctx,uint32_t enabled){return call(ctx,[&](Renderer& r){if(r.recording)throw std::runtime_error("presentation policy cannot change during recording");r.join_present();r.async_present=enabled!=0;return 0;});}
int32_t SEVK_CALL sevk_get_present_samples(void* ctx,uint64_t after,SevkPresentSample* samples,uint32_t capacity,uint32_t* count,uint64_t* oldest){return call(ctx,[&](Renderer& r){if(!count||!oldest||(!samples&&capacity)||capacity>4096)throw std::runtime_error("invalid present sample buffer");r.poll_present();*count=0;*oldest=r.present_samples.empty()?0:r.present_samples.front().sequence;for(const auto& sample:r.present_samples)if(sample.sequence>after){if(*count==capacity)break;samples[(*count)++]=sample;}return 0;});}
int32_t SEVK_CALL sevk_get_present_flow_stats(void* ctx,SevkPresentFlowStats* stats){return call(ctx,[&](Renderer& r){if(!stats||stats->size!=sizeof(*stats)||stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("present flow ABI mismatch");r.poll_present();*stats=r.present_flow;stats->enabled=r.async_present?1u:0u;stats->pending=r.pending_present.valid()?1u:0u;return 0;});}
int32_t SEVK_CALL sevk_init_world_pipeline(void* ctx,uint32_t id,const void* vs,uint64_t vb,const void* ps,uint64_t pb){return call(ctx,[&](Renderer& r){r.init_world_pipeline(id,vs,vb,ps,pb);return 0;});}
int32_t SEVK_CALL sevk_init_object_motion(void* ctx,uint32_t kind,const void* vs,uint64_t vb,const void* ps,uint64_t pb){return call(ctx,[&](Renderer& r){r.init_object_motion(kind,vs,vb,ps,pb);return 0;});}
int32_t SEVK_CALL sevk_init_indexed_attachment(void* ctx,uint32_t slot,uint32_t format,uint32_t kind,const void* vs,uint64_t vb,const void* ps,uint64_t pb){return call(ctx,[&](Renderer& r){r.init_indexed_attachment(slot,format,kind,vs,vb,ps,pb);return 0;});}
int32_t SEVK_CALL sevk_init_indexed_motion(void* ctx,uint32_t kind,const void* vs,uint64_t vb,const void* ps,uint64_t pb){return call(ctx,[&](Renderer& r){r.init_indexed_motion(kind,vs,vb,ps,pb);return 0;});}
int32_t SEVK_CALL sevk_declare_gbuffer_attachment(void* ctx,uint32_t slot,uint32_t format){return call(ctx,[&](Renderer& r){r.declare_gbuffer_attachment(slot,format);return 0;});}
int32_t SEVK_CALL sevk_init_gbuffer_attachment(void* ctx,uint32_t slot,uint32_t format,uint32_t kind,const void* vs,uint64_t vb,const void* ps,uint64_t pb){return call(ctx,[&](Renderer& r){r.init_gbuffer_attachment(slot,format,kind,vs,vb,ps,pb);return 0;});}
int32_t SEVK_CALL sevk_init_world_lighting(void* ctx,uint32_t kind,const void* spv,uint64_t bytes){return call(ctx,[&](Renderer& r){r.init_world_lighting(kind,spv,bytes);return 0;});}
int32_t SEVK_CALL sevk_upload_world_mesh(void* ctx,uint64_t id,const SevkWorldVertex* v,uint32_t vc,const uint32_t* i,uint32_t ic){return call(ctx,[&](Renderer& r){r.upload_world_mesh(id,v,vc,i,ic);return 0;});}
int32_t SEVK_CALL sevk_register_world_mesh_range(void* ctx,uint64_t id,uint64_t source,uint32_t first,uint32_t count){return call(ctx,[&](Renderer& r){r.register_world_mesh_range(id,source,first,count);return 0;});}
int32_t SEVK_CALL sevk_upload_voxel_mesh(void* ctx,uint64_t id,const void* vertices,uint32_t vc,const uint32_t* indices,uint32_t ic){return call(ctx,[&](Renderer& r){r.upload_world_mesh(id,vertices,vc,indices,ic,24);return 0;});}
int32_t SEVK_CALL sevk_upload_voxel_array(void* ctx,uint64_t id,uint32_t w,uint32_t h,uint32_t fmt,uint32_t srgb,uint32_t layers,uint32_t mips,const void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.upload_voxel_array(id,w,h,fmt,srgb!=0,layers,mips,data,bytes);return 0;});}
int32_t SEVK_CALL sevk_set_voxel_material(void* ctx,uint64_t id,const void* constants,uint32_t bytes,const uint64_t* arrays,uint64_t random){return call(ctx,[&](Renderer& r){r.set_voxel_material(id,constants,bytes,arrays,random);return 0;});}
int32_t SEVK_CALL sevk_queue_world(void* ctx,const void* frame,uint32_t fb,const float* projection,const float* inverse,const SevkWorldDraw* draws,uint32_t count){
    // Only the empty submission defers; populated legacy draws carry blob offsets.
    if(!frame&&!count)return call_deferrable(ctx,[fb,projection,inverse](Renderer& r){r.queue_world(nullptr,fb,projection,inverse,nullptr,0);return 0;});
    return call(ctx,[&](Renderer& r){r.queue_world(frame,fb,projection,inverse,draws,count);return 0;});
}
int32_t SEVK_CALL sevk_queue_world_retained(void* ctx,const void* frame,uint32_t fb,const float* projection,const float* inverse,const SevkWorldDraw* draws,uint32_t count,const uint64_t* ids){return call(ctx,[&](Renderer& r){if(count && !ids)throw std::runtime_error("retained object identities missing");r.queue_world(frame,fb,projection,inverse,draws,count,ids);return 0;});}
int32_t SEVK_CALL sevk_world_object_alignment(void* ctx,uint32_t* alignment){return call(ctx,[&](Renderer& r){if(!alignment || !r.world.alignment)throw std::runtime_error("world alignment unavailable");*alignment=uint32_t(r.world.alignment);return 0;});}
int32_t SEVK_CALL sevk_queue_world_blob(void* ctx,const void* frame,uint32_t fb,const float* projection,const float* inverse,const SevkWorldDraw* draws,uint32_t count,const uint8_t* objects,uint32_t object_bytes,const uint8_t* instances,uint32_t instance_bytes){return call(ctx,[&](Renderer& r){if(!count || !objects || !object_bytes)throw std::runtime_error("empty world blob");r.queue_world(frame,fb,projection,inverse,draws,count,nullptr,objects,object_bytes,instances,instance_bytes);return 0;});}
int32_t SEVK_CALL sevk_get_object_record_stats(void* ctx,SevkObjectRecordStats* stats){return call(ctx,[&](Renderer& r){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("object record stats ABI mismatch");const auto& o=r.world.object_records;*stats={sizeof(*stats),SEVK_ABI_VERSION,r.world.retained_objects?1u:0u,uint32_t(o.records.size()),r.world.retained_objects?uint32_t(o.selected.size()):0u,r.world.retained_objects?o.changed:0u,o.live,o.data.size(),r.world.object_uploaded_bytes};return 0;});}

int32_t SEVK_CALL sevk_upload_world_cube(void* ctx,uint64_t id,uint32_t w,uint32_t h,uint32_t fmt,uint32_t srgb,const void* bytes,uint64_t size){return call(ctx,[&](Renderer& r){r.upload_world_cube(id,w,h,fmt,srgb!=0,bytes,size);return 0;});}
int32_t SEVK_CALL sevk_upload_world_cube_mips(void* ctx,uint64_t id,uint32_t w,uint32_t h,uint32_t fmt,uint32_t srgb,const void* bytes,uint64_t size,uint32_t mips){return call(ctx,[&](Renderer& r){if(srgb>1)throw std::runtime_error("invalid cube color space");r.upload_world_cube(id,w,h,fmt,srgb!=0,bytes,size,mips);return 0;});}
int32_t SEVK_CALL sevk_set_world_skybox(void* ctx,uint64_t id){return call(ctx,[&](Renderer& r){if(id && !r.world.cubes.count(id))throw std::runtime_error("missing world cube");r.world.skybox=id;r.world.probe_skybox=0;return 0;});}
int32_t SEVK_CALL sevk_set_world_skyboxes(void* ctx,uint64_t visible,uint64_t indirect){return call_deferrable(ctx,[visible,indirect](Renderer& r){if((visible && !r.world.cubes.count(visible)) || (indirect && !r.world.cubes.count(indirect)))throw std::runtime_error("missing visible/indirect world cube");r.world.skybox=visible;r.world.probe_skybox=indirect;return 0;});}

int32_t SEVK_CALL sevk_get_voxel_stats(void* ctx,SevkVoxelStats* stats){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)return -1;return call(ctx,[&](Renderer& r){stats->frames=r.world.voxel_frames;stats->draws=r.world.voxel_draws;stats->meshes=0;for(auto& p:r.world.meshes)if(p.second.vertex_stride==24)++stats->meshes;stats->arrays=r.world.arrays.size();stats->materials=r.world.voxel_materials.size();return 0;});}

int32_t SEVK_CALL sevk_build_voxel_array(void* ctx,uint64_t id,uint32_t size,uint32_t srgb,const uint64_t* sources,uint32_t layers){return call(ctx,[&](Renderer& r){r.build_voxel_array(id,size,srgb!=0,sources,layers);return 0;});}

int32_t SEVK_CALL sevk_init_environment(void* ctx,uint32_t stage,const void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.init_environment(stage,data,bytes);return 0;});}
int32_t SEVK_CALL sevk_set_shadow_options(void* ctx,uint32_t flags){return call_deferrable(ctx,[flags](Renderer& r){if(r.recording || flags>1)throw std::runtime_error("invalid shadow options");r.shadow_blur_enabled=flags!=0;return 0;});}
int32_t SEVK_CALL sevk_prepare_postprocess_generation(void* ctx,uint64_t expected,const SevkPostprocessModule* modules,uint32_t count,uint64_t* generation){return call(ctx,[&](Renderer& r){if(!generation)throw std::runtime_error("null postprocess generation output");*generation=r.post_prepare_generation(expected,modules,count);return 0;});}
int32_t SEVK_CALL sevk_set_gbuffer_velocity_probe(void* ctx,uint32_t mode){return call(ctx,[&](Renderer& r){r.set_gbuffer_velocity_probe(mode);return 0;});}
int32_t SEVK_CALL sevk_native_owner_capabilities(void* ctx,SevkNativeOwnerCapabilities* state){if(!ctx||!state||state->size!=sizeof(*state)||state->abi_version!=SEVK_ABI_VERSION)return -1;*state={sizeof(*state),SEVK_ABI_VERSION,7u|(static_cast<Renderer*>(ctx)->gbuffer_velocity_supported?8u:0u)|16u|32u|64u,8192,117};return 0;}
int32_t SEVK_CALL sevk_prepare_native_generation(void* ctx,const SevkNativeGeneration* input,uint64_t* generation){if(!input||!generation||input->size!=sizeof(*input)||input->abi_version!=SEVK_ABI_VERSION||input->flags&~1u)return -1;auto request=*input;return call(ctx,[&](Renderer& r){*generation=r.post_prepare_generation(request.expected_revision,request.modules,request.count,request.flags);return 0;});}
int32_t SEVK_CALL sevk_activate_postprocess_generation(void* ctx,uint64_t generation){return call(ctx,[&](Renderer& r){r.post_queue_generation(generation);r.post_activate_pending();return 0;});}
int32_t SEVK_CALL sevk_discard_postprocess_generation(void* ctx,uint64_t generation){return call(ctx,[&](Renderer& r){r.post_discard_generation(generation);return 0;});}
int32_t SEVK_CALL sevk_get_postprocess_generation(void* ctx,uint32_t stage,SevkPostprocessGenerationStats* result){return call(ctx,[&](Renderer& r){r.post_generation_status(stage,result);return 0;});}
int32_t SEVK_CALL sevk_init_postprocess(void* ctx,uint32_t stage,const void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.init_postprocess(stage,data,bytes);return 0;});}
int32_t SEVK_CALL sevk_set_hbao(void* ctx,const SevkHbaoConfig* config){if(!config)return -1;return call_deferrable(ctx,[config_copy=*config](Renderer& r){auto* config=&config_copy;r.set_hbao(*config);return 0;});}
int32_t SEVK_CALL sevk_set_hbao_jitter(void* ctx,const float* values,uint32_t count){return call(ctx,[&](Renderer& r){r.set_hbao_jitter(values,count);return 0;});}
int32_t SEVK_CALL sevk_init_probes(void* ctx,uint32_t stage,const void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.init_probes(stage,data,bytes);return 0;});}
int32_t SEVK_CALL sevk_set_probes(void* ctx,const SevkProbeConfig* config){if(!config)return -1;return call(ctx,[&](Renderer& r){r.set_probes(*config);return 0;});}
int32_t SEVK_CALL sevk_set_probes_prefilter(void* ctx,const SevkProbeConfig* config,uint32_t skip_levels){if(!config)return -1;return call(ctx,[&](Renderer& r){r.set_probes(*config,skip_levels);return 0;});}
int32_t SEVK_CALL sevk_get_probe_stats(void* ctx,SevkProbeStats* stats){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)return -1;return call(ctx,[&](Renderer& r){*stats=r.probes.stats;stats->ready=r.probes.ready;stats->next_face=r.probes.face;return 0;});}
int32_t SEVK_CALL sevk_get_probe_schedule_stats(void* ctx,SevkProbeScheduleStats* stats){return call(ctx,[&](Renderer& r){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("probe schedule stats ABI mismatch");stats->stage=r.probes.stage;stats->busy=r.probes.stage!=0;stats->cycles=r.probes.stats.cycles;stats->reserved=0;return 0;});}
int32_t SEVK_CALL sevk_readback_probe(void* ctx,uint32_t kind,uint32_t face,uint32_t mip,void* pixels,uint64_t capacity){return call(ctx,[&](Renderer& r){r.readback_probe(kind,face,mip,pixels,capacity);return 0;});}
int32_t SEVK_CALL sevk_init_stock_shadows(void* ctx,const void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.init_stock_shadows(data,bytes);return 0;});}
int32_t SEVK_CALL sevk_init_shadow_usage(void* ctx,const void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.init_shadow_usage(data,bytes);return 0;});}
int32_t SEVK_CALL sevk_readback_shadow(void* ctx,uint32_t cascade,void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.readback_shadow(cascade,data,bytes);return 0;});}
int32_t SEVK_CALL sevk_readback_lighting_input(void* ctx,uint32_t input,void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.readback_lighting_input(input,data,bytes);return 0;});}
int32_t SEVK_CALL sevk_set_lighting_diagnostics(void* ctx,uint32_t enabled){return call(ctx,[&](Renderer& r){r.set_lighting_diagnostics(enabled);return 0;});}
int32_t SEVK_CALL sevk_set_lighting_diagnostic_sampler(void* ctx,uint32_t mode){return call(ctx,[&](Renderer& r){r.set_lighting_diagnostic_sampler(mode);return 0;});}
int32_t SEVK_CALL sevk_readback_lighting_stage(void* ctx,uint32_t stage,void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.readback_lighting_stage(stage,data,bytes);return 0;});}
int32_t SEVK_CALL sevk_get_shadow_feedback(void* ctx,SevkShadowFeedback* out){if(!out || out->size!=sizeof(*out) || out->abi_version!=SEVK_ABI_VERSION)return -1;return call_snapshot(ctx,[&](Renderer& r)->std::optional<int32_t>{*out=r.snapshot.shadow;return 0;},[&](Renderer& r){if(r.recording)throw std::runtime_error("invalid shadow feedback contract");*out=r.shadow_feedback();return 0;});}
int32_t SEVK_CALL sevk_get_shadow_cache_mask(void* ctx,uint32_t* mask){if(!mask)return -1;return call_snapshot(ctx,[&](Renderer& r)->std::optional<int32_t>{if(!r.snapshot.cache_valid)return std::nullopt;*mask=r.snapshot.cache_mask;return 0;},[&](Renderer& r){if(r.recording)throw std::runtime_error("invalid shadow cache mask query");*mask=r.shadow_image_mask&((1u<<r.shadow_count)-1);return 0;});}
int32_t SEVK_CALL sevk_init_occlusion(void* ctx,const void* vs,uint64_t vb,const void* ps,uint64_t pb){return call(ctx,[&](Renderer& r){r.init_occlusion(vs,vb,ps,pb);return 0;});}
int32_t SEVK_CALL sevk_queue_occlusion(void* ctx,uint64_t token,const SevkOcclusionDraw* queries,uint32_t count){return call_deferrable(ctx,[token,queries_copy=copy_in(queries,count),count](Renderer& r){auto* queries=queries_copy.get();r.queue_occlusion(token,queries,count);return 0;});}
int32_t SEVK_CALL sevk_queue_shadow_occlusion(void* ctx,uint32_t cascade,uint64_t token,const SevkOcclusionDraw* queries,uint32_t count){return call(ctx,[&](Renderer& r){r.queue_shadow_occlusion(cascade,token,queries,count);return 0;});}
int32_t SEVK_CALL sevk_get_shadow_occlusion(void* ctx,uint32_t cascade,SevkOcclusionResult* results,uint32_t capacity,uint32_t* count,uint64_t* token){return call(ctx,[&](Renderer& r){if(cascade>=8 || r.recording || !count || !token || (!results&&capacity))throw std::runtime_error("invalid cascade query result contract");r.collect();auto& state=r.shadow_occlusion[cascade];*count=uint32_t(state.completed.size());*token=state.completed_token;if(*count>capacity)throw std::runtime_error("cascade query result capacity");if(*count)std::memcpy(results,state.completed.data(),*count*sizeof(*results));return 0;});}
int32_t SEVK_CALL sevk_get_occlusion(void* ctx,SevkOcclusionResult* results,uint32_t capacity,uint32_t* count,uint64_t* token){if(!count || !token || (!results && capacity))return -1;
    auto copy=[&](const std::vector<SevkOcclusionResult>& completed,uint64_t completed_token){*count=uint32_t(completed.size());*token=completed_token;if(*count>capacity)throw std::runtime_error("occlusion result capacity too small");if(*count)std::memcpy(results,completed.data(),*count*sizeof(*results));};
    return call_snapshot(ctx,[&](Renderer& r)->std::optional<int32_t>{if(r.snapshot.occlusion.size()>capacity)return std::nullopt;copy(r.snapshot.occlusion,r.snapshot.occlusion_token);return 0;},[&](Renderer& r){r.collect();copy(r.occlusion.completed,r.occlusion.completed_token);return 0;});}
int32_t SEVK_CALL sevk_get_occlusion_stats(void* ctx,SevkOcclusionStats* stats){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)return -1;return call(ctx,[&](Renderer& r){r.collect();*stats=r.occlusion.stats;return 0;});}
int32_t SEVK_CALL sevk_set_shadow_exclusions(void* ctx,const SevkShadowExclusion* exclusions,uint32_t count){return call_deferrable(ctx,[exclusions_copy=copy_in(exclusions,count),count](Renderer& r){auto* exclusions=exclusions_copy.get();if(r.recording || count>8192 || (!exclusions && count))throw std::runtime_error("invalid shadow exclusion batch");std::unordered_set<uint64_t> copied;for(uint32_t i=0;i<count;++i){auto e=exclusions[i];if(e.light>=64 || e.draw>=r.world.current().draws.size())throw std::runtime_error("shadow exclusion outside light/draw range");copied.insert((uint64_t(e.light)<<32)|e.draw);}r.spots.exclusions=std::move(copied);return 0;});}
int32_t SEVK_CALL sevk_set_spots(void* ctx,uint32_t resolution,const SevkSpot* spots,uint32_t count){return call_deferrable(ctx,[resolution,spots_copy=copy_in(spots,count),count](Renderer& r){auto* spots=spots_copy.get();r.set_spots(resolution,spots,count);return 0;});}
int32_t SEVK_CALL sevk_set_spot_redraw(void* ctx,uint64_t static_mask,uint64_t dynamic_mask){return call(ctx,[&](Renderer& r){r.set_spot_redraw(static_mask,dynamic_mask);return 0;});}
int32_t SEVK_CALL sevk_get_spot_stats(void* ctx,SevkSpotStats* stats){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)return -1;return call(ctx,[&](Renderer& r){*stats=r.spots.stats;return 0;});}
int32_t SEVK_CALL sevk_set_probe_atmosphere(void* ctx,uint64_t id,float intensity,float desaturation,const float* direction){return call_deferrable(ctx,[id,intensity,desaturation,direction_copy=copy_in(direction,3)](Renderer& r){auto* direction=direction_copy.get();if(r.recording || !direction || !std::isfinite(intensity) || intensity<0 || !std::isfinite(desaturation))throw std::runtime_error("invalid probe atmosphere settings");for(uint32_t i=0;i<3;++i)if(!std::isfinite(direction[i]))throw std::runtime_error("nonfinite probe sun direction");r.probes.atmosphere_id=id;r.probes.atmosphere_intensity=intensity;r.probes.atmosphere_desaturation=desaturation;std::memcpy(r.probes.sun_direction,direction,12);return 0;});}
int32_t SEVK_CALL sevk_set_shadow_quality(void* ctx,uint32_t resolution,float bias){return call_deferrable(ctx,[resolution,bias](Renderer& r){if(r.recording || resolution<256 || resolution>4096 || (resolution&(resolution-1)) || !std::isfinite(bias) || bias<0 || bias>1)throw std::runtime_error("invalid shadow quality");r.requested_shadow_resolution=resolution;r.shadow_bias=bias;return 0;});}
int32_t SEVK_CALL sevk_queue_highlights(void* ctx,const SevkHighlightDraw* draws,uint32_t count){return call_deferrable(ctx,[draws_copy=copy_in(draws,count),count](Renderer& r){auto* draws=draws_copy.get();r.queue_highlights(draws,count);return 0;});}
int32_t SEVK_CALL sevk_get_highlight_stats(void* ctx,SevkHighlightStats* stats){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)return -1;return call(ctx,[&](Renderer& r){*stats=r.highlights.stats;return 0;});}
int32_t SEVK_CALL sevk_set_render_fraction(void* ctx,float fraction){
    // A deferred fraction is what the next resolution read must observe.
    if(ctx&&std::isfinite(fraction)&&fraction>=.25f&&fraction<=1){auto* renderer=static_cast<Renderer*>(ctx);std::lock_guard queue(renderer->deferred_mutex);if(renderer->record_in_flight){std::lock_guard read(renderer->snapshot_mutex);renderer->snapshot.render_fraction=fraction;}}
    return call_deferrable(ctx,[fraction](Renderer& r){if(r.recording || !std::isfinite(fraction) || fraction<.25f || fraction>1)throw std::runtime_error("invalid world render fraction");r.render_fraction=fraction;return 0;});
}
int32_t SEVK_CALL sevk_set_view_clip(void* ctx,float far_plane){return call_deferrable(ctx,[far_plane](Renderer& r){if(!std::isfinite(far_plane)||far_plane<1)throw std::runtime_error("invalid view clip");r.view_far=far_plane;return 0;});}
int32_t SEVK_CALL sevk_invalidate_guest_history(void* ctx){return call(ctx,[&](Renderer& r){r.guest.have_prev=false;r.guest.frame.history_valid=0;r.jitter.reset=true;return 0;});}
// Temporal jitter owner state: pixel x/y, clip x/y, active, phase count, sequence index, frames. force=1 runs it without an upscaler (diagnostics).
int32_t SEVK_CALL sevk_force_jitter(void* ctx,uint32_t force){return call(ctx,[&](Renderer& r){if(r.recording||force>1)throw std::runtime_error("invalid jitter override");r.jitter.forced=force!=0;r.jitter.reset=true;return 0;});}
int32_t SEVK_CALL sevk_get_jitter(void* ctx,float* state,uint32_t count){return call(ctx,[&](Renderer& r){if(!state||count<8)throw std::runtime_error("jitter state needs 8 floats");const auto& j=r.jitter;float values[8]{j.pixel[0],j.pixel[1],j.ndc[0],j.ndc[1],j.active?1.f:0.f,float(j.phases),float(j.index),float(j.frames)};std::memcpy(state,values,sizeof(values));return 0;});}
int32_t SEVK_CALL sevk_set_display_flags(void* ctx,uint32_t flags){return call(ctx,[&](Renderer& r){uint32_t mask=flags>>16;uint32_t value=flags&0xffffu;if(!mask)r.display_flags=value;else r.display_flags=(r.display_flags&~mask)|(value&mask);return 0;});}
int32_t SEVK_CALL sevk_force_hdr_display(void* ctx,uint32_t force){return call(ctx,[&](Renderer& r){if(r.recording||force>1)throw std::runtime_error("invalid HDR display override");r.hdr_display_forced=force!=0;return 0;});}
int32_t SEVK_CALL sevk_get_display_info(void* ctx,SevkDisplayInfo* info){return call(ctx,[&](Renderer& r){if(!info||info->size!=sizeof(*info)||info->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("display info ABI mismatch");*info=r.display_info();return 0;});}
int32_t SEVK_CALL sevk_readback_hdr(void* ctx,void* rgba16f,uint64_t bytes){return call(ctx,[&](Renderer& r){r.readback_hdr(rgba16f,bytes);return 0;});}
int32_t SEVK_CALL sevk_tenant_create_buffer(void* ctx,uint64_t bytes,uint64_t* id){if(!id){global_error="null tenant id";return -1;}return call(ctx,[&](Renderer& r){*id=r.tenant_create_buffer(bytes);return 0;});}
int32_t SEVK_CALL sevk_tenant_create_image(void* ctx,uint32_t width,uint32_t height,uint32_t layers,uint32_t format,uint32_t flags,uint64_t* id){if(!id){global_error="null tenant id";return -1;}return call(ctx,[&](Renderer& r){*id=r.tenant_create_image(width,height,layers,format,flags);return 0;});}
int32_t SEVK_CALL sevk_tenant_create_compute(void* ctx,const void* spirv,uint64_t bytes,const char* entry,uint64_t* id){if(!id){global_error="null tenant id";return -1;}return call(ctx,[&](Renderer& r){*id=r.tenant_create_compute(spirv,bytes,entry);return 0;});}
int32_t SEVK_CALL sevk_tenant_create_raster(void* ctx,const void* vs,uint64_t vs_bytes,const char* vs_entry,const void* ps,uint64_t ps_bytes,const char* ps_entry,uint32_t target_count,const uint32_t* formats,uint64_t* id){if(!id){global_error="null tenant id";return -1;}return call(ctx,[&](Renderer& r){*id=r.tenant_create_raster(vs,vs_bytes,vs_entry,ps,ps_bytes,ps_entry,target_count,formats);return 0;});}
int32_t SEVK_CALL sevk_tenant_destroy(void* ctx,uint64_t id){return call(ctx,[&](Renderer& r){r.tenant_destroy(id);return 0;});}
int32_t SEVK_CALL sevk_tenant_record(void* ctx,const SevkTenantCommand* commands,uint32_t count,const SevkTenantBinding* bindings,uint32_t binding_count,const void* blob,uint64_t blob_bytes){return call(ctx,[&](Renderer& r){r.tenant_record(commands,count,bindings,binding_count,blob,blob_bytes);return 0;});}
int32_t SEVK_CALL sevk_tenant_readback(void* ctx,uint64_t id,uint32_t layer,uint64_t offset,void* data,uint64_t bytes){return call(ctx,[&](Renderer& r){r.tenant_readback(id,layer,offset,data,bytes);return 0;});}
int32_t SEVK_CALL sevk_tenant_stats(void* ctx,SevkTenantStats* stats){if(!stats){global_error="null tenant stats";return -1;}return call(ctx,[&](Renderer& r){*stats=r.tenant_stats();return 0;});}
int32_t SEVK_CALL sevk_set_hdr_ui(void* ctx,float paper_white,float shoulder,float peak){return call(ctx,[&](Renderer& r){if(!(paper_white>=0.f&&paper_white<=1000.f)||!(shoulder>=0.f&&shoulder<.95f)||!(peak>=0.f&&peak<=1000.f))throw std::runtime_error("invalid HDR UI transform");r.hdr_ui[0]=paper_white;r.hdr_ui[1]=shoulder;r.hdr_ui[2]=peak>0.f?peak:1000.f;return 0;});}
int32_t SEVK_CALL sevk_set_upscale_owner(void* ctx,const char* id){return call(ctx,[&](Renderer& r){(void)r;return Renderer::ngx_set_owner(id);});}
int32_t SEVK_CALL sevk_ngx_configure(uint32_t enabled,uint32_t mode,uint32_t preset,const char* directory){return Renderer::ngx_configure(enabled,mode,preset,directory);}
int32_t SEVK_CALL sevk_ngx_set_sharpness(float sharpness){return Renderer::ngx_set_sharpness(sharpness);}
int32_t SEVK_CALL sevk_ngx_status(char* text,uint32_t capacity){return Renderer::ngx_copy_status(text,capacity);}
int32_t SEVK_CALL sevk_ngx_debug(char* text,uint32_t capacity){return Renderer::ngx_copy_debug(text,capacity);}
int32_t SEVK_CALL sevk_fsr_configure(uint32_t enabled,uint32_t mode,float sharpness,const char* directory){return Renderer::fsr_configure(enabled,mode,sharpness,directory);}
int32_t SEVK_CALL sevk_fsr_status(char* text,uint32_t capacity){return Renderer::fsr_copy_status(text,capacity);}
int32_t SEVK_CALL sevk_fsr_debug(char* text,uint32_t capacity){return Renderer::fsr_copy_debug(text,capacity);}
int32_t SEVK_CALL sevk_fg_status(SevkFrameGenStatus* status){return Renderer::fg_copy_status(status);}
int32_t SEVK_CALL sevk_resolve_render_resolution(void* ctx,uint32_t width,uint32_t height,uint32_t* render_width,uint32_t* render_height,float* fraction){
    if(!width || !height || !render_width || !render_height || !fraction)return -1;
    auto resolve=[&](float value){*render_width=std::max(1u,uint32_t(std::lround(width*value)));*render_height=std::max(1u,uint32_t(std::lround(height*value)));*fraction=value;};
    // Upscalers rewrite the fraction on the owner thread; those frames wait.
    return call_snapshot(ctx,[&](Renderer& r)->std::optional<int32_t>{if(Renderer::upscaler_requested())return std::nullopt;resolve(r.snapshot.render_fraction);return 0;},[&](Renderer& r){
        if(r.recording)throw std::runtime_error("cannot resolve world resolution during recording");
        r.ngx_apply_scale();r.fsr_apply_scale();resolve(r.render_fraction);return 0;
    });
}
int32_t SEVK_CALL sevk_get_resolution_stats(void* ctx,SevkResolutionStats* stats){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)return -1;return call_snapshot(ctx,[&](Renderer& r)->std::optional<int32_t>{*stats=r.snapshot.resolution;return 0;},[&](Renderer& r){r.collect();*stats=r.resolution_stats;return 0;});}
uint32_t SEVK_CALL sevk_guest_features(void){return 4294967295u;}
int32_t SEVK_CALL sevk_set_stage_bindings(void* ctx,const SevkStageBindings* rows,uint32_t count){
    return call(ctx,[&](Renderer& r){r.set_stage_bindings(rows,count);return 0;});
}
int32_t SEVK_CALL sevk_get_stage_binding_state(void* ctx,SevkStageBindingState* state){
    return call(ctx,[&](Renderer& r){r.stage_binding_state(state);return 0;});
}
int32_t SEVK_CALL sevk_guest_frame_environment(void* ctx,const SevkGuestEnvironment* environment){
    if(!environment || environment->size!=sizeof(*environment) || environment->abi_version!=SEVK_ABI_VERSION || environment->reserved || environment->pad)return -1;
    const auto copied=*environment;
    return call(ctx,[&](Renderer& r){r.guest_frame_environment(copied);return 0;});
}
int32_t SEVK_CALL sevk_guest_execution_status(void* ctx,SevkGuestExecutionStatus* status){
    if(!status || status->size!=sizeof(*status) || status->abi_version!=SEVK_ABI_VERSION)return -1;
    return call(ctx,[&](Renderer& r){
        auto found=r.guest.pipelines.find(status->pipeline);
        status->active=found!=r.guest.pipelines.end();status->executions=0;status->last_frame=UINT32_MAX;status->last_completion=0;
        check(vkGetSemaphoreCounterValue(r.device,r.timeline,&status->completed_timeline),"query guest execution completion");
        if(found!=r.guest.pipelines.end()){status->executions=found->second.executions;status->last_frame=found->second.last_frame;status->last_completion=found->second.last_completion;}
        return 0;
    });
}
int32_t SEVK_CALL sevk_guest_resource_command(void* ctx,const SevkGuestResourceCommand* command){
    if(!command || command->size!=sizeof(*command) || command->abi_version!=SEVK_ABI_VERSION)return -1;
    const auto copied=*command;
    return call(ctx,[&](Renderer& r){r.guest_resource_command(copied);return 0;});
}
int32_t SEVK_CALL sevk_guest_depth_views(void* ctx,const uint64_t* images,const float* matrices,uint32_t count){
    return call_deferrable(ctx,[images_copy=copy_in(images,count),matrices_copy=copy_in(matrices,uint64_t(count)*16),count](Renderer& r){auto* images=images_copy.get();auto* matrices=matrices_copy.get();r.guest_depth_views(images,matrices,count);return 0;});
}
uint32_t SEVK_CALL sevk_guest_depth_ready(void* ctx){
    uint32_t ready=0;call(ctx,[&](Renderer& r){ready=r.guest.depth_ready;return 0;});return ready;
}
int32_t SEVK_CALL sevk_guest_reset(void* ctx){return call(ctx,[](Renderer& r){r.guest.passes.clear();r.guest.has_early_owned=r.guest.has_global_clear=false;r.clear_stage_bindings();++r.guest.catalog_epoch;return 0;});}
uint32_t SEVK_CALL sevk_guest_catalog_mask(void* ctx){uint32_t mask=0;call(ctx,[&](Renderer& r){VkImageView v{};VkImage i{};uint32_t w{},h{};for(uint32_t b=0;b<3;b++)if(r.guest_catalog_view(0xFFFF00000000000Aull+b,v,i,w,h))mask|=1u<<b;return 0;});return mask;}
int32_t SEVK_CALL sevk_guest_create_pipeline(void* ctx,const void* vs,uint64_t vs_bytes,const char* vs_entry,const void* ps,uint64_t ps_bytes,const char* ps_entry,uint64_t* pipeline){
    return call(ctx,[&](Renderer& r){if(!pipeline)throw std::runtime_error("null guest pipeline");*pipeline=r.guest_create_pipeline(vs,vs_bytes,vs_entry,ps,ps_bytes,ps_entry);return 0;});
}
int32_t SEVK_CALL sevk_guest_create_image(void* ctx,uint64_t id,uint32_t width,uint32_t height,uint32_t depth,uint32_t format){
    return call(ctx,[&](Renderer& r){r.guest_create_image(id,width,height,depth,format);return 0;});
}
int32_t SEVK_CALL sevk_guest_update_image(void* ctx,uint64_t id,const void* data,uint64_t bytes){
    return call(ctx,[&](Renderer& r){r.guest_update_image(id,data,bytes);return 0;});
}
int32_t SEVK_CALL sevk_guest_submit(void* ctx,const SevkGuestPass* passes,uint32_t count){
    return call(ctx,[&](Renderer& r){r.guest_submit(passes,count);return 0;});
}
int32_t SEVK_CALL sevk_guest_create_pipeline2(void* ctx,const void* vs,uint64_t vs_bytes,const char* vs_entry,const void* ps,uint64_t ps_bytes,const char* ps_entry,uint32_t contract,uint64_t* pipeline){
    return call(ctx,[&](Renderer& r){if(!pipeline || contract<1 || contract>2)throw std::runtime_error("invalid extended guest pipeline");*pipeline=r.guest_create_pipeline(vs,vs_bytes,vs_entry,ps,ps_bytes,ps_entry,contract);return 0;});
}
int32_t SEVK_CALL sevk_guest_submit2(void* ctx,const SevkGuestPass2* passes,uint32_t count){
    return call(ctx,[&](Renderer& r){r.guest_submit2(passes,count);return 0;});
}
int32_t SEVK_CALL sevk_guest_readback(void* ctx,uint64_t id,void* rgba,uint64_t bytes){
    return call(ctx,[&](Renderer& r){r.guest_readback(id,rgba,bytes);return 0;});
}
int32_t SEVK_CALL sevk_guest_replay_now(void* ctx,uint32_t mark){
    return call(ctx,[&](Renderer& r){if(r.recording || mark<1 || mark>6)throw std::runtime_error("guest replay unavailable");r.immediate([&](VkCommandBuffer cmd){if(r.guest.has_global_clear)r.guest_replay(cmd,0);r.guest_replay(cmd,mark);});return 0;});
}
uint64_t SEVK_CALL sevk_guest_replay_count(void* ctx){uint64_t count=0;call(ctx,[&](Renderer& r){count=r.guest.replay_count;return 0;});return count;}
int32_t SEVK_CALL sevk_get_guest_frame(void* ctx, SevkGuestFrame* frame){if(!frame || frame->size < sizeof(SevkGuestFrame) || frame->abi_version != SEVK_ABI_VERSION)return -1;return call(ctx,[&](Renderer& r){r.guest_refresh_frame();*frame=r.guest.frame;frame->size=sizeof(SevkGuestFrame);frame->abi_version=SEVK_ABI_VERSION;return 0;});}
int32_t SEVK_CALL sevk_get_ray_capabilities(void* ctx, SevkRayCapabilities* caps){if(!caps)return -1;if(!ctx)ctx=active_context;return call(ctx,[&](Renderer& r){return r.get_ray_capabilities(caps);});}
int32_t SEVK_CALL sevk_set_ray_options(void* ctx, const SevkRayOptions* options){if(!options)return -1;if(!ctx)ctx=active_context;return call(ctx,[&](Renderer& r){return r.set_ray_options(*options);});}
int32_t SEVK_CALL sevk_set_postprocess(void* ctx,const SevkPostprocessConfig* config){if(!config)return -1;return call_deferrable(ctx,[config_copy=*config](Renderer& r){auto* config=&config_copy;r.set_postprocess(*config);return 0;});}
int32_t SEVK_CALL sevk_get_postprocess_stats(void* ctx,SevkPostprocessStats* stats){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)return -1;return call(ctx,[&](Renderer& r){*stats=r.post.stats;return 0;});}
int32_t SEVK_CALL sevk_queue_environment(void* ctx,const SevkEnvironmentDraw* draws,uint32_t count){return call_deferrable(ctx,[draws_copy=copy_in(draws,count),count](Renderer& r){auto* draws=draws_copy.get();r.queue_environment(draws,count);return 0;});}
int32_t SEVK_CALL sevk_get_environment_stats(void* ctx,SevkEnvironmentStats* stats){if(!stats || stats->size!=sizeof(*stats) || stats->abi_version!=SEVK_ABI_VERSION)return -1;return call(ctx,[&](Renderer& r){*stats=r.environment.stats;stats->luts=r.environment.luts.size();return 0;});}

int32_t SEVK_CALL sevk_upload_foliage(void* ctx,uint64_t id,const SevkFoliagePoint* points,uint32_t count){return call(ctx,[&](Renderer& r){if(count>4000000)throw std::runtime_error("foliage buffer exceeds point limit");uint32_t indices[3]{};r.upload_world_mesh(id,points,count,indices,3,16);r.world.meshes.at(id).index_count=count;return 0;});}
int32_t SEVK_CALL sevk_queue_foliage(void* ctx,const SevkFoliageDraw* draws,uint32_t count,const void* table,uint32_t bytes){return call_deferrable(ctx,[draws_copy=copy_in(draws,count),count,table_copy=copy_bytes(table,bytes),bytes](Renderer& r){auto* draws=draws_copy.get();const void* table=table_copy.get();r.queue_foliage(draws,count,table,bytes);return 0;});}

int32_t SEVK_CALL sevk_init_transparency(void* ctx,uint32_t id,const void* vs,uint64_t vb,const void* ps,uint64_t pb){return call(ctx,[&](Renderer& r){r.init_transparency(id,vs,vb,ps,pb);return 0;});}
int32_t SEVK_CALL sevk_queue_transparency(void* ctx,const SevkTransparentDraw* draws,uint32_t count){
    // Glass geometry carries constant and instance pointers; deferral owns copies.
    struct Owned {std::vector<SevkTransparentDraw> draws;std::vector<std::vector<uint8_t>> bytes;};auto owned=std::make_shared<Owned>();
    if(draws&&count<=8192){owned->draws.assign(draws,draws+count);owned->bytes.reserve(size_t(count)*2);for(auto& d:owned->draws){auto& g=d.geometry;
        if(g.object_constants&&g.object_bytes<=65536){auto* p=static_cast<const uint8_t*>(g.object_constants);g.object_constants=owned->bytes.emplace_back(p,p+g.object_bytes).data();}
        if(g.pipeline%5>=2&&g.instances&&g.instance_count&&g.instance_count<=1000000){auto* p=static_cast<const uint8_t*>(g.instances);g.instances=owned->bytes.emplace_back(p,p+uint64_t(g.instance_count)*64).data();}}}
    return call_deferrable(ctx,[owned,count,null=!draws](Renderer& r){r.queue_transparency(null?nullptr:owned->draws.data(),count);return 0;});
}
int32_t SEVK_CALL sevk_get_transparency_stats(void* ctx,SevkTransparencyStats* stats){return call(ctx,[&](Renderer& r){if(!stats||stats->size!=sizeof(*stats)||stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("invalid transparency stats");*stats=r.transparency.stats;return 0;});}

int32_t SEVK_CALL sevk_set_billboard_projections(void* ctx,const float* matrices){if(!matrices)return -1;return call_deferrable(ctx,[matrices_copy=copy_in(matrices,512)](Renderer& r){auto* matrices=matrices_copy.get();if(r.recording)throw std::runtime_error("billboard projection during frame");for(uint32_t i=0;i<512;++i)if(!std::isfinite(matrices[i]))throw std::runtime_error("nonfinite billboard projection");std::memcpy(r.transparency.custom_projections,matrices,2048);return 0;});}
int32_t SEVK_CALL sevk_queue_billboards(void* ctx,const SevkBillboardDraw* draws,uint32_t count){return call_deferrable(ctx,[draws_copy=copy_in(draws,count),count](Renderer& r){auto* draws=draws_copy.get();if(r.recording||count>32768||(!draws&&count))throw std::runtime_error("invalid billboard batch");for(uint32_t i=0;i<count;++i)if(draws[i].flags>31||draws[i].bucket>4||!r.textures.count(draws[i].texture))throw std::runtime_error("invalid billboard material/bucket");r.transparency.billboard_draws.clear();if(count)r.transparency.billboard_draws.assign(draws,draws+count);return 0;});}

int32_t SEVK_CALL sevk_init_particles(void* ctx,uint32_t stage,const void* spv,uint64_t bytes){return call(ctx,[&](Renderer& r){r.init_particles(stage,spv,bytes);return 0;});}
int32_t SEVK_CALL sevk_queue_particles(void* ctx,const void* data,uint32_t count,uint32_t bytes,uint64_t atlas,uint32_t reset){return call_deferrable(ctx,[data_copy=copy_bytes(data,bytes),count,bytes,atlas,reset](Renderer& r){const void* data=data_copy.get();if(r.recording||count>1024||bytes!=count*464||(!data&&count)||reset>1||(count&&!r.world.arrays.count(atlas)))throw std::runtime_error("invalid particle emitter batch/atlas");r.particles.packet.clear();if(bytes){auto* p=static_cast<const uint8_t*>(data);r.particles.packet.assign(p,p+bytes);}r.particles.count=count;r.particles.atlas=atlas;r.particles.reset|=reset!=0;r.particles.queued=count!=0||r.particles.initialized&&(atlas!=0||reset!=0);return 0;});}
int32_t SEVK_CALL sevk_get_particle_stats(void* ctx,SevkParticleStats* stats){return call(ctx,[&](Renderer& r){if(!stats||stats->size!=sizeof(*stats)||stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("invalid particle stats");*stats=r.particles.stats;return 0;});}
int32_t SEVK_CALL sevk_readback_particle_buffer(void* ctx,const SevkParticleReadback* request){if(!request)return -1;return call(ctx,[&](Renderer& r){r.readback_particle_buffer(*request);return 0;});}

int32_t SEVK_CALL sevk_init_decals(void* ctx,uint32_t role,const void* vertex,uint64_t vb,const void* pixel,uint64_t pb){return call(ctx,[&](Renderer& r){r.init_decals(role,vertex,vb,pixel,pb);return 0;});}
int32_t SEVK_CALL sevk_queue_decals(void* ctx,const SevkDecalDraw* draws,uint32_t count){return call_deferrable(ctx,[draws_copy=copy_in(draws,count),count](Renderer& r){auto* draws=draws_copy.get();if(r.recording||count>16384||(!draws&&count))throw std::runtime_error("invalid decal batch");for(uint32_t i=0;i<count;++i){auto& d=draws[i];if(d.size!=sizeof(d)||d.role>8||!r.decals.pipelines[d.role])throw std::runtime_error("invalid decal role/layout");for(uint64_t texture:{d.alpha,d.color,d.normal,d.extensions})if(!r.textures.count(texture))throw std::runtime_error("missing decal texture");}r.decals.draws.clear();if(count)r.decals.draws.assign(draws,draws+count);return 0;});}

int32_t SEVK_CALL sevk_queue_flares(void* ctx,const float* matrix,const SevkFlareQuery* queries,uint32_t count){return call(ctx,[&](Renderer& r){if(r.recording||count>4096||(!queries&&count)||!matrix)throw std::runtime_error("invalid flare query packet");for(uint32_t i=0;i<count;++i)if(!std::isfinite(queries[i].position[0])||!std::isfinite(queries[i].position[1])||!std::isfinite(queries[i].position[2])||!std::isfinite(queries[i].radius)||queries[i].radius<0)throw std::runtime_error("nonfinite flare query");std::memcpy(r.flares.projection,matrix,64);r.flares.raster_packet.clear();r.flares.packet.clear();if(count)r.flares.packet.assign(queries,queries+count);return 0;});}
int32_t SEVK_CALL sevk_queue_flare_raster(void* ctx,const SevkFlareRasterQuery* queries,uint32_t count){return call_deferrable(ctx,[queries_copy=copy_in(queries,count),count](Renderer& r){auto* queries=queries_copy.get();
    if(r.recording||count>4096||(!queries&&count))throw std::runtime_error("invalid raster flare packet");
    for(uint32_t i=0;i<count;++i){const auto& q=queries[i];for(float v:q.center)if(!std::isfinite(v))throw std::runtime_error("nonfinite raster flare center");for(float v:q.half_extent)if(!std::isfinite(v)||v<0)throw std::runtime_error("invalid raster flare extent");if(!std::isfinite(q.area)||q.area<=0)throw std::runtime_error("invalid raster flare normalization");}
    r.flares.packet.clear();r.flares.raster_packet.clear();if(count)r.flares.raster_packet.assign(queries,queries+count);return 0;
});}
int32_t SEVK_CALL sevk_get_flare_results(void* ctx,SevkFlareResult* results,uint32_t capacity,uint32_t* count){if(!count||(!results&&capacity)||capacity>4096)return -1;
    auto copy=[&](const auto& completed){*count=std::min(capacity,uint32_t(completed.size()));if(*count)std::memcpy(results,completed.data(),*count*sizeof(*results));};
    return call_snapshot(ctx,[&](Renderer& r)->std::optional<int32_t>{copy(r.snapshot.flares);return 0;},[&](Renderer& r){copy(r.flares.completed);return 0;});}

int32_t SEVK_CALL sevk_set_parallel_cascades(void* ctx,uint32_t enabled){return call(ctx,[&](Renderer& r){if(r.recording)throw std::runtime_error("cascade recording policy cannot change while recording");r.world.parallel_cascades=enabled!=0;return 0;});}
int32_t SEVK_CALL sevk_get_cascade_record_stats(void* ctx,SevkCascadeRecordStats* stats){return call(ctx,[&](Renderer& r){if(!stats || stats->size!=sizeof(*stats)||stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("cascade record stats ABI mismatch");*stats={sizeof(*stats),SEVK_ABI_VERSION,r.world.parallel_cascades?1u:0u,r.world.cascade_tasks,r.world.cascade_task_commands};return 0;});}

int32_t SEVK_CALL sevk_init_light_tiles(void* ctx,const void* spirv,uint64_t bytes){return call(ctx,[&](Renderer& r){r.init_light_tiles(spirv,bytes);return 0;});}
int32_t SEVK_CALL sevk_set_light_tiles(void* ctx,uint32_t enabled){return call(ctx,[&](Renderer& r){if(r.recording)throw std::runtime_error("light tile policy cannot change while recording");bool value=enabled!=0;if(value!=r.light_tiles.requested){r.light_tiles.requested=value;++r.light_tiles.epoch;r.light_tiles.stats.have_result=0;r.light_tiles.stats.completed_frame=r.light_tiles.stats.assignments=r.light_tiles.stats.full_assignments=0;}return 0;});}
int32_t SEVK_CALL sevk_get_light_tile_stats(void* ctx,SevkLightTileStats* stats){return call(ctx,[&](Renderer& r){if(!stats||stats->size!=sizeof(*stats)||stats->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("light tile stats ABI mismatch");*stats=r.light_tiles.stats;return 0;});}
#include "native_pass_exports.inc"
#include "old_cells_exports.inc"
int32_t SEVK_CALL sevk_critical_begin(void* ctx,uint32_t stride){return call(ctx,[&](Renderer& r){if(r.recording||stride>1024)throw std::runtime_error("invalid critical sample stride");r.critical.begin(stride);return 0;});}
int32_t SEVK_CALL sevk_critical_profile(void* ctx,SevkCriticalProfile* profile){return call(ctx,[&](Renderer& r){if(!profile||profile->size!=sizeof(*profile)||profile->abi_version!=SEVK_ABI_VERSION)throw std::runtime_error("critical profile ABI mismatch");*profile=r.critical.totals;return 0;});}
