"""Check an independently captured live stock component size decision."""
import argparse
import json
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('reports',type=Path);a=p.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
result=read(a.reports/'result.json');workload=read(a.reports/'geometry-workload.json')
assert result['Backend']=='Dx11' and result['Instrumented'] and (result['Width'],result['Height'])==(1920,1080)
views=[row for row in workload['OldGeometryViews'] if row.get('SizeCandidates')]
actors=[actor for view in views for actor in view['SizeCandidates']];assert actors
mismatches=[actor for actor in actors if actor['PredictedSizeCull']!=actor['ActualSizeCulled']]
output=dict(Source=str(a.reports.resolve()),Actors=len(actors),Views=[dict(ViewId=view['ViewId'],ViewType=view['ViewType'],Actors=len(view['SizeCandidates']),ActualCulled=sum(actor['ActualSizeCulled'] for actor in view['SizeCandidates'])) for view in views],ActualCulled=sum(actor['ActualSizeCulled'] for actor in actors),PredictedCulled=sum(actor['PredictedSizeCull'] for actor in actors),Mismatches=mismatches,
    Scope='Stock owning render job: compare independent rule evaluation using live stock distance, extent, final LOD and projection against actual MyRenderableComponent.IsCulled. This validates rule equivalence on sampled candidates, not cross-backend scene/pixel parity.')
(a.reports/'dx11-size-contract.json').write_text(json.dumps(output,indent=2),encoding='utf-8')
print(f"Stock actors {len(actors)}, culled {output['ActualCulled']}, mismatches {len(mismatches)}")
