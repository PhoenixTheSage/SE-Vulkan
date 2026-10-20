using System;
using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

// MyLightsRendering.PostprocessCulling consumes an already ordered spatial
// query. Root lights keep that order; grouped overflow uses rounded actor
// distances and distance to the group's bounds. Keep the installed selection
// loop, including its strict group-fit comparison, separate from discovery.
internal static class StockLightSelection
{
    internal readonly struct Group
    {
        public readonly int Start,Count;
        public readonly float Distance;
        public Group(int start,int count,float distance){Start=start;Count=count;Distance=distance;}
    }
    readonly struct Row<T>
    {
        public readonly T Item;
        public readonly float Distance;
        public Row(T item,Func<T,float> distance){Item=item;Distance=MathHelper.RoundOn2(distance(item));}
    }
    static List<T> Items<T>(List<Row<T>> rows)=>rows.ConvertAll(row=>row.Item);
    public static List<T> Spots<T>(List<T> candidates,Func<T,float> distance)
    {
        if(candidates.Count<=32)return new List<T>(candidates);
        var rows=candidates.ConvertAll(item=>new Row<T>(item,distance));
        rows.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
        rows.RemoveRange(32,rows.Count-32);return Items(rows);
    }
    public static List<T> Points<T>(List<T> candidates,List<Group> groups,int capacity,Func<T,float> distance)
    {
        if(capacity<=0)throw new ArgumentOutOfRangeException(nameof(capacity));
        if(candidates.Count<=capacity)return new List<T>(candidates);
        var rows=candidates.ConvertAll(item=>new Row<T>(item,distance));
        int roots=Math.Min(candidates.Count,Math.Min(capacity,groups.Count>0?groups[0].Start:candidates.Count));
        var selected=rows.GetRange(0,roots);
        if(roots<capacity){
            var orderedGroups=new List<Group>(groups);orderedGroups.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
            float worst=float.MaxValue;
            foreach(var group in orderedGroups){
                int start=group.Start,count=Math.Min(candidates.Count-start,group.Count);
                if(start<0||count<=0||start+count>candidates.Count)continue;
                if(selected.Count+count<capacity){selected.AddRange(rows.GetRange(start,count));continue;}
                var range=rows.GetRange(start,count);bool changed=false;
                foreach(var row in range)if(worst>row.Distance){changed=true;selected.Add(row);}
                if(changed){
                    range.Clear();range.AddRange(selected);selected.Clear();count=range.Count;
                    range.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
                    int remove=Math.Min(count,selected.Count+count-capacity);start=count-remove;
                    if(start>0&&remove>0&&start+remove<=range.Count)range.RemoveRange(start,remove);
                    if(range.Count>0)selected.AddRange(range);
                    if(selected.Count!=0)worst=selected[selected.Count-1].Distance;
                }
            }
        }
        return Items(selected);
    }
}
