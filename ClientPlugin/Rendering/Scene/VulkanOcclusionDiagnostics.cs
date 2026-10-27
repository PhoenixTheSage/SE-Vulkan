using System.Collections.Generic;
using VRageRender.Messages;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    // Opt-in attribution of the existing conservative policy. Counters do not
    // relax camera, depth, bounds or result-token checks.
    public bool OcclusionDiagnosticsEnabled {get;set;}
    private readonly Dictionary<string,long> occlusionRevisionReasons=new();
    private long occlusionFrames,acceptedSnapshots,rejectedSnapshots,cameraRejectedSnapshots,revisionRejectedSnapshots,extentRejectedSnapshots,missingSnapshots,completedHiddenResults;
    private double maximumEyeStep,maximumViewDelta;
    private Vector3D diagnosticEye;
    private bool haveDiagnosticEye;
    private long diagnosticCullBaseline;
    public void ResetOcclusionDiagnostics(){occlusionRevisionReasons.Clear();occlusionFrames=acceptedSnapshots=rejectedSnapshots=cameraRejectedSnapshots=revisionRejectedSnapshots=extentRejectedSnapshots=missingSnapshots=completedHiddenResults=0;maximumEyeStep=maximumViewDelta=0;haveDiagnosticEye=false;diagnosticCullBaseline=OcclusionCulls;}
    private void InvalidateOcclusion(string reason)
    {
        if(building)visibilityRevision++;else applyVisibilityRevision++;
        if(OcclusionDiagnosticsEnabled){occlusionRevisionReasons.TryGetValue(reason,out long count);occlusionRevisionReasons[reason]=count+1;}
    }
    private void InvalidateOcclusionMessage(MyRenderMessageBase message)
    {
        if(building)visibilityRevision++;else applyVisibilityRevision++;
        if(OcclusionDiagnosticsEnabled){string reason="Message."+message.MessageType;occlusionRevisionReasons.TryGetValue(reason,out long count);occlusionRevisionReasons[reason]=count+1;}
    }
    public object CaptureOcclusionDiagnostics()=>new{Frames=occlusionFrames,AcceptedSnapshots=acceptedSnapshots,RejectedSnapshots=rejectedSnapshots,CameraRejectedSnapshots=cameraRejectedSnapshots,RevisionRejectedSnapshots=revisionRejectedSnapshots,ExtentRejectedSnapshots=extentRejectedSnapshots,MissingSnapshots=missingSnapshots,CompletedHiddenResults=completedHiddenResults,SceneCulls=OcclusionCulls-diagnosticCullBaseline,MaximumEyeStepMetres=maximumEyeStep,MaximumViewMatrixDelta=maximumViewDelta,RevisionReasons=new Dictionary<string,long>(occlusionRevisionReasons),Scope="Opt-in counters since reset for unchanged production occlusion policy; rejection reasons overlap; camera matrix delta is unitless, eye step is world metres. GPU hidden results do not imply applied scene culls."};
    private void ObserveOcclusionFrame(MatrixD view)
    {
        if(!OcclusionDiagnosticsEnabled)return;
        occlusionFrames++;if(haveDiagnosticEye)maximumEyeStep=System.Math.Max(maximumEyeStep,Vector3D.Distance(FrameView.CameraPosition,diagnosticEye));diagnosticEye=FrameView.CameraPosition;haveDiagnosticEye=true;
        double Max4(double a,double b,double c,double d)=>System.Math.Max(System.Math.Max(System.Math.Abs(a),System.Math.Abs(b)),System.Math.Max(System.Math.Abs(c),System.Math.Abs(d)));
        if(occlusionFrames>1){var previous=visibilityCamera;double r1=Max4(view.M11-previous.M11,view.M12-previous.M12,view.M13-previous.M13,view.M14-previous.M14),r2=Max4(view.M21-previous.M21,view.M22-previous.M22,view.M23-previous.M23,view.M24-previous.M24),r3=Max4(view.M31-previous.M31,view.M32-previous.M32,view.M33-previous.M33,view.M34-previous.M34),r4=Max4(view.M41-previous.M41,view.M42-previous.M42,view.M43-previous.M43,view.M44-previous.M44);maximumViewDelta=System.Math.Max(maximumViewDelta,System.Math.Max(System.Math.Max(r1,r2),System.Math.Max(r3,r4)));}
    }
}
