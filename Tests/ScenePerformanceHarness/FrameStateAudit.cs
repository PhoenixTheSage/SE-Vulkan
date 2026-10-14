using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using HarmonyLib;
using ClientPlugin.Rendering.Shaders;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using VRageRender;

namespace ScenePerformanceHarness;
public sealed partial class Plugin
{
    static object frameStateAudit,frameProbeAudit;static byte[] frameStateBytes;
    void InstallFrameStateAudit()
    {
        frameStateAudit=null;frameProbeAudit=null;frameStateBytes=null;
        if(!selectionAudit)return;
        if(expected=="Dx11")harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyCommon"),"UpdateFrameConstants"),postfix:new HarmonyMethod(typeof(Plugin),nameof(CaptureStockFrameState)));
        else harmony.Patch(AccessTools.Method(typeof(WorldConstants),nameof(WorldConstants.Frame)),postfix:new HarmonyMethod(typeof(Plugin),nameof(CaptureVulkanFrameState)));
    }
    static object FrameMember(object value,string name)=>value==null?null:AccessTools.Field(value.GetType(),name)?.GetValue(value)??AccessTools.Property(value.GetType(),name)?.GetValue(value);
    static object AuditValue(object value,int depth=0)
    {
        if(value==null)return null;var type=value.GetType();if(type.IsPrimitive||type.IsEnum||value is string||value is decimal)return value;
        if(depth>5)return type.FullName;
        if(value is Array array)return array.Cast<object>().Select(v=>AuditValue(v,depth+1)).ToArray();
        return type.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).ToDictionary(f=>f.Name,f=>AuditValue(f.GetValue(value),depth+1));
    }
    static void CaptureStockFrameState()
    {
        if(!captureWorkloadFrame||frameStateAudit!=null)return;
        var common=AccessTools.TypeByName("VRageRender.MyCommon");var frame=AccessTools.Field(common,"FrameConstantsData").GetValue(null);
        int size=Marshal.SizeOf(frame);var memory=Marshal.AllocHGlobal(size);
        try{Marshal.StructureToPtr(frame,memory,false);frameStateBytes=new byte[size];Marshal.Copy(memory,frameStateBytes,0,size);}finally{Marshal.FreeHGlobal(memory);}
        var render=AccessTools.TypeByName("VRageRender.MyRender11");var environment=AccessTools.Field(render,"Environment").GetValue(null);var data=FrameMember(environment,"Data");
        var probe=AccessTools.TypeByName("VRage.Render11.LightingStage.EnvironmentProbe.MyEnvironmentProbe");
        frameStateAudit=new{Backend="Dx11",ThreadId=System.Environment.CurrentManagedThreadId,ByteSize=size,Frame=AuditValue(frame),Environment=AuditValue(data),ProbeAmbient=AccessTools.Property(probe,"LastAmbient").GetValue(null),Scope="CPU constants after stock UpdateFrameConstants; matrices retain stock storage convention."};
    }
    static void CaptureVulkanFrameState(ShaderMetadata layout,int width,int height,MyEnvironmentLightData light,byte[] __result)
    {
        if(!captureWorkloadFrame||frameStateAudit!=null||width!=1920||height!=1080)return;
        frameStateBytes=(byte[])__result.Clone();var fields=new Dictionary<string,object>();var offsets=new Dictionary<string,int>();var reader=new ReflectedConstantBuffer(layout,"Frame");
        foreach(var item in new[]{("directionalLightColor",3),("directionalLightSpecularColor",3),("directionalLightVec",3),("directionalLightDiffuseFactor",1),("directionalLightGlossFactor",1),("directionalLightSpecularFactor",1),("ambientDiffuseFactor",1),("ambientSpecularFactor",1),("forwardPassAmbient",1),("envSkyboxBrightness",1),("envAtmosphereBrightness",1),("skyboxBrightness",1),("AmbientFarDistance",1),("SkipIBLevels",1)}){
            int offset=reader.OffsetOf("frame_.Light."+item.Item1);offsets[item.Item1]=offset;fields[item.Item1]=Enumerable.Range(0,item.Item2).Select(i=>BitConverter.ToSingle(__result,offset+i*4)).ToArray();}
        foreach(string group in new[]{"Environment.world_offset","Screen.offset","DistanceFade","TextureDebugMultipliers.AlbedoMultiplier","TextureDebugMultipliers.MetalnessMultiplier","TextureDebugMultipliers.GlossMultiplier","TextureDebugMultipliers.AoMultiplier","TextureDebugMultipliers.EmissiveMultiplier","TextureDebugMultipliers.ColorMaskMultiplier"}){
            try{int offset=reader.OffsetOf("frame_."+group);offsets[group]=offset;fields[group]=BitConverter.ToSingle(__result,offset);}catch(Exception e){fields[group]=new{Unavailable=e.Message};}}
        frameStateAudit=new{Backend="Vulkan",ThreadId=System.Environment.CurrentManagedThreadId,ByteSize=__result.Length,Light=AuditValue(light),Encoded=fields,Offsets=offsets,Scope="Actual main-view returned frame bytes, resolved through shader member offsets; probe frames excluded."};
    }
    void WriteFrameStateAudit()
    {
        if(frameStateAudit==null)return;
        File.WriteAllText(Path.Combine(reports,"frame-state.json"),JsonSerializer.Serialize(frameStateAudit,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        File.WriteAllBytes(Path.Combine(reports,"frame-state.bin"),frameStateBytes);
        if(frameProbeAudit!=null)File.WriteAllText(Path.Combine(reports,"probe-state.json"),JsonSerializer.Serialize(frameProbeAudit,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
    static void CaptureProbeState(object renderer)
    {
        if(!selectionAudit||!captureWorkloadFrame||renderer is not MyVulkanRender)return;
        var target=(NativeBackend)BackendField.GetValue(renderer);var state=(VulkanProbes)AccessTools.Field(renderer.GetType(),"probes").GetValue(renderer);
        frameProbeAudit=new{Settings=state.Settings,state.Resolution,state.LastAmbient,Statistics=target.GetProbeStats(),Schedule=target.GetProbeScheduleStats(),Scope="Actual native probe state after the selected paused render frame; no FPS claim."};
    }
}
