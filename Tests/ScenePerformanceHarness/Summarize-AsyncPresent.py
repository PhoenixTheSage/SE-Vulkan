"""Verify completed-present cadence independently of renderer EndFrame return."""
import argparse
import json
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('runs',type=Path,nargs='+');p.add_argument('--output',type=Path,required=True);a=p.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
rows=[];launches=[]
for run in a.runs:
    launches.append(read(run/'launch.json'));phases={}
    for path in (run/'GameData/Reports').rglob('result.json'):
        result=read(path);mode=result['AsyncPresent'];assert not result['Instrumented'] and (result['Width'],result['Height'])==(1920,1080)
        flow=read(path.parent/'present-flow.json');assert flow['Enabled']==mode and flow['Frames']==result['Frames']
        assert flow['CompletedCalls']>=result['Frames']-3 and flow['First']['Enabled']==int(mode) and flow['Last']['Enabled']==int(mode)
        assert abs(flow['CompletionIntervalMeanMs']-result['IntervalMeanMs'])<.15
        phases[mode]=dict(Result=result,Flow=flow,Native=read(path.parent/'native-performance.json'),Frame=read(path.parent/'frame-profile.json'))
    assert set(phases)=={False,True}
    for key in ['RetainedSubmission','ActorLods','ActorSizeCulling','StockFarCascades','GroupOcclusion','ParallelCascades','ParallelQueue','WorldIndirect','GeometryArena','RetainedObjects','LightTiles']:
        assert phases[False]['Result'][key]==phases[True]['Result'][key]
    for key in ['WorldRecipes','NewActorViews','ModelBatching','ViewEligibility']:
        assert phases[False]['Result'].get(key)==phases[True]['Result'].get(key)
    rows.append(dict(Run=str(run.resolve()),Sync=phases[False],Async=phases[True],RendererSavedMs=phases[False]['Result']['CpuMeanMs']-phases[True]['Result']['CpuMeanMs'],CompletionIntervalSavedMs=phases[False]['Flow']['CompletionIntervalMeanMs']-phases[True]['Flow']['CompletionIntervalMeanMs']))
for key in ['PluginSha256','NativeSha256','SourceCheckpointSha256']:assert len({launch[key] for launch in launches})==1
a.output.write_text(json.dumps(dict(Launches=launches,Comparisons=rows,Scope='Ordinary private full1080p paired modes; native completed present-call cadence verified separately from renderer return. Not scanout FPS or complete DX11 visual parity. Call duration and wait condition come from each run; no hardcoded low-wait assumption.'),indent=2),encoding='utf-8')
for row in rows:print(f"{Path(row['Run']).name}: renderer saved {row['RendererSavedMs']:.3f}ms, completed-call interval saved {row['CompletionIntervalSavedMs']:.3f}ms")
