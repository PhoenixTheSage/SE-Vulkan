using System;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

// MyFlareOcclusionRenderer's completed-query scheduling and interpolation.
// Each request has an identity so repeated completed-slot results cannot
// accidentally satisfy a later query for the same light.
public sealed class StockFlareOcclusionState
{
    public float Current {get;private set;}=1;
    public float Last {get;private set;}=1;
    public float Next {get;private set;}=1;
    public float LastQueryTime {get;private set;}
    public float QueryTime {get;private set;}
    public ulong PendingId {get;private set;}
    public bool ShouldQuery(float time)=>PendingId==0 && time>=QueryTime;
    public void Begin(ulong id){if(id==0 || PendingId!=0)throw new InvalidOperationException("Flare query is already pending or has no identity.");PendingId=id;}
    public bool Complete(ulong id,float occlusion,float time,float minimumMs,float randomMs,float random)
    {
        if(PendingId==0 || PendingId!=id)return false;
        PendingId=0;
        if(occlusion==1)Last=Next=1;
        else{Last=Current;Next=occlusion;}
        LastQueryTime=time;
        QueryTime=time+(Last==1 && Next!=1?minimumMs/2:minimumMs+random*randomMs);
        return true;
    }
    public float Sample(float time)
    {
        Current=Math.Abs(QueryTime-LastQueryTime)<.1f?Next:MathHelper.Lerp(Last,Next,MathHelper.Saturate((time-LastQueryTime)/(QueryTime-LastQueryTime)));
        return Current;
    }
}
