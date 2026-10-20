using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using ClientPlugin.Rendering.Content;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    sealed class MainGroupKeyCache {public MainGroupKeyCache(){}public VulkanModel Model;public uint Revision;public bool Metal;public int State;public object Key;}
    readonly ConditionalWeakTable<ActorData,MainGroupKeyCache> mainGroupKeys=new();
    object MainGroupKey(ActorData actor,bool root=false){
        if(UsesOldDecalShaders(actor))return null;
        var model=root?actor.Model:actor.RenderModel??actor.Model;
        bool metal=(actor.Flags&RenderFlags.MetalnessColorable)!=0;
        int state=actor.Dithering<0?2:actor.Dithering>0?3:actor.LodAlpha!=0?1:0;
        var cached=mainGroupKeys.GetOrCreateValue(actor);
        if(cached.Key!=null&&cached.Model==model&&cached.Revision==actor.MaterialRevision&&cached.Metal==metal&&cached.State==state)return cached.Key;
        var identity=new StringBuilder();
        foreach(var item in actor.Materials.OrderBy(x=>x.Key,StringComparer.Ordinal)){
            var material=item.Value;var t=material.Textures;bool hidden=(material.Flags.Add&RenderFlags.SkipInMainView)!=0||(material.Flags.Remove&RenderFlags.Visible)!=0;
            if(!hidden&&t.ColorMetalFileName==null&&t.NormalGlossFileName==null&&t.ExtensionsFileName==null&&t.AlphamaskFileName==null)continue;
            foreach(string value in new[]{item.Key,t.ColorMetalFileName,t.NormalGlossFileName,t.ExtensionsFileName,t.AlphamaskFileName}){if(value==null)identity.Append("-:");else identity.Append(value.Length).Append(':').Append(value);}
            identity.Append(hidden?'1':'0');
        }
        cached.Model=model;cached.Revision=actor.MaterialRevision;cached.Metal=metal;cached.State=state;
        return cached.Key=Tuple.Create(model,identity.ToString(),metal,state);
    }
}
