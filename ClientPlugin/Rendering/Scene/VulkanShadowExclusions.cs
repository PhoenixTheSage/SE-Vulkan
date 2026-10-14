using System.Collections.Generic;
using ClientPlugin.Rendering.Interop;
using VRageRender.Messages;
namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    private readonly Dictionary<uint,HashSet<uint>> shadowIgnores=new();
    private readonly HashSet<uint> dirtyShadowOwners=new();
    private readonly HashSet<uint> frameExcludedActors=new();
    private void PrepareExcludedActors()
    {
        frameExcludedActors.Clear();
        foreach(var owner in frameSpotActors.Values)if(shadowIgnores.TryGetValue(owner,out var ignored))frameExcludedActors.UnionWith(ignored);
    }
    private readonly Dictionary<uint,uint> frameSpotActors=new();
    private readonly List<NativeShadowExclusion> frameShadowExclusions=new();
    private bool ProcessShadowExclusion(MyRenderMessageBase message){if(message is MyRenderMessageSetLightShadowIgnore s){if(!shadowIgnores.TryGetValue(s.ID,out var values))shadowIgnores[s.ID]=values=new();if(values.Add(s.ID2))dirtyShadowOwners.Add(s.ID);return true;}if(message is MyRenderMessageClearLightShadowIgnore c){if(shadowIgnores.Remove(c.ID))dirtyShadowOwners.Add(c.ID);return true;}return false;}
    private void SubmitShadowExclusion(uint draw,uint actor){foreach(var spot in frameSpotActors)if(shadowIgnores.TryGetValue(spot.Value,out var ignored) && ignored.Contains(actor)){if(frameShadowExclusions.Count>=8192)throw new System.InvalidOperationException("Shadow exclusion batch exceeds bounded budget.");frameShadowExclusions.Add(new NativeShadowExclusion{Light=spot.Key,Draw=draw});}}
}
