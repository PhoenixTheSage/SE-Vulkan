using System;
using VRageRender;
namespace ClientPlugin.Rendering.Scene;

// Old DX11 actor contract: one current LOD, optional two-mesh transition.
// GeometryStage2 per-view shifts are a different path and are not applied here.
public sealed class StockActorLodState
{
    public int Current {get;private set;}
    public int Next {get;private set;}
    public float Transition {get;private set;}
    private bool initialized,border;
    private float startDistance;
    internal ClientPlugin.Rendering.Interop.NativeGpuOpaqueSelector Export()=>new(){Current=Current,Other=Next,Transition=Transition,Start=startDistance,Initialized=initialized?1u:0u,Border=border?1u:0u};
    internal void Import(ClientPlugin.Rendering.Interop.NativeGpuOpaqueSelector value){Current=value.Current;Next=value.Other;Transition=value.Transition;startDistance=value.Start;initialized=value.Initialized!=0;border=value.Border!=0;}
    public void Update(float distance,float[] thresholds,MyGlobalLoddingSettings global,MyPassLoddingSetting pass,float delta,bool skipTransition=false)
    {
        if(thresholds.Length==0 || !global.IsUpdateEnabled)return;
        int Clamp(int lod)=>Math.Max(0,Math.Min(thresholds.Length,lod));
        int Select(){int lod=0;while(lod<thresholds.Length && distance>=thresholds[lod])lod++;return Clamp(Math.Max(pass.MinLod,lod+pass.LodShift));}
        if(global.EnableLodSelection){Current=Next=Clamp(global.LodSelection);Transition=0;initialized=true;return;}
        if(!initialized || skipTransition){Current=Next=Select();Transition=0;initialized=true;return;}
        if(Transition!=0){float width=Math.Max(4f*(float)Math.Pow(2,Next),4);float progress=Math.Max(Transition+delta/global.MaxTransitionInSeconds,Math.Max(0,Math.Min(1,Math.Abs(distance-startDistance)/width/8)));float step=Math.Min(Math.Abs(Transition-progress),.25f);if(float.IsNaN(step)||float.IsInfinity(step))step=1;Transition+=step;if(Transition>=1){Current=Next;Transition=0;}return;}
        if(border){float threshold=Current==0?0:thresholds[Current-1];if(Math.Abs(distance-threshold)>threshold*.1f)border=false;return;}
        int selected=Select();if(selected!=Current){Next=selected;Transition=.001f;startDistance=distance;border=true;}
    }
}
