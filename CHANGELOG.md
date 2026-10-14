# SE-Vulkan changelog

## 1.0.1 — 2026-10-14

- Register an independently retried renderer-neutral Status page through the shared Anomaly terminal.
- Expose Show Status, Show Profiler, and Write report to log from that diagnostic page.

## 1.0.0 — previous release

Release SE-Vulkan 1.0.0 native Vulkan renderer

Version: 1.0.0 (first published release).

Changelog:
- Add a native Vulkan 1.4 renderer for Space Engineers 1.210.014. Windows covers Pulsar Legacy/net48 and Interim/net10. Native Linux uses Interim/net10.
- Select the renderer from Display, keep DX11 as the default, and recover to DX11 when assets, hooks, or incompatible plugins fail closed. A failed Vulkan launch can retry on the next start.
- Render stock materials and depth, lighting, skinning, and instancing, then asteroid clipmaps, planets, atmosphere, grass, transparency, weighted OIT, billboards, and GPU particles.
- Add probes, cascaded and spotlight shadows, SSAO/HBAO, exposure, bloom, tone mapping, FXAA, and dynamic resolution.
- Bundle libmpv and FFmpeg for stock intro and menu playback.
- Replay Anomaly guest fullscreen packets and catalog images, and expose display services for DLSS, FSR, frame generation, HDR, and hardware ray queries.
- Full visual parity, broad gameplay acceptance, and live Linux game verification remain pending.

Co-authored-by: Cursor <cursoragent@cursor.com>
