using System;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanVoxels
{
    internal int CollectGuestDepth(Vector3D camera,BoundingFrustumD[] views)
    {
        RequireOwner();int cells=0;
        foreach(var actor in actors.Values) {
            if((actor.Flags&(RenderFlags.Visible|RenderFlags.CastShadows|RenderFlags.SkipInDepth))!=(RenderFlags.Visible|RenderFlags.CastShadows))continue;
            foreach(var cell in actor.Cells) {
                if(!cell.Visible || cell.Parts.Count==0)continue;
                var world=MatrixD.CreateScale(1<<cell.Lod)*MatrixD.CreateTranslation(cell.Offset)*actor.Matrix;
                var bounds=new BoundingBoxD(cell.Bounds.Min,cell.Bounds.Max).TransformFast(world);
                uint mask=VulkanScene.GuestDepthMask(views,bounds);if(mask==0)continue;
                world.Translation-=camera;
                foreach(var part in cell.Parts) {
                    uint pipeline=part.Material.SingleMaterial?20u:21u;
                    // Get retains main-frame material groups and can add a page
                    // for an offscreen caster. Budget/load failure cancels the
                    // shared query instead of replacing geometry with a proxy.
                    ulong material=Materials.Get(part.Material);
                    var constants=VulkanVoxelMaterials.Object(backend.WorldLayouts[pipeline],world,cell.Offset,cell.Lod,
                        actor.Dither,actor.SpherizeRadius,actor.SpherizeCenter-camera);
                    backend.AddWorldPacket(pipeline,part.Mesh,material,0,0,0,constants,null,1,4096|16384,mask,recordLogical:false);
                }
                cells++;
            }
        }
        return cells;
    }
}
