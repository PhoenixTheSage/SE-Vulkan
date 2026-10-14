"""Select exact vanilla triplanar/depth variants from the captured runtime inventory."""
import json
from pathlib import Path
root=Path(__file__).resolve().parent.parent
rows=json.loads((root/'TestResults/ShaderSuite/inventory-runtime.json').read_text())['Variants']
lines=['// Generated from the authenticated SE 1.210.014 runtime inventory.',
       'namespace ClientPlugin.Rendering.Shaders;','public static class VoxelShaderVariants {',
       'public static readonly (uint Id,string Vertex,string Pixel)[] Materials = {']
for kind,material in enumerate(['TriplanarSingle','TriplanarMulti']):
 for depth in [False,True]:
  ids=[]
  for stage in ['Vertex','Pixel']:
   keys={'RENDERING_PASS','TRANSFER_VERTEX_COMPONENTS','VERTEX_COMPONENTS_DECLARATIONS','USE_VOXEL_DATA'}|({'DEPTH_ONLY'} if depth else set())
   match=[r for r in rows if r['Source']==f'Geometry/Materials/{material}/{stage}.hlsl' and r['VertexLayout']=='VOXEL_POSITION_MAT;VOXEL_NORMAL' and set(r['Defines'])==keys and r['Defines']['RENDERING_PASS']==('1' if depth else '0')]
   assert len(match)==1,(material,depth,stage,len(match))
   ids.append(match[0]['Id'])
  lines.append(f'({20+kind+(32 if depth else 0)}u,"{ids[0]}","{ids[1]}"),')
lines+=['};','}']
(root/'ClientPlugin/Rendering/Shaders/VoxelShaderVariants.cs').write_text('\n'.join(lines)+'\n')
print('Selected four voxel pipelines (eight catalog stages); no recompilation required.')
