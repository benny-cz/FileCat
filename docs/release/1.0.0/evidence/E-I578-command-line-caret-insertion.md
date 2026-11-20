# E-I578 — command-line filename insertion follows the caret

31 October 2026. The owner resumed the bounded exploratory loop after its second cycle stopped. A distinct expert command-composition shot found that Ctrl+Enter appended the focused file to the end of the command even when the command-line caret was in the middle. This is outside the accepted Tab-completion, shell-quoting and Apply Command scopes. Product correction `294b26d30cdd8ca4110bb36cebf05baa0584a840` inserts at the caret, replaces a command-line selection, leaves the surrounding command intact, and keeps panel-invoked insertion as append. No command is executed by the shortcut.

## Reproduction and correction

On the disposable Ubuntu 24.04 VM, a clean isolated source build selected an owned `report #1.txt` (11 bytes, SHA-256 `624892d5136bcf87617e8a85890ffda2dc31647c50bdc2beab31b88ebcaf2492`). With `echo before after` and the caret between `before ` and `after`, Ctrl+Enter produced `echo before after 'report #1.txt'` on the old source. The [before capture](../../../validation/evidence/exploratory-20261031-commandline-before.png) shows the misplaced token. An isolated rebuild of the correction displayed the quoted filename between `before` and `after`; the [after capture](../../../validation/evidence/exploratory-20261031-commandline-after.png) shows its position. The file hash was unchanged, and no shell command was submitted. The VM screen capture alone is not a byte-for-byte text oracle; the focused test below asserts the exact spaces and caret.

Windows headless `CommandLineCompletionTests` pass **3/3** in Release, including the new real Ctrl+Enter keyboard route at mid-line, selected-text replacement, Ctrl+Shift+Enter full-path insertion and the legacy panel append action. The existing Tab completion cases remain passing. The VM app was a local final-source rebuild, not an installed candidate or a signed artifact.

After the captures, the exact VM app process was stopped and its owned fixture removed. The isolated source checkout was left available for further exploratory shots.

## Candidate stopping rule

On the exact installed 1.0.0 candidate, select an owned filename with shell punctuation and compose a command with text after the caret. Ctrl+Enter must insert one correctly quoted filename before that text and leave the caret immediately after the inserted token and separator; Ctrl+Shift+Enter must insert the quoted full path. Replace a selected placeholder once and confirm the panel shortcut still appends. Do not execute the command. Stop after these affected routes and five-lane latest-product-source CI with skips reconciled; do not repeat accepted generic quoting or external-tool tests. SignPath, installed-candidate input and human GO remain separate.

## Source CI follow-up

First product-source run [41182027345](https://github.com/benny-cz/FileCat/actions/runs/41182027345) at `294b26d3` had a Windows x64 setup failure before any build or test: `actions/setup-dotnet` received a prematurely ended response while downloading SDK `10.0.101`; its fallback URLs returned 404. The missing test-project receipt is downstream of that setup failure, not a product test result. A documentation-and-test descendant [run 41182412868](https://github.com/benny-cz/FileCat/actions/runs/41182412868) at `dbc994f9` contains the same product source and passed the producer, Windows x64, Windows ARM64, Ubuntu and macOS jobs; tag-only package/draft jobs skipped by design on main. The earlier failed run remains failed and is not reclassified. I579 later changed product source, so GATE-CI-FINAL must be checked again at its own identity.
