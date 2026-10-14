using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering;

/// <summary>Stable camera-relative orthographic cascades; depth replay is owned by native Vulkan.</summary>
internal sealed class VulkanShadows
{
    private float[] splits = new float[6];
    public readonly MyShadowsSettings Settings=new();
    private bool debugShadows=true;
    public void Process(MyRenderMessageBase message){if(message is MyRenderMessageUpdateShadowSettings s)Settings.CopyFrom(s.Settings);if(message is MyRenderMessageUpdateDebugOverrides d)debugShadows=d.Overrides.Shadows;if(message.MessageType==MyRenderMessageEnum.UnloadData){System.Array.Clear(fitted,0,8);schedule.Reset();frozenCameraInitialized=false;pending=false;retryUpdates=0;}}
    public float DrawDistance{get;private set;}=1500;
    private float[] matrices = new float[96];
    private readonly MatrixD[] worldMatrices=new MatrixD[8];private readonly bool[] fitted=new bool[8];
    private readonly BoundingFrustumD[] casterFrusta=new BoundingFrustumD[8];
    private int activeCascades=-1;
    public bool StockSchedulingEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_STOCK_SHADOW_SCHEDULE")=="1";
    public bool StockFittingEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_STOCK_SHADOW_FIT")=="1";
    public uint UpdateMask{get;private set;}
    public uint ValidMask{get;private set;}
    public NativeShadowFeedback UsageFeedback{get;private set;}
    private readonly ShadowCascadeSchedule schedule=new();
    private readonly Vector3D[] updatePositions=new Vector3D[8],lightDirections=new Vector3D[8];
    private readonly int[] framesSinceLightUpdate=new int[8];
    private bool frozenCameraInitialized,lastScheduling;
    private MatrixD frozenView,frozenProjection;
    private Vector3D frozenCamera;
    private int lastResolution;
    private bool lastFitting;
    private readonly MatrixD[] extrudedWorldMatrices=new MatrixD[8];
    private readonly Vector4[] cascadeScales=new Vector4[8];
    public readonly float[] ProjectionFactors=new float[8];
    public readonly Vector3D[] ViewOrigins=new Vector3D[8];
    private readonly Vector3D[] beforeOrigins=new Vector3D[8];
    public byte[] StockConstants{get;private set;}
    private readonly MatrixD[] beforeWorld=new MatrixD[8],beforeExtruded=new MatrixD[8];private readonly bool[] beforeFitted=new bool[8];
    private readonly Vector4[] beforeScales=new Vector4[8];private readonly Vector3D[] beforePositions=new Vector3D[8];private readonly float[] beforeFactors=new float[8];
    private bool pending,beforeScheduling,beforeFitting;private int beforeResolution,beforeCount,beforeActive;private uint retryUpdates;
    public void CommitPreparedFrame(){pending=false;retryUpdates&=~UpdateMask;}
    public void CancelPreparedFrame(){
        if(!pending)return;retryUpdates|=UpdateMask;System.Array.Copy(beforeWorld,worldMatrices,8);System.Array.Copy(beforeExtruded,extrudedWorldMatrices,8);System.Array.Copy(beforeFitted,fitted,8);System.Array.Copy(beforeScales,cascadeScales,8);System.Array.Copy(beforePositions,updatePositions,8);System.Array.Copy(beforeFactors,ProjectionFactors,8);
        System.Array.Copy(beforeOrigins,ViewOrigins,8);lastResolution=beforeResolution;lastScheduling=beforeScheduling;lastFitting=beforeFitting;activeCascades=beforeActive;if(splits.Length!=beforeCount){splits=new float[beforeCount];matrices=new float[beforeCount*16];}
        for(int c=0;c<8;c++)if(casterFrusta[c]!=null)casterFrusta[c].Matrix=worldMatrices[c];UpdateMask=0;pending=false;
    }
    internal BoundingFrustumD CasterFrustum(int cascade)=>casterFrusta[cascade];
    public uint CascadeMask(BoundingBoxD worldBounds, Vector3D camera)
    {
        uint mask = 0; int count = activeCascades<0?splits.Length:activeCascades;
        for (int cascade = 0; cascade < count && cascade < 8; cascade++)
        {
            if(StockSchedulingEnabled && (UpdateMask&(1u<<cascade))==0)continue;
            if(StockFittingEnabled && fitted[cascade]){float factor=ProjectionFactors[cascade],threshold=Settings.Cascades[cascade].SkippingSmallObjectThreshold;if(worldBounds.Size.LengthSquared()<=threshold*threshold/(factor*factor))continue;}
            if (!fitted[cascade] || casterFrusta[cascade]==null || casterFrusta[cascade].Contains(worldBounds)!=ContainmentType.Disjoint)mask|=1u<<cascade;
        }
        return mask;
    }
    public void Update(NativeBackend backend, MatrixD cameraView, Vector3 sunDirection,MyRenderSettings render,MatrixD cameraProjection,Vector3D camera)
    {
        if(StockFittingEnabled && !StockSchedulingEnabled)throw new System.InvalidOperationException("Stock fitting requires stock scheduling and retained cascade ownership.");
        if(StockSchedulingEnabled){beforeResolution=lastResolution;beforeScheduling=lastScheduling;beforeFitting=lastFitting;beforeCount=splits.Length;beforeActive=activeCascades;System.Array.Copy(worldMatrices,beforeWorld,8);System.Array.Copy(extrudedWorldMatrices,beforeExtruded,8);System.Array.Copy(fitted,beforeFitted,8);System.Array.Copy(cascadeScales,beforeScales,8);System.Array.Copy(updatePositions,beforePositions,8);System.Array.Copy(ProjectionFactors,beforeFactors,8);System.Array.Copy(ViewOrigins,beforeOrigins,8);pending=true;}
        int count=System.Math.Max(1,System.Math.Min(8,Settings.Data.CascadesCount));if(splits.Length!=count){splits=new float[count];matrices=new float[count*16];System.Array.Clear(fitted,0,8);schedule.Reset();}
        int resolution=render.User.ShadowMemoryQuality.ShadowCascadeResolution();if(resolution<256)resolution=1024;backend.SetShadowQuality((uint)resolution,System.Math.Max(0,System.Math.Min(1,Settings.Data.ZBias)));
        if(resolution!=lastResolution || StockSchedulingEnabled!=lastScheduling || StockFittingEnabled!=lastFitting){System.Array.Clear(fitted,0,8);schedule.Reset();frozenCameraInitialized=false;lastResolution=resolution;lastScheduling=StockSchedulingEnabled;lastFitting=StockFittingEnabled;}
        UpdateMask=0;ValidMask=(1u<<count)-1;
        backend.SetShadowOptions(Settings.Data.EnableShadowBlur && render.User.ShadowQuality>=MyShadowsQuality.HIGH);
        float distance=Settings.Data.ShadowCascadeMaxDistance;switch(render.User.ShadowQuality){case MyShadowsQuality.MEDIUM:distance*=Settings.Data.ShadowCascadeMaxDistanceMultiplierMedium;break;case MyShadowsQuality.HIGH:distance*=Settings.Data.ShadowCascadeMaxDistanceMultiplierHigh;break;case MyShadowsQuality.EXTREME:distance*=Settings.Data.ShadowCascadeMaxDistanceMultiplierExtreme;break;}
        DrawDistance=System.Math.Max(0,distance)+System.Math.Max(0,Settings.Data.ShadowCascadeZOffset);
        float spread=Settings.Data.ShadowCascadeSpreadFactor;for(int i=0;i<count;i++)splits[i]=(float)System.Math.Pow(System.Math.Max(2,distance),(i+1+spread)/(count+spread));
        Vector3D direction = sunDirection;
        if (!direction.IsValid() || (StockSchedulingEnabled && (!render.EnableShadows || !debugShadows)) || render.User.ShadowQuality==MyShadowsQuality.DISABLED || direction.LengthSquared() < 0.001) { activeCascades=0;backend.SetShadowCascades(System.Array.Empty<float>(), System.Array.Empty<float>());backend.SetShadowSchedule(StockSchedulingEnabled,0,0); return; }
        if(!StockSchedulingEnabled)direction.Normalize();
        if(!cameraProjection.IsValid() || System.Math.Abs(cameraProjection.M11)<1e-8 || System.Math.Abs(cameraProjection.M22)<1e-8 || !cameraView.IsValid()){activeCascades=0;backend.SetShadowCascades(System.Array.Empty<float>(),System.Array.Empty<float>());return;}
        MatrixD inverseView=MatrixD.Invert(cameraView);
        if(!inverseView.IsValid()){activeCascades=0;backend.SetShadowCascades(System.Array.Empty<float>(),System.Array.Empty<float>());return;}
        activeCascades=count;
        if(StockSchedulingEnabled){
            schedule.BeginFrame();
            UsageFeedback=backend.GetShadowFeedback();
            if(!render.ShadowCameraFrozen || !frozenCameraInitialized){frozenView=cameraView;frozenProjection=cameraProjection;frozenCamera=camera;frozenCameraInitialized=true;}
            cameraView=frozenView;cameraProjection=frozenProjection;inverseView=StockFittingEnabled?(MatrixD)Matrix.Invert((Matrix)cameraView):MatrixD.Invert(cameraView);
            for(int c=0;c<count;c++){++framesSinceLightUpdate[c];if(!fitted[c] || framesSinceLightUpdate[c]>c*Settings.Data.LightDirectionChangeDelayMultiplier || direction.Dot(lightDirections[c])<1-Settings.Data.LightDirectionDifferenceThreshold){lightDirections[c]=direction;framesSinceLightUpdate[c]=0;}}
        }
        var corners=new Vector3D[8];double x=1/System.Math.Abs(cameraProjection.M11),y=1/System.Math.Abs(cameraProjection.M22);
        MatrixD global=MatrixD.Identity;
        if(StockFittingEnabled){
            MatrixD stockProjection=cameraProjection;stockProjection.M13=stockProjection.M14-stockProjection.M13;stockProjection.M23=stockProjection.M24-stockProjection.M23;stockProjection.M33=stockProjection.M34-stockProjection.M33;stockProjection.M43=stockProjection.M44-stockProjection.M43;
            var inverseWorldProjection=MatrixD.Invert(MatrixD.CreateTranslation(-frozenCamera)*cameraView*stockProjection);Vector3D center=Vector3D.Zero;
            // Stock uses the array overload, which deliberately performs an
            // affine transform here; the scalar overload divides by W and the
            // infinite complementary projection has W=0 at the far corners.
            for(int i=0;i<8;i++){int corner=i%4;corners[i]=new Vector3D(corner<2?-1:1,corner==0||corner==3?-1:1,i<4?0:1);}
            Vector3D.Transform(corners,ref inverseWorldProjection,corners);foreach(var point in corners)center+=point;center/=8;
            global=MatrixD.CreateLookAt(center,center-direction,Vector3D.Up)*MatrixD.CreateOrthographic(1,1,0,1)*new MatrixD(.5,0,0,0,0,-.5,0,0,0,0,1,0,.5,.5,0,1);
        }
        Vector3D up=System.Math.Abs(direction.Y)>.99?Vector3D.Right:Vector3D.Up;
        for(int cascade=0;cascade<count;cascade++)
        {
            double near=cascade==0?1:splits[cascade-1],far=splits[cascade];
            bool cascadeFrozen=cascade<Settings.ShadowCascadeFrozen.Length && Settings.ShadowCascadeFrozen[cascade];
            bool retain=StockSchedulingEnabled?!schedule.IsDue(cascade,fitted[cascade],Settings.Data.UpdateCascadesEveryFrame,cascadeFrozen,near,Vector3D.DistanceSquared(updatePositions[cascade],frozenCamera)):(render.ShadowCameraFrozen || cascadeFrozen)&&fitted[cascade];
            if(StockSchedulingEnabled && !cascadeFrozen && (retryUpdates&(1u<<cascade))!=0)retain=false;
            if(StockSchedulingEnabled && !retain){
                updatePositions[cascade]=frozenCamera;
                bool used=schedule.IsUsed(cascade,render.ShadowCascadeUsageBasedSkip,UsageFeedback.Completion!=0,UsageFeedback.Pixels[cascade]);
                if(fitted[cascade] && !used){retain=true;ValidMask&=~(1u<<cascade);}
            }
            if(retain){System.Array.Copy(NativeBackend.MatrixValues(MatrixD.CreateTranslation(camera)*worldMatrices[cascade]),0,matrices,cascade*16,16);continue;}
            Vector3D cascadeDirection=StockSchedulingEnabled?lightDirections[cascade]:direction;
            up=System.Math.Abs(cascadeDirection.Y)>=.99?Vector3D.Right:Vector3D.Up;
            for(int i=0;i<8;i++){int corner=i%4;corners[i]=Vector3D.Transform(new Vector3D(StockFittingEnabled?(corner<2?-x:x):(i%2==0?-x:x),StockFittingEnabled?(corner==0||corner==3?-y:y):(i%4<2?-y:y),-1)*(i<4?near:far),inverseView);}
            BoundingSphereD sphere=BoundingSphereD.CreateFromPoints(corners);double radius=System.Math.Ceiling(sphere.Radius);
            if(StockSchedulingEnabled){Vector3D.Fract(ref frozenCamera,out var fraction);sphere.Center-=fraction;}
            // Stock frustum-sphere fitting, light direction and back extrusion.
            Vector3D eye=sphere.Center+cascadeDirection*(radius+1);
            MatrixD view=MatrixD.CreateLookAt(eye,eye-cascadeDirection,up);
            double back=2*radius+1+(StockFittingEnabled?(int)Settings.Data.ShadowCascadeZOffset:System.Math.Max(0,Settings.Data.ShadowCascadeZOffset));
            MatrixD projection=StockFittingEnabled?MatrixD.CreateOrthographicOffCenter(-radius,radius,-radius,radius,back,1):MatrixD.CreateOrthographicOffCenter(-radius,radius,-radius,radius,1,back);
            MatrixD extruded=view*MatrixD.CreateOrthographicOffCenter(-radius,radius,-radius,radius,back,-10000);
            ViewOrigins[cascade]=eye-cascadeDirection*back+(StockSchedulingEnabled?frozenCamera:camera);
            MatrixD fittedProjection=view*projection;
            if(StockFittingEnabled)fittedProjection=MatrixD.CreateTranslation(-frozenCamera)*fittedProjection;
            Vector3D snap=Vector3D.Transform(Vector3D.Zero,fittedProjection)*resolution/2;
            Vector3D offset=(snap.Round()-snap)*2/resolution;projection.M41+=offset.X;projection.M42+=offset.Y;
            MatrixD final=view*projection;if(!final.IsValid())throw new System.InvalidOperationException("Shadow fitting invalid: radius="+radius+" near="+near+" far="+far+" back="+back+" eye="+eye+" direction="+direction+" camera="+cameraView+" projection="+cameraProjection);
            // The current camera's translation adjusts uniforms, never the retained depth image.
            worldMatrices[cascade]=MatrixD.CreateTranslation(-(StockSchedulingEnabled?frozenCamera:camera))*final;fitted[cascade]=true;
            extrudedWorldMatrices[cascade]=MatrixD.CreateTranslation(-frozenCamera)*extruded;
            ProjectionFactors[cascade]=(float)(resolution*(double)resolution/(radius*radius*4));
            if(StockFittingEnabled){var inv=MatrixD.Invert(final);var start=Vector3D.Transform(Vector3D.Transform(new Vector3D(-1,-1,0),inv),global);var size=Vector3D.Transform(Vector3D.Transform(new Vector3D(1,1,1),inv),global)-start;cascadeScales[cascade]=new Vector4((float)(1/size.X),(float)(1/size.Y),(float)(1/size.Z),0);}
            updatePositions[cascade]=StockSchedulingEnabled?frozenCamera:camera;UpdateMask|=1u<<cascade;
            if(casterFrusta[cascade]==null)casterFrusta[cascade]=new BoundingFrustumD(worldMatrices[cascade]);else casterFrusta[cascade].Matrix=worldMatrices[cascade];
            System.Array.Copy(NativeBackend.MatrixValues(MatrixD.CreateTranslation(camera)*worldMatrices[cascade]),0,matrices,cascade*16,16);
        }
        backend.SetShadowCascades(matrices, splits);
        backend.SetShadowSchedule(StockSchedulingEnabled,UpdateMask,ValidMask,render.ShadowCascadeUsageBasedSkip);
        if(StockFittingEnabled){
            var constants=new float[300];MatrixD clip=new MatrixD(.5,0,0,0,0,-.5,0,0,0,0,1,0,.5,.5,0,1);
            for(int c=0;c<count;c++){
                System.Array.Copy(NativeBackend.MatrixValues(MatrixD.CreateTranslation(camera)*worldMatrices[c]*clip),0,constants,c*16,16);
                System.Array.Copy(NativeBackend.MatrixValues(MatrixD.CreateTranslation(camera)*extrudedWorldMatrices[c]*clip),0,constants,128+c*16,16);
                Vector4 scale=cascadeScales[c]/cascadeScales[0]+Vector4.One*c*.3f;scale*=c<4?3:c<5?2:1;
                constants[264+c*4]=(float)scale.X;constants[265+c*4]=(float)scale.Y;constants[266+c*4]=(float)scale.Z;constants[267+c*4]=0; // Unused stock padding; Scale.W is 0/0.
            }
            constants[256]=1;for(int c=1;c<8;c++)constants[256+c]=c<=count?splits[c-1]:1e20f;constants[296]=1f/resolution;constants[297]=Settings.Data.ZBias;
            var bytes=new byte[1200];System.Buffer.BlockCopy(constants,0,bytes,0,1200);System.Buffer.BlockCopy(System.BitConverter.GetBytes(count),0,bytes,1192,4);System.Buffer.BlockCopy(System.BitConverter.GetBytes(ValidMask),0,bytes,1196,4);StockConstants=bytes;backend.SetShadowPassConstants(bytes);
        }else{StockConstants=null;backend.SetShadowPassConstants(null);}
    }
}
