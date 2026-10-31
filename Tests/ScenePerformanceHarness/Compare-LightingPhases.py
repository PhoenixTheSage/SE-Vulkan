"""Compare a LightingReconciliation run: control vs control-return (in-process
determinism) and control vs treatment, per written artifact. Prints changed
element counts and max/mean absolute deltas; optional amplified view diff.
Split-mode runs (Compare-GpuOwnership.ps1 -Split) write only view.png; it is
compared as RGB pixels."""
import array
import os
import struct
import sys

root = os.path.join(sys.argv[1], 'GameData', 'Reports', 'Lighting')
out = sys.argv[2] if len(sys.argv) > 2 else None
phases = ['control', 'treatment', 'control-return']

def load(phase, name):
    with open(os.path.join(root, phase, name), 'rb') as f:
        return f.read()

def half(h):
    return struct.unpack('<e', struct.pack('<H', h))[0]

def values(name, data):
    if name.endswith('.f32'):
        return array.array('f', data)
    if name.endswith('.f16'):
        raw = array.array('H', data)
        return [half(v) for v in raw]
    return data  # rgba8

def compare(name, a, b):
    if a == b:
        return 'identical'
    va, vb = values(name, a), values(name, b)
    n = min(len(va), len(vb))
    diffs = [abs(va[i] - vb[i]) for i in range(n) if va[i] != vb[i] and not (va[i] != va[i] and vb[i] != vb[i])]
    if not diffs:
        return 'identical values'
    return f'changed {len(diffs)}/{n} ({100 * len(diffs) / n:.3f}%) max {max(diffs):.4g} mean {sum(diffs) / len(diffs):.4g}'

names = sorted(set(os.listdir(os.path.join(root, 'control'))))
names = [x for x in names if x.endswith(('.f32', '.f16', '.rgba8', '.sig', '.bin'))]
for name in names:
    try:
        c, t, r = (load(p, name) for p in phases)
    except FileNotFoundError:
        print(f'{name:18} missing in a phase')
        continue
    print(f'{name:18} return: {compare(name, c, r):60} treatment: {compare(name, c, t)}')
def png(phase):
    from PIL import Image
    return Image.open(os.path.join(root, phase, 'view.png')).convert('RGB')
if 'view.rgba8' not in names and os.path.exists(os.path.join(root, 'control', 'view.png')):
    c, t, r = (png(p).tobytes() for p in phases)
    print(f'{"view.png":18} return: {compare("view.rgba8", c, r):60} treatment: {compare("view.rgba8", c, t)}')
if out:
    from PIL import Image, ImageChops
    imgs = [Image.frombytes('RGBA', (1920, 1080), load(p, 'view.rgba8')).convert('RGB') if 'view.rgba8' in names else png(p) for p in phases]
    ImageChops.difference(imgs[0], imgs[1]).point(lambda v: min(255, v * 16)).save(out)
