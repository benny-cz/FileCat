# RAR5 parent planning and complete stream rewrites

**7 November 2026.** The RAR preservation assembly now has a portable parent planner and a complete stream rewrite
engine. This advances RAR integration independently of the remaining 7z graph work. It is a component checkpoint;
role-bound worker requests/secrets, native transactions/capture owners, application workflows and production
qualification remain open. No RAR write profile is registered in the application.

`ArchiveRar5TransformPlan` derives an ordered expected manifest from a complete source assessment, exact member
occurrence keys and reviewed numeric input descriptors. Rename/delete/replace and flattened file/folder additions
share one plan. It validates the complete source metadata and namespace before applying deletions, rejects stale
or repeated edits and unused/missing inputs, renumbers surviving duplicate occurrences and prevents newly ambiguous
paths. Every output row identifies either a retained source occurrence, a captured input or a new empty directory.
The plan holds no password, payload stream, filesystem path or publication authority.

Retained and renamed entries keep their complete canonical metadata. Replacement content keeps attributes,
host, basic/extended times, owner data, checksum presence and encryption mode; its compression fields describe
the explicitly selected bounded algorithm-version-0 encoder, and its filter observations reset with the newly
encoded payload. New encrypted payloads use the requested work factor and fresh cipher material. New files/folders
use portable Unix modes and exact modification times. Unix seconds plus nanoseconds cover 0 through `uint.MaxValue`;
otherwise an exactly representable FILETIME is used. Times before 1601 or outside the Unix range with sub-100 ns
precision refuse rather than round. Existing representable source metadata is preserved byte-exactly.

The source/profile password modes must agree. Payload-only protection cannot silently disappear when an operation
removes every encrypted file; creation of a protected archive containing only folders requires hidden names.
Encrypted source members with neither a password check nor CRC/BLAKE2sp refuse because their password cannot be
established. Existing hidden-header work factors below the current creation minimum remain a known preservation
gap: this planner refuses the mismatch rather than silently changing the recorded factor. Separating retained
header KDF parameters from new-payload defaults remains work. Native captured-directory objects also require the
future capture-owner adapter; the planner refuses them instead of discarding their identity/cost binding.

`Rar5RewriteEngine` reads and verifies the complete strict source catalog, including deleted/replaced members,
before touching the output. It checks the reviewed whole-source revision, rebuilds the parent-equivalent plan,
and feeds exact retained-payload witnesses or fresh captured input views to `Rar5ArchiveAssembler`. Retained
compressed/encrypted bytes must match their packed SHA-256. Captured payloads are counted and encoded through
fresh views, with complete content/digest checks on both passes. The assembler's semantic manifest must equal the
planned result. A final bounded pass hashes the complete source again, including headers and deleted regions.
It reads the declared extent and one EOF probe, so a stream cannot extend this pass indefinitely by returning an
arbitrary tail. Owned hash buffers are cleared; source and output streams remain borrowed.

The worker-side planner charges already retained physical/logical observations alongside request maps, metadata,
output expectations and payload witnesses. The existing 64 MiB source dictionary ceiling remains a verification
policy. This is local retained-memory accounting; complete job-wide work/IO and native hard-resource qualification
are still required. The Core project shares its internal observed-memory entry point with the existing reader
and dedicated writing assemblies. The writing closure continues to exclude Rar15 and DiscUtils.

The [checkpoint record](rar5-transform-checkpoint.json) pins the two source packets and frozen platform evidence,
compiler-source inputs, actual CLR, standard Windows account and independent reader identity. The initial full
matrix has **2,892 cases / 43 new cases** per platform and passes. Source review then bounded the final hash pass
and added six cases for an arbitrary tail, wrong passwords and remove-all behavior. The final expanded matrix has
**2,898 cases / 49 new cases**, with zero failures:

| Platform | Passed | Skipped | New cases passed |
|---|---:|---:|---:|
| Mac | 2,843 | 55 | 49 |
| Ubuntu VM | 2,856 | 42 | 49 |
| Windows VM, standard account | 2,678 | 220 | 49 |

Each phase/platform independently checks **36 native archives, 108 extracted payloads, 162 exact raw metadata
records and 18 retained packed-stream witnesses**. The eighteen workflows cover all six creation methods and
three password modes. They create a source and perform rename/replace/delete/folder/file additions together;
one captured input is reused through independent views. An independent Python parser checks complete header CRCs,
metadata field/presence bytes and features, while pinned official 7-Zip tests/extracts every archive/file. Semantic
manifests and deterministic unencrypted archive hashes match across platforms and phases. Encrypted physical hashes
are individually recorded rather than expected to match fresh salts/IVs.

The remaining cases exercise stale/conflicting occurrences, unused/duplicate/missing input grants, path collisions,
unrepresentable source metadata before deletion, damaged deleted/replaced payloads before output/capture access,
source changes after verification, captured input changes between passes, borrowed ownership, time limits,
cancellation, pre-allocation memory refusal, wrong passwords and password-preserving/refused remove-all outcomes.
The tail canary permits only the bounded end probe. All three changed C# inputs match the final platform compiler
snapshots. Host execution is limited to builds and static artifact validation; runtime tests/oracles execute only
on the authorized Mac/VMs. Final censuses contain no owned FileCat/test processes and preserve the Mac wake helper.

This checkpoint does not complete all RAR versions, new compression/filter encoding paths, password UI/browsing/edit
lifetimes or update/native-publication workflows. The broader encoder/profile inventories and resource/crash/fuzz/
six-RID/package gates remain required. The next integration work connects these parent/stream contracts to shared
worker requests, role-bound password consumers and native capture/publication without waiting for every 7z graph.
