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

## Fix

Made from `main` at `f4e42468`, in commit `003653fe` and two test commits after it, `27e545f6` and `81e76c46` ([PR #36](https://github.com/benny-cz/FileCat/pull/36)), by a session working in its own worktree. It is not in any candidate yet. The status above is unchanged: the owner decides.

### Change

- **The log and crash reports.** With diagnostic mode off, `AppLog.Write` hashes the paths in the whole record it writes: the message, and the error's type and own message (`AppLog.Record`). `AppLog.AppendCrash` hashes the paths in a crash report. Each path becomes the hash `AppLog.P` gives the same text, so support can still match one path across lines. The rule is `LogRedaction` (`src/FileCat.Core/Diagnostics/LogRedaction.cs`). With diagnostic mode on, both write as before.
- **The export.** With diagnostic mode off, **Export diagnostics** hashes `filecat.log`, `filecat.log.1` and `crash.log` again, line by line, as it copies them into the ZIP (`DiagnosticsBundle.CopyLog`). So lines written while diagnostic mode was on, and lines written by an earlier FileCat (an rc.2 log carried over), leave hashed too. A line already hashed comes out the same. The files on disk are not changed. With diagnostic mode on, the files are copied as they are.
- **Paths written by hand.** Eight log calls put a path straight into their message. They now pass it through `AppLog.P`, and a ninth call that logged a path twice is gone (list below).
- **Preview.** The log line of the preview now reads "the diagnostics log and crash reports, if any (paths are hashed, but a message can still name a file, folder, or server)". With diagnostic mode on, it reads "(diagnostic mode is ON, so paths are included)". Run as administrator, it still says that the logs are omitted. Crash reports moved into the log's line, since the same rule applies to both.

### The rule

Each line is read on its own; a log record counts as one line, whatever its message holds. Two passes find the paths:
1. **Quoted paths.** .NET quotes the whole path in its file errors ("Could not find file '…'.", "Access to the path '…' is denied."). Text in quotes ('…', "…", ‘…’ or “…”) that begins as an absolute path is hashed whole, spaces included. A quote inside a name (Bob's, Rock 'n' roll, Teachers' notes) does not end it. The closing quote is the first quote, followed by a space, punctuation or the end of the line, after which the rest of the line up to the next quoted path holds its quotes in pairs. Where no quote does, the last of them closes the path.
2. **Unquoted paths.** A server's error text names paths without quotes ("550 /home/u/My files/a.txt: No such file"), and so do some of FileCat's own messages. A path that begins a word runs to the end of the last word, among the next eight, that holds its folder separator, or to a word ending in ':' or ';' within two words of that (C:\Users\jan\Shared Folder: reason). It stops at another path, at marks alone (the " | " before an error's type), and after a word that ends in ':' or ';'. Closing punctuation stays outside.

A path begins with a drive (`C:\` or `C:/`), `\\` (`\\server\share`, `\\?\…`, `\\.\…`), a single `\` (`\Device\…`, `\??\…`), `/`, `~/`, or an address with a scheme (`file:`, `sftp://`, `smb://`, `https://`). A root alone (`C:\`, `/`) is kept: it names nothing of the user's, and a message about a drive still says which drive. The source location of a stack frame ("   at M() in D:\a\…\X.cs:line 12") is kept: it is where FileCat was built, and a crash report needs it.

### What the log can still hold, with diagnostic mode off

- **Names without their folder.** No rule tells a bare file or folder name, a relative path or a path inside an archive from other words. Examples: "An item named "plan.docx" already exists", "Could not open "report.docx": …", job titles in notifications, a place's title in "Opening {place} failed", and the file name in the Shell helper's "did not answer … for {name}".
- **The end of an unquoted name with spaces.** When no ':' follows it, the words after its last space stay: "C:\Docs\My notes.txt is missing" keeps "notes.txt". The same holds for an unquoted path that runs on past eight words.
- **Server and account names** written without a scheme: "Could not connect to jan@example.com: …". An address with a scheme is hashed whole.
- **A line break inside a path** (a name can hold one on Linux and macOS): in a crash report, the part after it stays. A log record is hashed as one line, so it keeps none.
- **Where FileCat was built.** An official build's stack frames name the CI's folders (`D:\a\FileCat\…`). A FileCat built from source names its builder's source folder.
- Registry key names, such as "Access to the registry key 'HKEY_…\…' is denied.", are not file paths and stay.

The rule errs towards hashing more. A stray apostrophe later in a line ("… to the users' folder") takes the words before it into the path's hash, and a two-word phrase before a ':' ("{path} needs review: …") can be taken for the end of a name. The hash then differs from the path's own, but no part of the path shows.

### Log calls checked

All 183 calls of `AppLog.Info`, `Warn`, `Error` and `Write` in `src` at `f4e42468` (70 files), including the calls that continue on the next line:
- **Now through `AppLog.P`:**
  - `src/FileCat.Core/Verification/Sidecars.cs:130` and `src/FileCat.Platform.Windows/WindowsFileOperations.EditCopy.cs:194`, the two this record names;
  - `src/FileCat.Core/Edit/EditPrivateCleanup.cs`: `RemoveEmptyFolder` ("FileCat kept its temporary folder …") and `Kept` ("FileCat kept its temporary edit file …"; the text it returns for the user keeps the path);
  - `src/FileCat.Core/Edit/EditSessions.cs:229` ("FileCat kept its edit snapshot …");
  - `src/FileCat.Core/Edit/EditSessions.Creation.cs:213`, the retained-creation text: the user's copy keeps the folder, the log's copy has it hashed;
  - `src/FileCat.App/Services/NativeBenchmark.cs:52` (the benchmark's output file);
  - `src/FileCat.Platform.Windows/Shell/ArchiveAppContainer.cs:157` (a portable copy's folder).
- **Removed:** `src/FileCat.Core/Edit/EditSessions.cs:1108` logged the text `Kept` returns, path and all, after `Kept` had logged it.
- **Already hashed:** `MainViewModel.Checksums.cs:243`, `ChangeMonitor.cs:161` and `HostKeys.cs:159`.
- **Error text, now under the rule:** every call that passes an exception; the calls that append `ex.Message`, a server's text or a helper's output (the page engines, Git status, the viewer, the Secret Service, the Shell and context-menu helpers); and "User notification: …" (`MainViewModel.cs:266` and `:270`), which logs error notifications that can name paths.
- **Names, not paths, left as they are:** `ShellHostClient.cs:116` and `:123` log the file name a Shell handler hung or crashed on, and Settings shows the same text when Shell pictures turn off. `MainViewModel.Lists.cs:78` logs a place's title.
- **No path:** the other calls log fixed text, type names, counts, HRESULTs, ids, the profile's name, setting names, the terminal's shell name (`auto`, `cmd`, `pwsh`, `wt`, `posix`) or a language file's culture.

### Tests

New tests:
- **`LogRedactionTests`** (Core, 14 tests, 39 cases):
  - each form of path inside six .NET error texts (a drive with `\` and with `/`, a share, `\\?\`, `\\?\UNC\`, `\\.\`, `\Device\`, Linux, macOS, `~/`, `file:`, `sftp:`);
  - the hash is the one `AppLog.P` writes;
  - quotes inside names, two paths in one message, a quoted value after a path, double and typographic quotes;
  - unquoted paths: a server's text, names with spaces up to the reason, brackets, a scheme prefix, addresses, a share, and a phrase before ':' that stays;
  - what is kept (bare names, a relative path, a server's name, roots, the end of an unquoted name) and what a stray apostrophe costs;
  - eight texts without a path come out unchanged;
  - a record with diagnostic mode on and off, still one line, with the error's type after a path kept;
  - a crash report keeps its stack frames, with LF and CRLF lines, and a quote that never closes stays within its line;
  - hashing twice changes nothing more;
  - `AppendCrash` writes the report hashed (skipped when elevated, where it writes nothing).
- **`DiagnosticsExportTests`** (App, 2 tests): the preview in its three states; an export with diagnostic mode off hashes the raw lines left in all three files and keeps the stack frame, and with it on copies them as they are.

Results, on this Windows 11 workstation at medium integrity (`whoami /groups` in each run: Medium Mandatory Level), with `TEMP` set to `%LOCALAPPDATA%\Temp`:
- **Before.** The change's behaviour taken out (no hashing, the old preview): `LogRedactionTests` 31 of 39 failed, and the 8 cases that passed are the texts without a path, which hold before and after. `DiagnosticsExportTests` 2 of 2 failed.
- **After,** at `81e76c46`: `LogRedactionTests` and `AppLogTests`, 40 of 40 passed. `DiagnosticsExportTests`, `ElevatedStateDiagnosticsTests` and `SecurityRouteTests`: 10 cases, 5 passed, 5 skipped (fixtures for the disposable Windows VM), none failed. Among the passes is `SecurityRouteTests.Failed_sidecar_scan_keeps_the_default_log_and_export_free_of_the_folder_path`, the earlier test of one hashed route.
- **Mutation check.** Each mutant below was built and run against the two classes, and each made at least one test fail. The Core mutants ran elevated, where only the `AppendCrash` test skips; that mutant and the App ones ran at medium integrity.

| Mutant | Failed |
|---|---|
| The record ignores diagnostic mode | the record test |
| Any quote opens a path | 9 cases (texts without a path among them) |
| Stack frames hashed | 2 (crash report, unclosed quote) |
| The first closing quote wins (no pairing) | 7 (quotes inside names, stray apostrophe) |
| No running on across spaces | 2 |
| Roots hashed | 2 |
| No ':' end for a name with spaces | 1 |
| No stop at marks alone | 1 (the record test; added after this mutant first survived) |
| No unquoted pass | 4 |
| No pass for double quotes | 1 |
| The record not hashed | 1 |
| `AppendCrash` not hashed | 1 (medium integrity) |
| The export copies the files as they are | 1 |
| The export hashes in diagnostic mode | 1 |
| The preview ignores diagnostic mode | 1 |

- **Whole suites,** at `003653fe`, both at once and at medium integrity: Core 12,461 tests, 0 failed, 1,408 skipped (fixtures and hardware this workstation lacks), 1,479 s. App 3,989 tests, 0 failed, 294 skipped, 3,047 s. The two later commits change only tests: one more assertion in the record test, and the export test's attribute.
- `python -I eng/validation/validate-release-issue-register.py` passes; no ledger was changed.

### Not verified

- No installed FileCat has this change, and no log or crash report was made by a running FileCat for this section: the tests call the log's own code.
- Not run on Linux or macOS here; hosted CI runs the new tests there.
- The changed call sites have no tests of their own. The rule hashes such paths too, unless their last name holds a space.
- SECURITY.md and docs/PRIVACY.md, as PR #25 words them, stay true: an error can still name a file. They could now say that the paths in error messages and crash reports are hashed too.

### Check on the next installed candidate

On the installed Windows candidate, and once on Linux and macOS, with diagnostic mode off:
1. Make FileCat log a file error that names a path: for example, verify checksums in a folder whose `SHA256SUMS` the user may not read. Export diagnostics. The preview reads as above. In the ZIP, `filecat.log` shows `path#…` where the error named the path, and neither the folder's path nor the checksum file's appears.
2. Start the candidate on a profile whose `filecat.log` holds an rc.2 line with a raw path, and export. The ZIP's copy has the path hashed; the file on disk is unchanged.
3. Turn diagnostic mode on and export again. The preview says diagnostic mode is on, and the paths written since then appear as written.

Stop when all three pass. Reopen only for a path of the user's, outside the kinds listed above, in a log or crash report exported with diagnostic mode off.
