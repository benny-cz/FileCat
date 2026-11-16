# Typed archive origin evidence checkpoint

2026-11-05, over `fc1cd966`. This implements bounded typed evidence and Windows held-object mark reads/copying. It
remains unregistered, with no new write profile enabled. It does not implement semantic zone/quarantine parsing,
strongest-origin aggregation across inputs, native Unix xattrs, full outer metadata/security snapshots, transaction
integration or marked-input release qualification.

## Implemented behavior

`ArchiveOriginSnapshot` distinguishes absent, present and unavailable evidence, with explicit access-denied, in-use,
IO, oversize, unsupported and changed failure reasons. The format identifies Windows Zone.Identifier, Mac quarantine
or Linux origin URL. The latter two values currently identify evidence only, not an implemented native adapter.
Present bytes are opaque private copies capped at the existing 64 KiB mark policy. Empty present bytes are distinct
from absent. No text decoder silently repairs invalid bytes, and unknown content never acquires a parsed trust level.

The schema-one SHA-256 covers five little-endian int32 fields (schema, format, state, failure, raw length), followed
by the exact raw bytes. An unavailable read cannot share a digest with absence or empty presence. This digest is
evidence, not authentication, publication permission or a cleanup receipt. Retaining origin evidence across all inputs
will also need job-wide aggregate metadata/heap charging; the per-attribute cap alone does not satisfy that requirement.

The Windows lease reads the actual named stream relative to its pinned directory. Before any ADS operation, a separate
regular-file name handle must still identify the originally held file; the original lease excludes external namespace
mutation and the directory gate serializes its own renames. The ADS handle must report the same file identity. Only
the exact native missing-stream result after the base-name check is absent. ACL/sharing/IO failures and unsupported or
over-limit streams remain unavailable. Size is checked before allocation, and short reads or length changes refuse
the capture. Cancellation propagates. Native names remain selected by the parent, never supplied by the worker.

`CopyOriginNoReplace` accepts only this transaction's newly created, sealed private artifact. Ordinary sources and
recovery leases cannot write marks. Unavailable evidence and foreign formats refuse without creating an ADS. An absent
input still requires the stage mark to be demonstrably absent. Present raw bytes are copied with native `FILE_CREATE`;
an existing or late mark is never overwritten/erased. ADS and original held-file flushes are checked, only the original
mtime is restored through the held artifact after the writing stream closes, and exact evidence, mtime and private ACL
are reread. A failed verification retains the artifact and its current mark.

These operations use the existing NTFS named-stream conventions described in
[Microsoft's file-stream documentation](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams).
They do not claim directory-entry power-loss durability or protection from equivalent-privilege hostile processes.
The coordinator must still derive and verify the strongest representable result, preserve original/input evidence,
reread actual retired/published marks, and refuse an unrepresentable aggregate instead of laundering provenance.
There is no fallback to the legacy nullable/path-taking origin APIs in this new layer.

## Validation

Three portable cases verify distinct canonical evidence for all formats/failures, frozen opaque bytes and independently
encoded digest layout, and invalid/oversized refusal. Eleven native cases cover genuine absence, empty presence,
locked/denied/oversized ADS states, cancellation, exact empty/UTF-8/UTF-16/invalid-text/64-KiB copies, unchanged payload
hashes and outer mtime, held-object rename, a newly marked retired source with unchanged bytes, preservation of existing
stronger marks, sealed-owned-only writes, and unavailable/foreign input refusal.

The new permission-denial case caught a fixture mistake: `SetAccessControl` persists modified descriptor sections, so
an untouched captured `FileSecurity` object did not restore the ACL. The fixture now marks the captured access section
modified before restoring. The earlier storage live-ACL case was corrected too and now ends with a native rename that
requires the restored private DACL. The denial itself correctly returned unavailable throughout. The failed intermediate
run remains in ignored evidence; no production permission failure was reclassified as absence.

Affected native filter is the 95-case storage/replacement filter in [the storage checkpoint](WINDOWS_ARCHIVE_STORAGE.md)
plus `WindowsArchiveOriginTests` (106 total). Core filter is `ArchiveOriginSnapshotTests` (3 total).

| Environment | Native/platform filter | Portable Core filter | Final evidence |
|---|---|---|---|
| Windows 11 x64 VM / NTFS, runtime 10.0.14, medium integrity | 104 passed, 2 skipped, 0 failed | 3 passed, 0 skipped/failed | `archive-origin-platform-vm3.trx`, `archive-origin-core-vm3.trx` |
| macOS ARM64 / APFS, source built, SDK 10.0.101 | 24 passed, 82 Windows-only skips, 0 failed | 3 passed, 0 skipped/failed | `archive-origin-platform-mac-final2.trx`, `archive-origin-core-mac-final2.trx` |
| Ubuntu x64 / ext4, source built, SDK 10.0.101 | 24 passed, 82 Windows-only skips, 0 failed | 3 passed, 0 skipped/failed | `archive-origin-platform-ubuntu-final2.trx`, `archive-origin-core-ubuntu-final2.trx` |

All eleven new Windows cases execute in the VM. Windows uses host-built binaries, not a guest source build; launcher
facts: parent PID 624, child 4908, runtime 10.0.14, integrity 12288 before / 8192 after, exit 0. This medium token retains
the administrative account's groups/privilege properties. It is not an ordinary filtered-user/installed-package lane.
The two Windows skips are the separately qualified elevated refusal and an existing physical FAT case. Builds finish
with zero warnings/errors. No host tests were started while another tester was active.

Final six-file source delta applied over the storage-updated owned Unix trees:
`F13C0553B9E20EAE8E393341442A6481B697E410CC0571ADA4AF56A31F747B81`.
Final Windows binary/symbol transfer archive:
`5A551265263D9F9951224E483B809D8349BAEBAD70149F343E1D2E5A7AA66DE8`.

| Artifact | SHA-256 |
|---|---|
| FileCat.Platform.Windows.Tests.dll | `81A6168CAB85732E61DFA24D7345BFA6AF98A91CAB7463B4ECD44F1388CF3835` |
| FileCat.Platform.Windows.dll | `D52ECCB386E2019855B1544A71123244C92892306A053E13EA86CAF0739227F8` |
| FileCat.Core.dll | `91AA08696C6E31182A2053A02D2D84F29D16E5FBA71D1427CF1512F5192782D2` |
| FileCat.Archives.dll | `17881F78FB0299CB33AB865CF90F69A6B06FAFBAA6C425815BEF296B486327C0` |
| FileCat.Core.Tests.dll | `4D0802B3BE9FC831FCEA673FF0C2C3AA13479B7B41C2BCE8804FD3F59004877A` |

Raw TRX/facts are retained under ignored `.tmp/test-results/archive-writing`. All guest FileCat/test processes exited;
Mac keep-awake assertions remain active. Shared `E:\FileCat` remains tracked-clean on `main`. No dependency/third-party
source was added. Full UI/package/crash qualification, independent final review and the conditional RAR analysis remain
pending after the rest of the implementation goal.
