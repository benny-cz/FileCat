# Private unmarked new-file publication, 5 November 2026

Partial development checkpoint over `c957f273`; no new application writer is enabled. The controlling
[archive-writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) remains incomplete.

## Native publication and durable evidence

`ArchiveNewFilePublication` continues an actual journaled, freshly verified TAR creation using the same retained
native stage and captured input owners. It accepts only a new destination, current schema facts and a stage-issued
creation review. A stale or deserialized snapshot cannot authorize another attempt. Updates, marked inputs, arbitrary
metadata copying and automatic cleanup are outside this checkpoint.

The stage must remain the exact verified object and bytes, sealed, creator-private and unmarked. Unix additionally
requires a creator-owned destination with no group/other namespace write permission, and no extended Mac directory ACL
entries. This restricts the non-conditional POSIX source-name operation to that namespace policy; it is not a new atomic
compare-and-swap primitive or protection from an equivalent-identity actor. Windows uses its existing held-object,
exclusive rename and now queries `FileStreamInformation` before review, immediately before claim and after publication.
Only an empty/`::$DATA` unnamed stream with the expected size is admitted. Unknown/additional streams refuse.
The bounded query follows Microsoft's [FILE_STREAM_INFORMATION contract](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_stream_information).

The live native review is issued by the fresh stage owner and binds its parent, source and destination leaf, verified
version, metadata digest, backend and bounded native witness. The Unix witness includes exact stat/flag/ACL/origin and
visible-attribute facts. The Windows witness includes queried owner/group/DACL/label security information, times,
attributes, links, absent origin and the unnamed-stream check. These describe the same newly created private object;
they do not assert that unqueried Windows SACL/CAP or hidden Unix policy state is absent or preserved by copying.

Journal schema 5 adds a `CreationMetadata` sidecar reservation at initial preparation. Its 512 KiB maximum is charged to
both evidence and disk admission before any intent or file creation. It remains uncreated until fresh verification
has completed. The parent serializes a bounded, scoped `FCCMETA1` record with exact native witness, records the native
identity before writing, seals/syncs it, reads it back completely including EOF, and durably records its file version.
`ReadyToPublish` binds that file's SHA-256 as the security snapshot. Schemas 1–4 remain readable and cannot authorize
this native operation; schema 4 rejects the new role.

After rereviewing every captured input and sealed evidence object, the parent durably records `Publishing` before
claiming the previously absent name. It then observes the actual published object, records `Reconciling`, rereviews
inputs/evidence and promised metadata, and records `Cleaning` then `CommittedWithRetainedArtifacts`. All evidence
files remain; no deletion is attempted and an ordinary end marker remains refused. Unix rename may change ctime;
the remaining promised facts and exact stage identity/length/hash must agree. Necessary observations after a possible
namespace effect ignore cancellation, so cancellation cannot invent a safe rollback or a NotCommitted outcome.

Any failed/uncertain operation retains the stage/evidence and records `NeedsReview` where the journal remains writable.
The first native replay exposed that an IO recovery hold also prevented this conservative append. The fix permits
only a retention record derived from the already accepted facts while retaining the hold; it never restores current
snapshot authority or grants a filesystem action. Failure to append still preserves the original error and existing
durable evidence. Generic recovery closure continues to refuse these journals.

## Validation

| Environment | Affected Core replay | Additional replay |
|---|---|---|
| Windows 11 x64 VM / NTFS | 1,158 passed, 158 skipped; 1,316 total | UI: 219 passed. Storage: 93 passed, 26 skipped. Real LPAC: 38 passed |
| macOS ARM64 / APFS | 1,264 passed, 52 skipped; 1,316 total | UI: 219 passed. Unix fixture follow-up: eight passed |
| Ubuntu x64 VM / ext4 | 1,277 passed, 39 skipped; 1,316 total | UI: 219 passed. Confined Linux: 49 passed, one skipped |

No final failures. SDK 10.0.101; actual test-host runtimes remain Mac 10.0.12 and Windows/Ubuntu 10.0.14. The Windows
owned launcher records integrity 12288 before / 8192 after, including the native lane; admin membership remains, so
this is not filtered-user qualification. The broad Core filter includes Archive, EditSession, BackingArchiveTarget
and PaxTimestamp. The affected UI filter includes the first three terms. Native lanes use the tracked containment
runners. The unchanged production source passed the complete platform replays; later edits only corrected test
fixture setup, followed by the affected replays described below.

The first Mac/Ubuntu failure replay found and fixed the recovery-hold append issue above. The later Unix namespace
gate correctly refused Ubuntu's default group-writable temporary directories. The publication fixtures now explicitly
create mode-0700 directories; the unsafe-directory case deliberately changes its fixture to 0777 and still refuses.
Ubuntu then passed the full Core/UI/native replay; Mac passed all eight affected fixture cases after its already
passing broad replay. The Windows ADS adversary initially failed at injection because File.WriteAllBytes did not share
the stage's existing access. An explicit FileStream with read/write/delete sharing reaches the actual gate and passes;
the complete affected storage and actual LPAC lanes then pass. No production guard was relaxed for these fixtures.

Eight independent Python exact-time fixtures pass per platform; native Unix C metadata/xattr/capacity observers remain
enabled. The final runner checks found no FileCat/test process left by this task. Mac caffeinate PIDs 1339/6428 remain
until reboot. Shared `E:\FileCat` is tracked-clean on `main`.

Tests added here cover scoped metadata framing/checksums/EOF/truncations, issuer/nonce/target binding, pre-growth witness
bounds, schema compatibility, recovery-hold recording, actual native create publication and failure cases for an
existing occupant, changed input, changed metadata evidence, cancellation and an exception immediately after the
native claim. Unix also exercises a writable-by-other-users destination refusal. Windows separately injects an ADS
between review and claim. Failure checks retain exact evidence and the original occupant, refuse generic closure and
do not erase a possibly published archive. Existing native create/update preparation, journal crash tests, independent
metadata/xattr/capacity observers and eight Python exact-time readers remain in the affected replay.

This is not the complete publication process-crash/power-loss matrix, a GUI acceptance result, installed-package/RID
qualification, full metadata/security admission, marked-input publication, replacement/rollback, cleanup or recovery
resolution. Scalable capture, remaining formats, platform worker gates, production integration, fuzz/scale gates and
release qualification remain open. The final whole-goal independent UX/UI/functional/security review and subsequent
RAR licensing analysis have not started.

## Evidence

Raw passing and earlier failure TRX files, launch facts, observers, logs and oracle fixtures are retained under ignored
`.tmp/test-results/archive-writing/creation-v3` and the task-owned guest roots. No third-party code/package was added
or upgraded. The Windows final follow-up contains only the rebuilt test assembly/PDB; production assets are unchanged.

| Transfer/result archive | SHA-256 |
|---|---|
| 23-file production/test source v3 | `70619301E16C6EEB664468006F068C0D8D1D932EA5F69E61C03CC6F13AF52D1B` |
| Source v4 with explicit Unix private fixtures | `57D6F54E2F5A50DE2207E3EE13B99D4D07D264A02C58395AFD22E01129909DA4` |
| Windows 84-file managed overlay v3 | `38DFA609BF9182B35925192F854757DCC4D154957D4FF1437978C3C594C515A7` |
| Windows ADS test follow-up v4 | `B38F6869D41CBCCA22A62CF9354FC584EF5E16307813169F721DCB72323624AC` |
| Mac broad results/observers/fixtures | `77602199EA3471D2A90A42AF702E363CE26686E196053481829333F1A017ED5B` |
| Mac explicit-private-fixture follow-up | `FD75DD86CAA788464E350F90C4B13B6EE1FEA477EC9C25248CDA30FD2E0F91DD` |
| Ubuntu results/observers/fixtures | `A90D5948F734DAC7B71F46EFCF8E2A29DEFC2D6881A7F2D80E73A467BEB222E7` |
| Windows results/launch facts/fixtures | `BD7AF9F4D4002CA58E110D63C350B22FC0A3150DC4D43531804A6486D12ED0B5` |
