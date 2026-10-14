using System;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRageMath;

namespace ClientPlugin.Rendering.Scene
{
    internal sealed class VulkanSkybox : IDisposable
    {
        private readonly NativeBackend backend;
        private readonly VulkanTextureCache textures;
        private readonly ulong[] meshes = new ulong[6];
        private string path;
        private Quaternion orientation = Quaternion.Identity;
        public MatrixD Orientation => MatrixD.CreateFromQuaternion(orientation);
        public long Draws { get; private set; }
        public bool Ready { get; private set; }
        public VulkanSkybox(NativeBackend backend, VulkanTextureCache textures) { this.backend = backend; this.textures = textures; }
        public void Set(string texture, Quaternion rotation) { path = texture; orientation = rotation; }

        public void Draw(Vector3D camera)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if(backend.WorldReady){ulong cube=textures.GetWorldCube(path);backend.SetWorldSkybox(cube);Ready=cube!=0;if(Ready)Draws++;return;}
            ulong[] faces = textures.GetCubeTextures(path);
            if (faces == null) return;
            MatrixD world = MatrixD.CreateFromQuaternion(orientation);
            world.Translation = camera;
            for (int face = 0; face < 6; ++face)
            {
                if (meshes[face] == 0)
                {
                    meshes[face] = 0x300000000ul + (ulong)face;
                    float[] vertices = new float[32];
                    VulkanModelCache.WriteVertex(vertices, 0, Direction(face, -1, -1), Vector3.Up, Vector2.Zero);
                    VulkanModelCache.WriteVertex(vertices, 1, Direction(face, 1, -1), Vector3.Up, Vector2.UnitX);
                    VulkanModelCache.WriteVertex(vertices, 2, Direction(face, 1, 1), Vector3.Up, Vector2.One);
                    VulkanModelCache.WriteVertex(vertices, 3, Direction(face, -1, 1), Vector3.Up, Vector2.UnitY);
                    backend.UploadMesh(meshes[face], vertices, new uint[] { 0, 1, 2, 0, 2, 3 });
                }
                backend.DrawMesh(meshes[face], world, Color.White, faces[face], 2 | 256);
                Draws++;
            }
            Ready = true;
        }

        // DDS cubemap sampling convention expressed as six flat textured faces.
        private static Vector3 Direction(int face, float u, float v)
        {
            switch (face)
            {
                case 0: return new Vector3(1, -v, -u);
                case 1: return new Vector3(-1, -v, u);
                case 2: return new Vector3(u, 1, v);
                case 3: return new Vector3(u, -1, -v);
                case 4: return new Vector3(u, -v, 1);
                default: return new Vector3(-u, -v, -1);
            }
        }
        public void Dispose() { foreach (ulong mesh in meshes) if (mesh != 0) backend.RemoveResource(1, mesh); Array.Clear(meshes, 0, meshes.Length); Ready = false; path = null; }
    }
}
