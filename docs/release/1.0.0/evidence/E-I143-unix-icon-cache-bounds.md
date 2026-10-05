# E-I143 — Unix icon cache and waiting work have no effective bounds

Confirmed 2026-10-05 while executing V12/I06/AI-10. The exact 348cbc7 production
FreedesktopIconSource getter is driven with 50,000 distinct owned extension names on
Ubuntu 26.04.1 VMware as ordinary UID 1000. An injected empty icon theme makes the
control independent of installed icon files; the actual production getter and worker
run unchanged. No files with those names, image contents or windows are opened.

Actual retained cache count is **50,000** and the sampled waiting-queue peak is
**43,795** (43,261 still waiting at observation). Managed heap grows from 81,360 to
7,411,592 bytes. This proves retained names/waiting work grow with demand; it does
not measure decoded-icon bytes, drawn frame latency or an OS memory limit.

Mac source has an unbounded queue and clears a 4,096-entry cache while old worker
results can reinsert into it. The source review identifies the same missing admission
and request-identity controls; no native Mac baseline failure is invented.

## Remedy and working checks

Linux and Mac sources now share a 4,096-entry LRU cache and a nonblocking 256-request
waiting queue, with one existing native worker per source. A full queue leaves the
existing vector fallback and permits a later redraw to retry. Queue refusal creates
no retained cache entry. Completed unavailable icons remain cached to avoid repeated
failed loads. Clearing drains queued work; evicted queued entries are skipped before
loading. Entry identity prevents an active old result from repopulating a cleared or
evicted key, even after the same key is requested again.

Unpublished stale bitmaps are disposed. Evicted published images can still be borrowed
by visible rows, so they are released from the cache without prematurely disposing a
displayed bitmap. Bounds cover retained cache references and waiting requests; they
do not bound all images retained by consumers or peak native renderer allocation.

Six new controls cover a held worker/50,000 requests and retry, LRU/borrowed-image
lifetime, clear/re-request identity, active-result rejection/disposal, skipped evicted
queued work, and concurrent producers/worker completion. Working affected host checks
pass 8/1 declared skip; final full App passes **351/23 declared skips/374 total**, zero
failures, including all six new cases. Original network-contact fixture remains
explicitly skipped without its capture prerequisite. Windows icon provider, broader
picture/queue budgets and drawn/native/candidate qualification remain outside this fix.

## Exact evidence

| Private item under `mac-resume-20261005` | SHA-256 |
|---|---|
| `i06-icons-baseline-v1/independent-guest-v1.json` | `2c693db8cae82e00befa96860069ff87b95ade224c20ba5e0eaa0a4103d54272` |
| `i143-host-v3/i143-full-app.trx` | `d60e18dee86bd886fb61648b385926a3059eceed58c9f4465a04d1b146c094fc` |

The native baseline independently verifies all 261 payload pins before/after, four
retained pins, transport digest, exact VMX routing/actual OS, ordinary identity,
process absence and empty owned temporary root. The source's empty-theme constructor
is invoked through reflection only in the private observation wrapper. It does not
modify the production getter or worker.

Committed clean-source/native and successor CI revalidation pass below. No human
Mac dialog case is queued. I06 remains open for aggregate pictures, Windows icon
and other materialized/queue scopes. No candidate/human GO; **NO-GO** remains.

## Clean committed native revalidation (2026-10-06 local)

Production is `15599332188f252c995aab89bce655946cdc7a08`. All 861 raw Git blobs verify their
object IDs, SHA-256, paths/modes before publication with the exact SourceRevisionId.
The archive is `a007c2bb6d2077985d1ac44c11d15708a104a865be9462ef08ecf59b360958e1`; producer proof is
`4a46ce3e8b47f01ff9e42a1f636aa8925cc9597546e29d9c9fb8a5ac9485389b`. Clean Mac arm64 UID 501 and Ubuntu 26.04.1 VMware
x64 UID 1000 each execute the complete nine-case affected inventory: **7 pass,
2 declared Windows-only skips**, including all six new controls. Each independently
verifies 350 payload pins before/after, six retained pins, native results/XML and
source manifest, process absence and empty owned temporary roots. No native dialog
or drawn FileCat workflow is used.

The same 50,000-entry production getter probe on clean Ubuntu now retains **2,538**
entries, observes a queue peak of **256**, and ends with **zero queued requests**.
Managed heap after forced collection is 608,392 bytes, versus baseline 7,411,592.
These are one finite schedule's actual measurements; the hard bounds are separately
validated by the held-worker and LRU controls, rather than inferred from that count.
The probe's diagnostic adapter reads the new cache properties; its entry names,
empty-theme fixture and production GetIcon call sequence are unchanged. App and Core
DLLs are byte-identical to the clean affected-test payload. All 261 probe input pins,
four retained pins, native/transport hashes, process and temp cleanup verify.

| Private proof under `i143-clean-v1` | SHA-256 |
|---|---|
| `mac-executed/independent-native-v1.json` | `953ae272444f06e1022e6be963fdee8b2d343806497b354e9d6c9504c52e1131` |
| `linux-executed/independent-guest-v1.json` | `388c545f269f65665c0e8ce4ef6142ea87f2b67504424d1353db7a961e0d988e` |
| `linux-probe/independent-guest-v1.json` | `bb6fd09615cfa8f0f1f5df43d0a4df5e5158cf389141dc71f955d3cf1f2f6cdb` |

CI 37379380371 passes all four required lanes on that exact source, with independently
verified server digests/full inventories below. Full candidate/frame/native
icon rendering, Windows icon/aggregate picture and other materialized/queue scopes
remain. Current Mac testing ends; both temporary sleep-disable runs and the owned
caffeinate are now independently restored/stopped (E-ENV-MAC-1). No additional Mac
interaction is queued. **NO-GO** remains.

## Exact clean-source CI qualification for the remedy

[CI 37379380371](https://github.com/benny-cz/FileCat/actions/runs/37379380371)
attempt one passes all four required build/test lanes at exact 1559933; all three
package publication jobs are skipped. Four downloaded server archive digests and
six complete TRX inventories independently verify matching definitions, unique
executions, zero failures and reasons/stdout for every declared skip. All three
TRX-bearing App inventories match the full host's exact 374-name case multiset.

| Complete TRX inventory | Passed | Declared skips |
|---|---|---|
| app-test-results-macos-latest/FileCat.App.Tests | 331 | 43 |
| app-test-results-ubuntu-latest/FileCat.App.Tests | 329 | 45 |
| test-results-windows/FileCat.App.Tests | 357 | 17 |
| test-results-windows/FileCat.Core.Tests | 787 | 57 |
| test-results-windows/FileCat.Platform.Windows.Tests | 165 | 34 |
| test-results-windows/FileCat.Remote.Tests | 88 | 28 |

The nine affected icon cases pass 8/1 declared skip on Windows and 7/2 declared
Windows-only skips per Ubuntu/macOS lane. All six new bounded-queue/LRU/lifetime/
stale/concurrency controls pass in each of those per-case inventories. ARM64 App
logs retain 357 pass/17 declared skips/374 total, with successful package start/
drawing and installer compilation. No per-case ARM64 TRX or physical ARM64/final
artifact qualification is inferred.

| Independently verified server archive | Artifact ID | SHA-256 |
|---|---|---|
| test-results-windows | 11372653858 | `a5b0183ca919cb09eb873ea8f842ca44a984db44b4b83cce1afce1ca6af6fbfb` |
| app-test-results-ubuntu-latest | 11373875887 | `8cdf5e07b3b29da5f7bc73466a64cba93e2038b434655f6a70dfa9de4c042c90` |
| app-test-results-macos-latest | 11373656349 | `99933c76ed31e2fc107acc286f621c683cace2578b21f0c85534b718b459128a` |
| windows-arm64-screenshot | 11372644388 | `0846f3096792b6f12eca387c1fb9374f62f43cdc73d168c6774f701695159fd9` |

Private `FileCatReleaseEvidence/ci-37379380371-attempt1/independent-ci.json` is
`7ca9ff4a03e672d33750eb34b19d17e6d51932f039cbbe0a9fb532d384d20b48`. Native source/payload/getter and original failure
proofs above remain separately pinned. I143 is remediated preliminarily on committed
host/native/CI evidence. I06 broader aggregate/Windows/native-frame/candidate gates
remain. No additional Mac interaction queued; **NO-GO** remains.
