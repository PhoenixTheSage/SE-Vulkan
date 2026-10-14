# Presentation overlap under measured backpressure

The existing presentation worker now defaults on. SE_VULKAN_ASYNC_PRESENT=0
selects synchronous presentation. The worker owns one immutable present call;
the render owner joins before the next queue operation, acquire, resize or
destruction. Rendering, GPU passes, resolution, swapchain mode and per-image
semaphore reuse are unchanged.

New ordinary 1920x1080 private Pertam both-order results exercise the roughly
4.7 ms blocked call identified by ETW, unlike the earlier low-wait comparisons:

- Vulkan-4f318eed2eb54bac84a49a576df2b2a0: sync 18.070280 ms, async 13.133450 ms;
  saves 4.936830 ms (27.32%). Completed native present-call intervals improve
  by 4.940 ms and reach 13.183657 ms (~75.85 completed calls/s).
- Vulkan-39db5c5d6e3e4e4aa0fd1dc2a2e91549: async 13.127653 ms, sync 18.664304 ms;
  saves 5.536652 ms (29.66%). Completed-call intervals improve by 5.534 ms.

Same managed/native/source hashes and full-resolution policy in both runs;
only presentation scheduling changes within each pair. Retained recipes,
new actor views, whole-model batching, actor LOD/old-path size/far policies,
group occlusion, parallel cascades and parallel queue are fixed. These include
experimental opt-ins, so this is not the normal-policy total. Independent
unpaused selection/physics remain a caveat. Completed API call cadence is measured
separately from EndFrame return and is not monitor scanout FPS. The internal
driver cause of the blocking call remains unproved.

The default promotion changes only constructor policy, preserving explicit
runtime mode selection and synchronous opt-out. Canonical native Release/Test/
Deploy passes all seven CTest checks. The HWND case explicitly starts with five
synchronous captures, then checks twenty async captures, resize, minimize,
restoration, explicit join, mode switch, completed counts and zero validation
errors. Default-on and environment-zero behavior are asserted separately.
The environment-zero HWND rerun also passes. Managed net48/net10 builds pass;
both installed renderer copies match native SHA256
D7F6CDBB957D95ACD9C2B7EED1F251859C8501ADED353C5EA2EDED8888F721FD.
The performance pairs predate that constructor-only promotion and use EFDBFE...
native DLL; their modes were explicitly selected through the same API.

The failed actor-worker prototype is removed. This confirmed scheduling gain
addresses presentation backpressure, while approximately 13.13 ms remains well
above the full <4.17 ms target. Dark walls, glossy floor and emission differences
still prevent claiming full DX11 visual parity. Next macro work must remove
repeated scene/instance/packet construction and native command work, rather than
retune local worker/copy/predicate caches.

Evidence: TestResults/PresentBackpressure-Comparisons.json and each run's
result.json, native-performance.json, present-flow.json and frame-profile.json.
