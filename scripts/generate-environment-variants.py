"""Select source/profile/define-exact stock planet and foliage shader stages."""
import json
from pathlib import Path
root = Path(__file__).resolve().parent.parent
rows = json.loads((root/'TestResults/ShaderSuite/inventory-runtime.json').read_text())['Variants']
specs = [('Transparent/Atmosphere/AtmospherePrecompute.hlsl','cs',{}),
         ('Transparent/Atmosphere/AtmosphereGBuffer.hlsl','ps',{'HQ':''}),
         ('Transparent/Clouds/Clouds.hlsl','vs',{}), ('Transparent/Clouds/Clouds.hlsl','ps',{}),
         ('Foliage/Foliage.hlsl','vs',{}), ('Foliage/Foliage.hlsl','gs',{}),
         ('Foliage/Foliage.hlsl','ps',{}), ('Foliage/Foliage.hlsl','gs',{'ROCK_FOLIAGE':''}),
         ('Foliage/Foliage.hlsl','ps',{'ROCK_FOLIAGE':''})]
lines = ['// Generated from the authenticated SE 1.210.014 runtime inventory.',
         'namespace ClientPlugin.Rendering.Shaders;',
         'public static class EnvironmentShaderVariants {',
         'public static readonly string[] Stages = {']
for source, profile, defines in specs:
    matches = [r for r in rows if r['Source']==source and r['Profile']==profile+'_5_0' and r['Defines']==defines]
    assert len(matches)==1, (source, profile, defines, len(matches))
    lines.append('"'+matches[0]['Id']+'",')
lines += ['};','}']
(root/'ClientPlugin/Rendering/Shaders/EnvironmentShaderVariants.cs').write_text('\n'.join(lines)+'\n')
print('Selected nine stock environment/foliage stages.')
