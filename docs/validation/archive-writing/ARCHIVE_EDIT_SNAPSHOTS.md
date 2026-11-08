# Retained native edit snapshots

This is an in-progress, unregistered component. Application composition still uses the legacy edit store.
The snapshot work does not qualify complete cleanup, native Save copy, native ZIP/SFTP integration,
archive publication, or a release profile.

The later [cleanup and recovery follow-up](ARCHIVE_EDIT_CAPTURE_RECOVERY.md) adds Windows live capture-tree
removal, bounded read-only restart inspection and active-copy reservations. The evidence below remains the
original capture/view checkpoint; the follow-up records its own exact candidates, failures and limitations.

## Implementation

`7cf19350` captures a native-store working file through its retained root/session/working-directory
authority. It creates a private session-owned capture directory, publishes a bounded ownership marker
before copying, and records the completed identity, length, SHA-256, metadata and origin digests.
Capture checks complete bytes, source revisions and exact origin evidence, carries the modification
time and origin mark to the new object, and retains the actual open file through job reconciliation.
Interrupted or uncertain captures remain under the recorded session namespace.

The parent-only `IArchiveOwnedEditInput` capability authorizes one exact native member replacement.
`ArchiveJobPlanning.CaptureInputs` supplies bounded readonly views over the retained object instead of
reopening its pathname. Source path strings remain planning labels; they grant no input ownership.
The capability is not serialized. Changed/stale records, a mismatched session, extra changes, source
redirection, move requests, changed bytes/marks and disposed views are refused before a rebuild can
use an unrelated replacement. A later editor save leaves the captured bytes intact.

Normal path-based input capture remains the route for ordinary archive additions. Its complete
regression/native acceptance still belongs to the remaining combined qualification packet.

The Windows cleanup implementation invokes the strict held-handle payload recipe, with exact version,
origin and stream-inventory review. Capture records/directories remain for fresh cleanup review.
Unix payload removal remains explicitly unavailable; the held file and its recorded directory are
retained. These outcomes are temporary implementation limits, not the intended final cleanup UX.

## Live evidence

The [checkpoint](edit-snapshots-checkpoint.json) preserves six exact bundles, 101,859,857 bytes, alongside
the inherited thirteen storage bundles. All nineteen bundles total 321,384,117 bytes. Current candidate
`fec4d19d592377c1d7a792b63df7a888e3df4341`, cohort `awe-storage-v7`, passes:

| Focused gate | Windows | Mac | Ubuntu |
|---|---:|---:|---:|
| Recovery parser | 19 | 19 | 19 |
| Native directory primitives | 16 | 16 | 16 |
| Native session storage | 14 | 19 | 19 |
| Native snapshot scenarios | 26 | 26 | 26 |
| Commit-copy regressions | 10 | 10 | 10 |
| Connected edit/password Core | 100 passed / 10 Unix-only skipped | 110 | 110 |
| Connected application edit selection | 190 | 190 | 190 |

The snapshot scenarios include empty and multi-buffer captures, exact origin/time/hash/CRC, late atomic
editor saves, cancellation, stale/replaced working namespaces, snapshot substitutions, changed data/marks,
view lifetime, session/request binding and all ten format/password planning combinations. The planning
tests deliberately fail if the ordinary pathname-open callback is invoked for a retained edit.

Windows binds 32 changed C# inputs and 15 DLL/PDB pairs; Mac/Ubuntu each bind 29 inputs and 12 pairs.
These are focused native store/planning and connected edit tests, not complete Core, actual F4/native-worker
publication, desktop/accessibility or fuzz/resource acceptance. Every final census has zero owned
FileCat/test processes and the Mac preserves wake helper 36765.

The original `awe-storage-v6` run is retained: sixteen workflow scenarios used a fixture whose catalog
revision differed from its declared source SHA-256, so the strict assessment check rejected them.
Windows also reported the correct native access-denial exception for a replaced working directory,
while the assertion expected only IOException. Candidates `753a94d3` and `fec4d19d` correct those
fixtures. Verification proves that captured product binaries are identical across the failed and
successful cohorts; the test-assembly changes are separate.

The ten commit-copy regressions include `cc5ea3a9`'s repeated-disposal fix: a second Dispose must keep
an unrelated later file at the old snapshot pathname. This follow-up now has live evidence on all
three platforms; the earlier storage checkpoint correctly recorded it as unvalidated at that time.

```powershell
python -B -X utf8 eng/validation/archive-writing/verify_edit_snapshot_checkpoint.py
python -B -X utf8 eng/validation/archive-writing/verify_edit_storage_checkpoint.py
```

## Remaining gates

At this frozen checkpoint, fresh capture-record cleanup, exact metadata-file/directory ownership,
newly appearing children and explicit payload-cleanup filesystem postconditions remained open. See the
follow-up for subsequent Windows runtime cleanup and fresh read-only recovery inspection. Resolve the
remaining Unix cleanup recipe without a conditional-check-then-path-unlink fallback.

Implement native Save copy destination publication, retained-input integration for ZIP and remote commits,
safe discard, legacy record adoption, bounded queued-snapshot resource ownership and application composition.
The present native Save copy/legacy ZIP guards intentionally keep those routes unavailable in the explicit
test factory; they must be completed before enabling it. Preserve existing legacy ZIP/SFTP behavior.

Then run complete Core and ordinary typed-job/input regressions, actual native F4/Commit/rebase/uncertainty
journeys, native desktop/editor/accessibility acceptance and all inherited platform, fuzz, resource, RID,
package and final independent-review gates. No default writer or native edit-store factory is enabled here.
