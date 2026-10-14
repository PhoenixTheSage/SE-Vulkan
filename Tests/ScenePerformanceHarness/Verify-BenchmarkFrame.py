"""Require complete fixed-camera evidence before ordinary timing comparison."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('run', type=Path)
args = parser.parse_args()
read = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
launch = read(args.run / 'launch.json')
reports = args.run / 'GameData/Reports'
result = read(reports / 'result.json')
audit = read(reports / 'benchmark-frame.json')
assert launch['BenchmarkFrameRequested']
assert launch['BenchmarkFrameSha256'] == audit['SourceSha256']
assert audit['Frames'] == result['Frames'] >= 1000
assert not audit['CameraMismatches'] and not audit['EnvironmentMismatches']
assert not audit['SimulationPaused'] and audit['GameplayInputSuppressed']
assert audit['Target']['Camera'] == audit['ObservedCamera']
for matrix in ('ViewMatrix', 'ProjectionMatrix', 'ProjectionFarMatrix'):
    assert all(f'M{row}{column}' in audit['ObservedCamera'][matrix]
               for row in range(1, 5) for column in range(1, 5))
assert (result['Width'], result['Height']) == (1920, 1080)
assert result['WarmupSeconds'] == 45 and result['SampleSeconds'] == 30
print(f"PASS {audit['Frames']} unpaused completed frames, exact camera/environment; input SHA256 {audit['SourceSha256']}")
