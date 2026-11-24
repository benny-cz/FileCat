# I662 — Linux File-system details no longer show a bind mount's source root (a regression of I612)

**Found:** 2026-11-24 by the Ubuntu GUI harness on rc.2's tarball ([`v1.0.0-rc.2`](E-RC2-TAG-20261124.md), `7269ec61`), row a4, and re-checked in three fresh FileCats (17:26, 18:50 and 20:06). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

**Steps:** open `/run/snapd`, select `ns` (findmnt gives its FSROOT as `/snapd/ns`), then File-system details (Ctrl+Alt+Enter), Copy all, and Read full record.

**Expected:** [I612](E-I612-linux-bind-mount-source-root.md)'s check: "Source root /snapd/ns" in the Item section and "Root within source /snapd/ns" in a Volume section, matching findmnt.

**Actual:** the sections are Item, Storage, Sharing, Access, Extra data and Location, and the native statx metadata. There is no Volume section, and no Mounted at, Source root or Root within source line: only "File system tmpfs".

**Cause.** Since `4364d1b37` (2026-11-17, "Add filesystem details and compression execution checkpoint"), `MainViewModel.FileRecord.cs` builds the report with `FileDetailsReader`. Its Linux reader takes only the file-system type from mountinfo. The I612 code in `UnixFileRecords.Read` (lines 82 and 260) has no caller left. Those files are the same at `7269ec61` and in today's tree.

## Why it matters

I612's remediation is undone in rc.2 and on main: a bind mount's source root is again absent from the report that is meant to show it. That is I612's own fault, advanced origin information missing from a read-only report, back again.

## Remedy (proposed)

Carry mountinfo's root field into the Linux reader of `FileDetailsReader`: "Source root" at the selected mount root, and "Root within source" in a Volume section, as I612's code did. Check it against findmnt for a bind mount and for `/`. Then remove `UnixFileRecords.Read`'s unused copy of that code, or route through it.

## Finite check

I612's check, on the next candidate: an owned bind mount (or `/run/snapd/ns` on Ubuntu) shows Source root and Root within source equal to findmnt's FSROOT, and a plain mount of `/` shows `/`.
