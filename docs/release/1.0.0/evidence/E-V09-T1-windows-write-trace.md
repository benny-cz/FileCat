# E-V09-T1 — recovery sessions under a write trace in the lent Windows 11 VM (I09)

Preliminary V09 evidence for I09's remediation (plan §V09: "observe FileCat plus every child/helper's I/O"; "before/after
full source hashes"). Not final: the sessions run FileCat's own code through the gated
`RecoveryWriteTraceTests.A_recovery_session_writes_only_where_it_should` (headless, as administrator, FileCat reading
the drives itself), not an installed candidate through its helper; Linux and macOS are not covered here.

## Setup

- **Machine:** the lent Windows 11 Insider VM (E-ENV-02), its owner's snapshot `updated #38`, administrator guest
  operations; .NET 10.0.12 as the private copy of E-X01 W7. Fuzz processes ran meanwhile (other process names).
- **Disks** (`v09-setup.txt` `83d806e64ee4e2bd06917a4db71db4e73e53298184a5ac72caf1eb59c65f7bc5`): disk 0 the VM's own
  (C:, NTFS, 200 GB); disk 1 a 256 MB fixed VHDX in `C:\fc-v09` with a FAT32 volume holding four files of recorded
  hashes, three of them then deleted, the volume then dismounted and taken offline (nothing mounts or writes to it);
  disk 2 a 1 GB VHDX (R:, NTFS) for traces and, in case 1b, FileCat's files. The Ubuntu VM's Samba share
  `\\192.168.58.129\fcshare` as another computer's storage.
- **Trace:** Process Monitor 3.95 (Microsoft-signed, from the host). Case 1b captured everything (a 370 MB log, exported
  and cut to FileCat's processes in the VM); cases 2c and 3 captured only FileCat's process names
  (`v09-filecat.pmc`, made with procmon-parser: the test host, FileCat, its Shell helper, its administrator helper,
  msedgewebview2, gpg, git; everything else dropped as it happened) and were read on the host
  (`pml_to_csv.py`, `analyze_v09.py`: every operation that writes, changes sizes, times, names or security, deletes,
  opens for writing, or changes the Registry).
- **Builds:** `1df5a21` (case 1b; VHD placement, its own check in `topology-1df5a21.txt`
  `87bcf4a3994235fb8dba14f04a1a1f93a3973d5f014a5ba7c01448ca14b88b4e`), `1977e8e` (cases 2c, 3).

## Cases

| Case | What | Result |
|---|---|---|
| 1b — safe topology | Whole disk 1 scanned with FileCat's files on R: (`--data R:\fc-data1`, a VHDX whose file lies on disk 0, not disk 1); a deleted 3 MiB file viewed and recovered to `R:\recovered1`; 70 s open; closed as a user does | Scan allowed; disk 1 opened **Generic Read only**, nothing but reads issued to it. **Disk 1's SHA-256 before and after: `fff7f76d6a66b8d280491643f7290c73f5b6d5dcd664b998d853800dd3468181`, both.** FileCat's 223 writing operations: R: (its folders, journal, recovered file, the test's console log) and cache flushes of the test build's own DLLs just unpacked on C: (paging I/O at load) |
| 2c — the system disk | Drive C: (Windows' own) scanned with FileCat's files on the share (`--data \\192.168.58.129\fcshare\…`); a deleted file read as the viewer reads it and recovered to the share; 70 s open; closed | Scan allowed after FileCat said Windows keeps writing to C: and that it holds off the Shell's pictures and gpg. **No file of FileCat's on C:.** Its writes went to the share (settings, journal, diagnostics, recovered file) and the console log on R:. On C:, in the scanning thread: 1,491 non-cached paging writes of **NTFS's own metadata** (`$Mft`, `$BitMap`, `$LogFile`, `$Mft::$BITMAP`, the USN journal) as FileCat read the mounted volume — Windows' pending changes written out sooner (the volume was opened Generic Read; no flush was asked). Registry: key opens through `RegCreateKey` by the network stack (no values set), and handle tags |
| 3 — FileCat's own files on the source | Drive C: with FileCat in its usual places (C:\Users\Admin\AppData) | **Refused before any device access**: the refusal names all four groups of FileCat's folders on that disk and gives the `--data` command (no other disk qualified: R: is a VHDX on C:'s disk). No volume open, no direct read of C: in the trace; FileCat's writes were its usual folders' as it started and closed |

Files (`artifacts/release-evidence/i09/v09-win/`): case 1b `case1b-disk-result.txt`
`c1388172d74a966a3c11b9f4e29cdfe381ba0670c3ff5e67dcfc75a19f235b41`, `case1b-disk-test.txt`
`b1f456902e54154cd8c49cf8afc78449172fef9ec0e3e2911b6c327af1ea5187`, `case1b-disk-filecat.csv`
`bd0c20aea82af9b94e567592235de830114e7b992c44f4c70461828110a96bd1`, `case1b-disk-writes.txt`
`7e85180715c5d69aec4ffec59b2079c85a4e028e17ac28db29e9734afdfce6aa`; case 2c `case2c-system-result.txt`
`b66972dc94c02505bc25313cc1a347a014d06e2a8e82889adf12895af48c9413`, `case2c-system-test.txt`
`100c4625692e357ff0202a394e1c7bb20ff27bed342203144888c3916804a895`, `case2c-system.pml`
`6554531c140bee66948f788aa9f86f464a5f50f1232c6e011a4a5cca5c9f7354`, `case2c-system-writes.txt`
`9eb0a764beeebb231e39493cbb3da3efec416c45fc79f7ec511f7d1b3fee1077`; case 3 `case3-refuse-result.txt`
`7ef061eb54aa8f598708502e482dd7270aaea358079da38c13fbabab9d61b1da`, `case3-refuse-test.txt`
`68f8d0e20a135550f02dedb384553da37a367cfb1774db3c63155aacba0bdd34`, `case3-refuse.pml`
`4700a3ce65e46929b2f04c4883d25c3c1e07e1b87ab6988e95207d70dda10f89`, `case3-refuse-writes.txt`
`094876683c26f75d4873f6ca58833693f78aa8076bc4b6e9b7a04aff59cd4770`.

## What the runs found on the way

- **Case 1 (`1df5a21`'s predecessor, `f241897`):** FileCat refused the scan because its data folder was on a VHDX:
  any VHD destination counted as unknown. Fixed in `1df5a21` (a VHD written into lies on itself and on its file's
  disk); the check `topology-1df5a21.txt` places R: (file on disk 0) apart from disk 1 and on the same disk as C:.
- **Case 2 / 2b:** the share's sign-in failed on a trailing space vmrun added to the last argument (the harness, not
  FileCat), and then the trace stopped when R: filled up; the session itself hung in headless Avalonia's stand-in text
  layout while drawing a recovered file as text (the dump: `PerformTextWrapping → CreateEmptyTextLine` under
  `TextViewer.Render`; a limitation of the headless platform, not of FileCat). The harness now reads the content as the
  viewer does without drawing it (`1977e8e`).
- **Wording (`deaf776`):** the question said "nothing on it is changed"; per case 2c it now says FileCat changes
  nothing, and that Windows may write its own pending changes to a mounted drive meanwhile.

## Not covered yet

The installed helper path and approval refusal; device removal mid-scan; Linux (UDisks2/polkit) and macOS (authopen)
with real devices and their own traces (`strace`/`fs_usage`); the system-disk case on those systems; the final
candidate's packaged FileCat rather than the test host.
