# Exact storage call pages — 2026-11-17

Operation reports can now open **View recorded calls…** at the immutable durable prefix captured with their summary. The reader holds only the independent evidence envelope, releases it before returning a detached page, and never opens a historical user-file path. Each page revalidates the entire captured prefix (at most 64 MiB), including framing, record sequence, job association and SHA-256, before publishing any tentative rows. Later appends, including a still-blocked flush, remain outside that endpoint.

Pages retain at most 64 call records and 1 MiB of conservatively charged record/string storage. The reader stops retaining rows when either bound is reached, continues validating the prefix, and exposes a detached call-number continuation with explicit More. No cursor, journal stream, review pin or mutation authority survives a user wait. Each subsequent page deliberately repeats prefix validation; performance at the largest permitted journal remains a qualification obligation.

The page preserves exact paths, selected-root mapping, held identity, before state, desired state, disposition, no-call reason, observed result and postcondition. Active-prefix intents without results are labelled pending rather than inferred completed uncertain effects. Missing inactive results remain explicitly unestablished. Summary wording is conditional when no actual pending call is known. Paths/reasons are JSON escaped and the original three-part summary remains separate from call evidence and bounded examples.

Navigation replaces one displayed page, with First/Next controls. Copy/Save are labelled **this call page**. Cancellation, a late refresh, corruption or missing/pruned evidence cannot publish stale rows. The report producer remains owned until it actually returns after close. Whole-prefix live export and paged navigation from inactive recovery reports remain outstanding; existing verified inactive raw export is unchanged.

Accepted disposable-machine runs:

| Selection | Windows 11 x64 VM | Ubuntu x64 VM | macOS ARM64 |
| --- | --- | --- | --- |
| Core storage/scheduler/recovery selection, 289 cases | 288 passed, 1 environment skip | Not repeated | Not repeated |
| Expanded App selection, 242 cases | 241 passed, 1 filesystem skip | 238 passed, 4 platform skips | 237 passed, 5 platform/filesystem skips |
| Core call/recovery selection, 45 cases | Included above | 45 passed | 45 passed |
| Final App selection after conditional summary wording, 37 cases | 37 passed | 36 passed, 1 Windows-only skip | 36 passed, 1 Windows-only skip |
| Native storage + Windows records, 57 cases | 47 passed, 10 privileged/opt-in skips | Not run | Not run |

Every accepted run has zero failures/errors and its completion marker. Builds completed with zero warnings/errors. Native checks ran at medium integrity, including the blocked-flush reader and all earlier ownership/retention cases. No native test ran on the workstation. Local receipts are `.tmp/filecat-storage-calls-{windows-core,windows-ui,ubuntu,mac,native}.log` and `.tmp/filecat-storage-calls-final-{windows-ui,ubuntu,mac}.log`.

SHA-256 inputs:

- Final source patch: `F6329E84135E7D8BEEA71C5F3711EBCDA9A0DE9490727FB5C0C8BAD07E48C7F0`.
- Final UI patch: `7D0ABCDB5B90037C3A452918A58B27C5950B8E8CD3E7F00B874A5C9B14276C1A`.
- Core patch: `A24E949D08015A58D68DB3190C35058338261EC917E91BD1C85B9790118DDF29`.
- Native patch: `81A642F9272E6167CC13178F61D0BEB2F4AAF11C92034B88D70CE2DCE0A4753E`.

Hosted CI [44084328695](https://github.com/benny-cz/FileCat/actions/runs/44084328695) was still running for the prior accounting checkpoint at this writing. These focused passes do not establish the complete CI result. Both implementation plans remain open, including product command/review/refresh integration, the wider qualification matrix and all EFS identity/key/output gates.
