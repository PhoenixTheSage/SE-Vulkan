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
catalog_specs = []
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
                    catalog_specs.append((pipeline, material if pipeline < 22 or pipeline >= 32 else 'Standard', dict(next(r for r in rows if r['Id'] == ids[0])['Defines'])))
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
            catalog_specs.append((pipeline, material if pipeline < 22 or pipeline >= 32 else 'Standard', dict(next(r for r in rows if r['Id'] == ids[0])['Defines'])))
        else:
            base_keys = keys - {'STATIC_DECAL_CUTOUT', 'USE_COLORMETAL_TEXTURE', 'USE_NORMALGLOSS_TEXTURE', 'USE_EXTENSIONS_TEXTURE', 'METALNESS_COLORABLE'}
            base = [r for r in rows if r['Source']=='Geometry/Materials/Standard/Vertex.hlsl'
                    and r['VertexLayout']==layouts[kind] and set(r['Defines'])==base_keys and r['Defines']['RENDERING_PASS']=='0']
            if len(base)!=1: raise ValueError(('Missing OLD cutout source template',kind))
            defines = dict(base[0]['Defines'])
            for key in keys - base_keys: defines[key] = ''
            extras.append((pipeline,'Standard',defines))
# Blended mesh decals: four stock texture bundles, both alpha conventions,
# both colorability choices, and all five OLD-compatible vertex streams.
# Banks separate authored blend masks from the first-use OLD shader macro bundle.
for bundle, authored_role in enumerate([1, 2, 3, 7]):
    for bank in range(4):
        role = [1, 2, 3, 7][(bundle + bank) % 4]
        for straight in [False, True]:
            for metal in [False, True]:
                for kind in range(5):
                    base_keys = {'RENDERING_PASS', 'TRANSFER_VERTEX_COMPONENTS', 'VERTEX_COMPONENTS_DECLARATIONS'}
                    if flags[kind]: base_keys.add(flags[kind])
                    base = [r for r in rows if r['Source']=='Geometry/Materials/Standard/Vertex.hlsl'
                            and r['VertexLayout']==layouts[kind] and set(r['Defines'])==base_keys and r['Defines']['RENDERING_PASS']=='0']
                    if len(base)!=1: raise ValueError(('Missing mesh decal source template',kind))
                    defines=dict(base[0]['Defines']);defines['STATIC_DECAL']=''
                    for bit,key in [(1,'USE_COLORMETAL_TEXTURE'),(2,'USE_NORMALGLOSS_TEXTURE'),(4,'USE_EXTENSIONS_TEXTURE')]:
                        if role & bit: defines[key]=''
                    if metal: defines['METALNESS_COLORABLE']=''
                    extras.append((128+bank*80+(400 if bank else 0)+bundle*20+int(straight)*10+int(metal)*5+kind,'Standard',defines))
# DISTANCE_FADE is an OLD instancing shader flag. Reserve a disjoint bank
# divisible by 32, 20 and 5 so stream/pass/alpha arithmetic remains stable.
fade_sources = catalog_specs + list(extras)
for pass_id, base_id in [(2, 64), (3, 96)]:
    for core in range(20):
        kind = core % 5
        if kind < 2: continue
        keys = {'RENDERING_PASS', 'TRANSFER_VERTEX_COMPONENTS', 'VERTEX_COMPONENTS_DECLARATIONS', flags[kind]}
        base = [r for r in rows if r['Source']=='Geometry/Materials/Standard/Vertex.hlsl'
                and r['VertexLayout']==layouts[kind] and set(r['Defines'])==keys and r['Defines']['RENDERING_PASS']=='0']
        if len(base)!=1: raise ValueError(('Missing distance-fade pass template',pass_id,core))
        defines=dict(base[0]['Defines']);defines['RENDERING_PASS']=str(pass_id)
        if core>=10: defines['METALNESS_COLORABLE']=''
        if core%10>=5: defines['ALPHA_MASKED']=''
        fade_sources.append((base_id+core,'AlphaMasked' if core%10>=5 else 'Standard',defines))
for pipeline, material, source_defines in fade_sources:
    if not any(flag in source_defines for flag in flags[2:]): continue
    defines=dict(source_defines);defines['DISTANCE_FADE']=''
    extras.append((2560+pipeline,material,defines))
lines += ['    };',
          '    public const uint DistanceFadeBank=2560;',
          '    public static uint WithoutDistanceFade(uint id) => id>=DistanceFadeBank ? id-DistanceFadeBank : id;',
          '    public static bool IsCutout(uint id) => WithoutDistanceFade(id) >= 22 && WithoutDistanceFade(id) < 32;',
          '    public static bool IsBlendedDecal(uint id) { id=WithoutDistanceFade(id); return (id >= 128 && id < 208) || (id >= 608 && id < 848); }',
          '    public static bool IsMeshDecal(uint id) => IsCutout(id) || IsBlendedDecal(id);',
          '    public static uint BasePipeline(uint id) { uint fade=id>=DistanceFadeBank?DistanceFadeBank:0u; id=WithoutDistanceFade(id); return fade+(IsBlendedDecal(id) ? (id-128)%5+((id-128)%10>=5?10u:0u) : IsCutout(id) ? (id-22)%5+(id>=27?10u:0u) : id); }',
          '    public static uint TextureRole(string color,string normal,string extensions) => (string.IsNullOrEmpty(color)?0u:1u)|(string.IsNullOrEmpty(normal)?0u:2u)|(string.IsNullOrEmpty(extensions)?0u:4u);',
          '    public static uint Pipeline(VRageRender.Import.MyMeshDrawTechnique technique,uint type,bool masked,bool metal,uint role=7,uint shaderRole=0,bool distanceFade=false) {',
          '        uint fade=distanceFade && type>=2?DistanceFadeBank:0u;',
          '        if(technique==VRageRender.Import.MyMeshDrawTechnique.DECAL || technique==VRageRender.Import.MyMeshDrawTechnique.DECAL_NOPREMULT){',
          '            uint bundle=role switch {1=>0u,2=>1u,3=>2u,7=>3u,_=>throw new System.ArgumentException("Unsupported stock mesh decal texture bundle.")};',
          '            uint effective=shaderRole==0?bundle:shaderRole switch {1=>0u,2=>1u,3=>2u,7=>3u,_=>throw new System.ArgumentException("Unsupported stock mesh decal shader bundle.")};',
          '            uint bank=(effective+4-bundle)%4;',
          '            return fade+128+bank*80+(bank>0?400u:0u)+bundle*20+(technique==VRageRender.Import.MyMeshDrawTechnique.DECAL_NOPREMULT?10u:0u)+(metal?5u:0u)+type;',
          '        }',
          '        return fade+(technique == VRageRender.Import.MyMeshDrawTechnique.DECAL_CUTOUT ? 22+type+(metal?5u:0u) : type+(masked?5u:0u)+(metal?10u:0u));',
          '    }',
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
print(f'Selected {sum(1 for line in lines if line.startswith("        (") and "new System" not in line)} catalog pipelines, {len(extras)} source-backed extra combinations and four fullscreen/lighting artifacts.')
