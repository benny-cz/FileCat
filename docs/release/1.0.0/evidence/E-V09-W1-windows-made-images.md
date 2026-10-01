# E-V09-W1 — Recovery from disk images that Windows' own file systems made

Preliminary automated evidence for plan V09 (deleted files, erased FAT starts, an independent reader of the raw bytes).
The repository's recovery fixtures are made on Linux (`mkfs.vfat`, `mkfs.exfat`, `ntfs-3g`); these were made by the
drivers of Windows 11 itself. Development builds; raw outputs under `artifacts/release-evidence/recovery-win/`.

## The images

On the lent Windows 11 VM (build 26300), elevated, `win-recovery-images.ps1`
`445b08fb503ceeb96df42d4a6fe8185075eba9cdb881bdbf9a4f5e1822ba17c8` made one fixed 64 MB VHD per file system with
`diskpart`, one partition labelled FIXTURE (quick format), and wrote the fixture's files: the names, sizes and content
of `eng/make-recovery-fixtures.sh` (`keep.txt` 4,000 bytes, `tiny.txt` 60, `docs/report.txt` 10,000,
`docs/Long file name with spaces.txt` 5,000, `docs/Příliš žluťoučký kůň.txt` 3,000, `photos/a.jpg` 70,000,
`photos/b.jpg` 12,345 — every file, the two ".jpg" ones too, holds numbered text lines). After a flush it deleted the
three files in `docs` and `tiny.txt` one by one, and `photos` with its two files (`Remove-Item -Recurse`), flushed and
detached the disk. On exFAT and FAT32 Windows wrote `System Volume Information` (`WPSettings.dat`, `IndexerVolumeGuid`)
by itself. Log `log.txt` `e45c5a18b8bd1e7693db2301d23129181540aa44c511259d30cb693b45528c97`.

| Image | SHA-256 |
|---|---|
| `ntfs.vhd` | `acd62b106a82baa9790bd8e662c3c8afae6e82013c20ef643a46aa03292081e0` |
| `exfat.vhd` | `621c79fd338fe168874bee50b26a90fedbe4596cde1c44386778f8d09b326c82` |
| `fat32.vhd` | `c64e5b619de8a77fe669c4320ad313434d29d4febf3216cfda22456de0a847d9` |

## The check

`RecoveryEngineTests.Deleted_files_on_images_Windows_made_come_back_as_they_were`, run where `FILECAT_RECOVERY_IMAGES`
names the folder of images: each image is scanned; each deleted fixture file is looked up (with FAT's lost first
letter allowed); every byte FileCat calls recovered must equal the fixture's; a file may be missing only where FileCat
says its folder's list of contents is lost; the file that still exists must not be listed.

| File system | Before (`9a0725b` with the new check) | After (`78a48ce`) |
|---|---|---|
| NTFS | 6 of 6 recoverable, byte for byte (`tiny.txt` from its own MFT record) | the same |
| exFAT | the 4 files of `docs` and the root recoverable, byte for byte; `photos` listed with "Its list of contents is gone: the space it used is in use by other data now", its 2 files not listed | the same |
| FAT32 | the same 4 recoverable (2 with FAT's lost first letter, `_eport.txt`, `_iny.txt`); `a.jpg` and `b.jpg` **Uncertain, and FileCat's guess was wrong for both** (I52) | **6 of 6 recoverable**, byte for byte |

Outputs: `scan.txt` `c2ef07420b971e158d5ea24f80fb23a3b2151555bd3967705280fa67b7e0144b` (before),
`scan-after.txt` `681f17502d87f551e1e98f56d00b11151586dec126f3f7745e300611a2a9ee08` (after).

## What the raw bytes say (an independent reading)

Read with a few lines of Python over the images, not with FileCat:

- **exFAT:** the deleted `photos` entry names cluster 9 as its listing. Cluster 9 is allocated now and holds the GUID
  text of `IndexerVolumeGuid`, written by Windows after the deletion, so FileCat's statement holds. The two files' data
  still lies in free clusters 18–35 and 36–39, but no surviving record names it.
- **FAT32:** the three entries in `docs` are marked deleted (first byte `0xE5`). The listing of the deleted `photos`
  (cluster 6, free) still holds `A.JPG` and `B.JPG` **unmarked**, with their whole first cluster numbers, 53 and 190,
  exactly where their data lies. So their deletions never reached the disk: the folder's listing was freed right after
  them, and Windows writes a listing back lazily.

## I52

FileCat treated every entry inside a deleted FAT32 folder as one whose first cluster number Windows had half erased, and
weighed every place the half left allows: here 53 and 65,589 for `a.jpg`. The files hold text and the type's signature
fits neither place, so the blank one ranked first. FileCat stated this as a guess, so it made no false claim; but the
files could have been recovered exactly. Only an entry marked deleted itself can have lost a half (`78a48ce`). The
issue register has the full record.

## Limitations

- One run per file system, 64 MB volumes. FAT32 here has 512-byte clusters and every cluster number is below 65,536.
  So Windows' erasure of the upper half could not show itself. That case is covered by synthetic images
  (`ErasedFatStartTests`) and was found on a real USB stick earlier.
- The ".jpg" files hold text, not JPEG data, so content signatures cannot help place them; real photos would.
- The deletions came within a second of the writes. How much of a deleted folder's listing reaches the disk depends
  on Windows' lazy writer: a slower deletion may leave the entries marked.
- Not covered here: V09's topology and zero-write cases (a VHD whose backing file lies on the source, device paths), and
  a second recovery tool as an oracle.
