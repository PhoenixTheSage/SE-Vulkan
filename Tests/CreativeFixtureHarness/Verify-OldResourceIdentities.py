"""Compare the live Vulkan metadata producer to actual held DX11 sort fields.

This gate does not qualify draw order, pixel/HDR parity, or plugin disposal.
"""
import argparse
from collections import Counter
import json
import re
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("dx11", type=Path)
parser.add_argument("vulkan", type=Path)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--full-tables", action="store_true")
args = parser.parse_args()

def require(condition, message):
    if not condition:
        raise ValueError(message)

manifests = {}
for root, backend in ((args.dx11, "Dx11"), (args.vulkan, "Vulkan")):
    reports = root / "Reports"
    manifest = ET.parse(reports / "mesh-decals-assets.xml").getroot()
    require(manifest.get("Backend") == backend and manifest.get("OldResourceOrder") == "true",
            "Requires matched live OLD resource-order fixtures")
    require([p.get("Case") for p in manifest.findall("Panel")] ==
            ["A-B", "B-A", "A-B-A-recreated", "A-B"], "Missing actor-order inputs")
    capture = ET.parse(reports / "mesh-decals-capture.xml").getroot()
    require(capture.get("Old") == "12" and capture.get("Actors") == "12" and
            capture.get("New") == "0" and capture.get("GenericActors") == "0" and
            capture.get("ValidationErrors") == "0", "Incomplete/errorful held rigid ownership capture")
    for marker in ("fixture-completed.txt", "fixture-unloaded.txt", "exit-requested.txt"):
        require((reports / marker).is_file(), "Missing fixture lifecycle marker: " + marker)
    manifests[backend] = manifest

stock = ET.parse(args.dx11 / "Reports/mesh-decals-old-sort.xml").getroot().findall("Part")
producer = ET.parse(args.vulkan / "Reports/mesh-old-resource-identities.xml").getroot()
require(producer.get("Initialized") == "true", "Shared identity producer was not initialized")
require(len(stock) == 12 and len(producer.findall("Actor")) == 12, "Missing owned fixture actors")
expected = Counter((int(p.get("Mesh")), int(p.get("Material")), int(p.get("SortKey"))) for p in stock)
actual = Counter()
candidate_by_actor = {}
for actor in producer.findall("Actor"):
    levels = actor.findall("Lod")
    require(len(levels) == 1, "The held fixture requires one authored LOD per model")
    lod = levels[0]
    parts = lod.findall("Part")
    require(len(parts) == 1, "The held fixture requires one emitted part per model")
    part = parts[0]
    item = (int(lod.get("Resource")), int(part.get("Material")), int(part.get("RigidSortKey")))
    actual[item] += 1
    candidate_by_actor[actor.get("Id")] = item
stock_by_actor = {p.get("Actor"): (int(p.get("Mesh")), int(p.get("Material")), int(p.get("SortKey"))) for p in stock}
a_expected = {stock_by_actor[p.get("Id")] for p in manifests["Dx11"].findall("Panel")}
a_actual = {candidate_by_actor[p.get("Id")] for p in manifests["Vulkan"].findall("Panel")}
passed = expected == actual and a_expected == a_actual
tables = None
if args.full_tables:
    reference = ET.parse(args.dx11 / "Reports/old-layout-identities.xml").getroot()
    require(reference.get("Passed") == "true", "Stock layout/source-table oracle did not complete")
    def normalize(value):
        return re.sub(r"(CreativeFixture[\\/])[0-9a-f]{32}([\\/])", r"\1RUN\2", value)
    def rows(root, path):
        values = root.findall(path)
        require(values, "Missing full identity table: " + path)
        return [{key: normalize(value) for key, value in item.attrib.items()} for item in values]
    materials_expected = rows(reference, "MaterialTable/Material")
    materials_actual = rows(producer, "MaterialTable/Material")
    layouts_expected = rows(reference, "Layouts/Layout")
    layouts_actual = rows(producer, "LayoutTable/Layout")
    def files(root):
        values = root.findall("Files/File")
        require(values, "Missing full file table")
        return [(normalize(file.get("Name")), [int(l.get("Resource")) for l in file.findall("Lod")]) for file in values]
    files_expected, files_actual = files(reference), files(producer)
    tables = {"Materials": {"Dx11Count": len(materials_expected), "VulkanCount": len(materials_actual), "Match": materials_expected == materials_actual},
              "Layouts": {"Dx11Count": len(layouts_expected), "VulkanCount": len(layouts_actual), "Match": layouts_expected == layouts_actual},
              "Files": {"Dx11Count": len(files_expected), "VulkanCount": len(files_actual), "Match": files_expected == files_actual},
              "Normalization": "Only isolated fixture-directory GUIDs; all material fields, layout components and file LOD indices compared in retained order."}
    passed = passed and all(tables[name]["Match"] for name in ("Materials", "Layouts", "Files"))
def records(values):
    return [{"Resource": r, "Material": m, "RigidSortKey": k, "Actors": count}
            for (r, m, k), count in sorted(values.items())]
result = {"Status": "PASS" if passed else "FAIL",
          "Scope": "Live CPU OLD resource/material/rigid sort-key production for the four-panel fixture; draw order, pixels/HDR and final plugin disposal are separate gates.",
          "Dx11": str(args.dx11), "Vulkan": str(args.vulkan),
          "Expected": records(expected), "Actual": records(actual),
          "ReversedAndRecreatedAIdentitiesMatch": a_expected == a_actual,
          "FullTables": tables}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, indent=2))
sys.exit(0 if passed else 1)
