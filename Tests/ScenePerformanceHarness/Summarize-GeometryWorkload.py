"""Compare captured geometry contracts; diagnostic data is not benchmark FPS."""
import argparse
import collections
import json
from pathlib import Path

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('vulkan_reports',type=Path)
p.add_argument('dx11_reports',type=Path)
p.add_argument('--occlusion-reports',type=Path)
a=p.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
vk=read(a.vulkan_reports/'geometry-workload.json');dx=read(a.dx11_reports/'geometry-workload.json')
for root in [a.vulkan_reports,a.dx11_reports]:
    result=read(root/'result.json');assert (result['Width'],result['Height'])==(1920,1080)
    assert read(root.parents[1]/'launch.json')['SourceCheckpointSha256']==read(a.vulkan_reports.parents[1]/'launch.json')['SourceCheckpointSha256']
assert vk['UnmappedScenePackets']==0
assert 'StaticLodGroups' in dx  # Do not mistake an incomplete path audit for the full prepared geometry.
old=next(v['Rows'] for v in dx['OldGeometryViews'] if v['ViewType']=='Main')
scene=[r for r in old if r['MeshFile'] and not r['Technique'].startswith('VOXEL')]
assert 'NewGeometryViews' in dx, 'Post-frame new-pipeline arrays are incomplete; recapture on owning render jobs.'
newmain=next(view for view in dx['NewGeometryViews'] if view['ViewType']=='Main')
groups=newmain['Groups']+newmain['StaticGroups']
dxwork=sum(r['IndexCount']*r['Instances'] for r in scene)+sum(r['GBufferIndexCount']*r['Instances'] for r in groups)
work=collections.Counter()
for row in vk['Packets']:
    if row['MainSelected'] and row['Pipeline']<20:work[row['ModelKey']]+=row['IndexCount']*row['Instances']
assert sum(work.values())==vk['MainSceneIndexInvocations']
lodpath=a.vulkan_reports/'model-lod-assets.json';lods=read(lodpath) if lodpath.exists() else dict(Models=[],ModelsWithLods=None,Failures=['Asset export absent in paused selection-only capture'])
output=dict(VulkanRun=str(a.vulkan_reports.resolve()),Dx11Run=str(a.dx11_reports.resolve()),
    VulkanSceneIndexInvocations=vk['MainSceneIndexInvocations'],Dx11SceneIndexInvocations=dxwork,
    IndexInvocationRatio=vk['MainSceneIndexInvocations']/dxwork,
    VulkanMainScenePackets=vk['MainScenePackets'],Dx11OldMainSceneRenderables=len(scene),
    Dx11NewMainInstanceGroups=len(newmain['Groups']),
    Dx11NewMainStaticGroups=len(newmain['StaticGroups']),
    Dx11LodCoefficient=dx['LodCoefficient'],Dx11LodSettings=dx['StockLodSettings'],
    ModelAssetAudit=dict(Models=len(lods['Models']),ModelsWithLods=lods['ModelsWithLods'],Failures=lods['Failures']),
    LargestVulkanModels=[dict(Model=k,IndexInvocations=v) for k,v in work.most_common(20)],
    Scope='Same source save, full1080p, separate unpaused diagnostic frames. Scene opaque indexed elements multiplied by instances; excludes voxels and transparent geometry. Counted old and new/static DX11 paths. Snapshot selection and physics can differ; ratio is workload evidence, not speedup or equality of view/visibility. Audit timing rows are instrumented and are not ordinary FPS proof.')
if a.occlusion_reports:output['Occlusion']=read(a.occlusion_reports/'occlusion-diagnostics.json')
path=a.vulkan_reports/'geometry-contract-comparison.json';path.write_text(json.dumps(output,indent=2),encoding='utf-8')
print(f"Scene indexed invocations: Vulkan {output['VulkanSceneIndexInvocations']:,}; DX11 {dxwork:,}; ratio {output['IndexInvocationRatio']:.2f}")
print(path)
