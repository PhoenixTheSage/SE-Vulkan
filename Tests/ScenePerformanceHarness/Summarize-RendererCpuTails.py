"""Validate full-frame tail attribution from accepted diagnostic frames only."""
import argparse
import json
import math
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('reports', type=Path)
args = parser.parse_args()
read = lambda name: json.loads((args.reports / name).read_text(encoding='utf-8-sig'))
result, phases, tails = read('result.json'), read('renderer-cpu-phases.json'), read('renderer-cpu-tails.json')
assert result['Instrumented'] and phases['Instrumented'] and tails['Instrumented']
p, t = phases['Data'], tails['Data']
assert p['Enabled'] and t['Enabled'] and not t['DroppedSamples']
assert t['Frames'] == p['Frames'] == result['Frames'] >= 1000
assert [row['Phase'] for row in p['Rows']] == [row['Phase'] for row in t['Rows']]
assert 0 <= t['GcTaggedFrames'] <= t['Frames']
for average, distribution in zip(p['Rows'], t['Rows']):
    assert math.isclose(average['WallMs'], distribution['MeanMs'], rel_tol=1e-10, abs_tol=1e-10)
    assert distribution['P99Ms'] >= distribution['P95Ms'] >= 0
    assert distribution['MeanOnSlowest5PercentMs'] >= 0
previous = math.inf
names = [row['Phase'] for row in t['Rows']]
for row in t['Slowest']:
    frame = row['Frame']
    assert frame['FrameWallMs'] <= previous
    previous = frame['FrameWallMs']
    assert len(row['PhaseWallMs']) == len(names)
    assert row['PhaseWallMs'][names.index('Frame')] == frame['FrameWallMs']
    assert math.isfinite(frame['CompletedDrawMs']) and frame['CompletedDrawMs'] >= 0
    assert frame['Gen0Collections'] >= frame['Gen1Collections'] >= frame['Gen2Collections'] >= 0
print(f"Validated {t['Frames']} completed diagnostic frames, zero dropped; {t['GcTaggedFrames']} overlap process GC collections.")
for row in sorted(t['Rows'], key=lambda row: row['MeanMs'], reverse=True)[:12]:
    print(f"{row['Phase']}: mean {row['MeanMs']:.3f}, own P95 {row['P95Ms']:.3f}, mean on full-frame tail {row['MeanOnSlowest5PercentMs']:.3f} ms (inclusive)")
