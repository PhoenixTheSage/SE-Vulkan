using System.Collections.Generic;
using ClientPlugin.Rendering.Interop;
using VRageMath;
namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    private sealed class VisibilitySnapshot {public MatrixD Camera;public float Fraction;public long Revision;public uint Width,Height;public readonly Dictionary<uint,BoundingBoxD> Bounds=new();}
    private readonly Dictionary<ulong,VisibilitySnapshot> visibilitySnapshots=new();
    private readonly Dictionary<uint,BoundingBoxD> occludedBounds=new();
    private readonly List<NativeOcclusionDraw> occlusionPackets=new();
    private readonly VisibilitySnapshot[] visibilityRing=new VisibilitySnapshot[8];
    private readonly NativePackedOcclusionDraw[] packedOcclusion=new NativePackedOcclusionDraw[8192];
    private int packedOcclusionCount;
    private VisibilitySnapshot currentVisibility;
    public long ExternalDepthRevision;private long previousExternalDepthRevision;
    private ulong visibilityToken,observedVisibilityToken;
    private MatrixD visibilityCamera;
    private float visibilityFraction;private long visibilityRevision,appliedVisibilityRevision;private int visibilityTextureCount;private uint visibilityWidth,visibilityHeight;
    public long OcclusionCulls{get;private set;}
    private void BeginOcclusion()
    {
        if(previousGroupMode!=GroupOcclusionEnabled){ResetGroupMode();previousGroupMode=GroupOcclusionEnabled;}
        if(GroupOcclusionEnabled){BeginGroupOcclusion();return;}
        MatrixD view=frustum?.Matrix??MatrixD.Identity;float fraction=backend.RenderFraction;
        ObserveOcclusionFrame(view);
        // OnMove/parent propagation also covers RenderDirect animation. That
        // revision avoids scanning all actor matrices on every camera frame.
        bool changed=ExternalDepthRevision!=previousExternalDepthRevision;
        previousExternalDepthRevision=ExternalDepthRevision;
        if(changed)InvalidateOcclusion("ExternalDepth");
        if(TextureCache.Count!=visibilityTextureCount){visibilityTextureCount=TextureCache.Count;InvalidateOcclusion("Textures");}
        if(view!=visibilityCamera || fraction!=visibilityFraction || visibilityWidth!=backend.PresentationWidth || visibilityHeight!=backend.PresentationHeight || visibilityRevision!=appliedVisibilityRevision){occludedBounds.Clear();visibilityCamera=view;visibilityFraction=fraction;visibilityWidth=backend.PresentationWidth;visibilityHeight=backend.PresentationHeight;appliedVisibilityRevision=visibilityRevision;}
        int resultCount=0;ulong token;
        var results=backend.RetainedSubmissionEnabled?backend.GetOcclusionBorrowed(out resultCount,out token):backend.GetOcclusion(out token);
        if(!backend.RetainedSubmissionEnabled)resultCount=results.Length;
        if(token!=observedVisibilityToken && visibilitySnapshots.TryGetValue(token,out var previous))
        {
            observedVisibilityToken=token;occludedBounds.Clear();
            bool cameraMatches=previous.Camera==view,revisionMatches=previous.Revision==visibilityRevision,extentMatches=previous.Fraction==fraction && previous.Width==backend.PresentationWidth && previous.Height==backend.PresentationHeight;
            if(OcclusionDiagnosticsEnabled){if(cameraMatches&&revisionMatches&&extentMatches)acceptedSnapshots++;else rejectedSnapshots++;if(!cameraMatches)cameraRejectedSnapshots++;if(!revisionMatches)revisionRejectedSnapshots++;if(!extentMatches)extentRejectedSnapshots++;for(int i=0;i<resultCount;i++)if(results[i].Visible==0)completedHiddenResults++;}
            if(cameraMatches&&revisionMatches&&extentMatches)for(int i=0;i<resultCount;++i){var result=results[i];if(result.Visible==0 && previous.Bounds.TryGetValue((uint)result.Id,out var bounds))occludedBounds[(uint)result.Id]=bounds;}
        }
        else if(OcclusionDiagnosticsEnabled && token!=observedVisibilityToken)missingSnapshots++;
        if(backend.RetainedSubmissionEnabled){int slot=(int)((visibilityToken+1)%8);currentVisibility=visibilityRing[slot]??(visibilityRing[slot]=new VisibilitySnapshot());if(visibilityToken>=8)visibilitySnapshots.Remove(visibilityToken-7);currentVisibility.Bounds.Clear();}
        else currentVisibility=new VisibilitySnapshot();
        currentVisibility.Camera=view;currentVisibility.Fraction=fraction;currentVisibility.Revision=visibilityRevision;currentVisibility.Width=backend.PresentationWidth;currentVisibility.Height=backend.PresentationHeight;
        occlusionPackets.Clear();packedOcclusionCount=0;
    }
    private bool QueryActorVisibility(uint id,BoundingBoxD bounds)
    {
        if(GroupOcclusionEnabled)return QueryGroupVisibility(id);
        if(RenderSettings.DisableOcclusionQueries || RenderSettings.IgnoreOcclusionQueries)return false;
        if((backend.RetainedSubmissionEnabled?packedOcclusionCount:occlusionPackets.Count)<8192)
        {
            Vector3D min=bounds.Min-FrameView.CameraPosition,size=bounds.Size;
            bool near= bounds.Contains(FrameView.CameraPosition)==ContainmentType.Contains || frustum!=null && bounds.Intersects(frustum.Near)==PlaneIntersectionType.Intersecting;
            if(backend.RetainedSubmissionEnabled)packedOcclusion[packedOcclusionCount++]=new NativePackedOcclusionDraw{Id=id,MinimumX=(float)min.X,MinimumY=(float)min.Y,MinimumZ=(float)min.Z,ExtentX=(float)size.X,ExtentY=(float)size.Y,ExtentZ=(float)size.Z,Flags=near?1u:0u};
            else occlusionPackets.Add(new NativeOcclusionDraw{Id=id,Minimum=new[]{(float)min.X,(float)min.Y,(float)min.Z},Extents=new[]{(float)size.X,(float)size.Y,(float)size.Z},Flags=near?1u:0u});currentVisibility.Bounds[id]=bounds;
            if(near)return false;
        }
        bool hidden=occludedBounds.TryGetValue(id,out var previous) && previous.Equals(bounds);if(hidden)OcclusionCulls++;return hidden;
    }
    private void EndOcclusion()
    {
        if(GroupOcclusionEnabled){EndGroupOcclusion();return;}
        ulong token=++visibilityToken;visibilitySnapshots[token]=currentVisibility;if(token>8)visibilitySnapshots.Remove(token-8);if(backend.RetainedSubmissionEnabled)backend.QueueOcclusion(token,packedOcclusion,packedOcclusionCount);else backend.QueueOcclusion(token,occlusionPackets.ToArray());
    }
}
