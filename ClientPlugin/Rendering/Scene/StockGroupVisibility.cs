using VRageMath;
namespace ClientPlugin.Rendering.Scene;

// One manual-group query owner. Result age/reentry/near guards follow stock
// asynchronous ownership; a changed query epoch cannot hide a recreated group.
public sealed class StockGroupVisibility
{
    public ulong Epoch {get;private set;}=1;
    public long LastInView {get;private set;}=-1;
    public long LastResultFrame {get;private set;}=-1;
    public bool Hidden {get;private set;}
    private BoundingBoxD hiddenVolume;
    public void Reset(){Epoch++;Hidden=false;LastResultFrame=-1;}
    public bool Observe(long frame,BoundingBoxD bounds,Vector3D eye,float near)
    {
        bool reentry=LastInView!=frame-1;LastInView=frame;
        var expanded=bounds.GetInflated(bounds.Size*.5);bool close=expanded.DistanceSquared(ref eye)<near;
        if(reentry||close){Reset();return false;}
        if(Hidden && hiddenVolume.Contains(bounds)!=ContainmentType.Contains){Reset();return false;}
        if(LastResultFrame<frame-100)Hidden=false;
        return Hidden;
    }
    public bool Accept(ulong epoch,long queryFrame,long frame,BoundingBoxD queriedInflatedBounds,BoundingBoxD currentBounds,bool visible)
    {
        if(epoch!=Epoch || frame-queryFrame>100 || queriedInflatedBounds.Contains(currentBounds)!=ContainmentType.Contains)return false;
        Hidden=!visible;hiddenVolume=queriedInflatedBounds;LastResultFrame=frame;return true;
    }
}
