# Column ownership finite acceptance

This matrix records the O1–O7 feature scope. Existing UX ledger cases remain closed; this feature added no general
accessibility, dialog-layout or focus re-audit. Dated package evidence records failed/initial attempts separately.

**Final outcome: O1–O7 complete.** The [full CI/package workflow](https://github.com/benny-cz/FileCat/actions/runs/43936482729)
passed at dc22579ad923a39db1f881be4dafba93aae1116e, attempt 2. Native qualifications below remain explicit.

| ID | Evidence | Outcome |
|---|---|---|
| OWN-01 | OwnerColumnChooserTests / ColumnProfileTests; Windows Owner available, Unix choices retained | Passed |
| OWN-02 | Profile import/round-trip/schema tests; ColumnHeaderMenu, CustomColumnProfileMenu and HorizontalColumn tests | Passed |
| OWN-03 | Owner header/sort-menu and Analyze chooser/scope tests; shared descriptions | Passed |
| OWN-04 | WindowsOwnershipTests native well-known IDs and fixture-owner name, typed fake domain/type/empty-domain cases | Passed; domain-lab interoperability remains qualified |
| OWN-05 | UnixOwnershipTests mask/zero/symbol/error cases; OwnershipFieldTests raw identities; native Linux missing UID | Passed for enabled routes |
| OWN-06 | Principal ordering/canonical-ID tests; OwnerColumnInspectionTests escaping and full identity | Passed |
| OWN-07 | Typed ownership/service outcome and Windows native-boundary error cases | Passed |
| OWN-08 | MetadataRefreshTests fake clock; outcome cell/inspection and analysis current/LastKnown counts | Passed |
| OWN-09 | Windows x64 ordinary-token file/folder/exact/long/link/broken-link/junction tests; Linux native links; path/provenance fakes | Passed for enabled native routes; UNC/storage diversity qualified |
| OWN-10 | Owner chooser/direct Analyze mixed-result guard, provider-path inspection, Parent/Offline applicability cases | Passed |
| OWN-11 | Listed-directory analysis path counter, including a directory without descendants | Passed |
| OWN-12 | Off-profile scrolling/inspection/accessibility/sorting counters; explicit off-profile analysis | Passed |
| OWN-13 | MetadataDemandTests, focused horizontal/vertical cases, PrincipalNames coalescing | Passed |
| OWN-14 | PrincipalNames blocked-slot/retiring-instance/queue cases; raw-ID UI remains independent | Passed deterministically; domain native delay unmeasured |
| OWN-15 | Activation repaint/visible-demand test; fake-clock refresh; effective publication retirement before reversal | Passed |
| OWN-16 | Pre-enumeration same-location replacement, blocked old native read and final-analysis-read replacement tests | Passed |
| OWN-17 | RunOwned buffer/store lease, analysis navigation/close/replacement/supersession tests; resolver generation/disposal cases | Passed |
| OWN-18 | Canonical numeric and captured-name keys; parent/container partition and unknown-last both directions | Passed |
| OWN-19 | MetadataStreamingOrderTests ordinary and forced external paths; fixed keys through filtering/appends; focus tests | Passed |
| OWN-20 | Current/LastKnown/Absent/Gone/Denied/Offline/Unsupported/Failed counts; mixed name categories; Esc wait; final-read races | Passed |
| OWN-21 | Cache 8 / 120 rows; independent row/character/shared-byte caps; long SIDs leave key space; two tabs and last-worker lease | Passed |
| OWN-22 | OwnerColumnInspectionTests and ClippedCellInspectionTests; sanitized cached details and quiet full/plain values | Passed |
| OWN-23 | FileListAutomationTests ownership/name completion speech-event counters and unchanged verification behavior | Passed |
| OWN-24 | Shared Get/Compute/Peek Offline gates and same-key retained-value override; macOS policy fake paths | Passed for deterministic policy; macOS shipping route disabled |
| OWN-25 | Effective local/slow clock intervals, cache freshness, transient retry and expensive explicit cached-result cases | Passed deterministically; SMB native coverage qualified |
| OWN-26 | Unix ownership/mask/name tests and unchanged permission/name-cache cases; native Linux evidence | Passed for enabled Linux x64; macOS new route withheld |
| OWN-27 | CAPABILITIES and ADR-10 corrected to source behavior and qualification | Passed; source/document review recorded with O7 |
| OWN-28 | PrincipalNames bounded cache/characters/queue/oversize/expiry/drop/generation/disposal tests | Passed |
| OWN-29 | Shared chooser note/help text; real Skia width test for two Windows names; short SID/full detail tests | Passed |

## Native gates

| Gate | Current result and shipping treatment |
|---|---|
| GW1 | Windows x64 ordinary-token native core cases passed, including unusual/long names and all promised link kinds. Exact UNC conversion has adapter coverage; representative UNC interoperability is GW4. Windows ARM64 native route stays Unsupported. |
| GW2 | Windows x64 final broken file/directory links and broken junction passed; Linux native final broken link passed. |
| GW3 | FAT32/exFAT fixtures Not run. No synthesized-owner suppression is enabled; OS-reported SID is retained with provenance qualification. |
| GW4 | UNC/mapped Windows/Samba, dropped connections and authentication/UI observation Not run. Safe ID/failure/provenance handling remains; no universal latency, authentication or round-trip claim. |
| GW5 | Native unreachable-domain lab Not run; bounded blocked-call deterministic cases passed. No cross-prefix circuit or local-name latency guarantee. |
| GW6 | ReFS Not run; provenance remains qualified. |
| GL1 | Ubuntu x64 WSL2 ordinary-user core/native stat comparison passed (O2 evidence). Mount/NSS diversity and release-runtime diversity remain qualified; Linux ARM64 stays Unsupported. |
| GM1 | Apple silicon/APFS/dataless/materialization policy Not run. New guarded macOS Owner/Group remains Unsupported; wrapper/retirement has fake coverage only. Existing Permissions route is unchanged. |
| GA1 | Passed in the coordinated disposable Windows guest, ordinary filecatdsa_r2 token, signed NVDA 2026.3.1, d62b8f24. Actual speech contains full Owner identity/detail and controlled Offline unavailable reason. Fixtures unchanged; app/reader closed, temporary task removed and sessions preserved. See GA1-20261116.md. |

## Completed performance and integration validation

PF1/PF3 passed the predeclared four-process ABBA set: all ten-sample pooled timing/allocation medians and every
reservation check passed. See O5-20261116.md and 20261116-o5-affinity-summary.json. Earlier screenshot-driver and
uncontrolled timing misses remain diagnostic evidence, never relabeled passes. The final twenty-sample actual
analysis fixture passed its declared elapsed and reservation limits; PF2 passed its Owner-on band in O4. PF4's
independently shrunken row/character/shared-byte and final-owner lifetime fixtures passed.

The final integration workflow passed Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26, plus Linux/macOS
package build/start/inventory checks and the ARM64 package start/drawing/installer checks. Twenty TRX receipts
contain **60794 passed, 5773 NotExecuted**, zero failed test outcomes or runner errors in the accepted receipts.
The Verified scalar-order regressions passed on all four platforms; the Windows x64 Owner class passed 51/51,
zero skips. Exact clean-source SDK/compiler fingerprints and per-project/feature counts are in
[20261116-ci-final.json](20261116-ci-final.json).

| Platform | Passed | NotExecuted |
|---|---:|---:|
| Windows x64 | 15212 | 2340 |
| Windows ARM64 (including high-token refusal control) | 15201 | 2352 |
| Ubuntu x64 | 15202 | 529 |
| macOS ARM64 | 15179 | 552 |

Skipped platform, lab and opt-in measurements are not native passes. O7 records the initial hover fixture and
ordinary-token setup corrections, the Verified compatibility regression, and the numeric-worker-ID fixture fix.
The first final-source macOS Remote process crashed; its partial failed receipt remains preserved. The same-source,
unchanged-test/deadline macOS-only retry passed, including Core, Remote, App and Rendering; the crash cause remains
unproven. See [O7's chronological record](O7-20261116.md). No earlier failure was discarded or relabeled.

The UX ledger checker passed with 149 accepted scenarios and zero open/failed cases. Release-register validation,
relative documentation targets and whitespace checks passed. Later closing-record edits are documentation/evidence
only; no application or test source changed after the green dc22579a revision. No tag or release was published.
