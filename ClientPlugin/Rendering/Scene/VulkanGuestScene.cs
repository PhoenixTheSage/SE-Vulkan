using System;
using System.Collections.Generic;
using VRageMath;
using VRageRender.Import;
using ClientPlugin.Rendering.Content;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    internal object[] CaptureGuestCharacter(uint actorId)
    {
        if (actorId == 0 || !buildActors.TryGetValue(actorId, out var actor) || BuildFrameOf(actor).Destroyed || actor.Model == null)
            return new object[] { Array.Empty<float>(), Array.Empty<float>(), Array.Empty<double>(), Array.Empty<double>(), "no controlled render character" };
        float[] Copy(VulkanModel model)
        {
            var parts = new List<uint[]>();
            foreach (var part in model.Parts)
                if (GuestShadowTechnique(part.Technique) && (part.Flags & 4) == 0) parts.Add(part.Indices);
            if (parts.Count == 0) return Array.Empty<float>();
            return GuestCharacterGeometry.CopyTriangles(model.Vertices, model.BlendIndices, model.BlendWeights, actor.SkinMatrices, parts);
        }
        // Do not use main-view visibility or first-person material flags. The
        // light camera needs the opaque head/body even when the viewer hides it.
        var atlasModel = actor.Model;
        float[] atlas = Copy(atlasModel);
        for (int lod = 1; atlas.Length == 0 && lod <= actor.Model.LodDescriptors.Length; lod++)
        {
            atlasModel = models.GetLod(actor.Model, lod);
            atlas = Copy(atlasModel);
        }
        var maskModel = actor.RenderModel ?? actor.Model;
        float[] mask = ReferenceEquals(atlasModel, maskModel) ? atlas : Copy(maskModel);
        var m = BuildFrameOf(actor).World;
        var box = actor.Bounds;
        return new object[] { atlas, mask,
            new double[] { m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44 },
            new double[] { box.Center.X,box.Center.Y,box.Center.Z,box.HalfExtents.X,box.HalfExtents.Y,box.HalfExtents.Z },
            atlas.Length == 0 ? "no opaque character triangles" : "copied skinned character" };
    }

    static bool GuestShadowTechnique(MyMeshDrawTechnique technique)
    {
        switch (technique)
        {
            case MyMeshDrawTechnique.DECAL: case MyMeshDrawTechnique.DECAL_CUTOUT: case MyMeshDrawTechnique.DECAL_NOPREMULT:
            case MyMeshDrawTechnique.GLASS: case MyMeshDrawTechnique.HOLO: case MyMeshDrawTechnique.SHIELD:
            case MyMeshDrawTechnique.SHIELD_LIT: case MyMeshDrawTechnique.ATMOSPHERE: case MyMeshDrawTechnique.CLOUD_LAYER:
            case MyMeshDrawTechnique.FOLIAGE: return false;
            default: return true;
        }
    }

    // Copied render-thread scene data only. Anomaly owns request policy,
    // incumbent light selection, limits, shader compilation and catalog names.
    internal object[] CaptureGuestShadowInputs(uint localActor)
    {
        var positions = new List<double>();
        var lightIds = new List<uint>();
        var bounds = new List<double>();
        var actorIds = new List<uint>();
        var camera = FrameView.CameraPosition;
        var anchor = camera;
        var lightFrame = buildLights;
        for (int i = 0; i < lightFrame.Count; i++)
        {
            var light = lightFrame.All[i];
            var data = light.Data;
            if (!light.HasOwner || !light.Visible || !data.PointLightOn || data.PointLight.Range < 0.05f) continue;
            var position = light.World.Translation + data.PointOffset * light.World.Forward;
            lightIds.Add(light.OwnerId);
            positions.Add(position.X); positions.Add(position.Y); positions.Add(position.Z); positions.Add(data.PointLight.Range);
        }
        foreach (var pair in actors)
        {
            var actor = pair.Value;
            if (actor.Model == null || BuildFrameOf(actor).Destroyed) continue;
            var box = actor.Bounds;
            var center = box.Center;
            var half = box.HalfExtents;
            if (pair.Key == localActor) anchor = center;
            actorIds.Add(pair.Key);
            bounds.Add(center.X); bounds.Add(center.Y); bounds.Add(center.Z);
            bounds.Add(half.X); bounds.Add(half.Y); bounds.Add(half.Z);
        }
        return new object[] { positions.ToArray(), lightIds.ToArray(), bounds.ToArray(), actorIds.ToArray(),
            new double[] { anchor.X, anchor.Y, anchor.Z, camera.X, camera.Y, camera.Z } };
    }
}
