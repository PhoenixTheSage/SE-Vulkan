"""Compare native bound-input lighting terms to a captured-input stock DX11 replay.

This diagnoses foreground shader terms; it does not certify complete rendering parity.
"""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import numpy as np

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("capture", type=Path)
parser.add_argument("replay", type=Path, help="CapturedAmbient --terms result folder (WARP or hardware)")
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
meta = ET.parse(args.capture / "complete.xml").getroot()
stages = ET.parse(args.capture / "lighting-stages.xml").getroot()
if meta.get("Backend") != "Vulkan" or meta.get("Stage") != "BeforeTonemap" or meta.get("LightingInputs") != "1":
    raise ValueError("Expected a complete native lighting-input capture")
w, h = int(meta.get("Width")), int(meta.get("Height"))
if stages.get("Backend") != "Vulkan" or int(stages.get("Width")) != w or int(stages.get("Height")) != h:
    raise ValueError("Lighting term dimensions/backend do not match the capture")
if stages.get("Schema") not in {"1", "2"} or (stages.get("Schema") == "2" and stages.get("RenderOrder") != "points,spots,directional"):
    raise ValueError("Unknown lighting-stage render-order contract")
scope = dict(line.split("=", 1) for line in (args.replay / "scope.txt").read_text().splitlines() if "=" in line)
if Path(scope["Input"]).resolve() != args.capture.resolve() or int(scope["Width"]) != w or int(scope["Height"]) != h:
    raise ValueError("Replay inputs do not belong to this capture")
if scope["SwapCloseFar"] != "False" or scope["LinearCubeSampler"] != "False" or len(scope["Planes"].split(",")) != 8:
    raise ValueError("Expected the stock sampler, unswapped, eight-plane replay")

def read(name):
    data = np.fromfile(args.capture / (name + ".rgba32f"), dtype="<f4")
    if data.size != h * w * 4:
        raise ValueError("Incomplete term dump: " + name)
    return data.reshape(h, w, 4)

raw = np.fromfile(args.replay / "stock-ambient.rgba32f", dtype="<f4")
if raw.size != 8 * h * w * 4:
    raise ValueError("Incomplete eight-plane WARP replay")
warp = raw.reshape(8, h, w, 4)
mask = read("depth")[:, :, 0] > 0
if not mask.any():
    raise ValueError("Capture has no foreground pixels")
tracked = [(216, 255), (200, 243), (184, 252), (403, 226), (191, 247)]

def compare(actual, reference):
    a, b = actual[mask], reference[mask]
    if not np.isfinite(a).all() or not np.isfinite(b).all():
        raise ValueError("Nonfinite foreground shader values")
    delta = np.abs(a.astype(np.float64) - b.astype(np.float64))
    return {"MeanAbsolute": delta.mean(axis=0).tolist(), "P95Absolute": np.percentile(delta, 95, axis=0).tolist(),
            "MaxAbsolute": delta.max(axis=0).tolist(),
            "Tracked": [{"Pixel": [x, y], "Native": actual[y, x].tolist(), "Reference": reference[y, x].tolist()}
                        for x, y in tracked if x < w and y < h and mask[y, x]]}

view_ao = warp[5].copy()
view_ao[:, :, 3] = warp[0, :, :, 3]
pairs = [("normal-depth", warp[4]), ("view-ao", view_ao), ("ibl-lod", warp[2]),
         ("brdf-gloss-nv", warp[3]), ("diffuse-ao", warp[6]), ("direct-shadow", warp[7]),
         ("fogged-light-ao", warp[0, :, :, :3])]
result = {"Capture": str(args.capture.resolve()), "Replay": str(args.replay.resolve()), "ReferenceDriver": scope.get("Driver", "Warp"), "ReferenceAdapter": scope.get("Adapter"), "ForegroundPixels": int(mask.sum()),
          "Scope": "Foreground term diagnosis; not complete renderer equality.", "Terms": {}}
for name, reference in pairs:
    native = read("term-" + name)[:, :, :reference.shape[2]]
    result["Terms"][name] = compare(native, reference)
prediction = read("term-fogged-light-ao")[:, :, :3]
if stages.get("RenderOrder") == "points,spots,directional":
    prediction = prediction + read("after-spots")[:, :, :3]
result["RenderOrder"] = stages.get("RenderOrder", "directional,points,spots")
result["RecompositionVsActualDirectional"] = compare(prediction, read("after-directional")[:, :, :3])
result["CompletedHdrMatchesPreTonemapBits"] = bool(np.array_equal(read("end-frame-hdr").view(np.uint32), read("hdr").view(np.uint32)))
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, indent=2))
for name, item in result["Terms"].items():
    print(name + ": mean absolute=" + str(item["MeanAbsolute"]) + "; max=" + str(item["MaxAbsolute"]))
print("Completed HDR matches pre-tonemap bits:", result["CompletedHdrMatchesPreTonemapBits"])
print("Diagnostic report:", args.output)
