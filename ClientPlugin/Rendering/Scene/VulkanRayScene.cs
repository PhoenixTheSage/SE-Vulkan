using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using ClientPlugin.Rendering.Content;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;

/// <summary>
/// Vulkan producer for Anomaly's RayScene API v1 (Codex audit P3 step 3). It fills the scene half of the v1 frame
/// with the same records Anomaly's DX11 capture builds from Render11:
/// mesh {revision, float[16/vertex] position/normal/uv/bones/weights, int[] indices, part[], voxel};
/// part {start, count, baseVertex, material, paths[4], technique[, scales]};
/// instance {actor id, mesh (-1 unknown), camera-relative row-major world, variant[], camera-relative bounds};
/// variant {part, colour*emissivity[4], textures[4], bones, optics};
/// light 16 floats {position, range, colour, falloff, direction, cone, kind, id bits, diffuse, gloss}.
/// Texture handles are renderer texture ids, boxed once so tenants can compare them by reference.
/// Not yet covered (retained as unknown/absent): skinned and instanced actors, voxels and billboard/GPU emitters.
/// </summary>
public sealed partial class VulkanScene
{
    const double RayRange = 256;
    static int rayMeshRevision;
    readonly ConditionalWeakTable<VulkanModel, object[]> rayMeshes = new();
    readonly Dictionary<ulong, object> rayTextures = new();

    object RayTexture(ulong id)
    {
        if (id == 0) return null;
        if (!rayTextures.TryGetValue(id, out var boxed)) rayTextures[id] = boxed = id;
        return boxed;
    }

    object[] RayMesh(VulkanModel model)
    {
        if (rayMeshes.TryGetValue(model, out var cached)) return cached;
        int vertexCount = model.Vertices.Length / 8;
        var vertices = new float[vertexCount * 16];
        for (int i = 0; i < vertexCount; i++)
        {
            Array.Copy(model.Vertices, i * 8, vertices, i * 16, 8);
            if (model.BlendIndices != null && model.BlendWeights != null && i < model.BlendIndices.Length && i < model.BlendWeights.Length)
            {
                var b = model.BlendIndices[i]; var w = model.BlendWeights[i]; int j = i * 16;
                vertices[j + 8] = b.X; vertices[j + 9] = b.Y; vertices[j + 10] = b.Z; vertices[j + 11] = b.W;
                vertices[j + 12] = w.X; vertices[j + 13] = w.Y; vertices[j + 14] = w.Z; vertices[j + 15] = w.W;
            }
        }
        int total = 0; foreach (var part in model.Parts) total += part.Indices.Length;
        var indices = new int[total]; var parts = new object[model.Parts.Count][]; int start = 0;
        for (int p = 0; p < model.Parts.Count; p++)
        {
            var part = model.Parts[p];
            for (int i = 0; i < part.Indices.Length; i++) indices[start + i] = (int)part.Indices[i];
            parts[p] = new object[] { start, part.Indices.Length, 0, part.MaterialName ?? "",
                new[] { part.ColorTexture, part.NormalTexture, part.ExtensionTexture, part.AlphaTexture }, part.Technique.ToString() };
            start += part.Indices.Length;
        }
        var mesh = new object[] { Interlocked.Increment(ref rayMeshRevision), vertices, indices, parts, false };
        rayMeshes.Add(model, mesh);
        return mesh;
    }

    // DX11 RayMaterialOptics: thin-glass transmission for file-textured GLASS materials.
    object[] RayOptics(object[] part)
    {
        if (!string.Equals((string)part[5], "GLASS", StringComparison.OrdinalIgnoreCase) ||
            !MyTransparentMaterials.TryGetMaterial(MyStringId.GetOrCompute((string)part[3]), out var material) || material == null ||
            ReferenceEquals(material, MyTransparentMaterials.ErrorMaterial) || material.TextureType != MyTransparentMaterialTextureType.FileTexture || string.IsNullOrEmpty(material.Texture)) return null;
        var texture = RayTexture(TextureCache.GetTexture(material.Texture));
        if (texture == null) return null;
        var c = material.Color; float a = Math.Max(0, Math.Min(1, c.W)); float inverse = a > 1e-6f ? 1 / a : 0;
        uint revision = unchecked((uint)c.GetHashCode() ^ (uint)material.UVOffset.GetHashCode() ^ (uint)material.UVSize.GetHashCode());
        return new object[] { 1, new[] { c.X * inverse, c.Y * inverse, c.Z * inverse, a }, texture,
            new[] { material.UVOffset.X, material.UVOffset.Y, material.UVSize.X, material.UVSize.Y }, revision, material.TriangleFaceCulling, "thin-glass" };
    }

    static float[] RayMatrix(Matrix m) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };

    static void RayLight(List<float> list, Vector3D position, Vector3 color, float range, Vector3D direction, float cone, int kind, uint id, float falloff, float diffuse, float gloss)
        => list.AddRange(new[] { (float)position.X, (float)position.Y, (float)position.Z, range, color.X, color.Y, color.Z, falloff,
            (float)direction.X, (float)direction.Y, (float)direction.Z, cone, kind, BitConverter.ToSingle(BitConverter.GetBytes(id), 0), diffuse, gloss });

    /// <summary>
    /// Scene half of a RayScene v1 frame around the camera: {meshes, instances, lights, cookies, physicalOrigins,
    /// center double[3], bloomEmissiveness}. Anomaly adds the frame metadata and renderer image ids.
    /// </summary>
    internal object[] CaptureRayScene()
    {
        var center = FrameView.CameraPosition;
        var meshes = new List<object[]>(); var meshIndices = new Dictionary<VulkanModel, int>(); var instances = new List<object[]>();
        foreach (var actor in buildActors.Values)
        {
            if (actor.Actor == null || BuildFrameOf(actor).Destroyed || !BuildFrameOf(actor).Visible) continue;
            var model = actor.RenderModel ?? actor.Model;
            // Light and other model-less actors are not render geometry (DX11 enumerates renderable components only).
            if (model == null) continue;
            // The actor's world AABB, as DX11 InRange uses; before its first update fall back to the model bounds.
            // The scene's own box (the game's WorldAabb is refreshed by its updater a frame later, outside the actor frames).
            var bounds = BuildFrameOf(actor).Bounds;
            if (!bounds.Valid || bounds.Size.LengthSquared() <= 0) bounds = ((BoundingBoxD)model.Bounds).TransformFast(BuildFrameOf(actor).World);
            if (!bounds.Valid || bounds.DistanceSquared(center) > RayRange * RayRange) continue;
            // Matches DX11: instanced and unresolved geometry are present as unknown occupants.
            bool known = model.Vertices != null && model.Parts.Count != 0 && actor.StaticInstances == null && actor.SkinMatrices == null;
            int meshIndex = -1; var variants = Array.Empty<object>();
            if (known)
            {
                var mesh = RayMesh(model);
                if (!meshIndices.TryGetValue(model, out meshIndex)) { meshIndex = meshes.Count; meshIndices[model] = meshIndex; meshes.Add(mesh); }
                var parts = (object[][])mesh[3]; variants = new object[parts.Length];
                for (int p = 0; p < parts.Length; p++)
                {
                    var part = parts[p]; var paths = (string[])((string[])part[4]).Clone();
                    actor.Materials.TryGetValue((string)part[3], out var material);
                    if (material?.Textures != null)
                    {
                        var change = material.Textures;
                        if (change.ColorMetalFileName != null) paths[0] = change.ColorMetalFileName;
                        if (change.NormalGlossFileName != null) paths[1] = change.NormalGlossFileName;
                        if (change.ExtensionsFileName != null) paths[2] = change.ExtensionsFileName;
                        if (change.AlphamaskFileName != null) paths[3] = change.AlphamaskFileName;
                    }
                    var tint = material?.Color?.ToVector3() ?? Vector3.One;
                    var multiplier = new[] { tint.X, tint.Y, tint.Z, material?.Emissivity ?? 1f };
                    var textures = new object[] { RayTexture(TextureCache.GetTexture(paths[0])), RayTexture(TextureCache.GetWorldNormal(paths[1])),
                        RayTexture(TextureCache.GetWorldExtensions(paths[2])), RayTexture(TextureCache.GetWorldAlpha(paths[3])) };
                    variants[p] = new object[] { part, multiplier, textures, Array.Empty<float>(), RayOptics(part) };
                }
            }
            var world = BuildFrameOf(actor).World; world.Translation -= center;
            instances.Add(new object[] { actor.Id, meshIndex, RayMatrix((Matrix)world), variants,
                new[] { (float)(bounds.Min.X - center.X), (float)(bounds.Min.Y - center.Y), (float)(bounds.Min.Z - center.Z),
                    (float)(bounds.Max.X - center.X), (float)(bounds.Max.Y - center.Y), (float)(bounds.Max.Z - center.Z) } });
        }
        var lightValues = new List<float>(); var origins = new List<float>(); var cookies = new List<object[]>();
        foreach (var light in lights)
        {
            var owner = light.Owner;
            if (owner == null || owner.IsDestroyed) continue;
            var data = light.EffectiveData; var m = owner.WorldMatrix;
            if (Vector3D.DistanceSquared(m.Translation, center) > Math.Pow(RayRange + Math.Max(data.PointLight.Range, data.SpotLight.Light.Range), 2)) continue;
            var up = new[] { (float)m.Up.X, (float)m.Up.Y, (float)m.Up.Z }; var physical = m.Translation - center;
            if (data.PointLightOn && data.PointIntensity > .001f)
            {
                origins.Add((float)physical.X); origins.Add((float)physical.Y); origins.Add((float)physical.Z);
                RayLight(lightValues, m.Translation + m.Forward * data.PointOffset - center, data.PointLight.Color, data.PointLight.Range, m.Forward, -1, 0, owner.ID,
                    data.PointLight.Falloff, data.PointLight.DiffuseFactor, data.PointLight.GlossFactor);
                cookies.Add(new object[] { null, up });
            }
            if (data.SpotLightOn && data.SpotIntensity > .001f)
            {
                origins.Add((float)physical.X); origins.Add((float)physical.Y); origins.Add((float)physical.Z);
                RayLight(lightValues, physical, data.SpotLight.Light.Color, data.SpotLight.Light.Range, m.Forward, data.SpotLight.ApertureCos, 1, owner.ID,
                    data.SpotLight.Light.Falloff, data.SpotLight.Light.DiffuseFactor, data.SpotLight.Light.GlossFactor);
                cookies.Add(new object[] { string.IsNullOrEmpty(data.ReflectorTexture) ? null : RayTexture(TextureCache.GetTexture(data.ReflectorTexture, true)), up });
            }
        }
        return new object[] { meshes.ToArray(), instances.ToArray(), lightValues.ToArray(), cookies.ToArray(), origins.ToArray(),
            new[] { center.X, center.Y, center.Z }, bloomEmissiveness };
    }
}
