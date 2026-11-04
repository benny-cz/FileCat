# I627 — Create file list and command discovery

**Source:** `65243370` (parent `bdcc86f0`). **Status:** implemented and checked in isolated source builds; hosted CI and the installed 1.0.0 candidate remain separate gates.

## User task and correction

Commands → **Create file list…** now lists all entries or the marked/focused selection, with optional recursion and file/folder inclusion. Four built-in formats, field buttons, a live example and named custom rules make the line format discoverable. The result opens in a viewer, copies to the clipboard, or saves as UTF-8 text. Recursion does not follow directory links. The full enumeration must succeed before anything is copied or saved; the work is bounded to 100,000 entries, 64 levels and 8 MiB of UTF-8 output. The command accepts local, archive, FTP/SFTP and MTP folders; no file contents are read.

The command-menu audit also added visible routes for previously search-only user commands. A focused command-registry test now requires a menu path for each static panel/global command, except the menu-focus action and dynamic bookmark slots. [User guide](../../../CREATE_FILE_LIST.md) documents the fields and limits.

## Exact checks

| Surface | Result |
|---|---|
| macOS ARM64 isolated source app | Native Create file list opened from command search. A saved `My remove list` rule using `{Filename}.{Ext} - to remove`, recursive files-only, produced `alpha.txt - to remove` and `nested.bin - to remove` in the viewer. The final compact card measured **700 × 520 DIP** via macOS accessibility; the output choice and safety note ended above the Create/Cancel footer. A native Save sheet wrote exactly `children\nalpha.txt\n` (19 bytes) into the owned fixture, SHA-256 `2c4e45dca68bcc7820b75a6c2a90165a117bf97015ba4e2c9cbb7ea5213e748d`. The final App build had zero warnings/errors; focused command-search tests passed **34/34**. |
| Ubuntu x64 isolated source app | The final 520-DIP dialog shows every control and the footer in a 1214 × 731 window: [native capture](../../../validation/evidence/I627-create-file-list-ubuntu.png). The viewer output listed `children` and `alpha.txt`; clipboard output read `children\nalpha.txt\n`. The final App build had zero warnings/errors; focused Core export tests passed **3/3**. |
| Windows VM | The prior 620-DIP source dialog was natively visible with every control, and its isolated build/focused suites passed before the final compact-only change. The VM was powered off during the 520-DIP follow-up; a headless `vmrun start` reported a host/guest file-access error. No final Windows visual pass is claimed from that attempt. |

The Mac save check found a real edge case in the initial source: a person could leave the Save sheet open past the two-minute export deadline, then receive a timeout without a file. The correction starts the deadline only after a destination is chosen. The final source build's native Mac Save flow produced the exact 19-byte file above. This is the consequential reason for the second Mac save check; unrelated viewers and operations were not reopened.

## Finite candidate acceptance and stop

1. Main CI at the exact product source passes producer, Windows x64, Windows ARM64, Ubuntu and macOS. Reconcile any skip changes against the preceding green source run; tag-only packaging skips on main are expected.
2. In the final installed or packaged 1.0.0 candidate, use one owned folder on each supported desktop to open the Commands menu and create a list. On Windows, confirm the final compact dialog fits the supported viewport. Exercise one selected recursive rule and all three destinations; verify lines/bytes, saved-rule reuse and Cancel without output. A single native representative per platform suffices unless a changed provider or failure supplies a consequential reason to reopen.
3. Check one unreadable child and one linked directory in a disposable fixture: unreadability yields no partial clipboard/file output, and recursion never enters the link. Confirm the 8 MiB/entry/depth bounds in focused Core tests; do not run an unbounded stress scan.

Stop when these checks pass and the exact candidate is recorded. Do not infer signed or stable release readiness from this feature's source checks. The Windows final-layout limitation and candidate/CI gates remain explicit until the stated evidence exists.
