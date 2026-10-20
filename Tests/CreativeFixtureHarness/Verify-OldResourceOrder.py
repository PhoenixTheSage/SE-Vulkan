"""Gate the paired live OLD resource-order fixture against its stock oracle."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("dx11", type=Path)
parser.add_argument("vulkan", type=Path)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()

def require(condition, message):
    if not condition:
        raise ValueError(message)

for root, backend in ((args.dx11, "Dx11"), (args.vulkan, "Vulkan")):
    reports = root / "Reports"
    manifest = ET.parse(reports / "mesh-decals-assets.xml").getroot()
    require(manifest.get("OldResourceOrder") == "true" and manifest.get("Backend") == backend,
            "Requires the matching live OLD resource-order scenario")
    panels = manifest.findall("Panel")
    require([p.get("Case") for p in panels] == ["A-B", "B-A", "A-B-A-recreated", "A-B"],
            "Missing forward/reverse/recreation/control inputs")
    capture = ET.parse(reports / "mesh-decals-capture.xml").getroot()
    require(capture.get("Actors") == "12" and capture.get("Old") == "12" and capture.get("New") == "0",
            "Fixture does not witness twelve actual OLD actors")
    require(capture.get("ValidationErrors") == "0", "Fixture has validation errors")
    if backend == "Vulkan":
        require(capture.get("CutoutPackets") == "8" and capture.get("BlendedPackets") == "0",
                "Vulkan fixture does not witness the required cutout packets")
    cleanup = ET.parse(reports / "mesh-decals-cleanup.xml").getroot()
    require(all(cleanup.get(field) == "0" for field in ("Actors", "BlendedPackets", "CutoutPackets", "ValidationErrors")),
            "Render-object cleanup did not complete cleanly")
    for marker in ("fixture-completed.txt", "fixture-unloaded.txt", "exit-requested.txt"):
        require((reports / marker).is_file(), "Incomplete fixture capture/cleanup: " + str(reports / marker))

stock_manifest = ET.parse(args.dx11 / "Reports/mesh-decals-assets.xml").getroot()
stock_parts = ET.parse(args.dx11 / "Reports/mesh-decals-old-sort.xml").getroot().findall("Part")
cutouts = [p for p in stock_parts if p.get("Technique") == "DECAL_CUTOUT"]
require(len(cutouts) == 8, "Stock key capture must contain all eight cutouts")
by_actor = {p.get("Actor"): p for p in cutouts}
a_keys = {int(by_actor[p.get("Id")].get("SortKey")) for p in stock_manifest.findall("Panel")}
all_keys = {int(p.get("SortKey")) for p in cutouts}
require(len(a_keys) == 1 and len(all_keys) == 2, "Resource keys did not survive actor reversal/recreation")
a_key = next(iter(a_keys))
b_key = next(iter(all_keys - a_keys))
require(a_key < b_key, "Stock allocation changed the fixture's blue-wins oracle")

# Regenerate comparisons now; never qualify a stale summary from another DLL/run.
args.output.parent.mkdir(parents=True, exist_ok=True)
comparison_path = args.output.with_suffix(".comparison.json")
subprocess.run([sys.executable, str(Path(__file__).with_name("Compare-MeshDecalBuffers.py")),
                str(args.dx11), str(args.vulkan), "--output", str(comparison_path)],
               check=True, capture_output=True, text=True)
comparison = json.loads(comparison_path.read_text(encoding="utf-8"))
require(not comparison["DiagnosticPartialCapture"], "Partial captures cannot qualify ordering")
require(len(comparison["Panels"]) == 4, "Requires all four live panel comparisons")
failed = []
for panel in comparison["Panels"]:
    stages = panel["Stages"]
    require(stages["color-lod"]["Dx11Mean"] == [0, 0, 1, 0], "Stock ROI is not independently blue")
    require(stages["depth"]["MaxAbsoluteDelta"] == [0, 0, 0, 0], "Depth mismatch invalidates this ordering isolation")
    if any(stages["color-lod"]["MaxAbsoluteDelta"]):
        failed.append(panel["Column"])
result = {
    "Status": "FAIL" if failed else "PASS",
    "Scope": "Live OLD resource-order parity: exact decoded color/LOD and depth in four matched ROIs, verified stock keys, actor ownership and render-object cleanup. HDR/light and final plugin-lifecycle acceptance are separate.",
    "Dx11": str(args.dx11), "Vulkan": str(args.vulkan),
    "StockAKey": a_key, "StockBKey": b_key,
    "FailedColumns": failed, "Comparison": str(comparison_path),
    "HarnessDisposedMarkers": {"Dx11": (args.dx11 / "Reports/harness-disposed.txt").is_file(),
                               "Vulkan": (args.vulkan / "Reports/harness-disposed.txt").is_file()},
}
args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, indent=2))
sys.exit(1 if failed else 0)
