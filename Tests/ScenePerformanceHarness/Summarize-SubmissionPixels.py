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
if len(rows)!=6 or not metadata["Paused"]:raise RuntimeError("Expected six paused submission captures")
if len({r["Camera"] for r in rows})!=1:raise RuntimeError("Camera moved")
for row,mode in zip(rows,expected):
    if not row["Paused"] or row["RetainedSubmissionRequested"]!=mode or row["LogicalDraws"]==0:raise RuntimeError("Submission mode not verified")
    stats=row.get("SubmissionStats")
    if stats and (stats["Enabled"]!=int(mode) or mode and (stats["Actors"]==0 or stats["Parts"]==0 or stats["ObjectBlobBytes"]==0)):raise RuntimeError("Actual submission replay not verified")
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
pairs = [("legacy-a", "legacy-b"), ("prepared-a", "prepared-b"), ("legacy-c", "legacy-d"),
         ("legacy-b", "prepared-b"), ("prepared-b", "legacy-d"), ("legacy-b", "legacy-d")]
comparisons = []
for first, second in pairs:
    for region, (x0, y0, x1, y1) in regions.items():
        delta = np.abs(arrays[first][y0:y1, x0:x1] - arrays[second][y0:y1, x0:x1])
        maximum = delta.max(axis=2)
        comparisons.append(dict(First=first, Second=second, Region=region,
            MeanAbsoluteChannelDelta=float(delta.mean()), ChangedPercent=float((maximum > 0).mean()*100),
            AboveFourPercent=float((maximum > 4).mean()*100), MaximumChannelDelta=int(delta.max())))
output = dict(Paused=True, SubmissionModesVerified=True, LogicalDraws=rows[0]["LogicalDraws"],
    PhysicalPackets=rows[0]["PhysicalPackets"], Regions=regions, Comparisons=comparisons,
    Scope="Paused saved-camera legacy/prepared/legacy RGB differences, with repeat/return controls; no FPS or full DX11 parity claim.")
(root / "pixel-delta.json").write_text(json.dumps(output, indent=2), encoding="utf-8")
print(root / "pixel-delta.json")
