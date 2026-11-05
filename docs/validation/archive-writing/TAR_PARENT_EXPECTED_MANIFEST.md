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

The 64 GiB logical input ceiling includes all source payloads, including deleted/replaced members, plus every new
payload reference. Reusing one captured object for several additions/replacements charges its bytes each time.
Output totals are independently bounded. Counts, retained names, encoded metadata and retained heap are
reserved before retaining additional representations. Accounting conservatively includes source, request maps/arrays
and the derived output together, even when immutable objects share strings. This may refuse a large job earlier than
an individual manifest limit; it does not replace the still-required global operation/lease budget integration.

## Validation

| Native test environment | Final affected regression |
|---|---|
| Windows 11 x64 VM / NTFS, actual test parent at medium integrity | 66 passed, 0 failed, 0 skipped |
| macOS ARM64 / APFS | 66 passed, 0 failed, 0 skipped |
| Ubuntu x64 VM / ext4 | 66 passed, 0 failed, 0 skipped |

The 27 planner cases cover creation, empty output, exact duplicate edits/reindexing, PAX/USTAR preservation,
optional atime/ctime and pre-epoch fractional mtime, source mode preservation, large IDs, Unicode owner/group names,
distinct case/Unicode member names, stale/repeated keys, malformed metadata including deleted rows, source and output
namespace ambiguity, frozen collections, reserved/unreferenced IDs, pre-enumeration count bounds, cancellation and the
combined logical input budget, including repeated references and the exact accepted boundary. The boundary budget
fixture uses synthetic bounded facts, not a claim of a 64 GiB scale
run. The other 39 cases cover existing strict TAR, rewrite, fresh-verifier and manifest-wire behavior.

For successful roundtrips the tests construct the parent's expectation first, encode owned synthetic fixtures with
the existing engine while discarding its returned manifest, then verify the physical output in a fresh child against
the parent's expectation. This exercises independent expectation construction; it is not an independent codec
implementation oracle, a production in-process fallback or additional native confinement qualification. The earlier
native Windows/Linux confinement evidence remains scoped to its recorded artifacts.

Filter: `FullyQualifiedName~ArchiveTarTransformPlan|FullyQualifiedName~ArchiveTarRewrite|FullyQualifiedName~ArchiveStrictTar|FullyQualifiedName~ArchiveVerification|FullyQualifiedName~ArchiveManifestWire`.
Builds used SDK 10.0.101, zero warnings/errors, and tests used runtime 10.0.14. Windows launch facts record before 12288,
after 8192 and exit 0; administrator membership/privileges remain, so ordinary filtered-user/package qualification is
still outstanding. Final portable runs use `tar-parent-plan-v4.trx`. Earlier v1/v2/v3 results are superseded by this final
source and are not added to its count. Post-commit review of `911c3c5b` found that charging each unique captured object
once could undercount repeated uses alongside deleted source content. The added regression accepts exactly 64 GiB
of source plus referenced input facts and refuses the next reference; all three final native-OS runs include the fix.

Three-file source overlay SHA-256: `6917637B74EE7913E345F483EE473FEBA0D8C3FA187A55F43BEA40FAC88A5140`.
Windows 28-file managed overlay SHA-256: `25C4F2DEBF2F25AF8BE513A0B32135BA8E406900CB83B547B9DE96C8D7275376`.

| Tested artifact | SHA-256 |
|---|---|
| Windows Core.dll | `F5199D98291D3E1125F4BD47D0EA16AAF72CA3E082367FBCACEBB1D431D3F110` |
| Windows Core.Tests.dll | `B748325558D539EAD8583D9FEFC3274913B9C5F0A47E1A916AE4E68E0245D525` |
| Mac Core.dll | `3FC50812EACD1EAEB10EEFECD4889FDC4DF6BB7A6B58406C5EF555FE3CD9B68C` |
| Mac Core.Tests.dll | `0CACEDF54653E5B293F3B61883C9C0BB26843793E9BF236A747E882B32FC0D4E` |
| Ubuntu Core.dll | `BCFD1D4316EAE2FA371D18E01B3EEE07F20731262B668C75C769E693CBB0DA3C` |
| Ubuntu Core.Tests.dll | `F59FD4D4767A98BA63FB4B926BEEEDC83A502B236A4B9A8E88D66D056CE6349F` |

Raw TRX, stdout/stderr, Windows launch facts and build logs remain in ignored
`.tmp/test-results/archive-writing/tar-parent-plan-v4` and owned guest directories. Each run checked for other testers
before testing and for remaining test processes afterward. No FileCat/test instance was left by this task, and the
Mac's temporary until-reboot awake assertions remain active. Initial post-build censuses found transient dotnet
processes and postponed testing until idle. After successful tests and cleanup, a later Ubuntu census found another
FileCat instance; it was left alone and had exited before subsequent inspection. The shared checkout remains on `main`.

Still required: reviewed source/folder capture and native lease binding, request wire/transform worker, output bounds,
receipt/parent rereview and journaled publication/recovery, cooperative cancellation, other formats, production/UI
dispatch and complete package/RID/fuzz/scale/native GUI qualification. The separate whole-goal independent review is
pending after the implementation loop, before the deferred RAR licensing analysis. This checkpoint satisfies neither
that review nor the complete goal.
