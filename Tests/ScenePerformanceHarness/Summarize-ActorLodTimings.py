"""Verify and preserve both-order experimental actor LOD timings."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[2]
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
ids=['Vulkan-a1136ab611f04ab89926a1e809b90384','Vulkan-8c1c64cd1a9d4c959b3e3ee95c7bafaf']
pairs=[];launches=[]
for name in ids:
    run=root/'TestResults/ScenePerformance'/name;launches.append(read(run/'launch.json'));rows={}
    for path in (run/'GameData/Reports').rglob('result.json'):
        r=read(path);assert not r['Instrumented'] and (r['Width'],r['Height'])==(1920,1080)
        assert r['RetainedSubmission'] and r['ParallelCascades'] and not any(r[k] for k in ['WorldIndirect','GeometryArena','LightTiles','RetainedObjects'])
        stats=read(path.parent/'actor-lods.json');assert stats['Averages']['Enabled']==int(r['ActorLods'])
        if r['ActorLods']:assert stats['Averages']['ReducedActors']>0
        rows[r['ActorLods']]=dict(Result=r,Lods=stats,Native=read(path.parent/'native-performance.json'),FrameProfile=read(path.parent/'frame-profile.json'))
    assert set(rows)=={False,True}
    saved=rows[False]['Result']['CpuMeanMs']-rows[True]['Result']['CpuMeanMs']
    pairs.append(dict(Run=name,Base=rows[False],Lods=rows[True],CpuSavedMs=saved,CpuReductionPercent=saved/rows[False]['Result']['CpuMeanMs']*100))
for key in ['PluginSha256','NativeSha256','SourceCheckpointSha256']:assert len({l[key] for l in launches})==1
output=dict(Launches=launches,Pairs=pairs,Scope='Ordinary full1080p same-source unpaused saved-camera snapshots; physics/selection drift. Experimental old-pipeline authored actor LODs; static and skeletal paths retain base geometry. No complete DX11 parity or <4.17ms claim. CPU Draw-through-Present differs from GPU time and consecutive-present intervals.')
path=root/'TestResults/ActorLods-CleanComparisons.json';path.write_text(json.dumps(output,indent=2),encoding='utf-8')
for row in pairs:print(f"{row['Run']}: saved {row['CpuSavedMs']:.3f} ms ({row['CpuReductionPercent']:.2f}%)")
print(path)
