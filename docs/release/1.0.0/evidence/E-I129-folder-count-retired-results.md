# E-I129 — canceled folder counts reject queued progress and results

Preliminary V12 / AI-03 remediation; no candidate qualification. Execution resumes at clean main
`cc5b358d4d07951665f4f3d480bc6bb36852a398`; retained baseline production comes from clean
`c45392542055a0d8dbd4ab1568f78596e38e3b01`. Private evidence is under
`FileCatReleaseEvidence/folder-count-retired-results-20261004` in the existing C-drive evidence root.
Both VMs stay running. No desktop input, physical-device access or USB mutation occurs; the historical
USB source-change gate remains held. Computer Use's previously recorded startup failure is not retried.

## Reproduction before modification

The independent headless assembly references actual retained clean production DLLs. The original
697-payload manifest is rechecked against SHA-256
`1626fc189dd8c7074cdec3a739b5db399abbe68ef854f8f9488390c00eabbac2`, producer identity and every
copied DLL. Four relevant source files are compared to the clean producer before editing.
Owned files contain 1,000 bytes in one counted folder and, in the many-folder cases, 31 additional
folders of 37 bytes each. A retry deliberately writes 2,345 bytes to the first file; its expected
post-write SHA-256 is independently checked. Marks and focus must remain stable.

The first eight cases hold the final identity call while canceling/retrying, replacing demand or
letting the count finish. All eight pass on unchanged production: the scheduler cancels a held task.
That does not establish safety after the worker has completed. A second boundary captures the actual
SizeFolder UI callbacks through its IUiDispatcher, permits the worker to finish, then cancels or
retries before replaying the already queued callbacks in their original order. Factory/dispatcher
substitution is controlled test instrumentation, not a real native device or desktop input.

Six of sixteen baseline cases fail. Queued progress/final callbacks make a canceled count complete,
overwrite a completed 2,345-byte retry with the old 1,000 bytes (marked total 2,147 instead of 3,492),
or clear the newer size and publish an obsolete replacement warning. Ten held-worker/active-completion
controls pass. Baseline v2 and v3 are retained; v3 XML SHA-256 `4daea00db1376e2fb0e7151f8a2b15340558a0212198337ed76e47432b44caff`.
The initial probe compile failure omitted the EntryFlags namespace; its complete logs are retained
as a harness-only failure, not a production result.

## Correction and controlled host validation

Progress and completion posts now check the count's open state and cancellation token at application
time. Completion captures that eligibility before ending its bookkeeping. This rejects a result from
an already completed task after Esc or new demand has retired it. Unmarked canceled sizes reset in the
stop callback, preserving that behavior without letting a retired result clear newer state.

The identical sixteen-case probe passes with only FileCat.dll changed; every other loaded input,
including the probe assembly and Core DLL, remains byte-identical. Corrected XML SHA-256
`b5d4a3423919a79e12eb95c601dad7cd9668b795d2f4ce845e086ee30c8ccbf5`. Sixteen regressions exercise both boundaries, 32 marked folders,
identity replacement, truthful completion/retry state, independent byte totals and unchanged marks/focus.
The repository regression injects only the owned AppServices adapter and dispatcher, so it can run on
all platforms; the independent baseline probe uses the Windows factory.

| Working host inventory | Pass | Declared skips |
|---|---:|---:|
| Identical corrected probe | 16 | 0 |
| Affected headless App | 43 | 0 |
| Full headless App | 306 | 21 |

All 742 captured inputs, twenty-one source/build inputs, selective replacement and full case IDs,
multiplicity/counts/skips independently verify. Working manifest SHA-256
`9f89631b28f6e09a7fe009073de76e9569bc71207d12a259574473d0429dbbc0`; independent proof SHA-256
`a301d19fac4654765d416373fc8ccddbc3fe6baa3b11c902d05922b5cbe555db`. These builds use cc5b358 plus the two-file source/test overlay.
The 21 full-suite skips retain their actual prerequisite reasons; they are not native passes.

Clean committed CI and SDK-free guest revalidation are next. Actual Esc/native frame/AT behavior,
slow/cloud/hung hardware, sparse/hard-linked data and final-candidate qualification remain open.
Earlier count passes do not qualify these queued-callback races. Overall **NO-GO**; no candidate or human GO.
