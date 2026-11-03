using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Threading;
using ClientPlugin.Bootstrap;
using Sandbox;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Settings;

internal static class AdvancedRenderingPage
{
    public const string Warning = "Don't change unless you know what you're doing";
    public const string RestartMessage = "Changes require a restart to take effect";
    static object terminal;
    static Type registry;
    static DateTime nextAttempt;
    static int dirty;
    static long revision;
    public static long Revision => Interlocked.Read(ref revision);
    static bool subscribed;
    public static bool RestartRequired => AdvancedRenderingOptions.RestartRequired(Config.Current.AdvancedRendering);
    public static bool Value(AdvancedRenderingOption option) => AdvancedRenderingOptions.IsOverridden(option.Id)
        ? AdvancedRenderingOptions.EffectiveEnabled(option.Id) : Config.Current.AdvancedRendering.Get(option.Id);
    public static string Description(AdvancedRenderingOption option) => option.Description +
        (AdvancedRenderingOptions.IsOverridden(option.Id) ? " An external startup override controls this option." : " Applies after restarting the game.");
    public static void Show() => MyGuiSandbox.AddScreen(new AdvancedRenderingScreen());
    public static void Update()
    {
        if (!subscribed)
        {
            Config.Current.PropertyChanged += Changed;
            subscribed = true;
        }
        if (terminal == null && DateTime.UtcNow >= nextAttempt) Install();
        if (terminal != null && Interlocked.Exchange(ref dirty, 0) != 0)
            try { Call(terminal, "Refresh"); } catch (Exception e) { BackendRuntime.RecordFailure("Advanced terminal refresh: " + e.GetBaseException().Message); }
    }
    static void Changed(object sender, System.ComponentModel.PropertyChangedEventArgs args)
    { Interlocked.Increment(ref revision); Interlocked.Exchange(ref dirty, 1); }
    public static void Dispose()
    {
        if (subscribed) Config.Current.PropertyChanged -= Changed;
        subscribed = false;
        try { registry?.GetMethod("UnregisterPage")?.Invoke(null, new object[] { "Vulkan/Advanced" }); } catch { }
        terminal = null;
    }
    static void Install()
    {
        nextAttempt = DateTime.UtcNow.AddSeconds(2);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if ((registry = assembly.GetType("ClientPlugin.RichHud.TerminalConfigRegistry", false)) != null) break;
        if (registry == null) return;
        object candidate = null;
        try
        {
            candidate = registry.GetMethod("RequestFolderPage", new[] { typeof(string), typeof(string) })?.Invoke(null, new object[] { "Vulkan", "Advanced" });
            if (candidate == null) return;
            Call(candidate, "Category", "Advanced");
            Call(candidate, "Columns", 1);
            Call(candidate, "Label", Warning);
            // Shared Anomaly extension keeps styling and conditional visibility in the host.
            if (!Call(candidate, "StatusLine", RestartMessage, Color.Yellow, (Func<bool>)(() => RestartRequired)))
                Call(candidate, "Label", "", (Func<string>)(() => RestartRequired ? RestartMessage : ""));
            foreach (var group in AdvancedRenderingOptions.Definitions.GroupBy(option => option.Group))
            {
                Call(candidate, "Category", group.Key);
                foreach (var option in group)
                {
                    var captured = option;
                    Call(candidate, "Checkbox", option.Label, (Func<bool>)(() => Value(captured)),
                        (Action<bool>)(value => Config.Current.SetAdvanced(captured.Id, value)), Description(option),
                        (Func<bool>)(() => !AdvancedRenderingOptions.IsOverridden(captured.Id)));
                }
            }
            terminal = candidate;
        }
        catch (Exception e)
        {
            registry.GetMethod("UnregisterPage")?.Invoke(null, new object[] { "Vulkan/Advanced" });
            BackendRuntime.RecordFailure("Advanced terminal page: " + e.GetBaseException().Message);
        }
    }
    static bool Call(object target, string name, params object[] args)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var parameters = method.GetParameters();
            if (method.Name != name || method.IsGenericMethodDefinition || parameters.Length != args.Length) continue;
            if (parameters.Where((p, i) => args[i] != null && !p.ParameterType.IsInstanceOfType(args[i])).Any()) continue;
            method.Invoke(target, args); return true;
        }
        return false;
    }
}

internal sealed class AdvancedRenderingScreen : MyGuiScreenBase
{
    MyGuiControlLabel restart;
    readonly Dictionary<string, MyGuiControlCheckbox> boxes = new();
    long shownRevision = -1;
    public AdvancedRenderingScreen() : base(new Vector2(.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR,
        new Vector2(.82f, .88f), false, null, MySandboxGame.Config.UIBkOpacity, MySandboxGame.Config.UIOpacity)
    {
        EnabledBackgroundFade = true; m_closeOnEsc = true; m_drawEvenWithoutFocus = true;
        CanHideOthers = true; CanBeHidden = true; CloseButtonEnabled = true;
    }
    public override string GetFriendlyName() => "VulkanAdvanced";
    public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor); boxes.Clear(); AddCaption("Vulkan Advanced");
        Controls.Add(Text(-.37f, -.342f, AdvancedRenderingPage.Warning, .76f, Color.White));
        restart = Text(-.37f, -.303f, AdvancedRenderingPage.RestartMessage, .7f, Color.Yellow);
        restart.Visible = AdvancedRenderingPage.RestartRequired; Controls.Add(restart);
        var groups = AdvancedRenderingOptions.Definitions.GroupBy(option => option.Group).ToArray();
        float height = AdvancedRenderingOptions.Definitions.Length * .041f + groups.Length * .045f + .02f;
        var parent = new MyGuiControlParent { Size = new Vector2(.74f, height) };
        var panel = new MyGuiControlScrollablePanel(parent)
        {
            Position = new Vector2(0, .035f), Size = new Vector2(.77f, .59f),
            ScrollbarVEnabled = true, CanFocusChildren = true, BackgroundTexture = null,
            ScrolledAreaPadding = new MyGuiBorderThickness(.008f),
        };
        Controls.Add(panel);
        float y = -.5f * height + .012f;
        foreach (var group in groups)
        {
            parent.Controls.Add(Text(-.345f, y, group.Key, .75f, new Color(150, 210, 240))); y += .045f;
            foreach (var option in group)
            {
                var label = Text(-.33f, y, option.Label, .68f, Color.White);
                label.SetToolTip(AdvancedRenderingPage.Description(option)); parent.Controls.Add(label);
                var captured = option;
                var box = new MyGuiControlCheckbox(new Vector2(.31f, y + .013f), toolTip: AdvancedRenderingPage.Description(option))
                {
                    IsChecked = AdvancedRenderingPage.Value(option), Enabled = !AdvancedRenderingOptions.IsOverridden(option.Id),
                };
                box.IsCheckedChanged = control => { Config.Current.SetAdvanced(captured.Id, control.IsChecked); restart.Visible = AdvancedRenderingPage.RestartRequired; };
                parent.Controls.Add(box); y += .041f;
                boxes[option.Id] = box;
            }
        }
        Controls.Add(new MyGuiControlButton(new Vector2(0, .397f), text: new StringBuilder("Restore optimized defaults"),
            onButtonClick: _ => { foreach (var option in AdvancedRenderingOptions.Definitions)
                if (!AdvancedRenderingOptions.IsOverridden(option.Id)) Config.Current.SetAdvanced(option.Id, true); }));
    }
    public override bool Update(bool hasFocus)
    {
        if (restart != null) restart.Visible = AdvancedRenderingPage.RestartRequired;
        if (shownRevision != AdvancedRenderingPage.Revision)
        {
            shownRevision = AdvancedRenderingPage.Revision;
            foreach (var option in AdvancedRenderingOptions.Definitions)
                if (boxes.TryGetValue(option.Id, out var box) && box.IsChecked != AdvancedRenderingPage.Value(option))
                    box.IsChecked = AdvancedRenderingPage.Value(option);
        }
        return base.Update(hasFocus);
    }
    public override void OnRemoved() { ConfigStorage.ScheduleSave(Config.Current); base.OnRemoved(); }
    static MyGuiControlLabel Text(float x, float y, string text, float scale, Color color) => new MyGuiControlLabel(
        new Vector2(x, y), text: text, colorMask: color.ToVector4(), textScale: scale, font: "White",
        originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP);
}
