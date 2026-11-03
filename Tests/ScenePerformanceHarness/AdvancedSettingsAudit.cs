using System.Reflection;
using System.Collections;
using System.Text.Json;
using ClientPlugin;
using Sandbox;
using Sandbox.Graphics.GUI;
using VRageMath;
using VRageRender;
namespace ScenePerformanceHarness;
public sealed partial class Plugin
{
    int advancedStage;
    double advancedNext;
    MyGuiScreenBase advancedScreen;
    object advancedPage;
    bool advancedInitial;
    object advancedNotice;
    static readonly BindingFlags AdvancedFields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    void AuditAdvancedSettings(double seconds)
    {
        if(seconds<advancedNext)return;
        var assembly=typeof(ClientPlugin.Plugin).Assembly;
        var ui=assembly.GetType("ClientPlugin.Settings.AdvancedRenderingPage",true);
        var config=ClientPlugin.Config.Current;
        var set=typeof(ClientPlugin.Config).GetMethod("SetAdvanced",AdvancedFields);
        bool Pending()=>(bool)ui.GetProperty("RestartRequired",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        void Shot(string name)=>MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(reports,name+".png"),false,false,false);
        void Set(bool value)=>set.Invoke(config,new object[]{"GpuOpaqueRecordCache",value});
        if(advancedStage==0)
        {
            if(MyScreenManager.Screens.Any(s=>s is Sandbox.Game.Gui.MyGuiScreenIntroVideo))
                throw new Exception("Startup video screen survived world load; RHUD visibility audit would be obscured by the main-menu overlay.");
            File.WriteAllText(Path.Combine(reports,"advanced-startup-evidence.json"),JsonSerializer.Serialize(
                AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetType("ClientPlugin.Bootstrap.AdvancedRenderingOptions")!=null).Select(a=>new{
                    Assembly=a.FullName,a.Location,
                    Started=a.GetType("ClientPlugin.Bootstrap.AdvancedRenderingOptions").GetProperty("Started").GetValue(null),
                    Override=a.GetType("ClientPlugin.Bootstrap.AdvancedRenderingOptions").GetMethod("IsOverridden").Invoke(null,new object[]{"GpuOpaqueRecordCache"}),
                    Value=Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_RECORD_CACHE")}),new JsonSerializerOptions{WriteIndented=true}));
            if(Pending())throw new Exception("Advanced restart notice was already pending at startup");
            advancedScreen=(MyGuiScreenBase)Activator.CreateInstance(assembly.GetType("ClientPlugin.Settings.AdvancedRenderingScreen",true),true);
            MyGuiSandbox.AddScreen(advancedScreen);advancedNext=seconds+2;advancedStage++;return;
        }
        if(advancedStage==1){Shot("advanced-pulsar-clean");advancedNext=seconds+2;advancedStage++;return;}
        if(advancedStage==2)
        {
            var boxes=(IDictionary)advancedScreen.GetType().GetField("boxes",AdvancedFields).GetValue(advancedScreen);
            var box=(MyGuiControlCheckbox)boxes["GpuOpaqueRecordCache"];
            if(!box.Enabled)throw new Exception("Audit checkbox unexpectedly externally overridden");
            advancedInitial=box.IsChecked;box.IsChecked=!advancedInitial;
            if(!Pending())throw new Exception("Checkbox did not request a restart");
            advancedNext=seconds+2;advancedStage++;return;
        }
        if(advancedStage==3){Shot("advanced-pulsar-restart");advancedNext=seconds+2;advancedStage++;return;}
        if(advancedStage==4)
        {
            Set(advancedInitial);if(Pending())throw new Exception("Revert did not clear restart notice");
            advancedScreen.CloseScreen();MyGuiSandbox.RemoveScreen(advancedScreen);advancedNext=seconds+2;advancedStage++;return;
        }
        var host=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetType("ClientPlugin.RichHud.TerminalConfigRegistry")!=null);
        if(host==null)throw new Exception("Advanced RHUD audit needs Anomaly");
        var registry=host.GetType("ClientPlugin.RichHud.TerminalConfigRegistry");
        var terminal=host.GetType("RichHudFramework.UI.Client.RichHudTerminal",true);
        if(advancedStage==5)
        {
            if(!(bool)host.GetType("ClientPlugin.RichHud.RichHudSupport").GetProperty("Available").GetValue(null))return;
            advancedPage=registry.GetMethod("RequestFolderPage",new[]{typeof(string),typeof(string)}).Invoke(null,new object[]{"Vulkan","Advanced"});
            var mounted=advancedPage.GetType().GetField("mountedPage",AdvancedFields).GetValue(advancedPage);
            if(mounted==null)return;
            terminal.GetMethod("OpenMenu").Invoke(null,null);
            terminal.GetMethod("OpenToPage").Invoke(null,new[]{mounted});
            hudAuditOpen=true;hudAuditReports=reports;
            advancedNext=seconds+3;advancedStage++;return;
        }
        if(advancedStage==6){Shot("advanced-rhud-clean");advancedNext=seconds+2;advancedStage++;return;}
        if(advancedStage==7){Set(!advancedInitial);advancedNext=seconds+2;advancedStage++;return;}
        if(advancedStage==8){
            CaptureRichHud();
            var mounted=advancedPage.GetType().GetField("mountedPage",AdvancedFields).GetValue(advancedPage);
            foreach(var category in (IEnumerable)mounted.GetType().GetProperty("Categories").GetValue(mounted))
                foreach(var tile in (IEnumerable)category.GetType().GetProperty("Tiles").GetValue(category))
                    foreach(var control in (IEnumerable)tile.GetType().GetProperty("Controls").GetValue(tile))
                        if((string)control.GetType().GetProperty("Name").GetValue(control)=="Changes require a restart to take effect")advancedNotice=control;
            if(advancedNotice==null)throw new Exception("RHUD restart notice is missing from the actual mounted controls");
            var id=advancedNotice.GetType().GetProperty("ID").GetValue(advancedNotice);
            var element=id.GetType().GetProperty("Element").GetValue(id);
            var format=element.GetType().GetProperty("Format").GetValue(element);
            if(!Equals(format.GetType().GetProperty("Color").GetValue(format),Color.Yellow)||(bool)advancedNotice.GetType().GetProperty("Enabled").GetValue(advancedNotice)!=true)
                throw new Exception("Actual Master notice is not visible and yellow");
            File.WriteAllText(Path.Combine(reports,"advanced-rhud-state.json"),JsonSerializer.Serialize(new{
                Open=terminal.GetProperty("Open").GetValue(null),Pending=Pending(),
                BillboardsStatic=MyRenderProxy.DebugOverrides.BillboardsStatic,
                HostStatus=host.GetType("ClientPlugin.RichHud.RichHudSupport").GetProperty("StatusLine").GetValue(null),
                RegistryError=registry.GetProperty("LastError")?.GetValue(null)}));
            Shot("advanced-rhud-restart");advancedNext=seconds+2;advancedStage++;return;}
        Set(advancedInitial);if(Pending())throw new Exception("RHUD revert did not clear restart notice");
        advancedPage.GetType().GetMethod("Refresh").Invoke(advancedPage,null);
        if((bool)advancedNotice.GetType().GetProperty("Enabled").GetValue(advancedNotice))throw new Exception("Master notice remained visible after revert");
        File.WriteAllText(Path.Combine(reports,"advanced-settings-audit.json"),JsonSerializer.Serialize(new{PulsarCheckboxChanged=true,RestartAppeared=true,RevertCleared=true,RichHudMounted=true,PrivateProfile=true}));
        finished=true;MySandboxGame.ExitThreadSafe();
    }
}
