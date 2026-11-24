# I643 — The diagnostics log keeps raw paths, though Export diagnostics says they are hashed

**Found:** 2026-11-24, by the documentation session that recorded I638 to I640 ([PR #25](https://github.com/benny-cz/FileCat/pull/25)), and confirmed by the release session in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). Every file cited below is identical at rc.2 and at `main` `565f7577`. **Status:** open; a fix is proposed for the next candidate, and the owner decides.

## Observation

**Tools → Export diagnostics** lists what its ZIP will hold before you share it. For the log it says "the diagnostics log (paths are hashed)", and adds a note only when diagnostic mode is on (`src/FileCat.App/Services/DiagnosticsBundle.cs:24`). The ZIP then takes `filecat.log`, `filecat.log.1` and `crash.log` from the profile's log folder, unless FileCat runs as administrator (line 48).

The log hashes only the paths that FileCat's code passes through `AppLog.P` (`src/FileCat.Core/Diagnostics/Diagnostics.cs:73`). Other text reaches it unchanged:
- **Error messages.** `AppLog.Write` appends the exception's type and its `Message` as they are (line 83). The .NET messages for file errors quote the whole path, for example "Could not find file '…'" and "Access to the path '…' is denied." At least 51 log calls pass an exception, among them `Job {id} failed` (`src/FileCat.Core/Jobs/JobManager.cs:631`) and `Command {id} failed` (`src/FileCat.App/ViewModels/MainViewModel.Commands.cs:213`).
- **Paths written by hand.** At least two calls put a path straight into the message:
  - "A checksum file or signature could not be read: {path}" (`src/FileCat.Core/Verification/Sidecars.cs:130`);
  - "FileCat kept the temporary saved edit at {path}: {error}" (`src/FileCat.Platform.Windows/WindowsFileOperations.EditCopy.cs:194`).
- **Crash reports.** `crash.log` receives each unhandled exception whole, its message included (`src/FileCat.App/App.axaml.cs:169` and `:183`).

So a user who reads the preview and shares the ZIP can share the names and folders of their own files, which the preview told them were hashed.

**Not shown live.** No log with such a line has been produced on the installed candidate for this record; the finding rests on the source above.

## Documents

PR #25 corrects SECURITY.md and docs/PRIVACY.md. Both used to say that the log hashes paths. Both now say that error messages and crash reports keep the system's own text, which can name a file. The product's own preview still says "paths are hashed".

## Remedy (proposed) and its cost

When diagnostic mode is off, replace the paths in error messages and crash reports with the same `path#…` hashes, route the two hand-written paths through `AppLog.P`, and make the preview say exactly what remains. Server error text, which can name remote paths without quotes, needs its own decision. This changes product source, so it reaches users only in a candidate after rc.2.
