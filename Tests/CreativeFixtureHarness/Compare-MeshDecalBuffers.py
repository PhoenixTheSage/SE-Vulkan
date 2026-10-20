"""Compare held-panel material/lighting captures, without fitting exposure."""
import argparse
import json
import math
import xml.etree.ElementTree as ET
from pathlib import Path

import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument("dx11", type=Path)
parser.add_argument("vulkan", type=Path)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--allow-partial-vulkan", action="store_true", help="Diagnostic use only: read completed raw files from a failed Vulkan capture; never establishes fixture acceptance")
args = parser.parse_args()
shape = (480, 640)
names = ("color-lod", "normal-ao", "metal-gloss-emissive", "depth", "hdr")

def quality(root):
    return {s.attrib["Name"]: s.attrib["Value"] for s in ET.parse(root / "Reports/lighting-quality.xml").getroot()}

dx_scene = ET.parse(args.dx11 / "Reports/mesh-decals-assets.xml").getroot()
vk_scene = ET.parse(args.vulkan / "Reports/mesh-decals-assets.xml").getroot()
if dx_scene.attrib.get("Overlap", "false") != vk_scene.attrib.get("Overlap", "false"):
    raise ValueError("Overlap scenario mismatch")
if dx_scene.attrib.get("Cutout", "false") != vk_scene.attrib.get("Cutout", "false"):
    raise ValueError("Cutout scenario mismatch")

if dx_scene.attrib.get("Raster", "false") != vk_scene.attrib.get("Raster", "false"):
    raise ValueError("Raster scenario mismatch")

if dx_scene.attrib.get("InstanceColor", "false") != vk_scene.attrib.get("InstanceColor", "false"):
    raise ValueError("Instance-color scenario mismatch")

if dx_scene.attrib.get("ColorUpdates", "false") != vk_scene.attrib.get("ColorUpdates", "false"):
    raise ValueError("Entity color-update scenario mismatch")

if dx_scene.attrib.get("MaterialColors", "false") != vk_scene.attrib.get("MaterialColors", "false"):
    raise ValueError("Material-color scenario mismatch")

for field in ("ActiveFade", "FadeThreshold", "OldResourceOrder", "Hdr16", "ImmediateProbes"):
    default = "false" if field in ("Hdr16", "ImmediateProbes") else ""
    if dx_scene.attrib.get(field, default) != vk_scene.attrib.get(field, default):
        raise ValueError(f"{field} scenario mismatch")

for root, scene, folder in ((args.dx11, dx_scene, "lighting-buffers"), (args.vulkan, vk_scene, "mesh-buffers")):
    if "FadeThreshold" in scene.attrib:
        frame = (root / "Reports" / folder / "frame.bin").read_bytes()
        # Authenticated stock Frame metadata: DistanceFade float32 at 1276.
        if len(frame) != 1280 or float(np.frombuffer(frame, dtype="<f4", count=1, offset=1276)[0]) != float(scene.attrib["FadeThreshold"]):
            raise ValueError("Captured frame fade threshold mismatch")

dx_quality, vk_quality = quality(args.dx11), quality(args.vulkan)
if dx_quality != vk_quality:
    raise ValueError("Pinned quality mismatch")
for scene, applied in ((dx_scene, dx_quality), (vk_scene, vk_quality)):
    if scene.attrib.get("Hdr16", "false").lower() != applied["HqTarget"].lower():
        raise ValueError("Requested HDR target quality was not applied")
dx_folder, vk_folder = args.dx11 / "Reports/lighting-buffers", args.vulkan / "Reports/mesh-buffers"
if dx_scene.attrib.get("ImmediateProbes", "false").lower() == "true":
    source = ET.parse(dx_folder / "probes.xml").getroot()
    native = ET.parse(vk_folder / "probes.xml").getroot()
    if float(source.attrib["ProbeTimeOut"]) != 0 or not int(native.attrib["ConfigFlags"]) & 16 or float(native.attrib["BlendStrength"]) != 1:
        raise ValueError("Immediate probe control was not applied on both backends")
partial = not (vk_folder / "complete.xml").is_file()
for folder in (dx_folder, vk_folder):
    if not (folder / "complete.xml").is_file() and not (folder == vk_folder and args.allow_partial_vulkan):
        raise ValueError(f"Incomplete buffer capture: {folder}")
dx = {name: np.fromfile(dx_folder / f"{name}.rgba32f", dtype="<f4").reshape(*shape, 4) for name in names}
g0 = np.fromfile(vk_folder / "color-lod.rgba8-srgb", dtype=np.uint8).reshape(*shape, 4).astype(np.float32) / 255
g0[..., :3] = np.where(g0[..., :3] <= .04045, g0[..., :3] / 12.92, ((g0[..., :3] + .055) / 1.055) ** 2.4)
packed = np.fromfile(vk_folder / "normal-ao.a2b10g10r10", dtype="<u4").reshape(shape)
# DX11's companion exports the same UNORM decode as float32. Compare equal
# decoded precision; mixed float64 division created spurious ~1e-9 differences.
g1 = np.stack(((packed & 1023) / 1023, ((packed >> 10) & 1023) / 1023,
               ((packed >> 20) & 1023) / 1023, (packed >> 30) / 3), axis=-1).astype(np.float32)
g2 = np.fromfile(vk_folder / "metal-gloss-emissive.rgba8", dtype=np.uint8).reshape(*shape, 4).astype(np.float32) / 255
depth = np.zeros((*shape, 4), dtype=np.float32)
depth[..., 0] = np.fromfile(vk_folder / "depth.d32f", dtype="<f4").reshape(shape)
depth[..., 3] = 1
vk = dict(zip(names, (g0, g1, g2, depth, np.fromfile(vk_folder / "hdr.rgba32f", dtype="<f4").reshape(*shape, 4))))
for name in ("screen-ao", "sun-visibility"):
    dx[name] = np.fromfile(dx_folder / f"{name}.r8", dtype=np.uint8).reshape(*shape, 1).astype(np.float32) / 255
    vk[name] = np.fromfile(vk_folder / f"{name}.r8", dtype=np.uint8).reshape(*shape, 1).astype(np.float32) / 255

def panels(root):
    return [{k: v for k, v in row.attrib.items() if k not in ("Id", "Model")}
            for row in ET.parse(root / "Reports/mesh-decals-assets.xml").getroot().findall("Panel")]

rows = panels(args.dx11)
if rows != panels(args.vulkan):
    raise ValueError("Panel input mismatch")
focal = shape[0] / (2 * math.tan(float(dx_quality["Graphics.FieldOfView"]) / 2))
report = []
for row in rows:
    r, c = int(row["Row"]), int(row["Column"])
    x, y = shape[1] / 2 + focal * ((c - 1.5) * 2.3) / 8, shape[0] / 2 - focal * (.6 + (1.5 - r) * 2.3) / 8
    half = .9 * focal / 8 - 8
    x0, y0, x1, y1 = map(round, (x - half, y - half, x + half, y + half))
    entry = {"Row": r, "Column": c, "Technique": row["Technique"], "TextureRole": row["TextureRole"], "OldRequested": row["OldRequested"], "Roi": [x0, y0, x1, y1], "Stages": {}}
    if "NoCull" in row:
        entry["NoCull"] = row["NoCull"]
    for name in dx:
        a, b = dx[name][y0:y1, x0:x1], vk[name][y0:y1, x0:x1]
        if not np.isfinite(a).all() or not np.isfinite(b).all():
            raise ValueError(f"Nonfinite panel {r}/{c} {name}")
        delta = np.abs(a.astype(np.float64) - b.astype(np.float64))
        entry["Stages"][name] = {"Dx11Mean": a.mean(axis=(0, 1)).tolist(), "VulkanMean": b.mean(axis=(0, 1)).tolist(), "MeanAbsoluteDelta": delta.mean(axis=(0, 1)).tolist(), "MaxAbsoluteDelta": delta.max(axis=(0, 1)).tolist()}
    report.append(entry)
result = {"Scope": "Paired held-panel decoded GBuffer/material/depth, linear HDR and screen-lighting inputs. Native raw formats are decoded explicitly; no resampling or exposure fit. Probes/frame constants are retained alongside captures for further diagnosis.", "DiagnosticPartialCapture": partial, "CaptureFailure": (args.vulkan / "Reports/fixture-failed.txt").read_text() if partial else None, "Dx11": str(args.dx11), "Vulkan": str(args.vulkan), "Panels": report}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"Output": str(args.output), "Panels": len(report), "MaterialMaxMeanDelta": np.max([p["Stages"]["metal-gloss-emissive"]["MeanAbsoluteDelta"] for p in report], axis=0).tolist()}, indent=2))
