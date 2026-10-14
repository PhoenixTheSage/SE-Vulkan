"""Require ordinary matched policy and actual traversal activation in both orders."""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
read = lambda path: json.loads(path.read_text(encoding="utf-8-sig"))
launches, comparisons = [], []
for name in sys.argv[1:]:
    run = root / "TestResults/ScenePerformance" / name
    launches.append(read(run / "launch.json"))
    phases = {}
    for path in (run / "GameData/Reports").rglob("result.json"):
        result = read(path)
        assert not result["Instrumented"]
        assert (result["Width"], result["Height"]) == (1920, 1080)
        assert all(result[k] for k in ("RetainedSubmission", "WorldRecipes", "ModelBatching", "BulkModels", "NewActorViews"))
        enabled = result["ResidentTraversal"]
        counters = read(path.parent / "resident-traversal.json")
        assert counters["Averages"]["Enabled"] == int(enabled)
        assert (counters["Averages"]["Candidates"] > 0) == enabled
        assert (counters["Averages"]["Selected"] > 0) == enabled
        phases[enabled] = dict(Result=result, Traversal=counters,
                              Native=read(path.parent / "native-performance.json"),
                              Bulk=read(path.parent / "bulk-models.json"),
                              Views=read(path.parent / "new-actor-views.json"),
                              Profile=read(path.parent / "frame-profile.json"),
                              Present=read(path.parent / "present-flow.json"))
    assert set(phases) == {False, True}
    for key, value in phases[False]["Result"].items():
        if isinstance(value, bool) and key != "ResidentTraversal":
            assert value == phases[True]["Result"][key], key
    saved = phases[False]["Result"]["CpuMeanMs"] - phases[True]["Result"]["CpuMeanMs"]
    comparisons.append(dict(Run=name, Managed=phases[False], Resident=phases[True],
                            CpuSavedMs=saved,
                            ReductionPercent=100 * saved / phases[False]["Result"]["CpuMeanMs"]))
for key in ("PluginSha256", "NativeSha256", "SourceCheckpointSha256", "HarnessSha256"):
    assert len({launch[key] for launch in launches}) == 1, key
target = root / "TestResults/ResidentTraversal-Comparisons.json"
target.write_text(json.dumps(dict(Launches=launches, Comparisons=comparisons,
    Scope="Ordinary full1080p private Pertam source, fixed policy except resident actor traversal. Both orders required; independent unpaused physics may drift. No DX11 visual parity or <4.17 ms claim."), indent=2), encoding="utf-8")
for row in comparisons:
    print(f"{row['Run']}: saved {row['CpuSavedMs']:.6f} ms ({row['ReductionPercent']:.2f}%)")
print(target)
