VkDeviceSize world_align(VkDeviceSize n){return (n+world.alignment-1)/world.alignment*world.alignment;}
uint32_t world_unfade(uint32_t id){return id>=2560?id-2560:id;}
bool world_cutout(uint32_t id){id=world_unfade(id);return id>=22 && id<32;}
bool world_blended_decal(uint32_t id){id=world_unfade(id);return (id>=128 && id<208) || (id>=608 && id<848);}
bool world_mesh_decal(uint32_t id){return world_cutout(id)||world_blended_decal(id);}
uint32_t world_decal_technique_order(uint32_t id){return world_cutout(id)?2u:((id-128)%20>=10?1u:0u);}
bool world_surface(uint32_t id){id=world_unfade(id);return id<32 || world_blended_decal(id);}
bool world_voxel(uint32_t id){return id==20 || id==21;}
uint32_t main_geometry_phase(bool old,bool decal,uint32_t technique=0){return old?(decal?1+technique:0u):4u;}
uint32_t world_main_phase(uint32_t pipeline,uint32_t flags){bool decal=world_mesh_decal(pipeline);return main_geometry_phase((flags&0x04000000u)||world_voxel(pipeline),decal,decal?world_decal_technique_order(pipeline):0u);}
uint32_t world_base_pipeline(uint32_t id){uint32_t fade=id>=2560?2560:0;id=world_unfade(id);return fade+(world_blended_decal(id)?(id-128)%5+((id-128)%10>=5?10u:0u):world_cutout(id)?(id-22)%5+(id>=27?10u:0u):id);}
void world_buffer(Buffer& b,VkDeviceSize bytes,VkBufferUsageFlags usage){
    if(b.handle && b.bytes>=bytes)return;destroy_buffer(b);b=make_buffer(std::max(bytes,VkDeviceSize(4096)),usage,true);
}
VkShaderModule world_shader(const void* data,uint64_t bytes,VkShaderStageFlagBits stage,SpvReflectShaderModule& reflected){
    if(!data || bytes<20 || bytes%4 || bytes>16*1024*1024)throw std::runtime_error("invalid world SPIR-V size");
    auto* words=static_cast<const uint32_t*>(data);
    for(uint64_t i=5;i<bytes/4;){uint32_t n=words[i]>>16,op=words[i]&65535;
        if(!n || n>bytes/4-i)throw std::runtime_error("malformed world SPIR-V");
        bool capability=n==2 && (words[i+1]==1 || words[i+1]==5379 || words[i+1]==50 || (words[i+1]==2 && stage==VK_SHADER_STAGE_GEOMETRY_BIT) || words[i+1]==43 || words[i+1]==44 || words[i+1]==46 || ((words[i+1]==49 || words[i+1]==47) && stage==VK_SHADER_STAGE_COMPUTE_BIT));
        if(op==10 || (op==17 && !capability))throw std::runtime_error("world shader requests an extension/capability not enabled by this pass: "+std::to_string(op==17?words[i+1]:0));i+=n;}
    if(spvReflectCreateShaderModule(bytes,data,&reflected)!=SPV_REFLECT_RESULT_SUCCESS)throw std::runtime_error("world reflection failed");
    if(uint32_t(reflected.shader_stage)!=uint32_t(stage)){spvReflectDestroyShaderModule(&reflected);throw std::runtime_error("world shader stage mismatch");}
    VkShaderModuleCreateInfo info{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};info.codeSize=bytes;info.pCode=words;
    VkShaderModule module{};try{check(vkCreateShaderModule(device,&info,nullptr,&module),"create world shader");}catch(...){spvReflectDestroyShaderModule(&reflected);throw;}return module;
}
VkDescriptorSetLayout world_layout(bool material,bool spot=false,bool probe=false,bool highlight=false,const PostStage* stage=nullptr){
    std::vector<VkDescriptorSetLayoutBinding> b;
    auto add=[&](uint32_t id,VkDescriptorType t){b.push_back({id,t,1,VK_SHADER_STAGE_VERTEX_BIT|VK_SHADER_STAGE_FRAGMENT_BIT,nullptr});};
    add(0,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
    if(material){add(1,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);add(2,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);add(6,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);for(uint32_t i=138;i<=143;++i)add(i,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE);add(157,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE);for(uint32_t i=128;i<=131;++i)add(i,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE);add(156,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE);}
    else {add(1,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);add(4,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);add(5,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);for(uint32_t i:{128u,129u,130u,131u,138u,139u,140u,143u,145u,147u,148u,149u,150u,151u,152u})add(i,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE);
        add(141,spot?VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE:VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);add(142,spot?VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE:VK_DESCRIPTOR_TYPE_STORAGE_BUFFER);if(spot)add(271,VK_DESCRIPTOR_TYPE_SAMPLER);add(256,VK_DESCRIPTOR_TYPE_SAMPLER);add(260,VK_DESCRIPTOR_TYPE_SAMPLER);}
    add(258,VK_DESCRIPTOR_TYPE_SAMPLER);add(259,VK_DESCRIPTOR_TYPE_SAMPLER);
    if(highlight)add(4,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
    if(probe){add(4,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);add(7,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);add(144,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE);add(271,VK_DESCRIPTOR_TYPE_SAMPLER);}
    if(stage){for(const auto& resource:stage->catalog)add(resource.binding,resource.type);for(const auto& binding:stage->bindings)if(binding.first==6)add(6,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);else if(binding.first==257)add(257,VK_DESCRIPTOR_TYPE_SAMPLER);}
    VkDescriptorSetLayoutCreateInfo info{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};info.bindingCount=static_cast<uint32_t>(b.size());info.pBindings=b.data();
    VkDescriptorSetLayout result{};check(vkCreateDescriptorSetLayout(device,&info,nullptr,&result),"create reflected world layout");return result;
}
VkPipelineLayout world_pipeline_layout(VkDescriptorSetLayout layout){VkPipelineLayoutCreateInfo p{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};p.setLayoutCount=1;p.pSetLayouts=&layout;VkPipelineLayout r{};check(vkCreatePipelineLayout(device,&p,nullptr,&r),"create world pipeline layout");return r;}
VkPipeline world_graphics(VkShaderModule vs,const char* ve,VkShaderModule ps,const char* pe,VkPipelineLayout layout,
    const std::vector<VkVertexInputBindingDescription>& bindings,const std::vector<VkVertexInputAttributeDescription>& attributes,const VkFormat* formats,uint32_t count,bool depth_test,bool shadow=false,bool additive=false,bool compose=false,uint32_t blend_mode=0,bool read_only_depth=false,VkShaderModule gs=VK_NULL_HANDLE,bool points=false,bool strip=false,bool stock_depth=false,bool cascade=false,VkCullModeFlags cull=VK_CULL_MODE_NONE,VkFrontFace front=VK_FRONT_FACE_COUNTER_CLOCKWISE,VkCompareOp depth_compare=VK_COMPARE_OP_GREATER_OR_EQUAL,bool depth_clamp=false,bool model_raster=false){
    std::vector<VkPipelineShaderStageCreateInfo> stages{{VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_VERTEX_BIT,vs,ve,nullptr}};
    if(ps)stages.push_back({VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_FRAGMENT_BIT,ps,pe,nullptr});
    VkPipelineVertexInputStateCreateInfo input{VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO};input.vertexBindingDescriptionCount=static_cast<uint32_t>(bindings.size());input.pVertexBindingDescriptions=bindings.data();input.vertexAttributeDescriptionCount=static_cast<uint32_t>(attributes.size());input.pVertexAttributeDescriptions=attributes.data();
    VkPipelineInputAssemblyStateCreateInfo assembly{VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO};assembly.topology=strip?VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP:points?VK_PRIMITIVE_TOPOLOGY_POINT_LIST:VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    VkPipelineViewportStateCreateInfo viewport{VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO};viewport.viewportCount=viewport.scissorCount=1;
    VkPipelineRasterizationStateCreateInfo raster{VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO};raster.polygonMode=VK_POLYGON_MODE_FILL;raster.cullMode=cull;raster.frontFace=front;raster.lineWidth=1;raster.depthClampEnable=depth_clamp;
    if(stock_depth){raster.cullMode=cascade?VK_CULL_MODE_BACK_BIT:VK_CULL_MODE_NONE;raster.depthBiasEnable=VK_TRUE;raster.depthBiasConstantFactor=cascade?20.f:2500.f;raster.depthBiasClamp=cascade?2.f:10000.f;raster.depthBiasSlopeFactor=4.f;}
    VkPipelineMultisampleStateCreateInfo ms{VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO};ms.rasterizationSamples=VK_SAMPLE_COUNT_1_BIT;
    VkPipelineDepthStencilStateCreateInfo depth_state{VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO};depth_state.depthTestEnable=depth_state.depthWriteEnable=depth_test;if(read_only_depth)depth_state.depthWriteEnable=VK_FALSE;depth_state.depthCompareOp=compose?VK_COMPARE_OP_ALWAYS:(shadow?VK_COMPARE_OP_LESS_OR_EQUAL:depth_compare);
    std::vector<VkPipelineColorBlendAttachmentState> attachments(count);for(auto& a:attachments){a.colorWriteMask=15;if(additive){a.blendEnable=VK_TRUE;a.srcColorBlendFactor=a.dstColorBlendFactor=VK_BLEND_FACTOR_ONE;a.colorBlendOp=VK_BLEND_OP_ADD;a.srcAlphaBlendFactor=a.dstAlphaBlendFactor=VK_BLEND_FACTOR_ONE;a.alphaBlendOp=VK_BLEND_OP_ADD;}if(blend_mode){a.blendEnable=VK_TRUE;a.srcColorBlendFactor=blend_mode==1?VK_BLEND_FACTOR_SRC_ALPHA:VK_BLEND_FACTOR_ONE;a.dstColorBlendFactor=blend_mode==1?VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA:VK_BLEND_FACTOR_SRC_ALPHA;a.colorBlendOp=VK_BLEND_OP_ADD;a.srcAlphaBlendFactor=blend_mode==1?VK_BLEND_FACTOR_SRC_ALPHA:VK_BLEND_FACTOR_ONE;a.dstAlphaBlendFactor=blend_mode==1?VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA:VK_BLEND_FACTOR_ONE;a.alphaBlendOp=VK_BLEND_OP_ADD;}}
    if(blend_mode==5)for(auto& a:attachments){a.blendEnable=VK_TRUE;a.srcColorBlendFactor=a.srcAlphaBlendFactor=VK_BLEND_FACTOR_ONE;a.dstColorBlendFactor=a.dstAlphaBlendFactor=VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;}
    if(blend_mode==3){if(count!=2)throw std::runtime_error("OIT needs two targets");for(auto& a:attachments){a.blendEnable=VK_TRUE;a.srcColorBlendFactor=a.srcAlphaBlendFactor=VK_BLEND_FACTOR_ONE;a.dstColorBlendFactor=a.dstAlphaBlendFactor=VK_BLEND_FACTOR_ONE;}attachments[1].srcColorBlendFactor=attachments[1].srcAlphaBlendFactor=VK_BLEND_FACTOR_ZERO;attachments[1].dstColorBlendFactor=attachments[1].dstAlphaBlendFactor=VK_BLEND_FACTOR_ONE_MINUS_SRC_COLOR;attachments[1].colorWriteMask=VK_COLOR_COMPONENT_R_BIT;}
    if(blend_mode==4)for(auto& a:attachments){a.blendEnable=VK_TRUE;a.srcColorBlendFactor=a.srcAlphaBlendFactor=VK_BLEND_FACTOR_CONSTANT_COLOR;a.dstColorBlendFactor=a.dstAlphaBlendFactor=VK_BLEND_FACTOR_ONE_MINUS_CONSTANT_COLOR;}
    if(blend_mode==6)for(auto& a:attachments){a.blendEnable=VK_TRUE;a.srcColorBlendFactor=a.srcAlphaBlendFactor=VK_BLEND_FACTOR_ZERO;a.dstColorBlendFactor=VK_BLEND_FACTOR_ONE_MINUS_SRC_COLOR;a.dstAlphaBlendFactor=VK_BLEND_FACTOR_ONE;a.colorBlendOp=a.alphaBlendOp=VK_BLEND_OP_ADD;}
    if(blend_mode==7)for(auto& a:attachments){a.blendEnable=VK_TRUE;a.srcColorBlendFactor=a.srcAlphaBlendFactor=a.dstColorBlendFactor=a.dstAlphaBlendFactor=VK_BLEND_FACTOR_ONE;a.colorBlendOp=a.alphaBlendOp=VK_BLEND_OP_MIN;}
    if(blend_mode>=16){uint32_t role=blend_mode>=64?(blend_mode-64)&7:blend_mode-16;bool straight=blend_mode>=64 && ((blend_mode-64)&8);if(count<3||count>7)throw std::runtime_error("decal GBuffer target count");for(auto& a:attachments){a.blendEnable=VK_TRUE;a.srcColorBlendFactor=straight?VK_BLEND_FACTOR_SRC_ALPHA:VK_BLEND_FACTOR_ONE;a.dstColorBlendFactor=VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;a.srcAlphaBlendFactor=VK_BLEND_FACTOR_ZERO;a.dstAlphaBlendFactor=VK_BLEND_FACTOR_ONE;a.colorBlendOp=a.alphaBlendOp=VK_BLEND_OP_ADD;a.colorWriteMask=0;}attachments[0].colorWriteMask=(role&1)?7:0;attachments[1].colorWriteMask=(role&2)?7:0;attachments[1].srcColorBlendFactor=VK_BLEND_FACTOR_SRC_ALPHA;attachments[2].colorWriteMask=((role&1)?1:0)|((role&2)?2:0)|((role&4)?4:0);
        // DX11 binds Target3 as RG replace for every GBuffer draw, decals included.
        // Pack attachments beyond Target3 replace their values like any opaque target.
        if(count>3)attachments[3]={VK_FALSE,VK_BLEND_FACTOR_ONE,VK_BLEND_FACTOR_ZERO,VK_BLEND_OP_ADD,VK_BLEND_FACTOR_ONE,VK_BLEND_FACTOR_ZERO,VK_BLEND_OP_ADD,VK_COLOR_COMPONENT_R_BIT|VK_COLOR_COMPONENT_G_BIT};
        for(uint32_t i=4;i<count;++i)attachments[i]={VK_FALSE,VK_BLEND_FACTOR_ONE,VK_BLEND_FACTOR_ZERO,VK_BLEND_OP_ADD,VK_BLEND_FACTOR_ONE,VK_BLEND_FACTOR_ZERO,VK_BLEND_OP_ADD,15};}
    VkPipelineColorBlendStateCreateInfo blend{VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO};blend.attachmentCount=count;blend.pAttachments=attachments.data();
    std::vector<VkDynamicState> states{VK_DYNAMIC_STATE_VIEWPORT,VK_DYNAMIC_STATE_SCISSOR};
    if(blend_mode==4)states.push_back(VK_DYNAMIC_STATE_BLEND_CONSTANTS);
    if(model_raster){states.push_back(VK_DYNAMIC_STATE_CULL_MODE);states.push_back(VK_DYNAMIC_STATE_FRONT_FACE);states.push_back(VK_DYNAMIC_STATE_DEPTH_BIAS_ENABLE);}
    VkPipelineDynamicStateCreateInfo dynamic{VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO};dynamic.dynamicStateCount=uint32_t(states.size());dynamic.pDynamicStates=states.data();
    VkPipelineRenderingCreateInfo rendering{VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO};rendering.colorAttachmentCount=count;rendering.pColorAttachmentFormats=formats;rendering.depthAttachmentFormat=depth_test?VK_FORMAT_D32_SFLOAT:VK_FORMAT_UNDEFINED;
    VkGraphicsPipelineCreateInfo p{VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO,&rendering};if(gs)stages.insert(stages.begin()+1,{VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_GEOMETRY_BIT,gs,"__geometry_shader",nullptr});p.stageCount=uint32_t(stages.size());p.pStages=stages.data();p.pVertexInputState=&input;p.pInputAssemblyState=&assembly;p.pViewportState=&viewport;p.pRasterizationState=&raster;p.pMultisampleState=&ms;p.pDepthStencilState=&depth_state;p.pColorBlendState=&blend;p.pDynamicState=&dynamic;p.layout=layout;
    VkPipeline result{};check(vkCreateGraphicsPipelines(device,pipeline_cache,1,&p,nullptr,&result),"create stock world pipeline");return result;
}
// Core Vulkan 1.3 dynamic raster state. Legacy raw packets (no explicit
// flags) retain their original pipeline defaults; production model packets
// carry stock technique/actor/material state through all submission routes.
// Last dynamic raster state recorded by one draw loop. A loop starts invalid
// and invalidates it after binding a pipeline whose raster state is static
// (voxels): that bind overwrites the command buffer state.
struct WorldRasterCache{bool valid{};VkCullModeFlags cull{};VkFrontFace front{};VkBool32 bias{};};
void world_model_raster(VkCommandBuffer cmd,uint32_t pipeline,uint32_t flags,bool depth=false,VkCullModeFlags depth_default=VK_CULL_MODE_NONE,bool stock_depth=false,WorldRasterCache* cache=nullptr){
    VkCullModeFlags cull=depth?depth_default:world_blended_decal(pipeline)?VK_CULL_MODE_BACK_BIT:VK_CULL_MODE_NONE;
    VkFrontFace front=depth?VK_FRONT_FACE_COUNTER_CLOCKWISE:world_blended_decal(pipeline)?VK_FRONT_FACE_CLOCKWISE:VK_FRONT_FACE_COUNTER_CLOCKWISE;
    if(flags&(depth?0x20000000u:0x10000000u))cull=VK_CULL_MODE_NONE;
    else if(!depth && (flags&0x08000000u))cull=VK_CULL_MODE_BACK_BIT;
    if(!depth && (flags&0x18000000u))front=VK_FRONT_FACE_CLOCKWISE;
    VkBool32 bias=stock_depth && !(flags&0x20000000u);
    if(!cache){vkCmdSetCullMode(cmd,cull);vkCmdSetFrontFace(cmd,front);vkCmdSetDepthBiasEnable(cmd,bias);return;}
    if(!cache->valid || cache->cull!=cull)vkCmdSetCullMode(cmd,cull);
    if(!cache->valid || cache->front!=front)vkCmdSetFrontFace(cmd,front);
    if(!cache->valid || cache->bias!=bias)vkCmdSetDepthBiasEnable(cmd,bias);
    *cache={true,cull,front,bias};
}
void init_world_layouts(){
    if(world.material_layout)return;
    VkPhysicalDeviceProperties props{};vkGetPhysicalDeviceProperties(physical,&props);world.alignment=props.limits.minUniformBufferOffsetAlignment;
    world.material_layout=world_layout(true);world.light_layout=world_layout(false);world.spot_layout=world_layout(false,true);
    world.material_pipeline_layout=world_pipeline_layout(world.material_layout);world.light_pipeline_layout=world_pipeline_layout(world.light_layout);world.spot_pipeline_layout=world_pipeline_layout(world.spot_layout);
    for(auto& slot:world.slots){std::array<VkDescriptorPoolSize,8> sizes{{{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,480000},{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC,480000},{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,3200000},{VK_DESCRIPTOR_TYPE_SAMPLER,800000},{VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,4096},{VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,256},{VK_DESCRIPTOR_TYPE_STORAGE_TEXEL_BUFFER,2048},{VK_DESCRIPTOR_TYPE_UNIFORM_TEXEL_BUFFER,2048}}};
        VkDescriptorPoolCreateInfo p{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};p.maxSets=208000;p.poolSizeCount=7;p.pPoolSizes=sizes.data();check(vkCreateDescriptorPool(device,&p,nullptr,&slot.pool),"create frame world descriptor pool");
        // One cached set per material, including probe and highlight layouts.
        // A material set holds up to 4 uniforms, 2 dynamic uniforms, 13 sampled images and 3 samplers.
        std::array<VkDescriptorPoolSize,4> cached{{{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,131072},{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC,65536},{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,425984},{VK_DESCRIPTOR_TYPE_SAMPLER,98304}}};
        VkDescriptorPoolCreateInfo materials{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};materials.flags=VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT;materials.maxSets=32768;materials.poolSizeCount=4;materials.pPoolSizes=cached.data();check(vkCreateDescriptorPool(device,&materials,nullptr,&slot.material_pool),"create cached world descriptor pool");
        slot.dimensions=make_buffer(32,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,true);slot.frame=make_buffer(1280,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,true);slot.projections=make_buffer(world_align(64)*76,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,true);slot.glue=make_buffer(352,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,true);slot.spots=make_buffer(world_align(208)*64,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT,true);}
    const float quad[]{-1,1,0,1,0,0,3,1,0,1,2,0,-1,-3,0,1,0,2};world.quad=make_buffer(sizeof(quad),VK_BUFFER_USAGE_VERTEX_BUFFER_BIT,true);std::memcpy(world.quad.mapped,quad,sizeof(quad));check(vmaFlushAllocation(allocator,world.quad.memory,0,sizeof(quad)),"flush stock fullscreen triangle");
    // Neutral black sky, explicit ambient fallback and source dither. Probe
    // prefiltering/specular IBL is a later parity subsystem, never implied here.
    const uint8_t ambient[]{15,15,15,255,15,15,15,255,15,15,15,255,15,15,15,255,15,15,15,255,15,15,15,255};
    const uint8_t black[24]{};upload_world_cube(0,1,1,0,false,black,24);world.black=world.cubes.at(0);world.cubes.erase(0);
    upload_world_cube(0,1,1,0,false,ambient,24);world.ambient=world.cubes.at(0);world.cubes.erase(0);
}
void reflect_motion_rows(const SpvReflectShaderModule& vr, uint32_t& row0, uint32_t& row1, uint32_t& row2){
    struct Walk { uint32_t &r0,&r1,&r2; void go(const SpvReflectBlockVariable& v, uint32_t base){
        uint32_t at=base+v.offset; std::string name=v.name?v.name:"";
        if(name.find("matrix_row0")!=std::string::npos)r0=at; else if(name.find("matrix_row1")!=std::string::npos)r1=at; else if(name.find("matrix_row2")!=std::string::npos)r2=at;
        for(uint32_t i=0;i<v.member_count && v.members;++i)go(v.members[i],at);
    }} walk{row0,row1,row2};
    for(uint32_t i=0;i<vr.descriptor_binding_count;++i)if(vr.descriptor_bindings[i].binding==2 && vr.descriptor_bindings[i].descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER)walk.go(vr.descriptor_bindings[i].block,0);
}
uint32_t reflect_distance_fade(const SpvReflectShaderModule& shader){
    struct Walk{uint32_t found=UINT32_MAX;void go(const SpvReflectBlockVariable& variable,uint32_t base){
        uint32_t at=base+variable.offset;if(variable.name && std::strcmp(variable.name,"DistanceFade")==0)found=at;
        for(uint32_t i=0;i<variable.member_count && variable.members;i++)go(variable.members[i],at);
    }} walk;
    for(uint32_t i=0;i<shader.descriptor_binding_count;i++)if(shader.descriptor_bindings[i].binding==0 && shader.descriptor_bindings[i].descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER)walk.go(shader.descriptor_bindings[i].block,0);
    return walk.found;
}
uint32_t world_distance_fade_bits(){
    if(world.distance_fade_at>world.current().frame.size()-4)throw std::runtime_error("distance-fade frame layout is unavailable");
    uint32_t bits;std::memcpy(&bits,world.current().frame.data()+world.distance_fade_at,4);return bits;
}
// Build every stock raster variant before installing the owner. Candidates never
// mutate the live pipeline, motion rows, distance-fade layout or recipe caches.
void world_destroy_pipeline(WorldPipeline& p){
    if(p.handle)vkDestroyPipeline(device,p.handle,nullptr);
    if(p.stock_cascade)vkDestroyPipeline(device,p.stock_cascade,nullptr);
    if(p.stock_spot)vkDestroyPipeline(device,p.stock_spot,nullptr);p={};
}
WorldPipeline build_world_pipeline(uint32_t id,const void* vertex,uint64_t vb,const void* pixel,uint64_t pb,uint32_t& distance_fade_at){
    const uint32_t source_id=world_unfade(id);bool decal=world_blended_decal(id);
    if(recording || id>=world.pipelines.size() || (!decal && (source_id>=116 || (source_id>=32 && (source_id%32)>=22) || (source_id>=86 && source_id<96) || (source_id>=96 && source_id%32>=20))))throw std::runtime_error("invalid world pipeline id/state");
    bool probe=source_id>=64 && source_id<86,highlight=source_id>=96 && source_id<116;
    WorldPipeline candidate{};SpvReflectShaderModule vr{},pr{};VkShaderModule vs{},ps{};
    try{vs=world_shader(vertex,vb,VK_SHADER_STAGE_VERTEX_BIT,vr);ps=world_shader(pixel,pb,VK_SHADER_STAGE_FRAGMENT_BIT,pr);
        if(id>=2560){uint32_t at=reflect_distance_fade(vr);if(at>world.current().frame.size()-4 || (world.distance_fade_at!=UINT32_MAX && world.distance_fade_at!=at))throw std::runtime_error("distance-fade frame field changed");distance_fade_at=at;}
        bool voxel=!decal && world_voxel(id%32);uint32_t stream=world_base_pipeline(decal?id:id%32);uint32_t object_bytes=voxel?144:(stream%5==1?3968:128);bool valid=true;bool admitted=gbuffer_velocity_admitted(id,decal,voxel),velocity_resources=false;
        for(auto* reflection:{&vr,&pr})for(uint32_t i=0;i<reflection->descriptor_binding_count;++i){auto& b=reflection->descriptor_bindings[i];
            bool uniform=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER &&
                ((b.binding==0 && b.block.size==1280) || (b.binding==1 && b.block.size==64) ||
                 (b.binding==2 && b.block.size==object_bytes) || (voxel && b.binding==6 && b.block.size==30720) || (highlight && b.binding==4 && b.block.size==16) || (probe && ((b.binding==4 && b.block.size==1200)||(b.binding==7 && b.block.size==64))));
            bool image=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE &&
                ((!voxel && b.binding>=128 && b.binding<=131) || b.binding==156 ||
                 (voxel && ((b.binding>=138 && b.binding<=143)||b.binding==157)) || (probe && b.binding==144)) &&
                b.image.dim==SpvDim2D && b.image.arrayed==((voxel && b.binding>=138 && b.binding<=143)||(probe && b.binding==144)) && !b.image.ms;
            bool sample=b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER && (b.binding==258 || b.binding==259 || (probe && b.binding==271));
            if(admitted&&gbuffer_velocity_binding(b)){velocity_resources=true;continue;}
            bool ok=b.set==0 && b.count==1 && (uniform || image || sample);
            if(!ok)throw std::runtime_error("world pipeline "+std::to_string(id)+" unsupported descriptor "+std::to_string(b.binding)+" type "+std::to_string(b.descriptor_type)+" size "+std::to_string(b.block.size));if(b.binding==2 && b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER)object_bytes=std::max(object_bytes,b.block.size);}
        uint32_t type=voxel?0:stream%5;std::vector<VkVertexInputBindingDescription> bindings{{0,voxel?24u:uint32_t(sizeof(SevkWorldVertex)),VK_VERTEX_INPUT_RATE_VERTEX}};
        if(type>=2)bindings.push_back({1,64,VK_VERTEX_INPUT_RATE_INSTANCE});std::vector<VkVertexInputAttributeDescription> attributes;
        for(uint32_t i=0;i<vr.input_variable_count;++i){auto& v=*vr.input_variables[i];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;
            std::string name=v.name?v.name:"";name=name.substr(name.find_last_of('.')+1);uint32_t offset{},binding{};VkFormat format{};
            // DXC names entry inputs by their HLSL semantic; the authenticated
            // source layout chooses the meaning of each instance TEXCOORD.
            if(name=="POSITION0")name="position";else if(name=="NORMAL0")name="normal";else if(name=="TANGENT0")name="tangent4";
            else if(name=="BLENDWEIGHT0")name="blend_weights";else if(name=="BLENDINDICES0")name="blend_indices";
            else if(name.rfind("TEXCOORD",0)==0){int tc=std::stoi(name.substr(8));
                if(tc==0)name="texcoord0";
                else if(type==2 && tc<=3)name="matrix_row"+std::to_string(tc-1);
                else if(type==2 && tc==4)name="colormask";
                else if(type>=3 && tc<=8)name="packed_bone"+std::to_string(tc-1);
                else if(type>=3 && tc==9)name="cube_transformation";
                else if(type>=3 && tc==10)name="colormask";}
            if(voxel){if(name=="position"){format=VK_FORMAT_R32G32B32_SFLOAT;offset=0;}else if(name=="MATERIAL0" || name=="material"){format=VK_FORMAT_R8G8B8A8_SINT;offset=12;}else if(name=="normal"){format=VK_FORMAT_R16G16_UINT;offset=16;}else if(name=="COLORSHIFT0" || name=="colorshift"){format=VK_FORMAT_R8G8B8A8_SINT;offset=20;}else throw std::runtime_error("unsupported voxel input "+name);}
            else if(name=="position"){format=VK_FORMAT_R32G32B32A32_SFLOAT;offset=0;}
            else if(name=="normal"){format=VK_FORMAT_R32G32_UINT;offset=16;}
            else if(name=="tangent4"){format=VK_FORMAT_R32G32B32A32_SFLOAT;offset=24;}
            else if(name=="texcoord0"){format=VK_FORMAT_R32G32_SFLOAT;offset=40;}
            else if(name=="blend_weights"){format=VK_FORMAT_R32G32B32A32_SFLOAT;offset=48;}
            else if(name=="blend_indices"){format=VK_FORMAT_R32G32B32A32_UINT;offset=64;}
            else if(name.rfind("matrix_row",0)==0 && name.size()==11 && name.back()>='0' && name.back()<='2'){format=VK_FORMAT_R32G32B32A32_SFLOAT;binding=1;offset=(name.back()-'0')*16;}
            else if(name=="colormask"){format=VK_FORMAT_R32G32B32A32_SFLOAT;binding=1;offset=48;}
            else if(name=="cube_transformation"){format=VK_FORMAT_R32G32B32A32_SFLOAT;binding=1;offset=32;}
            else if(name.rfind("packed_bone",0)==0 && name.size()==12 && name.back()>='0' && name.back()<='7'){format=VK_FORMAT_R8G8B8A8_UNORM;binding=1;offset=(name.back()-'0')*4;}
            else {throw std::runtime_error("world pipeline "+std::to_string(id)+" unsupported vertex input "+name);}
            if(binding==1 && type<2)valid=false;attributes.push_back({v.location,binding,format,offset});}
        if(!valid || vr.push_constant_block_count || pr.push_constant_block_count)throw std::runtime_error("unsupported reflected world vertex/material contract");
        bool velocity=false;std::array<VkFormat,7> velocity_formats{};uint32_t velocity_count=gbuffer_owner_formats(pr,admitted,velocity,velocity_formats);
        if(velocity_resources&&!velocity)throw std::runtime_error("world pipeline "+std::to_string(id)+" binds velocity resources without writing Target3");
        if(velocity)gbuffer_velocity_layouts();
        uint32_t blend=0;if(decal){const uint32_t roles[]{1,2,3,7};blend=64+roles[(id-128)/20%4]+(((id-128)%20>=10)?8u:0u);}
        bool shadow=source_id>=32 && source_id<64;const VkFormat highlight_format=VK_FORMAT_R16G16B16A16_SFLOAT;const VkFormat probe_formats[]{stock_probe_format,stock_probe_format};auto pipeline=world_graphics(vs,"__vertex_shader",ps,"__pixel_shader",highlight?highlights.pipeline_layout:probe?probes.material_pipeline_layout:velocity?gbuffer_velocity.pipeline_layout:world.material_pipeline_layout,bindings,attributes,highlight?&highlight_format:probe?probe_formats:shadow?nullptr:velocity?velocity_formats.data():stock_formats.data(),highlight?1:probe?2:shadow?0:velocity?velocity_count:3,true,shadow,false,false,blend,highlight||world_cutout(id)||decal,VK_NULL_HANDLE,false,false,false,false,decal?VK_CULL_MODE_BACK_BIT:VK_CULL_MODE_NONE,decal?VK_FRONT_FACE_CLOCKWISE:VK_FRONT_FACE_COUNTER_CLOCKWISE,VK_COMPARE_OP_GREATER_OR_EQUAL,false,!voxel&&!highlight);
        bool indirect_safe=type>=2;
        for(uint32_t i=0;i<vr.input_variable_count;++i)if(vr.input_variables[i]->decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)indirect_safe=false;
        auto& p=candidate;p={pipeline,object_bytes,type,indirect_safe&&!velocity};p.velocity=velocity;p.row0=p.row1=p.row2=0xFFFFFFFFu;if(!voxel)reflect_motion_rows(vr,p.row0,p.row1,p.row2);
        if(shadow && shadow_depth_bias_clamp){
            p.stock_cascade=world_graphics(vs,"__vertex_shader",ps,"__pixel_shader",world.material_pipeline_layout,bindings,attributes,nullptr,0,true,true,false,false,0,false,VK_NULL_HANDLE,false,false,true,true,VK_CULL_MODE_NONE,VK_FRONT_FACE_COUNTER_CLOCKWISE,VK_COMPARE_OP_GREATER_OR_EQUAL,false,!voxel);
            p.stock_spot=world_graphics(vs,"__vertex_shader",ps,"__pixel_shader",world.material_pipeline_layout,bindings,attributes,nullptr,0,true,true,false,false,0,false,VK_NULL_HANDLE,false,false,true,false,VK_CULL_MODE_NONE,VK_FRONT_FACE_COUNTER_CLOCKWISE,VK_COMPARE_OP_GREATER_OR_EQUAL,false,!voxel);
        }
        spvReflectDestroyShaderModule(&vr);spvReflectDestroyShaderModule(&pr);vkDestroyShaderModule(device,ps,nullptr);vkDestroyShaderModule(device,vs,nullptr);return candidate;
    }catch(...){world_destroy_pipeline(candidate);if(vr._internal)spvReflectDestroyShaderModule(&vr);if(pr._internal)spvReflectDestroyShaderModule(&pr);if(ps)vkDestroyShaderModule(device,ps,nullptr);if(vs)vkDestroyShaderModule(device,vs,nullptr);throw;}
}
void init_world_pipeline(uint32_t id,const void* vertex,uint64_t vb,const void* pixel,uint64_t pb){
    const uint32_t source_id=world_unfade(id);bool decal=world_blended_decal(id);
    if(recording || id>=world.pipelines.size() || (!decal && (source_id>=116 || (source_id>=32 && (source_id%32)>=22) || (source_id>=86 && source_id<96) || (source_id>=96 && source_id%32>=20))))throw std::runtime_error("invalid world pipeline id/state");wait(next_completion);init_world_layouts();bool probe=source_id>=64 && source_id<86;if(probe)require_probe_format();bool highlight=source_id>=96 && source_id<116;if(highlight && !highlights.layout){highlights.layout=world_layout(true,false,false,true);highlights.pipeline_layout=world_pipeline_layout(highlights.layout);}if(probe && !probes.material_layout){probes.material_layout=world_layout(true,false,true);probes.material_pipeline_layout=world_pipeline_layout(probes.material_layout);}
    uint32_t distance_fade_at=world.distance_fade_at;
    auto candidate=build_world_pipeline(id,vertex,vb,pixel,pb,distance_fade_at);
    world_destroy_pipeline(world.pipelines[id]);world.pipelines[id]=candidate;
    world.distance_fade_at=distance_fade_at;invalidate_world_recipes(8,id);
    if(source_id>=32&&source_id<64)geometry_invalidate_shadows();
    if(!post.geometry.empty()&&(post.geometry[2*id].generation||post.geometry[2*id+1].generation)){
        post_destroy_stage(post.geometry[2*id]);post_destroy_stage(post.geometry[2*id+1]);++post.revision;
    }
}

struct GeometryRange{uint32_t page;VkDeviceSize offset;VmaVirtualAllocation allocation;};
GeometryRange allocate_geometry(std::vector<GeometryPage>& pages,uint64_t bytes,uint32_t unit,VkBufferUsageFlags usage){
    if(!unit || bytes%unit)throw std::runtime_error("geometry allocation units mismatch");
    VmaVirtualAllocationCreateInfo info{};info.size=bytes/unit;info.alignment=1;
    for(uint32_t i=0;i<pages.size();++i){auto& p=pages[i];if(!p.block || p.unit!=unit)continue;
        VmaVirtualAllocation allocation{};VkDeviceSize offset{};VkResult result=vmaVirtualAllocate(p.block,&info,&allocation,&offset);
        if(result==VK_SUCCESS)return {i,offset*unit,allocation};
        if(result!=VK_ERROR_OUT_OF_DEVICE_MEMORY)check(result,"suballocate world geometry");}
    uint64_t capacity=(std::max(bytes,uint64_t(16)*1024*1024)+unit-1)/unit*unit;
    GeometryPage page{};page.unit=unit;VmaVirtualBlockCreateInfo block{};block.size=capacity/unit;
    check(vmaCreateVirtualBlock(&block,&page.block),"create world geometry ranges");
    try{page.buffer=make_buffer(capacity,ray_geometry_usage(usage|VK_BUFFER_USAGE_TRANSFER_DST_BIT),false);
        uint32_t at=0;while(at<pages.size() && pages[at].block)++at;
        if(at==pages.size())pages.push_back(page);else pages[at]=page;
        page={};auto& owner=pages[at];VmaVirtualAllocation allocation{};VkDeviceSize offset{};
        check(vmaVirtualAllocate(owner.block,&info,&allocation,&offset),"allocate new world geometry page");
        return {at,offset*unit,allocation};
    }catch(...){destroy_buffer(page.buffer);if(page.block)vmaDestroyVirtualBlock(page.block);throw;}
}
void upload_world_mesh(uint64_t id,const void* vertices,uint32_t vc,const uint32_t* indices,uint32_t ic,uint32_t stride=sizeof(SevkWorldVertex)){
    if(recording || !id || !vertices || !indices || !vc || vc>10000000 || !ic || ic%3 || ic>30000000)throw std::runtime_error("invalid world mesh");
    for(uint32_t i=0;i<ic;++i)if(indices[i]>=vc)throw std::runtime_error("world index outside vertex array");
    Mesh m{};m.vertex_stride=stride;Buffer staging{};uint64_t vb=uint64_t(vc)*stride,ib=uint64_t(ic)*4;
    try{staging=make_buffer(vb+ib,VK_BUFFER_USAGE_TRANSFER_SRC_BIT,true);std::memcpy(staging.mapped,vertices,vb);std::memcpy(static_cast<uint8_t*>(staging.mapped)+vb,indices,ib);check(vmaFlushAllocation(allocator,staging.memory,0,vb+ib),"flush persistent world geometry");
        if(world.geometry_arena_requested){auto v=allocate_geometry(world.vertex_pages,vb,stride,VK_BUFFER_USAGE_VERTEX_BUFFER_BIT);
            m.vertex_page=v.page;m.vertex_offset=v.offset;m.vertex_allocation=v.allocation;m.vertices.handle=world.vertex_pages[v.page].buffer.handle;m.vertices.bytes=vb;
            auto i=allocate_geometry(world.index_pages,ib,4,VK_BUFFER_USAGE_INDEX_BUFFER_BIT);
            m.index_page=i.page;m.index_offset=i.offset;m.index_allocation=i.allocation;m.indices.handle=world.index_pages[i.page].buffer.handle;m.indices.bytes=ib;
        }else {m.vertices=make_buffer(vb,ray_geometry_usage(VK_BUFFER_USAGE_TRANSFER_DST_BIT|VK_BUFFER_USAGE_VERTEX_BUFFER_BIT),false);m.indices=make_buffer(ib,ray_geometry_usage(VK_BUFFER_USAGE_TRANSFER_DST_BIT|VK_BUFFER_USAGE_INDEX_BUFFER_BIT),false);}
        m.index_count=ic;
        immediate([&](VkCommandBuffer cmd){
            if(m.vertex_allocation){std::array<VkBufferMemoryBarrier2,2> before{};for(auto& b:before){b.sType=VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;b.srcStageMask=VK_PIPELINE_STAGE_2_VERTEX_INPUT_BIT;b.srcAccessMask=VK_ACCESS_2_VERTEX_ATTRIBUTE_READ_BIT|VK_ACCESS_2_INDEX_READ_BIT;b.dstStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;b.dstAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;}
                before[0].buffer=m.vertices.handle;before[0].offset=m.vertex_offset;before[0].size=vb;before[1].buffer=m.indices.handle;before[1].offset=m.index_offset;before[1].size=ib;VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.bufferMemoryBarrierCount=2;d.pBufferMemoryBarriers=before.data();vkCmdPipelineBarrier2(cmd,&d);}
            VkBufferCopy v{0,m.vertex_offset,vb},i{vb,m.index_offset,ib};vkCmdCopyBuffer(cmd,staging.handle,m.vertices.handle,1,&v);vkCmdCopyBuffer(cmd,staging.handle,m.indices.handle,1,&i);
            std::array<VkBufferMemoryBarrier2,2> barriers{};for(auto& b:barriers){b.sType=VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;b.srcStageMask=VK_PIPELINE_STAGE_2_TRANSFER_BIT;b.srcAccessMask=VK_ACCESS_2_TRANSFER_WRITE_BIT;b.dstStageMask=VK_PIPELINE_STAGE_2_VERTEX_INPUT_BIT;b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;b.size=VK_WHOLE_SIZE;}
            barriers[0].buffer=m.vertices.handle;barriers[0].offset=m.vertex_offset;barriers[0].size=vb;barriers[0].dstAccessMask=VK_ACCESS_2_VERTEX_ATTRIBUTE_READ_BIT;barriers[1].buffer=m.indices.handle;barriers[1].offset=m.index_offset;barriers[1].size=ib;barriers[1].dstAccessMask=VK_ACCESS_2_INDEX_READ_BIT;VkDependencyInfo d{VK_STRUCTURE_TYPE_DEPENDENCY_INFO};d.bufferMemoryBarrierCount=2;d.pBufferMemoryBarriers=barriers.data();vkCmdPipelineBarrier2(cmd,&d);});
        m.ray_stamp=++ray.geometry_stamp;destroy_buffer(staging);invalidate_world_recipes(3,id);auto old=world.meshes.find(id);if(old!=world.meshes.end()){saved_shadows=false;retired.push_back({next_completion,{},old->second});old->second=m;}else world.meshes.emplace(id,m);stats.uploaded_bytes+=vb+ib;
    }catch(...){destroy_buffer(staging);destroy_mesh(m);throw;}
}
void register_world_mesh_range(uint64_t id,uint64_t source,uint32_t first,uint32_t count){
    auto found=world.meshes.find(source);
    if(recording || !id || id==source || found==world.meshes.end() || !count || first%3 || count%3 || uint64_t(first)+count>found->second.index_count)throw std::runtime_error("invalid shared geometry range");
    auto& parent=found->second;
    if(!parent.geometry_owner){auto allocation=std::make_shared<Mesh>(parent);parent.geometry_owner=std::move(allocation);}
    Mesh view=parent;view.index_offset+=uint64_t(first)*4;view.index_count=count;view.ray_stamp=++ray.geometry_stamp;
    invalidate_world_recipes(3,id);auto old=world.meshes.find(id);
    if(old!=world.meshes.end()){retired.push_back({next_completion,{},old->second});old->second=std::move(view);}else world.meshes.emplace(id,std::move(view));
    saved_shadows=false;
}
void upload_world_cube(uint64_t id,uint32_t width,uint32_t height,uint32_t format,bool srgb,const void* data,uint64_t bytes,uint32_t mip_count=1){
    if(recording || !data || !width || width!=height || width>16384 || format>8 || !mip_count || mip_count>std::bit_width(width))throw std::runtime_error("invalid world cube");
    static const VkFormat formats[]{VK_FORMAT_R8G8B8A8_UNORM,VK_FORMAT_B8G8R8A8_UNORM,VK_FORMAT_BC1_RGBA_UNORM_BLOCK,VK_FORMAT_BC2_UNORM_BLOCK,VK_FORMAT_BC3_UNORM_BLOCK,VK_FORMAT_BC4_UNORM_BLOCK,VK_FORMAT_BC5_UNORM_BLOCK,VK_FORMAT_BC6H_UFLOAT_BLOCK,VK_FORMAT_BC7_UNORM_BLOCK};
    static const VkFormat gamma[]{VK_FORMAT_R8G8B8A8_SRGB,VK_FORMAT_B8G8R8A8_SRGB,VK_FORMAT_BC1_RGBA_SRGB_BLOCK,VK_FORMAT_BC2_SRGB_BLOCK,VK_FORMAT_BC3_SRGB_BLOCK,VK_FORMAT_BC4_UNORM_BLOCK,VK_FORMAT_BC5_UNORM_BLOCK,VK_FORMAT_BC6H_UFLOAT_BLOCK,VK_FORMAT_BC7_SRGB_BLOCK};
    auto level_bytes=[&](uint32_t mip){uint32_t w=std::max(1u,width>>mip),h=std::max(1u,height>>mip);return format<2?uint64_t(w)*h*4:uint64_t((w+3)/4)*((h+3)/4)*((format==2 || format==5)?8:16);};
    uint64_t face_bytes=0;for(uint32_t mip=0;mip<mip_count;++mip)face_bytes+=level_bytes(mip);
    if(bytes!=face_bytes*6)throw std::runtime_error("world cube byte count mismatch");Buffer staging{};Image image{};
    try{staging=make_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_SRC_BIT,true);std::memcpy(staging.mapped,data,bytes);check(vmaFlushAllocation(allocator,staging.memory,0,bytes),"flush cube");image=make_image(width,height,srgb?gamma[format]:formats[format],VK_IMAGE_USAGE_TRANSFER_DST_BIT|VK_IMAGE_USAGE_SAMPLED_BIT,VK_IMAGE_ASPECT_COLOR_BIT,mip_count,6);
        immediate([&](VkCommandBuffer cmd){barrier(cmd,image.handle,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_NONE,0,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,mip_count,6);
            std::vector<VkBufferImageCopy> copies;copies.reserve(6*mip_count);uint64_t offset=0;
            for(uint32_t face=0;face<6;++face)for(uint32_t mip=0;mip<mip_count;++mip){VkBufferImageCopy copy{};copy.bufferOffset=offset;copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,mip,face,1};copy.imageExtent={std::max(1u,width>>mip),std::max(1u,height>>mip),1};copies.push_back(copy);offset+=level_bytes(mip);}
            vkCmdCopyBufferToImage(cmd,staging.handle,image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,uint32_t(copies.size()),copies.data());
            barrier(cmd,image.handle,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_IMAGE_ASPECT_COLOR_BIT,VK_PIPELINE_STAGE_2_TRANSFER_BIT,VK_ACCESS_2_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,mip_count,6);});
        destroy_buffer(staging);auto old=world.cubes.find(id);if(old!=world.cubes.end()){Texture t{};t.image=old->second;retired.push_back({next_completion,t,{}});old->second=image;}else world.cubes.emplace(id,image);stats.uploaded_bytes+=bytes;
    }catch(...){if(batch_command)wait(next_completion);destroy_buffer(staging);destroy_image(image);throw;}
}
void cleanup_world() noexcept {
    gbuffer_velocity_cleanup();
    for(auto& image:world.lighting_stages)destroy_image(image);
    for(auto& image:world.lighting_terms)destroy_image(image);if(world.lighting_terms_pipeline)vkDestroyPipeline(device,world.lighting_terms_pipeline,nullptr);
    if(world.lighting_terms_sampler)vkDestroySampler(device,world.lighting_terms_sampler,nullptr);
    cleanup_pass_scene();
    if(light_tiles.pipeline)vkDestroyPipeline(device,light_tiles.pipeline,nullptr);
    if(light_tiles.pipeline_layout)vkDestroyPipelineLayout(device,light_tiles.pipeline_layout,nullptr);
    if(light_tiles.layout)vkDestroyDescriptorSetLayout(device,light_tiles.layout,nullptr);
    light_tiles={};
    for(auto& slot:world.cascade_descriptor_pools)for(auto pool:slot)if(pool)vkDestroyDescriptorPool(device,pool,nullptr);
    for(auto& slot:world.cascade_pools)for(auto pool:slot)if(pool)vkDestroyCommandPool(device,pool,nullptr);
    for(uint32_t s=0;s<2;++s)for(uint32_t k=0;k<8;++k){if(world.spot_pools[s][k])vkDestroyCommandPool(device,world.spot_pools[s][k],nullptr);world.spot_pools[s][k]=VK_NULL_HANDLE;world.spot_commands[s][k].clear();world.spot_used[s][k]=0;}
    for(auto& p:world.pipelines){if(p.handle)vkDestroyPipeline(device,p.handle,nullptr);if(p.stock_cascade)vkDestroyPipeline(device,p.stock_cascade,nullptr);if(p.stock_spot)vkDestroyPipeline(device,p.stock_spot,nullptr);}
    for(auto p:world.other_hdr_lights)if(p)vkDestroyPipeline(device,p,nullptr);for(auto p:world.lights)if(p)vkDestroyPipeline(device,p,nullptr);if(world.compose)vkDestroyPipeline(device,world.compose,nullptr);if(world.resolve)vkDestroyPipeline(device,world.resolve,nullptr);if(world.fullscreen)vkDestroyShaderModule(device,world.fullscreen,nullptr);
    for(auto& b:world.indirect_buffers)destroy_buffer(b);
    for(auto& transient:world.atlas_transients){for(auto v:transient.views)vkDestroyImageView(device,v,nullptr);for(auto set:transient.sets)vkFreeDescriptorSets(device,descriptors,1,&set);}
    for(auto pipeline:world.copy_pipelines)if(pipeline)vkDestroyPipeline(device,pipeline,nullptr);if(world.copy_pipeline_layout)vkDestroyPipelineLayout(device,world.copy_pipeline_layout,nullptr);if(world.copy_layout)vkDestroyDescriptorSetLayout(device,world.copy_layout,nullptr);
    for(auto& s:world.slots){destroy_buffer(s.light_tile_gpu);destroy_buffer(s.light_tile_readback);destroy_buffer(s.light_tile_constants);destroy_buffer(s.frame);destroy_buffer(s.projections);destroy_buffer(s.objects);destroy_buffer(s.instances);destroy_buffer(s.history_projection);destroy_buffer(s.history_instances);destroy_buffer(s.point_lights);destroy_buffer(s.tiles);destroy_buffer(s.spots);destroy_buffer(s.glue);destroy_buffer(s.dimensions);if(s.pool)vkDestroyDescriptorPool(device,s.pool,nullptr);if(s.material_pool)vkDestroyDescriptorPool(device,s.material_pool,nullptr);}
    for(auto& p:world.meshes)destroy_mesh(p.second);for(auto& p:world.cubes)destroy_image(p.second);for(auto& p:world.arrays)destroy_image(p.second);for(auto& p:world.voxel_materials)destroy_buffer(p.second.constants);for(auto& i:world.gbuffer)destroy_image(i);destroy_image(world.hdr);destroy_image(world.shadows);cleanup_gbuffer_attachments();cleanup_object_motion();destroy_image(world.dither);destroy_image(world.black);destroy_image(world.ambient);destroy_buffer(world.quad);
    for(auto* pages:{&world.vertex_pages,&world.index_pages})for(auto& p:*pages){destroy_buffer(p.buffer);if(p.block)vmaDestroyVirtualBlock(p.block);}
    for(auto p:{world.material_pipeline_layout,world.light_pipeline_layout,world.spot_pipeline_layout})if(p)vkDestroyPipelineLayout(device,p,nullptr);
    for(auto p:{world.material_layout,world.light_layout,world.spot_layout})if(p)vkDestroyDescriptorSetLayout(device,p,nullptr);world={};++recipe_generation;
}
