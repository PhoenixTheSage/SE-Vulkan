using System.Text.Json;
using Sandbox;
using VRageMath;
using VRageRender;
namespace ScenePerformanceHarness;
public sealed partial class Plugin
{
    static bool selectionAudit;
    static volatile bool selectionArmed;
    int selectionStage;double selectionUntil;bool ownsSelectionPause;
    void ReleaseSelectionPause(){if(ownsSelectionPause){ownsSelectionPause=false;MySandboxGame.PausePop();}}
    void CapturePausedSelection(){
        double now=lifetime.Elapsed.TotalSeconds;
        if(selectionStage==0){MySandboxGame.PausePush();ownsSelectionPause=true;selectionUntil=now+8;selectionStage=1;return;}
        if(!MySandboxGame.IsPaused)throw new Exception("Private selection capture lost its pause.");
        if(now<selectionUntil)return;
        if(selectionStage==1){FreezeSystemFrame();StartProbeCoverage();oldGeometryViews.Clear();newGeometryViews.Clear();oldSizeCandidates.Clear();haveSelectionMainFrustum=false;workloadSnapshot=null;selectionArmed=true;selectionUntil=now+1;selectionStage=2;return;}
        if(selectionStage==2){if(workloadSnapshot==null)return;
            File.WriteAllText(Path.Combine(reports,"geometry-workload.json"),JsonSerializer.Serialize(workloadSnapshot,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(reports,"view.png"),debug:false,ignoreSprites:false,showNotification:false);selectionUntil=now+1;selectionStage=3;return;}
        if(!File.Exists(Path.Combine(reports,"view.png"))||!ProbeCoverageComplete)return;
        WriteProbeCoverage();
        File.WriteAllText(Path.Combine(reports,"result.json"),JsonSerializer.Serialize(new{Backend=expected,Instrumented=true,Paused=true,SelectionAudit=true,Width=MyRenderProxy.BackBufferResolution.X,Height=MyRenderProxy.BackBufferResolution.Y,Scope="Private copied save and paused saved-camera selection snapshot. Independent loads may still differ in actor IDs, residency, visibility and settled physics. Diagnostic only; no FPS claim."},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(reports,"vulkan-status.txt"),ClientPlugin.Diagnostics.VulkanDiagnostics.Report());
        WriteFrameStateAudit();WriteSystemParityAudit();ReleaseSelectionPause();finished=true;MySandboxGame.ExitThreadSafe();
    }
}
