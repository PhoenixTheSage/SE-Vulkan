"""Verify ordinary resident emission activation and matched full-scene policy."""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
read = lambda p: json.loads(p.read_text(encoding="utf-8-sig"))
launches, comparisons = [], []
for name in sys.argv[1:]:
    run = root / "TestResults/ScenePerformance" / name
    launches.append(read(run / "launch.json"))
    phases = {}
    for path in (run / "GameData/Reports").rglob("result.json"):
        result = read(path)
        assert not result["Instrumented"] and (result["Width"], result["Height"]) == (1920, 1080)
        assert result["BulkModels"] and result["NewActorViews"]
        enabled = result["ResidentEmission"]
        resident = read(path.parent / "resident-emission.json")
        assert resident["Averages"]["Enabled"] == int(enabled)
        if enabled:
            assert resident["Averages"]["Actors"] > 0 and resident["Averages"]["Direct"] > 0
        else:
            assert resident["Averages"]["Direct"] == 0
        phases[enabled] = dict(Result=result, Resident=resident,
                               Bulk=read(path.parent / "bulk-models.json"),
                               Native=read(path.parent / "native-performance.json"),
                               Profile=read(path.parent / "frame-profile.json"),
                               Present=read(path.parent / "present-flow.json"))
    assert set(phases) == {False, True}
    for key, value in phases[False]["Result"].items():
        if isinstance(value, bool) and key != "ResidentEmission":
            assert value == phases[True]["Result"][key], key
    saved = phases[False]["Result"]["CpuMeanMs"] - phases[True]["Result"]["CpuMeanMs"]
    comparisons.append(dict(Run=name, Prepared=phases[False], Resident=phases[True],
                            CpuSavedMs=saved,
                            ReductionPercent=saved / phases[False]["Result"]["CpuMeanMs"] * 100))
for key in ("PluginSha256", "NativeSha256", "SourceCheckpointSha256"):
    assert len({launch[key] for launch in launches}) == 1, key
target = root / "TestResults/ResidentEmission-Comparisons.json"
target.write_text(json.dumps(dict(Launches=launches, Comparisons=comparisons,
                                  Scope="Ordinary full1080p private saved camera, fixed accepted experimental policy except resident representation producer. Unpaused scene may drift. Requires both orders for acceptance; no full parity or <4.17ms claim."), indent=2), encoding="utf-8")
for row in comparisons:
    print(f"{row['Run']}: saved {row['CpuSavedMs']:.6f} ms ({row['ReductionPercent']:.2f}%)")
print(target)
