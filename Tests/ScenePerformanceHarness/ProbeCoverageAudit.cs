using System.Collections.Concurrent;
using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
namespace ScenePerformanceHarness;
public sealed partial class Plugin {
    static readonly ConcurrentDictionary<int,object> probeCoverage=new();
    static readonly ConcurrentDictionary<int,object> oldProbeCoverage=new();
    static volatile bool probeCoverageCollecting;
    static bool probeCoverageRequested,haveProbeCoverageCount;static ulong probeCoverageFaces;
    static bool sourceProbeRefreshRequested;
    static void RefreshSourceProbe(object __instance){if(!probeCoverageCollecting||sourceProbeRefreshRequested)return;sourceProbeRefreshRequested=true;var field=HarmonyLib.AccessTools.Field(__instance.GetType(),"m_lastUpdateTime");field.SetValue(__instance,Activator.CreateInstance(field.FieldType));}
    static void StartProbeCoverage(){probeCoverageRequested=Environment.GetEnvironmentVariable("SE_VULKAN_PROBE_COVERAGE_AUDIT")=="1";probeCoverage.Clear();oldProbeCoverage.Clear();probeCoverageFaces=0;haveProbeCoverageCount=false;sourceProbeRefreshRequested=false;probeCoverageCollecting=probeCoverageRequested;}
    static void ObserveProbeCoverage(object renderer){
        if(!probeCoverageCollecting||renderer is not MyVulkanRender)return;
        var backend=(NativeBackend)BackendField.GetValue(renderer);var stats=backend.GetProbeStats();if(!haveProbeCoverageCount){haveProbeCoverageCount=true;probeCoverageFaces=stats.Faces;return;}ulong changed=stats.Faces-probeCoverageFaces;probeCoverageFaces=stats.Faces;if(changed==0)return;
        object audit=AuditVulkan(renderer);var groups=Items(Member(audit,"NativePassGroups")).Where(g=>Number(g,"Kind")==2).ToArray();
        IEnumerable<int> faces=changed>=6?Enumerable.Range(0,6):new[]{(int)(stats.NextFace+5)%6};
        foreach(int face in faces){if(probeCoverage.ContainsKey(face))continue;var selected=groups.Where(g=>Number(g,"View")==face+9).ToArray();
            var owners=selected.SelectMany(g=>Items(Member(g,"Owners"))).Select(Convert.ToUInt32).ToHashSet();
            probeCoverage.TryAdd(face,new{Face=face,Groups=selected,Owners=Items(Member(audit,"NativePassOwners")).Where(o=>owners.Contains(Convert.ToUInt32(Member(o,"Owner")))).ToArray(),LegacyLogicalRows=Member(audit,"LogicalRows"),Models=Member(audit,"Models"),Scope="Actual resident forward groups for this face; legacy candidates have broader packet replay and GPU clipping. No timing claim."});
        }
    }
    static bool ProbeCoverageComplete=>!probeCoverageRequested||probeCoverage.Count==6;
    void WriteProbeCoverage(){if(!probeCoverageRequested)return;probeCoverageCollecting=false;if(probeCoverage.Count!=6)throw new Exception("Probe coverage did not observe all six faces");File.WriteAllText(Path.Combine(reports,"probe-coverage.json"),JsonSerializer.Serialize(new{Backend=expected,Faces=probeCoverage.OrderBy(x=>x.Key).Select(x=>x.Value).ToArray(),OldViews=oldProbeCoverage.OrderBy(x=>x.Key).Select(x=>x.Value).ToArray(),Scope="Six owning-view snapshots after pause and matched-input freeze; source ranges/material bindings and actual native resident groups. Frames can differ within a complete probe update cycle. Diagnostic only."},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));}
}
