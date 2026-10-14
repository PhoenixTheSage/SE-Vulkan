import argparse, hashlib, json, re, struct
from pathlib import Path
parser=argparse.ArgumentParser(description='Verify recorded Phase #4 controlled fixture evidence, separately from visual parity.')
parser.add_argument('--vulkan',type=Path,required=True);parser.add_argument('--dx11',type=Path,required=True);parser.add_argument('--output',type=Path,default=Path('TestResults/TransparencyAcceptance.json'))
a=parser.parse_args();reports=a.vulkan/'Reports';other=a.dx11/'Reports'
for folder in (reports,other):
    assert not (folder/'fixture-failed.txt').exists(),f'Failed fixture: {folder}'
    for name in ['fixture-completed.txt','fixture-unloaded.txt','exit-requested.txt','world-display-passed.txt']:
        assert (folder/name).exists(),f'Missing {folder/name}'
    for name in ['glass-particles','flare','rain','snow','after-expiry']:
        data=(folder/f'transparency-{name}.png').read_bytes()
        assert data[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',data[16:24])==(640,480),f'Invalid capture: {name}'
assert (reports/'harness-disposed.txt').exists(),'Vulkan graceful disposal missing'
history=(reports/'status-history.txt').read_text(encoding='utf-8-sig');final=(reports/'status-latest.txt').read_text(encoding='utf-8-sig');expiry=(reports/'transparency-status.txt').read_text(encoding='utf-8-sig')
assert 'active: Vulkan' in final and 'enabled: 1; errors: 0' in final
assert not re.search(r'enabled: 1; errors: [1-9]',history)
assert 'Resources: textures=' in final and re.search(r'Resources: textures=\d+, meshes=0',final)
assert re.search(r'Scene: actors=0, instances=0, models=0',final)
particles=[tuple(map(int,m)) for m in re.findall(r'GPU frames=(\d+), compute dispatches=(\d+), indirect draws=(\d+), completed alive/dead=(\d+)/(\d+), indices=(\d+)',history)]
assert particles and max(x[3] for x in particles)>0,'No completed alive-particle evidence'
assert 'completed alive/dead=0/409600, indices=0' in expiry and particles[-1][3:]==(0,409600,0),'Particles did not expire/reset'
transparency=tuple(map(int,re.search(r'Stock glass draws=(\d+), instances=(\d+), OIT resolves=(\d+), decal draws=(\d+), glass layer draws=(\d+)',final).groups()))
assert min(transparency)>0,'Missing glass/OIT/decal/layer submission evidence'
flares=tuple(map(int,re.search(r'GPU depth queries=(\d+), additive flare billboards=(\d+)',final).groups()));assert min(flares)>0
observer=json.loads((reports/'d3d11-observer.json').read_text(encoding='utf-8-sig'))
assert observer['readyBeforeRenderInitialization'] and not observer['attempts'] and not observer['successes']
def cameras(folder):
    return {m[0]:m[1] for m in re.findall(r'(transparency-[^;]+); camera=([^;]+);', (folder/'capture-camera.txt').read_text())}
assert cameras(reports)==cameras(other),'Held capture requests use different camera positions'
launch=json.loads((a.vulkan/'launch.json').read_text(encoding='utf-8-sig'))
for file,key in [('plugin.dll','PluginSha256'),('Assets/Native/win-x64/se_vulkan_native.dll','NativeSha256')]:
    assert hashlib.sha256((a.vulkan/'Pulsar/Legacy/Local/Vulkan'/file).read_bytes()).hexdigest().upper()==launch[key]
result={'Scope':'Controlled Phase #4 activity, expiry and cleanup; broad visual parity remains pending','VulkanRun':str(a.vulkan),'Dx11Run':str(a.dx11),'PluginSha256':launch['PluginSha256'],'NativeSha256':launch['NativeSha256'],'ValidationErrors':0,'GlassDraws':transparency[0],'GlassInstances':transparency[1],'OitResolves':transparency[2],'DecalDraws':transparency[3],'GlassLayerDraws':transparency[4],'FlareQueries':flares[0],'FlareBillboards':flares[1],'ParticleFrames':particles[-1][0],'ParticleDispatches':particles[-1][1],'ParticleDraws':particles[-1][2],'PeakObservedAlive':max(x[3] for x in particles),'FinalAlive':particles[-1][3],'FinalDead':particles[-1][4],'MatchedCaptureCameras':cameras(reports),'D3D11Observation':observer['scope'],'RenderDocCaptures':[p.name for p in reports.glob('*.rdc')],'VisualAcceptance':'Separate manual review; lighting/probe/quality gaps remain'}
a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(result,indent=2)+'\n');print('PASS controlled transparency/particle fixture evidence: '+str(a.output))
