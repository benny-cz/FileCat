# Bound storage history discovery and reads — 2026-11-17

This continues the [retention checkpoint](2026-11-17-storage-retention.md) on `main`, HEAD `68a435dc2a1d6122f5961f867bcb653b0e6968aa`, with uncommitted changes. WP3 remains partial. WindowsPlatform still supplies NoStorageTransforms, and the executor still refuses execution. EFS is required by the active goal.

## Implemented

The Windows evidence backend now supplies a held, case-aware, no-follow directory cursor. It validates the invoking-user namespace and actual volume binding, transfers the final directory handle from its temporary opening envelope into the dedicated one-handle cursor lane, and streams 64 KiB native batches. Opening the optional reviewed directory does not follow a reparse point. Empty dot-only batches do not prematurely end enumeration. Failure, cancellation and partial consumption retire the cursor owner.

Native accounting and retention now consume this cursor instead of managed discovery. Each journal/artifact must match its cursor identity and length when reopened through the private backend. The accounting pass binds both journal and artifact enumeration to the same captured root identity. A disappeared previously observed root cannot become a successful empty inventory. Duplicate journal associations use a bounded compact GUID set, replacing repeated full-directory scans.

Incremental history pages use the same cursor and a separate read-only private evidence authority. The latter has no acknowledgment/publication/deletion capability. It opens journal data relative to the held directory, validates private security, ordinary single-stream/single-link state and volume identity, and retains the independent evidence envelope through the actual read. A history page examines at most 64 directory entries and reads at most 1 MiB of bounded headers; unrelated names are skipped without opening them. Replaced namespace/entry identities remain visible as damaged descriptors.

Live detail reads use that read-only authority and the existing durable-prefix digest/endpoint validation. They can coexist with the writer's blocked later flush without taking its lock or claiming current unflushed bytes. The reader grants no mutation or closure authority. Inactive exact review and acknowledgment continue to require the separate exclusive private owner.

JobManager exposes directory-scoped discovery, detail, accounting, exact review and acknowledgment entry points. Full paths, parent traversal and stream syntax cannot select another history file. Its storage recovery query consults the validated acknowledgment. The App's five recovery availability checks now go through this manager; other operation kinds retain the existing recovery semantics. Startup storage enumeration, retained-pair presentation and storage report actions still need their UI integration.

## Verification

Windows builds completed with zero warnings/errors. The final focused Core selection discovers 240 cases, including cursor retirement/cancellation and namespace/entry mismatch cases. Windows passed 239 with one existing mount-fixture skip; Ubuntu and macOS each passed 238 with two existing platform/fixture skips. Zero failures.

The normal-user Windows 11 x64 NTFS selection passed 30 native cases without failures/skips. Added fixtures cover multiple native batches over 601 entries, bounded header pages amid unrelated entries, redirected reviewed-directory refusal with lane reuse, a previously durable prefix during a blocked flush, and manager acknowledgment-aware recovery with a busy writer. The log records medium integrity. This is scoped evidence, not qualification of every supported Windows build or architecture.

The expanded App recovery/Operations/Undo selection discovers 150 cases. Windows passed 149 with one existing case-sensitive-filesystem skip; Ubuntu passed 148 with two existing skips; macOS passed 147 with three existing skips. Zero failures. It includes the archive recovery, interrupted rename and interrupted transfer-intent UI fixtures in addition to the earlier Operations/Undo selection.

Transport archive SHA-256 values, retained in `.tmp`:

| Artifact | SHA-256 |
|---|---|
| filecat-storage-history-source.tar.gz | `1C759FA0382485C4133ADBC64A7923969DD1F054B2BB32F640FC2E01E565D505` |
| filecat-storage-history-native-patch.tar.gz | `DB65ABF98509F1F930F1AC486D4E7FC8BF7AEC5504665FF599FD95723080910B` |
| filecat-storage-history-core-binaries.tar.gz | `822BA573D48199C774D0B33F0D3031E7EECE32C398CE82BC4804081510E6859A` |
| filecat-storage-history-ui-patch.tar.gz | `B4C355E44B3B882D65446D160E165FBDEE72C250857040600815F6F5CDF8A8CA` |

Logs use FILECAT_STORAGE_REVIEW_NATIVE_FINISHED and FILECAT_STORAGE_CORE_FINISHED on Windows. Ubuntu/Mac reuse FILECAT_STORAGE_RETENTION_FINISHED in the history-named scripts/logs. Windows App uses FILECAT_STORAGE_UI_FINISHED.

## Remaining acceptance

T-REC.C and T-LIMIT.C remain partial. Finish storage startup/same-session candidate publication, selected and retained report UI, export and acknowledgment actions, large-history cost, dirty cross-instance observations, malformed/inherited artifacts, partial-prune faults and actual disk/device/crash qualification. The 30-case native selection does not close the full 256+188+1+3 ownership gate or the remaining provider/performance gates.

Connect the bounded traversal executor, overlap/closed-ID accounting and root completion to durable call execution. Complete the command/review/origin/mark/refresh and Operations surfaces before composing the product adapter. EFS binding, keys and output routes, plus the companion filesystem plan's unfinished packages, remain required.
