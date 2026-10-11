# E-I359 — admit a burst beyond the available idle workers

Exact baseline **0a8a8ee612d79703cd437f1cd85cc7aa41a1aeb4 /1625 canonical raw blobs**, plus one identical four-case control. The first enqueue pulses an idle worker, but that worker still counts as idle until it reacquires the queue lock. Later enqueues previously kept pulsing that same worker and did not start additional available capacity. A controlled burst therefore occupied only one worker even when two or four slots were available.

The scheduler now pulses available idle workers and separately starts a bounded worker when queued work exceeds idle capacity. Existing active-worker and maximum-thread bounds remain. The control holds the existing queue lock across enqueues, uses actual threads and release gates, and sets a one-hour hang threshold; it uses no product hook or watchdog replacement. Each host/Windows guest/Ubuntu/Mac baseline retains **two passes/two failures**. All **sixteen final controls pass**, with exact result sequences, actual named worker counts, pending tasks until release, bounded live workers and zero final workers.

Host affected coverage is **101 passes/one skip**. Native affected inventories retain **Windows143P3S, Ubuntu137P9S and Mac136P10S**. The Windows final controller exited1 during post-test processing, after all test outputs existed; its complete original outputs were retrieved without rerunning tests. No unavailable child PID/token receipt or successful controller exit is claimed. Independent postchecks rehash the inputs and verify owned worker/scratch retirement. The shared batch checks **3492 native files** and restores **eight host roots/24 files**.

This scheduler defect was found while making the [rename stop fixture](E-I360-rename-stop-boundary.md) genuinely queued. It does not establish the cause of historical hosted rename or edit failures. Wider I06 resources/native/frame/reference/candidate scopes remain; no workstation UI, physical source or persistent machine setting changed.

## Current followup —2026-10-11

[Exact committed qualification](E-I359-I361-committed-qualification.md) now verifies this correction at d9889ce, and its [original four-lane CI](E-CI-scheduler-rename-archive.md) passes with all predecessor outcomes/messages/skips retained. [I362](E-I362-nested-spool-admission.md) is a separate newer source-lifetime correction requiring its own committed followup. Historical adverse causes and broader I06 scope remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payload, commands, original failures/skips and restoration. Shared batch receipts have separate scopes below.

| File | SHA256 |
|---|---|
| `idle-rename-sharp-batch-20261011-v1/independent-final-v1.json` | `376f7302b70e2bf13b50ab7aea97d0a99e851d6c653d5bd3e99d5b9a81541b62` |
| `idle-rename-sharp-batch-20261011-v1/retained-tool-sources-v1.json` | `b544a34363f54e8c460aaf87a50c6451fccb9658a3f8a1430596a533bf9b26bc` |
