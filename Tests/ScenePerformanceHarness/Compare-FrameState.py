"""Compare captured frame floats at exact reflected ABI offsets; matrices excluded."""
import argparse,json,struct
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('dx',type=Path);p.add_argument('vulkan',type=Path);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
dx=read(a.dx/'frame-state.json');vk=read(a.vulkan/'frame-state.json');raw=(a.dx/'frame-state.bin').read_bytes();native=(a.vulkan/'frame-state.bin').read_bytes()
assert len(raw)==len(native)==dx['ByteSize']==vk['ByteSize']==1280
rows=[]
for name,offset in vk['Offsets'].items():
    encoded=vk['Encoded'][name];count=len(encoded) if isinstance(encoded,list) else 1
    stock=list(struct.unpack_from('<'+'f'*count,raw,offset));value=list(struct.unpack_from('<'+'f'*count,native,offset))
    rows.append(dict(Field=name,Offset=offset,Stock=stock,Vulkan=value,Equal=stock==value))
result=dict(StockSource=str(a.dx.resolve()),VulkanSource=str(a.vulkan.resolve()),ProbeSettings=dx['Environment']['EnvironmentProbe'],ProbeAmbient=dx['ProbeAmbient'],Rows=rows,Scope='Actual CPU frame block floats at declared shader offsets. Independent paused loads/sun times; matrix storage differs and is excluded. No direct pixel or FPS causality claim.')
a.output.write_text(json.dumps(result,indent=2),encoding='utf-8')
for row in rows:
    if not row['Equal']:print(row['Field'],row['Stock'],'->',row['Vulkan'])
print(a.output)
