"""Keep corrected both-order group visibility timings separate from old prototypes."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[2]
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
ids=['Vulkan-3142c7abc5ee43c794ed5d351a389961','Vulkan-7efcc05facb346a78fe3800698891d5d']
pairs=[];launches=[]
for name in ids:
    run=root/'TestResults/ScenePerformance'/name;launches.append(read(run/'launch.json'));rows={}
    for path in (run/'GameData/Reports').rglob('result.json'):
        r=read(path);assert not r['Instrumented'] and (r['Width'],r['Height'])==(1920,1080)
        assert r['ActorLods'] and r['RetainedSubmission'] and r['ParallelCascades']
        group=read(path.parent/'group-occlusion.json');assert group['Enabled']==r['GroupOcclusion']
        if r['GroupOcclusion']:assert group['Accepted']>0 and group['SceneCulls']>0
        rows[r['GroupOcclusion']]=dict(Result=r,Groups=group,Native=read(path.parent/'native-performance.json'),FrameProfile=read(path.parent/'frame-profile.json'))
    assert set(rows)=={False,True}
    saved=rows[False]['Result']['CpuMeanMs']-rows[True]['Result']['CpuMeanMs']
    pairs.append(dict(Run=name,Legacy=rows[False],Groups=rows[True],CpuSavedMs=saved,CpuReductionPercent=saved/rows[False]['Result']['CpuMeanMs']*100))
for key in ['PluginSha256','NativeSha256','SourceCheckpointSha256']:assert len({l[key] for l in launches})==1
output=dict(Launches=launches,Pairs=pairs,Scope='Ordinary same-source unpaused full1080p with corrected unparented LOD bounds and other treatments fixed. Group counters are lifetime snapshots, not per-frame rates. Pixel proof is separate; no full DX11 parity or <4.17 ms claim.')
path=root/'TestResults/GroupOcclusion-CorrectedComparisons.json';path.write_text(json.dumps(output,indent=2),encoding='utf-8')
for row in pairs:print(f"{row['Run']}: saved {row['CpuSavedMs']:.3f} ms ({row['CpuReductionPercent']:.2f}%)")
print(path)
