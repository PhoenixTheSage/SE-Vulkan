# Static object payload ownership

Rejected and removed after both-order ordinary comparison. The description below
records the archived prototype, not an active production feature.

Experimental SE_VULKAN_STATIC_WORLD_OBJECTS=1 registers rigid/model generic
batch constants once. These constants contain identity transform, immutable
per-part tint/emission/dither and zero object HSV; current transforms/HSV remain
in the current instance stream. Registration owns an exact byte copy. Managed
canonicalization runs on a hint miss, not every frame. Native selection uses
recipe generation identity without per-frame byte comparison or object hashing.

Dynamic object offsets remain tightly packed in the current frame blob. Static
draws carry UINT32_MAX instead of an object span. The native bounded object arena
selects unique static recipes, reclaims unused ranges and gives every reused
range a new revision. Each completed GPU slot tracks those revisions independently.
Static bytes occupy a reserved prefix; all dynamic and retained offsets follow
it. Prefix growth invalidates dynamic slot versions; buffer growth invalidates
static versions and cached descriptor epochs. Legacy/retained modes reserve the
prefix too, preserving live static contents for later reentry.

All consumers receive actual GPU offsets, including indirect draw preparation.
Indirect object equality reads the completed slot's populated host-visible
buffer, covering both registered static data and current dynamic data. No extra
concatenation/copy is introduced for that optional lane. Existing selection,
instance, view masks, flags, ordering and resource generation checks remain current.

Native APIs add static registration and 32-byte static-object statistics while
preserving the existing 56-byte recipe and 48-byte dynamic draw ABI. Static
registration requires the exact reflected generic-batch object layout. The
registered bytes are independent of caller memory and garbage collection.

Current verification: seven native CTest cases pass; 388 world GPU checks pass
with zero synchronization validation errors. New cases cover exact two-slot
pixels/material/HSV, unique payloads across spatial cells, zero steady uploads,
legacy and retained modes, indirect GPU offsets, instance-buffer growth, texture
replacement, malformed registration/queue sentinels and caller mutation. The
113 scene checks pass with static objects enabled, including model/material
invalidation, current-view ownership and highlight identity.

Ordinary full1080p Pertam comparison is pending. Fixed policy includes async
presentation, native recipes, model batching and the existing experimental view/
LOD policies. Upload reduction is not itself a FPS claim. The <4.17 ms full-frame
target and complete DX11 visual parity remain open. Anchor and source hash map:
TestResults/StaticRecipe-Before-2026-10-08.zip/.json. Current ordinary forward run:
Vulkan-e14f06d47cc546a0be46d6ef008af472.

Final ordinary results:

- Forward e14f06d4: dynamic 13.432911 ms, static 13.470890 ms; static adds
  0.037979 ms (0.28%).
- Reverse 0728b2ee: static 13.151961 ms, dynamic 13.205281 ms; static saves
  0.053320 ms (0.40%).

The payload ownership works: roughly 4596-4616 static draws/frame share about
2233 unique objects, with only 4.4-9.7 average uploaded constant bytes/frame after
warmup. That transfer reduction does not establish useful frame-time improvement.
Existing indirect compatibility already compares equal object bytes across
different offsets, so this prototype does not unlock new command groups merely
by sharing offsets. Do not tune it further as a performance optimization.

Sources and manifest archived as StaticRecipe-Rejected-2026-10-08.zip/.json,
SHA256 CCA9F300AB887CAF62D504F839E4FCF063E240FA55A4E6A38C82F5278637AA3D.
All anchor paths were restored byte-for-byte, added managed source removed, and
the focused extra arena test removed. Existing async-default gain is retained.
Restored Release builds/deployment and all seven native CTest pass; restored
world GPU suite passes368 checks and scene suite113, with zero validation errors.
The comparison reader remains for preserved evidence. Next architecture must
remove actor/packet/command work itself rather than only constant copying.
