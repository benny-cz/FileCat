# RAR and 7z public options and encoder adapters

**7 November 2026.** Shared integration now has explicit version-2 public options, canonical bindings and stream
encoder/password adapters. This is an implementation checkpoint, **not production format or workflow qualification**.
No new write profile is registered or offered by the application.

`ArchiveWriteFormat.Rar5` appends wire value **9**; existing values 0–8 retain their meanings. Existing version-1
profiles retain their exact 16-byte canonical encoding, manifest digests, explicit transaction JSON and evidence
layouts. They reject additional codec/encryption intent that their encoding cannot carry. RAR5 requires version 2.
New immutable options cover seven 7z codecs, thirteen filter choices, raw/LZMA2 headers, bounded LZMA dictionaries
and PPMd order/model memory, all six current RAR5 methods, and unencrypted/data/data-and-names modes. New encrypted
outputs have an explicit work factor 19–24; unencrypted intent requires zero work factor. No password, salt, IV,
derived key, password check or user pathname belongs to the public options.

The new factories select LZMA2 payloads and headers with an 8 MiB dictionary for 7z, and normal compression with
the current 128 KiB, algorithm-0 RAR5 encoder. Level/dictionary/model/Delta choices are checked for format/codec
compatibility and effectiveness. These are current encoder/profile policies, not format maxima or impossibility
claims. RAR algorithm 1, write filters, broader dictionaries and exact preservation of every observed metadata/graph
remain implementation and admission work. Header LZMA and other encoded-header graphs also need further writer/
strict-decoder qualification. There is no fallback from a refused option to another method or password mode.

The version-2 profile is exactly **52 bytes**, thirteen signed little-endian 32-bit integers:

| Offset | Meaning |
|---|---|
| 0, 4, 8, 12 | Format, profile version, level/method, dictionary bytes; version-1 prefix |
| 16, 20 | 7z codec and filter |
| 24, 28, 32 | PPMd model order, model memory bytes, Delta distance |
| 36 | 7z header compression |
| 40, 44 | Encryption mode and work-factor power |
| 48 | RAR algorithm version |

RAR's 7z-only fields and 7z's RAR-only field must be zero. Unknown versions/values, incompatible/ineffective
options, incomplete extensions and trailing profile bytes refuse. The explicit transaction JSON has four keys
for version 1 or exactly thirteen for version 2, with no ignored unknown or omitted fields. Generic .NET JSON
round-trip is also tested; explicit canonical codecs define the persisted/wire formats.

One encoder now supplies the assessment/catalog and transform serializers, all three manifest digest domains,
creation witness, manifest sidecar, provenance sidecar and transaction JSON. The manifest sidecar keeps its
60-byte fixed prefix and puts the extra 36 option bytes before features/digests/rows; provenance keeps its
48-byte prefix and puts the extension before rows. Exact-length reservations and transaction provenance minima
include those bytes. Existing fixed headers, magic values and version-1 golden vectors remain intact. Request
hashes therefore bind every option for role-specific, nonce/sequence-bound secret grants. Altered options cannot
reuse a previous manifest, sidecar, creation witness, transaction scope or fresh worker's password grant.

`ArchiveWriterProfileAdapter` maps validated public intent to existing stream codec/header methods and archive-local
encryption sessions. It consumes one-use grants, clears them on validation/cancellation/failure and refuses missing
or unused grants. The caller must validate the worker role and complete canonical-request binding before providing
a received grant. Existing encrypted archive tests now use these public factories/adapters before calling the
writers, exercising actual output and independent readers rather than only DTO equality.

The [checkpoint record](public-options-checkpoint.json) pins the complete source packet, frozen artifacts, actual
test runtimes, standard Windows account, compiler-input checksums and final process censuses. The expanded combined
matrix covers **2,497 cases** on each machine, including **82 new contract cases** and the changed encrypted writer
paths. An independent Python encoder matches **60** complete public profiles, requests, manifest sidecars/all
three digests and provenance sidecars. Fresh archives cover all current codec/method and password/header choices
plus encrypted filter/BCJ2 and empty-container cases. Fifty-two of 56 archives pass official 7-Zip, with 194 exact
payload checks; four Zstandard extension archives retain the documented official-tool unsupported-method gap.
Private py7zr independently reads the 29 applicable 7z archives, 196 metadata records and 140 exact payloads,
including those four Zstandard archives. All ten cached reader wheels/module bytes and tool identities are checked
before use. Platform skips and exact pass counts remain explicit in the checkpoint.

All application tests and independent readers run on the authorized Mac/VMs. The host performs edits, builds and
static evidence verification. Owned test processes close, the Mac's until-reboot wake helper remains, and the shared
`E:\FileCat` checkout remains on `main`.

RAR/7z strict preservation adapters, generic container transform plans/worker handlers, role-aware runtime secret
consumers, assessment/admission services, create/update/edit/F6 and password UI remain unfinished. The existing
TAR transform plan/worker does not accept either container as a TAR request. Complete strict 7z graph verification,
aggregate work/pass/resource accounting, native containment, broad UI/live/fuzz/package gates and final independent
review remain required. Constructing or round-tripping public options grants no mutation or publication authority.
