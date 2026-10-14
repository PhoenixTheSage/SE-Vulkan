"""Validate ordinary full-scene static-object ownership comparisons."""
import argparse,json
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('runs',type=Path,nargs='+');p.add_argument('--output',type=Path,required=True);args=p.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
rows=[];launches=[]
for run in args.runs:
    launches.append(read(run/'launch.json'));phases={}
    for path in (run/'GameData/Reports').rglob('result.json'):
        result=read(path);mode=result['StaticObjects'];assert not result['Instrumented'] and (result['Width'],result['Height'])==(1920,1080)
        assert result['Frames']>=1000 and result['WorldRecipes'] and result['RetainedSubmission'] and not result['RetainedObjects']
        stats=read(path.parent/'static-objects.json');assert stats['Frames']==result['Frames']
        if mode:assert stats['Averages']['Draws']>0 and stats['Averages']['UniqueObjects']>0
        assert mode not in phases
        phases[mode]=dict(Result=result,Static=stats,Submission=read(path.parent/'retained-submission.json'),Native=read(path.parent/'native-performance.json'),Frame=read(path.parent/'frame-profile.json'))
    assert set(phases)=={False,True}
    for key in ['AsyncPresent','ModelBatching','NewActorViews','WorldRecipes','RetainedSubmission','ActorLods','ActorSizeCulling','StockFarCascades','ViewEligibility','GroupOcclusion','ParallelCascades','ParallelQueue','WorldIndirect','GeometryArena','RetainedObjects','LightTiles']:
        assert phases[False]['Result'][key]==phases[True]['Result'][key]
    saved=phases[False]['Result']['CpuMeanMs']-phases[True]['Result']['CpuMeanMs']
    rows.append(dict(Run=str(run.resolve()),Dynamic=phases[False],Static=phases[True],FrameSavedMs=saved,FrameSavedPercent=saved*100/phases[False]['Result']['CpuMeanMs']))
for key in ['PluginSha256','NativeSha256','SourceCheckpointSha256']:assert len({launch[key] for launch in launches})==1
args.output.write_text(json.dumps(dict(Launches=launches,Comparisons=rows,Scope='Ordinary same-build/source/policy private full1080p pairs; only immutable object ownership changes. Unpaused physics/selection and independent GPU windows. No full DX11 parity or <4.17ms completion claim.'),indent=2),encoding='utf-8')
for row in rows:print(f"{Path(row['Run']).name}: saved {row['FrameSavedMs']:.3f}ms ({row['FrameSavedPercent']:.2f}%); static draws {row['Static']['Static']['Averages']['Draws']:.1f}, upload bytes {row['Static']['Static']['Averages']['UploadedBytes']:.1f}")
