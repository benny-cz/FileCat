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

Committed clean-source/native revalidation and successor CI are pending. No human
Mac dialog case is queued. I06 remains open for aggregate pictures, Windows icon
and other materialized/queue scopes. No candidate/human GO; **NO-GO** remains.
