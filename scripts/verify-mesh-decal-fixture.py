"""Validate paired fixture provenance and report per-panel image differences.

This is a comparison report, not an automatic visual-parity certificate.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import xml.etree.ElementTree as ET

import numpy as np
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("dx11", type=Path)
parser.add_argument("vulkan", type=Path)
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()

def read(root, backend):
    reports = root / "Reports"
    for name in ("fixture-completed.txt", "fixture-unloaded.txt", "exit-requested.txt", "mesh-decals.png"):
        if not (reports / name).is_file():
            raise ValueError(f"{backend}: missing terminal/capture artifact {name}")
    if (reports / "fixture-failed.txt").exists():
        raise ValueError(f"{backend}: fixture failed")
    render_log = root / "GameData/VRageRender-DirectX11.log"
    if render_log.exists() and any("meshdecalassets" in line.lower() and "error" in line.lower()
                                  for line in render_log.read_text(errors="replace").splitlines()):
        raise ValueError(f"{backend}: fixture texture loader errors invalidate the capture")
    assets = ET.parse(reports / "mesh-decals-assets.xml").getroot()
    overlap = assets.attrib.get("Overlap", "false") == "true"
    cutout = assets.attrib.get("Cutout", "false") == "true"
    raster = assets.attrib.get("Raster", "false") == "true"
    instance_color = assets.attrib.get("InstanceColor", "false") == "true"
    active_fade = assets.attrib.get("ActiveFade", "false") == "true"
    captured_fade = None
    if active_fade or "FadeThreshold" in assets.attrib:
        folder = "lighting-buffers" if backend == "Dx11" else "mesh-buffers"
        frame = (reports / folder / "frame.bin").read_bytes()
        # Authenticated stock Frame metadata: DistanceFade float32 at 1276.
        if len(frame) != 1280 or "FadeThreshold" not in assets.attrib:
            raise ValueError(f"{backend}: missing exact frame/threshold evidence")
        captured_fade = float(np.frombuffer(frame, dtype="<f4", count=1, offset=1276)[0])
        if captured_fade != float(assets.attrib["FadeThreshold"]):
            raise ValueError(f"{backend}: actual captured fade threshold differs: {captured_fade}")
    observed = ET.parse(reports / "mesh-decals-capture.xml").getroot().attrib
    if observed["Backend"] != backend or [int(observed[k]) for k in ("Actors", "Old", "New")] != ([16, 12, 4] if instance_color else [16, 8, 8]):
        raise ValueError(f"{backend}: source admission was not established: {observed}")
    if int(observed["ValidationErrors"]) or (backend == "Vulkan" and int(observed["BlendedPackets"]) != (0 if instance_color else 4 if raster else 32 if overlap else 16)):
        raise ValueError(f"{backend}: invalid Vulkan source/validation state")
    if backend == "Vulkan" and int(observed.get("CutoutPackets", "0")) != (0 if instance_color else 4 if raster else 16 if cutout else 0):
        raise ValueError("Vulkan cutout source admission mismatch")
    if backend == "Dx11" and (cutout or raster):
        keys = ET.parse(reports / "mesh-decals-old-sort.xml").getroot()
        if sum(part.attrib["Technique"] == "DECAL_CUTOUT" for part in keys) != (2 if raster else 8):
            raise ValueError("DX11 OLD cutout proxy admission mismatch")
    if instance_color:
        if backend == "Vulkan" and (int(observed["GenericActors"]) != 8 or int(observed["FadePackets"]) != 8):
            raise ValueError("Vulkan genuine generic/fade source admission mismatch")
        if backend == "Dx11":
            keys = ET.parse(reports / "mesh-decals-old-sort.xml").getroot()
            if sum(int(part.attrib["InstanceCount"]) > 0 for part in keys) != 16:
                raise ValueError("DX11 genuine generic proxy admission mismatch")
    cleanup = ET.parse(reports / "mesh-decals-cleanup.xml").getroot().attrib
    if any(int(cleanup[k]) for k in ("Actors", "Old", "New", "BlendedPackets", "ValidationErrors")):
        raise ValueError(f"{backend}: cleanup incomplete")
    if int(cleanup.get("CutoutPackets", "0")):
        raise ValueError("Cutout cleanup incomplete")
    quality = {s.attrib["Name"]: s.attrib["Value"] for s in ET.parse(reports / "lighting-quality.xml").getroot()}
    assets = ET.parse(reports / "mesh-decals-assets.xml").getroot()
    panels = [{k: v for k, v in panel.attrib.items() if k not in ("Id", "Model")} for panel in assets.findall("Panel")]
    hashes = {}
    for file in sorted((reports / "MeshDecalAssets").iterdir()):
        data = file.read_bytes()
        if file.suffix == ".mwm":
            # Absolute fixture texture paths contain this run's GUID. The two
            # runs use the same root, filenames, geometry and material bytes.
            data = data.replace(root.name.encode("ascii"), b"0" * len(root.name))
        hashes[file.name] = hashlib.sha256(data).hexdigest()
    screenshot = np.array(Image.open(reports / "mesh-decals.png").convert("RGB"))
    raster_coverage = []
    instance_coverage = []
    if raster or instance_color:
        if backend == "Dx11":
            depth = np.fromfile(reports / "lighting-buffers/depth.rgba32f", dtype="<f4").reshape(480, 640, 4)[..., 0]
        else:
            depth = np.fromfile(reports / "mesh-buffers/depth.d32f", dtype="<f4").reshape(480, 640)
        focal = 480 / (2 * math.tan(float(quality["Graphics.FieldOfView"]) / 2))
        for panel in panels:
            row, column = int(panel["Row"]), int(panel["Column"])
            x, y = 320 + focal * ((column - 1.5) * 2.3) / 8, 240 - focal * (.6 + (1.5 - row) * 2.3) / 8
            if instance_color:
                sx, sy = round(x), round(y)
                samples = depth[sy-1:sy+2, sx-1:sx+2]
                if active_fade and row == 3:
                    half = .9 * focal / 8 - 8
                    samples = depth[round(y-half):round(y+half), round(x-half):round(x+half)]
                    if not np.isfinite(samples).all() or not np.any(samples > 0) or not np.any(samples == 0):
                        raise ValueError(f"{backend}: active fade lacks partial visible coverage at {row}/{column}")
                    instance_coverage.append({"Row": row, "Column": column, "VisiblePixels": int(np.count_nonzero(samples > 0)), "Pixels": int(samples.size)})
                    continue
                if not np.isfinite(samples).all() or not np.all(samples > 0):
                    raise ValueError(f"{backend}: instance-color panel lacks visible depth at {row}/{column}: {samples}")
                instance_coverage.append({"Row": row, "Column": column, "Depth": float(samples.mean())})
                continue
            values = []
            for side in (-1, 1):
                sx, sy = round(x + side * .45 * focal / 8), round(y)
                samples = depth[sy-1:sy+2, sx-1:sx+2]
                expected_visible = side == -1 or column == 1
                if not np.isfinite(samples).all() or not np.all((samples > 0) == expected_visible):
                    raise ValueError(f"{backend}: raster front/back coverage mismatch at {row}/{column} side {side}: {samples}")
                values.append(float(samples.mean()))
            raster_coverage.append({"Row": row, "Column": column, "FrontDepth": values[0], "BackDepth": values[1]})
    return {"Root": str(root), "HarnessDisposeObserved": (reports / "harness-disposed.txt").exists(), "Observation": observed, "Quality": quality,
            "Panels": panels, "Assets": hashes, "Sun": assets.attrib["TowardSun"], "Overlap": overlap, "Cutout": cutout, "Raster": raster, "InstanceColor": instance_color, "ActiveFade": active_fade, "CapturedFrameDistanceFade": captured_fade, "FadeThreshold": assets.attrib.get("FadeThreshold", ""), "ColorUpdates": assets.attrib.get("ColorUpdates", "false"), "MaterialColors": assets.attrib.get("MaterialColors", "false"), "RasterCoverage": raster_coverage, "InstanceCoverage": instance_coverage}, screenshot

dx, a = read(args.dx11, "Dx11")
vk, b = read(args.vulkan, "Vulkan")
for field in ("Quality", "Panels", "Assets", "Sun", "Overlap", "Cutout", "Raster", "InstanceColor", "ActiveFade", "CapturedFrameDistanceFade", "FadeThreshold", "ColorUpdates", "MaterialColors"):
    if dx[field] != vk[field]:
        raise ValueError(f"Paired {field} mismatch")
if a.shape != b.shape or a.shape[:2] != (480, 640):
    raise ValueError("Fixture capture size mismatch")
observer = json.loads((args.vulkan / "Reports/d3d11-observer.json").read_text(encoding="utf-8-sig"))
if observer["attempts"]:
    raise ValueError("Observed managed D3D11 creation attempts in Vulkan")

focal = a.shape[0] / (2 * math.tan(float(dx["Quality"]["Graphics.FieldOfView"]) / 2))
panels = []
for panel in dx["Panels"]:
    row, column = int(panel["Row"]), int(panel["Column"])
    x = a.shape[1] / 2 + focal * ((column - 1.5) * 2.3) / 8
    y = a.shape[0] / 2 - focal * (.6 + (1.5 - row) * 2.3) / 8
    half = .9 * focal / 8 - 8  # Avoid silhouette/AA and depth-offset edges.
    box = [round(x - half), round(y - half), round(x + half), round(y + half)]
    x0, y0, x1, y1 = box
    left, right = a[y0:y1, x0:x1], b[y0:y1, x0:x1]
    delta = np.abs(left.astype(np.int16) - right.astype(np.int16))
    panels.append({"Row": row, "Column": column, "Technique": panel["Technique"],
                   "TextureRole": panel["TextureRole"], "OldRequested": panel["OldRequested"], "Roi": box,
                   "Dx11MeanRgb8": left.mean(axis=(0, 1)).tolist(), "VulkanMeanRgb8": right.mean(axis=(0, 1)).tolist(),
                   "MeanAbsoluteDeltaRgb8": delta.mean(axis=(0, 1)).tolist(),
                   "P95AbsoluteDeltaRgb8": np.percentile(delta, 95, axis=(0, 1)).tolist(),
                   "MaxAbsoluteDeltaRgb8": delta.max(axis=(0, 1)).tolist()})
result = {"Scope": "Reviewed-image aid: pinned settings, normalized identical authored MWM/DDS assets, source admission and per-panel interior RGB8 differences. No automatic appearance acceptance; process absence must be checked separately.",
          "Dx11": dx, "Vulkan": vk, "Panels": panels}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"Output": str(args.output), "Panels": len(panels),
                  "LargestPanelMeanDeltaRgb8": np.max([p["MeanAbsoluteDeltaRgb8"] for p in panels], axis=0).tolist()}, indent=2))
