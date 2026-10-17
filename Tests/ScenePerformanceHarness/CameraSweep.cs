using System.Text.Json;
using Sandbox;
using Sandbox.Engine.Utils;
using Sandbox.Game.World;
using VRage;
using VRage.Game;
using VRageMath;
using VRageRender;
using ClientPlugin.Rendering.Interop;

namespace ScenePerformanceHarness;

public sealed partial class Plugin
{
    static bool CameraSweepEnabled => Environment.GetEnvironmentVariable("SE_VULKAN_CAMERA_SWEEP")=="1";
    MatrixD sweepBasis;
    Vector3D sweepPosition;
    double sweepStepAt;
    int sweepIndex=-1;
    string sweepCapture;
    readonly List<object> sweepEvidence=new();
    static readonly JsonSerializerOptions sweepJson=new(){IncludeFields=true,WriteIndented=true};
    static bool SweepIsolation => Environment.GetEnvironmentVariable("SE_VULKAN_SWEEP_ISOLATE")=="1";
    readonly float[] sweepAngles=SweepIsolation?new[]{0f,90f,90f,90f,90f,90f,90f,0f}:new[]{0f}.Concat(Enumerable.Range(1,24).Select(i=>i*15f)).Concat(Enumerable.Range(1,24).Select(i=>-i*15f)).ToArray();
    static volatile int sweepPolicy;
    static int appliedSweepPolicy=-1;
    void WriteSweepRayState(){
        var assembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetType("SERT.Integration.VulkanRayApi")!=null);
        if(assembly==null)return;
        var api=assembly.GetType("SERT.Integration.VulkanRayApi");
        var pluginType=assembly.GetType("ClientPlugin.Plugin");
        var plugin=pluginType.GetProperty("Instance").GetValue(null);
        var configType=assembly.GetType("ClientPlugin.Config");
        var config=configType.GetField("Current").GetValue(null);
        File.WriteAllText(Path.Combine(reports,"ray-state.json"),JsonSerializer.Serialize(new{Assembly=assembly.FullName,Enabled=configType.GetProperty("Enabled").GetValue(config),HardwareAcceleration=configType.GetProperty("HardwareAcceleration").GetValue(config),Status=pluginType.GetProperty("Status").GetValue(plugin),Native=api.GetMethod("Query").Invoke(null,null)},sweepJson));
    }
    static void ApplySweepPolicy(object __instance){
        if(!SweepIsolation || appliedSweepPolicy==sweepPolicy)return;
        var backend=(NativeBackend)BackendField.GetValue(__instance);if(!backend.IsCreated)return;
        backend.SetAsyncPresent(sweepPolicy!=2);
        backend.SetParallelCascades(sweepPolicy!=3);
        backend.SetParallelQueue(sweepPolicy!=4);
        backend.TextureValidationCacheEnabled=sweepPolicy!=5;
        appliedSweepPolicy=sweepPolicy;
    }

    void SweepCamera(double seconds)
    {
        if(seconds<45)return;
        if(Environment.GetEnvironmentVariable("SE_VULKAN_PLAYER_TURN")=="1"){SweepPlayerCamera();return;}
        double now=lifetime.Elapsed.TotalSeconds;
        if(sweepIndex<0)
        {
            if(matchFrameLoaded||benchmarkFrameLoaded)throw new Exception("A game-camera sweep cannot use renderer-only frame replay.");
            if(latestSystemCamera==null)throw new Exception("Spawn camera message not observed.");
            sweepBasis=MatrixD.Invert(latestSystemCamera.ViewMatrix);sweepPosition=latestSystemCamera.CameraPosition;
            MySession.Static.SetCameraController(MyCameraControllerEnum.Spectator,null,sweepPosition);
            // Spectator would otherwise render the player's head around the
            // saved first-person eye position. Hide only that local actor in
            // this unsaved private fixture, matching the first-person view.
            if(MySession.Static.LocalCharacter!=null)MySession.Static.LocalCharacter.Render.Visible=false;
            MySpectator.Static.Position=sweepPosition;
            MySpectator.Static.SpectatorCameraMovement=MySpectatorCameraMovementEnum.None;
            sweepIndex=0;sweepStepAt=now;
            File.WriteAllText(Path.Combine(reports,"sweep-start.json"),JsonSerializer.Serialize(new{PlayerTurn=false,Position=sweepPosition,Basis=sweepBasis,Angles=sweepAngles,Scope="Actual game spectator camera; local character hidden at first-person eye position; continuous yaw at saved spawn position, normal renderer defaults, unpaused simulation."},sweepJson));
        }
        float previous=sweepIndex==0?0:sweepAngles[sweepIndex-1];
        float target=sweepAngles[sweepIndex];
        // Cross zero after the completed positive revolution, rather than
        // interpolating 360 -> -15 through another complete positive turn.
        if(sweepIndex==25)previous=0;
        double elapsed=now-sweepStepAt;
        float yaw=MathHelper.Lerp(previous,target,(float)Math.Min(1,elapsed/.4));
        var rotation=MatrixD.CreateFromAxisAngle(sweepBasis.Up,MathHelper.ToRadians(yaw));
        var direction=Vector3D.TransformNormal(sweepBasis.Forward,rotation);
        MySpectator.Static.Position=sweepPosition;MySpectator.Static.SetTarget(sweepPosition+direction,sweepBasis.Up);
        if(elapsed<1)return;
        if(sweepCapture==null)
        {
            var observed=latestSystemCamera;
            if(observed==null||(observed.CameraPosition-sweepPosition).Length()>.01)return;
            var observedWorld=MatrixD.Invert(observed.ViewMatrix);
            if(Vector3D.Dot(observedWorld.Forward,direction)<.99999)return;
            sweepCapture=Path.Combine(reports,$"sweep-{sweepIndex:D2}-{target:+000;-000;000}.png");
            File.WriteAllText(Path.ChangeExtension(sweepCapture,"json"),JsonSerializer.Serialize(new{Yaw=target,Camera=observed,Environment=latestSystemEnvironment,GameCamera=Sandbox.Game.World.MySector.MainCamera.WorldMatrix,Controller=MySession.Static.CameraController.GetType().FullName,RequestedAt=DateTime.UtcNow},sweepJson));
            MyRenderProxy.TakeScreenshot(Vector2.One,sweepCapture,debug:false,ignoreSprites:false,showNotification:false);
            return;
        }
        if(elapsed<1.5||!File.Exists(sweepCapture))return;
        sweepEvidence.Add(new{Yaw=target,Capture=sweepCapture});
        File.WriteAllText(Path.Combine(reports,"sweep-progress.json"),JsonSerializer.Serialize(sweepEvidence,sweepJson));
        if(++sweepIndex<sweepAngles.Length){sweepCapture=null;sweepStepAt=now;return;}
        File.WriteAllText(Path.Combine(reports,"vulkan-status.txt"),ClientPlugin.Diagnostics.VulkanDiagnostics.Report());
        File.WriteAllText(Path.Combine(reports,"result.json"),JsonSerializer.Serialize(new{CameraSweep=true,Captures=sweepEvidence.Count,Position=sweepPosition,Scope="Captured actual game-camera rotations; visual review required."},sweepJson));
        finished=true;MySandboxGame.ExitThreadSafe();
    }

    static float sweepTurnInput;
    double playerYaw,playerLastYaw,playerSettledAt=-1;
    void SweepPlayerCamera()
    {
        var player=MySession.Static.LocalCharacter??throw new Exception("Saved player missing.");
        double now=lifetime.Elapsed.TotalSeconds;
        var observed=latestSystemCamera??throw new Exception("Player camera missing.");
        var world=MatrixD.Invert(observed.ViewMatrix);
        if(sweepIndex<0){sweepBasis=world;sweepPosition=observed.CameraPosition;sweepIndex=0;sweepStepAt=now;File.WriteAllText(Path.Combine(reports,"sweep-start.json"),JsonSerializer.Serialize(new{PlayerInput=true,Position=sweepPosition,Controller=MySession.Static.CameraController.GetType().FullName,player.IsInFirstPersonView,Angles=sweepAngles},sweepJson));}
        double angle=Math.Atan2(Vector3D.Dot(world.Forward,sweepBasis.Right),Vector3D.Dot(world.Forward,sweepBasis.Forward))*180/Math.PI;
        double delta=angle-playerLastYaw;if(delta>180)delta-=360;if(delta< -180)delta+=360;playerYaw+=delta;playerLastYaw=angle;
        float label=sweepAngles[sweepIndex];double goal=label<0?360+label:label;
        if(SweepIsolation)sweepPolicy=sweepIndex;
        double error=goal-playerYaw;
        if(Math.Abs(error)>.6){playerSettledAt=-1;sweepTurnInput=(float)Math.Max(-20,Math.Min(20,error));}
        else{sweepTurnInput=0;if(playerSettledAt<0)playerSettledAt=now;}
        File.WriteAllText(Path.Combine(reports,"turn-observed.json"),JsonSerializer.Serialize(new{Index=sweepIndex,Requested=goal,Observed=playerYaw,Error=error,Input=sweepTurnInput,player.RotationSpeed,Position=observed.CameraPosition,Controller=MySession.Static.CameraController.GetType().FullName},sweepJson));
        if(now-sweepStepAt>20)throw new Exception("Player input failed to reach requested yaw; see turn-observed.json.");
        if(playerSettledAt<0||now-playerSettledAt<(SweepIsolation?3:.5))return;
        if(sweepCapture==null){sweepCapture=Path.Combine(reports,$"sweep-{sweepIndex:D2}-{label:+000;-000;000}.png");if(Environment.GetEnvironmentVariable("SE_VULKAN_RAY_AUDIT")=="1"){WriteSweepRayState();File.Copy(Path.Combine(reports,"ray-state.json"),Path.Combine(reports,$"ray-angle-{sweepIndex:D2}.json"),true);}File.WriteAllText(Path.ChangeExtension(sweepCapture,"json"),JsonSerializer.Serialize(new{RequestedYaw=label,ActualUnwrappedYaw=playerYaw,Camera=observed,Environment=latestSystemEnvironment,RequestedAt=DateTime.UtcNow},sweepJson));MyRenderProxy.TakeScreenshot(Vector2.One,sweepCapture,debug:false,ignoreSprites:false,showNotification:false);return;}
        if(!File.Exists(sweepCapture)||now-playerSettledAt<1)return;
        sweepEvidence.Add(new{Yaw=playerYaw,Capture=sweepCapture});File.WriteAllText(Path.Combine(reports,"sweep-progress.json"),JsonSerializer.Serialize(sweepEvidence,sweepJson));
        if(++sweepIndex<sweepAngles.Length){sweepCapture=null;playerSettledAt=-1;sweepStepAt=now;return;}
        WriteSweepRayState();File.WriteAllText(Path.Combine(reports,"vulkan-status.txt"),ClientPlugin.Diagnostics.VulkanDiagnostics.Report());File.WriteAllText(Path.Combine(reports,"result.json"),JsonSerializer.Serialize(new{PlayerInput=true,Captures=sweepEvidence.Count},sweepJson));finished=true;MySandboxGame.ExitThreadSafe();
    }
}
