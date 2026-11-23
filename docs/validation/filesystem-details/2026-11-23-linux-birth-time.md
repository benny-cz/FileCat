# Created on Linux: the birth time — 23 November 2026

The column audit of 22 November ([column meanings](2026-11-22-column-meanings.md)) left one wrong value open: on Linux the **Created** column did not show when an item was made. This receipt covers what .NET does there, what FileCat does now, and what it costs.

## What .NET does on Linux

A probe built with the .NET 10.0.101 SDK on the Ubuntu 24.04.5 guest (kernel 7.0) created a file, set its modification time to 2030, then called `File.SetCreationTimeUtc` with 1 January 2001, and compared each step with `statx`:

| Step | .NET's creation time | statx birth | statx modification | statx change |
|---|---|---|---|---|
| Created | 23:40:22.2779 | 23:40:22.2779 | 23:40:22.2779 | 23:40:22.2779 |
| Modification time set to 2030 | **23:40:22.2899** | 23:40:22.2779 | 2030-01-01 | 23:40:22.2899 |
| `SetCreationTimeUtc(2001-01-01)` | 2001-01-01 | 23:40:22.2779 | **2001-01-01** | 23:40:22.2909 |

- .NET's creation time on Linux is the older of the modification and status change times. After the modification time was set ahead, it reported the moment that was done as the creation time.
- `SetCreationTimeUtc` does not set a creation time on Linux. It sets the modification time, and the birth time stays.
- tmpfs (`/dev/shm`) gave the same three rows: this kernel keeps birth times there too.

So the Created column showed a made-up time, and Find's creation-time criteria checked that time. FileCat's file copy set the destination's modification time and then its "creation time", which on Linux set the modification time a second time, to the source's older time. A source whose modification time was ahead of its change time (set into the future, or unpacked from an archive made on a machine whose clock ran fast) was copied with its change time as its modification time.

## What FileCat does now

- **Created** on Linux is the birth time `statx` reports (`STATX_BTIME`), for the item itself, not a link's target. It is blank where the file system keeps none. The column header's tooltip says this.
- A list reads birth times only while it shows or sorts by Created: one more `statx` per item, asked by name in the folder opened once (`O_PATH`), with `AT_STATX_DONT_SYNC` and `AT_NO_AUTOMOUNT`. When a list starts to show Created (another column profile, a sort by Created), it reads the folder again quietly and the values fill in. A tab entering a folder with Created shown reads them in that first read.
- Find's creation-time criteria use the birth time on Linux. An item whose file system keeps none is logged as not examined, not counted as a non-match.
- Create file list reads birth times when its line format uses `{Created}`.
- A copy writes no creation time on Linux, and Attributes and times changes no creation time there; the Linux dialog never offered one.

## Cost

The same probe listed a folder of 100,000 empty files on the guest's ext4, seven times after two warm-up passes. The median listing, which reads each item's size, times and attributes as FileCat's does, took 1,768 ms; with one `statx` per item for its birth time it took 2,858 ms (+62%). System calls are expensive in this guest. That is why only a list that shows or sorts by Created pays for them.

## Tests

- `LinuxBirthTimeTests` (Core):
  - A Linux listing gives an item its birth time when the list asks for it. The item's modification time is set ahead a second after it is made, so .NET's value would be a second late. Without the request the item has no creation time.
  - A copy keeps a modification time set ahead.
  - Setting a creation time leaves the modification time alone.
  - Find matches a file made now but dated 2001 by creation-time criteria. On Linux .NET called that file created in 2001.
- `CreatedColumnTests` (App): a tab asks for birth times on Linux only while its profile shows Created or it sorts by Created, and the panel's value then equals what the folder lists when asked.

## Test runs

The Mac and Ubuntu runs built the Core and App test projects from a source archive of this work with each machine's .NET 10.0.101 SDK. The App classes were the column, Created, header help, owner, quiet refresh, folder watch and navigation tests.

| Platform | Core | App |
|---|---|---|
| Windows x64 | 24 run, 23 passed, 1 skipped (Linux only) | 263 run in the refresh, metadata, status and column classes, 259 passed, 4 skipped |
| Ubuntu 24.04.5, ext4 | 74 run, 68 passed, 6 skipped (Windows only); the Linux listing test ran | 93 run, 91 passed, 2 skipped |
| macOS 27.2.1 | 74 run, 67 passed, 7 skipped | 93 run, 91 passed, 2 skipped |
