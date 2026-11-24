# I659 — Git badges lose files whose names are not ASCII, because Git's output was read in a legacy code page

**Found:** 2026-11-24 by I657's fix ([PR #38](https://github.com/benny-cz/FileCat/pull/38)), while testing names with spaces and non-ASCII letters. The code is that of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** open; fixed on PR #38 by `3df37718d`, which reaches users in rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

`GitStatusReader` starts Git and reads its output with the process's default encoding. Git writes paths in UTF-8. The fix's author measured what .NET does in the test host:
- Once the legacy code pages are loaded, child output is read as IBM437 when the console's code page is 437, and as Windows-1252 when there is no console. FileCat's viewer, ZIP archives and shortcut reading all load those code pages.
- FileCat runs without a console, so it reads Git's output as Windows-1252.

So a changed file named `nový.txt` came back as a different name. Its row found no status, and it lost its badge.

## Why it matters

Git badges were wrong for every file and folder whose name is not plain ASCII, as soon as one of those features had loaded the code pages: no badge where Git reports a change. This is the same kind of fault as I647 and I657.

## Fix

`3df37718d` (PR #38) reads Git's output and error streams as UTF-8 without a byte-order mark, and writes its input the same way. `GitSubfolderBadgeTests` includes a changed non-ASCII name, and its cases failed before the commit. The record of I657 has the tests.

## Finite check

On the next candidate, in a repository holding a changed `nový.txt` (with an accented letter), after a ZIP archive or the viewer has been opened in the same FileCat: the row reads "Git changed".
