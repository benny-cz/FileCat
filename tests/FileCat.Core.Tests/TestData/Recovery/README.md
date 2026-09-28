# Recovery test images

Disposable disk images for the recovery engine (P10), made by `eng/make-recovery-fixtures.sh` with the Linux kernel's
own vfat and exfat drivers and with ntfs-3g, on a GitHub runner (workflow "Recovery fixtures", run manually). They
contain nothing but generated text: every file holds lines of `<name>:<8-digit line number>\n`, so tests regenerate
the expected bytes.

| Image | File system | Scenario |
|---|---|---|
| `fat12.img.gz`, `fat16.img.gz`, `fat32.img.gz` | FAT12/16/32 (mkfs.vfat) | The scenario below |
| `exfat.img.gz` | exFAT (mkfs.exfat, 4 KiB clusters) | The scenario below |
| `ntfs.img.gz` | NTFS (mkntfs, 4 KiB clusters; changed by ntfs-3g) | The scenario below |
| `disk-mbr.img.gz`, `disk-gpt.img.gz` | MBR and GPT disks with a FAT16 and an exFAT partition | One deleted file in each (`first.txt`, 7000 bytes; `second.txt`, 9000 bytes) |

The scenario: `keep.txt` stays; `old/overwritten.txt` (20000 bytes) is deleted, then `fill/zeros.bin` grows with
zeros until the volume is full, taking every free cluster (its entry existed beforehand, so no deleted entry is
reused); finally `frag-a.bin` (40960, written in two pieces around `frag-b.bin` and `frag-c.bin`),
`docs/report.txt` (10000), `docs/Long file name with spaces.txt` (5000), `docs/Příliš žluťoučký kůň.txt` (3000),
`tiny.txt` (60), and the folder `photos/` (`a.jpg` 70000, `b.jpg` 12345) are deleted, with nothing written after them.
