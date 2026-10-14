"""Select stock highlight vertex contracts and all stock exponential blur radii."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
rows=json.loads((root/'TestResults/ShaderSuite/inventory-runtime.json').read_text(encoding='utf-8'))['Variants']
probe=(root/'ClientPlugin/Rendering/Shaders/ProbeShaderVariants.cs').read_text(encoding='utf-8')
extra=probe[probe.index('public static readonly ('):probe.index('public static readonly string[] Stages')]
extra=extra.replace('"RENDERING_PASS","2"','"RENDERING_PASS","3"')
for i in range(22):extra=extra.replace(f'({256+i}u,',f'({512+i}u,')
# Voxel highlights are not emitted by the stock actor highlight subsystem.
extra=extra[:extra.index('(532u,')]+'};\n'
lines=['// Generated from installed stock shader inventory by select-highlight-stages.py.','namespace ClientPlugin.Rendering.Shaders;','public static class HighlightShaderVariants {',extra,'public static readonly string[] Blurs = {']
for radius in range(1,33):
 for axis in ['HORIZONTAL_PASS','VERTICAL_PASS']:
  defines={axis:'','MAX_OFFSET':str(radius),'DENSITY_FUNCTION':'0'}
  candidates=[v for v in rows if v['Source']=='Postprocess/Blur.hlsl' and v['Defines']==defines]
  assert len(candidates)==1,(radius,axis)
  lines.append('"'+candidates[0]['Id']+'",')
lines+=['};','}']
(root/'ClientPlugin/Rendering/Shaders/HighlightShaderVariants.cs').write_text('\n'.join(lines)+'\n',encoding='utf-8')
