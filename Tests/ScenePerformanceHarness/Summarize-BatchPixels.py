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
expected = [16, 16, 64, 64, 16, 16]
if len(rows) != 6 or not metadata["Paused"]:
    raise RuntimeError("Expected six paused captures")
if len({r["Camera"] for r in rows}) != 1:
    raise RuntimeError("Camera moved between captures")
for row, cell in zip(rows, expected):
    if not row["Paused"] or row["RigidCellMetres"] != cell or row["ActualCellMetres"] != cell or row["LogicalDraws"] == 0:
        raise RuntimeError("Capture lacks matching actual grouping/world evidence")
if len({r["LogicalDraws"] for r in rows}) != 1:
    raise RuntimeError("Logical draw coverage changed")
narrow = [r["PhysicalPackets"] for r in rows if r["ActualCellMetres"] == 16]
wide = [r["PhysicalPackets"] for r in rows if r["ActualCellMetres"] == 64]
if max(wide) >= min(narrow):
    raise RuntimeError("No actual packet reduction observed in paused captures")
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
pairs = [("16m-a", "16m-b"), ("64m-a", "64m-b"), ("16m-c", "16m-d"),
         ("16m-b", "64m-b"), ("64m-b", "16m-d"), ("16m-b", "16m-d")]
comparisons = []
for first, second in pairs:
    for region, (x0, y0, x1, y1) in regions.items():
        delta = np.abs(arrays[first][y0:y1, x0:x1] - arrays[second][y0:y1, x0:x1])
        maximum = delta.max(axis=2)
        comparisons.append(dict(First=first, Second=second, Region=region,
            MeanAbsoluteChannelDelta=float(delta.mean()), ChangedPercent=float((maximum > 0).mean()*100),
            AboveFourPercent=float((maximum > 4).mean()*100), MaximumChannelDelta=int(delta.max())))
output = dict(Paused=True, ActualGroupingVerified=True, LogicalDraws=rows[0]["LogicalDraws"],
    NarrowPackets=narrow, WidePackets=wide, Regions=regions, Comparisons=comparisons,
    Scope="Paused saved-camera RGB differences, with same-mode/return controls; does not prove full DX11 parity or every scene.")
(root / "pixel-delta.json").write_text(json.dumps(output, indent=2), encoding="utf-8")
print(root / "pixel-delta.json")
