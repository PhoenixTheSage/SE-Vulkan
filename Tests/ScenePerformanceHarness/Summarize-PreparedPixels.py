"""Verify paused serial/parallel/serial production and quantify repeat-controlled pixels."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('reports',type=Path)
args=parser.parse_args()
root=args.reports
metadata=json.loads((root/'pixel-comparison.json').read_text(encoding='utf-8-sig'))
rows=metadata['Requests'];expected=[False,False,True,True,False,False]
assert len(rows)==6 and metadata['Paused']
assert len({r['Camera'] for r in rows})==1
assert len({r['LogicalDraws'] for r in rows})==1 and rows[0]['LogicalDraws']>0
assert len({r['PhysicalPackets'] for r in rows})==1
for row,mode in zip(rows,expected):
    assert row['Paused'] and row['ParallelPreparedRequested']==mode and row['PreparedWork']['Enabled']==mode
    if mode:assert row['PreparedWork']['Actors']>=256 and row['PreparedWork']['Parts']>=1024 and row['PreparedWork']['WorkerThreads']>=2
resolution=metadata['Resolution']
assert (resolution['Width'],resolution['Height'],resolution['OutputWidth'],resolution['OutputHeight'],resolution['Fraction'])==(1920,1080,1920,1080,1)
arrays={}
for row in rows:
    pixels=np.asarray(Image.open(root/row['File']).convert('RGB'),dtype=np.int16)
    assert pixels.shape==(1080,1920,3)
    arrays[Path(row['File']).stem]=pixels
regions={'FullImage':(0,0,1920,1080),'WorldRegion':(0,90,1920,840),'UpperWall':(650,175,800,380)}
pairs=[('serial-a','serial-b'),('parallel-a','parallel-b'),('serial-c','serial-d'),('serial-b','parallel-b'),('parallel-b','serial-d'),('serial-b','serial-d')]
comparisons=[]
for first,second in pairs:
    for region,(x0,y0,x1,y1) in regions.items():
        delta=np.abs(arrays[first][y0:y1,x0:x1]-arrays[second][y0:y1,x0:x1]);maximum=delta.max(axis=2)
        comparisons.append(dict(First=first,Second=second,Region=region,MeanAbsoluteChannelDelta=float(delta.mean()),ChangedPercent=float((maximum>0).mean()*100),AboveFourPercent=float((maximum>4).mean()*100),MaximumChannelDelta=int(delta.max())))
output=dict(Paused=True,ActualModesAndWorkersVerified=True,LogicalDraws=rows[0]['LogicalDraws'],PhysicalPackets=rows[0]['PhysicalPackets'],Requests=rows,Comparisons=comparisons,Scope='Exact workload/camera and repeat-controlled RGB differences; no FPS or complete DX11 parity claim.')
(root/'pixel-delta.json').write_text(json.dumps(output,indent=2),encoding='utf-8')
for row in comparisons:
    if row['Region']=='WorldRegion':print(row)
