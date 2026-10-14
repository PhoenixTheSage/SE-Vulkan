"""Validate the ordinary both-order submission comparisons and preserve evidence."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
runs = root / "TestResults/ScenePerformance"
ids = ["Vulkan-d9a17b7e68894446a1f2a1c2f209893f", "Vulkan-69ad1ab8cc7b45b2a82acb95d29431c4"]
read = lambda p: json.loads(p.read_text(encoding="utf-8-sig"))
pairs = []
launches = []
for name in ids:
    run = runs / name
    launch = read(run / "launch.json")
    launches.append(launch)
    phases = list((run / "GameData/Reports").rglob("result.json"))
    assert len(phases) == 2
    rows = {}
    for path in phases:
        row = read(path)
        assert row["Backend"] == "Vulkan" and not row["Instrumented"]
        assert (row["Width"], row["Height"], row["SampleSeconds"], row["RigidCellMetres"]) == (1920,1080,30,64)
        assert row["ParallelCascades"] and not any(row[k] for k in ["WorldIndirect", "GeometryArena", "RetainedObjects", "LightTiles"])
        assert all(row[k] for k in ["RigidBatching", "BatchHints", "VoxelLookupCache", "VoxelObjectCache", "TextureValidationCache"])
        native = read(path.parent / "native-performance.json")
        submission = read(path.parent / "retained-submission.json")
        mode = row["RetainedSubmission"]
        assert submission["Averages"]["Enabled"] == int(mode)
        if mode: assert all(submission["Averages"][k]>0 for k in ["Actors", "Parts", "ObjectBlobBytes", "InstanceBlobBytes"])
        rows[mode] = dict(Result=row, Native=native, Submission=submission)
    assert set(rows) == {False,True}
    legacy, prepared = rows[False], rows[True]
    gain = legacy["Result"]["CpuMeanMs"] - prepared["Result"]["CpuMeanMs"]
    pairs.append(dict(Run=name, PreparedFirst=launch["RetainedSubmissionRequested"], Legacy=legacy, Prepared=prepared,
                      CpuSavedMs=gain, CpuReductionPercent=gain/legacy["Result"]["CpuMeanMs"]*100))
for key in ["PluginSha256", "NativeSha256", "SourceCheckpointSha256"]:
    assert len({l[key] for l in launches}) == 1
dxrun = runs / "Dx11-c6bae8cee7a14ce9afaa20d8ef3dba35"
dx = read(dxrun / "GameData/Reports/result.json")
assert not dx["Instrumented"] and dx["Backend"] == "Dx11" and (dx["Width"],dx["Height"]) == (1920,1080)
assert read(dxrun / "launch.json")["SourceCheckpointSha256"] == launches[0]["SourceCheckpointSha256"]
output = dict(Scope="Ordinary same-source unpaused saved-camera 1080p comparisons, external GPU load removed by user. Physics/selection drift; exact coverage/pixels verified separately in paused diagnostic. Draw-through-Present CPU is distinct from consecutive-present interval. Full DX11 parity and <4.17 ms remain open.",
              Launches=launches, Pairs=pairs, Dx11=dict(Run=dxrun.name,Result=dx),
              PixelEvidence="Vulkan-4384ca2ec35a4c87a2a2064a381395ca/GameData/Reports/pixel-delta.json")
path = root / "TestResults/RetainedSubmission-CleanComparisons.json"
path.write_text(json.dumps(output,indent=2),encoding="utf-8")
for pair in pairs: print(f'{pair["Run"]}: saved {pair["CpuSavedMs"]:.3f} ms ({pair["CpuReductionPercent"]:.2f}%)')
print(path)
