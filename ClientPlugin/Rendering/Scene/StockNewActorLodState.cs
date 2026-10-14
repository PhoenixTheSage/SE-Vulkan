using System;
using VRageRender;
namespace ClientPlugin.Rendering.Scene;
public sealed class StockNewActorLodState
{
    public int Current,Previous=-1;public float Transition;bool initialized;float start;
    public void Reset(){Current=0;Previous=-1;Transition=0;initialized=false;start=0;}
    internal ClientPlugin.Rendering.Interop.NativePassLod Export()=>new(){Current=Current,Previous=Previous,Transition=Transition,Start=start,Initialized=initialized?1u:0u};
    internal void Import(ClientPlugin.Rendering.Interop.NativePassLod value){Current=value.Current;Previous=value.Previous;Transition=value.Transition;start=value.Start;initialized=value.Initialized!=0;}
    public void Update(float distance,float[] thresholds,MyGlobalLoddingSettings settings,float delta,bool immediate){
        int Select(){int i=0;while(i<thresholds.Length && distance>=thresholds[i])i++;return i;}
        if(settings.EnableLodSelection){Current=Math.Min(thresholds.Length,Math.Max(0,settings.LodSelection));Previous=-1;Transition=0;initialized=true;return;}
        if(!settings.IsUpdateEnabled && initialized)return;
        if(!initialized || immediate){Current=Select();Previous=-1;Transition=0;initialized=true;return;}
        if(Transition==0){int selected=Current;float ratio=settings.HisteresisRatio;
            while(selected>0 && distance<thresholds[selected-1]*(1-ratio))selected--;
            while(selected<thresholds.Length && distance>thresholds[selected]*(1+ratio))selected++;
            if(selected==Current)return;Previous=Current;Current=selected;start=distance;
        }
        float moved=Math.Abs(start-distance),dead=Math.Max(settings.TransitionDeadZoneConst,settings.TransitionDeadZoneDistanceMult*moved);
        float duration=moved<=dead?settings.MaxTransitionInSeconds:settings.MinTransitionInSeconds;
        Transition+=duration>0?Math.Max(0,delta)/duration:1;
        if(Transition>=1){Transition=0;Previous=-1;}
    }
}
