using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    internal BoundingFrustumD[] GuestDepthFrusta {get;private set;}=Array.Empty<BoundingFrustumD>();
    uint guestDepthGeneration;
    int guestDepthActors,guestDepthDraws,guestDepthVoxels;
    bool guestDepthComplete;
    string guestDepthStatus="Not requested";
    internal void BeginGuestDepthFrame()
    {
        GuestDepthFrusta=Array.Empty<BoundingFrustumD>();guestDepthComplete=false;
        guestDepthActors=guestDepthDraws=guestDepthVoxels=0;guestDepthStatus="Not requested";guestDepthGeneration++;
    }
    internal bool ConfigureGuestSceneDepth(ulong[] images,double[] matrices)
    {
        if(images==null || images.Length<1 || images.Length>3 || matrices==null || matrices.Length!=images.Length*16)
            throw new ArgumentException("Expected copied world matrices for one to three depth views");
        var frusta=new BoundingFrustumD[images.Length];var local=new List<float>();
        for(int i=0;i<images.Length;i++) {
            int at=i*16;
            var m=new MatrixD(matrices[at],matrices[at+1],matrices[at+2],matrices[at+3],
                matrices[at+4],matrices[at+5],matrices[at+6],matrices[at+7],
                matrices[at+8],matrices[at+9],matrices[at+10],matrices[at+11],
                matrices[at+12],matrices[at+13],matrices[at+14],matrices[at+15]);
            if(!m.IsValid())throw new ArgumentException("Nonfinite scene-depth matrix");
            frusta[i]=new BoundingFrustumD(m);
            local.AddRange(NativeBackend.MatrixValues(MatrixD.CreateTranslation(Environment.CameraPosition)*m));
        }
        if(!backend.ConfigureGuestDepthViews(images,local.ToArray()))return false;
        GuestDepthFrusta=frusta;guestDepthStatus="Scene-depth views requested";return true;
    }
    internal static uint GuestDepthMask(BoundingFrustumD[] views,BoundingBoxD bounds)
    {
        uint mask=0;for(int i=0;i<views.Length;i++)if(views[i].Contains(bounds)!=ContainmentType.Disjoint)mask|=1u<<(8+i);
        return mask;
    }
    internal void CollectGuestSceneDepth(VulkanVoxels voxels)
    {
        if(GuestDepthFrusta.Length==0)return;
        try {
            foreach(var actor in actors.Values) {
                if(actor.Actor.IsDestroyed || !IsVisible(actor.Actor) ||
                   (actor.Flags&(RenderFlags.CastShadows|RenderFlags.SkipInDepth))!=RenderFlags.CastShadows)continue;
                if(actor.Model==null)throw new InvalidOperationException("Guest depth caster model unavailable: "+actor.Actor.ID);
                if(actor.InstanceBuffer!=uint.MaxValue && !instances.ContainsKey(actor.InstanceBuffer))
                    throw new InvalidOperationException("Guest depth instance buffer unavailable: "+actor.InstanceBuffer);
                // Batch bounds are not authoritative for every instance. Keep
                // their actual streams and let the light projection clip them.
                uint mask=actor.StaticInstances!=null || actor.InstanceBuffer!=uint.MaxValue
                    ? ((1u<<GuestDepthFrusta.Length)-1)<<8 : GuestDepthMask(GuestDepthFrusta,actor.Bounds);
                if(mask==0)continue;
                var model=actor.Model;
                for(int lod=1;!model.Parts.Any(p=>DepthGeometrySegments.DrawsDepth(p.Technique)) && lod<=actor.Model.LodDescriptors.Length;lod++)
                    model=models.GetLod(actor.Model,lod);
                var previous=actor.RenderModel;
                try {
                    actor.RenderModel=model;PrepareInstances(actor,out uint type,out uint count,out byte[] stream,out _);
                    if(count==0)continue;
                    if(type==0 && actor.StaticInstances==null && actor.InstanceBuffer==uint.MaxValue && model.BlendWeights?.Length>0 && actor.SkinMatrices==null)
                        throw new InvalidOperationException("Guest depth character pose unavailable");
                    var world=actor.Actor.WorldMatrix;world.Translation-=Environment.CameraPosition;
                    foreach(var part in model.Parts) {
                        if(!DepthGeometrySegments.DrawsDepth(part.Technique) || (part.Flags&4)!=0)continue;
                        actor.Materials.TryGetValue(part.MaterialName??string.Empty,out var material);
                        uint pipeline=WorldShaderVariants.Pipeline(part.Technique,type,(part.Flags&1)!=0,(actor.Flags&RenderFlags.MetalnessColorable)!=0);
                        ulong color=TextureCache.GetTexture(material?.Textures.ColorMetalFileName??part.ColorTexture);
                        ulong normal=TextureCache.GetWorldNormal(material?.Textures.NormalGlossFileName??part.NormalTexture);
                        ulong extensions=TextureCache.GetWorldExtensions(material?.Textures.ExtensionsFileName??part.ExtensionTexture);
                        string alphaPath=material?.Textures.AlphamaskFileName??part.AlphaTexture;
                        ulong alpha=TextureCache.GetWorldAlpha(alphaPath);
                        if(!string.IsNullOrWhiteSpace(alphaPath) && alpha==0)throw new InvalidOperationException("Guest depth alpha texture unavailable: "+alphaPath);
                        byte[] Constants(float[] bones=null)=>WorldConstants.Object(backend.WorldLayouts[pipeline],world,
                            (material?.Color??actor.Color).ToVector3(),actor.Hsv+colorizeHsv,material?.Emissivity??1,0,bones,actor.Dithering,true);
                        if(type==1) {
                            models.EnsureSkinParts(model,part);
                            foreach(var skin in part.SkinParts) {
                                var bones=new float[60*16];
                                for(int i=0;i<60;i++) {
                                    Matrix bone=Matrix.Identity;
                                    if(i<skin.Bones.Length) {
                                        int index=skin.Bones[i];if(index<0 || index>=actor.SkinMatrices.Length)throw new InvalidOperationException("Guest depth skin palette unavailable");
                                        bone=actor.SkinMatrices[index];
                                    }
                                    Array.Copy(NativeBackend.MatrixValues(bone),0,bones,i*16,16);
                                }
                                backend.AddWorldPacket(pipeline,skin.Mesh,color,normal,extensions,alpha,Constants(bones),null,1,4096|16384,mask,recordLogical:false);
                                guestDepthDraws++;
                            }
                        } else {
                            backend.AddWorldPacket(pipeline,part.Mesh,color,normal,extensions,alpha,Constants(),stream,count,4096|16384,mask,recordLogical:false);
                            guestDepthDraws++;
                        }
                    }
                    guestDepthActors++;
                } finally { actor.RenderModel=previous; }
            }
            guestDepthVoxels=voxels.CollectGuestDepth(Environment.CameraPosition,GuestDepthFrusta);
            guestDepthComplete=true;guestDepthStatus="Current actor/voxel depth sources collected";
        } catch(Exception error) {
            guestDepthComplete=false;guestDepthStatus="Scene-depth collection unavailable: "+error.Message;
            GuestDepthFrusta=Array.Empty<BoundingFrustumD>();backend.ConfigureGuestDepthViews(Array.Empty<ulong>(),null);
        }
    }
    internal object[] CaptureGuestSceneDepth()=>new object[]{guestDepthComplete,guestDepthGeneration,guestDepthActors,guestDepthDraws,guestDepthVoxels,guestDepthStatus};
    internal object[] CaptureGuestVolumeScene()=>new object[]{guestDepthGeneration,new[]{Environment.CameraPosition.X,Environment.CameraPosition.Y,Environment.CameraPosition.Z},Environment.FrameTime.Seconds,Environment.LastFrameDelta};
}
