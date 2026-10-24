// Tenant compute session: Anomaly RayScene v1 tenants (SE-RT) on Vulkan, audit P3 step 4.
// DX11 tenants own their device resources and record on the immediate context inside the
// frame callback. Here the tenant owns buffers, images and pipelines by id and records a
// command list; it runs after deferred lighting, before AfterLighting guest passes, with
// renderer HDR as the only renderer target (DX11 composes into LBuffer at the same point).
static_assert(sizeof(SevkTenantCommand)==128,"tenant command is 128 bytes");
static_assert(sizeof(SevkTenantBinding)==32,"tenant binding is 32 bytes");
// Renderer images a tenant samples besides the guest sentinels, as DX11 frame[7][9..12] supplies them.
static constexpr uint64_t kTenantSunShadows=0xFFFF000000000020ull;
static constexpr uint64_t kTenantCloseCube=0xFFFF000000000021ull;
static constexpr uint64_t kTenantFarCube=0xFFFF000000000022ull;
static constexpr uint64_t kTenantBrdf=0xFFFF000000000023ull;
// Names the world frame constants in frame[7][8]; tenants bind them with kind 6, not by id.
static constexpr uint64_t kTenantFrameConstants=0xFFFF000000000024ull;
struct TenantImage{Image image{};VkFormat format{};uint32_t width{},height{},layers{1},bpp{4},component{};bool array{},storage{},ready{};std::unordered_map<uint32_t,VkImageView> targets;};
struct TenantBinding{uint32_t binding{};VkDescriptorType type{};uint32_t shape{},component{},size{};};
struct TenantPipeline{VkPipeline pipeline{};VkPipelineLayout layout{};VkDescriptorSetLayout set{};std::vector<TenantBinding> bindings;bool compute{};uint32_t targets{};VkFormat formats[3]{};};
// Ray-query acceleration structures; storage grows on build and is reused while it fits.
struct TenantAccel{VkAccelerationStructureKHR handle{};Buffer storage{};VkDeviceSize size{};bool top{};};
struct TenantRetired{uint64_t completion{};Buffer buffer{};TenantImage image{};TenantPipeline pipeline{};TenantAccel accel{};};
struct TenantState{
    std::unordered_map<uint64_t,Buffer> buffers;std::unordered_map<uint64_t,TenantImage> images;std::unordered_map<uint64_t,TenantPipeline> pipelines;
    std::unordered_map<uint64_t,TenantAccel> accels;Buffer scratch[2]{};TenantAccel dummy_tlas{};
    // GPU time of each slot's tenant list: two timestamps per slot, read once the frame completes.
    VkQueryPool timing{};bool timing_valid[2]{};
    std::vector<uint64_t> fresh_buffers;std::vector<TenantRetired> retired;uint64_t next_id{1};
    std::vector<SevkTenantCommand> commands;std::vector<SevkTenantBinding> bindings;std::vector<uint8_t> blob;
    VkDescriptorPool pools[2]{};Buffer stream[2]{};VkSampler samplers[3]{};
    std::unordered_map<uint32_t,Image> dummies;Buffer dummy_buffer{};bool dummy_ready{},without_format{};
    SevkTenantStats stats{};
} tenant;

static uint32_t tenant_texel(VkFormat format,uint32_t& component){
    component=0;
    switch(format){
    case VK_FORMAT_R8G8B8A8_UNORM:case VK_FORMAT_R16G16_SFLOAT:case VK_FORMAT_R32_SFLOAT:return 4;
    case VK_FORMAT_R16_SFLOAT:return 2;
    case VK_FORMAT_R16G16B16A16_SFLOAT:case VK_FORMAT_R32G32_SFLOAT:return 8;
    case VK_FORMAT_R32G32B32A32_SFLOAT:return 16;
    case VK_FORMAT_R8G8B8A8_UINT:case VK_FORMAT_R32_UINT:component=1;return 4;
    case VK_FORMAT_R32G32_UINT:component=1;return 8;
    case VK_FORMAT_R32G32B32A32_UINT:component=1;return 16;
    default:return 0;
    }
}
void tenant_barrier(VkCommandBuffer cmd){
    VkMemoryBarrier2 m{VK_STRUCTURE_TYPE_MEMORY_BARRIER_2};m.srcStageMask=m.dstStageMask=VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
    m.srcAccessMask=VK_ACCESS_2_MEMORY_WRITE_BIT;m.dstAccessMask=VK_ACCESS_2_MEMORY_READ_BIT|VK_ACCESS_2_MEMORY_WRITE_BIT;
    VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.memoryBarrierCount=1;d.pMemoryBarriers=&m;vkCmdPipelineBarrier2(cmd,&d);
}
uint64_t tenant_retire_completion()const{return next_completion+(recording?1:0);}
void tenant_destroy_image(TenantImage& image){for(auto& view:image.targets)vkDestroyImageView(device,view.second,nullptr);image.targets.clear();destroy_image(image.image);}
void tenant_destroy_pipeline(TenantPipeline& p){
    if(p.pipeline)vkDestroyPipeline(device,p.pipeline,nullptr);if(p.layout)vkDestroyPipelineLayout(device,p.layout,nullptr);
    if(p.set)vkDestroyDescriptorSetLayout(device,p.set,nullptr);p={};
}
void tenant_destroy_accel(TenantAccel& a){if(a.handle && vkDestroyAccelerationStructureKHR)vkDestroyAccelerationStructureKHR(device,a.handle,nullptr);destroy_buffer(a.storage);a={};}
void tenant_destroy_retired(TenantRetired& r){destroy_buffer(r.buffer);tenant_destroy_image(r.image);tenant_destroy_pipeline(r.pipeline);tenant_destroy_accel(r.accel);}
void tenant_sample_timing(){
    for(uint32_t i=0;i<2;++i){if(!tenant.timing || !tenant.timing_valid[i])continue;uint64_t values[4]{};
        auto result=vkGetQueryPoolResults(device,tenant.timing,i*2,2,sizeof(values),values,16,VK_QUERY_RESULT_64_BIT|VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);
        if(result==VK_NOT_READY || !values[1] || !values[3])continue;check(result,"read tenant timestamps");
        uint64_t mask=timestamp_bits==64?~uint64_t(0):(uint64_t(1)<<timestamp_bits)-1;
        tenant.stats.gpu_ms=double((values[2]-values[0])&mask)*timestamp_period/1000000.0;++tenant.stats.measurements;tenant.timing_valid[i]=false;}
}
void tenant_collect(uint64_t complete){
    tenant_sample_timing();
    for(auto it=tenant.retired.begin();it!=tenant.retired.end();)if(it->completion<=complete){tenant_destroy_retired(*it);it=tenant.retired.erase(it);}else ++it;
}
void cleanup_tenant(){
    for(auto& r:tenant.retired)tenant_destroy_retired(r);
    for(auto& b:tenant.buffers)destroy_buffer(b.second);for(auto& i:tenant.images)tenant_destroy_image(i.second);for(auto& p:tenant.pipelines)tenant_destroy_pipeline(p.second);
    for(auto& d:tenant.dummies)destroy_image(d.second);destroy_buffer(tenant.dummy_buffer);
    for(auto& a:tenant.accels)tenant_destroy_accel(a.second);tenant_destroy_accel(tenant.dummy_tlas);
    for(uint32_t i=0;i<2;++i){if(tenant.pools[i])vkDestroyDescriptorPool(device,tenant.pools[i],nullptr);destroy_buffer(tenant.stream[i]);destroy_buffer(tenant.scratch[i]);}
    for(auto s:tenant.samplers)if(s)vkDestroySampler(device,s,nullptr);
    if(tenant.timing)vkDestroyQueryPool(device,tenant.timing,nullptr);
    bool without_format=tenant.without_format;tenant=TenantState{};tenant.without_format=without_format;
}
void tenant_ensure(){
    if(tenant.samplers[0])return;
    for(uint32_t i=0;i<3;++i){
        VkSamplerCreateInfo s{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO};s.magFilter=s.minFilter=i==2?VK_FILTER_NEAREST:VK_FILTER_LINEAR;
        s.mipmapMode=i==2?VK_SAMPLER_MIPMAP_MODE_NEAREST:VK_SAMPLER_MIPMAP_MODE_LINEAR;
        s.addressModeU=s.addressModeV=s.addressModeW=i==0?VK_SAMPLER_ADDRESS_MODE_REPEAT:VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;s.maxLod=VK_LOD_CLAMP_NONE;
        check(vkCreateSampler(device,&s,nullptr,&tenant.samplers[i]),"create tenant sampler");
    }
    for(uint32_t i=0;i<2;++i){
        VkDescriptorPoolSize sizes[]{{VK_DESCRIPTOR_TYPE_SAMPLER,4096},{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,32768},{VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,8192},
            {VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,16384},{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,4096},{VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR,1024}};
        VkDescriptorPoolCreateInfo p{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};p.maxSets=2048;p.poolSizeCount=ray.supported?6:5;p.pPoolSizes=sizes;
        check(vkCreateDescriptorPool(device,&p,nullptr,&tenant.pools[i]),"create tenant descriptor pool");
    }
    tenant.dummy_buffer=make_buffer(65536,VK_BUFFER_USAGE_STORAGE_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT,false);
    if(timestamp_bits){VkQueryPoolCreateInfo qp{VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO};qp.queryType=VK_QUERY_TYPE_TIMESTAMP;qp.queryCount=4;
        check(vkCreateQueryPool(device,&qp,nullptr,&tenant.timing),"create tenant timestamp pool");}
}

uint64_t tenant_create_buffer(uint64_t bytes){
    if(!bytes || bytes>(1ull<<31) || bytes%4)throw std::runtime_error("tenant buffer size must be a nonzero multiple of 4 up to 2 GiB");
    tenant_ensure();uint64_t id=tenant.next_id++;
    tenant.buffers[id]=make_buffer(bytes,tenant_buffer_usage(VK_BUFFER_USAGE_STORAGE_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_SRC_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT),false);
    tenant.fresh_buffers.push_back(id);return id;
}
// With ray query, tenant buffers can also be acceleration-structure build inputs.
VkBufferUsageFlags tenant_buffer_usage(VkBufferUsageFlags usage)const{
    return ray.supported?usage|VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT|VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT_KHR:usage;
}
uint64_t tenant_create_accel(uint32_t top){
    if(!ray.supported)throw std::runtime_error("tenant acceleration structures need ray query support");
    if(top>1)throw std::runtime_error("tenant acceleration structure kind is 0 bottom or 1 top");
    tenant_ensure();uint64_t id=tenant.next_id++;TenantAccel a{};a.top=top!=0;tenant.accels[id]=a;return id;
}
uint64_t tenant_create_image(uint32_t width,uint32_t height,uint32_t layers,uint32_t format_value,uint32_t flags){
    auto format=VkFormat(format_value);uint32_t component=0,bpp=tenant_texel(format,component);
    if(!bpp)throw std::runtime_error("unsupported tenant image format "+std::to_string(format_value));
    if(!width || !height || width>16384 || height>16384 || !layers || layers>2048 || (flags&~1u))throw std::runtime_error("invalid tenant image shape");
    VkFormatProperties properties{};vkGetPhysicalDeviceFormatProperties(physical,format,&properties);auto features=properties.optimalTilingFeatures;
    if(!(features&VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT))throw std::runtime_error("tenant image format is not sampleable");
    VkImageUsageFlags usage=VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    bool storage=(features&VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT)!=0;if(storage)usage|=VK_IMAGE_USAGE_STORAGE_BIT;
    if(features&VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BIT)usage|=VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    tenant_ensure();TenantImage image{};bool array=(flags&1) || layers>1;
    image.image=make_image(width,height,format,usage,VK_IMAGE_ASPECT_COLOR_BIT,1,layers,array);
    image.format=format;image.width=width;image.height=height;image.layers=layers;image.bpp=bpp;image.component=component;image.array=array;image.storage=storage;
    uint64_t id=tenant.next_id++;tenant.images[id]=std::move(image);return id;
}
// DXC types RWTexture2D<float4> as Rgba32f. DX11 binds any compatible UAV format there, so storage
// images are retyped Unknown (shaderStorageImageRead/WriteWithoutFormat) on the executable copy.
// Two images differing only by format would become duplicate types, so such a group keeps DXC's
// formats (inferred from the element type, e.g. uint -> R32ui, uint4 -> Rgba32ui).
std::vector<uint32_t> tenant_spirv(const void* data,uint64_t bytes){
    if(!data || bytes<20 || bytes%4 || bytes>16*1024*1024)throw std::runtime_error("invalid tenant SPIR-V size");
    std::vector<uint32_t> words(bytes/4);std::memcpy(words.data(),data,bytes);
    if(words[0]!=0x07230203u)throw std::runtime_error("invalid tenant SPIR-V header");
    static constexpr uint32_t allowed[]{1,25,43,44,46,47,49,50,51,55,56,5379};
    size_t first=0;bool storage=false,read=false,write=false;std::vector<size_t> images;
    for(size_t i=5;i<words.size();){uint32_t n=words[i]>>16,op=words[i]&65535;
        if(!n || n>words.size()-i)throw std::runtime_error("malformed tenant SPIR-V");
        // Inline ray tracing is the one extension a tenant may use, on ray-query devices.
        if(op==10){std::string name(reinterpret_cast<const char*>(&words[i+1]),strnlen(reinterpret_cast<const char*>(&words[i+1]),(n-1)*4));
            if(name!="SPV_KHR_ray_query" || !ray.supported)throw std::runtime_error("tenant shader requests SPIR-V extension "+name);}
        if(op==17 && n==2){uint32_t c=words[i+1];bool query=c==4472 && ray.supported;
            if(!query && std::find(std::begin(allowed),std::end(allowed),c)==std::end(allowed))throw std::runtime_error("tenant shader requests capability "+std::to_string(c));
            if(!first)first=i;read|=c==55;write|=c==56;}
        if(op==25 && n>=9)images.push_back(i);
        i+=n;}
    // An image's shape with the format cleared: everything after its result id.
    auto shape=[&](size_t i){std::vector<uint32_t> s(words.begin()+i+2,words.begin()+i+(words[i]>>16));s[6]=0;s.insert(s.begin(),words[i]>>16);return s;};
    for(size_t i:images){if(words[i+7]!=2 || words[i+3]==5 || words[i+8]==0)continue;auto own=shape(i);
        bool unique=std::none_of(images.begin(),images.end(),[&](size_t j){return j!=i && shape(j)==own;});
        if(unique){words[i+8]=0;storage=true;}}
    for(size_t i:images)if(words[i+7]==2 && words[i+3]!=5 && words[i+8]==0)storage=true;
    if(storage){
        if(!tenant.without_format)throw std::runtime_error("tenant storage images need shaderStorageImageRead/WriteWithoutFormat");
        if(!first)throw std::runtime_error("tenant SPIR-V declares no capabilities");
        std::vector<uint32_t> added;if(!read){added.push_back((2u<<16)|17u);added.push_back(55);}if(!write){added.push_back((2u<<16)|17u);added.push_back(56);}
        words.insert(words.begin()+first,added.begin(),added.end());
    }
    return words;
}
VkShaderModule tenant_module(const std::vector<uint32_t>& words,VkShaderStageFlagBits stage,const char* entry,std::vector<TenantBinding>& bindings,uint32_t* local=nullptr){
    if(!entry || !*entry)throw std::runtime_error("tenant shader entry point missing");
    SpvReflectShaderModule r{};if(spvReflectCreateShaderModule(words.size()*4,words.data(),&r)!=SPV_REFLECT_RESULT_SUCCESS)throw std::runtime_error("tenant reflection failed");
    try{
        auto* point=spvReflectGetEntryPoint(&r,entry);
        if(!point || uint32_t(point->shader_stage)!=uint32_t(stage))throw std::runtime_error(std::string("tenant shader lacks entry point ")+entry+" for its stage");
        if(local){local[0]=point->local_size.x;local[1]=point->local_size.y;local[2]=point->local_size.z;}
        if(r.push_constant_block_count)throw std::runtime_error("tenant shaders bind no push constants");
        for(uint32_t i=0;i<r.descriptor_binding_count;++i){auto& b=r.descriptor_bindings[i];auto type=VkDescriptorType(b.descriptor_type);
            if(b.set!=0 || b.count!=1)throw std::runtime_error("tenant shaders use single descriptors in set 0");
            if(type!=VK_DESCRIPTOR_TYPE_SAMPLER && type!=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE && type!=VK_DESCRIPTOR_TYPE_STORAGE_IMAGE && type!=VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER && type!=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER
                && !(type==VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR && ray.supported))
                throw std::runtime_error("unsupported tenant descriptor type at binding "+std::to_string(b.binding));
            TenantBinding t{b.binding,type};
            if(type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE || type==VK_DESCRIPTOR_TYPE_STORAGE_IMAGE){
                if((b.image.dim!=SpvDim2D && !(b.image.dim==SpvDimCube && !b.image.arrayed && type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE)) || b.image.ms)
                    throw std::runtime_error("tenant images are 2D, 2D arrays or sampled cubes (binding "+std::to_string(b.binding)+")");
                t.shape=b.image.dim==SpvDimCube?2:b.image.arrayed?1:0;
                auto* d=b.type_description;t.component=d && (d->type_flags&SPV_REFLECT_TYPE_FLAG_INT)?(d->traits.numeric.scalar.signedness?2:1):0;
            }
            if(type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER)t.size=std::max(b.block.padded_size,b.block.size);
            auto found=std::find_if(bindings.begin(),bindings.end(),[&](const TenantBinding& x){return x.binding==t.binding;});
            if(found==bindings.end())bindings.push_back(t);
            else if(found->type!=t.type || found->shape!=t.shape || found->component!=t.component)throw std::runtime_error("tenant stages disagree on binding "+std::to_string(t.binding));
            else found->size=std::max(found->size,t.size);
        }
    }catch(...){spvReflectDestroyShaderModule(&r);throw;}
    spvReflectDestroyShaderModule(&r);
    VkShaderModuleCreateInfo info{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};info.codeSize=words.size()*4;info.pCode=words.data();
    VkShaderModule module{};check(vkCreateShaderModule(device,&info,nullptr,&module),"create tenant shader");return module;
}
void tenant_layout(TenantPipeline& p,VkShaderStageFlags stages){
    std::vector<VkDescriptorSetLayoutBinding> b;for(const auto& t:p.bindings)b.push_back({t.binding,t.type,1,stages,nullptr});
    VkDescriptorSetLayoutCreateInfo info{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};info.bindingCount=uint32_t(b.size());info.pBindings=b.data();
    check(vkCreateDescriptorSetLayout(device,&info,nullptr,&p.set),"create tenant set layout");
    VkPipelineLayoutCreateInfo layout{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};layout.setLayoutCount=1;layout.pSetLayouts=&p.set;
    check(vkCreatePipelineLayout(device,&layout,nullptr,&p.layout),"create tenant pipeline layout");
}
uint64_t tenant_create_compute(const void* spirv,uint64_t bytes,const char* entry){
    tenant_ensure();auto words=tenant_spirv(spirv,bytes);TenantPipeline p{};p.compute=true;uint32_t local[3]{};
    VkShaderModule module=tenant_module(words,VK_SHADER_STAGE_COMPUTE_BIT,entry,p.bindings,local);
    try{
        if(!local[0] || !local[1] || !local[2])throw std::runtime_error("tenant compute shader lacks a workgroup size");
        tenant_layout(p,VK_SHADER_STAGE_COMPUTE_BIT);
        VkComputePipelineCreateInfo info{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};info.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,module,entry,nullptr};
        info.layout=p.layout;check(vkCreateComputePipelines(device,pipeline_cache,1,&info,nullptr,&p.pipeline),"create tenant compute pipeline");
    }catch(...){vkDestroyShaderModule(device,module,nullptr);tenant_destroy_pipeline(p);throw;}
    vkDestroyShaderModule(device,module,nullptr);uint64_t id=tenant.next_id++;tenant.pipelines[id]=std::move(p);return id;
}
uint64_t tenant_create_raster(const void* vs,uint64_t vs_bytes,const char* vs_entry,const void* ps,uint64_t ps_bytes,const char* ps_entry,uint32_t count,const uint32_t* formats){
    if(!count || count>3 || !formats)throw std::runtime_error("tenant raster pipelines write one to three targets");
    tenant_ensure();TenantPipeline p{};p.targets=count;
    for(uint32_t i=0;i<count;++i){uint32_t component=0;p.formats[i]=formats[i]?VkFormat(formats[i]):world.hdr_format;
        if(formats[i] && !tenant_texel(p.formats[i],component))throw std::runtime_error("unsupported tenant target format");}
    auto vw=tenant_spirv(vs,vs_bytes),pw=tenant_spirv(ps,ps_bytes);
    VkShaderModule vm=tenant_module(vw,VK_SHADER_STAGE_VERTEX_BIT,vs_entry,p.bindings),pm{};
    try{
        pm=tenant_module(pw,VK_SHADER_STAGE_FRAGMENT_BIT,ps_entry,p.bindings);
        tenant_layout(p,VK_SHADER_STAGE_VERTEX_BIT|VK_SHADER_STAGE_FRAGMENT_BIT);
        VkPipelineShaderStageCreateInfo stages[2]{{VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_VERTEX_BIT,vm,vs_entry,nullptr},
            {VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_FRAGMENT_BIT,pm,ps_entry,nullptr}};
        VkPipelineVertexInputStateCreateInfo input{VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO};
        VkPipelineInputAssemblyStateCreateInfo assembly{VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO};assembly.topology=VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
        VkPipelineViewportStateCreateInfo viewport{VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO};viewport.viewportCount=viewport.scissorCount=1;
        VkPipelineRasterizationStateCreateInfo raster{VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO};raster.polygonMode=VK_POLYGON_MODE_FILL;raster.cullMode=VK_CULL_MODE_NONE;raster.lineWidth=1;
        VkPipelineMultisampleStateCreateInfo ms{VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO};ms.rasterizationSamples=VK_SAMPLE_COUNT_1_BIT;
        VkPipelineDepthStencilStateCreateInfo depth{VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO};
        VkPipelineColorBlendAttachmentState attachments[3]{};for(auto& a:attachments)a.colorWriteMask=15;
        VkPipelineColorBlendStateCreateInfo blend{VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO};blend.attachmentCount=count;blend.pAttachments=attachments;
        VkDynamicState states[]{VK_DYNAMIC_STATE_VIEWPORT,VK_DYNAMIC_STATE_SCISSOR};
        VkPipelineDynamicStateCreateInfo dynamic{VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO};dynamic.dynamicStateCount=2;dynamic.pDynamicStates=states;
        VkPipelineRenderingCreateInfo rendering{VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO};rendering.colorAttachmentCount=count;rendering.pColorAttachmentFormats=p.formats;
        VkGraphicsPipelineCreateInfo info{VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO,&rendering};info.stageCount=2;info.pStages=stages;info.pVertexInputState=&input;
        info.pInputAssemblyState=&assembly;info.pViewportState=&viewport;info.pRasterizationState=&raster;info.pMultisampleState=&ms;info.pDepthStencilState=&depth;
        info.pColorBlendState=&blend;info.pDynamicState=&dynamic;info.layout=p.layout;
        check(vkCreateGraphicsPipelines(device,pipeline_cache,1,&info,nullptr,&p.pipeline),"create tenant raster pipeline");
    }catch(...){vkDestroyShaderModule(device,vm,nullptr);if(pm)vkDestroyShaderModule(device,pm,nullptr);tenant_destroy_pipeline(p);throw;}
    vkDestroyShaderModule(device,vm,nullptr);vkDestroyShaderModule(device,pm,nullptr);uint64_t id=tenant.next_id++;tenant.pipelines[id]=std::move(p);return id;
}
// TLAS instance record in the blob: VkTransformMatrixKHR rows, custom index (24 bits), mask, flags, BLAS id.
struct TenantInstance{float transform[12];uint32_t custom,mask,flags,pad;uint64_t blas,reserved;};
static_assert(sizeof(TenantInstance)==80,"tenant TLAS instance record is 80 bytes");
TenantInstance tenant_instance(const SevkTenantCommand& c,uint32_t k)const{TenantInstance r{};std::memcpy(&r,tenant.blob.data()+c.blob_offset+uint64_t(k)*80,80);return r;}
bool tenant_pending_uses(uint64_t id)const{
    for(const auto& c:tenant.commands){if(c.pipeline==id || c.source==id)return true;for(uint32_t i=0;i<c.target_count && i<3;++i)if(c.targets[i]==id)return true;}
    for(const auto& b:tenant.bindings)if((b.kind==1 || b.kind==2 || b.kind==8) && b.id==id)return true;
    for(const auto& c:tenant.commands)if(c.op==10)for(uint32_t k=0;k<c.x;++k)if(tenant_instance(c,k).blas==id)return true;
    return false;
}
void tenant_destroy(uint64_t id){
    if(tenant_pending_uses(id))throw std::runtime_error("tenant resource is used by this frame's commands");
    TenantRetired r{};r.completion=tenant_retire_completion();
    if(auto b=tenant.buffers.find(id);b!=tenant.buffers.end()){r.buffer=b->second;tenant.buffers.erase(b);}
    else if(auto i=tenant.images.find(id);i!=tenant.images.end()){r.image=std::move(i->second);tenant.images.erase(i);}
    else if(auto p=tenant.pipelines.find(id);p!=tenant.pipelines.end()){r.pipeline=std::move(p->second);tenant.pipelines.erase(p);}
    else if(auto a=tenant.accels.find(id);a!=tenant.accels.end()){r.accel=a->second;tenant.accels.erase(a);}
    else throw std::runtime_error("unknown tenant id");
    tenant.retired.push_back(std::move(r));
}
// Record-time validation keeps execution free of id and range failures; shader/binding type
// mismatches are found at execution and skip that command (stats.rejected).
void tenant_record(const SevkTenantCommand* commands,uint32_t count,const SevkTenantBinding* bindings,uint32_t binding_count,const void* blob,uint64_t blob_bytes){
    if(!recording)throw std::runtime_error("tenant commands are recorded between begin and end frame");
    if((count && !commands) || (binding_count && !bindings) || (blob_bytes && !blob) || count>4096 || binding_count>65536 || blob_bytes>(256ull<<20))throw std::runtime_error("invalid tenant command batch");
    uint64_t blob_base=tenant.blob.size(),binding_base=tenant.bindings.size();
    if(tenant.commands.size()+count>16384 || blob_base+blob_bytes>(512ull<<20))throw std::runtime_error("tenant frame command budget exceeded");
    auto buffer=[&](uint64_t id)->const Buffer&{auto f=tenant.buffers.find(id);if(f==tenant.buffers.end())throw std::runtime_error("unknown tenant buffer");return f->second;};
    auto image=[&](uint64_t id)->const TenantImage&{auto f=tenant.images.find(id);if(f==tenant.images.end())throw std::runtime_error("unknown tenant image");return f->second;};
    for(uint32_t i=0;i<binding_count;++i){const auto& b=bindings[i];
        if(b.kind>8 || (b.kind==8 && (!tenant.accels.count(b.id) || !tenant.accels.at(b.id).top)) || (b.kind==1 && !tenant.images.count(b.id)) || (b.kind==2 && !tenant.buffers.count(b.id)) || (b.kind==3 && (b.offset+b.bytes>blob_bytes || !b.bytes || b.bytes>65536)) || (b.kind==7 && b.id>2))
            throw std::runtime_error("invalid tenant binding "+std::to_string(i));
        if(b.kind==2 && b.offset+b.bytes>buffer(b.id).bytes)throw std::runtime_error("tenant buffer binding range exceeds the buffer");}
    for(uint32_t i=0;i<count;++i){const auto& c=commands[i];
        if(uint64_t(c.binding_first)+c.binding_count>binding_count || c.target_count>3)throw std::runtime_error("tenant command binding range");
        if(c.op==1 || c.op==2){auto p=tenant.pipelines.find(c.pipeline);if(p==tenant.pipelines.end() || p->second.compute!=(c.op==1))throw std::runtime_error("tenant command pipeline kind");
            if(c.op==1 && (!c.x || !c.y || !c.z || c.x>65535 || c.y>65535 || c.z>65535))throw std::runtime_error("tenant dispatch size");
            if(c.op==2){if(c.target_count!=p->second.targets || !c.x || !c.y)throw std::runtime_error("tenant draw targets/size");
                for(uint32_t t=0;t<c.target_count;++t){if(c.targets[t]==kGuestHdr)continue;const auto& target=image(c.targets[t]);
                    if(c.layers[t]>=target.layers || c.x>target.width || c.y>target.height || target.format!=p->second.formats[t])throw std::runtime_error("tenant draw target shape/format");}}}
        else if(c.op==3 || c.op==7){const auto& dst=buffer(c.targets[0]);uint64_t bytes=c.bytes?c.bytes:dst.bytes-std::min(dst.bytes,c.offset);
            if(c.offset%4 || bytes%4 || !bytes || c.offset+bytes>dst.bytes || (c.op==7 && (!c.bytes || c.blob_offset+c.bytes>blob_bytes)))throw std::runtime_error("tenant buffer fill/upload range");}
        else if(c.op==4)image(c.targets[0]);
        else if(c.op==5){const auto& src=buffer(c.source);const auto& dst=buffer(c.targets[0]);if(!c.bytes || c.offset+c.bytes>dst.bytes || c.bytes>src.bytes)throw std::runtime_error("tenant buffer copy range");}
        else if(c.op==6){const auto& src=image(c.source);const auto& dst=image(c.targets[0]);if(src.width!=dst.width || src.height!=dst.height || src.layers!=dst.layers || src.bpp!=dst.bpp || c.source==c.targets[0])throw std::runtime_error("tenant image copy shape");}
        else if(c.op==9){auto a=tenant.accels.find(c.targets[0]);if(a==tenant.accels.end() || a->second.top)throw std::runtime_error("tenant BLAS build needs a bottom-level id");
            const auto& src=buffer(c.source);
            if(c.flags&1){if(c.offset%8 || c.offset+uint64_t(c.x)*24>src.bytes)throw std::runtime_error("tenant AABB range exceeds its buffer");}
            else{const auto& index=buffer(c.targets[1]);if(c.value[0]<12 || c.value[0]%4 || !c.value[1] || uint64_t(c.value[1])*c.value[0]>src.bytes || c.offset%4 || c.offset+uint64_t(c.x)*12>index.bytes)
                throw std::runtime_error("tenant BLAS vertex/index range");}}
        else if(c.op==10){auto a=tenant.accels.find(c.targets[0]);if(a==tenant.accels.end() || !a->second.top)throw std::runtime_error("tenant TLAS build needs a top-level id");
            if(c.bytes!=uint64_t(c.x)*80 || c.blob_offset+c.bytes>blob_bytes || c.x>(1u<<24))throw std::runtime_error("tenant TLAS instance records are 80 bytes each");
            for(uint32_t k=0;k<c.x;++k){TenantInstance r{};std::memcpy(&r,static_cast<const uint8_t*>(blob)+c.blob_offset+uint64_t(k)*80,80);
                auto b=tenant.accels.find(r.blas);if(b==tenant.accels.end() || b->second.top)throw std::runtime_error("tenant TLAS instance names no bottom-level id");
                for(float v:r.transform)if(!std::isfinite(v))throw std::runtime_error("nonfinite tenant TLAS transform");}}
        else if(c.op==8){const auto& dst=image(c.targets[0]);if(c.layers[0]>=dst.layers || c.bytes!=uint64_t(dst.width)*dst.height*dst.bpp || c.blob_offset+c.bytes>blob_bytes)throw std::runtime_error("tenant image upload shape");}
        else throw std::runtime_error("unknown tenant command "+std::to_string(c.op));}
    for(uint32_t i=0;i<binding_count;++i){auto b=bindings[i];if(b.kind==3)b.offset+=blob_base;tenant.bindings.push_back(b);}
    for(uint32_t i=0;i<count;++i){auto c=commands[i];c.binding_first+=uint32_t(binding_base);c.blob_offset+=blob_base;tenant.commands.push_back(c);}
    if(blob_bytes)tenant.blob.insert(tenant.blob.end(),static_cast<const uint8_t*>(blob),static_cast<const uint8_t*>(blob)+blob_bytes);
}
void tenant_end_frame(){tenant.commands.clear();tenant.bindings.clear();tenant.blob.clear();}
// New resources start zeroed, in GENERAL.
void tenant_initialize(VkCommandBuffer cmd){
    bool any=false;
    if(tenant.dummy_buffer.handle && !tenant.dummy_ready){vkCmdFillBuffer(cmd,tenant.dummy_buffer.handle,0,VK_WHOLE_SIZE,0);tenant.dummy_ready=any=true;}
    for(uint64_t id:tenant.fresh_buffers){auto f=tenant.buffers.find(id);if(f!=tenant.buffers.end()){vkCmdFillBuffer(cmd,f->second.handle,0,VK_WHOLE_SIZE,0);any=true;}}
    tenant.fresh_buffers.clear();
    for(auto& item:tenant.images)if(!item.second.ready){auto& i=item.second;
        barrier(cmd,i.image.handle,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_NONE,0,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,1,i.layers);
        VkClearColorValue zero{};VkImageSubresourceRange range{VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,i.layers};vkCmdClearColorImage(cmd,i.image.handle,VK_IMAGE_LAYOUT_GENERAL,&zero,1,&range);i.ready=any=true;}
    if(any)tenant_barrier(cmd);
}
VkImageView tenant_dummy(VkCommandBuffer cmd,bool storage,uint32_t component,uint32_t shape){
    uint32_t key=(storage?256u:0u)|(component<<4)|shape;if(auto f=tenant.dummies.find(key);f!=tenant.dummies.end())return f->second.view;
    VkFormat format=component==1?VK_FORMAT_R32_UINT:component==2?VK_FORMAT_R32_SINT:VK_FORMAT_R16G16B16A16_SFLOAT;uint32_t layers=shape==2?6:1;
    auto image=make_image(1,1,format,VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT|(storage?VK_IMAGE_USAGE_STORAGE_BIT:0),VK_IMAGE_ASPECT_COLOR_BIT,1,layers,shape==1);
    barrier(cmd,image.handle,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_GENERAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_NONE,0,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,1,layers);
    VkClearColorValue zero{};VkImageSubresourceRange range{VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,layers};vkCmdClearColorImage(cmd,image.handle,VK_IMAGE_LAYOUT_GENERAL,&zero,1,&range);tenant_barrier(cmd);
    tenant.dummies[key]=image;return image.view;
}
VkImageView tenant_target_view(TenantImage& image,uint32_t layer){
    if(auto f=image.targets.find(layer);f!=image.targets.end())return f->second;
    VkImageViewCreateInfo info{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};info.image=image.image.handle;info.viewType=VK_IMAGE_VIEW_TYPE_2D;info.format=image.format;
    info.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,layer,1};VkImageView view{};check(vkCreateImageView(device,&info,nullptr,&view),"create tenant target view");
    image.targets[layer]=view;return view;
}
// Renderer images at the tenant point: GBuffer/depth/shadows/probes are shader-read; HDR is an
// attachment, so sampling it reads this frame's snapshot (DX11 binds LBuffer only as the target).
bool tenant_renderer_view(VkCommandBuffer cmd,uint64_t id,uint32_t shape,VkImageView& view,VkImageLayout& layout,VkImageView& hdr){
    layout=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;view=VK_NULL_HANDLE;
    bool cube=id==kTenantCloseCube || id==kTenantFarCube;if(cube!=(shape==2) || shape==1)return false;
    if(cube)view=probes.ready?probes.final[id==kTenantCloseCube?0:1].image.view:world.ambient.view;
    else if(id==kGuestHdr){if(!hdr)hdr=guest_copy_hdr(cmd,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL);view=hdr;}
    else if(id==kTenantSunShadows)view=world.shadows.view;
    else if(id==kTenantBrdf){auto t=textures.find(probes.ready?probes.config.brdf:0);if(t!=textures.end())view=t->second.image.view;}
    else if(id>=kGuestCatalogVelocity && id<=kGuestCatalogReactiveMask){auto f=guest.images.find(id);
        if(f==guest.images.end() || f->second.is_buffer || f->second.layout==VK_IMAGE_LAYOUT_UNDEFINED || f->second.layers!=1)return false;view=f->second.image.view;layout=f->second.layout;}
    else if(id==kGuestDepth || (id>=kGuestG0 && id<=kGuestG2))view=guest_source(id,nullptr);
    return view!=VK_NULL_HANDLE;
}
VkDescriptorSet tenant_set(VkCommandBuffer cmd,const TenantPipeline& p,const SevkTenantCommand& c,const std::vector<VkDeviceSize>& offsets,VkImageView& hdr,uint32_t s){
    std::unordered_map<uint32_t,uint32_t> provided;for(uint32_t i=0;i<c.binding_count;++i)provided[tenant.bindings[c.binding_first+i].binding]=c.binding_first+i;
    std::vector<VkDescriptorImageInfo> images(p.bindings.size());std::vector<VkDescriptorBufferInfo> buffers(p.bindings.size());std::vector<VkWriteDescriptorSet> writes;
    std::vector<VkWriteDescriptorSetAccelerationStructureKHR> accels(p.bindings.size(),{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET_ACCELERATION_STRUCTURE_KHR});std::vector<VkAccelerationStructureKHR> handles(p.bindings.size());
    for(size_t i=0;i<p.bindings.size();++i){const auto& t=p.bindings[i];auto f=provided.find(t.binding);const SevkTenantBinding* b=f==provided.end()?nullptr:&tenant.bindings[f->second];
        uint32_t kind=b?b->kind:0;VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};w.dstBinding=t.binding;w.descriptorCount=1;w.descriptorType=t.type;
        if(t.type==VK_DESCRIPTOR_TYPE_SAMPLER){if(kind!=0 && kind!=7)return VK_NULL_HANDLE;images[i].sampler=tenant.samplers[kind==7?b->id:1];w.pImageInfo=&images[i];}
        else if(t.type==VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE || t.type==VK_DESCRIPTOR_TYPE_STORAGE_IMAGE){bool storage=t.type==VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
            VkImageView view{};VkImageLayout layout=VK_IMAGE_LAYOUT_GENERAL;
            if(kind==1){auto& image=tenant.images.at(b->id);if(image.component!=t.component || (image.array?1u:0u)!=t.shape || (storage && !image.storage))return VK_NULL_HANDLE;view=image.image.view;}
            else if(kind==4 && !storage){if(t.component || !tenant_renderer_view(cmd,b->id,t.shape,view,layout,hdr)){view={};layout=VK_IMAGE_LAYOUT_GENERAL;}}
            else if(kind==5 && !storage){auto found=t.shape==0 && !t.component?textures.find(b->id):textures.end();if(found!=textures.end()){view=found->second.image.view;layout=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;}}
            else if(kind!=0)return VK_NULL_HANDLE;
            if(!view)view=tenant_dummy(cmd,storage,t.component,t.shape);
            images[i]={VK_NULL_HANDLE,view,layout};w.pImageInfo=&images[i];}
        else if(t.type==VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR){
            if(kind==8)handles[i]=tenant.accels.at(b->id).handle;else if(kind!=0)return VK_NULL_HANDLE;
            if(!handles[i])handles[i]=tenant_dummy_tlas(cmd,s);
            accels[i].accelerationStructureCount=1;accels[i].pAccelerationStructures=&handles[i];w.pNext=&accels[i];}
        else if(t.type==VK_DESCRIPTOR_TYPE_STORAGE_BUFFER){
            if(kind==2){const auto& buffer=tenant.buffers.at(b->id);buffers[i]={buffer.handle,b->offset,b->bytes?b->bytes:VK_WHOLE_SIZE};}
            else if(kind==0)buffers[i]={tenant.dummy_buffer.handle,0,VK_WHOLE_SIZE};else return VK_NULL_HANDLE;
            w.pBufferInfo=&buffers[i];}
        else{
            if(kind==3)buffers[i]={tenant.stream[s].handle,offsets[f->second],std::max<VkDeviceSize>(b->bytes,t.size)};
            else if(kind==6){if(t.size>1280)return VK_NULL_HANDLE;buffers[i]={world.slots[s].frame.handle,0,1280};}
            else if(kind==0){if(t.size>65536)return VK_NULL_HANDLE;buffers[i]={tenant.stream[s].handle,0,std::max<VkDeviceSize>(t.size,16)};}
            else return VK_NULL_HANDLE;
            w.pBufferInfo=&buffers[i];}
        writes.push_back(w);}
    VkDescriptorSetAllocateInfo allocate{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};allocate.descriptorPool=tenant.pools[s];allocate.descriptorSetCount=1;allocate.pSetLayouts=&p.set;
    VkDescriptorSet set{};if(vkAllocateDescriptorSets(device,&allocate,&set)!=VK_SUCCESS)return VK_NULL_HANDLE;
    for(auto& w:writes)w.dstSet=set;vkUpdateDescriptorSets(device,uint32_t(writes.size()),writes.data(),0,nullptr);return set;
}
// Builds into the accel, growing its storage; scratch is per slot and serialized by the command barriers.
void tenant_build(VkCommandBuffer cmd,TenantAccel& a,const VkAccelerationStructureGeometryKHR& geometry,uint32_t count,uint32_t s){
    VkAccelerationStructureBuildGeometryInfoKHR build{VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_GEOMETRY_INFO_KHR};
    build.type=a.top?VK_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL_KHR:VK_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL_KHR;build.flags=VK_BUILD_ACCELERATION_STRUCTURE_PREFER_FAST_TRACE_BIT_KHR;
    build.mode=VK_BUILD_ACCELERATION_STRUCTURE_MODE_BUILD_KHR;build.geometryCount=1;build.pGeometries=&geometry;
    VkAccelerationStructureBuildSizesInfoKHR sizes{VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_SIZES_INFO_KHR};
    vkGetAccelerationStructureBuildSizesKHR(device,VK_ACCELERATION_STRUCTURE_BUILD_TYPE_DEVICE_KHR,&build,&count,&sizes);
    if(!a.handle || a.size<sizes.accelerationStructureSize){
        if(a.handle || a.storage.handle){TenantRetired r{};r.completion=tenant_retire_completion();r.accel=a;tenant.retired.push_back(std::move(r));}
        bool top=a.top;a={};a.top=top;
        a.storage=make_buffer(sizes.accelerationStructureSize,VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_STORAGE_BIT_KHR|VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT,false);
        VkAccelerationStructureCreateInfoKHR info{VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_CREATE_INFO_KHR};info.buffer=a.storage.handle;info.size=sizes.accelerationStructureSize;info.type=build.type;
        check(vkCreateAccelerationStructureKHR(device,&info,nullptr,&a.handle),"create tenant acceleration structure");a.size=sizes.accelerationStructureSize;
    }
    auto& scratch=tenant.scratch[s];VkDeviceSize need=sizes.buildScratchSize+ray.scratch_alignment;
    if(scratch.bytes<need){if(scratch.handle){TenantRetired r{};r.completion=tenant_retire_completion();r.buffer=scratch;tenant.retired.push_back(std::move(r));}
        scratch=make_buffer(std::max<VkDeviceSize>(need,1ull<<20),VK_BUFFER_USAGE_STORAGE_BUFFER_BIT|VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT,false);}
    build.dstAccelerationStructure=a.handle;build.scratchData.deviceAddress=(ray_address(scratch.handle)+ray.scratch_alignment-1)&~(ray.scratch_alignment-1);
    VkAccelerationStructureBuildRangeInfoKHR range{count,0,0,0};const VkAccelerationStructureBuildRangeInfoKHR* ranges=&range;
    vkCmdBuildAccelerationStructuresKHR(cmd,1,&build,&ranges);++tenant.stats.builds;
}
// An empty TLAS answers unbound acceleration-structure bindings (every ray misses).
VkAccelerationStructureKHR tenant_dummy_tlas(VkCommandBuffer cmd,uint32_t s){
    if(tenant.dummy_tlas.handle)return tenant.dummy_tlas.handle;
    tenant.dummy_tlas.top=true;VkAccelerationStructureGeometryKHR g{VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR};g.geometryType=VK_GEOMETRY_TYPE_INSTANCES_KHR;
    g.geometry.instances={VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_INSTANCES_DATA_KHR};g.geometry.instances.data.deviceAddress=ray_address(tenant.stream[s].handle);
    tenant_build(cmd,tenant.dummy_tlas,g,0,s);--tenant.stats.builds;tenant_barrier(cmd);return tenant.dummy_tlas.handle;
}
// Runs this frame's tenant list. The caller has ended the lighting pass; HDR stays COLOR_ATTACHMENT.
void tenant_execute(VkCommandBuffer cmd){
    if(tenant.commands.empty())return;
    uint32_t s=slot_index;frame_mark("anomaly ray tenant");check(vkResetDescriptorPool(device,tenant.pools[s],0),"recycle tenant descriptors");
    // Stream layout: a 64 KiB zero block for unbound uniforms, then uniform ranges and uploads.
    std::vector<VkDeviceSize> offsets(tenant.bindings.size());std::vector<VkDeviceSize> uploads(tenant.commands.size());VkDeviceSize used=65536;
    auto size_of=[&](const SevkTenantCommand& c,uint32_t binding)->VkDeviceSize{auto p=tenant.pipelines.find(c.pipeline);if(p==tenant.pipelines.end())return 0;
        for(const auto& t:p->second.bindings)if(t.binding==binding && t.type==VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER)return t.size;return 0;};
    for(size_t i=0;i<tenant.commands.size();++i){const auto& c=tenant.commands[i];
        if(c.op==7 || c.op==8){used=(used+15)&~VkDeviceSize(15);uploads[i]=used;used+=c.bytes;}
        if(c.op==10){used=(used+15)&~VkDeviceSize(15);uploads[i]=used;used+=VkDeviceSize(c.x)*sizeof(VkAccelerationStructureInstanceKHR);}
        for(uint32_t j=0;j<c.binding_count;++j){const auto& b=tenant.bindings[c.binding_first+j];if(b.kind!=3)continue;
            used=(used+255)&~VkDeviceSize(255);offsets[c.binding_first+j]=used;used+=std::max<VkDeviceSize>(b.bytes,size_of(c,b.binding));}}
    auto& stream=tenant.stream[s];
    if(stream.bytes<used){if(stream.handle){TenantRetired r{};r.completion=tenant_retire_completion();r.buffer=stream;tenant.retired.push_back(std::move(r));stream={};}
        stream=make_buffer(std::max<VkDeviceSize>(used*2,4ull<<20),tenant_buffer_usage(VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT|VK_BUFFER_USAGE_TRANSFER_SRC_BIT),true);}
    auto* mapped=static_cast<uint8_t*>(stream.mapped);std::memset(mapped,0,65536);
    for(size_t i=0;i<tenant.commands.size();++i){const auto& c=tenant.commands[i];
        if(c.op==7 || c.op==8)std::memcpy(mapped+uploads[i],tenant.blob.data()+c.blob_offset,c.bytes);

        for(uint32_t j=0;j<c.binding_count;++j){const auto& b=tenant.bindings[c.binding_first+j];if(b.kind!=3)continue;
            VkDeviceSize span=std::max<VkDeviceSize>(b.bytes,size_of(c,b.binding));std::memset(mapped+offsets[c.binding_first+j],0,span);
            std::memcpy(mapped+offsets[c.binding_first+j],tenant.blob.data()+b.offset,b.bytes);}}
    check(vmaFlushAllocation(allocator,stream.memory,0,used),"flush tenant stream");
    // The slot is fenced before re-recording, so its previous measurement is complete: read it before the reset.
    if(tenant.timing){tenant_sample_timing();tenant.timing_valid[s]=false;vkCmdResetQueryPool(cmd,tenant.timing,s*2,2);vkCmdWriteTimestamp2(cmd,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,tenant.timing,s*2);}
    tenant_initialize(cmd);tenant_barrier(cmd);VkImageView hdr{};
    for(size_t i=0;i<tenant.commands.size();++i){const auto& c=tenant.commands[i];bool ok=true;
        if(c.op==1 || c.op==2){const auto& p=tenant.pipelines.at(c.pipeline);VkDescriptorSet set=tenant_set(cmd,p,c,offsets,hdr,s);ok=set!=VK_NULL_HANDLE;
            if(ok && c.op==1){vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,p.pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_COMPUTE,p.layout,0,1,&set,0,nullptr);
                vkCmdDispatch(cmd,c.x,c.y,c.z);++tenant.stats.dispatches;}
            else if(ok){VkRenderingAttachmentInfo attachments[3]{};
                for(uint32_t t=0;t<c.target_count && ok;++t){auto& a=attachments[t];a.sType=VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;a.loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;a.storeOp=VK_ATTACHMENT_STORE_OP_STORE;
                    if(c.targets[t]==kGuestHdr){ok=world.hdr.view && world.hdr_format==p.formats[t] && c.x<=world.width && c.y<=world.height;a.imageView=world.hdr.view;a.imageLayout=VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;}
                    else{auto& image=tenant.images.at(c.targets[t]);a.imageView=tenant_target_view(image,c.layers[t]);a.imageLayout=VK_IMAGE_LAYOUT_GENERAL;}}
                if(ok){VkRenderingInfo r{VK_STRUCTURE_TYPE_RENDERING_INFO};r.renderArea={{0,0},{c.x,c.y}};r.layerCount=1;r.colorAttachmentCount=c.target_count;r.pColorAttachments=attachments;
                    vkCmdBeginRendering(cmd,&r);vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,p.pipeline);vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,p.layout,0,1,&set,0,nullptr);
                    world_viewport(cmd,c.x,c.y);vkCmdDraw(cmd,3,1,0,0);vkCmdEndRendering(cmd);++tenant.stats.draws;}}}
        else if(c.op==3){const auto& dst=tenant.buffers.at(c.targets[0]);vkCmdFillBuffer(cmd,dst.handle,c.offset,c.bytes?c.bytes:VK_WHOLE_SIZE,c.value[0]);}
        else if(c.op==4){const auto& dst=tenant.images.at(c.targets[0]);VkClearColorValue value{};std::memcpy(&value,c.value,16);
            VkImageSubresourceRange range{VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,dst.layers};vkCmdClearColorImage(cmd,dst.image.handle,VK_IMAGE_LAYOUT_GENERAL,&value,1,&range);}
        else if(c.op==5){VkBufferCopy region{0,c.offset,c.bytes};vkCmdCopyBuffer(cmd,tenant.buffers.at(c.source).handle,tenant.buffers.at(c.targets[0]).handle,1,&region);}
        else if(c.op==6){const auto& src=tenant.images.at(c.source);const auto& dst=tenant.images.at(c.targets[0]);VkImageCopy region{};
            region.srcSubresource=region.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,src.layers};region.extent={src.width,src.height,1};
            vkCmdCopyImage(cmd,src.image.handle,VK_IMAGE_LAYOUT_GENERAL,dst.image.handle,VK_IMAGE_LAYOUT_GENERAL,1,&region);}
        else if(c.op==7){VkBufferCopy region{uploads[i],c.offset,c.bytes};vkCmdCopyBuffer(cmd,stream.handle,tenant.buffers.at(c.targets[0]).handle,1,&region);}
        else if(c.op==9){VkAccelerationStructureGeometryKHR g{VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR};g.flags=VK_GEOMETRY_NO_DUPLICATE_ANY_HIT_INVOCATION_BIT_KHR;
            const auto& src=tenant.buffers.at(c.source);
            if(c.flags&1){g.geometryType=VK_GEOMETRY_TYPE_AABBS_KHR;g.geometry.aabbs={VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_AABBS_DATA_KHR};
                g.geometry.aabbs.data.deviceAddress=ray_address(src.handle,c.offset);g.geometry.aabbs.stride=24;}
            else{g.geometryType=VK_GEOMETRY_TYPE_TRIANGLES_KHR;g.geometry.triangles={VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_TRIANGLES_DATA_KHR};
                g.geometry.triangles.vertexFormat=VK_FORMAT_R32G32B32_SFLOAT;g.geometry.triangles.vertexData.deviceAddress=ray_address(src.handle);
                g.geometry.triangles.vertexStride=c.value[0];g.geometry.triangles.maxVertex=c.value[1]-1;g.geometry.triangles.indexType=VK_INDEX_TYPE_UINT32;
                g.geometry.triangles.indexData.deviceAddress=ray_address(tenant.buffers.at(c.targets[1]).handle,c.offset);}
            tenant_build(cmd,tenant.accels.at(c.targets[0]),g,c.x,s);}
        else if(c.op==10){
            // BLAS ids become addresses when the TLAS command runs, after this list's earlier BLAS builds created them.
            // A BLAS never built is an inactive instance (reference 0). Host writes before submission need only a flush.
            for(uint32_t k=0;k<c.x;++k){auto r=tenant_instance(c,k);VkAccelerationStructureInstanceKHR v{};std::memcpy(&v.transform,r.transform,48);
                v.instanceCustomIndex=r.custom&0xFFFFFF;v.mask=r.mask&0xFF;v.flags=r.flags&0xFF;const auto& b=tenant.accels.at(r.blas);
                if(b.handle){VkAccelerationStructureDeviceAddressInfoKHR address{VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_DEVICE_ADDRESS_INFO_KHR};address.accelerationStructure=b.handle;
                    v.accelerationStructureReference=vkGetAccelerationStructureDeviceAddressKHR(device,&address);}
                std::memcpy(mapped+uploads[i]+uint64_t(k)*sizeof(v),&v,sizeof(v));}
            if(c.x)check(vmaFlushAllocation(allocator,stream.memory,uploads[i],VkDeviceSize(c.x)*sizeof(VkAccelerationStructureInstanceKHR)),"flush tenant TLAS instances");
            VkAccelerationStructureGeometryKHR g{VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR};g.geometryType=VK_GEOMETRY_TYPE_INSTANCES_KHR;
            g.geometry.instances={VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_INSTANCES_DATA_KHR};g.geometry.instances.data.deviceAddress=ray_address(stream.handle,uploads[i]);
            tenant_build(cmd,tenant.accels.at(c.targets[0]),g,c.x,s);}
        else if(c.op==8){const auto& dst=tenant.images.at(c.targets[0]);VkBufferImageCopy region{};region.bufferOffset=uploads[i];
            region.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,c.layers[0],1};region.imageExtent={dst.width,dst.height,1};
            vkCmdCopyBufferToImage(cmd,stream.handle,dst.image.handle,VK_IMAGE_LAYOUT_GENERAL,1,&region);}
        if(!ok)++tenant.stats.rejected;
        ++tenant.stats.commands;tenant_barrier(cmd);}
    if(tenant.timing){vkCmdWriteTimestamp2(cmd,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,tenant.timing,s*2+1);tenant.timing_valid[s]=true;}
    ++tenant.stats.frames;tenant_end_frame();
}
void tenant_readback(uint64_t id,uint32_t layer,uint64_t offset,void* data,uint64_t bytes){
    if(recording || batching_uploads)throw std::runtime_error("tenant readback runs outside frame recording");
    if(!data || !bytes || bytes>(1ull<<30))throw std::runtime_error("invalid tenant readback size");
    auto b=tenant.buffers.find(id);auto i=tenant.images.find(id);
    if(b!=tenant.buffers.end()){if(offset+bytes>b->second.bytes)throw std::runtime_error("tenant readback range exceeds the buffer");}
    else if(i!=tenant.images.end()){if(layer>=i->second.layers || offset || bytes!=uint64_t(i->second.width)*i->second.height*i->second.bpp)throw std::runtime_error("tenant image readback is one whole layer");}
    else throw std::runtime_error("unknown tenant readback id");
    wait(next_completion);Buffer staging=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,true,true);
    try{
        immediate([&](VkCommandBuffer cmd){tenant_ensure();tenant_initialize(cmd);tenant_barrier(cmd);
            if(b!=tenant.buffers.end()){VkBufferCopy region{offset,0,bytes};vkCmdCopyBuffer(cmd,b->second.handle,staging.handle,1,&region);}
            else{VkBufferImageCopy region{};region.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,layer,1};region.imageExtent={i->second.width,i->second.height,1};
                vkCmdCopyImageToBuffer(cmd,i->second.image.handle,VK_IMAGE_LAYOUT_GENERAL,staging.handle,1,&region);}
            VkMemoryBarrier2 m{VK_STRUCTURE_TYPE_MEMORY_BARRIER_2};m.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;m.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;
            m.dstStageMask=VK_PIPELINE_STAGE_2_HOST_BIT;m.dstAccessMask=VK_ACCESS_2_HOST_READ_BIT;VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.memoryBarrierCount=1;d.pMemoryBarriers=&m;vkCmdPipelineBarrier2(cmd,&d);});
        check(vmaInvalidateAllocation(allocator,staging.memory,0,bytes),"invalidate tenant readback");std::memcpy(data,staging.mapped,bytes);
    }catch(...){destroy_buffer(staging);throw;}
    destroy_buffer(staging);
}
SevkTenantStats tenant_stats()const{
    SevkTenantStats s=tenant.stats;s.size=sizeof(s);s.buffers=uint32_t(tenant.buffers.size());s.images=uint32_t(tenant.images.size());s.pipelines=uint32_t(tenant.pipelines.size());s.bytes=0;
    s.accels=uint32_t(tenant.accels.size());s.features=(ray.supported?1u:0u)|(timestamp_bits?2u:0u);
    for(const auto& b:tenant.buffers)s.bytes+=b.second.bytes;for(const auto& i:tenant.images)s.bytes+=uint64_t(i.second.width)*i.second.height*i.second.layers*i.second.bpp;return s;
}
