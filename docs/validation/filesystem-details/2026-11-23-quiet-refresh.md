# A busy folder's panel — 23 November 2026

The owner found the panel showing `C:\Windows` refreshing all the time: Windows writes logs there every second, and each change made FileCat read the folder again and show it. This receipt covers the fix.

## The probe

On the Windows 11 VMware guest, a hidden script appended a line to a file in `C:\Windows` every 300 ms, as a busy log does. Meanwhile FileCat, started at medium integrity on `C:\Windows` with the Owner, Allocated and Reparse type columns, was captured with `PrintWindow`.

## Before

The release build before the fix was captured every 250 ms for 15 seconds. 33 of the 59 pairs of consecutive frames differed. Most of the differences covered the Owner and Allocated columns, about 25,000 pixels each time: every reread showed "…" in them until their values were read again, while the tab showed ⟳ and the status line "refreshing…".

[Before](receipts/2026-11-23-quiet-refresh/before-columns-flash.png): Owner and Allocated show "…" during a reread.

## What FileCat does now

The fix is in `b80167889` and `6c787f6c8`:

- A folder read again three times within 10 seconds counts as busy. It is then read again at most every 2 seconds, until it has been quiet for 10 seconds. The status line says "changing often, shown every 2 s".
- A reread the folder's own changes start keeps the values read for unchanged items: they are cached by each item's size and time. The tab shows ⟳ only once such a reread takes over a second.
- A value past its field's interval keeps showing while the list reads it again. The inspection report still says it is last known, and so does a value whose reread failed.
- Refresh (Ctrl+R) still shows at once and reads every value again, owner and group included.

## After

The same probe ran with a self-contained release build of `8044a0c34` (its `FileCat.Core.dll` hash on the guest matched the build's, SHA-256 starting `2FBD6365`). It captured 100 frames over 35 seconds. All 99 pairs of consecutive frames were identical, including past the 20-second interval after which Owner and Allocated are read again.

[After](receipts/2026-11-23-quiet-refresh/after-still.png): the values stay, and the status line says "changing often, shown every 2 s".

## Tests

- `ChangeMonitorCadenceTests.A_busy_folder_is_read_again_at_the_churn_interval_until_it_calms` (Core): a file written every 400 ms for 6 seconds, with the busy interval shortened to 1 second. Rereads are at least 0.9 seconds apart once the folder counts as busy. After a quiet spell the next change is read again within the usual quarter second.
- `QuietRefreshTests` (App): a reread the folder starts keeps a value without reading it again and shows no ⟳; Refresh shows ⟳ at once and reads the value again. A value past its interval shows as it was while it is read again.
- `OwnerColumnTests` (App): Refresh retires owner and group before the folder is read, and a read started for the old list cannot publish into the new one.

They passed on Windows x64 at medium integrity, on the Ubuntu 24.04.5 guest and on the Mac (macOS 27.2.1).
