"""Authenticate isolated Windows media evidence; does not certify Linux or pixels."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import re

parser = argparse.ArgumentParser()
parser.add_argument('run', type=Path)
parser.add_argument('--soak', action='store_true')
parser.add_argument('--menu-soak', action='store_true', help='Require 600 seconds of actual menu-video observation')
parser.add_argument('--skip', action='store_true')
parser.add_argument('--intro', action='store_true')
parser.add_argument('--display', action='store_true')
args = parser.parse_args()
run = args.run.resolve()
reports = run / 'Reports'
launch = json.loads((run / 'launch.json').read_text(encoding='utf-8-sig'))
assert (reports / 'media-passed.txt').is_file(), 'Media assertions did not pass'
assert not (reports / 'media-failed.txt').exists(), 'Media assertion failure recorded'
for marker in ('exit-requested.txt', 'harness-disposed.txt', 'display-cancel-passed.txt'):
    assert (reports / marker).is_file(), f'Missing {marker}'
assert (reports / 'process-exit.txt').read_text().strip() == '0', 'Exit was not clean'
assert not (run / 'GameData/Storage/Vulkan.cfg.startup').exists(), 'Startup journal remains'
if args.skip:
    assert (reports / 'media-skip-passed.txt').is_file(), 'Skip action incomplete'
if args.intro or args.skip:
    assert (reports / 'media-intro-observed.txt').is_file(), 'KSH was not observed'
    closed = (reports / 'media-intro-closed.txt').read_text()
    assert ('Cancelled='+str(args.skip)) in closed, 'KSH closing action did not match the test'
if args.display:
    assert (reports / 'display-transactions-passed.txt').is_file(), 'Display transactions incomplete'
history = (reports / 'status-history.txt').read_text(encoding='utf-8-sig')
errors = re.findall(r'validation available: 1; enabled: 1; errors: (\d+)', history)
assert errors and all(int(value) == 0 for value in errors), 'Validation unavailable or errors recorded'
assert not re.search(r'^\d{4}-[^\n]*Video \d+:', history, re.M), 'Media failure recorded'
local = run / 'Pulsar' / launch['Edition'] / 'Local/Vulkan'
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest().upper()
assert digest(local / 'plugin.dll') == launch['ManagedSha256'], 'Managed snapshot changed'
assert digest(local / 'Assets/Native/win-x64/se_vulkan_native.dll') == launch['NativeSha256'], 'Native snapshot changed'
runtime_hash = json.loads((reports / 'media-runtime-hash.json').read_text(encoding='utf-8-sig'))['Hash']
assert digest(local / 'Assets/Media/win-x64/manifest.json') == runtime_hash, 'Media runtime changed'
result = (reports / 'media-result.txt').read_text()
measurements = {key.strip(): float(value) for key, value in re.findall(r'([^;=]+)=(-?[\d.]+)', result)}
assert measurements['steady source-cadence ratio'] >= .98
assert measurements['real audio samples'] > 0, 'No actual audio backend observed'
assert measurements['max observed A/V offset us'] <= 100000
if args.soak or args.menu_soak:
    assert measurements['Duration'] >= 600
    assert measurements['steady private-byte delta'] <= 32 * 1024 * 1024
    assert measurements['texture range'] <= 4
rows = list(csv.DictReader((reports / 'media-samples.csv').open(newline='')))
last_intro = max((float(row['elapsed_seconds']) for row in rows if row['source'].lower() == 'ksh.wmv'), default=0)
menu_rows = [row for row in rows if row['source'].lower().startswith('background') and float(row['elapsed_seconds']) > last_intro]
menu_seconds = float(menu_rows[-1]['elapsed_seconds']) - float(menu_rows[0]['elapsed_seconds']) if menu_rows else 0
if args.menu_soak:
    assert menu_seconds >= 600, f'Actual menu observation is only {menu_seconds} seconds'
for name in ('media-first-frame.png', 'media-menu-frame.png', 'menu-native.png'):
    assert (reports / name).read_bytes().startswith(b'\x89PNG\r\n\x1a\n'), f'Missing PNG: {name}'
observed = {}
pattern = r'Video (\d+): (\d+)x(\d+); frames=(\d+); dropped=(\d+); output owner CPU us=(\d+); upload CPU us=(\d+)'
for match in re.finditer(pattern, history):
    player, width, height, frames, dropped, owner, upload = map(int, match.groups())
    observed[player] = {'width': width, 'height': height, 'frames': frames, 'dropped': dropped,
                        'output_owner_cpu_us': owner, 'upload_cpu_us': upload}
receipt = {'run': str(run), 'edition': launch['Edition'], 'managed_sha256': launch['ManagedSha256'],
           'native_sha256': launch['NativeSha256'], 'runtime_manifest_sha256': runtime_hash,
           'measurements': measurements, 'last_observed_players': observed,
           'validation_errors': 0, 'clean_exit': True, 'skip_action': args.skip,
           'stock_intro_observed': args.intro or args.skip,
           'display_transactions': args.display, 'ten_minute_overall_soak': args.soak,
           'menu_observation_seconds': menu_seconds, 'ten_minute_menu_soak': args.menu_soak,
           'limits': ['Reported player A/V timing, not an acoustic measurement',
                      'Owner CPU excludes decoder/filter threads; upload CPU excludes GPU transfer duration',
                      'Matched DX11 pixels and live Linux require separate acceptance']}
(reports / 'media-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps(receipt, indent=2))
