# I632 — Starting a file Windows takes for a 16-bit program waited behind a modal system box

**Source:** `94f20963` (main, 2026-11-23), part of the third release sync ([E-REL-SYNC-20261123](E-REL-SYNC-20261123-release-branch.md#third-sync-main-at-8a1e6763-pr-6)). It was first written on an old worktree branch and rebased onto main at the owner's request. **Disposition:** qualify for 1.0.0 (V23/GATE-CI-FINAL); remediated preliminarily at source. This record was written from the commit, its test and the sync's CI; nothing was re-run for it.

## Fault and user consequence

For a file that 64-bit Windows takes for a 16-bit, DOS or unrecognized program, `CreateProcess` shows the modal "Unsupported 16-Bit Application" box (`NtVdm64RaiseInvalid16BitError` calls `MessageBoxW`). `CreateProcess` returns only once someone closes the box. A 128-byte file named `.exe` is enough.

- **FileCat waited behind the box.** A configured tool, or an Apply command naming such a file, held the calling thread until the box was closed. For a tool, that is the UI thread.
- **Every other program start waited too.** `Process.Start` holds one lock for the whole process across `CreateProcess`.
- **Whether the box appears depends on how FileCat was started.** The box appears only in a process that reports critical errors, and a process inherits that error mode from its parent. A program started from a command shell gets the default, which reports them. Explorer and Windows PowerShell 5.1 fail critical errors.

The fault was found when the whole Core suite stalled on the Windows 11 workstation:
- A dozen tests that start child processes ran past ten minutes with an idle processor, yet each passed alone.
- `ToolListFailureTests` starts such a file, and a stack of the stalled host showed the tool launch inside `CreateProcess` with nine tests waiting for the lock.
- The full runs had been started through `cmd.exe`, and the single-test runs through Windows PowerShell 5.1.

## Change

- Tools (`ToolLauncher`) and Apply commands (`ApplyCommand`) now start programs through `ProgramStart.Start`. On Windows, it sets `SEM_FAILCRITICALERRORS` in the calling thread's error mode for that one call and restores the previous mode afterwards.
- `CreateProcess` then fails at once, for example with `ERROR_EXE_MACHINE_TYPE_MISMATCH`, and FileCat reports the start error as before.
- A started program does not inherit a thread's error mode, so tools keep the process's mode. The commit checked this with a child, which kept its parent's process mode.

## Tests

- **`ProgramStartTests.A_file_Windows_cannot_run_is_a_start_error_even_where_critical_errors_are_reported`.**
  - It runs the case in a child test host that reports critical errors, whatever started its parent.
  - It fails if the refusals take long or the child outlives 60 seconds.
  - Without the fix, the child sat on the box on a desktop nobody sees until it was stopped at 60 seconds. With the fix, both refusals took 13 ms.
- **Full Core suite.** Started through `cmd.exe` at medium integrity, it then finished in one run: 12,180 tests, 0 failed, 1,204 skipped, 669 seconds.
- **Hosted CI.** [PR #6's run 45237015331](https://github.com/benny-cz/FileCat/actions/runs/45237015331) passed all 19 jobs that ran on this tree.
- **Native run.** The [native Windows run of the release tree](E-REL-NATIVE-20261123-release-tree.md) passed Core with no failures.

## Limits

- No installed FileCat was started from `cmd.exe` and pointed at such a file.
- Opening a file through its Windows association (`WindowsShellServices.Open`) goes through the Shell (`UseShellExecute`), which shows its own error UI, as Explorer does. It was not tried with such a file.
- Programs FileCat starts for itself, such as workers, helpers and system tools, keep using `Process.Start`. None of them is a file the user named.

## Finite candidate check and stop

1. **Hosted CI.** At the candidate source, the Windows jobs pass `ProgramStartTests`.
2. **Installed check.** On the installed Windows candidate, started from `cmd.exe`, run a configured tool and an Apply command that name a 128-byte `.exe`:
   - Each reports a start error at once.
   - No "Unsupported 16-Bit Application" box appears.
   - The window stays responsive.

Stop when both pass. Reopen only for:
- a change to how FileCat starts user-named programs;
- a demonstrated wait behind a system box.
