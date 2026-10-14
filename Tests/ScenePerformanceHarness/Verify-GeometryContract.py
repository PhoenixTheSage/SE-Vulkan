"""Verify that current world shader selection excludes index-driven merge inputs."""
import gzip
import json
import pathlib
import re
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[2]
selection = (root / "ClientPlugin/Rendering/Shaders/WorldShaderVariants.cs").read_text()
ids = set(re.findall(r'\(\d+u,"([0-9a-f]{64})","[0-9a-f]{64}"\)', selection))
with gzip.open(root / "Assets/Shaders/Vanilla/catalog.xml.gz", "rb") as stream:
    catalog = ET.parse(stream).getroot()
nodes = [n for n in catalog.iter("Variant") if n.get("Id") in ids]
assert ids and len(nodes) == len(ids), "Selected stock vertex variant missing from catalog"
rows = [{"Id": n.get("Id"), "Source": n.get("Source"),
         "MergeInstancing": any(d.get("Name") == "USE_MERGE_INSTANCING" for d in n.findall("Define"))} for n in nodes]
assert not any(r["MergeInstancing"] for r in rows), "Vertex-index-driven shader cannot use the remap"
assert "USE_MERGE_INSTANCING" not in selection, "Extra world variant requires a new compaction contract"
destination = root / "TestResults/Geometry-Shader-Contract.json"
destination.write_text(json.dumps({"ActiveStockVertexVariants": rows, "StockMergeInstancingUsed": False,
                                   "ExtraWorldMergeDefinePresent": False}, indent=2), encoding="utf-8")
print(f"PASS {len(rows)} selected stock vertex variants and extra world definitions exclude merge instancing")
