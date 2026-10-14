using System;
using System.Collections.Generic;
using ClientPlugin.Rendering.Content;
using VRageMath;
using VRageRender.Messages;
namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    private sealed class Highlight {public string Section;public Vector4 Color;public float Thickness,Pulse;public int Instance;}
    private readonly Dictionary<uint,List<Highlight>> highlights=new();
    private readonly HashSet<uint> overlappingHighlights=new();
    public float HighlightSepia;
    private bool ProcessHighlight(MyRenderMessageBase message)
    {
        if(message is MyRenderMessageUpdateModelHighlight h)
        {
            if(!actors.ContainsKey(h.ID))return true;
            void Update(uint id,string[] sections,int instance)
            {
                if(!(h.Thickness>0))
                {
                    if(sections==null)highlights.Remove(id);
                    else if(highlights.TryGetValue(id,out var old)){old.RemoveAll(x=>Array.IndexOf(sections,x.Section)>=0);if(old.Count==0)highlights.Remove(id);}
                    return;
                }
                if(!h.OutlineColor.HasValue || float.IsNaN(h.Thickness) || float.IsInfinity(h.Thickness) || float.IsNaN(h.PulseTimeInSeconds) || float.IsInfinity(h.PulseTimeInSeconds))throw new ArgumentException("Invalid highlight color/thickness/pulse.");
                if(!highlights.TryGetValue(id,out var list))highlights[id]=list=new();
                foreach(string section in sections??new string[]{null})
                {
                    var color=h.OutlineColor.Value.ToVector4();if(list.Exists(x=>x.Section==section && x.Instance==instance && x.Color==color && x.Thickness==h.Thickness && x.Pulse==h.PulseTimeInSeconds))continue;
                    list.Add(new Highlight{Section=section,Color=color,Thickness=h.Thickness,Pulse=h.PulseTimeInSeconds,Instance=instance});
                }
            }
            DirtyNativePass(actors[h.ID]);Update(h.ID,h.SectionNames,h.InstanceIndex);
            if(h.SubpartIndices!=null)foreach(uint id in h.SubpartIndices)if(actors.ContainsKey(id)){DirtyNativePass(actors[id]);Update(id,null,-1);}
            return true;
        }
        if(message is MyRenderMessageUpdateOverlappingModelsForHighlight o){if(actors.TryGetValue(o.OverlappingModelID,out var changed))DirtyNativePass(changed);if(o.Enable)overlappingHighlights.Add(o.OverlappingModelID);else overlappingHighlights.Remove(o.OverlappingModelID);return true;}
        return false;
    }
    private bool HasHighlight(uint id)=>highlights.ContainsKey(id)||overlappingHighlights.Contains(id);
    private void SubmitHighlight(uint draw,ActorData actor,VulkanModelPart part,uint sourceStart,uint count,uint instances)
    {
        uint id=actor.Actor.ID;SubmitShadowExclusion(draw,id);if(actor.SuppressViewHighlight)return;
        if(highlights.TryGetValue(id,out var list))foreach(var h in list)
        {
            int selected=h.Instance<0?0:h.Instance-(actor.InstanceBuffer!=uint.MaxValue?actor.InstanceStart:0);if(selected<0 || selected>=instances)continue;uint instance=(uint)selected;
            var color=h.Color*(1-MathHelper.Clamp(HighlightSepia,0,1));if(h.Pulse>0)color.W*=(float)Math.Pow(Math.Cos(2*Math.PI*Environment.FrameTime.Seconds/h.Pulse),2);
            void Add(uint start,uint length){if(length>0)backend.AddHighlightPacket(draw,start,length,instance,h.Instance<0?instances:1,color,h.Thickness);}
            if(string.IsNullOrEmpty(h.Section))Add(0,count);
            else if((actor.RenderModel??actor.Model).Sections.TryGetValue(h.Section,out var ranges))foreach(var range in ranges)
            {
                if(range.MaterialName!=part.MaterialName)continue;uint start=Math.Max(sourceStart,(uint)range.StartIndex),end=Math.Min(sourceStart+count,(uint)(range.StartIndex+range.IndexCount));if(end>start)Add(start-sourceStart,end-start);
            }
        }
        if(overlappingHighlights.Contains(id))backend.AddHighlightPacket(draw,0,count,0,instances,new Vector4(0,0,0,1),0,true);
    }
}
