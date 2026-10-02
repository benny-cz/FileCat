# E-V12-C2 — a folder's count ends with its tab's stay in the folder

V12: "navigating away" and closing while folders are counted; results land only where they belong (E-V12-C1). Found
while writing that check: **I88**.

## What was wrong (I88)

A folder's size count (Space on a folder, or Count) runs in the background and posts its progress to the window four
times a second, and its result at the end. Nothing stopped it when its tab moved on or closed:

- **Closing the tab** disposed the tab's listing, and each post from the count then threw `ObjectDisposedException`
  on the window's thread. FileCat's crash guard reports such an exception and keeps going, but ends the process past
  five in three seconds. Measured with a throwaway headless experiment (90,300 folders, counted alone in 6.2 s; the
  tab closed 0.3 s into the count): **24 exceptions in the five seconds after closing**, one every 0.27 s, so
  FileCat would have ended about 1.6 s after the tab closed.
- **Leaving the folder** kept the count going for nothing (its size could not land: the panel no longer shows that
  folder). Meanwhile the tab still counted it as running: the next folder's status line said "counting N folders…"
  of its own unsized marked folders, Count was refused there, and Esc said it stopped counts of the folder left (read
  in the code).

## The fix (`a9a9dcf`)

The count is the tab's until it ends or the tab leaves the folder or closes. Then it is cancelled at once, the tab's
count of running counts drops at once (not when a call held by a slow disk or a dead share returns), and nothing it
posts touches the listing; a closed tab is not asked to update its status line. The tab raises `Closed` as it is
disposed.

## How it was checked

| Check | Old code | Fixed |
|---|---|---|
| `FolderCountLeaveTests`, the count held at its start (the folder's identity read, as a slow disk would hold it), then the folder left | fails: the tab still counting | passes: nothing counting, Count offered for a folder marked in the next folder, and released, the count does not go on (the folder's identity asked once, not twice) |
| The same, the tab closed instead | fails: the tab still counting, then the disposed listing touched | passes |
| The experiment above (not committed) | 24 window-thread exceptions | **0** |
| App suite | — | 222, 0 failed |

## The same kind elsewhere (`34042cc`)

The window's other posted work was read for the same mistake (work finishing after its tab closed). Most of it checks
the tab first. Two paths did not: an SFTP tab closed while connecting navigated once connected, loading its disposed
listing again (a read for nobody); a search's result tab closed while the search ran refreshed on the next results,
which the listing ignored unless it had failed, when it loaded again too. A load of a disposed listing does not throw
(the control below). A disposed listing now ignores `Load`, and a closed tab ignores `Refresh` and the late navigation. `EntryStoreTests.A_disposed_listing_does_not_load_again` fails without
the guard. Core 741, App 222.

## Other views closed while busy (`f2b850b`)

`ClosedWhileBusyTests` closes, each while its background work runs: a comparison of two 48 MiB contents, a viewer and
a hex editor of 24 MiB files, a search through 12,000 files; drives the quick view across three 24 MiB files and
closes it; and closes a tab while its folder is counted. The window's thread is watched until three seconds after the
last of them. **None raised anything** (two runs; App suite 223, 0 failed). With the count code from before I88's fix
the same test saw two `ObjectDisposedException`s (the commit message says six seconds of watching; it is three).

## Esc, and a call that does not return (`84bb7ed`)

Esc (or unmarking the folder, or unmarking everything) stopped a count, but the tab took it off its running counts only
when the count's task returned, which a call held by a slow disk or a dead share can put off; meanwhile the status line
said it was counting and Count was refused. A stopped count now ends in its tab at once, as leaving the folder does.
`FolderCountLeaveTests.Esc_ends_a_count_in_its_tab_at_once_even_while_a_call_is_held` fails without the change (the
tab still counting after Esc). Esc on an analysis cancels it and its banner says how far it got
(`AnalysisLeaveTests.Esc_cancels_an_analysis_and_says_how_far_it_got`). App 229, 0 failed. (The commit message names
Ctrl+- for unmarking everything; that key was not checked.)

## A tab moved to the other panel (`d8349dd`)

Moving a tab between panels moves the same tab, still in its folder, so its count is not stopped: held at its start,
the count goes on after the move and the folder's size (1,000 bytes) lands on its row in the other panel
(`FolderCountLeaveTests.A_tab_moved_to_the_other_panel_while_counting_gets_its_size_there`).

## Analyze folder (I91, `800cd52`)

The same mistake in View → Analyze folder: an analysis went on when its tab left the folder, then labelled ("Sorted by
… with every value computed") and re-sorted the next folder; when the tab closed, it read the released listing and
failed with `ObjectDisposedException`. `AnalysisLeaveTests` (20,000 files, the folder left or the tab closed 0.1 s into
the analysis): both fail on the old code, pass twice on the fix; App 225, 0 failed.

## Not covered here

- Many folders counted and the count cancelled by Esc (partial sizes labelled, E-V12-C1, read in the code).
