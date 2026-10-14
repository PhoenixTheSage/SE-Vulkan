using System;
using System.Text;
using Sandbox;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Diagnostics;

/// <summary>Two-column CPU snapshot; full details stay available on hover and in the log.</summary>
internal sealed class StatusScreen : MyGuiScreenBase
{
    const float Padding = 0.035f, Gutter = 0.035f, TopInset = 0.085f, BottomInset = 0.12f;
    const string Font = "Blue";
    public static void Show() => MyGuiSandbox.AddScreen(new StatusScreen());

    static Vector2 PanelSize()
    {
        var full = MyGuiManager.GetFullscreenRectangle();
        var safe = MyGuiManager.GetSafeGuiRectangle();
        return new Vector2(Math.Min(1.22f, safe.Width > 0 ? full.Width / (float)safe.Width * 0.94f : 0.94f), 0.92f);
    }
    StatusScreen() : base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, PanelSize(), false, null, MySandboxGame.Config.UIBkOpacity, MySandboxGame.Config.UIOpacity)
    {
        EnabledBackgroundFade = true;
        m_closeOnEsc = true;
        m_drawEvenWithoutFocus = true;
        CanHideOthers = true;
        CanBeHidden = true;
        CloseButtonEnabled = true;
    }
    public override string GetFriendlyName() => "SEVulkanStatus";
    public override void LoadContent() { base.LoadContent(); RecreateControls(true); }

    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor);
        AddCaption("Vulkan Renderer Status");
        var snapshot = VulkanDiagnostics.CaptureScreen();
        var size = Size ?? PanelSize();
        float left = -size.X * 0.5f + Padding;
        float top = -size.Y * 0.5f + TopInset;
        float width = (size.X - 2 * Padding - Gutter) * 0.5f;
        float right = left + width + Gutter;
        float height = size.Y - TopInset - BottomInset;
        int rows = Math.Max(snapshot.Summary.Count, snapshot.Systems.Count + 1);
        float step = height / Math.Max(40, rows);
        float scale = 0.60f;
        while (scale > 0.35f && MyGuiManager.MeasureString(Font, "Ag", scale).Y > step * 0.88f) scale -= 0.01f;

        for (int i = 0; i < snapshot.Summary.Count; i++)
        {
            var row = snapshot.Summary[i];
            AddText(left, top + i * step, width, scale, row.Text, row.Detail, row.Heading ? Vector4.One : StatusColor(row.State));
        }
        float stateX = right + width * 0.60f, frameX = right + width * 0.85f;
        AddText(right, top, width * 0.58f, scale, "VISUAL SYSTEM", null, Vector4.One);
        AddText(stateX, top, width * 0.24f, scale, "STATE", null, Vector4.One);
        AddText(frameX, top, width * 0.15f, scale, "FRAME", "Last observed frame; activity does not establish visual parity.", Vector4.One);
        for (int i = 0; i < snapshot.Systems.Count; i++)
        {
            var row = snapshot.Systems[i];
            float y = top + (i + 1) * step;
            string detail = row.Name + " — " + VulkanDiagnostics.StateName(row.State) + "\nObserved frame: " + row.Frame + "\n" + row.Detail;
            AddText(right, y, width * 0.58f, scale, row.Name, detail, StatusColor(null));
            AddText(stateX, y, width * 0.24f, scale, VulkanDiagnostics.StateName(row.State), detail, StatusColor(row.State));
            AddText(frameX, y, width * 0.15f, scale, row.Frame.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), detail, StatusColor(null));
        }
        AddText(left, size.Y * 0.5f - 0.105f, size.X - 2 * Padding, 0.52f,
            "Hover for details. Full per-message counts and failure history are available in the log report.", null, StatusColor(null));
        AddButton(-0.24f, "Refresh", _ => RecreateControls(false), size);
        AddButton(0, "Write report to log", _ => VulkanDiagnostics.WriteReportToLog(), size);
        AddButton(0.24f, "Close", _ => CloseScreen(), size);
    }
    void AddText(float x, float y, float width, float scale, string text, string detail, Vector4 color)
    {
        string singleLine = (text ?? "").Replace('\r', ' ').Replace('\n', ' ');
        var label = new MyGuiControlLabel(new Vector2(x, y), text: singleLine, colorMask: color,
            textScale: scale, font: Font, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
            isAutoEllipsisEnabled: true, maxWidth: width);
        if (!string.IsNullOrEmpty(detail)) label.SetToolTip(detail);
        Controls.Add(label);
    }
    void AddButton(float x, string text, Action<MyGuiControlButton> click, Vector2 size)
    {
        Controls.Add(new MyGuiControlButton(text: new StringBuilder(text), onButtonClick: click)
        {
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM,
            Position = new Vector2(x, size.Y * 0.5f - 0.018f),
        });
    }
    static Vector4 StatusColor(SubsystemState? state)
    {
        switch (state)
        {
            case SubsystemState.Active: case SubsystemState.Verified: return new Vector4(0.55f, 1, 0.72f, 1);
            case SubsystemState.Failed: return new Vector4(1, 0.55f, 0.5f, 1);
            case SubsystemState.NotImplemented: case SubsystemState.Unavailable: return new Vector4(1, 0.82f, 0.48f, 1);
            case SubsystemState.Disabled: return new Vector4(0.65f, 0.7f, 0.73f, 1);
            default: return new Vector4(0.78f, 0.91f, 0.96f, 1);
        }
    }
}
