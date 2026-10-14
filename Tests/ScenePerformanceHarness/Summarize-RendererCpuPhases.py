"""Validate renderer phase evidence; never turn instrumented scopes into FPS proof."""
import argparse
import json
import math
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('reports', type=Path)
parser.add_argument('--ordinary-reports', type=Path)
args = parser.parse_args()
read = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
reports = args.reports.resolve()
result = read(reports / 'result.json')
launch = read(reports.parents[1] / 'launch.json')
diagnostic = read(reports / 'renderer-cpu-phases.json')
data = diagnostic['Data']
assert result['Instrumented'] and diagnostic['Instrumented'] and data['Enabled']
assert (result['Width'], result['Height']) == (1920, 1080)
assert result['Frames'] == data['Frames'] and data['Frames'] >= 1000
assert data['RenderThreadId'] != 0
rows = {r['Phase']: r for r in data['Rows']}
assert len(rows) == len(data['Rows'])
assert math.isclose(rows['Frame']['Calls'], 1) and rows['Frame']['CpuAvailable']
for name, row in rows.items():
    assert row['WallMs'] >= 0 and row['Calls'] >= 0
    if name != 'Frame':
        assert not row['CpuAvailable'] and row['CpuMs'] is None, 'Short-phase OS CPU accounting is quantized and invalid for attribution.'
for name in ['PreparedActors', 'PreparedReplay', 'LegacyRigidActors', 'LegacyOpaqueParts', 'GlassParts']:
    row = rows[name]
    assert row['Calls'] > 0 and not row['CyclesAvailable'] and not row['AllocationAvailable']
assert rows['Scene']['CyclesAvailable'] and rows['NativeRecord']['CyclesAvailable']
assert data['CounterReadWallMs'] > 0
frame = read(reports / 'frame-profile.json')
native = read(reports / 'native-performance.json')
output = dict(Source=str(reports), Launch=launch, Instrumented=True,
    Frames=data['Frames'], RenderThreadId=data['RenderThreadId'],
    FrameWallMs=rows['Frame']['WallMs'], FrameCpuMs=rows['Frame']['CpuMs'],
    CounterReadWallMs=data['CounterReadWallMs'], GpuMs=frame['GpuMs'],
    Rows=list(rows.values()), Native=native,
    Interpretation='Inclusive wall scopes and render-thread CPU cycles; workers excluded. OS CPU-ms exposed only for frame aggregate. Fine actor/part buckets are wall-only. Cycles are not converted to milliseconds. No exclusive CPU-stack or instrumented FPS claim.')
if args.ordinary_reports:
    ordinary_reports = args.ordinary_reports.resolve()
    ordinary = read(ordinary_reports / 'result.json')
    ordinary_root = next(parent for parent in ordinary_reports.parents
                         if (parent / 'launch.json').is_file())
    ordinary_launch = read(ordinary_root / 'launch.json')
    assert not ordinary['Instrumented'] and (ordinary['Width'], ordinary['Height']) == (1920, 1080)
    for key in ['PluginSha256', 'NativeSha256', 'SourceCheckpointSha256']:
        assert ordinary_launch[key] == launch[key]
    for key in ['RigidBatching', 'RetainedSubmission', 'ParallelCascades', 'ParallelQueue', 'ActorLods', 'GroupOcclusion', 'WorldIndirect', 'GeometryArena', 'RetainedObjects', 'WorldRecipes', 'NewActorViews', 'ModelBatching', 'AsyncPresent', 'ActorSizeCulling', 'StockFarCascades'] + (['BulkModels'] if 'BulkModels' in result else []):
        assert ordinary[key] == result[key]
    output['OrdinaryControl'] = dict(Source=str(ordinary_reports), Launch=ordinary_launch, Result=ordinary,
        Scope='Matched policy/build/source world with diagnostics disabled; separate unpaused run, not a randomized causal before/after optimization comparison.')
path = reports / 'renderer-cpu-summary.json'
path.write_text(json.dumps(output, indent=2), encoding='utf-8')
print(f"{data['Frames']} accepted frames; frame wall/CPU {output['FrameWallMs']:.3f}/{output['FrameCpuMs']:.3f} ms")
print(f"Scene {rows['Scene']['WallMs']:.3f} ms/{rows['Scene']['CpuCycles']/1e6:.2f}M cycles; native record {rows['NativeRecord']['WallMs']:.3f} ms/{rows['NativeRecord']['CpuCycles']/1e6:.2f}M cycles")
print(path)
