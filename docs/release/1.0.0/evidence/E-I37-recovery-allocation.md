# E-I37 — A damaged size made a recovery scan allocate gigabytes

Release issue I37. Preliminary automated evidence: fuzz measurements on the host and the Linux VM's kernel log, a
development build.

## Discovery (E-I37-D1)

The Ubuntu VM's part of the fuzz campaign (E-I28-C1: all images, rounds 100,000–1,099,999, started 2026-09-30 23:01 on
`98fb594`, NTFS on `bb977d0`) ended without results. Its kernel log (`i37-ubuntu-oom-log.txt`
`b3bca52957e3ee7b1e99a6711d3f052100bfe4a0a6847b5fd8786ff1f1facd0b`) shows why: at 01:54:47 the machine ran out of
memory, and the kernel killed one fuzz process holding **4.4 GiB** (`anon-rss:4406012kB`, a 16–40 MiB image under
test). The runs had been started through VMware guest operations, so they lived in the `open-vm-tools` service's group;
systemd then stopped that whole group — every other fuzz run with it, and VMware Tools itself, which is why guest
operations on that VM failed intermittently afterwards.

## Measurement (E-I37-M1)

The fuzz test now measures what each round allocates on its thread (the scan and the reading of what it offers). 1,500
rounds per image from round 100,000, on the host, before the fix (`i37-alloc-before.txt`
`72ab39eb3a94d13f4d804b08a60fce9a1a88a6fc1f827c41036d25042046e439`) and after it (`i37-alloc-after.txt`
`4a6cfb5e7b1160372d18c7a7e8684c7b2c23ff8e8a89f3d3905560ecd3400333`):

| Image (size) | Most one round allocated, before | After |
|---|---|---|
| FAT12 (4 MiB) | 2 MB | 2 MB |
| FAT16 (24 MiB) | 4 MB | 4 MB |
| FAT32 (40 MiB) | **1,025 MiB** (round 100061; the run stopped there) | 6 MB |
| exFAT (16 MiB) | 42 MB | 42 MB |
| NTFS (16 MiB) | **513 MB** (round 100927) | 1 MB (same outcomes: damaged 30, changed 87, same 1,383) |
| MBR disk, GPT disk (48 MiB) | 0 MB | 0 MB |

## Mechanism (E-I37-M2)

Stacks taken with the heap capped at 512 MiB (the allocation then fails at its site):

- **FAT:** `FatScanner`'s constructor built its in-memory table for every cluster the boot sector declares —
  `new uint[clusterCount + 2]`, with the count from the declared total sectors (capped only at FAT32's 268 million).
  The existing 64 MiB limit applied to the table *read*, sized by a different field, so a boot sector declaring a huge
  volume with a small table passed it: 1 GiB for a 40 MiB image, and as many steps in every pass over the clusters.
- **NTFS:** the `$Bitmap` was read at whatever size its record claims, up to the 512 MiB ceiling, although a volume
  needs one bit per cluster.

## Remediation `02acee6`

- FAT: the cluster range is held to the clusters that lie within the volume after the data area starts (the FAT type
  still follows the declared count, as the specification says); a data area beyond the volume's end is reported as
  damage.
- NTFS: the `$Bitmap` read is held to one bit per cluster of the volume as it exists.
- The fuzz harness fails a round that allocates more than 256 MiB or eight times its image (`FILECAT_FUZZ_ALLOC_MB`
  overrides), and replays FAT32 round 100061 and NTFS round 100927 in every run.

## Revalidation (E-I37-V1)

- The two replayed rounds fail on the previous decoders ("fat32, round 100061 allocated 1025 MiB to scan a 40 MiB
  image"; "ntfs, round 100927 allocated 513 MiB to scan a 16 MiB image") and pass on `02acee6`.
- 1,500 rounds per image pass with the figures above; Core 561 (37 skipped), Remote 91 (22 skipped) and
  Platform.Windows 118 (22 skipped) pass on the host.
- The Ubuntu VM's share of the campaign restarted on `02acee6` (zip `fuzz-02acee6.zip`
  `b1a86a5adcc6dcfa0603587b52c7947e2e5f71db911f879d46bfb96db3eda674`) as user services, outside VMware Tools' group, at
  the lowest priority and with the .NET heap capped at 1 GiB per process, so a runaway round fails as a finding instead
  of taking the machine down (`fuzz-linux-units.sh`).

## Limitations

- The measure is what a round allocates in total, not the peak held at once; it bounds both.
- Rounds before 100,000 and the other machines' ranges ran on builds before the fix; they found no exception, but their
  allocations were not measured (the Windows VM and the Mac kept 160–650 MB per process while sampled).
