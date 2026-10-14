"""Compare camera slices against the fitted GPU shadow clip volume."""
import argparse
import json
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('reports',type=Path);a=p.parse_args()
read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
result=read(a.reports/'result.json');data=read(a.reports/'geometry-workload.json')
assert result['Instrumented'] and (result['Width'],result['Height'])==(1920,1080)
actors=data['ShadowCandidates'];assert actors
missed=[actor for actor in actors if actor['MissedLightCascades']]
extra=[actor for actor in actors if actor['ExtraCameraCascades']]
output=dict(Source=str(a.reports.resolve()),ActorBounds=len(actors),ActorsMissingFittedCascades=len(missed),ActorsWithExtraCameraCascades=len(extra),
    MissedCascadeMemberships=sum(actor['MissedLightCascades'].bit_count() for actor in actors),ExtraCascadeMemberships=sum(actor['ExtraCameraCascades'].bit_count() for actor in actors),Examples=missed[:12],
    Scope='Diagnostic model-bound disagreement only; includes non-casters/invisible actors. Actual rendered shadow loss or GPU gain is not established by this count. Fitted matrices are current after frame completion; preparation previously used camera-slice state fitted later.')
(a.reports/'shadow-membership-audit.json').write_text(json.dumps(output,indent=2),encoding='utf-8')
print(f"{len(actors)} bounds; missing fitted memberships on {len(missed)} actors; extra camera memberships on {len(extra)}")
