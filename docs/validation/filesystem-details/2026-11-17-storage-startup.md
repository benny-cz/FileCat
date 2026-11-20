# Bounded storage startup and retained reviews — 2026-11-17

This continues the [Operations checkpoint](2026-11-17-storage-operations.md), from `main` parent `4364d1b37f2804171e47ed064c9e5337971189c0`. Product transforms remain disabled; this is a recovery/discovery checkpoint, not completion of the two implementation plans. EFS remains required and unfinished.

## Implementation

MainWindow now starts storage history discovery separately from the legacy eager scan. Each returned page owns and closes its cursor before the UI receives it. New header inspection remains bounded to 64 examined entries or 1 MiB, with the existing 256 KiB record limit. More storage history, Refresh storage history and Stop history discovery reuse Operations. An empty journal page can yield a separate artifact-discovery slice so retained reports appear after restart even when their journals have been pruned.

Continuation retains only the native namespace/anchor identity and filename. An explicit More request re-enumerates old names without reopening their headers, cancellably, with a separate one-million-entry replay bound. It then reads the next bounded header page. A replaced namespace/anchor, missing anchor or exhausted replay bound requires Refresh; it does not silently substitute an unrelated continuation. Enumeration is not a frozen or newest-first snapshot. Concurrent insertion/reordering may require a fresh scan. This re-enumeration cost remains part of the outstanding high-scale performance qualification.

Pending and retained storage entries share one 64-descriptor limit. Eviction chooses the oldest displayed unselected storage row across both collections; the selected pending review remains stable. Older evidence is preserved, More remains available, and the status explains the display limit. Descriptor titles are bounded and manager-scoped filenames replace repeated full private-directory paths. Sources/roots remain in evidence. Retained rows keep no report bodies or full acknowledgment objects: opening one verifies the actual native report/acknowledgment pair and then loads its bounded report. Late legacy startup loads preserve current-session storage rows.

Retained artifact pages inspect names only and explicitly label their rows verification-required. They do not treat a filename as proof of acknowledgment. A selected pair is verified through Core's private evidence backend, including identity/digest binding. The displayed historical acknowledgment preserves uncertainty and distinguishes whether the original journal still exists. Its existence does not establish that a historical acknowledgment covers the journal's current contents. Invalid/missing pairs produce an explicit error and do not dismiss pending review.

The manager supports bounded storage references as recovery row keys. Interactive storage I/O uses the manager's private directory, not those keys as filesystem paths. Generic cleanup and closure remain refused. A fully qualified clean/currently acknowledged journal can remove its pending row only after the actual status read; a newly discovered header alone cannot do so.

Review also found a discrepancy between header discovery and full history interpretation: an unrecognized filename with an otherwise valid ended sequence could appear clean. Both full-history readers and the owned review path now latch that name as damaged. It remains pending until exact explicit acknowledgment; acknowledgment preserves the damaged diagnosis.

## Verification

Builds completed with zero warnings/errors. Suites ran on the disposable Windows 11 x64 VM at medium integrity, Ubuntu VM, and Apple Silicon Mac. No test suite ran on the user's workstation.

| Selection | Windows | Ubuntu | macOS |
|---|---:|---:|---:|
| Final Core storage/scheduler/lifecycle/Undo/archive, 282 discovered | 281 passed / 1 existing fixture skip | 280 passed / 2 existing platform/fixture skips | 280 passed / 2 existing platform/fixture skips |
| Broad App Operations/recovery/Undo, 169 discovered | 168 passed / 1 existing fixture skip | 166 passed / 3 platform/fixture skips | 165 passed / 4 platform/fixture skips |
| Final storage startup/projection + OperationCenter, 27 discovered | 27 passed | 26 passed / 1 Windows-only native fixture skip | 26 passed / 1 Windows-only native fixture skip |
| Final native compression/evidence selection, 37 discovered | 37 passed | Not run | Not run |

All accepted runs had zero failures/errors/not-run tests. The broad App selection passed before the final malformed-name interpretation fix; after that focused Core fix, the complete 282-case Core selection, focused 27-case App selection and 37 native cases ran again. The final source/binary hashes below identify those runs.

New tests cover detached continuation across 130 histories, released cursor admission between pages, replay cancellation/missing anchors, native namespace/anchor replacement, artifact pages that never read report bodies, the shared 64-row limit, preserved selection and compact retained rows. The native cursor fixture resumes through more than 600 directory entries with bounded header pages and permits independent journal review between pages. Native manager tests verify retained-pair selection, bounded references, acknowledgment-aware status and byte-exact export.

The new normal-user Windows App fixture creates only private evidence metadata. It discovers a pending intent at startup, opens the storage report, clicks acknowledgment and its actual confirmation, verifies the durable pair and removes the pending action. It then closes the app, removes its own acknowledged fixture journal to simulate completed retention, restarts and loads/verifies the retained uncertain summary from disk. The fixture does not claim to test Core's deletion-order policy; those tests are separate. Main windows are 640×420 and report actions pass through the actual App recovery path. This is headless UI plus real native evidence, not completion of the outstanding manual/native UX matrix.

The first Windows App trial failed in fixture ACL setup: the default SetSecurityDescriptorSddlForm overload requested audit privileges. The corrected fixture explicitly limits changes to owner/access sections and remains at medium integrity. That initial failed trial is not acceptance evidence. The corrected 27-case run and a subsequent clean 169-case Windows run passed; the final 27-case run also passed after the malformed-name fix. No elevated fallback was added.

Scripts and complete logs are retained under `.tmp/filecat-storage-startup-*`. Each accepted remote run has its explicit finish marker and successful runner exit. The final source archive excludes bin/obj. The final Windows UI/native patches combine their qualified binaries with the latest Core binary; no public signature changed in the final malformed-name fix. Mac/Ubuntu rebuilt from final source.

| Final artifact | SHA-256 |
|---|---|
| filecat-storage-startup-final-source.tar.gz | `26E4443D869A5DFF797137E240230046EC3A8C138DB7C08F47140AE9E3EB047D` |
| filecat-storage-startup-final-core-binaries.tar.gz | `AF8B146950E22849C6EA5BECD0B518E41D19381EFCA3DE09882CD2A4E47E5328` |
| filecat-storage-startup-final-ui-patch.tar.gz | `46F61D64D3837B7F13C32330931750AF6DE2CB4A5A58C8D499DBE7AB965BBC3B` |
| filecat-storage-startup-final-native-patch.tar.gz | `95478FD72620762B0661C12BA15ECBA63C8AAC8B2ED6E58907580914394CF0BF` |

## Remaining gates

The App still needs to run/publish the full streaming accounting census with cancellation and interaction priority; header paging never claims completed accounting. Job admission already performs its own qualified census and remains disabled at the product transform seam. Cross-instance dirty accounting, provider/device/crash/quota pressure and large-history performance need broader qualification. This checkpoint does not close T-LIMIT/T-REC globally.

Live ordinary reports still need paged/streamed exact call details from the last durable prefix; inactive recovery export is available. Fresh current-object observations, the transform command/review dialog, revision-safe preparation, captured-origin/mark-preserving refresh, wider native UX/capability coverage, EFS protocol/key/output implementation, and incomplete companion filesystem collectors remain outstanding. No completion claim for either roadmap follows from these focused passes.
