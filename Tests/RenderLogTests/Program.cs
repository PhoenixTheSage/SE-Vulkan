using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ClientPlugin.Diagnostics;
using VRage;
using VRage.FileSystem;
using VRageRender;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private static string Body(string line)
    {
        int arrow = line.IndexOf("->", StringComparison.Ordinal);
        return arrow < 0 ? line : line.Substring(arrow);
    }

    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "se-vulkan-render-log-" + Guid.NewGuid().ToString("N"));
        string content = Path.Combine(root, "Content");
        string user = Path.Combine(root, "User");
        try
        {
            Directory.CreateDirectory(content);
            Directory.CreateDirectory(user);
            InstallPlatform();
            MyFileSystem.Init(content, user);
            File.WriteAllText(Path.Combine(user, "VRageRender-Vulkan_old.log"), "stale");
            File.SetLastWriteTime(Path.Combine(user, "VRageRender-Vulkan_old.log"), DateTime.Now.AddDays(-5));
            File.WriteAllText(Path.Combine(user, "VRageRender-Vulkan_recent.log"), "keep");

            var session = RenderSessionLog.Open();
            Check(RenderSessionLog.Current == session, "open session is current");
            Check(session.FilePath != null && session.FilePath.StartsWith(user, StringComparison.OrdinalIgnoreCase), "log is in the profile directory");
            Check(Path.GetFileName(session.FilePath).StartsWith("VRageRender-Vulkan_", StringComparison.Ordinal) && session.FilePath.EndsWith(".log", StringComparison.Ordinal), "Keen file name");
            Check(!File.Exists(Path.Combine(user, "VRageRender-Vulkan_old.log")), "logs older than three days are removed");
            Check(File.Exists(Path.Combine(user, "VRageRender-Vulkan_recent.log")), "recent render logs are kept");

            var render = MyRenderSettings.Default;
            render.User.AntialiasingMode = MyAntialiasingMode.FXAA;
            render.User.GrassDrawDistance = 1000;
            render.User.GrassDensityFactor = 3;
            session.RenderSettings(render);
            var device = new MyRenderDeviceSettings { AdapterOrdinal = 0, BackBufferWidth = 1920, BackBufferHeight = 1080, WindowMode = MyWindowModeEnum.Window, RefreshRate = 60000 };
            session.Write("CreateDeviceInternal");
            session.DeviceSettings(device);
            session.DeviceReady(device, "Vulkan device: 'Test GPU', loader 1.4.0, device 1.4.0");
            session.Write("Content Models/Debug/Cone.mwm: inconsistent vertex streams");
            session.Fail("CreateDevice failed: Regular exception occurred", new InvalidOperationException("window missing"));
            var autodetect = device;
            autodetect.AdapterOrdinal = -1;
            session.DeviceSettings(autodetect);
            session.UnloadSession();
            session.Close();
            Check(RenderSessionLog.Current == null, "close releases the current session");

            string[] lines = File.ReadAllLines(session.FilePath);
            string text = string.Join("\n", lines);
            Check(lines.All(line => line.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} - Thread:\s+\d+ ->") || !line.Contains("Thread:")), "timestamped lines use Keen's prefix");
            foreach (string marker in new[]
            {
                "Log Started", "Timezone (local - UTC):", "App Version: Version unknown", "VRage renderer started",
                "MyRenderSettings1 = {", "AntialiasingMode = FXAA", "GrassDrawDistance = 1000", "GrassDensityFactor = 3",
                "CreateDeviceInternal", "MyRenderDeviceSettings = {", "Adapter id = 0", "PhysicalDevice id = 0", "Display id = 0",
                "Resolution = 1920 x 1080", "Window mode = Window", "ApplySettings", "Screen resolution: 1920x1080",
                "CreateScreenResources: 1920x1080", "CreateDevice: deviceCreated = True",
                "Vulkan device: 'Test GPU', loader 1.4.0, device 1.4.0",
                "Content Models/Debug/Cone.mwm: inconsistent vertex streams",
                "CreateDevice failed: Regular exception occurred", "window missing",
                "PhysicalDevice id = <autodetect>", "Display id = <autodetect>",
                "Unloading session data", "Log Closed"
            })
                Check(text.Contains(marker), marker);

            string antialias = lines.Select(Body).First(line => line.Contains("AntialiasingMode = FXAA"));
            Check(antialias.StartsWith("->     AntialiasingMode", StringComparison.Ordinal), "settings fields use Keen's three-space indent");
            string nested = lines.Select(Body).First(line => line.Contains("Screen resolution:"));
            Check(nested.StartsWith("->     Screen resolution:", StringComparison.Ordinal), "screen resolution is indented under ApplySettings");
            string adapter = lines.Select(Body).First(line => line.Contains("Adapter id = 0") && line.StartsWith("->        ", StringComparison.Ordinal));
            Check(adapter.Contains("Adapter id = 0"), "device settings inside ApplySettings use a second indent");
            Console.WriteLine(checks + " render-log checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static void InstallPlatform()
    {
        var system = DispatchProxy.Create<IVRageSystem, AnyProxy>();
        var render = DispatchProxy.Create<IVRageRender, AnyProxy>();
        var platform = DispatchProxy.Create<IVRagePlatform, AnyProxy>();
        ((AnyProxy)(object)platform).System = system;
        ((AnyProxy)(object)platform).Render = render;
        typeof(MyVRage).GetProperty("Platform", BindingFlags.Public | BindingFlags.Static).GetSetMethod(true).Invoke(null, new object[] { platform });
    }

    public class AnyProxy : DispatchProxy
    {
        public object System, Render;
        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name == "get_System") return System;
            if (targetMethod.Name == "get_Render") return Render;
            var parameters = targetMethod.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!parameters[i].ParameterType.IsByRef) continue;
                Type element = parameters[i].ParameterType.GetElementType();
                args[i] = element.IsValueType ? Activator.CreateInstance(element) : null;
            }
            if (targetMethod.ReturnType == typeof(void)) return null;
            if (targetMethod.ReturnType == typeof(ulong)) return 0UL;
            return targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }
    }
}
