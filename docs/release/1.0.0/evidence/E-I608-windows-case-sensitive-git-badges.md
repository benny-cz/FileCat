# I608 — Git badges in a case sensitive Windows directory

**Status:** Remediated preliminarily at product `1e27ec169ef5e8b22f9ee61e4386ca8e9de15b28`; frozen installed 1.0.0 candidate and latest-source CI remain open. **Severity:** Medium UX and expert-workflow misattribution. **Owner:** V17/V23/GATE-CI-FINAL. Public preview.5 predates this correction.

## Exact observation

The accepted Git badge journey `FCUX-GIT-001` covered clean-to-changed refresh on ordinary Windows and macOS repositories. Cycle 21 separately proved that FileCat can list and view two names that differ only in case in an NTFS directory whose per-directory case-sensitive flag is set. Neither result covered the intersection: whether Git badges keep those names separate.

In the disposable Win11 VM, the exact `68d8517c` App DLL SHA-256 was `9BFBC75030ED20D5CBB453F3795F564C9F81DE56E74B2C4EDB438A371266D4C0`. The owned repository directory's case-sensitive flag was enabled and independently queried with `fsutil`. Git for Windows 2.49.0 tracked both `PROOF.txt` and `proof.txt`, then `git status --porcelain=v1` reported only ` M proof.txt`. Their exact SHA-256 values were `8E22A2CD844F109242E5ED8738A0A879BDA0792DBEBE8BEE5A7F41F4FC9BD856` and `386C2FDB55C9DE6CD2D918F368F68FC00569E3356739C0C577B789C1BCD2D072`. In the normal desktop-token FileCat instance, process-scoped UI Automation found both rows, but both accessible HelpText values said **Git changed**. The untouched upper-case file was misattributed.

`GitStatusSnapshot.Parse` used `OrdinalIgnoreCase` for every Windows directory. The badge reader's dictionary merged the two Git paths before the list asked for either row. No file bytes were changed by FileCat.

## Correction and focused checks

The Git reader now asks the existing handle-based `WindowsCaseSensitivity.NamesIn(folder)` rule for the displayed directory and for child-repository names. The snapshot accepts that comparer. A case-sensitive directory keeps exact names separate; an ordinary case-insensitive Windows directory retains its prior case-insensitive lookup. If the OS flag cannot be read, the existing rule uses exact names so distinct files are not merged.

The focused App regression passed **1/1** and its Git status class passed **14 passes / one existing external-share skip** on the local Windows workstation outside the process-restricted test sandbox. A serial Release App build passed with **0 warnings and 0 errors**. The first corrected App DLL SHA-256 `065F8FDAAFB3D15F7337F7125F423B46DCA45C6680A9948CA3302D648B58D41F` produced **Git clean** on `PROOF.txt` and **Git changed** on `proof.txt` in a fresh normal-token VM instance. The API was then tightened to require an explicit name comparer at every snapshot call, and the same Git class passed 14/one skip again. The final source-build App DLL SHA-256 `46D15025F33DA7E656D95546A2E022A1C553D3B85424AA2226F02987C79EF0D6` passed a second native VM fixture with the same case-sensitive flag, exact Git status and distinct **Git clean/Git changed** accessible rows. Both fixtures retained the upper and lower file hashes, kept the target empty, and were removed by path/hash-guarded cleanup after their isolated process stopped.

Local ignored receipts: `.tmp/i602-vm/cycle26-gitcase-elevated-result.txt`, `cycle26-gitcase-init-result.txt`, `cycle26-gitcase-ui.txt` (before), `cycle26-gitcase-ui-fixed.txt` (first correction), `cycle26-gitcase-cleanup-result.txt`, and `cycle26-gitcase-final-{init,ui,cleanup}-result.txt` (final source build). No global Git safe-directory or certificate exception was added.

## Finite candidate acceptance and stopping rule

On the frozen installed Windows 1.0.0 candidate, create an owned case-sensitive NTFS repository with two tracked names differing only by case. Modify just one and verify Git's exact-path status, the two FileCat rows, visible badges and accessible descriptions: the untouched name must remain clean and the changed name must say changed. On an ordinary case-insensitive NTFS directory, verify one tracked file still receives its badge after a case-only filesystem spelling change. Check source and target hashes and remove only the owned fixtures. Require producer, Windows x64, Windows ARM64, Ubuntu and macOS CI at the exact product source, with skips reconciled. Stop at these two Windows directory modes plus the required CI; do not reopen the earlier listing/viewer or general Git badge matrix without a new consequential result.
