"""Read-only intake: authenticate selected world vertex modules and index inputs.

This reports shader interfaces, not indirect-draw correctness or performance.
It never edits game shader assets or changes production shader selection.
"""
import gzip
import hashlib
import json
import pathlib
import re
import struct
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[2]
assets = root / "Assets/Shaders/Vanilla"
selection_bytes = (root / "ClientPlugin/Rendering/Shaders/WorldShaderVariants.cs").read_bytes()
selection = selection_bytes.decode("utf-8-sig")
coverage = ET.parse(assets / "coverage.xml").getroot()
catalog_bytes = (assets / "catalog.xml.gz").read_bytes()
assert hashlib.sha256(catalog_bytes).hexdigest() == coverage.get("CatalogSha256")
catalog = ET.fromstring(gzip.decompress(catalog_bytes))
variants = {n.get("Id"): n for n in catalog.iter("Variant")}
manifest_bytes = (assets / "Extra/manifest.xml").read_bytes()
assert hashlib.sha256(manifest_bytes).hexdigest() == coverage.get("WorldExtrasSha256")
manifest = ET.fromstring(manifest_bytes)
selected = []
for pipeline, vertex, _pixel in re.findall(r'\((\d+)u,"([0-9a-f]{64})","([0-9a-f]{64})"\)', selection):
    variant = variants[vertex]
    selected.append((int(pipeline), "stock", variant.get("Module"), assets / "Modules"))
for pipeline in re.findall(r'\((\d+)u,"[A-Za-z]+",new ', selection):
    matches = [n for n in manifest.iter("Shader") if n.get("Id") == pipeline and n.get("Stage") == "Vertex"]
    assert len(matches) == 1, f"Missing/ambiguous supplement vertex {pipeline}"
    selected.append((int(pipeline), "supplement", matches[0].get("Module"), assets / "Extra"))
assert selected and len({p for p, *_ in selected}) == len(selected)
index_builtins = {5: "VertexId", 6: "InstanceId", 42: "VertexIndex", 43: "InstanceIndex",
                  4424: "BaseVertex", 4425: "BaseInstance", 4426: "DrawIndex"}
rows = []
for pipeline, source, module, directory in selected:
    raw = (directory / (module + ".spv")).read_bytes()
    assert hashlib.sha256(raw).hexdigest() == module, f"Module hash mismatch: {module}"
    assert len(raw) >= 20 and len(raw) % 4 == 0
    words = struct.unpack(f"<{len(raw)//4}I", raw)
    assert words[0] == 0x07230203
    inputs, decorated = set(), {}
    cursor = 5
    while cursor < len(words):
        count, opcode = words[cursor] >> 16, words[cursor] & 65535
        assert count and cursor + count <= len(words)
        instruction = words[cursor:cursor+count]
        if opcode == 59 and count >= 4 and instruction[3] == 1:  # OpVariable, Input
            inputs.add(instruction[2])
        if opcode == 71 and count == 4 and instruction[2] == 11:  # OpDecorate BuiltIn
            decorated[instruction[1]] = instruction[3]
        # Fail explicitly rather than overlooking a member-decorated input block.
        if opcode == 72 and count == 5 and instruction[3] == 11 and instruction[4] in index_builtins:
            raise AssertionError(f"Member index builtin requires input-block resolution: {module}")
        cursor += count
    indexes = sorted({index_builtins[value] for target, value in decorated.items()
                      if target in inputs and value in index_builtins})
    rows.append({"Pipeline": pipeline, "Source": source, "ModuleSha256": module,
                 "IndexInputs": indexes})
report = {"Scope": "WorldShaderVariants object-geometry vertex interfaces; voxel and other pass shaders excluded",
          "ShaderSelectionSha256": hashlib.sha256(selection_bytes).hexdigest(),
          "CatalogSha256": coverage.get("CatalogSha256"),
          "SupplementManifestSha256": coverage.get("WorldExtrasSha256"),
          "VertexPipelines": sorted(rows, key=lambda r: r["Pipeline"]),
          "IndirectExecutionProven": False}
destination = root / "TestResults/Indirect-Shader-Contract.json"
destination.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
with_index = [r["Pipeline"] for r in rows if r["IndexInputs"]]
print(f"Authenticated {len(rows)} world vertex pipelines; index-input pipelines: {with_index}")
print(f"Read-only interface evidence: {destination}")
