using System;
using System.Globalization;
using System.Text;
using Sandbox;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Diagnostics;

internal sealed class ProfilerScreen : MyGuiScreenBase
{
    const float Padding = 0.04f, TopInset = 0.09f, BottomInset = 0.13f;
    const string Font = "Blue";
    float nextRefresh;
    FrameProfiler.View shown;
    public static void Show() => MyGuiSandbox.AddScreen(new ProfilerScreen());
    ProfilerScreen() : base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.92f, 0.86f), false, null, MySandboxGame.Config.UIBkOpacity, MySandboxGame.Config.UIOpacity)
    {
        EnabledBackgroundFade = true;
        m_closeOnEsc = true;
        m_drawEvenWithoutFocus = true;
        CanHideOthers = true;
        CanBeHidden = true;
        CloseButtonEnabled = true;
        shown = FrameProfiler.Capture(FrameProfiler.Live);
    }
    public override string GetFriendlyName() => "SEVulkanProfiler";
    public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor);
        AddCaption("Vulkan Performance");
        var size = Size ?? new Vector2(0.92f, 0.86f);
        float left = -size.X * 0.5f + Padding;
        float top = -size.Y * 0.5f + TopInset;
        float width = size.X - 2 * Padding;
        float height = size.Y - TopInset - BottomInset;
        var view = shown;
        string mode = !FrameProfiler.Record
            ? (view.Live ? "Live frame. 10 second recording is off, so no gameplay average is kept." : "10 second recording is off. Turn Record on to keep gameplay frames, or Live to sample this menu.")
            : view.Live
            ? (view.Paused ? "Live frame while paused. Scene work may be idle; the 10 second gameplay average is kept." : "Live frame. The 10 second gameplay average is still recording.")
            : (view.Frames > 0 ? "Average of " + view.Frames + " unpaused frames from the last 10 seconds of gameplay." : "No unpaused frames in the last 10 seconds. Enable Live to sample this menu.");
        int rows = FrameProfiler.SectionCount + 12;
        float step = height / rows;
        float scale = 0.58f;
        while (scale > 0.35f && MyGuiManager.MeasureString(Font, "Ag", scale).Y > step * 0.88f) scale -= 0.01f;
        AddText(left, top, width, scale, mode, new Vector4(0.9f, 0.95f, 1, 1), null);
        float y = top + step;
        AddText(left, y, width * 0.55f, scale, "SYSTEM", Vector4.One, null);
        AddText(left + width * 0.58f, y, width * 0.2f, scale, "TIME", Vector4.One, null);
        AddText(left + width * 0.78f, y, width * 0.2f, scale, "SHARE", Vector4.One, null);
        y += step;
        AddRow(left, y, width, scale, "Frame (CPU)", view.Total, view.Total, "Render-thread Draw through Present, including waits. 240 fps budget: 4.17 ms. World draws " + view.WorldDraws.ToString("N0", CultureInfo.InvariantCulture));
        y += step;
        AddRow(left, y, width, scale, "World (GPU)", view.GpuMs, -1, "Asynchronous completed GPU timestamps for world rendering; excludes uploads, final composition, HUD and present. Independent of CPU frame time. Samples: " + view.GpuSamples);
        for (int i = 0; i < FrameProfiler.SectionCount; i++)
        {
            y += step;
            AddRow(left, y, width, scale, FrameProfiler.Names[i], view.Milliseconds[i], view.Total, FrameProfiler.Details[i]);
            if (i == (int)FrameProfiler.Section.FrameSlot)
            {
                y += step; AddRow(left, y, width, scale, "Slot wait", view.Wait, view.Total, "Inside frame slot. Previous use of this frame slot had not finished.");
                y += step; AddRow(left, y, width, scale, "Swapchain acquire", view.Acquire, view.Total, "Inside frame slot.");
                y += step; AddRow(left, y, width, scale, "Command reset", view.Reset, view.Total, "Inside frame slot. Recycles the command pool from the previous use.");
            }
        }
        y += step;
        AddRow(left, y, width, scale, "Native prepare", view.Prepare, view.Total, "Copies, descriptor setup and draw sort inside native record.");
        y += step;
        AddRow(left, y, width, scale, "Spot shadows", view.Spot, view.Total, "Inside native record.");
        y += step;
        AddRow(left, y, width, scale, "Cascades", view.Shadow, view.Total, "Inside native record.");
        y += step;
        AddRow(left, y, width, scale, "Main view", view.Main, view.Total, "Inside native record.");
        y += step;
        AddRow(left, y, width, scale, "Probes and occlusion", view.Probe, view.Total, "Occlusion, decals, atmosphere setup and probes inside native record.");
        y += step;
        AddRow(left, y, width, scale, "Lighting and post", view.Tail, view.Total, "Deferred lighting, transparency and post inside native record.");
        var screenSize = size;
        AddButton(-0.34f, FrameProfiler.Record ? "Record: On" : "Record: Off", _ =>
        {
            FrameProfiler.Record = !FrameProfiler.Record;
            shown = FrameProfiler.Capture(FrameProfiler.Live);
            RecreateControls(false);
        }, screenSize);
        AddButton(-0.12f, FrameProfiler.Live ? "Live: On" : "Live: Off", _ =>
        {
            FrameProfiler.Live = !FrameProfiler.Live;
            shown = FrameProfiler.Capture(FrameProfiler.Live);
            RecreateControls(false);
        }, screenSize);
        AddButton(0.12f, "Refresh", _ => { shown = FrameProfiler.Capture(FrameProfiler.Live); RecreateControls(false); }, screenSize);
        AddButton(0.34f, "Close", _ => CloseScreen(), screenSize);
    }
    public override bool Update(bool hasFocus)
    {
        if (FrameProfiler.Live && MySandboxGame.TotalTimeInMilliseconds / 1000f >= nextRefresh)
        {
            nextRefresh = MySandboxGame.TotalTimeInMilliseconds / 1000f + 0.5f;
            shown = FrameProfiler.Capture(true);
            RecreateControls(false);
        }
        return base.Update(hasFocus);
    }
    void AddButton(float x, string text, Action<MyGuiControlButton> click, Vector2 size)
    {
        Controls.Add(new MyGuiControlButton(text: new StringBuilder(text), onButtonClick: click)
        {
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM,
            Position = new Vector2(x, size.Y * 0.5f - 0.02f),
        });
    }
    void AddRow(float x, float y, float width, float scale, string name, float milliseconds, float total, string detail)
    {
        float share = total > 0.01f ? milliseconds / total * 100f : 0;
        var color = share >= 25f ? new Vector4(1, 0.72f, 0.45f, 1) : new Vector4(0.78f, 0.91f, 0.96f, 1);
        AddText(x, y, width * 0.55f, scale, name, color, detail);
        AddText(x + width * 0.58f, y, width * 0.18f, scale, milliseconds >= 0 ? milliseconds.ToString("0.00", CultureInfo.InvariantCulture) + " ms" : "Unavailable", color, detail);
        AddText(x + width * 0.78f, y, width * 0.18f, scale, total >= 0 ? share.ToString("0", CultureInfo.InvariantCulture) + "%" : "—", color, detail);
    }
    void AddText(float x, float y, float width, float scale, string text, Vector4 color, string detail)
    {
        var label = new MyGuiControlLabel(new Vector2(x, y), text: text ?? "", colorMask: color, textScale: scale, font: Font,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, isAutoEllipsisEnabled: true, maxWidth: width);
        if (!string.IsNullOrEmpty(detail)) label.SetToolTip(detail);
        Controls.Add(label);
    }
}
