# Presentation overlap experiment

Update 2026-10-08: presentation overlap now defaults on after new high-wait
both-order pairs saved 4.94-5.54 ms with independently improved completed-call
cadence. SE_VULKAN_ASYNC_PRESENT=0 forces the synchronous path. The historical
low-wait results below remain scoped to their original condition; see
Performance-2026-10-08-PresentBackpressure.md for promotion evidence.

Earlier ordinary traces measured~4.66ms inside synchronous vkQueuePresentKHR.
Khronos explicitly permits this call to block and requires external synchronization
of its queue. The experiment gives one immutable call to a worker; the render
owner joins it before every queue submit, begin-frame acquire, resize/destruction,
cleanup or mode switch. No concurrent queue access, renderer mutation or changed
GPU/semaphore/image-layout contract. Per-image present semaphores remain reused
through acquire as required by the Vulkan guide.

References: [vkQueuePresentKHR](https://docs.vulkan.org/refpages/latest/refpages/source/vkQueuePresentKHR.html)
and [swapchain semaphore reuse](https://docs.vulkan.org/guide/latest/swapchain_semaphore_reuse.html).

Native seven CTest cases pass. The hidden HWND case exercises20 async calls after
five synchronous calls, exact capture bytes, minimize/restore, resize, mode switch,
completed25 calls and zero validation errors. The experiment is opt-in
SE_VULKAN_ASYNC_PRESENT=1 and off by default. Async results and actual completed
call timestamps are applied on the render owner, not raced into shared statistics.

## Current ordinary paired result

Full1080p copied source, usual warm45/sample30 then warm5/sample30, with LOD/group/
size/far experiments fixed. Both pairs have matching build/native/source hashes.

- Forwardb76e2baa...:10.2476ms sync versus10.1644ms async; renderer saves0.0833ms,
  completed-present-call interval saves0.0821ms.
- Reverse0c27f47a...:10.0654ms async versus10.2690ms sync; renderer saves0.2037ms,
  completed-call interval saves0.1994ms.

Actual completed-call coverage lags accepted submitted rows by one. Completion
interval agrees with render return interval within0.15ms; this verifies the shorter
return is not merely leaving unfinished presentation work uncounted. Neither
metric is physical scanout FPS. GPU timestamp and scanout remain separate.

The synchronous call itself is now~0.15ms; the prior~4.66ms condition is absent.
Its cause is not established and the cross-run5ms improvement is not attributed
to this worker. The current0.08–0.20ms savings do not establish a substantial macro
gain. Keep policy off; do not tune small details to rescue an unsupported claim.
Evidence: TestResults/AsyncPresent-Comparisons.json and each run's present-flow.json,
result.json, frame-profile.json and native-performance.json. Report helper verifies
actual modes, build/source, completed coverage and cadence independently.

Native flow64-byte ABI remains useful for future driver-wait investigations. The
current~10.1–10.3ms CPU budget is still far above4.17ms, and full DX11 visual parity
remains open. Refresh the CPU owner profile under this state before selecting
whole-scene/per-view preparation work. No default flip or completion claim.
