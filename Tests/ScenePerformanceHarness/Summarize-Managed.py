"""Read evented dotnet-trace Speedscope output without treating waits as CPU use."""
import argparse
import collections
import json
from pathlib import Path


def summarize(profile, frames):
    if profile['type'] != 'evented' or profile['unit'] != 'milliseconds':
        raise ValueError('Expected evented millisecond thread-time samples')
    stack = []
    inclusive, callers, leaves = (collections.Counter() for _ in range(3))
    native = collections.Counter()
    chains = collections.Counter()
    buckets = collections.Counter()
    previous = profile['startValue']
    covered = 0.0
    for event in profile['events']:
        now = event['at']
        if now < previous:
            raise ValueError('Non-monotonic events')
        elapsed = now - previous
        if stack and elapsed:
            names = [frames[index]['name'] for index in stack]
            covered += elapsed
            bucket = next((label for match, label in (
                ('VulkanScene.Draw(', 'Scene frontend'),
                ('NativeBackend.QueueWorld(', 'Packet bridge and native queue'),
                ('VulkanVoxels.Draw(', 'Voxel frontend'),
                ('NativeBackend.EndFrame(', 'Native record/submit/present boundary'),
                ('MyVulkanRender.Present_Patch', 'Present boundary and harness postfix'),
            ) if any(match in name for name in names)), 'Other renderer/engine')
            buckets[bucket] += elapsed
            for name in set(names):
                inclusive[name] += elapsed
            leaves[names[-1]] += elapsed
            real = [name for name in names if name not in ('CPU_TIME', 'UNMANAGED_CODE_TIME')]
            if real:
                callers[real[-1]] += elapsed
                if names[-1] == 'UNMANAGED_CODE_TIME':
                    native[real[-1]] += elapsed
            if any('VulkanVoxels.Draw(' in name for name in names):
                for name in real:
                    if 'ReflectedConstantBuffer.' in name:
                        chains[name] += elapsed
        if event['type'] == 'O':
            stack.append(event['frame'])
        elif event['type'] == 'C':
            if not stack or stack.pop() != event['frame']:
                raise ValueError('Unbalanced close event')
        else:
            raise ValueError('Unknown event type')
        previous = now
    if stack:
        raise ValueError('Unclosed stack')
    def ranked(counter):
        return [{'Frame': name, 'ThreadTimeMs': value} for name, value in counter.most_common(30)]
    return {
        'Thread': profile['name'], 'CoveredThreadTimeMs': covered,
        'Inclusive': ranked(inclusive), 'SampleLeaf': ranked(leaves),
        'CallerAfterPseudoLeafRemoval': ranked(callers),
        'OpaqueNativeBoundaryCaller': ranked(native),
        'VoxelConstantBufferInclusiveChain': ranked(chains),
        'NonOverlappingThreadTimeBuckets': dict(buckets),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('trace', type=Path)
    args = parser.parse_args()
    data = json.loads(args.trace.read_text(encoding='utf-8-sig'))
    frames = data['shared']['frames']
    selected = [profile for profile in data['profiles'] if any(
        'MyVulkanRender.Draw_Patch' in frames[event['frame']]['name']
        for event in profile.get('events', []))]
    if len(selected) != 1:
        raise ValueError(f'Expected one render thread, found {len(selected)}')
    result = {
        'Source': str(args.trace.resolve()), 'Instrumented': True,
        'Scope': 'Sampled render-thread wall time, including waits and native boundaries; not CPU utilization or benchmark FPS.',
        'Caveats': [
            'Inclusive entries overlap and must not be summed.',
            'Removing pseudo leaves names the boundary caller, not exclusive managed execution.',
            'PollGC samples alone do not establish a garbage-collection bottleneck.',
            'Native boundaries do not identify native functions or Vulkan passes.',
        ],
        **summarize(selected[0], frames),
    }
    output = args.trace.with_name('managed-summary.json')
    output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(output)


if __name__ == '__main__':
    main()
