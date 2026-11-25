# I647 — Git badges say "changed" for files Git calls clean, because they ignore Git's line-ending settings

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (checklist c23, I608), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)), with Git for Windows 2.54.0 copied into the VM's Program Files. **Status:** remediated preliminarily for rc.3 on 2026-11-25 ([I647](../FILECAT_1_0_RELEASE_ISSUES.md#i647), merged into main as `a2f983e9a`); first recorded as open. A fix is proposed for the next candidate, and the owner decides.

## Observation

**c23, the ordinary repository.**
1. The repository tracked `File.txt`.
2. Only its spelling on disk changed, to `file.txt`.
3. `git status --porcelain` printed nothing: the repository is clean.
4. FileCat's row said "Git changed".

In the case-sensitive repository of the same run, FileCat agreed with Git. `PROOF.txt` was clean and `proof.txt` changed, matching Git's `M proof.txt`.

**The cause, reproduced with Git alone in the VM.**
- **The settings.** Git for Windows' system settings hold `core.autocrlf=true`, the installer's default.
- **The file.** It was written with CRLF, 6 bytes, and committed through those settings, so its blob has LF, 5 bytes.
- **The trigger.** Only its time was then changed.
- **What Git reported:**

| How Git was run | Result |
|---|---|
| As FileCat runs it: no system or global settings (`GIT_CONFIG_NOSYSTEM=1`, `GIT_CONFIG_GLOBAL=/dev/null`), no index refresh (`GIT_OPTIONAL_LOCKS=0`) | `M a.txt` |
| As Git normally runs | clean; it also refreshed the index |
| As FileCat runs it, after that refresh | clean |

**Why.** I150's hardening keeps automatic badges away from settings that can start programs. It reads no system or global settings (`src/FileCat.App/Services/GitStatusReader.cs:645-663`). That also drops the data-only line-ending settings. Whenever a tracked file's timestamps no longer match Git's index, Git compares its bytes without them. FileCat never refreshes the index, so the false "changed" stays until another Git command does.

## Why it matters

On Windows, `core.autocrlf=true` is the Git for Windows default, and many people also set it in their own global settings. Timestamps change without content changes:
- after a case-only rename;
- after a tool rewrites a file with the same bytes;
- after a copy back.

FileCat then labels clean files changed. That is the same class as I608: an expert status that disagrees with Git.

Repositories that use Git LFS are likely affected the same way. Their clean filter is defined in the system settings and is rightly not run. This was not tried.

## Remedy (proposed) and its cost

Read the data-only settings that `git status` needs for line endings from Git's normal configuration, and pass them to the badge run with `-c`. These are at least `core.autocrlf` and `core.eol`. Reading a setting runs no program. Keep every program-selecting setting out, as I150 requires.

A file whose comparison would need a filter that must not run should not be called "changed". It could get no badge, or an "unknown" one.

This changes product source, so it reaches users only in a candidate after rc.2.

## Fix

Commit `a158f9fd5` changes `src/FileCat.App/Services/GitStatusReader.cs`, with the new state's label in `FileListControl.cs` and no mark for it in `IconProvider.cs`. `docs/CAPABILITIES.md` says what automatic badges now take from the user's settings.

**The read.** Before each automatic status run, FileCat runs `git --no-pager config -z --includes --get-regexp <allowlist>` once, in the same folder. Git reads its ordinary chain: the system file, the global files, the repository's own (checked as before) and what they include. Reading runs no program.
- `GIT_*` variables stay removed for this run too, so `GIT_DIR` and the like cannot redirect it.
- System and global files can name trace destinations. This run sets `GIT_TRACE2`, `GIT_TRACE2_EVENT` and `GIT_TRACE2_PERF` to `0`, so automatic badges write no trace.
- If Git cannot read its configuration, the folder gets no badges; Git itself would fail too. No match means Git's defaults.

**The import.** Each allowlisted setting found goes to every Git run of that read with `-c`. Its value is the last one Git reports, which is the one Git uses there; a repository's own value still wins. The status run still reads no system or global file, so nothing else from them applies (I150 unchanged). FileCat's own `-c` overrides come after the imports.

**The allowlist, and why.** Each was tried with Git for Windows 2.54.0 on this workstation, in a temporary repository whose own configuration lacked it, running `git status` as FileCat runs it.

| Setting | What it changes in `git status` | Seen |
|---|---|---|
| `core.autocrlf` | CRLF in the work tree is converted before contents are compared. Git for Windows' system default is `true`; this is I647's cause | ` M` without it; clean with `true` or `input` |
| `core.eol` | Only the line endings Git writes on checkout; the comparison normalizes CRLF either way | No result changed. Kept because it belongs with `core.autocrlf` and is only data |
| `core.symlinks` | Whether a tracked link checked out as a plain file reads type-changed | ` T` with `true`, clean with `false` |
| `core.ignorecase` | Whether `file.txt` on disk is the tracked `File.txt` or an untracked file | `??` with `false`, clean with `true` |
| `core.filemode` | Whether a different executable bit reads changed | ` M` with `true`, clean with `false` |
| `core.trustctime` | Whether a changed ctime (the creation time on Windows) makes Git compare contents | ` M` with `true`, clean with `false`, for a filtered file |
| `core.checkStat` | With `minimal`, fewer recorded times and identities make Git compare contents | ` M` by default, clean with `minimal`, for a filtered file |
| `core.precomposeUnicode` | macOS: names are compared in composed Unicode | Not tried: macOS only |
| `core.longpaths` | Git for Windows: without it, a changed file whose path exceeds 260 characters is not reported, and its folder reads clean | Not reported without it; ` M` with `true` |

`core.trustctime` and `core.checkStat` decide only whether Git compares contents. They change a result only where FileCat cannot repeat Git's comparison, for filtered files.

**Never imported.**
- Settings that name a program or a path: `filter.*`, `diff.*.textconv`, `diff.external`, `core.fsmonitor`, `core.hooksPath`, `core.attributesFile`, `core.excludesFile`, `core.worktree`, `include.*` and `includeIf.*`, `credential.*`, `core.pager` and `pager.*`, `core.editor`, `core.sshCommand`, `core.askPass`, `alias.*` and `trace2.*`.
- Settings with no effect on the result: `core.fscache`, `core.preloadIndex`, `core.untrackedCache`, `index.*` and `core.bigFileThreshold`. `core.safecrlf` and `core.checkRoundtripEncoding` act only when Git writes objects, and `core.ignoreStat` only when it updates the index.
- Settings FileCat's command line fixes: `status.showUntrackedFiles`, `status.relativePaths`, submodules (`--ignore-submodules=all`) and `core.quotePath` (`-z`).
- Rename detection (`status.renames`, `diff.renames`, `*.renameLimit`). It decides only whether a staged rename's new name reads added or changed, never whether a file is changed.
- `safe.directory`. It decides who may own a repository, not how files compare. A repository owned by another account still gets no badges.

**Filters that must not run: "unknown".**
- **What shows.** A file that Git called changed only by comparing it without such a filter gets no mark. Its tip and accessible description say "Git state unknown". In a folder, unknown ranks above clean and below every decided change, so the folder is never called clean for it and hides no real change.
- **How it is found.** The same read notes which clean filters the configuration defines: `filter.<driver>.clean` or `.process` with a command. Their commands are never used. When `git status` reports work-tree changes (` M`) and such filters exist, one `git check-attr -z --stdin filter` run asks which of those paths select one. It is isolated like the status run.
- **What still reads changed.** For the filtered paths, one `git --literal-pathspecs ls-files --debug -z` run reads the size Git recorded. A different, non-zero size proves the change, as it does for Git, which then compares no contents. The rest read unknown.
- **Bounds.** With more than 10,000 such paths (or 1,000,000 characters), all of them read unknown without asking. With more than 256 filtered paths (or 16,000 characters), there is no size check, so they read unknown.
- **Cost.** The configuration read is one more Git run per folder read, and per listed child repository. `check-attr` runs only when status reports ` M` and filters are defined; Git for Windows' system file always defines LFS's. `ls-files --debug` runs only for filtered changes. Measured on this workstation: the median of 20 reads of a 50-file repository, three rounds each, with rc.2's and the fixed App DLL.

| Repository state | rc.2 | Fixed |
|---|---|---|
| Clean | 192–244 ms | 271–295 ms |
| One file changed | 181–223 ms | 319–442 ms |

**Tests.** `tests/FileCat.App.Tests/GitComparisonSettingsTests.cs` holds 8 methods and 10 cases; all but the last run real Git in temporary repositories.
- Each of those writes its settings to the global file of its own `HOME`, where FileCat's read finds them on every runner. A global value overrides whatever the machine's system file holds.
- Each also runs Git directly, as FileCat ran it until now and as Git ordinarily runs it. So each fixture is shown to reproduce its case, and each program it sets up is shown to run under ordinary Git.
- Automatic reads are optional past their deadlines (8 s for a folder, 5 s for each repository listed in its parent). So, as in I405's test, a folder or a repository's badge is read again only while it is absent, twice at most. A present state is asserted at once, right or wrong.

| Test | What it shows |
|---|---|
| `Automatic_badges_compare_line_endings_as_the_users_autocrlf_says`, with `true`, `input` and `false` | I647's case: a CRLF file with an LF blob, only its time changed. It reads clean with `true` and `input` and changed with `false`, as Git says, in its folder and in its repository's badge in the parent. The index is unchanged |
| `A_real_change_still_reads_changed_with_the_users_autocrlf` | The control: a content change of the same size still reads changed |
| `Automatic_badges_follow_the_users_file_mode_setting` | A second setting: `core.filemode=false` from the global file |
| `The_configuration_read_passes_on_only_the_comparison_settings_at_the_value_Git_uses` | Exactly the nine settings, in order: a value-less key, an empty value, a repository's own value and an included file's later value. No program or path setting passes. Filters with a command are noted; smudge-only and withdrawn ones are not |
| `A_change_that_needs_a_filter_automatic_badges_never_run_reads_unknown_not_changed` | An LFS-like required filter defined globally. Touched files and their folder read unknown, and the filter never runs. A size change then reads changed |
| `Filters_and_textconv_in_the_users_settings_still_never_run` | A global clean filter and a textconv command: neither runs |
| `Trace_destinations_in_the_users_settings_receive_nothing_from_automatic_badges` | A global `trace2.eventTarget`: FileCat writes no trace |
| `An_undecided_change_reads_unknown_and_ranks_above_clean_only` | The snapshot's ranking and the label |

**Results.** Run on this workstation (Windows 11, Git for Windows 2.54.0), at medium integrity through Explorer, with `TEMP=%LOCALAPPDATA%\Temp`.
- **The new class:** 10/10, and 10/10 again in five repeats. With the re-read of absent badges added (commit `24bd2c467`), it and `GitEnvironmentTests` passed 16/16, none needing a second read.
- **Every `Git*` class**, with `ElevatedSecurityBoundaryTests` and `BrowsingProcessTests`: 143 cases, 136 passed, 0 failed, 7 declared platform or opt-in skips. A second run of the same source had one failure in an unchanged check with a 250 ms elapsed budget, `GitLazyFetchTests.Explicitly_disabled_promisor_remotes_leave_ordinary_repositories_available("0")`. That class then passed 20/20 three times.
- **The original reader.** The same final test DLL was run with rc.2's App DLL, built here from `1dfc0c12d` (SHA-256 `ca51b0b4bc1d54833d814feaed5de9d226124e3d68db9312ec7c1fac63b23935`). It had 11 failures:
  - five behavioral: `autocrlf` `true` and `input`, file mode, and both filter tests read changed;
  - two in the new class that call new APIs and fail to bind;
  - four existing snapshot tests that fail only to bind, because `GitStatusSnapshot.Parse` gained an optional parameter.
  - `autocrlf=false`, the real-change control and the trace test pass with both readers.
- **Mutations of the fixed reader**, each caught: no size check (1 failure), filters ignored (2), trace not switched off (1), and every setting read passed on, filters included (3; two because the filter ran).
- **CI**: [run 45438010500](https://github.com/benny-cz/FileCat/actions/runs/45438010500) of PR #38 on `24bd2c467` passed 18 of its 19 jobs. They include the App jobs that run the new class on Ubuntu, Windows x64 and Windows ARM64, and the macOS App focus shard. The macOS App remainder shard, which runs the new class there, was still running when this was written. Its per-case results (TRX) were not inspected.

**Residuals.**
- A filtered file that really changed but kept its size reads unknown. FileCat cannot tell without running the filter.
- The read ignores `GIT_CONFIG_GLOBAL`, `GIT_CONFIG_SYSTEM`, `GIT_CONFIG_PARAMETERS` and the like in FileCat's own environment. Someone who moves their global file that way gets Git's default file read instead.
- The read follows the include and includeIf targets of the user's own system and global files, and reads the global file where `HOME` points, as any Git command does. A target on a share is contacted. A repository's own file still cannot include: such repositories get no badges, as since V24.
- User-wide ignore and attribute files stay unused, as in I150's scope. A file that only the user's global excludes ignore reads untracked.
- `core.precomposeUnicode` was not tried here.
- Found while testing, not part of I647, and recorded as [I657](E-I657-git-badges-below-repository-top.md), fixed after this on the same branch: in a folder below a work tree's top, status-based badges are misplaced. `git status --porcelain` names paths from the top of the work tree, and FileCat files each one under its first component. In `repo/sub`, a changed `a.txt` reads clean and a new file has no badge, with rc.2's reader and with this one. The repository's top folder and its badge in the parent are right; this fix's own path handling keeps paths from the top.

**What the next installed candidate must check.**
1. c23 again, in the Windows VM with Git for Windows' system `core.autocrlf=true`. The CRLF file whose time alone changed reads "Git clean", as `git status` says. So does the repository's own folder, listed in its parent.
2. A real change to that file reads "Git changed".
3. A Git LFS repository with a tracked LFS file. Touched only: no mark, and "Git state unknown". Edited to another size: "Git changed". FileCat starts no `git-lfs` process.
4. A process trace of browsing. `git config` runs before each status, and `check-attr` and `ls-files --debug` only as described here. Nothing they start runs a filter.
