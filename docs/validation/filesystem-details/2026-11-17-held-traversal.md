# Held Windows compression adapter — 2026-11-17

This continues the [search and ownership checkpoint](2026-11-17-search-and-storage.md) on `main`. WP2 now has an internal Windows inspection/traversal/mutation adapter. WindowsPlatform still composes the unsupported adapter, and StorageTransformExecutor still refuses execution. Durable call records, atomic job control, recovery, review UI and remaining native qualification must close before enabling the product action. EFS remains explicitly required by the user's active goal; its unresolved prerequisites are not a scope omission.

## Implemented boundary

WindowsStorageTransforms validates the captured ordinary unnamed-stream spelling again at its native entry. Preparation requires the Core-owned root-pin reservation/context. It opens the actual drive/volume root, queries held FileFsDeviceInformation, FileFsSizeInformation and volume capabilities, and refuses remote/non-NTFS/unknown root bindings. A drive alias whose root is not the actual volume root is refused instead of silently canonicalizing away its ancestry. Drive, extended-drive and volume-GUID spellings are covered by the fixture below.

StorageObjectIdentity now carries a verified volume binding in addition to native volume serial and 128-bit file ID. The adapter obtains the actual volume GUID from each opened object's handle and compares it with the admitted containing volume. Distinct verified bindings prevent cloned-volume serial/ID collisions from causing deduplication or protected-tree misclassification. Closed IDs remain hints; this change is not a substitute for live pins.

Each component opens relative to its held parent, with FILE_OPEN_REPARSE_POINT, no forced case folding and one validated raw child name. Case mode is queried. Metadata-only ancestors retain no-delete sharing; the review leaf pin shares read/write/delete and survives review/queueing. Execution freshly walks the original captured spelling and compares it with that live pin. A moved original/replacement at the old spelling is stale; no old-ID search or pathname fallback occurs.

Protected-tree classification binds the platform-known Windows directory and compares held identities/ancestry. An invalid, missing, cloud-managed or reparse-backed Windows-directory anchor makes classification unknown and refuses inspection. Execution retains and revalidates that anchor. System targets, volume roots, reparse/cloud/offline/pinned entries, multiple regular-file names, sparse entries and external/WOF backing are excluded. An ancestor's System attribute alone does not create a denylist. Ordinary names such as Windows, Program Files and ProgramData do not create protected status.

Eligibility uses held metadata and main-stream GET compression. It does not enumerate ADS, query EFS status/users/keys, read file payloads or call a legacy details reader. ReadOnly regular files refuse a required change; known no-ops require no mutation handle. Directory ReadOnly markers remain intact. The enable-only ceiling is 30,000,000,000 bytes. Known allocation units above 4096 are a distinct FileCat boundary, and unknown units remain unknown. EFS input is the held encrypted attribute; detailed EFS work remains separate and unfinished.

Cloud Files membership uses [CfGetSyncRootInfoByHandle](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/nf-cfapi-cfgetsyncrootinfobyhandle), which accepts READ_ATTRIBUTES access. Only the documented/native-qualified not-under-sync-root error 390 is accepted as this API's negative result; other failures remain unknown. Positive sync membership refuses even hydrated content. Queried basic attribute bit 0x40000 is treated as the EA indication rather than enumeration's RECALL_ON_OPEN. Broader registered-provider and fetch-sentinel fixtures are still open; this is not a universal no-provider-activity guarantee.

## Traversal and mutation ownership

WindowsStorageDirectoryCursor borrows the admitted directory handle and uses NtQueryDirectoryFile/FileIdExtdDirectoryInformation with a 64 KiB buffer. It yields bounded detached batches with exact names, kind/tag and 128-bit IDs. It validates byte counts, progress, offsets, UTF-16 and component grammar; zero/unknown IDs cannot become mutation authority. It completes only on the native terminal status. It opens no second enumeration handle and retains no descendant tree.

The traversal compares each opened child with its cursor identity/kind before permitting mutation. Active frames retain no-follow ancestry, and relevant identity/case/reparse/cloud state is rechecked. Observed churn refuses the remaining branch. The 64-level depth bound includes ancestors above the selected root. Per-object paths and buffers belong only to active bounded frames; final global visited/overlap/outcome accounting remains worker work.

PrepareMutation and ApplyAdmitted are separate portable lease operations. Preparation acquires/validates any required handle and can cancel before a native mutation. The caller must then reserve durable result capacity, flush intent and atomically admit one call. ApplyAdmitted accepts no cancellation token, consumes that preparation once, invokes SET on the same handle and attempts a held postcondition. A prepared no-op remains a no-call outcome. Precall observation failure is NotProcessed/no call; an ambiguous entered call or unreadable result is Uncertain. Known desired state after a failed native return remains Changed with an issue. The job-layer atomic admission and persistence protocol are not implemented by this native seam alone.

A regular mutation handle uses READ_DATA | WRITE_DATA | READ_ATTRIBUTES | SYNCHRONIZE and share-read only. A directory needing change uses the same bits under their directory aliases and retains that exact mutation handle for its cursor/ancestor ownership. An already-satisfied recursive directory acquires only listing/attributes/synchronization rights, sharing read/write and denying delete. If a no-op directory changes, a mutation handle is acquired while continuity remains held; a new prepared/admitted call is required before descent. There is no ReOpenFile or path reopen fallback.

StorageNativeAllowance makes the fixed 188-slot owner concrete: 128 traversal/ancestor slots plus 60 scratch slots. Opens are charged first. Handle promotion temporarily uses a scratch slot, validates identity, closes the old metadata/listing handle while its replacement remains held, then transfers the replacement to the released traversal slot. A retired allowance retains exclusive process capacity until its last charged handle returns. Nested independent acquisition fails instead of self-waiting. Review pins retain their separate whole-root allowance. The future journal writer and history/evidence lanes are not yet part of a fully qualified 448-handle implementation.

Policy exclusions are warnings, distinct from permission/unknown/native failures. They do not turn an all-policy-skipped pass into an error by default. Native and per-scope result reduction remain separate.

## Verification

The final disposable Windows 11 x64 run passed **17 tests**, zero failures/skips:

- WindowsStorageInspectionTests (4): ordinary file/directory volume binding, ReadOnly change/no-op distinction, held protected-tree identity versus lookalike/System ancestor, unknown/redirected protected anchor, reparse ancestor/leaf, sparse and hard-link exclusions, and a 1,100-entry cursor spanning multiple batches with each native ID checked against a held-relative child.
- StorageDirectoryCursorTests (2): detached exact/case-sensitive names/IDs/kinds/tags, malformed offsets/lengths/UTF-16/component names, bounds and unknown IDs.
- WindowsStorageTraversalTests (8): recursive compress/remove with live review pins and ReadOnly directory preservation; no-op directory listing sharing; ReadOnly file no-op with an existing writer; original rename/replacement refusal; writer conflict before call followed by fresh preparation; case-sensitive siblings and drive/extended/volume aliases; reparse descendant skip with untouched target; canceled preparation versus admitted postcondition; inclusive ancestor/depth limits. Some related checks share one fixture.
- WindowsCompressionNativeTests (3): main/named stream states and SHA-256 preservation, held directory ABI and postcondition after native refusal.

These are prepared native protocol fixtures. Their direct admitted calls are test setup/qualification, not proof that the product's durable job call protocol exists.

The focused Core contracts/owners/allowance/scheduler selection passed **42 tests** on the host. The broader focused lifecycle selection passed **184 tests on Windows** with one mounted-filesystem skip and **183 tests on each of Ubuntu and macOS** with two platform/fixture skips each (185 discovered per platform). Windows recovery ran at medium integrity. App test projects compiled on all three platforms with zero warnings/errors. These Core runs include separate slot caps, duplicate-return protection, retention through late handle retirement, nested-acquisition refusal and colliding serial/ID cases on distinct verified bindings.

Final native Windows platform binary SHA-256: `087C7E021898BD9D5A4686248E53B2BE31AD01C6C45B2BF76DA06248CA82E7C6`.

Source SHA-256 values at that checkpoint:

| Source | SHA-256 |
|---|---|
| WindowsStorageTransforms.cs | `0C66E57A11FFC1D5534D89EF4D99F7E3EEB941A17A849D265A0EEC1D5FA22B87` |
| WindowsStorageTransforms.Inspection.cs | `9C611A6F2A05BB3919EA9E351202ACE76BF2991931FE86DDC96321BF7156B2ED` |
| WindowsStorageTransforms.Traversal.cs | `FB61514A469B4E2A8ABB3261C72D6363337E40B5EDCECA4DD5C2E683EBF1E153` |
| WindowsStorageDirectoryCursor.cs | `97793D5B4A29769396E95156E091A99C9AA7FAC8E88881211C14AA48DC647023` |

## Remaining acceptance

WP2/N-C1/N-C2/N-ID remain partial. Complete closed-ID rediscovery/overlap attribution in the worker, deterministic cursor replacement and attribute-only adversary probes, recognized cloud/fetch sentinels and wider volume/provider capabilities, enable-boundary/large-removal, ACL/full/device-loss/removable/mapped-network/non-NTFS cases, and creation/move/inheritance observations. Do not infer those outcomes from the ordinary NTFS run.

WP3 must connect the prepared held call to control-lock admission, exact intent/result persistence, bounded census/retention/recovery and terminal owner retirement. The Windows adapter and UI stay unavailable until those gates and the remaining review/refresh/UI qualification pass. EFS protocol/key/output work and the filesystem plan's remaining collectors/native/output gates also remain part of the active objective.

## Native cursor replacement, 2026-11-19

On the disposable Windows 11 x64 VM at medium integrity, the cursor returned one child from a native batch and kept the other child observations pending. The fixture moved a compressed child out and moved a different compressed file into its captured name. The directory default was already clear, so traversal was using its listing handle. The stale entry reported an identity change and attempted no compression call. Both moved and replacement files retained their payload and compression. The unchanged sibling had compression removed and retained its payload.

The x64 receipt records **1 passed, 0 failed, 0 skipped** in `.tmp/filecat-efs-live-166b6897-r1/cursor-vm-4a3450ea-cursor-native-x64-r3.trx`. The test assembly SHA-256 was `8620E572771109A1BD6144594207A1A4812C3D6220707CA2F70CCD6FEC60494B`.

Native ARM64 at `9a93b8fe` passed the same case and all 12 other native storage cases without skips. The downloaded `native-storage-abi.trx` SHA-256 is `6271FF648511F85FCEFA82941578F77E8C3EFFD798554A9536981CC58CB4AD00`. N-ID remains partial pending the other adversary cases. [Native ARM64 qualification run](https://github.com/benny-cz/FileCat/actions/runs/44560055582)

## Native ARM64 held directory and read-only no-op cases

At medium integrity on fixed 4 KiB NTFS, x64 and native ARM64 ran the same two held-traversal cases. Recursive Compress and Remove compression changed the parent default before enumerating through its held mutation handle. The review pins stayed live and the folder retained its ReadOnly marker. An already-compressed directory used its listing handle and admitted the separate writer used by the fixture. The already-compressed ReadOnly file likewise returned no native call while a compatible writer remained open. Both file payloads matched their inputs.

The x64 runner passed both cases. Its `native-storage-traversal-x64.trx` SHA-256 is `3E6F362EC302E4E562CA4043049BAAB7204B45B0F04DF1DD187D6872816C19D3`. The native ARM64 run passed all 16 storage ABI and job cases at `022bc304cabf2c182dfcbe5ca5b84ae4a7970993`. Its `native-storage-abi.trx` SHA-256 is `AFFC59B9A6BD5C8F1C75992C2254CC30923E49920C89A64966523F6F9C73ACD2`. The same run passed all 15 Recovery admission cases, including the nine Windows cases fixed after full CI, and all ten native storage command cases. [Native ARM64 test receipts](https://github.com/benny-cz/FileCat/actions/runs/44593580639)

## Review path replacement, writer conflict and case-distinct identities

On fixed 4 KiB NTFS, Windows x64 and native ARM64 passed three additional compression cases at medium integrity. Renaming the pinned object and placing another file at its reviewed name led to zero native calls and preserved both files. A writer with incompatible sharing caused a refusal before the compression call. Fresh preparation after it closed changed the same reviewed item and preserved its payload. An NTFS directory with its case-sensitive flag set kept the two exact sibling names and IDs distinct. Drive, extended-drive and volume-GUID spellings of a single fixture converged on its physical identity.

The x64 three-case TRX SHA-256 is `154CE164912D8C78FB20285C347DE38968C67C557E7F8CC962AE7A2548F8603E`. The ARM64 run at `26bfcec30aec2715cc35621a74a79cde59e339b1` passed all 19 ABI and storage job cases. Its `native-storage-abi.trx` SHA-256 is `CB86CF9F278AD28B50ED41F2053EC5BC0F4C07EBA646E6CC9D00FCEDEF8F6E48`. It also passed all ten native command cases and 15 Recovery admission cases. N-ID remains partial. [Native ARM64 test receipts](https://github.com/benny-cz/FileCat/actions/runs/44599137959)

### Hard link created after compression preparation

Windows 11 x64 build 26340 allowed the fixture to create a second name while FileCat held the compression-ready handle and the review pin. The final observation returned `multiple-names` and skipped before the compression call. Both names still exposed the same original bytes and neither name showed compression. The focused VM run passed all four identity cases with zero skips. The SHA-256 of `native-storage-alias.trx` is `B2BDD4508DFBF93D0FFD6BA6DCECFD2E0065BF3E75A51124591D8B40F83F0D3E`. The [EFS equivalent](2026-11-17-efs-native-binding.md#new-hard-link-and-alternate-local-path-spellings) uses its own pathname-binding checks.

Native ARM64 reproduced the same result at `080538ad`. Adding the link succeeded at medium integrity, and the held check refused before SET. All 20 ABI and storage job cases passed. The SHA-256 of `native-storage-abi.trx` is `F4C2D2914E2718483119ABBEBDFFF53AB9CE51458B729C299F143FC3D768CD7A`. [Native ARM64 qualification run](https://github.com/benny-cz/FileCat/actions/runs/44605022260)
