void begin_uploads() {
    if(recording || batching_uploads)throw std::runtime_error("upload collection requires a closed frame and no nested batch");
    batching_uploads=true;
}
void ensure_upload_command() {
    if(batch_command)return;
    auto& slot=upload_slots[upload_index];
    if(slot.completion){uint64_t done{};check(vkGetSemaphoreCounterValue(device,timeline,&done),"query upload slot completion");if(done<slot.completion){++work.upload_slot_waits;wait(slot.completion);}}
    for(auto& b:slot.pending) {
        if(b.mapped && b.usage==VK_BUFFER_USAGE_TRANSFER_SRC_BIT && b.bytes<=64*1024*1024 && slot.cached_bytes+b.bytes<=64*1024*1024) {
            slot.cached_bytes+=b.bytes;slot.reusable.push_back(b);b={};
        }else destroy_buffer(b);
    }
    slot.pending.clear();
    if(!slot.pool){VkCommandPoolCreateInfo p{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};p.queueFamilyIndex=queue_family;p.flags=VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        check(vkCreateCommandPool(device,&p,nullptr,&slot.pool),"create batched upload pool");
        VkCommandBufferAllocateInfo a{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};a.commandPool=slot.pool;a.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY;a.commandBufferCount=1;
        check(vkAllocateCommandBuffers(device,&a,&slot.command),"allocate batched upload command");}
    check(vkResetCommandPool(device,slot.pool,0),"recycle upload commands");
    VkCommandBufferBeginInfo b{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};b.flags=VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    check(vkBeginCommandBuffer(slot.command,&b),"begin batched uploads");slot.completion=++next_completion;batch_command=slot.command;gpu_mark(batch_command,"upload batch begin",slot.completion);
}
void flush_uploads() {
    if(!batch_command)return;
    auto& slot=upload_slots[upload_index];
    gpu_mark(batch_command,"upload batch end",slot.completion);check(vkEndCommandBuffer(batch_command),"end batched uploads");
    VkCommandBufferSubmitInfo command{VK_STRUCTURE_TYPE_COMMAND_BUFFER_SUBMIT_INFO};command.commandBuffer=batch_command;
    VkSemaphoreSubmitInfo signal{VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO};signal.semaphore=timeline;signal.value=slot.completion;signal.stageMask=VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
    VkSubmitInfo2 submit{VK_STRUCTURE_TYPE_SUBMIT_INFO_2};submit.commandBufferInfoCount=1;submit.pCommandBufferInfos=&command;submit.signalSemaphoreInfoCount=1;submit.pSignalSemaphoreInfos=&signal;
    // The direct present shares the frame queue, so the batch joins it first. The decoupled present runs on its own
    // queue and never touches the frame queue: no join (it waited about 2 ms behind the worker's present).
    auto join_present=[&]{if(!(frame_decoupled&&!present_pending))this->join_present();};
    // Asynchronous submission keeps the batch in timeline order behind a frame submit still in the FIFO.
    if(async_submit_active()){submit_frame_async(batch_command,{},{signal},{});++upload_flushes;}
    // Census: the batch's wait for an asynchronous present in flight, then its queue submit.
    else if(pipeline_census){auto at=std::chrono::steady_clock::now();join_present();auto joined=std::chrono::steady_clock::now();check_gpu(vkQueueSubmit2(queue,1,&submit,VK_NULL_HANDLE),"submit batched uploads");
        upload_present_us+=uint64_t(std::chrono::duration_cast<std::chrono::microseconds>(joined-at).count());upload_submit_us+=uint64_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-joined).count());++upload_flushes;}
    else{join_present();check_gpu(vkQueueSubmit2(queue,1,&submit,VK_NULL_HANDLE),"submit batched uploads");}
    upload_fence=slot.completion;++work.upload_batches;batch_command={};upload_index=(upload_index+1)%upload_slots.size();
}
void end_uploads(){gap_mark("gap end_uploads start us");flush_uploads();batching_uploads=false;gap_mark("gap end_uploads end us");}
void cleanup_uploads() noexcept {
    batch_command={};batching_uploads=false;
    for(auto& slot:upload_slots){for(auto& b:slot.pending)destroy_buffer(b);for(auto& b:slot.reusable)destroy_buffer(b);if(slot.pool)vkDestroyCommandPool(device,slot.pool,nullptr);slot={};}
}
