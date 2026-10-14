# E-I495 — what changes on a real USB source during a FileCat recovery scan

V09/I09/I106/I110/ENV-07, 2026-10-14/15. **The historical whole-device hash drift is identified and attributed: Windows' own NTFS metadata, not FileCat.** The owner reconfirmed disposable USB `G:` and authorized destructive use; this slice wrote nothing to the source. No candidate, publication or human GO follows.

## Why this run exists

[G6](E-V09-G6-incomplete-usb-source-trace.md) hashed the whole USB device before and after a FileCat recovery case and the two hashes differed, with no before image retained, so the delta could be neither located nor attributed. That is the hold on physical-source validation. This run keeps **per-chunk hashes and both full images**, so any difference is localized to 4 MiB and can be read back.

## The source

Disk 5, USB, serial `2F2000129618`, `7,796,162,560` bytes, one NTFS partition at offset `1,048,576` of `7,795,113,984` bytes, volume `{bd052877-2d83-11f1-a457-18c04da6742d}`, label FCTEST, not boot and not system. Identity was rechecked immediately before and after every image, and the evidence root is on disk 2 (E:), never the source disk.

## Run 1 — the volume mounted read/write, as a user would have it

| Phase | Result |
|---|---|
| Before image | `7,796,162,560` bytes in `1,859` chunks, device SHA-256 `13fa90bd268a90e426281a45cb1dcd6ad9be5490a9775b3b090e2d95f3aa8989`, 359.4 s |
| FileCat read-only recovery tests | The quick scan and the free-space scan both ran through the helper's read protocol; each found **326 deleted files (71 MiB): 126 Overwritten, 1 Partial, 199 Recoverable**. The direct and helper paths returned identical bytes and identical deleted-entry counts |
| After image | `7,796,162,560` bytes in `1,859` chunks, device SHA-256 `6cc2621f8a63a1a9dc5726d0e69fbcd59587f8019e0938dbe23abebaed64fde8`, 359.8 s |

The device hash **changed**, reproducing G6's observation. This time it is located: **4 of 1,859 chunks differ**, and inside them **64 of 15,224 sectors**, that is **32,768 bytes of 7.8 GB**.

| Chunk | Device offset | Volume-relative | Changed sectors | Signatures of the changed sectors | What lives there |
|---|---|---|---|---|---|
| 0 | 0 | — | 8 of 8,192 | `INDX` and unsignatured sectors | partition table, NTFS boot sector, `$MFTMirr` (LCN 2 → 1,056,768) |
| 764 | 3,204,448,256 | 3,203,399,680 | 32 of 8,192 | `RSTR` ×2, `RCRD` ×2, others | the `$LogFile` journal, immediately before the MFT zone |
| 765 | 3,208,642,560 | 3,207,593,984 | 8 of 8,192 | `RCRD` | `$LogFile`; a sector of `FF` filler became a written log record |
| 768 | 3,221,225,472 | 3,220,176,896 | 16 of 8,192 | `FILE` ×8 | `$MFT`, whose start is LCN `0xC0000` → device offset 3,222,274,048 |

`RSTR`/`RCRD` are NTFS journal structures, `FILE` an MFT file record, `INDX` a directory index block. Every changed sector is file-system metadata; no user-data region differs. The volume's own layout, from `fsutil fsinfo ntfsinfo G:`, places `$MFT` at LCN `0xC0000`, `$MFTMirr` at LCN 2 and the MFT zone at LCN `0xC0080`–`0xCC880`, which is where three of the four chunks fall.

## Run 2 — the same scan with the disk read-only, so only FileCat could write

`Set-Disk -IsReadOnly $true` on the source, verified by a refused write (`IOException`) while the volume stayed mounted and readable. Windows' own NTFS writes are then impossible, so any difference would have to be FileCat's.

| Phase | Device SHA-256 |
|---|---|
| Before the scan | `a9c5d4bab25cea3488bb1def137c42ee9222713ec67215c9ef58c040f6b09c14` |
| After the read-only recovery tests | `a9c5d4bab25cea3488bb1def137c42ee9222713ec67215c9ef58c040f6b09c14` |
| After a second run, including the whole-disk scan | `a9c5d4bab25cea3488bb1def137c42ee9222713ec67215c9ef58c040f6b09c14` |

**Byte-identical across both scan runs: FileCat changed none of the 7,796,162,560 bytes.** Each hash covers the whole device in 1,859 chunks and the source identity was rechecked before and after each pass. Both scans reported the same content — 326 deleted files, 126 Overwritten, 1 Partial, 199 Recoverable — so the source was read, not merely opened. The disk was returned to read/write afterwards and a write/delete probe confirms it.

## The lost-partition path, on real hardware

A new live control scans the **whole disk** instead of its volume, read-only, through the helper's protocol and directly: `\\.\PhysicalDrive5 (7435 MiB): direct found 1 file systems, the helper's protocol found 1: NTFS at 1048576 (7434 MiB)`. The two paths agree on the file system, its offset and its length, and the partition is found at its own offset rather than at byte zero, which is what makes this the lost-partition case. This ran inside the read-only run above, so it is covered by the byte-identical result.

## Why this cannot be FileCat

Static: every device open in the recovery paths requests read access only — `DeviceReadHost.OpenForReading` asks for `GENERIC_READ` (`0x80000000`), `BlockSource` and the Unix device opens use `FileAccess.Read`, and the seven IOCTLs the recovery code issues are all queries (`IOCTL_DISK_GET_LENGTH_INFO`, `IOCTL_DISK_GET_DRIVE_GEOMETRY_EX`, `IOCTL_STORAGE_GET_DEVICE_NUMBER`/`_EX`, `IOCTL_STORAGE_QUERY_PROPERTY`, `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`). There is no write handle, no lock, no dismount and no flush of the source anywhere in those paths. A handle opened for reading cannot write the journal.

Dynamic: the read-only run above leaves the device byte-identical across two scans.

## A short read at the very end of the volume, and its correction

The direct/helper comparison asked for the last 5,000 bytes of the volume and **both paths returned 904** — equal short reads, which is exactly the acceptance risk [I110](../FILECAT_1_0_RELEASE_ISSUES.md#i110) names. Measured with plain `CreateFile`/`ReadFile`, the volume device serves nothing from its last 4,096 bytes: `IOCTL_DISK_GET_LENGTH_INFO` reports the partition's `7,795,113,984`, while the volume reads end at `7,795,109,888` — exactly the size Windows itself reports for the file system (`1,903,103` clusters × 4,096). The same bytes read normally through `\\.\PhysicalDrive5`, so the NTFS **backup boot sector** is reachable from the whole disk and not through the volume.

FileCat was therefore telling a scan that 4,096 bytes existed which it could never read. `DeviceTopology.ServedLength` now probes the end once at open and reports what the device will actually serve, and both the direct source and the installed helper use it, so the two still agree on the source's size. Three unit controls fix the contract and the full Windows Platform suite passes 442 tests with 35 explicit skips. On the stick, the same comparison now reads the **full 5,000 bytes through both paths** at offset 7,795,104,888 with identical SHA-256, instead of two equal 904-byte reads.

The two remaining live failures in both runs are the signed-program assertions: this stick no longer carries deleted signed programs, so nothing recovered can have a valid signature. That fixture is independent of the write question and of the two corrections above.

## Run 3 — the destructive ground truth, all three file systems

With the attribution settled, the owner's destructive authorization was used for the gate it exists for: `LiveDriveScenarioTests` formats the stick, writes generated files, has Windows delete most of them, scans through the helper's read protocol and compares **every byte FileCat calls recovered** with what was written. **All three cases pass**, 432 s in total.

| File system | Written | Found | Bytes read back | States | Cluster size |
|---|---|---|---|---|---|
| FAT32 | 328 (74 MiB) | 328 | 74 MiB | 327 Recoverable, 1 Overwritten | 4,096 |
| exFAT | 328 (74 MiB) | 328 | 74 MiB | 327 Recoverable, 1 Overwritten | 32,768 |
| NTFS | 328 (71 MiB) | 326 | 71 MiB | 326 Recoverable | 4,096 |

The fixture deliberately includes a file whose data is gone, a fragmented file and an overwritten one; the overwritten case is reported as `Overwritten` on FAT32 and exFAT with its missing range named, and the unrecoverable ones are not offered as recoverable. Each run's written and observed manifests are retained per file system. The stick is left formatted by the last case, as this gate requires; its previous contents remain in the retained `before-image.bin`.

## Retained private evidence

`E:\FileCat\artifacts\release-evidence\i495-usb-source-safety-20261014-v1`, 32 files (14.5 GiB, both full device images kept), self-excluding manifest `sha256.json` SHA-256 `ba10565dfc005e6cb0c52d2537d6594ab88cfd550cb67e3774c91975d52164f6`. It holds both full images, all five per-chunk hash lists (1,859 lines each) with their summaries and before/after source identities, the chunk comparison, the difference identification, the live test output, the scripts used, the three destructive runs' written and observed manifests, and the failing CI log for `925ef29b`. Unlike G6, a future difference on this stick can be located against these chunk hashes and read back from the images.

## What this does not establish

It does not prove that G6's specific historical delta was metadata: G6 retained no before image, so its bytes cannot be recovered — only the mechanism is now demonstrated and expected on any mounted NTFS volume. It is one stick, one file system and one scan shape; multi-disk volumes, other file systems, the actual app consent path, hostile or damaged sources, and an installed candidate remain. The live recovery tests' signed-program assertions did not pass in run 1 because this stick no longer carries deleted signed programs; that fixture is separate from the write question. I09, I106 and I110 remain open.
