using System;
using System.Collections.Generic;
using VRageRender.Import;
namespace ClientPlugin.Rendering.Content;

public readonly struct DepthGeometryPart
{
    public readonly uint First,Count;public readonly MyMeshDrawTechnique Technique;public readonly bool Visible;
    public DepthGeometryPart(uint first,uint count,MyMeshDrawTechnique technique,bool visible=true){First=first;Count=count;Technique=technique;Visible=visible;}
}
public struct DepthGeometrySegment {public uint First,Count;public int MaterialPart;public bool Alpha;}
public static class DepthGeometrySegments
{
    public static bool DrawsDepth(MyMeshDrawTechnique technique)=>!technique.IsTransparent() && technique!=MyMeshDrawTechnique.DECAL && technique!=MyMeshDrawTechnique.DECAL_NOPREMULT && technique!=MyMeshDrawTechnique.DECAL_CUTOUT;
    public static List<DepthGeometrySegment> Build(IReadOnlyList<DepthGeometryPart> parts)
    {
        var result=new List<DepthGeometrySegment>();
        for(int i=0;i<parts.Count;i++){
            var part=parts[i];var technique=part.Technique;
            if(!part.Visible || part.Count==0 || !DrawsDepth(technique))continue;
            if(part.First%3!=0 || part.Count%3!=0)throw new ArgumentException("Depth segments must contain complete triangles.");
            bool alpha=technique==MyMeshDrawTechnique.ALPHA_MASKED || technique==MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED;
            if(!alpha && result.Count>0){var previous=result[result.Count-1];if(!previous.Alpha && (ulong)previous.First+previous.Count==part.First){previous.Count=checked(previous.Count+part.Count);result[result.Count-1]=previous;continue;}}
            result.Add(new DepthGeometrySegment{First=part.First,Count=part.Count,MaterialPart=i,Alpha=alpha});
        }
        return result;
    }
}
