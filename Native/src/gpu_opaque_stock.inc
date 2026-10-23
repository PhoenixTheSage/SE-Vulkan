void cleanup_opaque_stock(){
    auto& s=opaque_stock;for(auto* list:{&s.newer})for(auto& p:*list){if(p.back)vkDestroyPipeline(device,p.back,nullptr);if(p.no_cull)vkDestroyPipeline(device,p.no_cull,nullptr);if(p.layout)vkDestroyPipelineLayout(device,p.layout,nullptr);}
    for(auto& p:s.older){if(p.back)vkDestroyPipeline(device,p.back,nullptr);if(p.no_cull)vkDestroyPipeline(device,p.no_cull,nullptr);if(p.layout)vkDestroyPipelineLayout(device,p.layout,nullptr);}
    if(s.pool)vkDestroyDescriptorPool(device,s.pool,nullptr);if(s.indexed_layout)vkDestroyDescriptorSetLayout(device,s.indexed_layout,nullptr);s={};
}
void init_opaque_stock_layout(){
    auto& s=opaque_stock;if(s.indexed_layout)return;
    std::array<VkDescriptorSetLayoutBinding,6> b{};for(uint32_t n=0;n<4;n++)b[n]={128+n,VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,1,VK_SHADER_STAGE_VERTEX_BIT|VK_SHADER_STAGE_FRAGMENT_BIT,nullptr};
    for(uint32_t n=4;n<6;n++)b[n]={128+n,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,gpu_opaque::texture_capacity,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr};
    VkDescriptorSetLayoutCreateInfo create{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};create.bindingCount=uint32_t(b.size());create.pBindings=b.data();check(vkCreateDescriptorSetLayout(device,&create,nullptr,&s.indexed_layout),"create indexed stock input layout");
}
VkShaderModule opaque_stock_shader(const void* data,uint64_t bytes,VkShaderStageFlagBits stage,SpvReflectShaderModule& reflected){
    if(!opaque_caps.enabled||!data||bytes<20||bytes%4||bytes>16*1024*1024)throw std::runtime_error("invalid indexed stock shader/admission");
    auto* words=static_cast<const uint32_t*>(data);for(uint64_t i=5;i<bytes/4;){uint32_t n=words[i]>>16,op=words[i]&65535;if(!n||n>bytes/4-i)throw std::runtime_error("malformed indexed stock SPIR-V");
        if(op==10){const char* name=reinterpret_cast<const char*>(words+i+1);if(std::strcmp(name,"SPV_EXT_descriptor_indexing")!=0)throw std::runtime_error("indexed stock execution extension outside admitted core");}
        if(op==17){uint32_t c=words[i+1];if(c!=1&&c!=50&&c!=5379&&c!=4427&&c!=5301&&c!=5302&&c!=5307)throw std::runtime_error("indexed stock capability outside admitted contract: "+std::to_string(c));}i+=n;}
    if(spvReflectCreateShaderModule(bytes,data,&reflected)!=SPV_REFLECT_RESULT_SUCCESS)throw std::runtime_error("indexed stock reflection failed");
    if(uint32_t(reflected.shader_stage)!=uint32_t(stage))throw std::runtime_error("indexed stock stage changed");
    VkShaderModuleCreateInfo create{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};create.codeSize=bytes;create.pCode=words;VkShaderModule shader{};check(vkCreateShaderModule(device,&create,nullptr,&shader),"create authenticated indexed stock shader");return shader;
}
OpaqueStockState::Pipeline build_opaque_stock_pipeline(uint32_t id,uint32_t old,const void* vertex,uint64_t vb,const void* pixel,uint64_t pb){
    if(recording||old>1||id>=(old?2656u:28u) || (old && world_unfade(id)>=96)||!opaque_caps.enabled)throw std::runtime_error("invalid indexed stock pipeline candidate");
    uint32_t role=old?world_unfade(id)/32:id<12?id/4:0;bool depth=role==1,forward=role==2,cutout=old?world_cutout(id):id>=12;
    VkShaderModule vs{},ps{};SpvReflectShaderModule vr{},pr{};VkPipeline back{},no_cull{};VkPipelineLayout layout{};
    // GPU NEW GBuffer owners admit DX11's USE_SIMPLE_INSTANCING contract; GPU OLD classic streams (below 20)
    // admit the per-draw currToPrev contract. Both read set 2.
    bool admitted=gbuffer_velocity_supported&&role==0&&(!old||world_unfade(id)<20),velocity_resources=false;
    try{
        vs=opaque_stock_shader(vertex,vb,VK_SHADER_STAGE_VERTEX_BIT,vr);ps=opaque_stock_shader(pixel,pb,VK_SHADER_STAGE_FRAGMENT_BIT,pr);
        for(auto* module:{&vr,&pr})for(uint32_t n=0;n<module->descriptor_binding_count;n++){auto& b=module->descriptor_bindings[n];
            bool indexed=b.set==1&&((b.binding>=128&&b.binding<=131&&b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_STORAGE_BUFFER&&b.count==1)||(b.binding>=132&&b.binding<=133&&b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&b.count==0));
            bool stock=b.set==0&&b.count==1&&((b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER&&((b.binding==0&&b.block.size==1280)||(b.binding==1&&b.block.size==64)||(forward&&((b.binding==4&&b.block.size==1200)||(b.binding==7&&b.block.size==64)))))||(b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE&&(b.binding==156||(forward&&b.binding==144)))||(b.descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER&&(b.binding==258||b.binding==259||(forward&&b.binding==271))));
            if(admitted&&gbuffer_velocity_binding(b)){velocity_resources=true;continue;}
            if(!indexed&&!stock)throw std::runtime_error("indexed stock resource layout exceeded: "+std::to_string(b.set)+":"+std::to_string(b.binding)+" count "+std::to_string(b.count));}
        for(auto* module:{&vr,&pr})for(uint32_t n=0;n<module->push_constant_block_count;n++)if(module->push_constant_blocks[n].size!=4)throw std::runtime_error("indexed stock draw-base contract");
        // Auto-assigned locations must still link after adding the flat draw identity (Vulkan interface matching).
        validate_stage_link(vr,pr,"indexed stock");
        std::vector<VkVertexInputAttributeDescription> attributes;
        for(uint32_t n=0;n<vr.input_variable_count;n++){auto& v=*vr.input_variables[n];if(v.decoration_flags&SPV_REFLECT_DECORATION_BUILT_IN)continue;std::string name=v.name?v.name:"";name=name.substr(name.find_last_of('.')+1);VkFormat format{};uint32_t offset{};
            if(name=="POSITION0"){format=VK_FORMAT_R32G32B32A32_SFLOAT;}else if(name=="NORMAL0"){format=VK_FORMAT_R32G32_UINT;offset=16;}else if(name=="TANGENT0"){format=VK_FORMAT_R32G32B32A32_SFLOAT;offset=24;}else if(name=="TEXCOORD0"){format=VK_FORMAT_R32G32_SFLOAT;offset=40;}else if(name=="BLENDWEIGHT0"){format=VK_FORMAT_R32G32B32A32_SFLOAT;offset=48;}else if(name=="BLENDINDICES0"){format=VK_FORMAT_R32G32B32A32_UINT;offset=64;}else throw std::runtime_error("indexed stock retains an unsupported/source instance attribute: "+name);attributes.push_back({v.location,0,format,offset});}
        bool velocity=false;std::array<VkFormat,7> velocity_formats{};uint32_t velocity_count=gbuffer_owner_formats(pr,admitted,velocity,velocity_formats);
        if(velocity_resources&&!velocity)throw std::runtime_error("indexed stock pipeline binds velocity resources without writing Target3");
        if(velocity)gbuffer_velocity_layouts();
        VkDescriptorSetLayout sets[]{forward?probes.material_layout:world.material_layout,opaque_stock.indexed_layout,gbuffer_velocity.layout};VkPushConstantRange push{VK_SHADER_STAGE_VERTEX_BIT|VK_SHADER_STAGE_FRAGMENT_BIT,0,4};
        VkPipelineLayoutCreateInfo create{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};create.setLayoutCount=velocity?3:2;create.pSetLayouts=sets;create.pushConstantRangeCount=1;create.pPushConstantRanges=&push;check(vkCreatePipelineLayout(device,&create,nullptr,&layout),"create indexed stock pipeline layout");
        if(forward)require_probe_format();const VkFormat forward_formats[]{stock_probe_format,stock_probe_format};
        auto formats=depth?nullptr:forward?forward_formats:velocity?velocity_formats.data():stock_formats.data();uint32_t count=depth?0:forward?2:velocity?velocity_count:3;
        back=world_graphics(vs,"__vertex_shader",ps,"__pixel_shader",layout,{{0,uint32_t(sizeof(SevkWorldVertex)),VK_VERTEX_INPUT_RATE_VERTEX}},attributes,formats,count,true,depth,false,false,0,cutout,{},false,false,depth,depth,VK_CULL_MODE_BACK_BIT,depth?VK_FRONT_FACE_COUNTER_CLOCKWISE:VK_FRONT_FACE_CLOCKWISE);
        no_cull=world_graphics(vs,"__vertex_shader",ps,"__pixel_shader",layout,{{0,uint32_t(sizeof(SevkWorldVertex)),VK_VERTEX_INPUT_RATE_VERTEX}},attributes,formats,count,true,depth,false,false,0,cutout,{},false,false,depth,false,VK_CULL_MODE_NONE);
        OpaqueStockState::Pipeline result{back,no_cull,layout,velocity};back=no_cull={};layout={};spvReflectDestroyShaderModule(&vr);spvReflectDestroyShaderModule(&pr);vkDestroyShaderModule(device,vs,nullptr);vkDestroyShaderModule(device,ps,nullptr);return result;
    }catch(...){if(back)vkDestroyPipeline(device,back,nullptr);if(no_cull)vkDestroyPipeline(device,no_cull,nullptr);if(layout)vkDestroyPipelineLayout(device,layout,nullptr);if(vr._internal)spvReflectDestroyShaderModule(&vr);if(pr._internal)spvReflectDestroyShaderModule(&pr);if(vs)vkDestroyShaderModule(device,vs,nullptr);if(ps)vkDestroyShaderModule(device,ps,nullptr);throw;}
}
void init_opaque_stock_pipeline(uint32_t id,uint32_t old,const void* vertex,uint64_t vb,const void* pixel,uint64_t pb){
    if(recording||old>1||id>=(old?2656u:28u) || (old && world_unfade(id)>=96)||!opaque_caps.enabled)throw std::runtime_error("invalid indexed stock pipeline");
    auto& dst=old?opaque_stock.older[id]:opaque_stock.newer[id];if(dst.back)throw std::runtime_error("indexed stock pipeline already registered");
    wait(next_completion);init_world_layouts();init_opaque_stock_layout();dst=build_opaque_stock_pipeline(id,old,vertex,vb,pixel,pb);
}

void validate_opaque_stock_geometry(){
    GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::StockGeometry);
    for(auto& g:opaque_stock.geometry){auto it=world.meshes.find(g.id);
        if(it==world.meshes.end()||it->second.ray_stamp!=g.stamp||it->second.vertices.handle!=g.vertices||it->second.indices.handle!=g.indices||it->second.vertex_offset!=g.vertex_offset||it->second.index_offset!=g.index_offset)
            throw std::runtime_error("resident stock geometry generation disappeared; return ownership before publication");}
}
void set_opaque_stock_batches(const SevkOpaqueStockBatch* inputs,uint32_t count,const uint64_t* meshes,uint32_t parts,const void* objects,uint32_t bytes){
    GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::StockPublication);
    auto& s=opaque_stock;auto& r=opaque_reference;
    if(recording||batching_uploads||!r.initialized||!inputs||count!=r.batch_count||!meshes||parts!=r.part_count||!objects||bytes!=r.actors.size()*128)
        throw std::runtime_error("invalid immutable stock batches");
    std::vector<OpaqueStockState::Batch> batches;std::vector<OpaqueStockState::Geometry> geometry;std::unordered_set<uint64_t> geometry_ids;auto derived=r.part_inputs;std::vector<int> family_kind(r.family_inputs.size(),-1);
    if(gpu_resident_deltas)for(auto& actor:r.actors){int kind=bool(actor.flags&2);auto& prior=family_kind[actor.family];if(prior!=-1&&prior!=kind)throw std::runtime_error("stock family mixes OLD/NEW input contracts");prior=kind;}
    for(uint32_t b=0;b<count;b++){auto input=inputs[b];auto found=world.meshes.find(input.mesh);
        if(input.old>1||(input.flags&~1u)||input.reserved||found==world.meshes.end()||input.main_pipeline>=(input.old?2656u:28u)||input.depth_pipeline>=(input.old?2656u:28u) || (input.old && (world_unfade(input.main_pipeline)>=96 || world_unfade(input.depth_pipeline)>=96)))throw std::runtime_error("invalid stock pipeline/page batch");
        auto& main=input.old?s.older[input.main_pipeline]:s.newer[input.main_pipeline];auto& depth=input.old?s.older[input.depth_pipeline]:s.newer[input.depth_pipeline];
        if(!main.back||!depth.back)throw std::runtime_error("stock batch shader unavailable");
        batches.push_back({input,found->second.vertices.handle,found->second.indices.handle});
    }
    for(uint32_t p=0;p<parts;p++){auto& part=derived[p];auto it=world.meshes.find(meshes[p]);
        if(it==world.meshes.end()||uint64_t(part.first_index)+part.index_count>it->second.index_count)throw std::runtime_error("stock part outside shared model geometry");
        auto& mesh=it->second;auto& batch=batches.at(part.reserved);
        // Caller padding stays zero; derived metadata records shader fade dependency.
        part.padding=batch.input.old&&batch.input.depth_pipeline>=2560?1u:0u;
        if(mesh.vertex_stride!=sizeof(SevkWorldVertex)||mesh.vertices.handle!=batch.vertices||mesh.indices.handle!=batch.indices||mesh.vertex_offset%sizeof(SevkWorldVertex)||mesh.index_offset%4||mesh.vertex_offset/sizeof(SevkWorldVertex)>INT32_MAX||mesh.index_offset/4+part.first_index>UINT32_MAX)
            throw std::runtime_error("stock part does not belong to pipeline/geometry-page batch");
        if(gpu_resident_deltas){if(family_kind[part.family]!=-1&&family_kind[part.family]!=int(batch.input.old))throw std::runtime_error("stock batch mixes OLD/NEW input contracts");}else for(auto& actor:r.actors)if(actor.family==part.family&&bool(actor.flags&2)!=bool(batch.input.old))throw std::runtime_error("stock batch mixes OLD/NEW input contracts");
        if(!gpu_owned_inputs||geometry_ids.insert(meshes[p]).second)geometry.push_back({meshes[p],mesh.ray_stamp,mesh.vertices.handle,mesh.indices.handle,mesh.vertex_offset,mesh.index_offset});
        part.first_index+=uint32_t(mesh.index_offset/4);part.vertex_offset=int32_t(mesh.vertex_offset/sizeof(SevkWorldVertex));
    }
    // Immutable registration only. Live pose/appearance updates use slot-owned
    // commits; they must never call this cold topology publication every frame.
    wait(next_completion);std::memcpy(r.parts.mapped,derived.data(),derived.size()*sizeof(SevkOpaquePart));check(vmaFlushAllocation(allocator,r.parts.memory,0,r.parts.bytes),"flush immutable stock page ranges");
    r.gpu_part_inputs=std::move(derived);if(gpu_owned_inputs)for(auto& slot:r.slots)slot.pending_parts.emplace_back(0,r.part_count);
    for(uint32_t i=0;i<r.actors.size();i++)r.object_inputs.object(i,static_cast<const uint8_t*>(objects)+i*128);for(auto& version:r.versions)version=++r.revision;
    for(auto& slot:r.slots)opaque_reference_placeholder(slot.stock_objects);
    if(s.pool)vkDestroyDescriptorPool(device,s.pool,nullptr);s.pool={};s.indexed_sets={};s.batches=std::move(batches);s.geometry=std::move(geometry);s.materials_dirty={true,true};s.ready=true;
}
void set_opaque_stock_inputs(uint64_t mesh,const void* objects,uint32_t bytes,uint32_t old,uint32_t main,uint32_t depth){
    SevkOpaqueStockBatch batch{mesh,old,main,depth,UINT32_MAX};std::vector<uint64_t> meshes(opaque_reference.part_count,mesh);
    set_opaque_stock_batches(&batch,1,meshes.data(),uint32_t(meshes.size()),objects,bytes);
}
void prepare_opaque_stock(WorldSlot& world_slot){
    auto& reference=opaque_reference;
    {GpuSceneCostScope cost(gpu_scene_costs,GpuSceneCosts::RecordingMembership);
    if(reference.view_batches)reference.recording_batches=gpu_opaque::recording_batches(reference.actors,reference.recording_families,reference.views,reference.batch_count);}
    auto& s=opaque_stock;auto& r=opaque_reference;if(!s.ready)return;validate_opaque_stock_geometry();
    if(!s.pool){VkDescriptorPoolSize sizes[]{{VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,16},{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,gpu_opaque::texture_capacity*8}};VkDescriptorPoolCreateInfo create{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};create.maxSets=4;create.poolSizeCount=2;create.pPoolSizes=sizes;check(vkCreateDescriptorPool(device,&create,nullptr,&s.pool),"create fenced stock indexed descriptor pool");}
    auto& slot=r.slots[slot_index];bool dirty=s.materials_dirty[slot_index];
    for(uint32_t depth=0;depth<2;depth++){auto& set=s.indexed_sets[slot_index][depth];if(!set){VkDescriptorSetAllocateInfo alloc{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};alloc.descriptorPool=s.pool;alloc.descriptorSetCount=1;alloc.pSetLayouts=&s.indexed_layout;check(vkAllocateDescriptorSets(device,&alloc,&set),"allocate fenced indexed stock set");dirty=true;
            Buffer* buffers[]{depth?&slot.stock_depth:&slot.stock_colour,&slot.stock_objects,&r.sources,&slot.stock_draws};VkDescriptorBufferInfo infos[4]{};VkWriteDescriptorSet writes[4]{};for(uint32_t b=0;b<4;b++){infos[b]={buffers[b]->handle,0,buffers[b]->bytes};writes[b]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[b].dstSet=set;writes[b].dstBinding=128+b;writes[b].descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;writes[b].descriptorCount=1;writes[b].pBufferInfo=&infos[b];}vkUpdateDescriptorSets(device,4,writes,0,nullptr);}
        if(dirty){std::vector<VkDescriptorImageInfo> images(r.material_ids.size());for(uint32_t t=0;t<images.size();t++)images[t]={VK_NULL_HANDLE,textures.at(r.material_ids[t]).image.view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};VkWriteDescriptorSet writes[2]{};for(uint32_t b=0;b<2;b++){writes[b]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};writes[b].dstSet=set;writes[b].dstBinding=132+b;writes[b].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;writes[b].descriptorCount=uint32_t(images.size());writes[b].pImageInfo=images.data();}vkUpdateDescriptorSets(device,2,writes,0,nullptr);}}
    s.materials_dirty[slot_index]=false;
}
uint32_t record_opaque_stock_view(VkCommandBuffer cmd,WorldSlot& slot,uint32_t view,bool spot,Buffer* probe_frame,Buffer* probe_projection,uint32_t light,VkDescriptorPool descriptors=nullptr,int main_phase=-1,uint32_t only_batch=UINT32_MAX,VkDescriptorSet* shared_set=nullptr){
    auto& s=opaque_stock;auto& r=opaque_reference;uint32_t original_view=view;if(gpu_resident.published){if(view>=64)return 0;view=gpu_resident.view_map[view];}if(!s.ready||!r.frame_ready||view>=r.view_count||!(r.views[view].flags&1))return 0;
    bool depth=(r.views[view].flags&2)!=0,forward=probe_frame!=nullptr;
    if(r.view_batches&&r.recording_batches.at(view).empty())return 0;
    auto set=shared_set?*shared_set:VK_NULL_HANDLE;
    if(!set){set=world_set(slot,forward?probes.material_layout:world.material_layout,descriptors);
    world_write_buffer(set,0,forward?*probe_frame:slot.frame,1280,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
    world_write_buffer(set,1,forward?*probe_projection:slot.projections,64,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);
    world_write_buffer(set,2,r.slots[slot_index].stock_objects,128,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC);
    world_write_image(set,156,stock.dither.view);for(uint32_t binding:{258u,259u})world_write_image(set,binding,{},standard_sampler(binding),true);
    if(forward){world_write_buffer(set,4,transparency.shadow_constants[slot_index],1200,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_buffer(set,7,probes.forward_projection[slot_index][probes.work_face],64,VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER);world_write_image(set,144,transparency.csm.view);world_write_image(set,271,{},shadow_sampler,true);}
    if(shared_set)*shared_set=set;}
    uint32_t offsets[]{depth?uint32_t(world_align(64)*(spot?9+light:original_view)):0,0};VkDescriptorSet sets[]{set,s.indexed_sets[slot_index][depth?1:0]};uint32_t commands=0;
    VkPipeline bound_pipeline{};VkPipelineLayout bound_layout{};VkBuffer bound_vertices{},bound_indices{};
    uint32_t size=r.view_batches?uint32_t(r.recording_batches.at(view).size()):uint32_t(s.batches.size());
    // Main callers supply shared group/part order; depth/probe keep their view path.
    for(uint32_t phase=0;phase<(depth||forward||main_phase>=0?1u:2u);++phase)
    for(uint32_t n=0;n<(only_batch==UINT32_MAX?size:1u);n++){uint32_t b=only_batch!=UINT32_MAX?only_batch:r.view_batches?r.recording_batches[view][n]:n;auto& batch=s.batches[b];auto& input=batch.input;if(forward&&input.forward_pipeline==UINT32_MAX)continue;bool decal=input.old?world_cutout(input.main_pipeline):input.main_pipeline>=12;if(!depth&&!forward&&(main_phase>=0?main_geometry_phase(input.old!=0,decal,decal&&input.old?world_decal_technique_order(input.main_pipeline):0u)!=uint32_t(main_phase):decal!=(phase!=0)))continue;auto range=r.ranges[b];if(!range.capacity)continue;uint32_t id=depth?input.depth_pipeline:forward?input.forward_pipeline:input.main_pipeline;
        if(id>=(input.old?2656u:28u) || (input.old && world_unfade(id)>=96))throw std::runtime_error("stock batch forward shader unavailable");auto& pipeline=input.old?s.older[id]:s.newer[id];if(!pipeline.back)throw std::runtime_error("stock batch pipeline unavailable");
        auto handle=spot||(!depth&&(input.flags&1))?pipeline.no_cull:pipeline.back;
        if(!r.view_batches||handle!=bound_pipeline){vkCmdBindPipeline(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,handle);bound_pipeline=handle;}
        if(!r.view_batches||pipeline.layout!=bound_layout){vkCmdBindDescriptorSets(cmd,VK_PIPELINE_BIND_POINT_GRAPHICS,pipeline.layout,0,2,sets,2,offsets);if(pipeline.velocity)gbuffer_velocity_bind_gpu(cmd,pipeline.layout);bound_layout=pipeline.layout;}
        VkDeviceSize vertex_offset=0;if(!r.view_batches||batch.vertices!=bound_vertices){vkCmdBindVertexBuffers(cmd,0,1,&batch.vertices,&vertex_offset);bound_vertices=batch.vertices;}if(!r.view_batches||batch.indices!=bound_indices){vkCmdBindIndexBuffer(cmd,batch.indices,0,VK_INDEX_TYPE_UINT32);bound_indices=batch.indices;}
        uint32_t batch_view=b*r.view_count+view,base=view*r.draw_capacity+range.first;vkCmdPushConstants(cmd,pipeline.layout,VK_SHADER_STAGE_VERTEX_BIT|VK_SHADER_STAGE_FRAGMENT_BIT,0,4,&base);
        vkCmdDrawIndexedIndirectCount(cmd,r.slots[slot_index].args.handle,uint64_t(base)*sizeof(SevkOpaqueIndirect),r.slots[slot_index].counts.handle,batch_view*4,range.capacity,sizeof(SevkOpaqueIndirect));gpu_owner_record((input.old?49153u:32769u)+2*id,batch_view,base,range.capacity);commands++;
    }return commands;
}
