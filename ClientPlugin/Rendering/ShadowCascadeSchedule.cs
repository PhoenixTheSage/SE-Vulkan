using System;

namespace ClientPlugin.Rendering;

// Installed MyShadowCascades scheduling, independent of resource and GPU ownership.
internal sealed class ShadowCascadeSchedule
{
    private static readonly int[] intervals={1,1,1,1,4,8,8,8};
    private static readonly int[] phases={0,0,0,0,3,5,6,7};
    private readonly long[] firstSkip=new long[8];
    private long frame;
    public void Reset(){frame=0;for(int c=0;c<8;c++)firstSkip[c]=20;}
    public ShadowCascadeSchedule(){Reset();}
    public void BeginFrame(){++frame;}
    public bool IsDue(int cascade,bool initialized,bool everyFrame,bool frozen,double nearSplit,double cameraDistanceSquared)
    {
        if(!initialized)return true;
        if(frozen)return false;
        bool jump=nearSplit>1000 && cameraDistanceSquared>1000.0*1000.0;
        return everyFrame || jump || frame%intervals[cascade]==phases[cascade];
    }
    public bool IsUsed(int cascade,bool enabled,bool feedbackReady,uint pixels)
    {
        // No readback is not proof of zero usage. Never wait for feedback.
        if(!enabled || !feedbackReady || pixels>=10){firstSkip[cascade]=frame+20;return true;}
        return frame<=firstSkip[cascade];
    }
}
