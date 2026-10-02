# E-V12-C1 — a counted folder size lands only on that folder

V12 passes when, among other things, "results do not land on replacement resources" and counts state lower bounds and
uncounted folders accurately. Reading how a folder's size count is applied, for that point, found I85.

## What was wrong (I85)

Counting a folder's size (Space, or Count in the status line) runs in the background and, when it ends, puts the size on
the panel's row of that name, while the panel still shows the same parent folder. A folder deleted and made again, or
replaced (a build's output, an archive extracted again, a sync client), under the same name while it was counted is
another folder; its row took the first one's size, flagged as counted. The folder's time recorded at the start (I32)
could not tell: the listing may show a time from before the folder finished being written (I32's own case), and Windows
may carry a deleted name's creation time over to the next item of that name.

## The fix (`1ec9d13`)

The count reads the folder's own identity from the file system (FileCat's `GetFileIdentity`: volume and file ID on
Windows, device and inode elsewhere) as it begins and as it ends, both off the window's thread. When they differ, the
row shows no size and FileCat says the folder was replaced while it was counted, so that it can be counted again. Where a
file system gives no identity, it behaves as before.

## How it was checked

| Check | Result |
|---|---|
| `FolderCountIdentityTests` (a test platform gives the folder another identity after the count began, as a replacement would) | no size on the row, and "was replaced while its size was counted"; with the same identity, its size (1,234 bytes). Without the check the first fails: "the successor shows 1234 bytes counted for the folder before it" |
| FileCat's own Windows identity of a folder deleted and made again at once under the same name (NTFS) | differs: the same record, its sequence number moved on (`…72…` then `…73…`) |
| App suite | 218, 0 failed |

## Watchers under churn (`4a156b5`)

`FolderChurnTests`: a shown folder of 50 files, ten marked and the cursor on another, takes 12,000 changes from another
thread as fast as the disk takes them (6,000 files made, 2,000 renamed, 2,000 deleted, 2,000 rewritten). Once settled
the panel shows exactly what the disk holds (4,050 items, 0.6 to 0.7 s after the churn ended, in three runs), and the
marks and the cursor on the untouched files are where they were. Whether the system's change buffer overflowed is not
observed; the panel was right either way. (Since E-V12-W1: overflows are counted, none in this churn, and the panel
matches the disk 0.0 s after it, I87's fix rereading during the churn.)

## Partial sizes labelled

Read in the code (`Formatters`): a folder's size is drawn bare only when its count finished; a size from a count still
running or cancelled is drawn with "…" after it, and a folder never counted as `<DIR>`. A cancelled count leaves its
partial size on a marked folder, so labelled, and clears it from an unmarked one.

## Not covered here

V12's other parts: million-entry and long-name listings, slow parsers, rapidly changing viewports, many tabs,
disconnected devices, expensive sorting asked for and cancelled, visible-row
verification beside a copy or search, and many folders counted and the count cancelled by a test. Navigating away,
closing the tab and moving it to the other panel while counting: E-V12-C2 (I88).
