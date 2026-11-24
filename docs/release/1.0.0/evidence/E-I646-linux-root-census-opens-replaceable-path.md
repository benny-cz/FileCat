# I646 — The Linux root process census opens an executable path that another account can replace

**Found:** 2026-11-24 by the B12 review of the modes added since B12 (I641, [PR #28](https://github.com/benny-cz/FileCat/pull/28), finding F2), in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md). `src/` at `main` `f4e42468b` is identical to the tag. **Status:** open. A fix is proposed for the next candidate, and the owner decides.

## Observation

`--filecat-recovery-process-census` runs as root by design during deleted-file recovery. It goes through `RecoveryProcessCensus.cs:43` and then `SingleInstance.cs:110`, `:136-141`, `:145` and `:157-180`. For every process it:
- opens `Process.MainModule.FileName` with a plain `FileStream`, which follows links and blocks on a FIFO;
- reads up to 512 MiB from it.

The review checked dotnet/runtime v10.0.12: for a deleted executable, that name keeps the ` (deleted)` suffix that `/proc/<pid>/exe` shows.

## Why it matters

Any local account can:
1. run a program;
2. delete it;
3. create `<path> (deleted)` as a FIFO, or as a link to a device.

The census, running as root, then either hangs on the FIFO, so FileCat cannot finish and the scan is refused, or opens that device and reads from it as root.

**Not run.** This rests on the source and the runtime's source; nothing was tried on Linux.

## Remedy (proposed) and its cost

Read the executable through `/proc/<pid>/exe` itself, which reaches the running file even after deletion. Otherwise open the path without following links and without blocking, and accept only a regular file. Keep the size cap. This changes product source, so it reaches users only in a candidate after rc.2.
