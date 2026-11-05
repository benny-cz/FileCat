# Parent TAR expected manifest checkpoint, 5 November 2026

Status: partial implementation; no new writer is registered. This follows the
[fresh-verifier checkpoint](FRESH_VERIFICATION_CHECKPOINT.md), commit `43f6d749`, and remains subject to the complete
[archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

`ArchiveTarTransformPlan.Create` freezes reviewed input facts, exact occurrence edits, flattened additions and a full
expected output manifest before a writer runs. Input IDs are positive and distinct, with zero reserved for the source
archive. Only referenced captured inputs are retained. The DTOs contain no filesystem paths, recursive operations or
publication authority. Caller lists can change after construction without changing the plan.

The parent derives stable physical order, renumbered duplicate occurrences, exact raw names, lengths, payload hashes,
canonical semantic metadata and TAR features from the complete assessed catalog and captured regular-file facts.
Replacement preserves the old member metadata; additions use explicit captured mode/mtime with fixed owner/group
defaults. New directories use the documented engine defaults. USTAR additions use the engine's whole-second timestamp
rule and must fit its name/numeric fields. PAX times retain their exact admitted tick precision. An empty output has
no PAX extension and therefore uses the strict observer's USTAR feature inventory.

The bounded metadata reader requires schema 1, strict UTF-8 text without embedded NUL, nonnegative owner/group IDs,
the admitted mode bits, exact timestamp ranges/presence bytes and complete EOF. All source metadata and the complete
source namespace are checked, including rows being deleted. Unknown features, unsupported profiles, stale/unknown/
repeated keys, file/folder ambiguity, changed-name collisions, unrepresentable USTAR changes and unrelated input grants
refuse before encoding. Existing untouched duplicate records remain distinct; equality is ordinal without Unicode
normalization or case folding. Namespace ancestor checks use sorted ordinal names rather than allocating and hashing
every prefix of every deep path.

The 64 GiB logical input ceiling includes all source payloads, including deleted/replaced members, plus captured new
inputs. Output totals are independently bounded. Counts, retained names, encoded metadata and retained heap are
reserved before retaining additional representations. Accounting conservatively includes source, request maps/arrays
and the derived output together, even when immutable objects share strings. This may refuse a large job earlier than
an individual manifest limit; it does not replace the still-required global operation/lease budget integration.

## Validation

| Native test environment | Final affected regression |
|---|---|
| Windows 11 x64 VM / NTFS, actual test parent at medium integrity | 65 passed, 0 failed, 0 skipped |
| macOS ARM64 / APFS | 65 passed, 0 failed, 0 skipped |
| Ubuntu x64 VM / ext4 | 65 passed, 0 failed, 0 skipped |

The 26 new planner cases cover creation, empty output, exact duplicate edits/reindexing, PAX/USTAR preservation,
optional atime/ctime and pre-epoch fractional mtime, source mode preservation, large IDs, Unicode owner/group names,
distinct case/Unicode member names, stale/repeated keys, malformed metadata including deleted rows, source and output
namespace ambiguity, frozen collections, reserved/unreferenced IDs, pre-enumeration count bounds, cancellation and the
combined logical input budget. The boundary budget fixture uses synthetic bounded facts, not a claim of a 64 GiB scale
run. The other 39 cases cover existing strict TAR, rewrite, fresh-verifier and manifest-wire behavior.

For successful roundtrips the tests construct the parent's expectation first, encode owned synthetic fixtures with
the existing engine while discarding its returned manifest, then verify the physical output in a fresh child against
the parent's expectation. This exercises independent expectation construction; it is not an independent codec
implementation oracle, a production in-process fallback or additional native confinement qualification. The earlier
native Windows/Linux confinement evidence remains scoped to its recorded artifacts.

Filter: `FullyQualifiedName~ArchiveTarTransformPlan|FullyQualifiedName~ArchiveTarRewrite|FullyQualifiedName~ArchiveStrictTar|FullyQualifiedName~ArchiveVerification|FullyQualifiedName~ArchiveManifestWire`.
Builds used SDK 10.0.101, zero warnings/errors, and tests used runtime 10.0.14. Windows launch facts record before 12288,
after 8192 and exit 0; administrator membership/privileges remain, so ordinary filtered-user/package qualification is
still outstanding. Final portable runs use `tar-parent-plan-v3.trx`. Earlier v1/v2 results are superseded by this final
source and are not added to its count.

Three-file source overlay SHA-256: `49417EDC2E3511794499DB201CD5DCA4F7440548A48963FBCD3F15B702755C76`.
Windows 28-file managed overlay SHA-256: `B7C918AF86C928FCF75F56F3D896F43AC52D141B32297D38950C7BBC95359BC6`.

| Tested artifact | SHA-256 |
|---|---|
| Windows Core.dll | `5A4AFC09912032B8A8E4B88CB986C447E8D8C251ED809E9144028AABB0CC748F` |
| Windows Core.Tests.dll | `9B70BA73AEC0FC21894F71A82861AE7950B2CD7DB6732331F22F5E6BE639514E` |
| Mac Core.dll | `A95C30816B3391B2CF01A139F808D368882C561F9C3D0BF57320116D6776F54F` |
| Mac Core.Tests.dll | `AD221254BC0B85EE3B4D0DBB05F7F976C9EA9E2FC4E7263EBEB25664F11C14F9` |
| Ubuntu Core.dll | `75E4976A1C413416FD10153247F75B8ECB894BBF0E1D3065B3F5180D0C1C7E1F` |
| Ubuntu Core.Tests.dll | `D3B1E717B42F7CAE0C1D5F27C946687D7B40193CBB7DA360F973533B7725589A` |

Raw TRX, stdout/stderr, Windows launch facts and build logs remain in ignored
`.tmp/test-results/archive-writing/tar-parent-plan-v3` and owned guest directories. Each run checked for other testers
before testing and for remaining test processes afterward. No FileCat/test instance was left by this task, and the
Mac's temporary until-reboot awake assertions remain active. The shared checkout remains on `main`.

Still required: reviewed source/folder capture and native lease binding, request wire/transform worker, output bounds,
receipt/parent rereview and journaled publication/recovery, cooperative cancellation, other formats, production/UI
dispatch and complete package/RID/fuzz/scale/native GUI qualification. The separate whole-goal independent review is
pending after the implementation loop, before the deferred RAR licensing analysis. This checkpoint satisfies neither
that review nor the complete goal.
