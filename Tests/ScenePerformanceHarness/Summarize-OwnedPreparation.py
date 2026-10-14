"""Compare ordinary full-scene preparation scheduling with identical policy/build."""
import argparse,json
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('runs',type=Path,nargs='+');p.add_argument('--output',type=Path,required=True);a=p.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
rows=[];launches=[]
for run in a.runs:
    launch=read(run/'launch.json');launches.append(launch);phases={}
    for path in (run/'GameData/Reports').rglob('result.json'):
        result=read(path);mode=result['OwnedPreparation'];assert not result['Instrumented'] and (result['Width'],result['Height'])==(1920,1080)
        assert result['Frames']>=1000 and result['RetainedSubmission'] and result['NewActorViews']
        stats=read(path.parent/'owned-preparation.json');assert stats['Frames']==result['Frames'] and stats['Averages']['Enabled']==int(mode)
        if mode:assert stats['Averages']['Jobs']>0 and stats['Averages']['CpuPrepared']>0 and stats['Averages']['Threads']>1
        assert mode not in phases
        phases[mode]=dict(Result=result,Preparation=stats,Native=read(path.parent/'native-performance.json'),Frame=read(path.parent/'frame-profile.json'))
    assert set(phases)=={False,True}
    for key in ['ModelBatching','RetainedSubmission','ActorLods','ActorSizeCulling','StockFarCascades','AsyncPresent','ViewEligibility','WorldRecipes','NewActorViews','GroupOcclusion','ParallelCascades','ParallelQueue','WorldIndirect','GeometryArena','RetainedObjects','LightTiles']:
        assert phases[False]['Result'][key]==phases[True]['Result'][key]
    saved=phases[False]['Result']['CpuMeanMs']-phases[True]['Result']['CpuMeanMs']
    rows.append(dict(Run=str(run.resolve()),Serial=phases[False],Owned=phases[True],FrameSavedMs=saved,FrameSavedPercent=saved*100/phases[False]['Result']['CpuMeanMs']))
for key in ['PluginSha256','NativeSha256','SourceCheckpointSha256']:assert len({launch[key] for launch in launches})==1
a.output.write_text(json.dumps(dict(Launches=launches,Comparisons=rows,Scope='Ordinary same-build/source/policy full1080p private saved-camera runs. Only actor preparation scheduling changes. Unpaused physics/selection and independent GPU windows; no full DX11 pixel or <4.17ms completion claim.'),indent=2),encoding='utf-8')
for row in rows:print(f"{Path(row['Run']).name}: saved {row['FrameSavedMs']:.3f}ms ({row['FrameSavedPercent']:.2f}%); CPU-ready jobs {row['Owned']['Preparation']['Averages']['CpuPrepared']:.1f}/{row['Owned']['Preparation']['Averages']['Jobs']:.1f}")
