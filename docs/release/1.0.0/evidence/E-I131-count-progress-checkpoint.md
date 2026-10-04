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
bytes are not assumed identical. Clean committed revalidation is recorded below.

I130 clean 3a408eb passes all four required jobs in [CI 37228520630](https://github.com/benny-cz/FileCat/actions/runs/37228520630).
Three artifact server digests and six complete TRX inventories verify, independent proof SHA-256
`775b802f3b567da512bdc3f9f09aa7cdbde9a7a63375e52a72613ab15a3e9a5f`. Windows has 72 affected passes; each Unix lane has 56 passes and
sixteen declared Windows factory/ACL/drive-letter/Shell skips. All sixteen original I129 and twelve I130
cases pass per platform. That CI does not erase either failed guest run. Native input/AT/hardware and
final-candidate evidence remain required. Both VMs remain running, G: is untouched, no stable publication
or human GO. Overall **NO-GO**.


## Clean committed combined revalidation

Source **`fdb17b453d515df437bce34eaaaf8f3626f7cb09`** changes only the I131 test fixture over 3a408eb;
I129 and I130 production corrections are unchanged. All four required jobs pass in
[CI 37231232236](https://github.com/benny-cz/FileCat/actions/runs/37231232236); three package jobs are
declared skipped. Three downloaded artifact ZIPs match their server SHA-256 digests; six complete TRX
inventories and full case IDs/multiplicity independently verify in `ci-37231232236/independent-ci.json`,
SHA-256 `9492ed6584767b068a812381c22bc7d11c195481b5c46c10a50bde242e3167d9`.

| Clean CI inventory | Pass | Declared skips |
|---|---:|---:|
| Windows Core | 753 | 47 |
| Windows App | 326 | 15 |
| Windows Platform | 166 | 33 |
| Windows Remote | 88 | 28 |
| Ubuntu App | 298 | 43 |
| macOS App | 298 | 43 |
| Affected Windows App | 74 | 0 |
| Affected App on each Unix lane | 58 | 16 |

All eighteen retired-count cases (sixteen original plus two forced-refresh controls), all twelve
caption cases and all seven I127 lower-bound cases pass on their supported lanes. The sixteen affected
Unix skips retain actual Windows factory/ACL/drive-letter/Shell prerequisites and are not native passes.

The pinned SDK-free Windows guest run ends **20:20:11 UTC with 74 passes, zero failures and zero skips**.
UUID `9D224D56-1161-A849-ABA7-2581A980895C`, Insider build 26300, controller 5940/worker 4492. All
381 payloads/382 ZIP members/twenty-seven canonical source/build files verify against the exact clean
producer and guest input manifest SHA-256
`9d4e1891d8f2f13908efe6791c01981f4642f1e7cccca824a7f4a341ea39dfc3`.
ZIP SHA-256 `38da7e48fb6ab97521c5f5d6dfbacbbfc3c39270fedc60597e0d3805c7a4f650`.
Guest XML SHA-256 `556b4d8b750d3e2eeeb3111c337dd5feccdd6cca345a57a0785b65ed1008ac67`; complete case inventory matches the affected host inventory.
Controller, worker, owned executable children and temp/listing files are absent at **20:22:08 UTC**;
cleanup SHA-256 `ce0d218e841881b1a78b1b3d4792fa9f99f107878847d1c1e6087e4958c5c931`. Independent native proof SHA-256 `2911e42af096558da0b5b72780fab68534aa825c317da789dd8f8af4bf94ce30`
is retained in `count-progress-checkpoint-20261004/clean-fdb17b4/independent-native.json`.

This completes preliminary I129-I131 revalidation. The original 41/43 and 70/72 guest runs remain failed
and retained; the controlled reproductions do not establish their exact historical event triggers.
Native desktop/Esc/frame/AT, slow/cloud/hung devices, wider filesystem data and final-candidate
qualification remain. Both VMs stay running. G: is untouched; no native desktop input, source-device
access or stable publication. No candidate or human GO; overall **NO-GO**.
