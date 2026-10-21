using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    // Structural key with the former Tuple<model,identity,metal,state> equality,
    // hashed once. Native groups stay keyed by equal values, not by instance.
    internal sealed class MainGroupIdentity:NativeBackend.MainGroupKeyBase,IEquatable<MainGroupIdentity>
    {
        readonly VulkanModel model;readonly string identity;readonly bool metal;readonly int state,hash;
        public MainGroupIdentity(VulkanModel model,string identity,bool metal,int state){this.model=model;this.identity=identity;this.metal=metal;this.state=state;
            unchecked{hash=(((model?.GetHashCode()??0)*31+(identity?.GetHashCode()??0))*31+(metal?1:0))*31+state;}}
        public bool Equals(MainGroupIdentity other)=>other!=null&&(ReferenceEquals(this,other)||hash==other.hash&&metal==other.metal&&state==other.state&&Equals(model,other.model)&&string.Equals(identity,other.identity));
        public override bool Equals(object obj)=>Equals(obj as MainGroupIdentity);
        public override int GetHashCode()=>hash;
    }
    // One actor asks for its root model and each selected LOD model every
    // frame. The identity depends only on material overrides, whose every
    // relevant change bumps MaterialRevision, so it is rebuilt only then.
    internal sealed class MainGroupKeyCache {public uint Revision;public string Identity;public readonly List<MainGroupIdentity> Keys=new();public readonly List<(VulkanModel Model,bool Metal,int State)> Inputs=new();}
    object MainGroupKey(ActorData actor,bool root=false){
        if(UsesOldDecalShaders(actor))return null;
        var model=root?actor.Model:actor.RenderModel??actor.Model;
        bool metal=(actor.Flags&RenderFlags.MetalnessColorable)!=0;
        int state=actor.Dithering<0?2:actor.Dithering>0?3:actor.LodAlpha!=0?1:0;
        var cached=actor.MainGroupKeys??=new MainGroupKeyCache();
        if(cached.Identity==null||cached.Revision!=actor.MaterialRevision){cached.Identity=InternIdentity(MaterialIdentity(actor));cached.Revision=actor.MaterialRevision;cached.Keys.Clear();cached.Inputs.Clear();}
        for(int i=0;i<cached.Inputs.Count;i++){var input=cached.Inputs[i];if(ReferenceEquals(input.Model,model)&&input.Metal==metal&&input.State==state)return cached.Keys[i];}
        if(cached.Keys.Count>=32){cached.Keys.Clear();cached.Inputs.Clear();}
        var key=new MainGroupIdentity(model,cached.Identity,metal,state);cached.Keys.Add(key);cached.Inputs.Add((model,metal,state));return key;
    }
    // Equal identities share one string instance, so native group lookups for
    // actors of the same skinned model compare references, not long paths.
    readonly Dictionary<string,string> mainGroupIdentities=new(StringComparer.Ordinal);
    string InternIdentity(string identity){lock(mainGroupIdentities){if(!mainGroupIdentities.TryGetValue(identity,out var shared))mainGroupIdentities.Add(identity,shared=identity);return shared;}}

    // Candidate decisions that do not depend on traversal order: ancestor
    // visibility and the root main-group key. They are computed in parallel
    // before the ordered traversal, which declares owners and emits serially.
    // An actor appears once per candidate list and only its own processing
    // changes its key inputs, so the planned key equals the in-loop result.
    struct CandidatePlan {public bool Visible;public object Key;}
    CandidatePlan[] candidatePlans=Array.Empty<CandidatePlan>();
    public bool ParallelTraversalEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_PARALLEL_TRAVERSAL") switch{"1"=>true,"0"=>false,_=>System.Environment.ProcessorCount>=4};
    void PlanCandidates(List<ActorData> list){
        if(candidatePlans.Length<list.Count)candidatePlans=new CandidatePlan[list.Count];
        // Model classification is cached lazily on first use; settle it here.
        foreach(var data in list)if(data.Model!=null&&!data.Model.NewPipelineEligible.HasValue)models.IsStockNewModel(data.Model);
        var plans=candidatePlans;const int chunk=128;
        System.Threading.Tasks.Parallel.For(0,(list.Count+chunk-1)/chunk,c=>{
            for(int i=c*chunk,end=Math.Min(list.Count,i+chunk);i<end;i++){var data=list[i];bool visible=data.Model!=null&&IsVisible(data.Actor);
                plans[i]=new CandidatePlan{Visible=visible,Key=visible&&(data.Flags&RenderFlags.SkipInMainView)==0?MainGroupKey(data,true):null};}
        });
    }
    static string MaterialIdentity(ActorData actor){
        if(actor.Materials.Count==0)return string.Empty;
        var identity=new StringBuilder();
        foreach(var item in actor.Materials.OrderBy(x=>x.Key,StringComparer.Ordinal)){
            var material=item.Value;var t=material.Textures;bool hidden=(material.Flags.Add&RenderFlags.SkipInMainView)!=0||(material.Flags.Remove&RenderFlags.Visible)!=0;
            if(!hidden&&t.ColorMetalFileName==null&&t.NormalGlossFileName==null&&t.ExtensionsFileName==null&&t.AlphamaskFileName==null)continue;
            foreach(string value in new[]{item.Key,t.ColorMetalFileName,t.NormalGlossFileName,t.ExtensionsFileName,t.AlphamaskFileName}){if(value==null)identity.Append("-:");else identity.Append(value.Length).Append(':').Append(value);}
            identity.Append(hidden?'1':'0');
        }
        return identity.ToString();
    }
}
