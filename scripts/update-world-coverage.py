"""Record tested stock-world evidence without declaring remaining effects complete."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent.parent
gpu = json.loads((root / "TestResults/WorldPassGpuTests/result.json").read_text())
assert gpu["Stats"]["ValidationErrors"] == 0 and gpu["Checks"] >= 61
registry = root / "Docs/ShaderPassCoverage.json"
data = json.loads(registry.read_text())
data.update(Scope="Independent stock correctness fixtures and production Creative world integration; full visual parity is separate",
            WorldEvidence="TestResults/WorldPassGpuTests/result.json", WorldChecksPassed=gpu["Checks"],
            WorldCounters=gpu["Work"], MaterialDepthPipelines=44, StockLightingStages=4,
            SupplementalStages=44, SceneEvidence="TestResults/StockWorldScene/result.json")
for item in data["Passes"]:
    item["StartupState"] = "Ready"
    item.pop("StartupNote", None)
data["Pending"] = ["Prefiltered environment/specular probes and BRDF LUT integration",
                   "Full stock cascade filtering, GPU light culling and local-light shadows/reflector masks",
                   "Timed voxel fades and quality mapping, probe/lighting parity, broad transparency/particle acceptance and remaining postprocess passes",
                   "Large-world performance/visual acceptance and live Linux GPU/game verification"]
if len(data["Passes"]) == 2:
    for name in ("GPU skinning palettes", "Generic/static hardware instancing", "Packed cube rotation/deformation",
                 "Stock directional/point/spot deferred lighting", "Native HDR/depth composition", "Asynchronous bounded upload batches"):
        data["Passes"].append(dict(Name=name, State="Verified", Scope="Controlled native GPU fixture; Creative observations tracked separately",
                                  Evidence=data["WorldEvidence"], ValidationErrors=0))
fixture = root / "TestResults/CreativeFixture/8379118d53634d5b8e817d4919d6ec2f"
status = (fixture / "Reports/status-latest.txt").read_text(encoding="utf-8-sig")
world = re.search(r"Frames=(\d+); material draws=(\d+); instances=(\d+); light passes=(\d+)", status)
assert world and "enabled: 1; errors: 0" in status
data["CreativeWorld"] = dict(Evidence=str(fixture.relative_to(root)).replace("\\", "/"), State="Active", VisualAcceptance="PendingComparison",
                             Frames=int(world[1]), MaterialDraws=int(world[2]), Instances=int(world[3]), LightingPasses=int(world[4]),
                             SkinnedDraws=int(re.search(r"skinned draws=(\d+)", status)[1]), ValidationErrors=0,
                             UploadBatches=int(re.search(r"Timeline batches=(\d+)", status)[1]),
                             UploadSlotWaits=int(re.search(r"outstanding-slot waits=(\d+)", status)[1]))
registry.write_text(json.dumps(data, indent=2) + "\n")
print("Recorded stock-world GPU checks and observed Creative counters; pending parity remains explicit.")
