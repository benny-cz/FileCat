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


## Clean CI and first guest outcome — investigation in progress

Clean f341dfd passes all four jobs in [CI 37224753782](https://github.com/benny-cz/FileCat/actions/runs/37224753782);
three package jobs are declared skipped. Three server digests and six full TRX inventories verify in
`ci-37224753782/independent-ci.json`, SHA-256 `dbe05ac8764b3d0dea62d1a4fb1e99f551cc0c88597d45faa852bb1dbaff7061`. All 43 affected Windows
cases pass. Each Unix App lane has 28 affected passes and fifteen declared Windows factory/ACL/drive-letter
skips. All sixteen new I129 cases pass per platform. The initial verifier's 30/13 expectation omitted two
drive-letter guards; its source and correction record are retained, no product result altered.

The SDK-free guest's first run ends at 18:36:34 UTC with **41/43 passes, two failures, zero skips**.
All sixteen new I129 cases pass. Both failures are existing FolderCountLowerBoundTests zero-visible-byte
refresh cases (deny true/false), waiting for the real quick-view debounce/event caption. The failed XML,
stdout/stderr and controller error are retained, XML SHA-256 `1c28f973074e3f67719c1ec06c141fcf0dbb0da46ff05c720bac5db011028a42`.
375 pinned payloads/twenty-one source inputs were verified in guest; controller 5800/worker 8896 are absent
and owned temp/listing files are empty at 18:44:07 UTC. This is a failed affected run, not complete guest
qualification. I127's new native caption failures remain under investigation.

A separate seven-case diagnostic assembly uses the same exact f341dfd production DLLs and adds observation
only after a failed caption wait. Its isolated guest run passes 7/7 at 18:54:49 UTC; it does not explain or
erase the original failure. Its changed fixture/order makes it diagnostic evidence only. XML SHA-256
`000ca10614d7a6097671699d273e3fe8a1cf77463c0c4841dfcb4b31e3aba7c0`. Further controlled caption-demand investigation continues. Overall NO-GO.

The owner now authorizes the Mac when needed through SSH. Read-only identity succeeds: macOS 26.6.2/25G83,
arm64 MacBookPro17,1, eight logical CPUs/16 GiB, benny in admin group. No remote mutation or key-content
exposure occurs. `mac-owner-access.json` SHA-256 `8ae9fc4221fdc093d0bdb31a38496a10013d72b742b74a136d81632dc26f396a`. Physical/native
Mac qualification and its distribution/credentials decisions remain required; SSH availability alone
does not close them. Both VMware guests remain running and G: remains untouched.
