# I625–I626 — operation decision emphasis and mask folder default

**Source:** `ff2fce5cee170e133e6daa4af23a74646f9c3467` on main. **Status:** Remediated preliminarily; final installed candidate and exact-source CI remain separate gates. Public preview.9 predates these changes.

## I625 — make a waiting operation's review action conspicuous

The owner reported that **Review decision…** was easy to miss when a background file operation paused for input. Both the compact operation strip and selected operation details used an ordinary button. They now use the existing theme-aware primary button fill, stronger text weight and a larger click target. The button still appears only while `NeedsDecision` is true, and the existing safe review flow and choice defaults remain intact. The accent fill and contrasting foreground are computed for every theme by `ThemeManager`.

## I626 — include folders in mask selection by default

The owner asked for **Include folders** to start checked in the Select/Unselect by mask prompt. The initial checkbox value is now true; the user can turn it off before applying a mask. This changes only the prompt default, not the mask parser or the selected item set before confirmation.

## Verification and limits

- The Release App and App test projects built on Windows with zero warnings and errors. Focused BackgroundDecision and CommandKeyText classes exited successfully.
- The exact source built on macOS ARM64 with zero warnings and errors. Seven focused BackgroundDecision/CommandKeyText cases passed with zero failures or skips. An isolated source app started with two owned sample files; it was closed without starting a transfer, and both file hashes remained unchanged. The remote source app exposed no child accessibility controls, so this session did not establish a native visual screenshot of the highlighted button or a native checked-checkbox observation.
- GitHub main run `41733789839` was in progress when this record was prepared. Its five required platform/producer jobs and expected tag-only skips must be reconciled before source CI is called green.

## Candidate acceptance and stop rule

On the final installed candidate, start an owned copy that waits for a conflict. In both the compact strip and Operations details, **Review decision…** must stand out as the primary action in the active theme; opening it must show the current job's decision, and completing or canceling that decision must remove the button. In Select by mask, **Include folders** must start checked; turning it off must leave folders unselected for a file-only mask. Reconcile the five CI lanes and skips. Stop when these finite checks pass; reopen only for a demonstrated missed decision, incorrect job association, unreadable theme contrast, or a mask that selects folders despite an explicit unchecked choice.
