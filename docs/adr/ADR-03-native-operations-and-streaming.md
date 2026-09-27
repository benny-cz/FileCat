# ADR-03: Native operations versus common streaming

**Status:** Decided for v1 (2026-09-27).

## Decision

One operation engine (`JobManager` plus executors) owns policy for every job:

- conflicts and staged publishing;
- journaling;
- decisions and "apply to all";
- per-device queues and overlap detection;
- verification and truthful outcomes.

Strategies underneath:

| Work | Strategy |
|---|---|
| Local file copy | `CopyFile2` into a staged name, with a progress callback. This gets block cloning on ReFS and Dev Drive and SMB server-side copy. The file is then published with `MoveFileEx`. |
| Local move | `MoveFileEx` within a volume. Across volumes: copy, publish, revalidate, then delete the source. |
| Delete | `DeleteFileW` / `RemoveDirectoryW` with `\\?\` long paths. Links are never followed; folders are removed only when empty. |
| Recycle | `IFileOperation` on an STA thread, with a pre-delete abort when the Recycle Bin would delete permanently. Each outcome is verified; restore uses the bin item's `undelete` verb. |
| Provider content (ZIP members) | Managed streaming (`StreamTransferExecutor`) under size, ratio, and name limits, propagating Mark-of-the-Web. |

## Why

Native copy keeps Windows fidelity: streams, attributes, cloning, and offload. The single policy engine keeps behavior
consistent, including the staged-name rule and every error explanation. TV-03 on this machine confirmed the recycle abort guard and the
restore mechanism.

## Consequences

- If a destination cannot store alternate data streams or Mark-of-the-Web, the loss is named per file.
- Failures are classified (full, offline, AV-blocked, in-use with quiet retries, and so on). Fault-injection tests cover them.
