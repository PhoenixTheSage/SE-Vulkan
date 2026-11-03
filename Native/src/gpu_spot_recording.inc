// Inside render_spots: prepare the command bodies only. The primary retains
// each light's original barriers, load ops and static-before-overlay execution.
// A secondary owns its conditional block; the primary is never conditional,
// so inheritedConditionalRendering is deliberately not required.
struct GpuSpotPlan {
    uint32_t light,view,overlay;VkCommandBuffer statics{},dynamics{};
    uint32_t static_draws{},dynamic_draws{};
};
std::vector<GpuSpotPlan> gpu_spot_plans;
std::array<int32_t,64> gpu_spot_plan_of;gpu_spot_plan_of.fill(-1);
static const bool parallel_gpu_spots=env_flag("SE_VULKAN_GPU_OPAQUE_PARALLEL_SPOTS",true);
if(parallel_gpu_spots&&world.parallel_spots&&opaque_stock.ready&&opaque_reference.frame_ready){
    for(uint32_t light=0;light<64;++light){
        auto config=find_spot(light);if(!config||!(config->flags&1))continue;
        uint32_t view=UINT32_MAX,overlay=UINT32_MAX;
        for(uint32_t v=0;v<opaque_reference.view_count;++v){const auto& input=opaque_reference.views[v];
            if((input.flags&128)&&input.light==light&&view==UINT32_MAX)view=v;
            if((input.flags&136)==136&&input.light==light&&overlay==UINT32_MAX)overlay=v;
        }
        if(view==UINT32_MAX)continue;if(overlay==UINT32_MAX)overlay=view;
        // Copy descriptors refer to this frame's frozen image. Create/replace
        // it on the owner before recording any worker command that uses it.
        auto& frozen=spots.frozen[light];
        if(!frozen.handle||spots.frozen_sizes[light]!=spots.resolution){
            retire_image(frozen);frozen=make_image(spots.resolution,spots.resolution,VK_FORMAT_D32_SFLOAT,
                VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT,VK_IMAGE_ASPECT_DEPTH_BIT);
            spots.frozen_sizes[light]=spots.resolution;spots.frozen_ready[light]=0;spots.frozen_layout[light]=VK_IMAGE_LAYOUT_UNDEFINED;
        }
        prepare_spot_copy(light,frozen);
        gpu_spot_plan_of[light]=int32_t(gpu_spot_plans.size());gpu_spot_plans.push_back({light,view,overlay});
    }
    uint32_t workers=std::min<uint32_t>(uint32_t(gpu_spot_plans.size()),std::min(4u,sevk_jobs::shared().worker_count()+1));
    if(workers){
        const VkDescriptorSet stock_set=opaque_stock_view_set(slot,false);
        for(uint32_t k=0;k<workers;++k){auto& pool=world.spot_pools[slot_index][k];
            if(!pool){VkCommandPoolCreateInfo create{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};create.queueFamilyIndex=queue_family;
                check(vkCreateCommandPool(device,&create,nullptr,&pool),"create GPU spot recording pool");}
            check(vkResetCommandPool(device,pool,0),"reset fenced GPU spot recording pool");world.spot_used[slot_index][k]=0;
        }
        auto begin_secondary=[&](uint32_t k){auto& list=world.spot_commands[slot_index][k];auto& used=world.spot_used[slot_index][k];
            if(used==list.size()){VkCommandBufferAllocateInfo allocate{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};allocate.commandPool=world.spot_pools[slot_index][k];
                allocate.level=VK_COMMAND_BUFFER_LEVEL_SECONDARY;allocate.commandBufferCount=1;VkCommandBuffer buffer{};
                check(vkAllocateCommandBuffers(device,&allocate,&buffer),"allocate GPU spot secondary");list.push_back(buffer);}
            auto buffer=list[used++];
            VkCommandBufferInheritanceRenderingInfo rendering{VK_STRUCTURE_TYPE_COMMAND_BUFFER_INHERITANCE_RENDERING_INFO};rendering.depthAttachmentFormat=VK_FORMAT_D32_SFLOAT;rendering.rasterizationSamples=VK_SAMPLE_COUNT_1_BIT;
            VkCommandBufferInheritanceInfo inheritance{VK_STRUCTURE_TYPE_COMMAND_BUFFER_INHERITANCE_INFO,&rendering};
            VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};begin.flags=VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT|VK_COMMAND_BUFFER_USAGE_RENDER_PASS_CONTINUE_BIT;begin.pInheritanceInfo=&inheritance;
            check(vkBeginCommandBuffer(buffer,&begin),"begin GPU spot secondary");world_viewport(buffer,spots.resolution,spots.resolution);return buffer;
        };
        auto record=[&](uint32_t k){critical_profile::Bind bind(critical.record_worker(8+k));critical_profile::Scope scope(critical_profile::SpotRecord,true);
            struct ResetCondition{~ResetCondition(){gpu_owner_condition=UINT32_MAX;}} reset;
            for(size_t p=k;p<gpu_spot_plans.size();p+=workers){auto& plan=gpu_spot_plans[p];prepare_casters(plan.light,true);
                VkDescriptorSet shared_set=stock_set;
                auto body=[&](bool dynamic){auto target=begin_secondary(k);
                    uint32_t view=dynamic?plan.overlay:plan.view;
                    VkConditionalRenderingBeginInfoEXT conditional{VK_STRUCTURE_TYPE_CONDITIONAL_RENDERING_BEGIN_INFO_EXT};conditional.buffer=opaque_reference.slots[slot_index].counts.handle;
                    conditional.offset=(opaque_reference.view_count*opaque_reference.batch_count+1+view)*4ull;
                    gpu_owner_condition=uint32_t(conditional.offset/4);vkCmdBeginConditionalRenderingEXT(target,&conditional);
                    if(dynamic){vkCmdBindPipeline(target,VK_PIPELINE_BIND_POINT_GRAPHICS,spots.copy_pipeline);
                        vkCmdBindDescriptorSets(target,VK_PIPELINE_BIND_POINT_GRAPHICS,spots.copy_layout,0,1,&spots.copy_sets[slot_index][plan.light],0,nullptr);vkCmdDraw(target,3,1,0,0);}
                    else{VkClearAttachment clear{VK_IMAGE_ASPECT_DEPTH_BIT,0,{}};clear.clearValue.depthStencil={1,0};
                        VkClearRect rect{{{0,0},{spots.resolution,spots.resolution}},0,1};vkCmdClearAttachments(target,1,&clear,1,&rect);}
                    uint32_t count=draw_casters(target,plan.light,dynamic,&shared_set);
                    vkCmdEndConditionalRenderingEXT(target);gpu_owner_condition=UINT32_MAX;
                    check(vkEndCommandBuffer(target),"end GPU spot secondary");
                    if(dynamic){plan.dynamics=target;plan.dynamic_draws=count;}else{plan.statics=target;plan.static_draws=count;}
                };
                body(false);body(true);
            }
        };
        sevk_jobs::Group jobs;for(uint32_t k=1;k<workers;++k)sevk_jobs::shared().submit(jobs,sevk_jobs::Spot,sevk_jobs::Priority::High,[&,k]{record(k);});
        std::exception_ptr failure;try{record(0);}catch(...){failure=std::current_exception();}
        cpu_lap("GPU spot wait");try{jobs.wait();}catch(...){if(!failure)failure=std::current_exception();}
        if(failure)std::rethrow_exception(failure);world.spot_tasks=workers;
        cpu_lap("GPU spot execute");
    }
    record_count("GPU spot workers",double(workers));record_count("GPU spot plans",double(gpu_spot_plans.size()));
}
