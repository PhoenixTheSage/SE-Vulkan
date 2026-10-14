using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace D3D11DeviceObserver
{
    // Invoked by injected IL. Deliberately uses only BCL types.
    public static class Observer
    {
        private static readonly object Gate = new object();
        private static readonly List<string> Events = new List<string>();
        private static readonly List<string> Patches = new List<string>();
        private static readonly Dictionary<string, long> Attempts = new Dictionary<string, long>();
        private static readonly Dictionary<string, long> Successes = new Dictionary<string, long>();
        private static readonly Dictionary<string, long> Failures = new Dictionary<string, long>();
        private static string directory, lastError = "", initializedUtc = "", sharpDxPatchUtc = "", sharpDxLoadUtc = "", factoryUtc = "";
        private static bool subscribed, lazyDisabled, sharpDxStamped, beforeFactoryReady;
        private static int patchedCallSites;
        private static DateTime nextRefresh;
        public static long AttemptCount { get { lock (Gate) return Attempts.Values.Sum(); } }
        public static long SuccessCount { get { lock (Gate) return Successes.Values.Sum(); } }
        public static int PatchedCallSites { get { lock (Gate) return patchedCallSites; } }

        public static void Initialize(bool lazyCacheDisabled)
        {
            lock (Gate)
            {
                directory = Environment.GetEnvironmentVariable("SE_VULKAN_D3D11_REPORT_DIR");
                if (string.IsNullOrEmpty(directory)) return;
                directory = Path.GetFullPath(directory);
                initializedUtc = Utc(); lazyDisabled = lazyCacheDisabled;
                if (!subscribed)
                {
                    AppDomain.CurrentDomain.AssemblyLoad += (_, args) => ObserveAssembly(args.LoadedAssembly);
                    subscribed = true;
                }
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) ObserveAssembly(assembly);
                Event("initialize", "lazy-cache-disabled=" + lazyCacheDisabled);
                Write();
            }
        }
        public static void Patched(string assemblyName, string details, int callSites)
        {
            lock (Gate)
            {
                if (directory == null) return;
                Patches.Add(assemblyName + ": " + details);
                if (assemblyName == "SharpDX.Direct3D11") { patchedCallSites = callSites; sharpDxPatchUtc = Utc(); }
                Event("patch", assemblyName + ": " + details); Write();
            }
        }
        public static void Failed(string message)
        {
            lock (Gate) { if (directory == null) return; lastError = message; Event("failed", message); Write(); }
        }
        public static void Attempt(string entryPoint)
        {
            lock (Gate)
            {
                if (directory == null) return;
                Increment(Attempts, entryPoint);
                Event("attempt", entryPoint + "; thread=" + Thread.CurrentThread.ManagedThreadId + "; stack=" + Environment.StackTrace);
                Write();
            }
        }
        public static void Result(int hresult, string entryPoint)
        {
            lock (Gate)
            {
                if (directory == null) return;
                Increment(hresult >= 0 ? Successes : Failures, entryPoint);
                Event("result", entryPoint + "; HRESULT=0x" + hresult.ToString("X8", CultureInfo.InvariantCulture)); Write();
            }
        }
        public static void Milestone(string name)
        {
            lock (Gate)
            {
                if (directory == null) return;
                if (name == "before-render-initialization")
                {
                    factoryUtc = Utc();
                    // The assembly may be loaded later; its prepatch must already be registered.
                    beforeFactoryReady = lazyDisabled && patchedCallSites > 0 && string.IsNullOrEmpty(lastError);
                }
                Event("milestone", name); Write();
            }
        }
        public static void Refresh()
        {
            lock (Gate)
            {
                if (directory == null || DateTime.UtcNow < nextRefresh) return;
                nextRefresh = DateTime.UtcNow.AddSeconds(1); Write();
            }
        }
        private static void ObserveAssembly(Assembly assembly)
        {
            if (directory == null || assembly.GetName().Name != "SharpDX.Direct3D11") return;
            lock (Gate)
            {
                sharpDxLoadUtc = Utc();
                try { sharpDxStamped = assembly.GetType("SharpDX.Direct3D11.SE_Vulkan_D3D11_Observer_1", false) != null; }
                catch (Exception error) { lastError = "Loaded stamp inspection: " + error.Message; }
                if (!sharpDxStamped) lastError = "SharpDX.Direct3D11 loaded without observer stamp; zero calls are not evidence.";
                Event("assembly-load", "SharpDX.Direct3D11; stamp=" + sharpDxStamped); Write();
            }
        }
        private static void Increment(Dictionary<string, long> values, string name) { values.TryGetValue(name, out long value); values[name] = value + 1; }
        private static string Utc() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        private static void Event(string kind, string detail)
        {
            if (Events.Count == 512) Events.RemoveAt(0);
            Events.Add(Utc() + " | " + kind + " | " + detail);
        }
        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            foreach (char character in value ?? "")
            {
                switch (character)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '"': result.Append("\\\""); break;
                    case '\r': result.Append("\\r"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\t': result.Append("\\t"); break;
                    default: if (character < 32) result.Append("\\u" + ((int)character).ToString("x4")); else result.Append(character); break;
                }
            }
            return result.Append('"').ToString();
        }
        private static string Counters(Dictionary<string, long> values) => "{" + string.Join(",", values.OrderBy(p => p.Key).Select(p => Quote(p.Key) + ":" + p.Value)) + "}";
        private static void Write()
        {
            try
            {
                Directory.CreateDirectory(directory);
                string json = "{\n" +
                    "\"schema\":1,\n\"scope\":\"Managed SharpDX.Direct3D11 native import call sites only; native overlays and other libraries are outside observation\",\n" +
                    "\"initializedUtc\":" + Quote(initializedUtc) + ",\n\"sharpDxPatchUtc\":" + Quote(sharpDxPatchUtc) + ",\n\"sharpDxLoadUtc\":" + Quote(sharpDxLoadUtc) + ",\n\"renderInitializationUtc\":" + Quote(factoryUtc) + ",\n" +
                    "\"lazyCacheDisabled\":" + lazyDisabled.ToString().ToLowerInvariant() + ",\n\"loadedAssemblyStamped\":" + sharpDxStamped.ToString().ToLowerInvariant() + ",\n\"readyBeforeRenderInitialization\":" + beforeFactoryReady.ToString().ToLowerInvariant() + ",\n" +
                    "\"patchedCallSites\":" + patchedCallSites + ",\n\"attempts\":" + Counters(Attempts) + ",\n\"successes\":" + Counters(Successes) + ",\n\"failures\":" + Counters(Failures) + ",\n" +
                    "\"lastError\":" + Quote(lastError) + ",\n\"patches\":[" + string.Join(",", Patches.Select(Quote)) + "],\n\"events\":[" + string.Join(",", Events.Select(Quote)) + "]\n}";
                string path = Path.Combine(directory, "d3d11-observer.json"), temp = path + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (Exception error) { lastError = "Observer report write: " + error.Message; }
        }
    }
}
