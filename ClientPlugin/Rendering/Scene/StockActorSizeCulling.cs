using System;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

// Installed MyRenderableComponent.CheckDistanceCulling. Raw bounds distance
// and authored last-LOD distance; no global/cube LOD coefficient is applied.
public static class StockActorSizeCulling
{
    public static bool Evaluate(float distanceSquared,float finalLodSquared,float volumeExtent,ref Matrix projection,uint width,uint height,out float? area)
    {
        area=null;
        if(!(distanceSquared>finalLodSquared)||width==0||height==0||volumeExtent<0||float.IsNaN(volumeExtent)||float.IsInfinity(volumeExtent))return false;
        Vector3 position=new Vector3(volumeExtent,volumeExtent,0.0-Math.Sqrt(distanceSquared));
        Vector3.TransformProjection(ref position,ref projection,out Vector3 projected);
        Vector2 pixels=new Vector2(projected.X,projected.Y)*new Vector2(width,height)/2f;
        float product=Math.Abs(pixels.X*pixels.Y);if(float.IsNaN(product)||float.IsInfinity(product))return false;
        area=product;return product<20f;
    }
}
