using System.Runtime.CompilerServices;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

// BoundingFrustumD.Contains(BoundingBoxD) calls BoundingBoxD.Intersects for
// every plane, testing both support corners to distinguish Contains from
// Intersects. VisibleObjects needs only the Front/Disjoint result. Keep the
// game's min-corner choice and dot-product evaluation order (including > 0 at
// the boundary); a centre/radius conversion can round differently far from 0.
internal readonly struct FrustumVisibility
{
    readonly bool active;
    readonly Plane near,far,left,right,top,bottom;

    public FrustumVisibility(BoundingFrustumD frustum)
    {
        active=frustum!=null;
        near=active?new Plane(frustum.Near):default;
        far=active?new Plane(frustum.Far):default;
        left=active?new Plane(frustum.Left):default;
        right=active?new Plane(frustum.Right):default;
        top=active?new Plane(frustum.Top):default;
        bottom=active?new Plane(frustum.Bottom):default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(in BoundingBoxD box)
        =>!active||!(near.Outside(in box)||far.Outside(in box)||left.Outside(in box)||right.Outside(in box)||top.Outside(in box)||bottom.Outside(in box));

    readonly struct Plane
    {
        readonly double x,y,z,d;
        readonly bool minX,minY,minZ;
        public Plane(PlaneD plane)
        {
            x=plane.Normal.X;y=plane.Normal.Y;z=plane.Normal.Z;d=plane.D;
            minX=x>=0;minY=y>=0;minZ=z>=0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Outside(in BoundingBoxD box)
            =>x*(minX?box.Min.X:box.Max.X)+y*(minY?box.Min.Y:box.Max.Y)+z*(minZ?box.Min.Z:box.Max.Z)+d>0;
    }
}
