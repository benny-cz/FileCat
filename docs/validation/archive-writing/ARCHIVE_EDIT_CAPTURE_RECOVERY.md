# Native edit capture cleanup and restart review

This component remains behind an explicit native-store test factory. Application composition still
uses the legacy store. This checkpoint does not qualify an archive write profile or complete the
native edit workflow.

## Implementation

Windows normal disposal now removes the retained payload, both exact metadata-record objects and the
empty capture directories. Deletion rights belong to the directory handle granted at exclusive
creation; cleanup never reopens a pathname to obtain them. Child owners, unexpected directory entries,
changed records, changed payloads/origins and unknown data streams cause retention. Repeated disposal
does not remove an unrelated later occupant of the old pathname. Tests assert actual payload and
capture-folder absence on successful cleanup.

The original download mark is copied through the retained parent-directory authority. Its opaque
contents, including source URLs, are preserved. Cleanup inventories exactly the expected data and
origin streams; the separate archive-creation aggregation policy remains in force for new archives.

`ReviewRetainedCaptures` reopens native session/capture namespaces after restart and reports complete,
interrupted or review-needed copies. It checks the current session state, fixed versioned record shapes,
duplicate/unknown fields, directory identities, payload identity, complete SHA-256, metadata and origin.
Unknown names, unexpected children, missing/malformed/oversized records, changed payloads and stale
session states remain available for review. The review does not write, publish or delete files. Matching
a saved receipt is an observation, not proof of deletion ownership.

Discovery admits at most 1,024 captures per review. Each capture record is at most 16 KiB with bounded
JSON depth; payload verification has a 2 GiB aggregate read budget, accounting for both complete scans.
An individual payload remains limited to 1 GiB. Record hashing uses the record-specific bound, and
allocation uses the captured length instead of reading a mutable length again after validation.
Cancellation is checked during discovery, record-history reads and payload scans.

Queued native captures independently reserve at most 16 active copies and 2 GiB of active payloads.
Reservation ownership follows the actual copy until disposal and is released even when cleanup retains
files. This does not limit all durable retained disk usage or complete the disk-space/resource gates.

## Live evidence

Candidate `b99299878e09436c8de4f54655d77e1930c94d15`, cohort `awe-storage-v14`, restores the full-inventory
fixtures for the rerun. The preceding `f62d81f9` cohort passed on all three platforms: 27 fresh recovery
scenarios, 35 Windows / 34 Unix snapshot scenarios, 20 directory scenarios, the native storage,
record/parser, commit-copy and connected edit/job/UI selections, plus creation-origin policy checks.
Windows also passed 18 native origin regressions. The [progress checkpoint](edit-capture-recovery-progress.json)
retains completed failed/successful cohorts with exact compiler sources and DLL/PDB identities. Its
`current` rows are the last completed three-platform focused cohort, not the pending full-inventory candidate.

The earlier Windows failures are retained: `awe-storage-v8` could not reopen a directory for deletion;
`v9` could not repin the removable parent for origin propagation; `v10` captured that origin correctly
but cleanup incorrectly applied new-archive mark aggregation restrictions to the exact edit copy.
These failures motivated the retained creation handle, held-parent origin route and separate stream
inventory policy respectively. `v12` passes the new byte-boundary case on Mac/Ubuntu; Windows correctly
denied its fixture's extra pathname reopen while the creator retained deletion rights. `08097526` fixes
the readback to use the held stream. Reservation-sensitive classes share an isolated xUnit collection
when included in the complete inventory.

`v13` passes the corrected focused gates. Its complete Mac Core inventory reports 8,447 passes, 207
platform skips and four case-distinct move-fixture failures on the case-insensitive volume. Ubuntu
reports 8,472 passes, 185 skips and a desktop-keyring collection failure. `v14` restores the earlier
case-sensitive Mac image and isolated Linux bus/keyring setup; the test assertions are unchanged.
Windows `v13` completed Core with 8,087 passes and 576 skips and is still running the application inventory.
The complete Core/application qualification remains open until all required results are preserved and checked.

```powershell
python -B -X utf8 eng/validation/archive-writing/verify_edit_storage_checkpoint.py --checkpoint docs/validation/archive-writing/edit-capture-recovery-progress.json
```

## Remaining gates

Unix exact-object payload/directory removal is still unavailable and retains the copy. Fresh restart
inspection does not authorize destructive cleanup on any platform. Complete the native cleanup/recovery
action, native Save copy destination publication, ZIP/SFTP retained-input integration, safe discard,
legacy adoption and application composition. The new inspection also needs its connected startup UI.

Complete durable-retention disk admission, full scale/memory/CPU/IO/handle measurements, complete Core
and native worker/editor/F4/Commit/rebase/uncertainty workflows, desktop/accessibility acceptance, Unix
mutation/confinement, fuzz campaigns, RID/package/CI evidence and the final independent review. No
unqualified factory or write profile is enabled by this work.
