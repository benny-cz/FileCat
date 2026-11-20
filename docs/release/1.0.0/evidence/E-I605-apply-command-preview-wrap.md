# I605 — full command preview before execution

**Disposition:** Remediated preliminarily at `cb14d486` on main. The final installed 1.0.0 candidate remains unqualified.

## Consequence and accepted-case comparison

In the disposable Windows 11 Insider VM, the exact `6a22decd` App build opened **Run a command for “header-review.txt”** from the owned source panel. With `C:\Windows\System32\certutil.exe -hashfile {file} SHA256` and shell execution off, the preview showed only the command through `...\source\header-r`; the rest of the file name and `SHA256` were outside the visible row, with no visible horizontal scrollbar. The user could authorize a command without seeing its complete expanded arguments. The screenshot is in the local ignored validation root at `.tmp/i602-vm/cycle21-certutil-preview.png`.

Earlier I583 verified accessible command-search names and one short Mac checksum command. Apply-command planner tests cover token expansion, admission and quoting; the Mac expert handoff covered a different shell. None established that a long expanded Windows command remains readable in the **native preview**. This finding reopens only that presentation boundary. No wrong command execution or data mutation was observed in the old build; execution was canceled there.

## Correction and verification

`MainViewModel.Apply` now renders each planned command with the established wrapping list template and disables horizontal scrolling in that preview. This keeps the complete expanded path and trailing arguments within the dialog's width while preserving the 500-row preview cap and command-planning logic.

- Windows Release App build: 0 warnings, 0 errors. Focused `ApplyCommandDialogTests`: 1 passed, 0 failed, 0 skipped. Sandboxed .NET SDK resolution failed before compilation without a code error; the same build and test passed outside that sandbox.
- A hash-guarded DLL overlay in the isolated VM app used App DLL SHA-256 `1A7844CF360981100BCE971B4952712C7B4540DAE6902767F589937974FEE5B5`. In the same native dialog, the entire command ended visibly with `header-review.txt SHA256` on a second line. Capture: `.tmp/i602-vm/cycle21-i605-preview.png`.
- Clicking **Run** produced one Completed `certutil.exe` item, exit code 0, no issues. The wrapped operation report's bounded standard output displayed SHA-256 `2e4d7623e27757ad2cd4e0c43da7cd8b8943e56b3cf66f3731f0dcfe152a413e` for the exact owned path, matching an independent post-run rehash. The target stayed empty. Captures: `.tmp/i602-vm/cycle21-i605-result.png` and `cycle21-i605-report-wrap.png`; independent receipt: `check-i605-receipt.txt`.
- Exact product [main CI 41317266905](https://github.com/benny-cz/FileCat/actions/runs/41317266905) at `cb14d486` must pass producer, Windows x64, Windows ARM64, Ubuntu and macOS; tag-only package/draft jobs should skip on main. Reconcile new skips against a prior green source run. The run was in progress at the nonblocking sample; no CI acceptance is inferred yet.

## Candidate stopping rule

On the final installed Windows x64 package, run one owned command whose expanded file path and final argument exceed the initial preview width. The complete command must be readable without horizontal clipping before **Run** is enabled; cancel must execute nothing. Then run one read-only command and verify its exact path, exit code, bounded output, unchanged source bytes and empty target. This is a focused candidate UX check, not permission to execute untrusted commands or a substitute for the other release gates.
