// Stage-7 temporal jitter owner. While a temporal upscaler (DLSS or FSR) runs, the main view rasterizes with a
// Halton(2,3) sub-pixel offset: the frame block's projection matrices and the view-projection in slot.projections.
// Motion vectors, the GBuffer velocity contract and the transforms published to packs stay unjittered, as DX11's
// upscaler jitter owner leaves them. Camera cuts (invalidated history) restart the sequence.
struct JitterState {bool active{},forced{},reset{true};uint32_t index{},phases{};float pixel[2]{},ndc[2]{};uint64_t frames{};};
JitterState jitter{};
static float jitter_halton(uint32_t i,uint32_t base){float f=1,r=0;while(i){f/=float(base);r+=f*float(i%base);i/=base;}return r;}
void jitter_begin_frame(){
    uint32_t width=world.width?world.width:extent.width,height=world.height?world.height:extent.height;
    bool active=(jitter.forced||ngx_active()||fsr_active())&&width&&height;
    if(!active||jitter.reset)jitter.index=0;
    jitter.reset=false;jitter.active=active;jitter.pixel[0]=jitter.pixel[1]=jitter.ndc[0]=jitter.ndc[1]=0;
    if(!active){jitter.phases=0;return;}
    // FSR's phase count, 8 * (display / render)^2, also covers DLSS's sequence length.
    float ratio=float(std::max(1u,extent.width))/float(width);jitter.phases=std::max(8u,uint32_t(std::ceil(8.f*ratio*ratio)));
    uint32_t i=jitter.index%jitter.phases+1;jitter.index=(jitter.index+1)%jitter.phases;
    jitter.pixel[0]=jitter_halton(i,2)-.5f;jitter.pixel[1]=jitter_halton(i,3)-.5f;
    // Pixel offsets map to clip space as FSR/DLSS expect: +x right, +y down in pixels.
    jitter.ndc[0]=2.f*jitter.pixel[0]/float(width);jitter.ndc[1]=-2.f*jitter.pixel[1]/float(height);++jitter.frames;
}
// Row-vector clip = v*M (row-major, -Zpr): offset clip x/y by ndc*w, i.e. M*J with J[3][0]=jx, J[3][1]=jy.
void jitter_matrix(float* m){if(!jitter.active)return;for(uint32_t r=0;r<4;++r){m[r*4]+=jitter.ndc[0]*m[r*4+3];m[r*4+1]+=jitter.ndc[1]*m[r*4+3];}}
// inverse(M*J) = inverse(J)*inverse(M): row 3 loses jx*row 0 + jy*row 1.
void jitter_inverse(float* inv){if(!jitter.active)return;for(uint32_t c=0;c<4;++c)inv[12+c]-=jitter.ndc[0]*inv[c]+jitter.ndc[1]*inv[4+c];}
// Frame.hlsli EnvironmentSettings: projection 64, skybox projection 128, view-projection 192,
// inverse projection 320, inverse view-projection 384 (view and inverse view are unaffected).
void jitter_frame(uint8_t* frame){
    if(!jitter.active)return;float m[16];
    for(uint32_t at:{64u,128u,192u}){std::memcpy(m,frame+at,64);jitter_matrix(m);std::memcpy(frame+at,m,64);}
    for(uint32_t at:{320u,384u}){std::memcpy(m,frame+at,64);jitter_inverse(m);std::memcpy(frame+at,m,64);}
}
// PostPP billboards draw after upscaling at display resolution; like DX11 they see the unjittered frame block.
Buffer jitter_unjittered_frames[2]{};
Buffer& jitter_unjittered_frame(WorldSlot& slot){
    if(!jitter.active||slot.host_frame.size()!=1280)return slot.frame;
    auto& buffer=jitter_unjittered_frames[slot_index];world_buffer(buffer,1280,VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);
    std::memcpy(buffer.mapped,slot.host_frame.data(),1280);check(vmaFlushAllocation(allocator,buffer.memory,0,1280),"flush unjittered frame");return buffer;
}
void cleanup_jitter() noexcept {for(auto& buffer:jitter_unjittered_frames)destroy_buffer(buffer);}
