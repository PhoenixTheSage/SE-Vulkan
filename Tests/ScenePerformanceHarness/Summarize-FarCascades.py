"""Validate the installed old-pipeline far-cascade policy and ordinary timing pairs."""
import argparse
import json
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('runs',type=Path,nargs='+');p.add_argument('--output',type=Path,required=True);a=p.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
rows=[];launches=[]
for run in a.runs:
    launches.append(read(run/'launch.json'));phases={}
    for path in (run/'GameData/Reports').rglob('result.json'):
        result=read(path);mode=result['StockFarCascades'];assert not result['Instrumented'] and (result['Width'],result['Height'])==(1920,1080)
        stats=read(path.parent/'far-cascade-policy.json');assert stats['Frames']==result['Frames'] and stats['Averages']['Enabled']==int(mode)
        if mode:assert stats['Averages']['RemovedMemberships']>0
        phases[mode]=dict(Result=result,Policy=stats,Native=read(path.parent/'native-performance.json'),Frame=read(path.parent/'frame-profile.json'))
    assert set(phases)=={False,True}
    for key in ['RetainedSubmission','ActorLods','ActorSizeCulling','GroupOcclusion','ParallelCascades','ParallelQueue','WorldIndirect','GeometryArena','RetainedObjects','LightTiles']:
        assert phases[False]['Result'][key]==phases[True]['Result'][key]
    rows.append(dict(Run=str(run.resolve()),Legacy=phases[False],Stock=phases[True],CpuSavedMs=phases[False]['Result']['CpuMeanMs']-phases[True]['Result']['CpuMeanMs'],GpuSavedMs=phases[False]['Frame']['GpuMs']-phases[True]['Frame']['GpuMs']))
for key in ['PluginSha256','NativeSha256','SourceCheckpointSha256']:assert len({launch[key] for launch in launches})==1
a.output.write_text(json.dumps(dict(Launches=launches,Comparisons=rows,Scope='Ordinary paired full1080p same-source private runs. Only stock far actor flag policy changes. Other prototypes fixed; unpaused selection/physics and independent GPU sample windows. No complete DX11 pixel or <4.17ms claim.'),indent=2),encoding='utf-8')
for row in rows:print(f"{Path(row['Run']).name}: CPU saved {row['CpuSavedMs']:.3f}ms, GPU-window difference {row['GpuSavedMs']:.3f}ms")
