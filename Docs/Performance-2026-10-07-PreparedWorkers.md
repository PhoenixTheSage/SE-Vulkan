# Rejected parallel byte-production experiment

Measured prepared replay2.28ms suggested testing worker production. The prototype
reserved exact packet/batch order and growth on the render owner, then used up to
four workers to fill exclusively owned object constants and disjoint rigid-instance
byte ranges. All jobs joined before native copy. Duplicate identities, mode reset,
buffer relocation and worker exceptions had explicit handling. No worker GPU calls,
resource lookup, batch dictionary mutation or packet publication was allowed.

The prototype passed274 world GPU checks, including1024 actors, exact both-slot
pixels, multiple buffer growths, moving poses, HSV/visibility, stock LOD alpha,
duplicate submissions, mode switches, discarded collection and failed-worker retry.
Core/synchronization validation was clean. Full paused1080p Pertam serial/parallel/
serial images retained18690 logical draws and matching physical packets; world RGB
cross differences0.000308/0.000151 per255 versus repeat0.000147–0.000257 and return
0.000215. These small differences are comparable with temporal controls, not an
exact cross-backend visual-parity proof.

Ordinary results reject the optimization:

- Forward037887b4...: serial19.1066ms, parallel20.0663ms; parallel adds0.9597ms.
  Present spans5.4214/5.4318ms; the delta outside that span is0.9493ms.
- Reversee9eacf06...: parallel20.9658ms versus serial19.7877ms; adds1.1782ms.
  The delta outside the present span is1.1463ms.

Actual four worker threads processed3298 actors/14345 parts per frame. Owner
planning remained1.758–1.816ms, worker dispatch/join0.487–0.550ms, plus cleanup and
caller overhead. Byte production alone is too small; moving it adds work without
removing the owner-side planning dependency. Do not tune small clock/copy/hash
details to rescue this result or claim a speedup.

GPU presentation call duration increased to5.4–5.6ms from earlier0.18ms controls;
swapchain remains Mailbox, VSync off, three images. Cause is not established. The
within-pair presentation spans are nearly identical. Each pair uses one exact
binary. Exception recovery was added between pairs, changing hash; these are
independent negative comparisons, not a pooled identical-binary estimate. Timing
rows retain full1080p and copied source checkpoint. No source save was modified.

The experiment was removed from production and harness launch paths. Prototype
sources are archived in TestResults/ParallelPrepared-RejectedPrototype-2026-10-07.zip
(SHA83A8F2E153D117C966BF532BC86EF6DB1C817115B1AA9248E9ABDE9D7ECACC1E).
The archive contains flat filenames; its manifest maps them to original source
paths. JSON evidence is TestResults/ParallelPrepared-NegativeComparisons.json;
pixel rune3b34470... contains pixel-comparison.json and pixel-delta.json. The report
helpers remain for reading this preserved evidence, not as active launch features.

Next macro work must remove planning/visibility work or parallelize whole owned
preparation units, rather than isolating byte writes. Installed DX11 MyRenderScheduler
confirms dependency-scheduled prepare/render jobs; MyGeometryRendererOld's merge
instancing preparation returns null, validating the old-path workload audit's
coverage. Missing stock CheckDistanceCulling projects Owner.VolumeExtent and tests
pixel-area<20 only beyond the final authored LOD distance. It uses exact parent/
unparented distance and extent ownership, not a generic sphere threshold. Investigate
that contract next while preserving authored LODs, fades, highlight identity and
shadow/probe invalidation. The <4.17ms and complete visual-parity goal remains open.
