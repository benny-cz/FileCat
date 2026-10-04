# E-I131 — count fixture observes the progress callback before refresh

Preliminary validation reliability repair for V12 / AI-03. Production source remains clean
`3a408eb0f87615b83cfb707ab04c64c51c94207b`; the working overlay changes only
FolderCountRetiredResultTests. Private evidence is `FileCatReleaseEvidence/count-progress-checkpoint-20261004`
in the authorized C-drive evidence root. Tests own their real-file roots and controlled identity adapter.
No native desktop input or physical-drive access occurs.

## Failed guest run and controlled reproduction

The exact clean I130 SDK-free guest run ends at 19:36:55 UTC with **70 passes, two failures, zero skips**.
All twelve I130 caption cases and all seven I127 lower-bound cases pass. Two I129 held-worker cases
(cancel-retry/replace-demand, replaced true, 32 folders, queuedResult false) fail at line 124 before
cancellation/retry, waiting for the original partial row still to contain 1,000 bytes. All raw files and
381 payload pins/twenty-seven source files remain retained; XML SHA-256
`bd932e548950770020a6ffc8b550f1891d776715091d984cf60d21d8a46a3993`. Controller 10956/worker 10420 and owned temp files
are absent at 19:39:57 UTC; cleanup SHA-256 `bb362a101f6d31bf02d5ba3604b64a8c3ba1261e675a8590ca87ec03d7a3dcad`.
The original f341dfd 41/43 guest failure remains a distinct failed run.

An independent probe copies the original fixture, references the actual clean production DLLs, records
real UI callback application and listing events, and explicitly refreshes while the identity call is held.
The original live-row checkpoint fails twice; two no-refresh controls pass. Before refresh, the actual
callback applied 1,000 partial bytes and one folder remains active with two identity asks. After refresh,
the partial row is unknown (-1, no SizeComputed flag), with that worker still held. A later live-row wait
cannot establish whether the earlier callback occurred. Baseline XML SHA-256 `94b7234b4f9d490d56fb104473178bb75184a85e2a2b3880b92122d7d2d55db3`.

This controlled trace proves that the fixture's checkpoint is invalid after refresh. The failed guest
did not retain its listing event trace, so the exact historical refresh trigger is not asserted.
Unknown partial data during refresh is permitted; this is not evidence that a completed count was lost.

## Correction and working validation

The dispatcher records actual count callback application before the row can be reconciled. The fixture
waits for observed 1,000-byte partial progress while the identity call is held. It still requires all
final/new-count byte totals, completion/lower-bound/retry state, marks, focus, hashes, queued callbacks
and retired-result exclusion. Two new controls force refresh between progress and cancel/replacement;
the original sixteen case names are preserved.

The same four-case probe passes after only its checkpoint changes. **Every production DLL remains
byte-identical**; changed loaded inputs are CountProbe.dll and its PDB. Corrected XML SHA-256
`fa1ce98c8ddce18e49226f8cf4412bc759535ee9ce505fc9edcc2f954bb76fa0`. This is a fixture change, not a production remedy.

| Working host inventory | Pass | Declared skips |
|---|---:|---:|
| Corrected independent checkpoint probe | 4 | 0 |
| Affected App / fourteen classes | 74 | 0 |
| Full App | 320 | 21 |

All 748 captured files, twenty-seven canonical source/build inputs, complete case IDs/multiplicity,
affected/full inventories and selective fixture-only changes verify. Working manifest SHA-256
`56b2e1a75ef2fc4089044fa4e7f0c89c6ea3eabeed1becf6e31ca42808185c1f`; independent proof SHA-256 `85e51a8a0be24ac4b20b5ed2e9c09a07e91439c1f992873959aa37e4e700179a`.
Framework-dependent host build and self-contained guest publish are separately pinned; their build
bytes are not assumed identical. Clean committed CI and SDK-free guest revalidation are next.

I130 clean 3a408eb passes all four required jobs in [CI 37228520630](https://github.com/benny-cz/FileCat/actions/runs/37228520630).
Three artifact server digests and six complete TRX inventories verify, independent proof SHA-256
`775b802f3b567da512bdc3f9f09aa7cdbde9a7a63375e52a72613ab15a3e9a5f`. Windows has 72 affected passes; each Unix lane has 56 passes and
sixteen declared Windows factory/ACL/drive-letter/Shell skips. All sixteen original I129 and twelve I130
cases pass per platform. That CI does not erase either failed guest run. Native input/AT/hardware and
final-candidate evidence remain required. Both VMs remain running, G: is untouched, no stable publication
or human GO. Overall **NO-GO**.
