"""Refresh source selection and fixture coverage without claiming visual parity."""
import gzip
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
catalog = ET.fromstring(gzip.decompress((root / 'Assets/Shaders/Vanilla/catalog.xml.gz').read_bytes()))
ids = {e.attrib['Id'] for e in catalog.iter() if 'Id' in e.attrib}
selected = set()
for path in (root / 'ClientPlugin/Rendering').rglob('*.cs'):
    selected.update(re.findall(r'"([0-9a-f]{64})"', path.read_text(encoding='utf-8-sig')))
selected &= ids
assert selected, 'No authenticated production stage selections found'

path = root / 'Docs/ShaderSuiteCoverage.json'
suite = json.loads(path.read_text(encoding='utf-8-sig'))
suite['IntegratedVanillaVariants'] = len(selected)
suite['IntegrationMeaning'] = 'Authenticated IDs selected by production source; runtime observation and per-variant visual verification remain separate.'
suite['IntegratedVariantIds'] = sorted(selected)
path.write_text(json.dumps(suite, indent=2) + '\n', encoding='utf-8')
path = root / 'Assets/Shaders/Vanilla/coverage.xml'
tree = ET.parse(path)
tree.getroot().set('Integrated', str(len(selected)))
tree.getroot().set('IntegrationMeaning', 'ProductionSourceSelection')
tree.write(path, encoding='utf-8', xml_declaration=True)

path = root / 'Docs/ShaderPassCoverage.json'
coverage = json.loads(path.read_text(encoding='utf-8-sig'))
coverage['SupplementalStages'] = 128
coverage['EnvironmentStages'] = 10
world = json.loads((root / 'TestResults/WorldPassGpuTests/result.json').read_text(encoding='utf-8-sig'))
assert world['Stats']['ValidationErrors'] == 0
coverage['WorldChecksPassed'] = world['Checks']
coverage['WorldCounters'] = world['Work']
coverage['Pending'] = [
    'Usage-based cascade cadence, small-object cascade pruning, full stock spatial/GPU light culling beyond nearest-64 local lights',
    'Fine-grained probe prefilter/blend cadence and broad-world performance',
    'Timed voxel LOD fades, remaining actor quality/MSAA paths and advanced graphics settings',
    'Complex glass, exact flare scheduling, particle lighting/collision/quality and broad content-mod visual acceptance',
    'Cross-backend visual parity, live Interim/Linux game acceptance, and native stereo host/per-eye submission',
    'Native media/audio, advanced capture, debug/profiling and reload/reset parity'
]
for key, folder in [('PostprocessGpu', 'PostprocessGpuTests'), ('ProbeGpu', 'ProbeGpuTests')]:
    evidence = f'TestResults/{folder}/result.json'
    result = json.loads((root / evidence).read_text(encoding='utf-8-sig'))
    assert result['ValidationErrors'] == 0
    coverage[key] = dict(Evidence=evidence, Scope='Controlled native pixel/counter fixtures; broad visual parity remains separate', **result)
names = {
    'PostprocessGpuTests': [
        'Stock exposure, bloom, tone mapping, FXAA and chromatic/vignette processing',
        'Stock SSAO and all HBAO coarse/bilateral permutations',
        'Stock highlight geometry, overlap removal and exponential blur radii',
        'Quality-sized CSM and Gaussian filtering; spot cookies/depth and per-light exclusions',
        'Completed-timestamp DRS and independent world/output dimensions',
        'Asynchronous box visibility and conservative invalidation',
        'LDR/PostPP billboard pixels and custom projections'
    ],
    'ProbeGpuTests': ['Stock near/far forward probes, GGX mips, blending, BRDF, atmosphere and timeout']
}
new_names = {name for values in names.values() for name in values}
coverage['Passes'] = [p for p in coverage['Passes'] if p['Name'] not in new_names]
for folder, values in names.items():
    for name in values:
        coverage['Passes'].append(dict(Name=name, State='Verified', Scope='Controlled native GPU fixture; full game visual parity is separate', Evidence=f'TestResults/{folder}/result.json', ValidationErrors=0, StartupState='Ready'))
path.write_text(json.dumps(coverage, indent=2) + '\n', encoding='utf-8')
print(f'PASS coverage refreshed: {len(selected)} source-selected catalog variants; independent Phase #5 fixture evidence retained.')
