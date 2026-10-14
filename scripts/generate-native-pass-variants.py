"""Select installed stock GeometryStage2 simple streams; no invented definitions."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent.parent
rows = json.loads((root / 'TestResults/ShaderSuite/inventory-runtime.json').read_text())['Variants']
colour = 'POSITION_PACKED;NORMAL;TANGENT_SIGN_OF_BITANGENT;TEXCOORD0_H;SIMPLE_INSTANCE;SIMPLE_INSTANCE_COLORING'
depth = 'POSITION_PACKED;TEXCOORD0_H;SIMPLE_INSTANCE'
lines = ['// Exact installed MyShaderBundleManager inputs, SE 1.210.014.',
         'namespace ClientPlugin.Rendering.Shaders;', 'public static class NativePassShaderVariants {',
         '    public static readonly (uint Id,string Vertex,string Pixel)[] Materials = {']
for pass_id in range(3):
    for metal in (False, True):
        for alpha in (False, True):
            material = 'AlphaMasked' if alpha else 'Standard'
            layout = depth if pass_id == 1 else colour
            keys = {'RENDERING_PASS', 'USE_SIMPLE_INSTANCING', 'TRANSFER_VERTEX_COMPONENTS', 'VERTEX_COMPONENTS_DECLARATIONS'}
            keys.add('DEPTH_ONLY' if pass_id == 1 else 'USE_SIMPLE_INSTANCING_COLORING')
            if metal: keys.add('METALNESS_COLORABLE')
            if alpha: keys.add('ALPHA_MASKED')
            selected = []
            for stage in ('Vertex', 'Pixel'):
                candidates = [r for r in rows if r['Source'] == f'Geometry/Materials/{material}/{stage}.hlsl'
                              and r.get('VertexLayout') == layout and set(r['Defines']) == keys
                              and r['Defines']['RENDERING_PASS'] == str(pass_id)]
                if len(candidates) != 1:
                    raise ValueError((pass_id, material, metal, stage, len(candidates)))
                selected.append(candidates[0]['Id'])
            variant = pass_id * 4 + int(alpha) + 2 * int(metal)
            lines.append(f'        ({variant}u,"{selected[0]}","{selected[1]}"),')
stock_declarations=next(r['Defines']['VERTEX_COMPONENTS_DECLARATIONS'] for r in rows if r['Id']=='707ae8118278d70f73429ed2568d4661fc82450c0f4c89979c658ff7b486fc76')
for texture_mask in range(8):
    for metal in (False, True):
        keys = {'RENDERING_PASS','USE_SIMPLE_INSTANCING','USE_SIMPLE_INSTANCING_COLORING','TRANSFER_VERTEX_COMPONENTS','VERTEX_COMPONENTS_DECLARATIONS','STATIC_DECAL_CUTOUT'}
        for bit, define in enumerate(('USE_COLORMETAL_TEXTURE','USE_NORMALGLOSS_TEXTURE','USE_EXTENSIONS_TEXTURE')):
            if texture_mask & (1 << bit): keys.add(define)
        if metal: keys.add('METALNESS_COLORABLE')
        selected=[]
        for stage in ('Vertex','Pixel'):
            candidates=[r for r in rows if r['Source']==f'Geometry/Materials/Standard/{stage}.hlsl' and r['Defines'].get('VERTEX_COMPONENTS_DECLARATIONS')==stock_declarations and set(r['Defines'])==keys and r['Defines']['RENDERING_PASS']=='0']
            if len(candidates)!=1:raise ValueError(('cutout',texture_mask,metal,stage,len(candidates)))
            selected.append(candidates[0]['Id'])
        variant=12+texture_mask*2+int(metal)
        lines.append(f'        ({variant}u,"{selected[0]}","{selected[1]}"),')
lines += ['    };', '}']
(root/'ClientPlugin/Rendering/Shaders/NativePassShaderVariants.cs').write_text('\n'.join(lines)+'\n')
print('Selected twenty-eight authenticated colour/depth/forward/cutout pipeline pairs.')
