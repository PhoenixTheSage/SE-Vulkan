"""Compare paused captures, verifying actual grouping and full-size geometry."""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("reports", type=Path)
args = parser.parse_args()
root = args.reports
metadata = json.loads((root / "pixel-comparison.json").read_text(encoding="utf-8-sig"))
rows = metadata["Requests"]
expected = [False,False,True,True,False,False]
if len(rows)!=6 or not metadata["Paused"]:raise RuntimeError("Expected six paused light-list captures")
if len({r["Camera"] for r in rows})!=1:raise RuntimeError("Camera moved")
for row,mode in zip(rows,expected):
    t=row["LightTileStats"]
    if not row["Paused"] or row["LightTilesRequested"]!=mode or t["Enabled"]!=int(mode) or row["LogicalDraws"]==0:raise RuntimeError("Light-list mode not verified")
    if mode and (t["Dispatches"]!=1 or t["HaveResult"]!=1 or t["Assignments"]>=t["FullAssignments"] or t["CompletedFrame"]>=t["Frame"]):raise RuntimeError("Completed light selection/result epoch unverified")
if len({r["LogicalDraws"] for r in rows})!=1:raise RuntimeError("Logical draw coverage changed")
if len({r["PhysicalPackets"] for r in rows})!=1:raise RuntimeError("Physical draw coverage changed")
resolution = metadata["Resolution"]
if (resolution["Width"], resolution["Height"], resolution["OutputWidth"], resolution["OutputHeight"], resolution["Fraction"]) != (1920, 1080, 1920, 1080, 1):
    raise RuntimeError("Full 1080p world/output required")
arrays = {}
for row in rows:
    image = np.asarray(Image.open(root / row["File"]).convert("RGB"), dtype=np.int16)
    if image.shape != (1080, 1920, 3):
        raise RuntimeError("Capture dimensions mismatch")
    arrays[Path(row["File"]).stem] = image
regions = {"FullImage": (0, 0, 1920, 1080), "WorldRegion": (0, 90, 1920, 840), "UpperWall": (650, 175, 800, 380)}
pairs = [("full-a", "full-b"), ("gpu-a", "gpu-b"), ("full-c", "full-d"),
         ("full-b", "gpu-b"), ("gpu-b", "full-d"), ("full-b", "full-d")]
comparisons = []
for first, second in pairs:
    for region, (x0, y0, x1, y1) in regions.items():
        delta = np.abs(arrays[first][y0:y1, x0:x1] - arrays[second][y0:y1, x0:x1])
        maximum = delta.max(axis=2)
        comparisons.append(dict(First=first, Second=second, Region=region,
            MeanAbsoluteChannelDelta=float(delta.mean()), ChangedPercent=float((maximum > 0).mean()*100),
            AboveFourPercent=float((maximum > 4).mean()*100), MaximumChannelDelta=int(delta.max())))
output = dict(Paused=True, ActualLightSelectionVerified=True, LogicalDraws=rows[0]["LogicalDraws"],
    PhysicalPackets=rows[0]["PhysicalPackets"], Regions=regions, Comparisons=comparisons,
    Scope="Paused saved-camera full/GPU/full RGB differences, with repeat/return controls; no FPS or full DX11 parity claim.")
(root / "pixel-delta.json").write_text(json.dumps(output, indent=2), encoding="utf-8")
print(root / "pixel-delta.json")
