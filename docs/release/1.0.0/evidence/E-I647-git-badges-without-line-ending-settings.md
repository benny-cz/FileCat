# I647 — Git badges say "changed" for files Git calls clean, because they ignore Git's line-ending settings

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (checklist c23, I608), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)), with Git for Windows 2.54.0 copied into the VM's Program Files. **Status:** open. A fix is proposed for the next candidate, and the owner decides.

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
