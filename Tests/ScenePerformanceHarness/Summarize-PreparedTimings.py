"""Preserve ordinary actor-production experiments without mixing revisions or FPS lanes."""
import argparse
import json
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('runs',type=Path,nargs='+')
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
comparisons=[]
for run in args.runs:
    launch=read(run/'launch.json');phases={}
    for path in (run/'GameData/Reports').rglob('result.json'):
        result=read(path);mode=result['ParallelPrepared']
        assert not result['Instrumented'] and (result['Width'],result['Height'])==(1920,1080)
        assert result['RetainedSubmission'] and result['ParallelCascades'] and result['ParallelQueue']
        workers=read(path.parent/'prepared-workers.json');native=read(path.parent/'native-performance.json')
        assert workers['Frames']==result['Frames'] and workers['Averages']['Enabled']==int(mode)
        if mode:assert workers['Averages']['Actors']>=256 and workers['Averages']['Parts']>=1024 and workers['Averages']['WorkerThreads']>=2
        phases[mode]=dict(Result=result,Workers=workers,Native=native,Scope='Each pair shares one exact launch build; revisions between separate pairs are reported independently.')
    assert set(phases)=={False,True}
    for key in ['ActorLods','GroupOcclusion','GeometryArena','WorldIndirect','RetainedObjects','LightTiles']:
        assert phases[False]['Result'][key]==phases[True]['Result'][key]
    serial=phases[False]['Result']['CpuMeanMs'];parallel=phases[True]['Result']['CpuMeanMs']
    serial_present=phases[False]['Native']['PresentUs']/1000;parallel_present=phases[True]['Native']['PresentUs']/1000
    comparisons.append(dict(Run=str(run.resolve()),Launch=launch,Serial=phases[False],Parallel=phases[True],ParallelAddedMs=parallel-serial,
        SerialPresentMs=serial_present,ParallelPresentMs=parallel_present,
        AddedOutsidePresentMs=(parallel-parallel_present)-(serial-serial_present),
        OutsidePresentScope='Draw-through-present wall duration minus measured vkQueuePresentKHR call span; not exclusive CPU time.'))
output=dict(Comparisons=comparisons,RevisionsVerifiedWithinEachPair=True,
    Scope='Both-order ordinary same-source full1080p private runs. New exception-recovery handling between pairs changes binary hash; each is an independent negative result, not one identical-binary pooled dataset. GPU presentation cost changed since earlier controls. No optimization gain or <4.17ms claim.')
args.output.write_text(json.dumps(output,indent=2),encoding='utf-8')
for row in comparisons:print(f"{Path(row['Run']).name}: parallel added {row['ParallelAddedMs']:.3f} ms; outside-present delta {row['AddedOutsidePresentMs']:.3f} ms")
