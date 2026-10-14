"""Validate actual stock-size removal and matched ordinary treatment pairs."""
import argparse
import json
from pathlib import Path

p=argparse.ArgumentParser(description=__doc__);p.add_argument('runs',type=Path,nargs='+');p.add_argument('--output',type=Path,required=True);a=p.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
rows=[];launches=[]
for run in a.runs:
    launches.append(read(run/'launch.json'));phases={}
    for path in (run/'GameData/Reports').rglob('result.json'):
        result=read(path);mode=result['ActorSizeCulling'];assert not result['Instrumented'] and (result['Width'],result['Height'])==(1920,1080)
        stats=read(path.parent/'actor-size-culling.json');assert stats['Frames']==result['Frames'] and stats['Averages']['Enabled']==int(mode)
        if mode:assert stats['Averages']['CulledActors']>0 and stats['Averages']['MainCulledActors']>0
        phases[mode]=dict(Result=result,Size=stats,Native=read(path.parent/'native-performance.json'),Frame=read(path.parent/'frame-profile.json'),Submission=read(path.parent/'retained-submission.json'))
    assert set(phases)=={False,True}
    for key in ['RetainedSubmission','ActorLods','GroupOcclusion','ParallelCascades','ParallelQueue','WorldIndirect','GeometryArena','RetainedObjects','LightTiles']:
        assert phases[False]['Result'][key]==phases[True]['Result'][key]
    delta=phases[False]['Result']['CpuMeanMs']-phases[True]['Result']['CpuMeanMs']
    rows.append(dict(Run=str(run.resolve()),Legacy=phases[False],StockSize=phases[True],SavedMs=delta,ReductionPercent=delta/phases[False]['Result']['CpuMeanMs']*100))
for key in ['PluginSha256','NativeSha256','SourceCheckpointSha256']:assert len({launch[key] for launch in launches})==1
a.output.write_text(json.dumps(dict(Launches=launches,Comparisons=rows,Scope='Ordinary full1080p same-source private pairs; flags/build verified, unpaused physics/selection drift. Stock rule changes workload; no complete DX11 selection or pixel parity proven by timing.'),indent=2),encoding='utf-8')
for row in rows:print(f"{Path(row['Run']).name}: saved {row['SavedMs']:.3f}ms ({row['ReductionPercent']:.2f}%)")
