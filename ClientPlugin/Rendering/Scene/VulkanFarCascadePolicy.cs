using System;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    public bool StockFarCascadesEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_STOCK_FAR_CASCADES")=="1";
    FarCascadePolicyStats farCascadeStats;
    public FarCascadePolicyStats GetFarCascadePolicyStats()=>farCascadeStats;
    public static uint StockOldActorCascadeMask(uint fittedMask,RenderFlags flags)
        =>(flags&RenderFlags.DrawOutsideViewDistance)!=0?fittedMask:fittedMask&15u;
    static bool KnownStockOldPipeline(ActorData actor)=>actor.Character || actor.InstanceBuffer!=uint.MaxValue || actor.ConvertedOldMaterial!=null || (actor.Flags&RenderFlags.ForceOldPipeline)!=0;
    uint FilterFarActorCascades(uint mask,ActorData actor)
    {
        if(!StockFarCascadesEnabled || !KnownStockOldPipeline(actor))return mask;
        uint selected=StockOldActorCascadeMask(mask,actor.Flags);farCascadeStats.TestedActors++;
        if(selected!=mask)farCascadeStats.FilteredActors++;
        uint removed=mask&~selected;while(removed!=0){farCascadeStats.RemovedMemberships++;removed&=removed-1;}
        return selected;
    }
}
public struct FarCascadePolicyStats {public uint Enabled,TestedActors,FilteredActors,RemovedMemberships;}
