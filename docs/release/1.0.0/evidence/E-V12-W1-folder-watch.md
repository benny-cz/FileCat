# E-V12-W1 — a shown folder under constant change, and the watcher's overflow

V12 asks for rapidly changing folders and "a watcher's overflow observed as such" (E-V12-C1 left the second open: the
churned panel ended right, but whether notifications overflowed was not seen).

## What was wrong (I87)

`ChangeMonitor` turns the system's change notifications for the shown folder into rereads of it, coalescing a burst
into one and promising never to put a reread off more than two seconds past the first change. It re-armed its timer
at every change, and past two seconds the delay was floored at the minimum interval between rereads (300 ms, more for
folders that take long to read), so while changes kept coming more often than that, no reread came at all:

| Workload (`ChangeMonitorCadenceTests`, `ChangeMonitorOverflowTests`) | Rereads asked, before |
|---|---|
| A new file every 50 ms for six seconds | none during; one 0.32 s after the last file |
| 100,000 changes with long names over 30 s (five rounds, four threads) | none during the churn |

A folder a program keeps saving into (frames, build outputs, rotated logs, a sync client at work) stayed as the panel
first showed it until the program paused.

One file growing does not show it on NTFS: written for six seconds in 64 KiB chunks, it raised a notification when it
was made and when it was closed, none in between (observed here, three runs), so no reread was due meanwhile.

## The fix (`3d2bb2e`)

A reread is due a quarter second after the last change but no later than two seconds after the first, and no sooner
than the minimum interval after the previous one (still the throttle for large folders, three times the last listing's
duration, 0.3 to 10 s).

| Workload | Rereads asked, after |
|---|---|
| A new file every 50 ms for six seconds (five runs) | at 2.0, 4.0 and 6.0–6.1 s: every two seconds, and one after the last file |
| The App's churn of a shown folder, 12,000 changes (`FolderChurnTests`) | the panel matched the disk 0.0 s after the churn ended (0.6–0.7 s before the fix, E-V12-C1) |

Negative control: the old debounce restored, the cadence test fails (no reread while files arrive).

## The overflow, observed

The watcher now counts overflows of the system's notification buffer (`Overflows`; the panel's tab exposes it to tests)
and logs the first one per folder, the path hashed unless diagnostic mode is on. Every overflow already led to a full
reread; now it can be seen.

- Unhindered, this machine's watcher (64 KiB buffer) kept up with 100,000 changes of 180-character names in 30 s: no
  overflow in three runs. Held up 2 ms per notification, as a busy machine might hold it, 20,000 changes overflowed it
  four times in each of three runs, and the folder was asked to be read again after the churn.
- `FolderChurnTests` (12,000 changes through the window) reports 0 overflows here.
- CI run 36946911728 (Windows runner): 45 overflows in one round; the test then failed on its own timing (it took the
  churn's end from its wake-up after the threads, late on a busy runner); fixed in `9a03d8b` to use the last change's time.

## Many tabs (`1cf3af5`)

Only each panel's active tab watches its folder (plan §8.2). `ManyTabsTests`: forty tabs over two panels hold two
watches; a background tab whose folder gained a file meanwhile lists it once it is active again, from comparing the
folder's time with the one at its last read (the test fails with that comparison removed). App 227, 0 failed.

## Checks

Core 740, App 220, 0 failed.

## Not covered here

- macOS and Linux: the cadence test runs on CI's Ubuntu and macOS lanes; an overflow there (inotify's queue, FSEvents'
  must-rescan) is not forced.
- A share whose server sends no notifications is polled every three seconds (`FolderPoller`), unchanged.
