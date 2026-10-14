"""Verify native worker activation and matched ordinary queue comparisons."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[2]
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
ids=['Vulkan-b0b9a2cd6c0e49dfac4e4842ee9b8818','Vulkan-fe8dd3a705b641fcae3f4746a79e61c3','Vulkan-cd18432f115a456bbdf29c8773c8afb8']
rows=[];launches=[]
for name in ids:
    run=root/'TestResults/ScenePerformance'/name;launches.append(read(run/'launch.json'));phases={}
    for path in (run/'GameData/Reports').rglob('result.json'):
        r=read(path);assert not r['Instrumented'] and (r['Width'],r['Height'])==(1920,1080)
        assert r['RetainedSubmission'] and r['ParallelCascades']
        stats=read(path.parent/'queue-work.json');assert stats['Averages']['Enabled']==int(r['ParallelQueue'])
        if r['ParallelQueue']:assert stats['Averages']['Workers']==4 and stats['Averages']['Draws']>=2048
        phases[r['ParallelQueue']]=dict(Result=r,Queue=stats,Native=read(path.parent/'native-performance.json'))
    assert set(phases)=={False,True}
    for key in ['ActorLods','GroupOcclusion','WorldIndirect','GeometryArena','LightTiles']:assert phases[False]['Result'][key]==phases[True]['Result'][key]
    delta=phases[False]['Result']['CpuMeanMs']-phases[True]['Result']['CpuMeanMs']
    rows.append(dict(Run=name,Serial=phases[False],Parallel=phases[True],CpuSavedMs=delta,CpuReductionPercent=delta/phases[False]['Result']['CpuMeanMs']*100))
for key in ['PluginSha256','NativeSha256','SourceCheckpointSha256']:assert len({l[key] for l in launches})==1
output=dict(Launches=launches,Comparisons=rows,Scope='Ordinary full1080p same-source unpaused camera, combined experimental path both orders plus normal path one order. Only queue policy changes within each pair. Physics/selection drift; no full visual parity or <4.17ms claim.')
path=root/'TestResults/ParallelQueue-CleanComparisons.json';path.write_text(json.dumps(output,indent=2),encoding='utf-8')
for row in rows:print(f"{row['Run']}: saved {row['CpuSavedMs']:.3f} ms ({row['CpuReductionPercent']:.2f}%)")
print(path)
