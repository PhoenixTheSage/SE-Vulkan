"""Compare same-process paused GPU ownership captures; no performance claim."""
import argparse, json
from pathlib import Path
import numpy as np

p=argparse.ArgumentParser(description=__doc__);p.add_argument('reports',type=Path);a=p.parse_args()
root=a.reports/'Lighting';names=['control','treatment','control-return']
states=[json.loads((root/n/'state.json').read_text(encoding='utf-8-sig')) for n in names]
for s in states:
    assert s['SameProcess'] and s['GpuScenePolicy'] and s['Paused']
    assert (s['Width'],s['Height'])==(1920,1080) and s['Schedule']['Busy']==0 and s['Schedule']['Stage']==0
keys=['MainFrameHash','LightHash','SpotHash','PostprocessHash','ProbeFrameHash','ProbeProjectionHash','CascadeMatrices']
for k in keys:assert states[0][k]==states[1][k]==states[2][k],f'unmatched input {k}'
def differences(left,right,dtype):
    x=np.fromfile(left,dtype=dtype);y=np.fromfile(right,dtype=dtype);assert x.shape==y.shape
    d=np.abs(x.astype(np.float64)-y.astype(np.float64));finite=np.isfinite(d)
    assert finite.all(),f'nonfinite comparison {left.name}'
    return dict(Elements=int(d.size),Changed=int(np.count_nonzero(d)),MeanAbsolute=float(d.mean()),P95Absolute=float(np.percentile(d,95)),MaxAbsolute=float(d.max()))
files=['view.rgba8']+sorted(states[0]['ImageHashes'])
rows=[]
for f in files:
    dtype=np.uint8 if f.endswith('rgba8') else np.float16 if f.endswith('f16') else np.float32
    rows.append(dict(File=f,GpuVsControl=differences(root/names[0]/f,root/names[1]/f,dtype),ControlReturn=differences(root/names[0]/f,root/names[2]/f,dtype)))
out=dict(Source=str(a.reports.resolve()),Paused=True,SameProcess=True,MatchedInputs=keys,RetainedSpotPolicy=states[0]['RetainedSpotPolicy'],Rows=rows,Scope='GPU ownership OFF/ON/OFF; lights/frame/probe/postprocess constants and cascade matrices matched; all probes completed. Diagnostic correctness only.')
(a.reports/'gpu-lighting-comparison.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
for r in rows:
    if r['File']=='view.rgba8' or r['GpuVsControl']['Changed'] or r['ControlReturn']['Changed']:print(r)
print('Matched inputs verified; comparison saved. No automatic tolerance or parity claim.')
