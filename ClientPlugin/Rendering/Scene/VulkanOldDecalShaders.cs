using System.Collections.Generic;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    // MyMaterialShaders.Get omits TextureTypes from its process-wide key.
    // STATIC_DECAL is shared by DECAL and DECAL_NOPREMULT. Keep the first
    // bundle's macros separate from each part's actual blend/write masks.
    private static readonly Dictionary<(uint Stream, RenderFlags Flags), uint> oldDecalShaderRoles = new();

    private bool UsesOldDecalShaders(ActorData actor) => KnownStockOldPipeline(actor) ||
        actor.StaticInstances != null || actor.SkinMatrices != null || !models.IsStockNewModel(actor.Model);

    private List<VulkanModelPart> MaterialParts(ActorData actor) => UsesOldDecalShaders(actor)
        ? actor.RenderModel.Parts : actor.RenderModel.NewParts ?? actor.RenderModel.Parts;

    private uint MaterialRasterFlags(ActorData actor, VulkanModelPart part, MaterialOverride material)
    {
        bool noCull = part.Technique == MyMeshDrawTechnique.ALPHA_MASKED ||
            part.Technique == MyMeshDrawTechnique.FOLIAGE || part.Technique == MyMeshDrawTechnique.CLOUD_LAYER;
        if (UsesOldDecalShaders(actor)) {
            // OLD starts with actor + technique proxy flags, then applies the
            // per-material add/remove masks. NEW fixes raster state per part.
            noCull |= (actor.Flags & RenderFlags.NoBackFaceCulling) != 0;
            if (material != null) {
                noCull |= (material.Flags.Add & RenderFlags.NoBackFaceCulling) != 0;
                noCull &= (material.Flags.Remove & RenderFlags.NoBackFaceCulling) == 0;
            }
        }
        return (noCull ? NativeBackend.StockNoCull : NativeBackend.StockBackCull) |
            (noCull && UsesOldDecalShaders(actor) ? NativeBackend.OldDepthNoCull : 0u);
    }

    private static bool IsBlendedMeshDecal(VulkanModelPart part) =>
        part.Technique == MyMeshDrawTechnique.DECAL || part.Technique == MyMeshDrawTechnique.DECAL_NOPREMULT;

    private void RegisterOldDecalShaders(ActorData actor)
    {
        EnsureOldModelIdentity(actor);
        RegisterOldActorLayouts(actor);
        RegisterOldIdentityVariants(actor);
        if (actor.Model == null || !UsesOldDecalShaders(actor)) return;
        // Stock rebuilds all LOD proxies before visibility/camera selection.
        // Run from the shared actor updater, after message-authored instancing
        // and skinning state is available, rather than from draw traversal.
        var previous = actor.RenderModel;
        try {
            for (int lod = 0; lod <= actor.Model.LodDescriptors.Length; lod++) {
                var model = models.GetLod(actor.Model, lod);
                bool hasDecal = false;
                foreach (var part in model.Parts) if (IsBlendedMeshDecal(part)) { hasDecal = true; break; }
                if (!hasDecal) continue;
                actor.RenderModel = model;
                PrepareInstances(actor, out uint type, out _, out _, out _);
                var key = (type, actor.Flags & (RenderFlags.MetalnessColorable | RenderFlags.DistanceFade));
                lock (oldDecalShaderRoles) {
                    if (oldDecalShaderRoles.ContainsKey(key)) continue;
                    foreach (var part in model.Parts) if (IsBlendedMeshDecal(part)) {
                        oldDecalShaderRoles.Add(key, WorldShaderVariants.TextureRole(part.ColorTexture, part.NormalTexture, part.ExtensionTexture));
                        break;
                    }
                }
            }
        } finally { actor.RenderModel = previous; }
    }

    private uint MaterialPipeline(ActorData actor, VulkanModelPart part, uint type)
    {
        uint role = WorldShaderVariants.TextureRole(part.ColorTexture, part.NormalTexture, part.ExtensionTexture), shaderRole = 0;
        bool metal = (actor.Flags & RenderFlags.MetalnessColorable) != 0;
        if (IsBlendedMeshDecal(part) && UsesOldDecalShaders(actor)) {
            lock (oldDecalShaderRoles) {
                // A missing registration is a lifecycle error, not permission to
                // select a first bundle from spatial/camera draw order.
                if (!oldDecalShaderRoles.TryGetValue((type, actor.Flags & (RenderFlags.MetalnessColorable | RenderFlags.DistanceFade)), out shaderRole))
                    throw new System.InvalidOperationException("OLD mesh decal shader bundle was not registered by the actor updater.");
            }
        }
        return WorldShaderVariants.Pipeline(part.Technique, type, (part.Flags & 1) != 0, metal, role, shaderRole, UsesOldDecalShaders(actor) && (actor.Flags & RenderFlags.DistanceFade) != 0);
    }
}
