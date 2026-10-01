# E-V09-T3 — a recovery session on macOS, without administrator rights (I09)

Preliminary V09 evidence on the owner's Mac, the counterpart of E-V09-T1 (Windows) and E-V09-T2 (Linux), with what an
ordinary user may do there: the same gated session (`RecoveryWriteTraceTests.A_recovery_session_writes_only_where_it_should`,
`FILECAT_V09_UNIX_DEVICE`), on a disk image this user made and attached, so that its device is the user's own and
FileCat reads it directly (no authopen). Without `fs_usage` or DTrace (both need root, which this campaign was not given
on the owner's machine), this shows what the source's bytes and the recovered file show, not every write the processes
made.

- **Machine:** the owner's MacBook Pro M1, macOS 26.6.2 (E-ENV-05); its other runs (fuzz queue v8) going meanwhile.
- **Build:** `134db5e` (App tests, Debug).
- **Script:** `artifacts/vm/mac-v09.sh`, with `fat-delete.py`.

| Case | What | Result |
|---|---|---|
| M1 — a disk image the user attached | A 64 MiB raw image (`mkfile`), attached unmounted (`hdiutil attach -nomount`, raw disk image class), formatted FAT32 by `newfs_msdos` (512-byte clusters), mounted, four files written (hashes recorded), unmounted and detached; three deleted by editing the image as Windows deletes (entry marked, chain freed, size and start kept; I66); attached again unmounted as `/dev/disk5`, `brw-r----- benny staff`. FileCat (its files in a folder of their own, `--data`) scanned `/dev/rdisk5` as macOS lists it, read a deleted 3 MiB file as the viewer reads it and recovered it; 30 s open; closed | The question said FileCat reads it with the user's own rights and changes nothing. **The image's SHA-256 before and after: `b747cf734046ceda96a5eb4f382b28463758956c9eb65838045aafb96c0b055d`**. **The recovered file's SHA-256 equals the deleted original's, `0b522f3fca4a1ad6bbbc99220eb366b4266038b3dfc1bbda578e0c44e869d880`** (its first letter shown as `_`, as FAT loses it). One device open |

First attempt: the session was given `/dev/disk5`, which FileCat does not list (macOS disks are listed by their raw
devices, `/dev/rdiskN`); the run with `/dev/rdisk5` is the one above.

Files (`artifacts/release-evidence/i09/v09-mac/`): `m1-out.txt` `cd78e4fd787d92e2543fcb4753501f6b5a04ef2183fcb19306ba791bba232003`,
`m1-test.txt` `20be8eaf35922733398c020bf46d65a1c75ef85a3d46a185c66327b6dd6986ab`.

**Not covered here:** every write of FileCat's processes (`fs_usage`, with the owner's administrator rights); a device
only an administrator may read (authopen, its prompt and its refusal); a mounted source; device removal.
