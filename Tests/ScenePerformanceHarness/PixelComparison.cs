using System.Text.Json;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering.Interop;
using Sandbox;
using VRageMath;
using VRageRender;

namespace ScenePerformanceHarness;

public sealed partial class Plugin
{
    static bool comparePixels,compareLightPixels,compareSubmissionPixels,compareGroupPixels;
    static bool PixelMode=>compareGroupPixels?groupOcclusionEnabled:compareSubmissionPixels?submissionEnabled:compareLightPixels?lightTilesEnabled:wideEnabled;
    static void SetPixelMode(bool mode){if(compareGroupPixels)groupOcclusionEnabled=mode;else if(compareSubmissionPixels)submissionEnabled=mode;else if(compareLightPixels)lightTilesEnabled=mode;else wideEnabled=mode;}
    sealed class PixelFrameEvidence
    {
        public readonly bool Mode;
        public readonly NativeLightTileStats Tiles;
        public readonly NativePerformanceStats Stats;
        public readonly NativeResolutionStats Resolution;
        public readonly SceneSubmissionStats Submission;
        public readonly object Groups;
        public PixelFrameEvidence(bool mode,NativePerformanceStats stats,NativeResolutionStats resolution,NativeLightTileStats tiles,SceneSubmissionStats submission,object groups){Mode=mode;Stats=stats;Resolution=resolution;Tiles=tiles;Submission=submission;Groups=groups;}
    }
    static volatile PixelFrameEvidence pixelEvidence;
    int pixelStage;
    double pixelUntil;
    bool ownsPixelPause;
    readonly List<object> pixelRequests=new();
    static string[] pixelFiles={"16m-a.png","16m-b.png","64m-a.png","64m-b.png","16m-c.png","16m-d.png"};
    void ReleasePixelPause(){if(ownsPixelPause){ownsPixelPause=false;MySandboxGame.PausePop();}}
    void CapturePausedViews()
    {
        double now=lifetime.Elapsed.TotalSeconds;
        if(pixelStage==0){MySandboxGame.PausePush();ownsPixelPause=true;SetPixelMode(false);pixelUntil=now+8;pixelStage=1;return;}
        if(!MySandboxGame.IsPaused)throw new Exception("Private pixel comparison lost its pause.");
        if(now<pixelUntil)return;
        void Capture(string file){
            var completed=pixelEvidence;
            if(completed==null || completed.Mode!=PixelMode)return;
            var frameStats=completed.Stats;
            if(frameStats.LogicalDraws==0)
                throw new Exception("Pixel capture did not observe the requested grouping and live world packets.");
            pixelRequests.Add(new{File=file,GroupOcclusionRequested=compareGroupPixels?(bool?)groupOcclusionEnabled:null,GroupStats=completed.Groups,RetainedSubmissionRequested=compareSubmissionPixels?(bool?)submissionEnabled:null,SubmissionStats=completed.Submission,RigidCellMetres=compareLightPixels||compareSubmissionPixels||compareGroupPixels?(int?)null:wideEnabled?64:16,LightTilesRequested=compareLightPixels?(bool?)lightTilesEnabled:null,LightTileStats=completed.Tiles,Paused=MySandboxGame.IsPaused,
                ActualCellMetres=compareLightPixels||compareSubmissionPixels?(int?)null:completed.Mode?64:16,PhysicalPackets=frameStats.SubmittedDraws,
                LogicalDraws=frameStats.LogicalDraws,RequestedAtSeconds=now,Camera=Sandbox.Game.World.MySector.MainCamera.Position.ToString()});
            MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(reports,file),debug:false,ignoreSprites:false,showNotification:false);
            pixelUntil=now+1;pixelStage++;
        }
        if(pixelStage==1){Capture(pixelFiles[0]);return;}
        if(pixelStage==2){Capture(pixelFiles[1]);return;}
        if(pixelStage==3){if(!File.Exists(Path.Combine(reports,pixelFiles[1])))return;SetPixelMode(true);pixelUntil=now+8;pixelStage++;return;}
        if(pixelStage==4){Capture(pixelFiles[2]);return;}
        if(pixelStage==5){Capture(pixelFiles[3]);return;}
        if(pixelStage==6){if(!File.Exists(Path.Combine(reports,pixelFiles[3])))return;SetPixelMode(false);pixelUntil=now+8;pixelStage++;return;}
        if(pixelStage==7){Capture(pixelFiles[4]);return;}
        if(pixelStage==8){Capture(pixelFiles[5]);return;}
        if(pixelFiles.Any(file=>!File.Exists(Path.Combine(reports,file))))return;
        var final=pixelEvidence;if(final==null || final.Mode!=PixelMode)return;
        var stats=final.Stats;var resolution=final.Resolution;
        if(stats.LogicalDraws==0 || resolution.Width!=1920 || resolution.Height!=1080 || resolution.Fraction!=1)
            throw new Exception("Paused comparison did not render the full-resolution world.");
        File.WriteAllText(Path.Combine(reports,"pixel-comparison.json"),JsonSerializer.Serialize(new{
            Paused=true,Requests=pixelRequests,LogicalDraws=stats.LogicalDraws,Resolution=resolution,
            Scope=compareGroupPixels?"Private paused saved-camera legacy/group/legacy visibility sequence; repeated same-mode images estimate temporal changes. No FPS claim.":compareSubmissionPixels?"Private paused saved-camera legacy/prepared/legacy submission sequence; repeated same-mode images estimate residual temporal changes. No FPS claim.":compareLightPixels?"Private paused saved-camera full/GPU/full light-list sequence; repeated same-mode images estimate residual temporal changes. No FPS claim.":"Private paused saved-camera 16/64/16 sequence; repeated same-mode images estimate residual temporal changes. No FPS claim."
        },new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        File.WriteAllText(Path.Combine(reports,"vulkan-status.txt"),VulkanDiagnostics.Report());
        ReleasePixelPause();finished=true;MySandboxGame.ExitThreadSafe();
    }
}
