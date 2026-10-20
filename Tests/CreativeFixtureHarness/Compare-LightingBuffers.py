"""Inspect paired live stage dumps; reports differences, never declares visual parity."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument("dx11", type=Path, help="DX11 Reports/lighting-buffers directory")
parser.add_argument("vulkan", type=Path, help="Vulkan Reports/lighting-buffers directory")
parser.add_argument("--output", required=True, type=Path)
parser.add_argument("--allow-unpinned", action="store_true", help="Inspect historical unpinned captures; cannot establish full scene parity")
args = parser.parse_args()
names = ("color-lod", "normal-ao", "metal-gloss-emissive", "depth", "hdr")


def load(folder, backend):
    metadata = ET.parse(folder / "complete.xml").getroot()
    if metadata.attrib["Backend"] != backend or metadata.attrib["Stage"] != "BeforeTonemap":
        raise ValueError("Capture backend/stage does not match the requested comparison")
    w, h = int(metadata.attrib["Width"]), int(metadata.attrib["Height"])
    values = {}
    for name in names:
        data = np.fromfile(folder / (name + ".rgba32f"), dtype="<f4")
        if data.size != w * h * 4 or not np.isfinite(data).all():
            raise ValueError("Incomplete/nonfinite stage dump: " + str(folder / name))
        values[name] = data.reshape(h, w, 4)
    if metadata.attrib.get("LightingInputs") == "1":
        for name in ("screen-ao", "sun-visibility"):
            data = np.fromfile(folder / (name + ".r8"), dtype=np.uint8)
            if data.size != w * h:
                raise ValueError("Incomplete lighting-input dump: " + str(folder / name))
            values[name] = data.reshape(h, w, 1).astype(np.float32) / 255
    if metadata.attrib.get("PostprocessInputs") == "1":
        for name in ("pre-fxaa", "post-fxaa"):
            data = np.fromfile(folder / (name + ".rgba8"), dtype=np.uint8)
            if data.size != w * h * 4:
                raise ValueError("Incomplete same-frame postprocess dump: " + str(folder / name))
            values[name] = data.reshape(h, w, 4).astype(np.float32) / 255
    return metadata.attrib, values


dx_meta, dx = load(args.dx11, "Dx11")
vk_meta, vk = load(args.vulkan, "Vulkan")
lighting_inputs_comparable = "screen-ao" in dx and "screen-ao" in vk
if lighting_inputs_comparable:
    names += ("screen-ao", "sun-visibility")
postprocess_inputs_comparable = "pre-fxaa" in dx and "pre-fxaa" in vk
if postprocess_inputs_comparable:
    names += ("pre-fxaa", "post-fxaa")
def quality(folder):
    path = folder.parent / "lighting-quality.xml"
    if not path.exists():
        return None
    root = ET.parse(path).getroot()
    values = {item.attrib["Name"]: item.attrib["Value"] for item in root}
    required = {"HqTarget", "HqDepth", "DRScaling", "AntialiasingMode", "AmbientOcclusionEnabled", "ShadowQuality", "ShadowGPUQuality", "ShadowMemoryQuality", "TextureQuality", "VoxelTextureQuality", "AnisotropicFiltering", "ModelQuality", "VoxelQuality", "VoxelShaderQuality", "AlphaMaskedShaderQuality", "AtmosphereShaderQuality", "LightsQuality", "ParticleQuality", "GrassDrawDistance", "GrassDensityFactor", "DistanceFade", "Graphics.FieldOfView", "Graphics.FlaresIntensity", "Graphics.PostProcessingEnabled"}
    if root.tag != "LightingQuality" or root.attrib.get("Schema") != "1" or not required.issubset(values):
        raise ValueError("Incomplete lighting-quality evidence: " + str(path))
    return values
dx_quality, vk_quality = quality(args.dx11), quality(args.vulkan)
quality_matched = dx_quality is not None and dx_quality == vk_quality
def scene_state(folder):
    path = folder.parent / "lighting-scene.xml"
    return ET.parse(path).getroot().attrib if path.exists() else {"TurbineVisible": "true"}
dx_scene, vk_scene = scene_state(args.dx11), scene_state(args.vulkan)
if dx_scene != vk_scene:
    raise ValueError("Lighting scene states differ; static isolation requires a matched pair")
if not quality_matched and not args.allow_unpinned:
    raise ValueError("Render-quality evidence is missing or differs. Capture a new pinned pair; --allow-unpinned is for historical diagnostics only.")
if (dx_meta["Width"], dx_meta["Height"]) != (vk_meta["Width"], vk_meta["Height"]):
    raise ValueError("Viewport dimensions differ; comparison does not resample")
foreground = (dx["depth"][:, :, 0] > 0) & (vk["depth"][:, :, 0] > 0)
depth_match = foreground & (np.abs(dx["depth"][:, :, 0] - vk["depth"][:, :, 0]) <= 1e-6)
depth_bits_match = foreground & (dx["depth"][:, :, 0].view("<u4") == vk["depth"][:, :, 0].view("<u4"))
normal_match = depth_match & (np.max(np.abs(dx["normal-ao"][:, :, :2] - vk["normal-ao"][:, :, :2]), axis=2) <= .002)
material_exact = depth_match.copy()
for name in ("color-lod", "normal-ao", "metal-gloss-emissive"):
    material_exact &= np.max(np.abs(dx[name][:, :, :3] - vk[name][:, :, :3]), axis=2) < 1e-5
dx_lod = np.rint(dx["color-lod"][:, :, 3] * 255).astype(np.int32)
vk_lod = np.rint(vk["color-lod"][:, :, 3] * 255).astype(np.int32)
lod_match = foreground & (dx_lod == vk_lod)


def metrics(mask):
    if not mask.any():
        return {"Pixels": 0}
    result = {"Pixels": int(mask.sum())}
    for name in names:
        a, b = dx[name][mask].astype(np.float64), vk[name][mask].astype(np.float64)
        delta = np.abs(a - b)
        result[name] = {"Dx11Mean": a.mean(axis=0).tolist(), "VulkanMean": b.mean(axis=0).tolist(),
                        "MeanAbsoluteDelta": delta.mean(axis=0).tolist(), "MaxAbsoluteDelta": delta.max(axis=0).tolist(),
                        "P95AbsoluteDelta": np.percentile(delta, 95, axis=0).tolist()}
    weights = np.array([.2126, .7152, .0722])
    a, b = dx["hdr"][mask, :3] @ weights, vk["hdr"][mask, :3] @ weights
    valid = a > .01
    result["HdrLuminance"] = {"Dx11Mean": float(a.mean()), "VulkanMean": float(b.mean()),
                              "MedianVulkanOverDx11": float(np.median(b[valid] / a[valid])) if valid.any() else None}
    return result


result = {"Dx11": dx_meta, "Vulkan": vk_meta, "QualityMatched": quality_matched, "LightingInputsComparable": lighting_inputs_comparable, "PostprocessInputsComparable": postprocess_inputs_comparable, "Dx11Quality": dx_quality, "VulkanQuality": vk_quality, "SceneState": dx_scene,
          "Scope": "Decoded live stage data, no resampling or exposure correction. Reports all shared foreground and subsets with depth/encoded-normal agreement; not a pass/fail oracle.",
          "LightingInputScope": "Lighting-input subsets compare captured screen AO and sun visibility only. They do not establish equality of point/spot buffers, probe texels, glass, decals, particles or volume state.",
          "DepthMatchTolerance": 1e-6, "EncodedNormalMatchTolerance": .002,
          "SharedForeground": metrics(foreground), "DepthAligned": metrics(depth_match), "DepthAndNormalAligned": metrics(normal_match),
          "CapturedMaterialExact": metrics(material_exact)}
result["AuthoredLod"] = {"Scope": "GBuffer0 alpha decoded as old-pipeline authored LOD or newer-pipeline marker 255 over shared foreground; background clear values excluded.",
                          "SharedForegroundPixels": int(foreground.sum()), "ExactPixels": int(lod_match.sum()),
                          "Dx11Histogram": {str(int(k)): int(v) for k, v in zip(*np.unique(dx_lod[foreground], return_counts=True))},
                          "VulkanHistogram": {str(int(k)): int(v) for k, v in zip(*np.unique(vk_lod[foreground], return_counts=True))}}
result["CapturedMaterialAndLodExact"] = metrics(material_exact & lod_match)
# Keep the historical tolerance-based subsets above, and expose an independent
# strict subset for attributing lighting differences to identical raster inputs.
gbuffer_bits_match = depth_bits_match.copy()
for name in ("color-lod", "normal-ao", "metal-gloss-emissive"):
    gbuffer_bits_match &= np.all(dx[name].view("<u4") == vk[name].view("<u4"), axis=2)
result["DepthBitExact"] = metrics(depth_bits_match)
result["CapturedGbufferBitExact"] = metrics(gbuffer_bits_match)
result["PipelineSubsets"] = {}
for label, pipeline_mask in (("Old", foreground & (dx_lod != 255) & (vk_lod != 255)),
                             ("New", foreground & (dx_lod == 255) & (vk_lod == 255))):
    result["PipelineSubsets"][label] = {"SharedForegroundPixels": int(pipeline_mask.sum()),
                                       "DepthAlignedPixels": int((pipeline_mask & depth_match).sum()),
                                       "DepthAndNormalAlignedPixels": int((pipeline_mask & normal_match).sum()),
                                       "CapturedMaterialExactPixels": int((pipeline_mask & material_exact).sum()),
                                       "DepthAndNormalAligned": metrics(pipeline_mask & normal_match)}
if lighting_inputs_comparable:
    lighting_exact = material_exact.copy()
    for name in ("screen-ao", "sun-visibility"):
        lighting_exact &= np.abs(dx[name][:, :, 0] - vk[name][:, :, 0]) < 1e-5
    result["CapturedMaterialAndLightingInputsExact"] = metrics(lighting_exact)
    lighting_bits_match = gbuffer_bits_match.copy()
    for name in ("screen-ao", "sun-visibility"):
        lighting_bits_match &= np.all(dx[name].view("<u4") == vk[name].view("<u4"), axis=2)
    result["CapturedGbufferAndLightingInputsBitExact"] = metrics(lighting_bits_match)
    # read_gbuffer samples screen AO at integer-pixel UVs with LinearSampler,
    # so the effective input spans this pixel and its left/upper neighbors.
    ao_equal = np.all(dx["screen-ao"].view("<u4") == vk["screen-ao"].view("<u4"), axis=2)
    padded = np.pad(ao_equal, ((1, 0), (1, 0)), mode="edge")
    ao_footprint_equal = padded[1:, 1:] & padded[:-1, 1:] & padded[1:, :-1] & padded[:-1, :-1]
    result["CapturedGbufferAndLightingFootprintsBitExact"] = metrics(lighting_bits_match & ao_footprint_equal)
    stage_path = args.vulkan / "lighting-stages.xml"
    if stage_path.exists():
        stages = ET.parse(stage_path).getroot()
        # Schema 2 includes preceding local lights in after-directional. Schema
        # 1 would classify local lighting as a later change, so do not mix them.
        if stages.get("Schema") == "2" and stages.get("RenderOrder") == "points,spots,directional":
            before = np.fromfile(args.vulkan / "after-directional.rgba32f", dtype="<f4")
            if before.size != vk["hdr"].size or not np.isfinite(before).all():
                raise ValueError("Incomplete/nonfinite native directional-stage snapshot")
            before = before.reshape(vk["hdr"].shape)
            changed = np.any(before[:, :, :3].view("<u4") != vk["hdr"][:, :, :3].view("<u4"), axis=2)
            matched = lighting_bits_match & ao_footprint_equal
            subsets = {}
            for label, mask in (("Unchanged", matched & ~changed), ("Changed", matched & changed)):
                delta = np.abs(dx["hdr"][mask, :3].astype(np.float64) - vk["hdr"][mask, :3])
                subsets[label] = {"Pixels": int(mask.sum()),
                                  "HdrMeanAbsoluteDelta": delta.mean(axis=0).tolist() if mask.any() else None,
                                  "HdrMaxAbsoluteDelta": delta.max(axis=0).tolist() if mask.any() else None}
            total = np.abs(dx["hdr"][:, :, :3].astype(np.float64) - vk["hdr"][:, :, :3])
            all_error, changed_error = total[matched].sum(axis=0), total[matched & changed].sum(axis=0)
            result["VulkanLaterHdrChanges"] = {
                "Scope": "Strict captured-input footprint subset, grouped by whether Vulkan HDR changes after completed local/directional lighting. Later changes include transparency and guest composition. This does not establish DX11 later-stage input equality or identify which backend is wrong.",
                "Subsets": subsets,
                "PairwiseErrorFractionOnChangedPixels": [float(b / a) if a else None for a, b in zip(all_error, changed_error)]}
flare_paths = [folder / "flares.xml" for folder in (args.dx11, args.vulkan)]
if all(path.exists() for path in flare_paths):
    flare_roots = [ET.parse(path).getroot() for path in flare_paths]
    flare_rows = [[dict(row.attrib) for row in root] for root in flare_roots]
    pairs, unmatched = [], []
    available = list(flare_rows[1])
    for a in flare_rows[0]:
        candidates = [b for b in available if a["Type"] == b["Type"] and
                      sum((float(a[c]) - float(b[c])) ** 2 for c in ("X", "Y", "Z")) <= .01 ** 2]
        if len(candidates) != 1:
            unmatched.append(a)
            continue
        b = candidates[0]
        available.remove(b)
        pair = {"Dx11": a, "Vulkan": b}
        if a["Occlusion"] != "Pending" and b["Occlusion"] != "Pending":
            pair["OcclusionAbsoluteDelta"] = abs(float(a["Occlusion"]) - float(b["Occlusion"]))
        pairs.append(pair)
    result["Flares"] = {"Scope": "Actual CPU glare/query state at capture. Paired by type and world position within 1cm; owner IDs differ between renderer routes. Does not establish transparent-input equality.",
                        "Dx11State": dict(flare_roots[0].attrib), "VulkanState": dict(flare_roots[1].attrib),
                        "Pairs": pairs, "UnmatchedDx11": unmatched, "UnmatchedVulkan": available}
# Installed MyHBAO.GlobalConstantBuffer: named fields exclude explicit padding.
# Retain CPU matrix storage separately instead of silently transposing an API.
hbao_fields = (("InvQuarterResolution", 0, 2), ("InvFullResolution", 8, 2),
               ("UVToViewA", 16, 2), ("UVToViewB", 24, 2),
               ("RadiusToScreen", 32, 1), ("R2", 36, 1), ("NegInvR2", 40, 1),
               ("NDotVBias", 44, 1), ("SmallScaleAOAmount", 48, 1),
               ("LargeScaleAOAmount", 52, 1), ("PowExponent", 56, 1),
               ("BlurViewDepth0", 64, 1), ("BlurViewDepth1", 68, 1),
               ("BlurSharpness0", 72, 1), ("BlurSharpness1", 76, 1),
               ("LinearizeDepthA", 80, 1), ("LinearizeDepthB", 84, 1),
               ("InverseDepthRangeA", 88, 1), ("InverseDepthRangeB", 92, 1),
               ("InputViewportTopLeft", 96, 2), ("ViewDepthThresholdNegInv", 104, 1),
               ("ViewDepthThresholdSharpness", 108, 1), ("BackgroundAORadiusPixels", 112, 1),
               ("ForegroundAORadiusPixels", 116, 1), ("NormalDecodeScale", 192, 1),
               ("NormalDecodeBias", 196, 1))
hbao_paths = [folder / "hbao-constants.bin" for folder in (args.dx11, args.vulkan)]
if all(path.exists() for path in hbao_paths):
    blocks = [np.fromfile(path, dtype="<f4") for path in hbao_paths]
    if any(block.size != 52 or not np.isfinite(block).all() for block in blocks):
        raise ValueError("Incomplete/nonfinite HBAO global constants")
    fields = {}
    for name, offset, count in hbao_fields:
        a, b = [block[offset // 4:offset // 4 + count].astype(np.float64) for block in blocks]
        fields[name] = {"Dx11": a.tolist(), "Vulkan": b.tolist(), "AbsoluteDelta": np.abs(a - b).tolist()}
    result["HbaoConstants"] = {"Fields": fields,
                               "DebugNormalComponent": [int(block.view("<i4")[30]) for block in blocks],
                               "NormalMatrixCpuStorage": [block[32:48].reshape(4, 4).tolist() for block in blocks]}

dx_png, vk_png = args.dx11.parent / "anomaly-volume-off.png", args.vulkan.parent / "anomaly-volume-off.png"
if dx_png.exists() and vk_png.exists():
    from PIL import Image
    a, b = np.asarray(Image.open(dx_png).convert("RGB")), np.asarray(Image.open(vk_png).convert("RGB"))
    expected_shape = (*foreground.shape, 3)
    if a.shape != expected_shape or b.shape != expected_shape:
        raise ValueError("Final screenshot dimensions differ from the stage viewport; comparison does not resample")
    # The toolbar/HUD overlaps scene geometry near the bottom of this fixture.
    # The screenshots are asynchronous; this is diagnostic evidence, not an
    # assertion that their pixels came from the exact raw-stage capture frame.
    above_hud = np.indices(foreground.shape)[0] < int(foreground.shape[0] * .83)
    screenshot_metrics = {}
    for name, mask in (("DepthAndNormalAligned", normal_match), ("CapturedMaterialExact", material_exact)):
        mask = mask & above_hud
        if not mask.any():
            continue
        delta = np.abs(a[mask].astype(np.int16) - b[mask].astype(np.int16))
        screenshot_metrics[name] = {"Pixels": int(mask.sum()), "MeanAbsoluteDeltaRgb8": delta.mean(axis=0).tolist(),
                                    "P95AbsoluteDeltaRgb8": np.percentile(delta, 95, axis=0).tolist(),
                                    "MaxAbsoluteDeltaRgb8": delta.max(axis=0).tolist(),
                                    "ExactRgbFraction": float((delta.max(axis=1) == 0).mean()),
                                    "WithinOneRgbFraction": float((delta.max(axis=1) <= 1).mean())}
    result["FinalSrgbScreenshot"] = {"Scope": "Asynchronously captured final sRGB images at the held camera, using stage-aligned masks above the HUD; no resampling/exposure correction or automatic pass claim.",
                                     "Dx11": str(dx_png), "Vulkan": str(vk_png), "Subsets": screenshot_metrics}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"Output": str(args.output.resolve()), "QualityMatched": quality_matched, "LightingInputsComparable": lighting_inputs_comparable, "SharedForeground": int(foreground.sum()),
                  "DepthAligned": int(depth_match.sum()), "DepthAndNormalAligned": int(normal_match.sum()),
                  "HdrLuminance": result["DepthAndNormalAligned"].get("HdrLuminance")}, indent=2))
