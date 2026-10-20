using System;
using System.Collections;
using System.IO;
using System.Xml.Linq;
using HarmonyLib;
using VRageMath;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    static object lightingScene;
    static object FlareValue(object value,string name)
    {
        var type=value as Type??value.GetType();object target=value is Type?null:value;
        var field=AccessTools.Field(type,name);if(field!=null)return field.GetValue(target);
        var property=AccessTools.Property(type,name);if(property!=null)return property.GetValue(target,null);
        throw new MissingMemberException(type.FullName,name);
    }
    static void SaveLightingFlares()
    {
        uint solarId=(uint)FlareValue(typeof(ClientPlugin.Rendering.GuestBridge),"SunLightRenderId");
        var root=new XElement("LightingFlares",new XAttribute("Schema",1),new XAttribute("Backend",lightingOwner.expectedBackend),new XAttribute("SolarRenderId",solarId),new XAttribute("NativeSunGlare",ClientPlugin.Rendering.GuestBridge.NativeSunGlare),
            new XAttribute("Scope","CPU flare descriptors and occlusion factors for the captured render frame; HDR also includes transparent draws."));
        void Add(object owner,object desc,object query,object occlusion)
        {
            var world=(MatrixD)FlareValue(owner,"LastWorldMatrix");
            var row=new XElement("Flare",new XAttribute("Owner",FlareValue(owner,"ID")),new XAttribute("X",world.Translation.X),new XAttribute("Y",world.Translation.Y),new XAttribute("Z",world.Translation.Z),
                new XAttribute("Occlusion",occlusion??"Pending"));
            foreach(string name in new[]{"Enabled","Type","Intensity","MaxDistance","QuerySize","QueryShift","QueryFreqMinMs","QueryFreqRndMs"})row.Add(new XAttribute(name,FlareValue(desc,name)));
            if(query is ClientPlugin.Rendering.Scene.StockFlareOcclusionState state){row.Add(new XAttribute("LastOcclusionFactor",state.Last),new XAttribute("NextOcclusionFactor",state.Next),new XAttribute("LastQueryTime",state.LastQueryTime),new XAttribute("QueryTime",state.QueryTime),new XAttribute("PendingId",state.PendingId));}
            else if(query!=null)foreach(string name in new[]{"LastOcclusionFactor","NextOcclusionFactor","LastVolumeSquared","Visible","LastQueryTime","QueryTime"})row.Add(new XAttribute(name,FlareValue(query,name)));
            foreach(object glare in (IEnumerable)FlareValue(desc,"Glares"))row.Add(new XElement("SubGlare",new XAttribute("Material",FlareValue(glare,"Material")),new XAttribute("Type",FlareValue(glare,"Type")),new XAttribute("Size",FlareValue(glare,"Size")),new XAttribute("ScreenCenterDistance",FlareValue(glare,"ScreenCenterDistance"))));
            root.Add(row);
        }
        if(lightingOwner.expectedBackend.ToString()=="Vulkan"){
            if(lightingScene==null)throw new InvalidOperationException("The captured Vulkan scene was not observed.");
            var occlusions=(IDictionary)FlareValue(lightingScene,"flareOcclusion");
            var states=(IDictionary)FlareValue(lightingScene,"flareStates");
            foreach(object light in (IEnumerable)FlareValue(lightingScene,"lights")){
                object desc=FlareValue(FlareValue(light,"m_data"),"Glare");if(!(bool)FlareValue(desc,"Enabled"))continue;
                object id=FlareValue(light,"FlareId");Add(FlareValue(light,"Owner"),desc,states.Contains(id)?states[id]:null,occlusions.Contains(id)?occlusions[id]:null);
            }
        }else{
            var pool=FlareValue(AccessTools.TypeByName("VRageRender.MyFlareRenderer"),"m_flares");
            foreach(object data in (IEnumerable)FlareValue(pool,"Data")){
                object query=FlareValue(data,"Query"),owner=FlareValue(data,"Owner");if(query==null||owner==null)continue;
                Add(owner,FlareValue(data,"Desc"),query,FlareValue(query,"OcclusionFactor"));
            }
        }
        if(lightingOwner.expectedBackend.ToString()=="Vulkan" && (solarId==uint.MaxValue || !System.Linq.Enumerable.Any(root.Elements("Flare"),row=>(uint)row.Attribute("Owner")==solarId)))throw new InvalidOperationException("The actual game-owned solar glare identity was not captured.");
        string folder=Path.Combine(lightingOwner.reports,"lighting-buffers");Directory.CreateDirectory(folder);root.Save(Path.Combine(folder,"flares.xml"));
    }
}
