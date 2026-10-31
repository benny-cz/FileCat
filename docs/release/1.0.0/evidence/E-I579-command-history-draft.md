# E-I579 — command history preserves an unsubmitted draft

31 October 2026. A distinct expert command-line journey found that Up from a partly typed command recalled history, but Down to the live entry set the line to empty. The user lost an unsubmitted command. Accepted Tab completion, Ctrl+Enter insertion and history access did not cover this draft-preservation boundary.

## Reproduction and correction

In a disposable Ubuntu 24.04 VM, an isolated local source build of FileCat was started with its own state and folder. After submitting harmless `cd` once to create a history entry, `Ctrl+E`, typing `echo unfinished`, Up, Down showed an empty line. The [draft before](../../../validation/evidence/exploratory-20261031-command-draft-before.png) and [lost after](../../../validation/evidence/exploratory-20261031-command-draft-lost.png) captures preserve the visible result. No shell command was executed for the draft.

Product commit `a1997784` captures the current live text when history navigation starts and restores it when Down returns to the live entry. It clears the saved draft after return, after command submission and when a history-dialog choice replaces the line. The isolated Ubuntu VM checkout was based at `f78638f0` with the I578 and I579 source files overlaid, not a clean full-head package. Its rebuilt app used the same state and real keyboard sequence and showed `echo unfinished` restored in the [after capture](../../../validation/evidence/exploratory-20261031-command-draft-restored.png). The Windows Avalonia headless `CommandLineCompletionTests` pass **4/4**, including a real Up/Down key case for both a nonempty draft and an initially empty line. No command was executed during that test.

The owned VM fixture's `keep.txt` was 14 bytes with SHA-256 `61563c6f1c99f38ccdc784e3d6744f3f52eab08755506a9efac5366df6505160` before and after. The exact isolated app process and fixture path were verified and removed after capture. The isolated source checkout remains available for later exploratory shots. The local build is not a signed installed candidate.

Capture SHA-256: before `3616c71bd1368efe47faacdd5eb5d66ea91efa4649c5bb078608265c966695e7`; lost `7c15c18c319a47d5f8125c4a3b05a86a2ed85c094a1bb9278c30e5f52671f712`; restored `4f760b564bdc7e5f1e3b77d4c99d6c655511292cc9538579b79ae619c47695cb`.

## Candidate stopping rule

On the exact installed Windows x64 1.0.0 candidate, enter an owned harmless draft in the command line, Up to a known harmless history entry, then Down. The original draft must reappear exactly and remain editable. Repeat with an initially empty live line to ensure it returns empty. No external command is needed. Stop after these affected routes and latest-product five-lane CI with skips reconciled; do not repeat accepted Tab, quoting, or external-tool tests. Stable signing, installed-candidate qualification and final GO remain separate.
