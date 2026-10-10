# ADR-06: Worker isolation — the Shell integration host

**Status:** Decided (P7, TV-16, 2026-09-28). P8 and P10 adopted no native parsers: archives (SharpCompress, DiscUtils),
inspectors, and recovery are managed, bounded, and fuzzed (ADR-07, ADR-08), so this helper is the only worker. A native
engine, if one is ever adopted, runs out of process under the same kind of restrictions.

## Decision

**Shell handlers never run in FileCat.** Thumbnail and icon handlers are third-party native code that parses file
content. They run only in `FileCat.ShellHost.exe`, a helper beside `FileCat.exe` that FileCat starts on first use.
The helper answers one request at a time over its standard input and output with a length-checked binary protocol:
a kind, a size of at most 1024 pixels, and a path in; a status and premultiplied BGRA pixels out. In FileCat itself,
icons still come from file types only (`SHGFI_USEFILEATTRIBUTES`).

**The helper is restricted where Windows allows it.**

- It runs at low integrity. FileCat duplicates its own token, lowers it to S-1-16-4096, and starts the helper with
  `CreateProcessAsUser`. If Windows refuses, fallback requires a positively queried process token at most medium
  integrity, and the log reports the ordinary fallback. An elevated or unqueryable parent refuses to start the
  helper rather than passing administrator rights to a parser or handler. I312 records the controlled original
  high-integrity fallback and its correction; job limits alone did not prevent an administrator-protected write.
- A job object ends the helper with FileCat and allows it no child processes. It caps the helper's memory at 1 GiB
  and denies it the clipboard, global atoms, and desktop, display, system-parameter, and shutdown changes.
- It starts suspended and joins the job before any of its code runs. It inherits exactly two handles, its pipe ends
  (`PROC_THREAD_ATTRIBUTE_HANDLE_LIST`). Its environment turns off .NET diagnostics endpoints.
- At startup it refuses DLLs from network paths and low-integrity files, prefers System32 DLLs, and disables legacy
  extension points and non-system fonts. It also turns off error dialogs.

**Failures stay in the helper.** Each request has a deadline: 3 s for icons and 8 s for thumbnails. A helper that
misses one, crashes, or breaks the protocol is ended, and the next request starts a new one. The item that caused the
failure is not asked for again that session. Three failures within two minutes turn Shell pictures off until FileCat
restarts; Settings → Privacy shows why.

**What reaches the Shell.** `ShellPreviewPolicy` decides before anything is sent. It refuses shortcut-like types whose
handlers follow paths stored inside them: `.lnk`, `.url`, `.website`, `.library-ms`, `.searchConnector-ms`,
`.search-ms`, `.scf`, `.pif`, `.appref-ms`, and themes (`.theme`, `.themepack`, `.deskthemepack`, `.msstyles`), among
others. It also refuses `desktop.ini`, folders, and cloud placeholders, which would be downloaded. Files on network
and removable drives, including `\\server\share` and `\\?\UNC\` paths, are refused unless the user opts in.

**Where pictures appear.** Quick view (Ctrl+Q) shows the Shell's thumbnail for binary files, with the bytes a key away
(F3). Programs, icon files, and similar types show their own icons in lists. Both can be turned off in Settings →
Privacy. Shell property handlers and context menus are not part of this decision.

## TV-16 evidence

Windows integration tests start the real helper (`ShellHostTests`) and check the following:

- It reports low integrity. It cannot start a program or write a file in the user's temp folder.
- It still produces thumbnails through Windows' own image handler.
- A hang costs only its deadline. A crash is replaced by a new helper that answers normally. Neither item is retried.
- Repeated crashes turn pictures off.
- Excluded types, placeholders, and network paths never start the helper.
- Identical requests share one answer.

A headless UI test shows a thumbnail in quick view through the helper. Copy hooks were already disabled for Shell file
operations (`FOFX_NOCOPYHOOKS`, ADR-03).

## Consequences

- Low integrity means Windows' thumbnail cache cannot be written, so thumbnails are regenerated when needed. FileCat
  keeps its own in-memory cache of recent pictures instead.
- A handler can still open files that its own type refers to. The exclusion list and the local-drives default are the
  mitigation; network contact from other types is a residual risk documented here.
- The helper ships in every build, including the portable ZIP. It needs no privileges beyond the user's own and runs
  with fewer.
