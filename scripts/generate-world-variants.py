"""Select exact source-captured core variants; never guess shader macros/IDs."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent.parent
inventory = json.loads((root / 'TestResults/ShaderSuite/inventory-runtime.json').read_text())
rows = inventory['Variants']
rigid = 'POSITION_PACKED;NORMAL;TANGENT_SIGN_OF_BITANGENT;TEXCOORD0_H'
blend = 'POSITION_PACKED;BLEND_WEIGHTS;BLEND_INDICES;NORMAL;TANGENT_SIGN_OF_BITANGENT;TEXCOORD0_H'
layouts = [rigid, blend, rigid+';GENERIC_INSTANCE', rigid+';CUBE_INSTANCE', blend+';CUBE_INSTANCE']
flags = [None, 'USE_SKINNING', 'USE_GENERIC_INSTANCING', 'USE_CUBE_INSTANCING', 'USE_DEFORMED_CUBE_INSTANCING']
extras = []
lines = ['// Generated from the authenticated SE 1.210.014 runtime inventory.',
         'namespace ClientPlugin.Rendering.Shaders;', 'public static class WorldShaderVariants {',
         '    public static readonly (uint Id,string Vertex,string Pixel)[] Materials = {']
for depth in [False, True]:
    for metal in [False, True]:
        for alpha in [False, True]:
            material = 'AlphaMasked' if alpha else 'Standard'
            for kind in range(5):
                keys = {'RENDERING_PASS', 'TRANSFER_VERTEX_COMPONENTS', 'VERTEX_COMPONENTS_DECLARATIONS'}
                if flags[kind]: keys.add(flags[kind])
                if metal: keys.add('METALNESS_COLORABLE')
                if alpha: keys.add('ALPHA_MASKED')
                if depth: keys.add('DEPTH_ONLY')
                ids = []
                for stage in ['Vertex', 'Pixel']:
                    candidates = [r for r in rows if r['Source'] == f'Geometry/Materials/{material}/{stage}.hlsl'
                                  and r['VertexLayout'] == layouts[kind]
                                  and set(r['Defines']) == keys and r['Defines']['RENDERING_PASS'] == ('1' if depth else '0')]
                    if not candidates: break
                    if len(candidates) != 1: raise ValueError((kind,metal,alpha,depth,stage,len(candidates)))
                    ids.append(candidates[0]['Id'])
                pipeline = kind + (5 if alpha else 0) + (10 if metal else 0) + (32 if depth else 0)
                if len(ids) == 2:
                    lines.append(f'        ({pipeline}u,"{ids[0]}","{ids[1]}"),')
                else:
                    base_keys=keys-{'ALPHA_MASKED','METALNESS_COLORABLE'}
                    base=[r for r in rows if r['Source']=='Geometry/Materials/Standard/Vertex.hlsl' and r['VertexLayout']==layouts[kind] and set(r['Defines'])==base_keys and r['Defines']['RENDERING_PASS']==('1' if depth else '0')]
                    if len(base)!=1: raise ValueError(('Missing source-backed template',pipeline))
                    defines=dict(base[0]['Defines'])
                    if alpha: defines['ALPHA_MASKED']=''
                    if metal: defines['METALNESS_COLORABLE']=''
                    extras.append((pipeline,material,defines))
# OLD mesh decals need their own stock vertex offset and pixel alpha clip.
# Slots 20/21 remain voxel pipelines; 22..31 cover all five OLD streams.
for metal in [False, True]:
    for kind in range(5):
        keys = {'RENDERING_PASS', 'TRANSFER_VERTEX_COMPONENTS', 'VERTEX_COMPONENTS_DECLARATIONS',
                'STATIC_DECAL_CUTOUT', 'USE_COLORMETAL_TEXTURE', 'USE_NORMALGLOSS_TEXTURE', 'USE_EXTENSIONS_TEXTURE'}
        if flags[kind]: keys.add(flags[kind])
        if metal: keys.add('METALNESS_COLORABLE')
        ids = []
        for stage in ['Vertex', 'Pixel']:
            candidates = [r for r in rows if r['Source'] == f'Geometry/Materials/Standard/{stage}.hlsl'
                          and r['VertexLayout'] == layouts[kind] and set(r['Defines']) == keys
                          and r['Defines']['RENDERING_PASS'] == '0']
            if not candidates: break
            if len(candidates) != 1: raise ValueError(('Ambiguous OLD cutout',kind,metal,stage,len(candidates)))
            ids.append(candidates[0]['Id'])
        pipeline = 22 + kind + (5 if metal else 0)
        if len(ids) == 2:
            lines.append(f'        ({pipeline}u,"{ids[0]}","{ids[1]}"),')
        else:
            base_keys = keys - {'STATIC_DECAL_CUTOUT', 'USE_COLORMETAL_TEXTURE', 'USE_NORMALGLOSS_TEXTURE', 'USE_EXTENSIONS_TEXTURE', 'METALNESS_COLORABLE'}
            base = [r for r in rows if r['Source']=='Geometry/Materials/Standard/Vertex.hlsl'
                    and r['VertexLayout']==layouts[kind] and set(r['Defines'])==base_keys and r['Defines']['RENDERING_PASS']=='0']
            if len(base)!=1: raise ValueError(('Missing OLD cutout source template',kind))
            defines = dict(base[0]['Defines'])
            for key in keys - base_keys: defines[key] = ''
            extras.append((pipeline,'Standard',defines))
lines += ['    };',
          '    public static bool IsCutout(uint id) => id >= 22 && id < 32;',
          '    public static uint BasePipeline(uint id) => IsCutout(id) ? (id - 22) % 5 + (id >= 27 ? 10u : 0u) : id;',
          '    public static uint Pipeline(VRageRender.Import.MyMeshDrawTechnique technique,uint type,bool masked,bool metal) =>',
          '        technique == VRageRender.Import.MyMeshDrawTechnique.DECAL_CUTOUT ? 22 + type + (metal ? 5u : 0u) : type + (masked ? 5u : 0u) + (metal ? 10u : 0u);',
          '    public static readonly string[] Lighting = {',
          '        "30b8244ce109a84de34caf242728f83242023418475e19ac72d306ecf3776b22",',
          '        "486ce864a346aaea71f0934de8873577886139cc9e795869ed72c927c6066c0c",',
          '        "a6a98288f3efd10effb6bf7192ff1e3fe3ec96ac4a7ef285b054d2a41459c78e",',
          '        "38a758a2660e8c5677d1357bb8eb5913598b53d07ec3365d328246fe63aed342",', '    };',
          '    // Additional supported combinations absent from vanilla precompile annotations.',
          '    public static readonly (uint Id,string Material,System.Collections.Generic.Dictionary<string,string> Defines)[] Extra = {']
for pipeline,material,defines in extras:
    lines.append(f'        ({pipeline}u,"{material}",new System.Collections.Generic.Dictionary<string,string> {{')
    for key,value in defines.items(): lines.append('            {'+json.dumps(key)+','+json.dumps(value)+'},')
    lines.append('        }),')
lines += ['    };','}']
(root/'ClientPlugin/Rendering/Shaders/WorldShaderVariants.cs').write_text('\n'.join(lines)+'\n')
print(f'Selected {50-len(extras)} catalog pipelines, {len(extras)} source-backed extra combinations and four fullscreen/lighting artifacts.')
