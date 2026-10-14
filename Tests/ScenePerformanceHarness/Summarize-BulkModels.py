"""Verify activation, matched policy and ordinary bulk scene comparisons."""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
read = lambda path: json.loads(path.read_text(encoding="utf-8-sig"))
launches = []
comparisons = []
for name in sys.argv[1:]:
    run = root / "TestResults/ScenePerformance" / name
    launches.append(read(run / "launch.json"))
    phases = {}
    for path in (run / "GameData/Reports").rglob("result.json"):
        result = read(path)
        assert not result["Instrumented"]
        assert (result["Width"], result["Height"]) == (1920, 1080)
        assert result["RetainedSubmission"] and result["WorldRecipes"] and result["ModelBatching"]
        enabled = result["BulkModels"]
        bulk = read(path.parent / "bulk-models.json")
        assert bulk["Averages"]["Enabled"] == int(enabled)
        if enabled:
            assert bulk["Averages"]["Instances"] > 0
            assert bulk["Averages"]["Groups"] > 0
            assert bulk["Averages"]["Packets"] > 0
        else:
            assert bulk["Averages"]["Instances"] == 0
        phases[enabled] = dict(Result=result, Bulk=bulk,
                               Native=read(path.parent / "native-performance.json"),
                               Models=read(path.parent / "model-batches.json"),
                               Submission=read(path.parent / "retained-submission.json"),
                               Views=read(path.parent / "new-actor-views.json"),
                               Profile=read(path.parent / "frame-profile.json"),
                               Present=read(path.parent / "present-flow.json"))
    assert set(phases) == {False, True}
    for key, value in phases[False]["Result"].items():
        if isinstance(value, bool) and key != "BulkModels":
            assert value == phases[True]["Result"][key], key
    saved = phases[False]["Result"]["CpuMeanMs"] - phases[True]["Result"]["CpuMeanMs"]
    comparisons.append(dict(Run=name, Managed=phases[False], Native=phases[True],
                            CpuSavedMs=saved,
                            ReductionPercent=saved / phases[False]["Result"]["CpuMeanMs"] * 100))
for key in ("PluginSha256", "NativeSha256", "SourceCheckpointSha256"):
    assert len({launch[key] for launch in launches}) == 1, key
output = dict(Launches=launches, Comparisons=comparisons,
              Scope="Ordinary full1080p same-source private saved camera, fixed policy except bulk model ownership. Independent unpaused geometry/physics may drift. Requires both orders for acceptance; no full pixel parity or <4.17 ms claim.")
target = root / "TestResults/BulkModels-Comparisons.json"
target.write_text(json.dumps(output, indent=2), encoding="utf-8")
for row in comparisons:
    print(f"{row['Run']}: saved {row['CpuSavedMs']:.6f} ms ({row['ReductionPercent']:.2f}%)")
print(target)
