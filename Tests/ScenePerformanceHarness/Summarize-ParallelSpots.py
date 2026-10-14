"""Validate full-workload, same-binary paired spotlight recording results."""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
read = lambda p: json.loads(p.read_text(encoding="utf-8-sig"))
comparisons = []
launches = []
for name in sys.argv[1:]:
    run = root / "TestResults/ScenePerformance" / name
    launch = read(run / "launch.json")
    launches.append(launch)
    phases = {}
    for path in (run / "GameData/Reports").rglob("result.json"):
        result = read(path)
        assert not result["Instrumented"]
        assert (result["Width"], result["Height"]) == (1920, 1080)
        key = result["ParallelSpots"]
        stats = read(path.parent / "spot-recording.json")
        assert stats["Averages"]["Enabled"] == int(key)
        if key:
            assert 0 < stats["Averages"]["Workers"] <= 4
            assert stats["Averages"]["Lights"] > 0
            assert stats["Averages"]["Commands"] > 0
        native = read(path.parent / "native-performance.json")
        assert abs(stats["Averages"]["Commands"] - native["SpotCommands"]) < 0.001
        phases[key] = dict(Result=result, Recording=stats, Native=native,
                           Profile=read(path.parent / "frame-profile.json"),
                           Present=read(path.parent / "present-flow.json"))
    assert set(phases) == {False, True}
    for key, value in phases[False]["Result"].items():
        if isinstance(value, bool) and key != "ParallelSpots":
            assert value == phases[True]["Result"][key], key
    saved = phases[False]["Result"]["CpuMeanMs"] - phases[True]["Result"]["CpuMeanMs"]
    comparisons.append(dict(Run=name, Serial=phases[False], Parallel=phases[True],
                            CpuSavedMs=saved,
                            ReductionPercent=saved / phases[False]["Result"]["CpuMeanMs"] * 100))
for key in ("PluginSha256", "NativeSha256", "SourceCheckpointSha256"):
    assert len({launch[key] for launch in launches}) == 1, key
output = dict(Launches=launches, Comparisons=comparisons,
              Scope="Ordinary 1080p private saved camera; identical policy except spotlight recording. Independent unpaused selection can drift. Requires both orders for acceptance; no full visual parity or <4.17 ms claim.")
target = root / "TestResults/ParallelSpots-Comparisons.json"
target.write_text(json.dumps(output, indent=2), encoding="utf-8")
for row in comparisons:
    print(f"{row['Run']}: saved {row['CpuSavedMs']:.6f} ms ({row['ReductionPercent']:.2f}%)")
print(target)
