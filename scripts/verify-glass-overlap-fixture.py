"""Check isolated glass source/capture evidence; report, never certify, pixel parity."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import numpy as np
from PIL import Image

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("--vulkan", required=True, type=Path)
p.add_argument("--dx11", required=True, type=Path)
p.add_argument("--output", required=True, type=Path)
a = p.parse_args()
phases = ("baseline", "decals", "restored")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def load(root, backend):
    reports = root / "Reports"
    assert not (reports / "fixture-failed.txt").exists(), f"Failed fixture: {root}"
    for name in ("fixture-completed.txt", "fixture-unloaded.txt", "exit-requested.txt"):
        assert (reports / name).exists(), f"Missing lifecycle marker: {name}"
    launch = json.loads((root / "launch.json").read_text(encoding="utf-8-sig"))
    prepared = json.loads((root / "prepared.json").read_text(encoding="utf-8-sig"))
    assert launch["Backend"] == backend and not launch["AnomalyParity"]
    assert prepared["Environment"]["SE_VULKAN_GLASS_OVERLAP_FIXTURE"] == "1"
    local = root / "Pulsar/Legacy/Local"
    assert digest(local / "Vulkan/plugin.dll") == launch["PluginSha256"]
    assert digest(local / "Vulkan/Assets/Native/win-x64/se_vulkan_native.dll") == launch["NativeSha256"]
    inputs = ET.parse(reports / "glass-overlap-inputs.xml").getroot()
    assert inputs.get("Backend") == backend and inputs.get("Schema") == "1"
    assert inputs.get("SceneRoi") == "40 160 560 160"
    panes = [{k: v for k, v in row.attrib.items() if k != "Id"} for row in inputs]
    assert len(panes) == 9 and sum(row["Source"] == "OLD" for row in panes) == 4
    observations, images, materials = {}, {}, {}
    for phase in (*phases, "cleanup"):
        row = ET.parse(reports / f"glass-overlap-{phase}.xml").getroot()
        assert row.get("Backend") == backend and row.get("Schema") == "1"
        names = ("OldVisibleParts", "NewVisibleParts", "OldCandidates", "OldNear", "OldFar", "SelectedNear", "SelectedFar", "RequestedPassFlags")
        values = {k: int(row.get(k)) for k in names}
        frame_input = ET.parse(reports / f"glass-overlap-{phase}.frame.xml").getroot()
        sun_offset = int(frame_input.get("SunOffset"))
        frame = (reports / f"glass-overlap-{phase}.frame.bin").read_bytes()
        assert len(frame) == 1280
        sun = np.frombuffer(frame, dtype="<f4", count=3, offset=sun_offset)
        expected_sun = -np.array([.3, .5, 1]) / np.linalg.norm([.3, .5, 1])
        assert np.isfinite(sun).all() and np.allclose(sun, expected_sun, atol=1e-5, rtol=0), "Invalid actual sun input"
        assert np.allclose(sun, [float(frame_input.get("Sun" + c)) for c in "XYZ"], atol=1e-7, rtol=0)
        material = ET.parse(reports / f"glass-overlap-{phase}.materials.xml").getroot()
        definitions = sorted((dict(row.attrib) for row in material.findall("Definition")), key=lambda row: row["Name"])
        assert [row["Name"] for row in definitions] == ["GlassInside", "GlassOutside"]
        materials[phase] = {"Multipliers": dict(material.attrib), "Definitions": definitions}
        if phase == "cleanup":
            assert values["OldVisibleParts"] == values["NewVisibleParts"] == values["OldCandidates"] == 0
        else:
            assert values["OldVisibleParts"] == 8 and values["NewVisibleParts"] == 10
            assert values["OldCandidates"] == (8 if phase == "decals" else 0)
            if phase == "decals":
                assert tuple(values[k] for k in names[3:]) == (6, 2, 6, 0, 48)
            image = Image.open(reports / f"glass-overlap-{phase}.png").convert("RGB")
            assert image.size == (640, 480)
            images[phase] = np.asarray(image).astype(np.int16)[160:320, 40:600]
        observations[phase] = values
    quality = {row.get("Name"): row.get("Value") for row in ET.parse(reports / "lighting-quality.xml").getroot()}
    assert "Diagnostics.HbaoSeed" in quality
    cameras = {line.split(";", 1)[0]: line.split("; camera=", 1)[1].split(";", 1)[0]
               for line in (reports / "capture-camera.txt").read_text().splitlines() if line.startswith("glass-overlap-")}
    if backend == "Vulkan":
        assert (reports / "harness-disposed.txt").exists()
        assert "enabled: 1; errors: 0" in (reports / "status-latest.txt").read_text(encoding="utf-8-sig")
        assert not re.search(r"enabled: 1; errors: [1-9]", (reports / "status-history.txt").read_text(encoding="utf-8-sig"))
    return reports, panes, observations, images, quality, cameras, digest(local / "CreativeFixtureHarness/plugin.dll"), materials


vk, dx = load(a.vulkan, "Vulkan"), load(a.dx11, "Dx11")
assert vk[1] == dx[1], "Authored source stacks differ"
assert vk[4] == dx[4], "Actual captured quality differs"
assert vk[5] == dx[5] and len(vk[5]) == 3, "Held cameras differ"
assert vk[6] == dx[6], "Prepared harness binaries differ"
assert vk[7] == dx[7], "Observed material definitions or renderer multipliers differ"
for path in vk[0].glob("glass-overlap-*.dds"):
    assert digest(path) == digest(dx[0] / path.name), f"Decal bytes differ: {path.name}"


def metrics(delta):
    delta = np.abs(delta)
    return {"MeanAbsoluteRgb8": delta.mean(axis=(0, 1)).tolist(), "MaxAbsoluteRgb8": delta.max(axis=(0, 1)).tolist(),
            "P95AbsoluteRgb8": np.percentile(delta, 95, axis=(0, 1)).tolist()}


result = {"Scope": "Isolated static OLD/NEW/mixed stock glass, decal membership, captures and cleanup. Metrics cover scene ROI x40,y160,w560,h160, excluding the lower HUD; the central reticle remains. Pixel metrics require separate review; no automatic whole-renderer parity claim.",
          "Vulkan": str(a.vulkan), "Dx11": str(a.dx11), "QualityMatched": True, "FinitePinnedSunVerified": True, "MaterialDefinitionsMatched": True,
          "VulkanObservations": vk[2], "Dx11Observations": dx[2], "Phases": {}, "DecalChanges": {}}
for phase in phases:
    result["Phases"][phase] = metrics(vk[3][phase] - dx[3][phase])
    # Absolute-image x64/272/480, y200, 80x80: pane interiors around
    # the authored projected centers. These are diagnostic, not tolerances.
    result["Phases"][phase]["SourceInteriors"] = {
        name: metrics(vk[3][phase][40:120, x:x+80] - dx[3][phase][40:120, x:x+80])
        for name, x in (("OLD", 24), ("NEW", 232), ("Mixed", 440))}
for label, data in (("Vulkan", vk), ("Dx11", dx)):
    delta = data[3]["decals"] - data[3]["baseline"]
    result["DecalChanges"][label] = {"ChangedPixels": int(np.any(np.abs(delta) > 1, axis=2).sum()),
                                    "RestoredVsBaseline": metrics(data[3]["restored"] - data[3]["baseline"])}
    assert result["DecalChanges"][label]["ChangedPixels"] > 20, f"No visible decal signal: {label}"
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2) + "\n")
print("PASS isolated glass source/capture evidence; pixel review remains separate: " + str(a.output))
