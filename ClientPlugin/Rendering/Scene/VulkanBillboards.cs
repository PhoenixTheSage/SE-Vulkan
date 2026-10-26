using System;
using System.Collections.Generic;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRage.Render.Scene;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Rendering.Scene
{
    /// <summary>World-space billboard packets copied before game pools can recycle them.</summary>
    internal sealed class VulkanBillboards : IMyBillboardsHelper, IDisposable
    {
        private readonly VulkanScene scene;
        private readonly NativeBackend backend;
        private readonly List<MyBillboard> generated = new List<MyBillboard>();
        private readonly List<MyBillboard> frame = new List<MyBillboard>();
        private readonly List<MyBillboard> hudFrame = new List<MyBillboard>();
        private readonly List<ulong> handles = new List<ulong>();
        private static readonly uint[] Indices = { 0, 1, 2, 0, 2, 3 };
        public long Submitted { get; private set; }
        public long Unsupported { get; private set; }

        public VulkanBillboards(VulkanScene scene, NativeBackend backend) { this.scene = scene; this.backend = backend; }

        public void Draw(MySharedData shared){Collect(shared);Prepare();EmitPackets(backend.BillboardPackets);}
        // Fork (job graph, plan 2.2): the render thread collects at the serial point; a worker then runs
        // the flares, whose glare billboards go to a private capture list and follow the collected
        // generated billboards exactly as they would through the queue, then prepares and emits into a
        // pending list with resident-only texture reads. FinishAhead appends it on the render thread; a
        // texture miss instead re-emits there from the same prepared frame.
        [ThreadStatic] private static List<MyBillboard> capture;
        private readonly List<MyBillboard> flareCapture = new List<MyBillboard>();
        private readonly List<NativeBillboardDraw> pending = new List<NativeBillboardDraw>();
        private bool pendingReady;
        private MySharedData collected;
        public long AheadFrames { get; private set; }
        public long AheadFallbacks { get; private set; }
        public static bool Verify = System.Environment.GetEnvironmentVariable("SE_VULKAN_EFFECTS_VERIFY") == "1";
        private readonly List<NativeBillboardDraw> verifyScratch = new List<NativeBillboardDraw>();
        public long VerifyMatches { get; private set; }
        public long VerifyMismatches { get; private set; }
        public void EmitAhead(Action flares)
        {
            pendingReady = false; pending.Clear(); flareCapture.Clear();
            capture = flareCapture; try { flares(); } finally { capture = null; }
            frame.AddRange(flareCapture); flareCapture.Clear();
            Prepare();
            long submitted = Submitted, unsupported = Unsupported;
            try { using (VulkanTextureCache.ResidentOnly()) EmitPackets(pending); pendingReady = true; }
            catch (VulkanTextureCache.TextureNotResident) { pending.Clear(); Submitted = submitted; Unsupported = unsupported; }
        }
        public void FinishAhead()
        {
            if (pendingReady)
            {
                // Self-check (SE_VULKAN_EFFECTS_VERIFY=1): re-emit the same prepared frame here and compare.
                if (Verify) { verifyScratch.Clear(); long submitted = Submitted, unsupported = Unsupported; EmitPackets(verifyScratch); Submitted = submitted; Unsupported = unsupported;
                    if (SamePackets(verifyScratch, pending)) VerifyMatches++; else VerifyMismatches++; }
                backend.BillboardPackets.AddRange(pending); pending.Clear(); pendingReady = false; AheadFrames++; return;
            }
            AheadFallbacks++; EmitPackets(backend.BillboardPackets);
        }
        public void Collect(MySharedData shared)
        {
            frame.Clear(); collected = shared;
            if (shared != null)
            {
                foreach (MyBillboard billboard in shared.Billboards.Read.Billboards) frame.Add(Copy(billboard));
                shared.ApplyActionOnPersistentBillboards(billboard => frame.Add(Copy(billboard)));
            }
            lock (generated) { frame.AddRange(generated); generated.Clear(); }
        }
        private void Prepare()
        {
            var shared = collected;
            var projections=new float[512];for(int j=0;j<32;j++)Array.Copy(NativeBackend.MatrixValues(MatrixD.Identity),0,projections,j*16,16);
            if(shared!=null)foreach(var entry in shared.Billboards.Read.Matrices){if(entry.Key<0 || entry.Key>=32)continue;var data=entry.Value;float width=backend.PresentationWidth,height=backend.PresentationHeight;MatrixD viewport=MatrixD.Identity;viewport.M11=data.Viewport.Width/width;viewport.M22=data.Viewport.Height/height;viewport.M41=data.Viewport.OffsetX/width;viewport.M42=(height-data.Viewport.OffsetY-data.Viewport.Height)/height;MatrixD matrix=data.ViewAtZero*data.Projection*viewport;Array.Copy(NativeBackend.MatrixValues(matrix),0,projections,entry.Key*16,16);}backend.SetBillboardProjections(projections);
            Vector3D camera = scene.Environment.CameraPosition;
            // Keen deliberately leaves LDR/PostPP in submission order. HUD
            // backgrounds, labels and cursor share a plane and alpha-compose
            // in that order; sorting their corners by distance hides controls.
            hudFrame.Clear();
            int worldCount = 0;
            for (int i = 0; i < frame.Count; ++i)
            {
                MyBillboard billboard = frame[i];
                if (billboard.BlendType == MyBillboard.BlendTypeEnum.LDR || billboard.BlendType == MyBillboard.BlendTypeEnum.PostPP)
                    hudFrame.Add(billboard);
                else
                    frame[worldCount++] = billboard;
            }
            frame.RemoveRange(worldCount, frame.Count - worldCount);
            frame.Sort((a, b) =>
            {
                int bucket = Bucket(a.BlendType).CompareTo(Bucket(b.BlendType));
                return bucket != 0 ? bucket : Vector3D.DistanceSquared(WorldPosition(b), camera).CompareTo(Vector3D.DistanceSquared(WorldPosition(a), camera));
            });
            frame.AddRange(hudFrame);
        }
        private void EmitPackets(List<NativeBillboardDraw> target)
        {
            var shared = collected; Vector3D camera = scene.Environment.CameraPosition;
            for (int i = 0; i < frame.Count; ++i)
            {
                MyBillboard billboard = frame[i];
                var settings=scene.RenderSettings;
                bool enabled=settings.DrawBillboards && (billboard.BlendType==MyBillboard.BlendTypeEnum.Standard?settings.DrawBillboardsStandard:billboard.BlendType==MyBillboard.BlendTypeEnum.AdditiveBottom?settings.DrawBillboardsBottom:billboard.BlendType==MyBillboard.BlendTypeEnum.AdditiveTop?settings.DrawBillboardsTop:billboard.BlendType==MyBillboard.BlendTypeEnum.LDR?settings.DrawBillboardsLDR:settings.DrawBillboardsPostPP);
                if(!enabled)continue;
                Vector3D billboardCamera=camera;
                if(billboard.CustomViewProjection!=-1){if(billboard.CustomViewProjection<0 || billboard.CustomViewProjection>=32 || shared==null || !shared.Billboards.Read.Matrices.TryGetValue(billboard.CustomViewProjection,out var custom)){Unsupported++;continue;}billboardCamera=custom.CameraPosition;}
                MyTransparentMaterial material = MyTransparentMaterials.GetMaterial(billboard.Material);
                Vector3D a = billboard.Position0, b = billboard.Position1, c = billboard.Position2, d = billboard.Position3;
                if (billboard.ParentID != uint.MaxValue)
                {
                    MyActor actor = MyIDTracker<MyActor>.FindByID(billboard.ParentID);
                    if (actor == null || !VulkanScene.IsVisible(actor)) continue;
                    MatrixD parent = actor.WorldMatrix;
                    a = Vector3D.Transform(a, parent);
                    if (billboard.LocalType == MyBillboard.LocalTypeEnum.Line) b = Vector3D.TransformNormal(b, parent);
                    else if (billboard.LocalType == MyBillboard.LocalTypeEnum.Custom)
                    { b = Vector3D.Transform(b, parent); c = Vector3D.Transform(c, parent); d = Vector3D.Transform(d, parent); }
                }
                if (billboard.LocalType == MyBillboard.LocalTypeEnum.Point)
                {
                    float radius = (float)c.X, angle = (float)c.Y;
                    Vector3D forward = billboardCamera - a;
                    if (forward.LengthSquared() < 0.000001) continue;
                    forward.Normalize();
                    Vector3D right = Vector3D.Cross(Math.Abs(forward.Y) > .99 ? Vector3D.Right : Vector3D.Up, forward);
                    right.Normalize(); Vector3D up = Vector3D.Cross(forward, right);
                    Vector3D rotatedRight = right * Math.Cos(angle) + up * Math.Sin(angle);
                    Vector3D rotatedUp = up * Math.Cos(angle) - right * Math.Sin(angle);
                    Corners(a, rotatedRight, rotatedUp, radius, radius, out a, out b, out c, out d);
                }
                else if (billboard.LocalType == MyBillboard.LocalTypeEnum.Line)
                {
                    Vector3D line = b;
                    if (line.LengthSquared() < 0.000001) continue;
                    line.Normalize();
                    Vector3D end = a + line * c.X;
                    Vector3D side = Vector3D.Cross(line, billboardCamera - a);
                    if (side.LengthSquared() < 0.000001) continue;
                    side.Normalize(); side *= c.Y * .5;
                    b = end - side; c = end + side; d = a + side; a -= side;
                }
                Vector3D center = (a+b+c+d)*.25;
                Vector2 uv0=material.UVOffset+billboard.UVOffset*material.UVSize,uv1=uv0+material.UVSize*billboard.UVSize;
                var vertices=new float[20];void V(int index,Vector3D point,Vector2 uv){if(billboard.CustomViewProjection==-1)point-=camera;int j=index*5;vertices[j]=(float)point.X;vertices[j+1]=(float)point.Y;vertices[j+2]=(float)point.Z;vertices[j+3]=uv.X;vertices[j+4]=uv.Y;}
                var triangle = billboard as MyTriangleBillboard;
                if (triangle != null)
                {
                    // Match Keen: the quad's second triangle is degenerate, and
                    // triangle UVs are already final (including font atlas UVs).
                    V(0,a,triangle.UV0);V(1,b,triangle.UV1);V(2,c,triangle.UV2);V(3,c,triangle.UV2);
                }
                else
                {
                    V(0,a,uv0);V(1,b,new Vector2(uv1.X,uv0.Y));V(2,c,uv1);V(3,d,new Vector2(uv0.X,uv1.Y));
                }
                var data=new byte[48];Vector4 color=billboard.Color;color.X*=billboard.ColorIntensity;color.Y*=billboard.ColorIntensity;color.Z*=billboard.ColorIntensity;
                Buffer.BlockCopy(new[]{color.X,color.Y,color.Z,color.W},0,data,0,16);Buffer.BlockCopy(BitConverter.GetBytes(billboard.CustomViewProjection),0,data,16,4);
                Vector3 normal=(Vector3)Vector3D.Cross(b-a,d-a);if(normal.LengthSquared()>1e-10f)normal.Normalize();
                if (triangle != null) normal = triangle.Normal0;
                Buffer.BlockCopy(new[]{billboard.Reflectivity,material.AlphaSaturation,billboard.AlphaCutout,normal.X,normal.Y,normal.Z,billboard.SoftParticleDistanceScale*material.SoftParticleDistanceScale},0,data,20,28);
                // 32: Keen's emissive producer signal (ColorIntensity > 1), which HdrRender lifts by LdrIntensity under HDR.
                uint flags=(material.CanBeAffectedByOtherLights?1u:0u)|(material.SoftParticleDistanceScale>0?2u:0u)|(material.AlphaCutout?4u:0u)|(billboard.ColorIntensity>1f?32u:0u);
                uint bucket=billboard.BlendType==MyBillboard.BlendTypeEnum.Standard?1u:billboard.BlendType==MyBillboard.BlendTypeEnum.AdditiveBottom?0u:billboard.BlendType==MyBillboard.BlendTypeEnum.AdditiveTop?2u:billboard.BlendType==MyBillboard.BlendTypeEnum.LDR?3u:4u;
                ulong texture=scene.TextureCache.GetTexture(material.Texture);if(scene.TextureCache.IsSingleChannel(texture))flags|=16;if(bucket!=1)flags|=8;if(target.Count>=32768){Unsupported++;continue;}
                target.Add(new NativeBillboardDraw{Texture=texture,Flags=flags,Bucket=bucket,Vertices=vertices,Data=data});
                Submitted++;
            }
        }

        private static bool SamePackets(List<NativeBillboardDraw> a, List<NativeBillboardDraw> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                var x = a[i]; var y = b[i];
                if (x.Texture != y.Texture || x.Flags != y.Flags || x.Bucket != y.Bucket || x.Vertices.Length != y.Vertices.Length || x.Data.Length != y.Data.Length) return false;
                for (int j = 0; j < x.Vertices.Length; j++) if (BitConverter.ToInt32(BitConverter.GetBytes(x.Vertices[j]), 0) != BitConverter.ToInt32(BitConverter.GetBytes(y.Vertices[j]), 0)) return false; // verify mode only
                for (int j = 0; j < x.Data.Length; j++) if (x.Data[j] != y.Data[j]) return false;
            }
            return true;
        }
        private static int Bucket(MyBillboard.BlendTypeEnum blend) => blend == MyBillboard.BlendTypeEnum.AdditiveBottom ? 0 : blend == MyBillboard.BlendTypeEnum.Standard ? 1 : (int)blend;
        private static Vector3D WorldPosition(MyBillboard billboard)
        {
            if (billboard.ParentID == uint.MaxValue) return billboard.Position0;
            MyActor actor = MyIDTracker<MyActor>.FindByID(billboard.ParentID);
            return actor == null ? billboard.Position0 : Vector3D.Transform(billboard.Position0, actor.WorldMatrix);
        }

        private static MyBillboard Copy(MyBillboard b)
        {
            // Rich HUD submits pooled MyTriangleBillboards. Preserve the subtype
            // and its payload before the producer can reuse the source object.
            MyBillboard copy = b is MyTriangleBillboard triangle
                ? new MyTriangleBillboard { UV0 = triangle.UV0, UV1 = triangle.UV1, UV2 = triangle.UV2, Normal0 = triangle.Normal0 }
                : new MyBillboard();
            copy.Material = b.Material; copy.BlendType = b.BlendType;
            copy.Position0 = b.Position0; copy.Position1 = b.Position1; copy.Position2 = b.Position2; copy.Position3 = b.Position3;
            copy.Color = b.Color; copy.ColorIntensity = b.ColorIntensity; copy.Reflectivity = b.Reflectivity;
            copy.AlphaCutout = b.AlphaCutout; copy.SoftParticleDistanceScale = b.SoftParticleDistanceScale;
            copy.UVOffset = b.UVOffset; copy.UVSize = b.UVSize; copy.LocalType = b.LocalType;
            copy.ParentID = b.ParentID; copy.CustomViewProjection = b.CustomViewProjection;
            return copy;
        }

        public void AddPointBillboard(MyStringId material, Vector4 color, Vector3D origin, float radius, float angle, MyBillboard.BlendTypeEnum blendType = MyBillboard.BlendTypeEnum.Standard, int customViewProjection = -1, float intensity = 1)
        {
            Enqueue(new MyBillboard { Material = material, Color = ApplyMaterial(color, material, origin), Position0 = origin, Position2 = new Vector3D(radius, angle, 0), LocalType = MyBillboard.LocalTypeEnum.Point, BlendType = blendType, ColorIntensity = intensity,SoftParticleDistanceScale=1, CustomViewProjection = customViewProjection, UVSize = Vector2.One });
        }
        public void AddLineBillboard(MyStringId material, Vector4 color, Vector3D origin, Vector3 directionNormalized, float length, float thickness, MyBillboard.BlendTypeEnum blendType = MyBillboard.BlendTypeEnum.Standard, int customViewProjection = -1, float intensity = 1)
        {
            Enqueue(new MyBillboard { Material = material, Color = ApplyMaterial(color, material, origin), Position0 = origin, Position1 = directionNormalized, Position2 = new Vector3D(length, thickness, 0), LocalType = MyBillboard.LocalTypeEnum.Line, BlendType = blendType, ColorIntensity = intensity,SoftParticleDistanceScale=1, CustomViewProjection = customViewProjection, UVSize = Vector2.One });
        }
        public void AddBillboardOriented(MyStringId material, Vector4 color, Vector3D origin, Vector3 leftVector, Vector3 upVector, float radiusX, float radiusY, MyBillboard.BlendTypeEnum blendType = MyBillboard.BlendTypeEnum.Standard, float softParticleDistanceScale = 1, int customViewProjection = -1)
        {
            Vector3D a, b, c, d;
            Corners(origin, -leftVector, upVector, radiusX, radiusY, out a, out b, out c, out d);
            Enqueue(new MyBillboard { Material = material, Color = ApplyMaterial(color, material, origin), Position0 = a, Position1 = b, Position2 = c, Position3 = d, BlendType = blendType, ColorIntensity = 1,SoftParticleDistanceScale=softParticleDistanceScale, CustomViewProjection = customViewProjection, UVSize = Vector2.One });
        }
        public void AddBillboardRotated(MyStringId material,Vector4 color,Vector3D origin,Vector3D camera,float radiusX,float radiusY,MyBillboard.BlendTypeEnum blendType)
        {
            // Stock rotated flares use the world-up basis, independently of
            // camera roll, including its pole and near-origin handling.
            if(!MyUtils.GetBillboardQuadAdvancedRotated(out var quad,origin,radiusX,radiusY,0,camera))return;
            Enqueue(new MyBillboard{Material=material,Color=ApplyMaterial(color,material,origin),Position0=quad.Point0,Position1=quad.Point1,Position2=quad.Point2,Position3=quad.Point3,BlendType=blendType,ColorIntensity=1,SoftParticleDistanceScale=1,CustomViewProjection=-1,UVSize=Vector2.One});
        }
        private void Enqueue(MyBillboard billboard) { var target = capture; if (target != null) { target.Add(billboard); return; } lock (generated) generated.Add(billboard); }
        private Vector4 ApplyMaterial(Vector4 color, MyStringId id, Vector3D origin)
        {
            var material = MyTransparentMaterials.GetMaterial(id);
            color *= material.Color;
            if (material.AlphaMistingEnable)
            {
                double distance = Vector3D.Distance(origin, scene.Environment.CameraPosition);
                float span = material.AlphaMistingEnd - material.AlphaMistingStart;
                color *= span > .00001f ? MathHelper.Clamp(((float)distance - material.AlphaMistingStart) / span, 0, 1) : 1;
            }
            return color;
        }
        private static void Corners(Vector3D center, Vector3D right, Vector3D up, double x, double y, out Vector3D a, out Vector3D b, out Vector3D c, out Vector3D d)
        { right *= x; up *= y; a = center - right + up; b = center + right + up; c = center + right - up; d = center - right - up; }
        public void Dispose() { foreach (ulong handle in handles) backend.RemoveResource(1, handle); handles.Clear(); lock (generated) generated.Clear(); frame.Clear(); hudFrame.Clear(); }
    }
}
