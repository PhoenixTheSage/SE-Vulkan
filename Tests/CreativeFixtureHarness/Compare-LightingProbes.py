"""Inspect paired raw probe captures without resampling or a visual-pass claim."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument("dx11", type=Path)
parser.add_argument("vulkan", type=Path)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--mesh-fixture", action="store_true", help="Compare the explicit immediate-probe material control; folders must contain its actual probe captures")
args = parser.parse_args()
folders = (args.dx11, args.vulkan)
for folder, backend in zip(folders, ("Dx11", "Vulkan")):
    frame = ET.parse(folder / "complete.xml").getroot()
    mesh_capture = args.mesh_fixture and backend == "Vulkan" and frame.tag == "MeshBuffers"
    if frame.attrib["Backend"] != backend or (not mesh_capture and frame.attrib.get("Stage") != "BeforeTonemap"):
        raise ValueError("Probe capture does not accompany the requested completed lighting frame")
quality = [{row.attrib["Name"]: row.attrib["Value"] for row in ET.parse(folder.parent / "lighting-quality.xml").getroot()} for folder in folders]
if quality[0] != quality[1]:
    raise ValueError("Lighting quality differs; probe comparison does not change quality")
scene_name = "mesh-decals-assets.xml" if args.mesh_fixture else "lighting-scene.xml"
scenes = [dict(ET.parse(folder.parent / scene_name).getroot().attrib) for folder in folders]
if args.mesh_fixture:
    for scene, backend in zip(scenes, ("Dx11", "Vulkan")):
        if scene.pop("Backend") != backend or scene.get("ImmediateProbes") != "true":
            raise ValueError("Requires the paired immediate-probe material control")
if scenes[0] != scenes[1]:
    raise ValueError("Lighting scene states differ")
roots = [ET.parse(folder / "probes.xml").getroot() for folder in folders]
if args.mesh_fixture and (float(roots[0].attrib["ProbeTimeOut"]) != 0 or not int(roots[1].attrib["ConfigFlags"]) & 16 or float(roots[1].attrib["BlendStrength"]) != 1):
    raise ValueError("Immediate probe setting was not applied")
for root, backend in zip(roots, ("Dx11", "Vulkan")):
    if any(root.attrib.get(k) != v for k, v in {"Schema": "1", "Backend": backend, "Layout": "FaceMajorMipChain"}.items()):
        raise ValueError("Unsupported probe capture schema/layout")
metadata = [{row.attrib["Name"]: dict(row.attrib) for row in root} for root in roots]
roles = ("close-original", "far-original", "close-final", "far-final")
if any(len(root) != len(roles) for root in roots) or any(set(data) != set(roles) for data in metadata):
    raise ValueError("Missing or duplicate probe role")


def decode(packed):
    codes = np.stack((packed & 2047, (packed >> 11) & 2047, packed >> 22), axis=-1)
    bits = np.array([6, 6, 5])
    exponent = (codes >> bits).astype(np.int32)
    mantissa = codes & ((1 << bits) - 1)
    if (exponent == 31).any():
        raise ValueError("Nonfinite captured unsigned probe value")
    return np.where(exponent > 0, np.ldexp(1 + mantissa / (1 << bits), exponent - 15),
                    np.ldexp(mantissa.astype(np.float64), 1 - 15 - bits))


def metrics(a, b, packed_a, packed_b):
    delta = np.abs(a - b).reshape(-1, 3)
    lum_a, lum_b = [v.reshape(-1, 3) @ np.array([.2126, .7152, .0722]) for v in (a, b)]
    valid = lum_a > 1e-5
    return {"Pixels": int(lum_a.size), "ExactPackedFraction": float((packed_a == packed_b).mean()),
            "Dx11MeanRgb": a.reshape(-1, 3).mean(axis=0).tolist(), "VulkanMeanRgb": b.reshape(-1, 3).mean(axis=0).tolist(),
            "MeanAbsoluteDeltaRgb": delta.mean(axis=0).tolist(), "P95AbsoluteDeltaRgb": np.percentile(delta, 95, axis=0).tolist(),
            "MaxAbsoluteDeltaRgb": delta.max(axis=0).tolist(),
            "MedianLuminanceRatio": float(np.median(lum_b[valid] / lum_a[valid])) if valid.any() else None}


result = {"Dx11": str(args.dx11.resolve()), "Vulkan": str(args.vulkan.resolve()), "QualityMatched": True,
          "Dx11State": dict(roots[0].attrib), "VulkanState": dict(roots[1].attrib),
          "Scope": "Raw R11G11B10 probe snapshots at the completed lighting frame. Only final cubes are published lighting inputs; originals can contain an unfinished refresh at different working stages. No resampling/exposure correction or automatic parity verdict.", "Cubes": {}}
for role in roles:
    records = [data[role] for data in metadata]
    if records[0] != records[1] or records[0]["Format"] != "R11G11B10_Float" or records[0]["Faces"] != "6":
        raise ValueError("Probe shape/format differs: " + role + ": " + str(records))
    resolution, mips = int(records[0]["Resolution"]), int(records[0]["MipLevels"])
    expected = 6 * sum(max(1, resolution >> mip) ** 2 for mip in range(mips))
    packed = [np.fromfile(folder / ("probe-" + role + ".r11g11b10"), dtype="<u4") for folder in folders]
    if any(data.size != expected for data in packed):
        raise ValueError("Incomplete probe payload: " + role)
    offset = 0
    levels = [[] for _ in range(mips)]
    faces = []
    for face in range(6):
        face_result = []
        for mip in range(mips):
            size = max(1, resolution >> mip)
            pair = [data[offset:offset + size * size].reshape(size, size) for data in packed]
            values = [decode(data) for data in pair]
            face_result.append({"Mip": mip, "Size": size, **metrics(*values, *pair)})
            levels[mip].append((pair, values))
            offset += size * size
        faces.append({"Face": face, "Mips": face_result})
    aggregate = []
    for mip, entries in enumerate(levels):
        pair = [np.stack([entry[0][i] for entry in entries]) for i in range(2)]
        values = [np.stack([entry[1][i] for entry in entries]) for i in range(2)]
        aggregate.append({"Mip": mip, "Size": max(1, resolution >> mip), **metrics(*values, *pair)})
    result["Cubes"][role] = {"Metadata": records[0], "PublishedLightingInput": role.endswith("-final"), "Mips": aggregate, "Faces": faces}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"Output": str(args.output.resolve()), "QualityMatched": True,
                  "FinalProbeMips": {role: result["Cubes"][role]["Mips"] for role in ("close-final", "far-final")}}, indent=2))
