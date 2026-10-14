import json
from pathlib import Path
r=Path('.')
rows=json.loads((r/'TestResults/ShaderSuite/inventory-runtime.json').read_text(encoding='utf-8'))['Variants']
rigid='POSITION_PACKED;NORMAL;TANGENT_SIGN_OF_BITANGENT;TEXCOORD0_H'
blend='POSITION_PACKED;BLEND_WEIGHTS;BLEND_INDICES;NORMAL;TANGENT_SIGN_OF_BITANGENT;TEXCOORD0_H'
layouts=[rigid,blend,rigid+';GENERIC_INSTANCE',rigid+';CUBE_INSTANCE',blend+';CUBE_INSTANCE']
flags=[None,'USE_SKINNING','USE_GENERIC_INSTANCING','USE_CUBE_INSTANCING','USE_DEFORMED_CUBE_INSTANCING']
lines=['// Generated from the authenticated SE 1.210.014 inventory.','namespace ClientPlugin.Rendering.Shaders;','public static class TransparencyShaderVariants {','public static readonly (uint Id,string Vertex,string Pixel)[] Glass = {']
for material,base in [("Glass",0),("Holo",5)]:
 for k in range(3):
  keys={'RENDERING_PASS','TRANSFER_VERTEX_COMPONENTS','VERTEX_COMPONENTS_DECLARATIONS'}
  if flags[k]: keys.add(flags[k])
  ids=[]
  for stage in ['Vertex','Pixel']:
   a=[x for x in rows if x['Source']==f'Geometry/Materials/{material}/{stage}.hlsl' and x['VertexLayout']==layouts[k] and set(x['Defines'])==keys and x['Defines']['RENDERING_PASS']=='5']
   assert len(a)==1,(k,stage,len(a))
   ids.append(a[0]['Id'])
  lines.append(f'({base+k}u,"{ids[0]}","{ids[1]}"),')
lines+=['};','public static readonly (uint Id,string Material,System.Collections.Generic.Dictionary<string,string> Defines)[] Extra = {']
for material,main_base,depth_base in [("Glass",64,70),("Holo",80,90)]:
 for k in [3,4]:
  a=[x for x in rows if x['Source']=='Geometry/Materials/Standard/Vertex.hlsl' and x['VertexLayout']==layouts[k] and len(x['Defines'])==4 and x['Defines'].get('RENDERING_PASS')=='0']
  assert len(a)==1
  defs=dict(a[0]['Defines']);defs['RENDERING_PASS']='5'
  lines.append(f'({main_base+k}u,"{material}",new System.Collections.Generic.Dictionary<string,string> {{')
  for key,value in defs.items(): lines.append('{'+json.dumps(key)+','+json.dumps(value)+'},')
  lines.append('}),')
 for k in range(5):
  a=[x for x in rows if x['Source']=='Geometry/Materials/Standard/Vertex.hlsl' and x['VertexLayout']==layouts[k] and set(x['Defines'])==({'RENDERING_PASS','TRANSFER_VERTEX_COMPONENTS','VERTEX_COMPONENTS_DECLARATIONS'}|({flags[k]} if flags[k] else set())) and x['Defines'].get('RENDERING_PASS')=='0']
  assert len(a)==1
  defs=dict(a[0]['Defines']);defs['RENDERING_PASS']='6'
  lines.append(f'({depth_base+k}u,"{material}",new System.Collections.Generic.Dictionary<string,string> {{')
  for key,value in defs.items():lines.append('{'+json.dumps(key)+','+json.dumps(value)+'},')
  lines.append('}),')
lines+=['};','public static readonly (uint Id,string Vertex,string Pixel)[] Billboards = {']
for single in [False,True]:
 for additive in [False,True]:
  for flags in range(8):
   defs={}
   if single:defs['SINGLE_CHANNEL']=''
   if not additive: defs['OIT']=''
   if flags&1: defs['LIT_PARTICLE']=''
   if flags&2: defs['SOFT_PARTICLE']=''
   if flags&4: defs['ALPHA_CUTOUT']=''
   ids=[]
   for profile in ['vs','ps']:
    a=[x for x in rows if x['Source']=='Transparent/Billboards.hlsl' and x['Profile']==profile+'_5_0' and x['Defines']==defs]
    assert len(a)==1
    ids.append(a[0]['Id'])
   lines.append(f'({20+flags+(8 if additive else 0)+(16 if single else 0)}u,"{ids[0]}","{ids[1]}"),')
lines+=['};','public static readonly string[] Particles = {']
for source,profile,defs in [('InitDeadList','cs',{}),('Reset','cs',{}),('Emit','cs',{}),('Emit','cs',{'ALT_INDEX_BUFFER':''}),('EmitSkipFix','cs',{}),('Simulation','cs',{}),('Simulation','cs',{'ALT_INDEX_BUFFER':''}),('SimulationArgs','cs',{}),('Render','vs',{'STREAKS':'','LIT_PARTICLE':''}),('Render','ps',{'STREAKS':'','LIT_PARTICLE':'','OIT':''})]:
 a=[x for x in rows if x['Source']==f'Transparent/GPUParticles/{source}.hlsl' and x['Profile']==profile+'_5_0' and x['Defines']==defs]
 assert len(a)==1,(source,profile,defs)
 lines.append('"'+a[0]['Id']+'",')
lines+=['};','public static readonly (uint Id,string Vertex,string Pixel)[] Decals = {']
for role in [1,2,3,7,8]:
 defs={}
 if role==8:defs['RENDER_TO_TRANSPARENT']=''
 else:
  for bit,key in [(1,'USE_COLORMETAL_TEXTURE'),(2,'USE_NORMALGLOSS_TEXTURE'),(4,'USE_EXTENSIONS_TEXTURE')]:
   if role&bit:defs[key]=''
 ids=[]
 for profile in ['vs','ps']:
  a=[x for x in rows if x['Source']=='Decals/Decals.hlsl' and x['Profile']==profile+'_5_0' and x['Defines']==(defs if profile=='ps' else {})]
  assert len(a)==1,(role,profile,len(a))
  ids.append(a[0]['Id'])
 lines.append(f'({role}u,"{ids[0]}","{ids[1]}"),')
lines+=['};','public static readonly string Resolve = "'+next(x['Id'] for x in rows if x['Source']=='Transparent/OIT/Resolve.hlsl')+'";','}']
(r/'ClientPlugin/Rendering/Shaders/TransparencyShaderVariants.cs').write_text('\n'.join(lines)+'\n',encoding='utf-8')
