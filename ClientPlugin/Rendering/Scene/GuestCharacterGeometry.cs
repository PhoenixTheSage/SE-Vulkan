using System;
using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

// Device-free copy of the stock VertexTemplateBase skin-position equation.
// SkeletonIndices are already resolved into SkinMatrices by VulkanScene.
internal static class GuestCharacterGeometry
{
    internal static float[] CopyTriangles(float[] vertices, Vector4I[] indices, Vector4[] weights, Matrix[] bones, IEnumerable<uint[]> parts)
    {
        if (vertices == null || vertices.Length % 8 != 0) throw new InvalidOperationException("character vertex layout unavailable");
        int count = vertices.Length / 8;
        var triangles = new List<uint>();
        foreach (var part in parts)
        {
            if (part == null || part.Length % 3 != 0) throw new InvalidOperationException("character triangle indices unavailable");
            foreach (uint index in part)
            {
                if (index >= count) throw new InvalidOperationException("character triangle index outside mesh");
                triangles.Add(index);
            }
        }
        if (triangles.Count == 0) return Array.Empty<float>();
        var positions = new Vector3[count];
        if (weights != null && weights.Length != 0 && bones == null) throw new InvalidOperationException("character skin pose unavailable");
        bool skinned = bones != null && weights != null && weights.Length != 0;
        var copied = new bool[count];
        foreach (uint vertex in triangles)
        {
            int i = (int)vertex;
            if (copied[i]) continue;
            if (skinned && (indices == null || i >= indices.Length || i >= weights.Length))
                throw new InvalidOperationException("character skin arrays incomplete");
            var position = new Vector4(vertices[i*8], vertices[i*8+1], vertices[i*8+2], 1);
            if (skinned)
            {
                var sum = Vector4.Zero;
                for (int b = 0; b < 4; b++)
                {
                    float weight = weights[i][b]; if (weight == 0) continue;
                    int index = indices[i][b];
                    if (index < 0 || index >= bones.Length) throw new InvalidOperationException("character skin palette unavailable");
                    sum += Vector4.Transform(position, bones[index]) * weight;
                }
                if (!(Math.Abs(sum.W) > 1e-8f)) throw new InvalidOperationException("character skin position has zero weight");
                position = sum / sum.W;
            }
            if (!Finite(position.X) || !Finite(position.Y) || !Finite(position.Z)) throw new InvalidOperationException("character skin position is not finite");
            positions[i] = new Vector3(position.X, position.Y, position.Z);
            copied[i] = true;
        }
        var output = new List<float>();
        foreach (uint index in triangles)
        {
            var p = positions[index]; output.Add(p.X); output.Add(p.Y); output.Add(p.Z); output.Add(1);
        }
        return output.ToArray();
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
