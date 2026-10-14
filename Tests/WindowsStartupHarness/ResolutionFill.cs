using System;
using System.IO;
using System.Linq;
using Sandbox;
using Sandbox.Engine.Platform.VideoMode;
using Sandbox.Game.Gui;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRageMath;
using VRageRender;

namespace WindowsStartupHarness;

// Drive the real Display controls and observe the game-side coordinate space,
// which a successful swapchain resize alone does not validate.
internal sealed class ResolutionFill
{
    private readonly string directory;
    private readonly long[] modes = { 0, 0, 1, 2, 0, 3 };
    private readonly string[] names = { "window-1920", "window-2560", "borderless-2560", "fullscreen-2560", "window-reset-2560", "exclusive-2560" };
    private int sample, phase;
    private double next = 20;
    private bool failed;
    private MyGuiScreenOptionsDisplay display;
    private MyGuiScreenBase options;
    internal bool Complete { get; private set; }
    internal ResolutionFill(string directory) => this.directory = directory;
    internal void Update(double now)
    {
        if (Complete || now < next) return;
        next = now + 3;
        switch (phase++)
        {
            case 0:
                display = new MyGuiScreenOptionsDisplay(); MyGuiSandbox.AddScreen(display);
                var mode = display.Controls.OfType<MyGuiControlCombobox>().Single(c => c.Name == "WindowMode");
                if (mode.TryGetItemByKey(modes[sample]) == null) throw new InvalidOperationException("Required window mode unavailable: " + names[sample]);
                mode.SelectItemByKey(modes[sample]);
                var resolution = display.Controls.OfType<MyGuiControlCombobox>().Single(c => c.Name == "Resolution");
                long key = ((long)(sample == 0 ? 1920 : 2560) << 32) | 1080;
                if (resolution.TryGetItemByKey(key) == null) throw new InvalidOperationException("Required resolution unavailable: " + names[sample]);
                resolution.SelectItemByKey(key); display.OnOkClick(null);
                break;
            case 1:
                var confirmation = MyScreenManager.GetFirstScreenOfType<MyGuiScreenMessageBox>();
                if (confirmation != null) { display.OnMessageBoxCallback(MyGuiScreenMessageBox.ResultEnum.YES); confirmation.CloseScreenNow(); }
                display.CloseScreenNow();
                options = (MyGuiScreenBase)Activator.CreateInstance(Type.GetType("SpaceEngineers.Game.GUI.MyGuiScreenOptionsSpace, SpaceEngineers.Game", true)); MyGuiSandbox.AddScreen(options);
                break;
            case 2:
                MyRenderProxy.TakeScreenshot(Vector2.One, Path.Combine(directory, "fill-" + names[sample] + ".png"), debug: false, ignoreSprites: false, showNotification: false);
                break;
            case 3:
                var selected = MyVideoSettingsManager.CurrentDeviceSettings;
                var expected = new Vector2I(sample == 0 ? 1920 : 2560, 1080);
                var gui = MyGuiManager.GetFullscreenRectangle();
                bool valid = selected.BackBufferWidth == expected.X && selected.BackBufferHeight == expected.Y &&
                    MySandboxGame.ScreenSize == expected && gui.Width == expected.X && gui.Height == expected.Y;
                failed |= !valid;
                File.AppendAllText(Path.Combine(directory, "resolution-fill.txt"), (valid ? "PASS " : "FAIL ") + names[sample] +
                    "; selected=" + selected.BackBufferWidth + "x" + selected.BackBufferHeight + "; game=" + MySandboxGame.ScreenSize + "; GUI=" + gui + "\n");
                options.CloseScreenNow();
                if (++sample == modes.Length)
                {
                    if (failed) throw new InvalidOperationException("Game/GUI coordinates did not follow Display resolution. See resolution-fill.txt and fill PNGs.");
                    File.WriteAllText(Path.Combine(directory, "resolution-fill-passed.txt"), "Real Display controls, game and GUI coordinates, and captures verified at 1920/2560 across all four window modes.");
                    Complete = true;
                }
                phase = 0;
                break;
        }
    }
}
