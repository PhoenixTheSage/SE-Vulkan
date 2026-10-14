"""Read-only installed-contract prediction; no culled rendering or FPS claim."""
import argparse
import collections
import json
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('reports',type=Path)
args=parser.parse_args();root=args.reports
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
result=read(root/'result.json');workload=read(root/'geometry-workload.json')
assert result['Instrumented'] and (result['Width'],result['Height'])==(1920,1080)
actors=workload['MainCandidates'];assert actors
assert all('StockSizeWouldCull' in actor for actor in actors)
assert all(actor.get('VolumeSource') in ['AuthoritativeLocalAabb','AuthoritativeWorldBounds'] for actor in actors), 'Native VolumeExtent cache is stale and cannot define the prediction.'
predicted=[actor for actor in actors if actor['StockSizeWouldCull']]
eligible=[actor for actor in predicted if actor['StockActorPolicyEligible']]
models=collections.Counter(actor['ModelKey'] for actor in eligible)
for actor in predicted:
    assert actor['ViewerDistanceSquared']>actor['FinalAuthoredLodDistanceSquared']
    assert actor['ProjectedPixelArea'] is not None and actor['ProjectedPixelArea']<20
    assert actor['SizeContractInputsKnown']
output=dict(Source=str(root.resolve()),Instrumented=True,AppliedPolicy=False,MainCandidates=len(actors),
    PredictedStockCullActors=len(predicted),EligibleRigidActorPredictions=len(eligible),
    PredictedModels=[dict(Model=model,Actors=count) for model,count in models.most_common(20)],
    Examples=sorted(eligible,key=lambda actor:actor['ProjectedPixelArea'])[:20],
    Scope='One read-only candidate snapshot under installed old-pipeline distance/extent/projection/last-LOD/20-pixel semantics. No rendering change, GPU index-savings prediction, quality acceptance or ordinary FPS claim. Native unparented AABB cache replaced by authoritative scene bounds.')
(root/'stock-size-contract-audit.json').write_text(json.dumps(output,indent=2),encoding='utf-8')
print(f"{len(actors)} main candidates; {len(predicted)} stock-size predictions, {len(eligible)} rigid-policy eligible")
