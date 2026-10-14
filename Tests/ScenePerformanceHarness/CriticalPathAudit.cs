using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;

namespace ScenePerformanceHarness;
public sealed partial class Plugin {
    static bool nativeCriticalEnabled,haveCritical;
    static NativeCriticalProfile criticalFirst,criticalLast;
    static NativeRayProfileState criticalRayFirst,criticalRayLast;
    static void BeginNativeCritical(object renderer){if(nativeCriticalEnabled&&renderer is MyVulkanRender){var backend=(NativeBackend)BackendField.GetValue(renderer);if(backend.IsCreated)backend.BeginCriticalFrame(8);}}
    static void SampleNativeCritical(NativeBackend backend){if(!nativeCriticalEnabled)return;var value=backend.GetCriticalProfile();var ray=backend.GetCriticalRayState();if(!haveCritical){haveCritical=true;criticalFirst=value;criticalRayFirst=ray;}criticalLast=value;criticalRayLast=ray;}
    void WriteNativeCritical(){if(!nativeCriticalEnabled)return;if(!haveCritical||criticalLast.Selected<=criticalFirst.Selected)throw new Exception("No completed native critical-path samples.");if(criticalRayLast.Builds!=0||criticalRayLast.Dispatches!=0)throw new Exception("Ray-query work contaminated the raster profile.");File.WriteAllText(Path.Combine(reports,"native-critical-path.json"),JsonSerializer.Serialize(new{Instrumented=true,Data=criticalLast.Describe(criticalFirst),RayStart=criticalRayFirst,RayEnd=criticalRayLast},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));}
}
