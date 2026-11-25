# I657 — Git badges are wrong in every folder below a repository's top

**Found:** 2026-11-24 by I647's fix ([PR #38](https://github.com/benny-cz/FileCat/pull/38), its "Not verified" list), and reproduced the same day with Git alone; the code is that of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

For a folder in a Git work tree, `GitStatusReader` runs two commands in that folder (`GitStatusReader.cs:115-117`):
- `git status --porcelain=v1 -z --untracked-files=normal --ignore-submodules=all -- .`
- `git ls-files --cached -z -- .`

It then files each record under its first path component (`GitStatusSnapshot.Parse`, `GitStatusReader.cs:31-39`).

`ls-files` names paths from the folder, but porcelain status always names them from the top of the work tree: Git's documentation says the porcelain format does not respect `status.relativePaths`, which FileCat sets to true. Reproduced with Git for Windows 2.54.0, in `repo/sub` with a committed `sub/a.txt` changed and a new `sub/b.txt`:

```
status:   " M sub/a.txt", "?? sub/b.txt"
ls-files: "a.txt"
```

So in `repo/sub`:
- `a.txt` reads "Git clean", because `ls-files` lists it and no status record is filed under it;
- the new `b.txt` gets no badge;
- the change is charged to a child named `sub`, which this folder does not have, or which is another folder if it does.

The repository's top folder, and a repository's badge in its parent, are right.

## Why it matters

The badges are wrong, in a way that looks right, in every folder below a repository's top: changed files read clean and new files are unmarked. This is the same kind of fault as I647, in more places.

## Remedy (proposed) and its cost

Make the status paths relative to the folder before filing them. One way is to strip the folder's prefix, which `git rev-parse --show-prefix` gives. Another is to run status from the top and keep only the folder's records. Both cost one short Git run per folder read or none, change product source, and reach users in rc.3.

## Finite check

In a repository's subfolder, a changed tracked file reads "Git changed", a new file "Git untracked", a clean file "Git clean", and a subfolder holding a change reads changed; the same at the top and in a sibling folder.

## Fix

Commit `3df37718d` changes `src/FileCat.App/Services/GitStatusReader.cs`, on [PR #38](https://github.com/benny-cz/FileCat/pull/38) after I647's fix. The tests are in `tests/FileCat.App.Tests/GitSubfolderBadgeTests.cs`.

**The folder's own records.** Below the top, FileCat now asks Git where the folder lies: `git rev-parse --show-prefix`, one short run, isolated like the status run. At the top, the folder where `.git` was found, nothing more runs.
- `GitStatusSnapshot.Parse` keeps only the status records below that prefix, cuts it off, and files the rest under the folder's children, as it does `ls-files`' paths. A rename's old path is treated the same way.
- Git limits the records to the folder itself (`-- .`), matching letter case as its `core.ignorecase` says. So the prefix is compared ignoring case and cut by its length.
- Git's prefix, not one FileCat works out from its own path, because Git gives it in the spelling of its paths. Reached as `repo\SUB`, Git printed `sub/` and named the change `sub/a.txt`. On Linux and macOS, Git also resolves symbolic links in the folder's path, and FileCat's path does not.
- I647's `check-attr` and `ls-files --debug` already run at the top with the paths as status prints them, so they are unchanged. The "unknown" state is still looked up by the full path.

**A folder inside an untracked folder.** In normal untracked mode, Git names a wholly untracked folder as one record, even from inside it: in `sub/newdir/inner` it prints `?? sub/newdir/inner/`. Such a record names no child, so nothing there had a badge.
- When the records name the folder itself, or one holding it, FileCat runs status again with `--untracked-files=all`. That names each untracked file and leaves ignored ones out.
- It skips `ls-files` then, since nothing below an untracked folder is tracked. Such a read starts as many Git runs as any other folder's.

**Names in other letters.** Git's output is now read as UTF-8; Windows .NET otherwise picks the encoding.
- Measured in this App test host, after FileCat makes the legacy code pages available (the viewer, ZIP archives and shortcut reading do): a redirected child's output was read as UTF-8 with the console at code page 65001, as IBM437 with it at 437, and as Windows-1252 with no console. FileCat itself has no console.
- A name such as `nový.txt` then missed its badge. The folder's own path was misread the same way on both sides, so ASCII names in it still matched.

**Cost.** One more short Git run per folder read below the top. None at the top, and none for a repository listed in its parent. On this workstation, one Git run through Git for Windows' launcher costs about 70–90 ms (I647's measurements).

**Tests.** `GitSubfolderBadgeTests` has 4 cases. Each builds one real temporary repository and reads it in several folders, through `GitStatusReader.ReadAsync`. The fixture's own Git reads no system or global settings.

| Test | What it shows |
|---|---|
| `A_subfolder_shows_its_own_changes` | Git's own status in `sub` names the paths from the top. In `sub`, a changed file reads changed, a clean one clean, a new one untracked, a folder holding a change changed and an untracked folder untracked. A child named `sub`, to which rc.2 charged every change there, reads clean |
| `The_top_and_a_sibling_folder_show_their_own_states` | The top, which was right before and still is, and the sibling `other` |
| `A_folder_with_spaces_and_non_ASCII_names_shows_its_own_changes` | In `sp ace/ü`, after the legacy code pages are made available and with the console at code page 437 during the read: `x.txt` changed, `nový.txt` untracked, `čistý.txt` clean |
| `A_folder_inside_an_untracked_folder_shows_its_files_untracked` | In `sub/newdir` and `sub/newdir/inner`: files and folders untracked, an ignored file without a badge |

The class runs in the serial "Git environment" collection, because one test changes the console's code page for a while.

**Results.** Run on this workstation (Windows 11, Git for Windows 2.54.0), at medium integrity through Explorer, with `TEMP=%LOCALAPPDATA%\Temp`.
- **Before:** with the reader at the branch head before this fix (`cd7f0307b`), all 4 cases fail. In `sub`, `a.txt` read clean; in `other`, `o.txt` read clean; in `sp ace/ü`, `x.txt` read clean; in `sub/newdir`, `inner` had no badge.
- **After:** 4/4. With every `Git*` class, `ElevatedSecurityBoundaryTests` and `BrowsingProcessTests`: 147 cases, 140 passed, 0 failed, 7 declared platform or opt-in skips.
- **Mutations**, each caught:
  - records filed without cutting the prefix: all 4 fail;
  - no second status for an untracked folder: 1 fails;
  - Git's output read without the UTF-8 setting: the non-ASCII case fails, `nový.txt` without a badge.
- **CI:** none for this commit. The coordinator runs CI on the integration branch.

**Residuals.**
- Inside an untracked folder whose untracked files exceed the output budget (4,000,000 characters of status), there are no badges, as for any folder past the budget.
- An untracked folder holding only ignored files is not named by Git. Its contents get no badge, which is right: they are ignored.
- `status.relativePaths=true` stays on FileCat's command line. The porcelain format ignores it, so it changes nothing.
- Not tried on Linux or macOS here; CI runs these tests there. Symbolic links inside a work tree on Linux or macOS were not tried.

**What the next installed candidate must check.**
1. In a repository's subfolder: a changed tracked file reads "Git changed", a new file "Git untracked", a clean file "Git clean", and a folder holding a change "Git changed"; the same at the top and in a sibling folder.
2. A child folder named like the folder it is in reads its own state.
3. Inside a new, untracked folder: its files read "Git untracked".
4. In a folder with non-ASCII letters in its path, after opening a file in FileCat's viewer: a changed and a new file with such a name read "Git changed" and "Git untracked".
