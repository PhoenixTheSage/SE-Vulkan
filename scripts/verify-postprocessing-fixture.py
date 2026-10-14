"""Verify recorded Phase #5 activity, settings tests and cleanup; never infer visual parity."""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--vulkan', type=Path, required=True)
parser.add_argument('--dx11', type=Path)
parser.add_argument('--output', type=Path, default=Path('TestResults/PostprocessingAcceptance.json'))
args = parser.parse_args()
reports = args.vulkan / 'Reports'
for name in ['fixture-completed.txt', 'fixture-unloaded.txt', 'exit-requested.txt', 'world-display-passed.txt', 'harness-disposed.txt']:
    assert (reports / name).exists(), f'Missing completion evidence: {name}'
assert not (reports / 'fixture-failed.txt').exists()
captures = ['phase5-highlight', 'phase5-ssao', 'fixture-overview', 'fixture-lcd-text', 'fixture-lcd-sprites', 'fixture-animation', 'fixture-player-hud', 'fixture-astronaut-walking']
for name in captures:
    data = (reports / (name + '.png')).read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II', data[16:24]) == (640, 480), f'Invalid PNG {name}'
history = (reports / 'status-history.txt').read_text(encoding='utf-8-sig')
final = (reports / 'status-latest.txt').read_text(encoding='utf-8-sig')
assert 'active: Vulkan' in final and 'enabled: 1; errors: 0' in final
assert not re.search(r'enabled: 1; errors: [1-9]', history), 'Validation errors observed'
assert re.search(r'Resources: textures=\d+, meshes=0', final)
assert 'Scene: actors=0, instances=0, models=0' in final
post_keys = ['Frames', 'Exposure', 'Histogram', 'Bloom', 'Tone', 'Fxaa', 'Ssao', 'Chromatic', 'Hbao', 'Csm']
post = tuple(map(int, re.search(r'Stock postprocessing:.*?Frames=(\d+); exposure=(\d+); histogram=(\d+); bloom=(\d+); tone=(\d+); FXAA=(\d+); SSAO=(\d+); chromatic/vignette=(\d+); HBAO=(\d+); stock CSM=(\d+)', final).groups()))
assert min(post) > 0, 'Missing postprocessing submission evidence, including live SSAO/HBAO'
probes = tuple(map(int, re.search(r'Environment probes:.*?Faces=(\d+); geometry=(\d+); GGX=(\d+); blend=(\d+); atmosphere=(\d+); published=(\d+)', final).groups()))
assert min(probes[:4]) > 0 and probes[-1] == 1
spot = tuple(map(int, re.search(r'Spotlight shadows:.*?Depth draws=(\d+); quality map creations=(\d+); cookie passes=(\d+)', final).groups()))
assert min(spot) > 0
visibility = tuple(map(int, re.search(r'Queries=(\d+); completed=(\d+); hidden=(\d+); scene culls=(\d+)', final).groups()))
assert visibility[0] > 0 and visibility[1] > 0
timestamps = int(re.search(r'timestamp samples=(\d+)', final).group(1))
assert timestamps > 0
observer = json.loads((reports / 'd3d11-observer.json').read_text(encoding='utf-8-sig'))
assert observer['readyBeforeRenderInitialization'] and not observer['attempts'] and not observer['successes']
launch = json.loads((args.vulkan / 'launch.json').read_text(encoding='utf-8-sig'))
for file, key in [('plugin.dll', 'PluginSha256'), ('Assets/Native/win-x64/se_vulkan_native.dll', 'NativeSha256')]:
    assert hashlib.sha256((args.vulkan / 'Pulsar/Legacy/Local/Vulkan' / file).read_bytes()).hexdigest().upper() == launch[key]
result = dict(Scope='Controlled Windows Legacy Creative submission, capture delivery, Display transaction and unload; full visual parity remains separate', VulkanRun=str(args.vulkan), PluginSha256=launch['PluginSha256'], NativeSha256=launch['NativeSha256'], ValidationErrors=0, Postprocess=dict(zip(post_keys, post)), Probes=dict(zip(['Faces', 'GeometryDraws', 'PrefilterPasses', 'BlendPasses', 'AtmospherePasses', 'Ready'], probes)), Spots=dict(zip(['Draws', 'Maps', 'Cookies'], spot)), Visibility=dict(zip(['Queries', 'Results', 'Hidden', 'SceneCulls'], visibility)), TimestampSamples=timestamps, Captures=captures, D3D11Observation=observer['scope'], VisualAcceptance='Separate manual comparison; counters alone do not prove parity')
if args.dx11:
    other = args.dx11 / 'Reports'
    assert (other / 'fixture-completed.txt').exists() and not (other / 'fixture-failed.txt').exists()
    for name in captures:
        assert (other / (name + '.png')).exists()
    def cameras(folder):
        return dict(re.findall(r'(phase5-[^;]+); camera=([^;]+);', (folder / 'capture-camera.txt').read_text(encoding='utf-8-sig')))
    assert cameras(reports) and cameras(reports) == cameras(other), 'Phase #5 capture camera requests differ'
    result['Dx11Run'] = str(args.dx11)
    result['MatchedCaptureCameras'] = cameras(reports)
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print('PASS recorded Phase #5 fixture evidence: ' + str(args.output))
