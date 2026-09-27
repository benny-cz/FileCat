# ADR-04: Durable operation journal and reconciliation

**Status:** Decided (2026-09-27). This replaces the plan's SQLite recommendation.

## Decision

Each job writes an **append-only journal** (`job-<time>-<id>.fcj`). Every line is `crc32 json`.

- **Durability is tiered.** Intents before destructive or externally visible steps are flushed to disk synchronously;
  other records are group-committed.
- **Torn lines are skipped** on recovery. A recovery note is appended on a fresh line.
- **Headers are bounded.** They hold a 64-item source sample plus the exact count. Recovery streams the records.
- **Retention.** At startup, `JournalRecovery.Scan` lists interrupted jobs, meaning those with no `end` record. It prunes
  finished journals beyond 200 files or 30 days.
- **Review.** The user sees what was in flight: open intents and staged leftovers that are safe to delete.
  FileCat inspects the current state before offering anything. It never replays blindly.

## Why

The plan preferred SQLite if measurements favored it. The append-only format turned out simpler:

- no native dependency or schema migrations;
- the writer never contends with a reader;
- a torn write loses at most one record.

Recovery needs only a sequential scan. Querying history across jobs is not a v1 need.

## Consequences

- Cross-job history queries, if ever needed, would require an index over the journals.
- The journal records intent and outcome; it is not undo. Undo stays a separate, guarded, capped list per job.
