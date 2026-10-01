# E-BIN-1 — the Recycle Bin: a place, and FileCat's own read-only view of it

The owner asked (2026-10-01): "Recycle Bin icon is missing in panels. expected behavior is that it opens the Recycle Bin
windows, since only it can work with files inside. unless you are do it as well, in that case try to implement the
parser of deleted files, but then queue a proper validation check and test it properly"; then "place the recycle bin
icon next to Download icon"; then, offered (a) the window only, (b) a read-only view, (c) restoring as well: "do (b),
test it properly"; and "you are allowed to test on my recycle bin".

## What FileCat does (`f9b0c13`, `5a4161b`)

- **The place** beside Downloads, with Windows' stock Recycle Bin icon, empty or full (the fixed drives' bins counted
  by the Shell; removable and network drives are not asked). It opens FileCat's view in the panel; its second entry,
  "Recycle Bin in Windows" (the button's menu and the location menu), opens Windows' own window, where items are
  restored to where they were or removed for good — Explorer started by its full path on `shell:RecycleBinFolder`.
  Where FileCat has no view of the bin, the place opens Windows' window directly; where the system has no such window,
  there is no such place.
- **The view** (`RecycleBinProvider`, read-only): every item this user deleted on the computer's fixed drives, read from
  the bins' own records — the `$I` file Windows keeps beside each `$R` item in `X:\$Recycle.Bin\<SID>` — with the name
  it had, the folder it was deleted from, its size and when it was deleted (its own columns: Name, Deleted from, Size,
  Deleted). A deleted folder opens; a file is viewed with F3 and copied out with F5, the copy keeping the file's own
  time. Each row is an item of its own, so two deleted "notes.txt" from two folders stay two. Deleting, renaming and
  copying into the bin are not offered, and say where that is done.
- **The records** (`RecycleBinRecords`, Core): both formats — Windows Vista to 8.1 (a fixed 260-character path) and
  Windows 10 and later (the path's length first). A record is untrusted input: one that does not hold together (a
  wrong version, a length past its end, no time, a relative path) is refused with a reason and its item is not shown;
  the view then says how many records it could not read.
- **Safety:** nothing outside the bin is reached (a location's parts and an item's name are checked, and the joined path
  must stay inside the bin folder); a link inside a deleted folder is shown and not followed; a deleted file's content
  has no local path, so nothing offers to open or change it where it lies.

## How it was checked

| Check | Result |
|---|---|
| `RecycleBinRecordsTests` (every platform): both formats; 19 ways a record can be wrong; damaged records | all refused with a reason; the fuzz run **found a crash before it shipped** (a version 2 record of 26 or 27 bytes passed the length check and the path length was read past its end; fixed); since then a million damaged records, 568,210 still read, the rest refused, none crashed |
| `RecycleBinProviderTests` (a bin made here as Windows lays it out: two drives, both formats, two deleted files of one name, a folder holding a folder, a record without its item, an item without its record, a damaged record, a junction inside a deleted folder) | 7 tests: the listing (names, original folders, sizes, deletion times, the damaged record said), folders entered and left by their old names, the two "notes.txt" two items with their own bytes, nothing outside the bin reached (**fails with the name check taken out**), links not followed, read-only explanations, copying a file and a folder out through the job engine |
| `RecycleBinPlaceTests` (app, a test platform's Shell) | 3 tests: the place beside Downloads opens the view in the panel, Delete is refused with the reason, a deleted folder is entered, Windows' window is the second entry; without the view the place opens the window and no tab; no place without such a window |
| `RecycleBinLiveTests` (`FILECAT_RECYCLE_BIN_LIVE=1`): the real bin against the Shell's own listing of its Recycle Bin folder, item by item, joined on where each item is kept; only counts printed | **owner's computer: 40 of 40 items agree** — none missing on either side, the folder each was deleted from and the time it was deleted the same (the Shell's time is UTC, unmarked, to the second); **the lent Windows 11 VM (Czech, build 26300): 6 of 6**, with a folder with a folder in it, Czech names, a hidden file and two files of one name from two folders. On both, test items deleted with FileCat's Recycle job were listed under their names, entered, copied back out byte for byte and then removed from the bin — only they |
| The command the place's second entry runs | run in the VM: its Shell then lists an open "Recycle Bin" window |
| The view drawn with the app's own renderer on the owner's bin | as above; the picture showed the owner's file names and was deleted after looking |

Windows platform suite 152, app suite 206, core suite 718: 0 failed.

## Found on the way (I77, `f95e4cd`)

The first picture of the owner's bin showed two leftovers of a FileCat test (`filecat-recycle-test-…`). The bin held 163
records of that test's files (162 found then, one more found later from a run in the user's own temporary folder) and
two of their items: the test recycles a file and undoes it on the computer it runs on, and the undo restores through the
Recycle Bin's own undelete command, which puts the item back and leaves the bin's record of it, where Windows neither
shows nor counts it and nothing removes it. The test now removes its own items from the bin whatever happens; FileCat's
undo now also removes the record of the item it restored (the test fails without it); the 163 records and two items were
removed from the owner's bin, only those.

**Windows' own Restore does the same.** The fix's comment and commit message said Explorer's restore removes the record.
That had not been checked, and it is wrong. Checked afterwards on the lent VM (Windows 11 build 26300, its own bin;
`artifacts/vm/win-restore-exp*.ps1`):

| What restored the file | The record afterwards |
|---|---|
| The undelete command, then the calling thread handling its messages for 15 s | still there |
| The undelete command on a thread that ended at once (as FileCat's restore did) | still there 10 s later |
| The undelete command run through Explorer's own view of the bin | still there 5 s later |
| Explorer's "Restore the selected items" button, the item selected in its window (three runs) | still there, byte for byte unchanged |
| Emptying the bin with Windows' own call (`SHEmptyRecycleBin`) with three such records | all three still there |

Windows' own count of a bin's items ignores such records (`SHQueryRecycleBin` on the owner's C:, holding five of them
and no items: 0), so they never make the bin look full. FileCat's tidying after its own undo therefore goes one step past
Windows; it touches only the record beside the item it restored, once that item has left the bin. Whether Explorer's
Ctrl+Z after a restore uses the record stays unknown: the keystroke did not demonstrably reach the list.

Rechecked after all this, on the owner's computer: FileCat's view 38 items, Windows' 38 (the owner's 36 and the test's
two), none missing on either side, where each was deleted from and when the same; the test's items purged; no record of
a FileCat test left. Six records without items stay in the owner's bin, none provably FileCat's (one on D: from 2023,
three on C: from 2025-02 to 2026-05, two on C: in a temporary folder from FileCat's first night that match neither
the test's names nor its file's size).

## Not covered

- Restoring and emptying in FileCat itself (the owner's (c)); they stay with Windows' window.
- The bins of removable drives (Windows keeps none on a USB stick) and of folders redirected to a network share.
- Other users' bins (not readable to this user) and the bins of Linux and macOS (their trash is a folder FileCat already
  shows; FileCat's own trash support there is unchanged).
