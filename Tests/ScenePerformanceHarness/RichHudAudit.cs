using System.Reflection;
using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using Sandbox;
using VRageMath;
using VRageRender;
namespace ScenePerformanceHarness;
public sealed partial class Plugin
{
    static readonly bool HudAuditEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_HUD_AUDIT")=="1";
    static volatile bool hudAuditOpen;
    static int hudAuditFrames;
    static string hudAuditReports;
    static string hudNgxStatus;
    static object hudRenderer;
    double hudAuditOpened=-1;
    bool hudAuditCaptured;
    void AuditRichHud(double seconds)
    {
        if(hudAuditOpened<0)
        {
            var assembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetType("ClientPlugin.RichHud.RichHudSupport")!=null);
            if(assembly==null)throw new Exception("Anomaly shared HUD assembly not loaded");
            var support=assembly.GetType("ClientPlugin.RichHud.RichHudSupport");
            if(!(bool)support.GetProperty("Available").GetValue(null))return;
            var terminal=assembly.GetType("RichHudFramework.UI.Client.RichHudTerminal",true);
            terminal.GetMethod("OpenMenu",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
            hudAuditOpened=seconds;hudAuditReports=reports;hudAuditOpen=true;
            File.WriteAllText(Path.Combine(reports,"hud-open.txt"),DateTime.UtcNow.ToString("O")+"\n"+support.GetProperty("StatusLine").GetValue(null));
        }
        if(!hudAuditCaptured&&seconds-hudAuditOpened>3)
        {
            MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(reports,"hud-terminal.png"),false,false,false);
            hudAuditCaptured=true;
        }
        if(seconds-hudAuditOpened>6)
        {
            if(hudAuditFrames==0)throw new Exception("No rendered HUD audit frames");
            if(Environment.GetEnvironmentVariable("SE_VULKAN_HUD_REQUIRE_NGX")=="1" && hudNgxStatus!="NGX evaluating")throw new Exception("Requested NGX was not evaluating: "+hudNgxStatus);
            finished=true;MySandboxGame.ExitThreadSafe();
        }
    }
    static void CaptureRichHud()
    {
        if(hudRenderer==null)return;
        var scene=SceneField.GetValue(hudRenderer);
        var owner=scene.GetType().GetField("billboards",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(scene);
        object Read(string name)=>owner?.GetType().GetProperty(name)?.GetValue(owner);
        var sprites=hudRenderer.GetType().GetField("sprites",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(hudRenderer);
        File.WriteAllText(Path.Combine(hudAuditReports,"hud-ownership.json"),JsonSerializer.Serialize(new{HudCollected=Read("HudCollected"),HudEmitted=Read("HudEmitted"),HudDisabled=Read("HudDisabled"),Sprites=sprites?.GetType().GetProperty("HudSprites")?.GetValue(sprites),Screens=Sandbox.Graphics.GUI.MyScreenManager.Screens.Select(s=>new{Type=s.GetType().FullName,State=s.State.ToString(),s.EnabledBackgroundFade}).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
    }
    static void ObserveRichHud(object renderer)
    {
        if(!HudAuditEnabled||!hudAuditOpen||renderer is not MyVulkanRender render)return;
        hudRenderer=renderer;
        if(++hudAuditFrames%15!=1)return;
        var shared=render.SharedData;
        var incoming=shared.Billboards.Read.Billboards;
        var backend=(NativeBackend)BackendField.GetValue(renderer);
        var scene=SceneField.GetValue(renderer);
        var billboardOwner=scene.GetType().GetField("billboards",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(scene);
        object Owner(string name)=>billboardOwner?.GetType().GetProperty(name)?.GetValue(billboardOwner);
        var source=incoming.Where(b=>b.BlendType==MyBillboard.BlendTypeEnum.PostPP).Take(16).Select(b=>new {
            Type=b.GetType().FullName,Material=b.Material.String,Texture=MyTransparentMaterials.GetMaterial(b.Material).Texture,
            P0=b.Position0,P1=b.Position1,P2=b.Position2,Color=b.Color,b.ColorIntensity,b.CustomViewProjection,b.ParentID,
            UV0=(b as MyTriangleBillboard)?.UV0,UV1=(b as MyTriangleBillboard)?.UV1,UV2=(b as MyTriangleBillboard)?.UV2
        }).ToArray();
        var packet=backend.BillboardPackets.Where(b=>b.Bucket==4).Take(8).ToArray();
        var ngxType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("ClientPlugin.Dlss.NgxVulkan")).FirstOrDefault(t=>t!=null);
        hudNgxStatus=ngxType?.GetProperty("ShortStatus",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)?.GetValue(null) as string;
        var info=new {Ngx=hudNgxStatus,Native=backend.GetStats(),Utc=DateTime.UtcNow,Frame=hudAuditFrames,Incoming=incoming.Count,PostPP=incoming.Count(b=>b.BlendType==MyBillboard.BlendTypeEnum.PostPP),
            HudCollected=Owner("HudCollected"),HudEmitted=Owner("HudEmitted"),HudDisabled=Owner("HudDisabled"),HudPreview=Owner("HudPreview"),Transparency=backend.GetTransparencyStats(),
            Packets=backend.BillboardPackets.Count,PostPackets=backend.BillboardPackets.Count(b=>b.Bucket==4),Source=source,Packet=packet,
            Camera=((ClientPlugin.Rendering.Scene.VulkanScene)scene).Environment.CameraPosition,Postprocess=backend.GetPostprocessStats()};
        File.WriteAllText(Path.Combine(hudAuditReports,"hud-packets.json"),JsonSerializer.Serialize(info,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
    }
}
